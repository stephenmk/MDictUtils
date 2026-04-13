using System;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Lib;

// Tbh this is the Writer part

public sealed record MDictEntry(string Key, long Pos, string Path, long Size)
{
    public override string ToString()
        => $"Key=\"{Key}\", Pos={Pos}, Size={Size}";
}

internal class OffsetTableEntry
{
    public required byte[] Key { get; init; }
    public required byte[] KeyNull { get; init; }
    public required int KeyLen { get; init; }
    public required long Offset { get; init; }
    // public required byte[] RecordNull { get; set; }
    public required bool IsMdd { get; init; }
    public required long RecordSize { get; init; }
    public required long RecordPos { get; init; }

    // Weird stuff from get_record_null()
    public required string FilePath { get; init; }

    public override string ToString()
    {
        static string BytesToString(ReadOnlySpan<byte> bytes)
            => bytes.IsEmpty ? "null" : Encoding.UTF8.GetString(bytes);

        var sb = new StringBuilder();
        sb.Append("OffsetTableEntry(");
        sb.Append($"KeyLen={KeyLen}, ");
        sb.Append($"Offset={Offset}, ");
        sb.Append($"RecordPos={RecordPos}, ");
        sb.Append($"RecordSize={RecordSize}, ");
        sb.Append($"IsMdd='{IsMdd}', ");
        sb.Append($"Key='{BytesToString(Key)}', ");
        sb.Append($"KeyNull='{BytesToString(KeyNull)}', ");
        // sb.Append($"RecordNull='{BytesToString(RecordNull)}'");
        sb.Append(')');
        return sb.ToString();
    }
}

/// <summary>
/// Abstract base class for <see cref="MdxRecordBlock"/> and <see cref="MdxKeyBlock"/>.
/// </summary>
internal abstract class MdxBlock
{
    private readonly static ArrayPool<byte> _arrayPool = ArrayPool<byte>.Shared;
    protected long _decompSize;
    protected byte[] _compData;
    protected long _compSize;
    protected string _version;

    protected MdxBlock(List<OffsetTableEntry> offsetTable, int compressionType, string version)
    {
        if (compressionType != 2 || version != "2.0")
            throw new NotSupportedException();

        // Console.WriteLine("[Debug] Calling MdxBlock...");

        var decompDataSize = offsetTable.Sum(LenBlockEntry);
        var decompData = _arrayPool.Rent(decompDataSize);

        var maxBlockSize = offsetTable.Max(LenBlockEntry);
        var blockBuffer = maxBlockSize < 256
            ? stackalloc byte[maxBlockSize]
            : new byte[maxBlockSize];

        int totalSize = 0;
        foreach (var entry in offsetTable)
        {
            int blockSize = GetBlockEntry(entry, version, blockBuffer);
            // Console.WriteLine($"[Debug] BlockEntry ({blockEntry.Length} bytes): {BitConverter.ToString(blockEntry)}");
            var source = blockBuffer[..blockSize];
            var destination = decompData.AsSpan(start: totalSize, length: blockSize);
            source.CopyTo(destination);
            totalSize += blockSize;
        }

        // Console.WriteLine("[Debug] Building MdxBlock...");
        _decompSize = totalSize;
        // Console.WriteLine($"[Debug] Decompressed array length (_decompSize): {_decompSize}");
        // Common.PrintPythonStyle(decompArray);

        _compData = MdxCompress(decompData[..totalSize], compressionType);
        _compSize = _compData.Length;
        // Console.WriteLine($"[Debug] Compressed array length (_compSize): {_compSize}");

        _version = version;

        _arrayPool.Return(decompData);
    }

    public ReadOnlySpan<byte> BlockData => _compData;

    public abstract void GetIndexEntry(Span<byte> buffer);
    protected abstract int GetBlockEntry(OffsetTableEntry entry, string version, Span<byte> buffer);
    public abstract int LenBlockEntry(OffsetTableEntry entry);
    public abstract int IndexEntryLength { get; }

    // Called in MdxBlock init
    public static byte[] MdxCompress(ReadOnlySpan<byte> data, int compressionType)
    {
        if (compressionType != 2)
            throw new NotSupportedException("Only compressionType=2 (Zlib) is supported in this version.");

        // Compression type (little-endian)
        Span<byte> lend = stackalloc byte[4];
        Common.ToLittleEndian((uint)compressionType, lend); // <L in Python

        // Adler32 checksum (big-endian)
        uint adler = Common.Adler32(data);
        Span<byte> adlerBytes = stackalloc byte[4];
        Common.ToBigEndian(adler, adlerBytes); // Python uses >L

        // byte[] header = [.. lend, .. adlerBytes];

        // It's possible for compressed data to be larger than the uncompressed.
        // See: https://zlib.net/zlib_tech.html
        // "For the default settings, ... five bytes per 16 KB block (about 0.03%)"
        // So we have to rent a size a little bit larger.
        var buffer = _arrayPool.Rent(data.Length + (data.Length * 5 / 16_000) + 32);

        var size = ZLibCompression.Compress(data, buffer);

        byte[] compressed = [.. lend, .. adlerBytes, .. buffer.AsSpan(..size)];
        _arrayPool.Return(buffer);

        // Common.PrintPythonStyle(data);
        // Common.PrintPythonStyle(lend);
        // Console.WriteLine($"adler: {adler}");
        // Common.PrintPythonStyle(adlerBytes);
        // Console.WriteLine($"header: {BitConverter.ToString(header)}");
        // Common.PrintPythonStyle(final);

        return compressed;
    }
}

internal class MdxRecordBlock(List<OffsetTableEntry> offsetTable, int compressionType, string version)
    : MdxBlock(offsetTable, compressionType, version)
{
    public override int IndexEntryLength => 16;

    public override void GetIndexEntry(Span<byte> buffer)
    {
        // Console.WriteLine("Called GetIndexEntry on MDXRECORDBLOCK");
        // Console.WriteLine($"    compSize {_compSize}; decompsize {_decompSize}");
        if (_version != "2.0")
        {
            throw new NotImplementedException();
        }

        Debug.Assert(buffer.Length == IndexEntryLength);

        // Big-endian 64-bit values
        Common.ToBigEndian((ulong)_compSize, buffer[..8]);
        Common.ToBigEndian((ulong)_decompSize, buffer[8..16]);
    }

    // rg: get_record_null
    // We overwrite "return entry.RecordNull"
    protected override int GetBlockEntry(OffsetTableEntry entry, string version, Span<byte> buffer)
    {
        int size = ReadRecord(entry.FilePath, entry.RecordPos, (int)entry.RecordSize, entry.IsMdd, buffer);
        // entry.RecordNull = buffer[..size].ToArray();
        return size;
    }

    /// <summary>
    /// Helper method: read from file and null-terminate
    /// </summary>
    private static int ReadRecord(string filePath, long pos, int size, bool isMdd, Span<byte> buffer)
    {
        if (size < 1) throw new ArgumentException("Size must be >= 1", nameof(size));

        using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read);

        fs.Seek(pos, SeekOrigin.Begin);
        if (isMdd)
        {
            int totalRead = 0;
            while (true)
            {
                int bytesRead = fs.Read(buffer[totalRead..]);
                totalRead += bytesRead;
                if (bytesRead == 0)
                    break;
            }
            // For MDD, apparently fewer bytes than the expected size might be read?
            return totalRead;
        }
        else
        {
            // For MDX, read size-1 bytes and append null byte
            fs.ReadExactly(buffer[..(size - 1)]);
            buffer[size - 1] = 0; // null-terminate
            return size;
        }

        // Console.WriteLine($"[ReadRecord] Record length: {record.Length}");
    }


    public override int LenBlockEntry(OffsetTableEntry entry)
    {
        return (int)entry.RecordSize; // TODO: fix cast
    }
}

internal class MdxKeyBlock : MdxBlock
{
    private readonly int _numEntries;
    private readonly byte[] _firstKey;
    private readonly byte[] _lastKey;
    private readonly int _firstKeyLen;
    private readonly int _lastKeyLen;

    public override string ToString()
    {
        var _encoding = Encoding.UTF8;
        string firstKeyStr = _encoding.GetString(_firstKey, 0, _firstKeyLen);
        string lastKeyStr = _encoding.GetString(_lastKey, 0, _lastKeyLen);
        return $"NumEntries={_numEntries}, FirstKey='{firstKeyStr}', LastKey='{lastKeyStr}'";
    }

    public MdxKeyBlock(List<OffsetTableEntry> offsetTable, int compressionType, string version)
        : base(offsetTable, compressionType, version)
    {
        _numEntries = offsetTable.Count;
        Debug.Assert(version == "2.0");

        _firstKey = offsetTable[0].KeyNull;
        _lastKey = offsetTable[^1].KeyNull;
        _firstKeyLen = offsetTable[0].KeyLen;
        _lastKeyLen = offsetTable[^1].KeyLen;
    }

    protected override int GetBlockEntry(OffsetTableEntry entry, string version, Span<byte> buffer)
    {
        Debug.Assert(version == "2.0");

        Common.ToBigEndian((ulong)entry.Offset, buffer[..8]);
        entry.KeyNull.CopyTo(buffer[8..]);

        return 8 + entry.KeyNull.Length;
    }

    // Approximate for version 2.0
    public override int LenBlockEntry(OffsetTableEntry entry)
    {
        return 8 + entry.KeyNull.Length;
    }

    public override int IndexEntryLength
        => 8 + 2 + _firstKey.Length + 2 + _lastKey.Length + 8 + 8;

    public override void GetIndexEntry(Span<byte> buffer)
    {
        Debug.Assert(_version == "2.0");
        Debug.Assert(buffer.Length == IndexEntryLength);

        var r = new SpanReader<byte>(buffer);

        Common.ToBigEndian((ulong)_numEntries, r.Read(8));
        Common.ToBigEndian((ushort)_firstKeyLen, r.Read(2));
        _firstKey.CopyTo(r.Read(_firstKey.Length));
        Common.ToBigEndian((ushort)_lastKeyLen, r.Read(2));
        _lastKey.CopyTo(r.Read(_lastKey.Length));
        Common.ToBigEndian((ulong)_compSize, r.Read(8));
        Common.ToBigEndian((ulong)_decompSize, r.Read(8));
    }
}

#pragma warning disable format
public sealed record MDictWriterOptions
(
    string Title           = "",
    string Description     = "",
    int    KeySize         = 32768,
    int    BlockSize       = 65536,
    string Encoding        = "utf8",
    int    CompressionType = 2,
    string Version         = "2.0",
    bool   IsMdd           = false,
    bool   Logging         = true
);
#pragma warning restore format

public sealed class MDictWriter
{
    private readonly IMDictWriterLogger _logger;
    private readonly int _numEntries;
    private readonly string _title;
    private readonly string _description;
    private readonly int _blockSize;
    private readonly int _compressionType;
    private readonly string _version;
    private readonly Encoding _encoding;
    // _python_encoding in the original
    private readonly Encoding _innerEncoding;
    private readonly int _encodingLength;
    private readonly bool _isMdd;

    private List<OffsetTableEntry> _offsetTable = [];
    private List<MdxKeyBlock> _keyBlocks = [];
    private List<MdxRecordBlock> _recordBlocks = [];
    private byte[] _keybIndex = [];
    private long _keybIndexCompSize;
    private long _keybIndexDecompSize;
    private byte[] _recordbIndex = [];
    private long _recordbIndexSize;
    private long _totalRecordLen;

    public MDictWriter(List<MDictEntry> entries, MDictWriterOptions? opt = null)
    {
        opt ??= new();

        _logger = opt.Logging
            ? new MDictWriterLogger()
            : new MDictWriterDummyLogger();

        _numEntries = entries.Count;
        _title = opt.Title;
        _description = opt.Description;
        _blockSize = opt.BlockSize;
        _compressionType = opt.CompressionType;
        _version = opt.Version;
        _isMdd = opt.IsMdd;

        // Set encoding
        var encoding = opt.Encoding.ToLower();
        Debug.Assert(encoding == "utf8");
        if (opt.IsMdd || encoding == "utf16" || encoding == "utf-16")
        {
            _innerEncoding = Encoding.Unicode;
            _encoding = Encoding.Unicode;
            _encodingLength = 2;
        }
        else if (encoding == "utf8" || encoding == "utf-8")
        {
            _innerEncoding = Encoding.UTF8;
            _encoding = Encoding.UTF8;
            _encodingLength = 1;
        }
        else
        {
            throw new ArgumentException("Unknown encoding. Supported: utf8, utf16");
        }

        if (opt.Version != "2.0")
        {
            throw new ArgumentException("Unknown version. Supported: 2.0");
        }

        BuildOffsetTable(entries);
        _logger.LogOffsetTable(_offsetTable, _totalRecordLen);

        _logger.LogBeginBuildingKeyBlocks();
        _blockSize = opt.KeySize;
        BuildKeyBlocks();
        _logger.LogKeyBlocks(_blockSize, _keyBlocks);

        _blockSize = opt.BlockSize;
        _logger.LogBlockSizeReset(_blockSize);

        _logger.LogBeginBuildingKeybIndex();
        BuildKeybIndex();
        _logger.LogKeybIndex(_keybIndexDecompSize, _keybIndexCompSize);

        BuildRecordBlocks();
        _logger.LogRecordBlocks(_recordBlocks);

        BuildRecordbIndex();
        _logger.LogRecordIndex(_recordbIndexSize);

        _logger.LogInitializationComplete();
    }

    private void BuildOffsetTable(List<MDictEntry> entries)
    {
        entries.Sort((a, b) => MDictKeyComparer.Compare(a.Key, b.Key, _isMdd));

        _offsetTable = [];
        long offset = 0;

        foreach (var item in entries)
        {
            // Console.WriteLine($"dict item: {item}");
            var keyEnc = _innerEncoding.GetBytes(item.Key);
            var keyNull = _innerEncoding.GetBytes($"{item.Key}\0");
            var keyLen = keyEnc.Length / _encodingLength;

            // var recordNull = _innerEncoding.GetBytes(item.Path);

            var tableEntry = new OffsetTableEntry
            {
                Key = keyEnc,
                KeyNull = keyNull,
                KeyLen = keyLen,
                // RecordNull = recordNull,
                Offset = offset,
                RecordSize = item.Size,
                RecordPos = item.Pos,
                FilePath = item.Path,
                IsMdd = _isMdd,
            };
            _offsetTable.Add(tableEntry);

            offset += item.Size;
        }

        // pretty print it here
        // {
        //     Console.WriteLine("---- Offset Table ----");
        //
        //     int index = 0;
        //     foreach (var entry in _offsetTable)
        //     {
        //         string key = _encoding.GetString(entry.Key);
        //         string keyNull = _encoding.GetString(entry.KeyNull);
        //         string recordNull = _encoding.GetString(entry.RecordNull);
        //         string valuePreview = _encoding.GetString(entry.RecordNull)
        //             .TrimEnd('\0')
        //             .Replace("\r", "")
        //             .Replace("\n", " ");
        //
        //         valuePreview = $"{valuePreview[..40]}...";
        //
        //         Console.WriteLine(
        //             $"[{index}] " +
        //             $"Key=\"{key}\", " +
        //             $"Offset={entry.Offset}, " +
        //             $"KeyNull=\"{keyNull}\", " +
        //             $"KeyLen={entry.KeyLen}, " +
        //             $"RecordNull={recordNull}, " +
        //             $"RecordPos={entry.RecordPos}, " +
        //             $"RecordSize={entry.RecordSize}, " +
        //             $"Path=\"{valuePreview}\""
        //         );
        //
        //         index++;
        //     }
        //
        //     Console.WriteLine("----------------------");
        // }

        _totalRecordLen = offset;
    }

    private List<T> SplitBlocks<T>(Func<List<OffsetTableEntry>, int, string, T> blockConstructor,
                                   Func<OffsetTableEntry, long> lenFunc) where T : MdxBlock
    {
        var blocks = new List<T>();
        int thisBlockStart = 0;
        long curSize = 0;

        for (int ind = 0; ind <= _offsetTable.Count; ind++)
        {
            var offsetTableEntry = (ind == _offsetTable.Count)
                ? null
                : _offsetTable[ind];

            bool flush = false;

            if (ind == 0)
            {
                flush = false;
            }
            else if (offsetTableEntry == null)
            {
                flush = true;
            }
            else if (curSize + lenFunc(offsetTableEntry) > _blockSize)
            {
                flush = true;
            }

            if (flush)
            {
                var blockEntries = _offsetTable.GetRange(thisBlockStart, ind - thisBlockStart);
                // foreach (var entry in blockEntries)
                // {
                //     Console.WriteLine($"[split flush] {entry}");
                // }
                var block = blockConstructor(blockEntries, _compressionType, _version);
                blocks.Add(block);
                curSize = 0;
                thisBlockStart = ind;
            }

            if (offsetTableEntry != null)
            {
                curSize += lenFunc(offsetTableEntry);
            }
        }

        return blocks;
    }

    private void BuildKeyBlocks()
        => _keyBlocks = SplitBlocks
        (
            static (entries, comp, ver) => new MdxKeyBlock(entries, comp, ver),
            static (entry) => 8 + entry.KeyNull.Length
        );

    private void BuildRecordBlocks()
        => _recordBlocks = SplitBlocks
        (
            static (entries, comp, ver) => new MdxRecordBlock(entries, comp, ver),
            static (entry) => entry.RecordSize
        );

    private void BuildKeybIndex()
    {
        Debug.Assert(_version == "2.0");

        if (_keyBlocks is [])
        {
            _keybIndexDecompSize = 0;
            _keybIndex = [];
            _keybIndexCompSize = 0;
            return;
        }

        var arrayPool = ArrayPool<byte>.Shared;

        int decompDataTotalSize = _keyBlocks.Sum(static b => b.IndexEntryLength);
        var decompData = arrayPool.Rent(decompDataTotalSize);

        int maxBlockSize = _keyBlocks.Max(static b => b.IndexEntryLength);
        var blockBuffer = maxBlockSize < 256
            ? stackalloc byte[maxBlockSize]
            : new byte[maxBlockSize];

        int bytesWritten = 0;
        foreach (var block in _keyBlocks)
        {
            var indexEntry = blockBuffer[..block.IndexEntryLength];
            block.GetIndexEntry(indexEntry);
            _logger.LogIndexEntry(indexEntry);

            var destination = decompData.AsSpan().Slice(bytesWritten, indexEntry.Length);
            indexEntry.CopyTo(destination);
            bytesWritten += indexEntry.Length;
        }

        Debug.Assert(bytesWritten == decompDataTotalSize);

        _keybIndexDecompSize = bytesWritten;
        _keybIndex = MdxBlock.MdxCompress(decompData.AsSpan(..bytesWritten), _compressionType);
        _keybIndexCompSize = _keybIndex.Length;

        arrayPool.Return(decompData);
    }

    private void BuildRecordbIndex()
    {
        if (_recordBlocks is [])
        {
            _recordbIndex = [];
            _recordbIndexSize = 0;
            return;
        }

        int indexSize = _recordBlocks.Sum(static b => b.IndexEntryLength);
        var indexData = new byte[indexSize];

        int maxBlockSize = _keyBlocks.Max(static b => b.IndexEntryLength);
        var blockBuffer = maxBlockSize < 256
            ? stackalloc byte[maxBlockSize]
            : new byte[maxBlockSize];

        int bytesWritten = 0;
        foreach (var block in _recordBlocks)
        {
            var indexEntry = blockBuffer[..block.IndexEntryLength];
            block.GetIndexEntry(indexEntry);

            var destination = indexData.AsSpan().Slice(bytesWritten, indexEntry.Length);
            indexEntry.CopyTo(destination);
            bytesWritten += indexEntry.Length;
        }

        Debug.Assert(bytesWritten == indexData.Length);

        _recordbIndex = indexData;
        _recordbIndexSize = indexData.Length;
    }

    public void Write(Stream outfile)
    {
        WriteHeader(outfile);
        WriteKeySection(outfile);
        WriteRecordSection(outfile);
    }

    private void WriteHeader(Stream stream)
    {
        var header = GetHeaderString();

        // Encode to UTF-16 LE (must be identical to python .encode("utf_16_le")
        ReadOnlySpan<byte> headerBytes = Encoding.Unicode.GetBytes(header);
        // Console.WriteLine($"header bytes: {headerBytes.Length}");
        // Console.WriteLine("        " + string.Join(" ", headerBytes.Select(b => b.ToString("X2"))));

        // Write header length (big-endian)
        Span<byte> lengthBytes = stackalloc byte[4];
        Common.ToBigEndian((uint)headerBytes.Length, lengthBytes);
        stream.Write(lengthBytes);

        // Write header string
        stream.Write(headerBytes);

        // Write Adler32 checksum (little-endian)
        uint checksum = Common.Adler32(headerBytes);
        Span<byte> checksumBytes = stackalloc byte[4];
        Common.ToLittleEndian(checksum, checksumBytes);

        stream.Write(checksumBytes);
    }

    internal string GetHeaderString()
    {
        const string encrypted = "No";
        const string registerByStr = "";
        const string encoding = "UTF-8";

        var now = DateTime.Today;
        var sb = new StringBuilder();

        void append(ReadOnlySpan<char> val)
        {
            sb.Append(val.Trim());
            sb.Append(' ');
        }

        if (_isMdd)
        {
            append($"""  <Library_Data                                    """);
            append($"""  GeneratedByEngineVersion="{_version}"            """);
            append($"""  RequiredEngineVersion="{_version}"               """);
            append($"""  Encrypted="{encrypted}"                          """);
            append($"""  Encoding=""                                      """);
            append($"""  Format=""                                        """);
            append($"""  CreationDate="{now.Year}-{now.Month}-{now.Day}"  """);
            append($"""  KeyCaseSensitive="No"                            """);
            append($"""  Stripkey="No"                                    """);
            append($"""  Description="{EscapeHtml(_description)}"         """);
            append($"""  Title="{EscapeHtml(_title)}"                     """);
            append($"""  RegisterBy="{registerByStr}"                     """);
        }
        else
        {
            append($"""  <Dictionary                                      """);
            append($"""  GeneratedByEngineVersion="{_version}"            """);
            append($"""  RequiredEngineVersion="{_version}"               """);
            append($"""  Encrypted="{encrypted}"                          """);
            append($"""  Encoding="{encoding}"                            """);
            append($"""  Format="Html"                                    """);
            append($"""  Stripkey="Yes"                                   """);
            append($"""  CreationDate="{now.Year}-{now.Month}-{now.Day}"  """);
            append($"""  Compact="Yes"                                    """);
            append($"""  Compat="Yes"                                     """);
            append($"""  KeyCaseSensitive="No"                            """);
            append($"""  Description="{EscapeHtml(_description)}"         """);
            append($"""  Title="{EscapeHtml(_title)}"                     """);
            append($"""  DataSourceFormat="106"                           """);
            append($"""  StyleSheet=""                                    """);
            append($"""  Left2Right="Yes"                                 """);
            append($"""  RegisterBy="{registerByStr}"                     """);
        }
        sb.Append("/>\r\n\0");
        return sb.ToString();
    }

    // Same as python: escape(self._description, quote=True),
    // System.Web.HttpUtility.HtmlAttributeEncode(s) doesn't do the trick...
    private static string EscapeHtml(string s)
    {
        return s
            .Replace("&", "&amp;")   // Must be first
            .Replace("<", "&lt;")
            .Replace(">", "&gt;")
            .Replace("\"", "&quot;")
            .Replace("'", "&#x27;");
    }

    private void WriteKeySection(Stream outfile)
    {
        if (_version != "2.0")
        {
            throw new NotImplementedException();
        }

        long keyBlocksTotalValue = _keyBlocks.Sum(static b => b.BlockData.Length);

        Span<byte> preamble = stackalloc byte[5 * 8]; // Five 8-byte buffers

        Common.ToBigEndian((ulong)_keyBlocks.Count, preamble[0..8]);
        Common.ToBigEndian((ulong)_numEntries, preamble[8..16]);
        Common.ToBigEndian((ulong)_keybIndexDecompSize, preamble[16..24]);
        Common.ToBigEndian((ulong)_keybIndexCompSize, preamble[24..32]);
        Common.ToBigEndian((ulong)keyBlocksTotalValue, preamble[32..40]);

        uint checksumValue = Common.Adler32(preamble);
        Span<byte> checksum = stackalloc byte[4];
        Common.ToBigEndian(checksumValue, checksum);

        outfile.Write(preamble);
        outfile.Write(checksum);
        outfile.Write(_keybIndex.AsSpan());

        foreach (var block in _keyBlocks)
        {
            outfile.Write(block.BlockData);
        }
    }

    private void WriteRecordSection(Stream outfile)
    {
        if (_version != "2.0")
        {
            throw new NotImplementedException();
        }

        long recordblocksTotal = _recordBlocks.Sum(static b => b.BlockData.Length);

        Span<byte> preamble = stackalloc byte[4 * 8]; // Four 8-byte buffers

        Common.ToBigEndian((ulong)_recordBlocks.Count, preamble[0..8]);
        Common.ToBigEndian((ulong)_numEntries, preamble[8..16]);
        Common.ToBigEndian((ulong)_recordbIndexSize, preamble[16..24]);
        Common.ToBigEndian((ulong)recordblocksTotal, preamble[24..32]);

        outfile.Write(preamble);
        outfile.Write(_recordbIndex.AsSpan());

        foreach (var block in _recordBlocks)
        {
            outfile.Write(block.BlockData);
        }
    }
}

internal partial class MDictKeyComparer
{
    /// <summary>
    /// https://docs.python.org/3/library/string.html#string.punctuation
    /// </summary>
    public const string PunctuationChars = "!\"#$%&'()*+,-./:;<=>?@[\\]^_`{|}~";

    /// <summary>
    /// Regex to strip the python punctuation characters, and also the space character.
    /// </summary>
    [GeneratedRegex(@"[!\""#$%&'()*+,\-./:;<=>?@\[\\\]^_`{|}~ ]+")]
    public static partial Regex RegexStrip { get; }

    public static int Compare(ReadOnlySpan<char> k1, ReadOnlySpan<char> k2, bool isMdd)
    {
        if (!isMdd)
        {
            if (RegexStrip.IsMatch(k1))
                k1 = StripPunctuation(k1);

            if (RegexStrip.IsMatch(k2))
                k2 = StripPunctuation(k2);
        }

        // key1 = locale.strxfrm(key1) ??
        // this was locale dependent in py, but then we don't pass our tests,
        // and it shouldn't matter anyway as long as the internal mapping works
        int cmp = k1.CompareTo(k2, StringComparison.OrdinalIgnoreCase);

        if (cmp != 0)
            return cmp;

        // reverse length (longer first) - compare on current k1/k2
        if (k1.Length != k2.Length)
            return k2.Length.CompareTo(k1.Length);

        // trim punctuation (already stripped if this is not MDD)
        if (isMdd)
        {
            k1 = k1.TrimEnd(PunctuationChars);
            k2 = k2.TrimEnd(PunctuationChars);
        }

        return k2.CompareTo(k1, StringComparison.OrdinalIgnoreCase);
    }

    private static ReadOnlySpan<char> StripPunctuation(ReadOnlySpan<char> text)
    {
        Span<char> buffer = new char[text.Length];

        int lastIndex = 0;
        int charsWritten = 0;

        foreach (var match in RegexStrip.EnumerateMatches(text))
        {
            text[lastIndex..match.Index].CopyTo(buffer[charsWritten..]);
            charsWritten += match.Index - lastIndex;
            lastIndex = match.Index + match.Length;
        }

        text[lastIndex..text.Length].CopyTo(buffer[charsWritten..]);
        charsWritten += text.Length - lastIndex;

        return buffer[..charsWritten];
    }
}
