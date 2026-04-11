using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.IO.Compression;

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
    public required byte[] RecordNull { get; set; }
    public required bool IsMdd { get; init; }

    // This are sort of hidden in inheritance
    public required long RecordSize { get; init; }
    public required long RecordPos { get; init; }

    // Weird stuff from get_record_null()
    public required string FilePath { get; init; }

    public override string ToString()
    {
        static string BytesToString(ReadOnlySpan<byte> bytes)
            => bytes.IsEmpty ? "null" : Encoding.UTF8.GetString(bytes);

        return "OffsetTableEntry(" +
               $"KeyLen={KeyLen}, " +
               $"Offset={Offset}, " +
               $"RecordPos={RecordPos}, " +
               $"RecordSize={RecordSize}, " +
               $"IsMdd='{IsMdd}', " +
               $"Key='{BytesToString(Key)}', " +
               $"KeyNull='{BytesToString(KeyNull)}', " +
               $"RecordNull='{BytesToString(RecordNull)}')";
    }
}

/// <summary>
/// Abstract base class for <see cref="MdxRecordBlock"/> and <see cref="MdxKeyBlock"/>.
/// </summary>
internal abstract class MdxBlock
{
    protected long _decompSize;
    protected byte[] _compData;
    protected long _compSize;
    protected string _version;

    // This are sort of hidden in inheritance
    public long RecordSize { get; set; }
    public long RecordPos { get; set; }

    protected MdxBlock(List<OffsetTableEntry> offsetTable, int compressionType, string version)
    {
        if (compressionType != 2 || version != "2.0")
            throw new NotSupportedException();

        // Console.WriteLine("[Debug] Calling MdxBlock...");

        var decompData = new List<byte>();
        foreach (var entry in offsetTable)
        {
            var blockEntry = GetBlockEntry(entry, version);
            // Console.WriteLine($"[Debug] BlockEntry ({blockEntry.Length} bytes): {BitConverter.ToString(blockEntry)}");
            decompData.AddRange(blockEntry);
        }

        var decompArray = decompData.ToArray();
        // Console.WriteLine("[Debug] Building MdxBlock...");
        _decompSize = decompArray.Length;
        // Console.WriteLine($"[Debug] Decompressed array length (_decompSize): {_decompSize}");
        // Common.PrintPythonStyle(decompArray);

        _compData = MdxCompress(decompArray, compressionType);
        _compSize = _compData.Length;
        // Console.WriteLine($"[Debug] Compressed array length (_compSize): {_compSize}");

        _version = version;
    }

    public ReadOnlySpan<byte> BlockData => _compData;

    public abstract byte[] GetIndexEntry();
    protected abstract byte[] GetBlockEntry(OffsetTableEntry entry, string version);
    public abstract int LenBlockEntry(OffsetTableEntry entry);

    // Called in MdxBlock init
    public static byte[] MdxCompress(ReadOnlySpan<byte> data, int compressionType)
    {
        if (compressionType != 2)
            throw new NotSupportedException("Only compressionType=2 (Zlib) is supported in this version.");

        // Compression type (little-endian)
        var lend = Common.ToLittleEndian(BitConverter.GetBytes(compressionType)); // <L in Python

        // Adler32 checksum (big-endian)
        uint adler = Common.Adler32(data);
        var adlerBytes = Common.ToBigEndian(adler); // Python uses >L

        // byte[] header = [.. lend, .. adlerBytes];

        using var ms = new MemoryStream();

        // return header + zlib.compress(data)
        // python default is -1 == 6 , see: https://docs.python.org/3/library/zlib.html#zlib.Z_DEFAULT_COMPRESSION
        // c# are cooked, custom-made levels, and may not correspond to anything
        // https://learn.microsoft.com/en-us/dotnet/api/system.io.compression.compressionlevel?view=net-10.0
        //
        // There is no reliable way to get the same exact bytes, so live with that
        using (var z = new ZLibStream(ms, CompressionLevel.Optimal, leaveOpen: true))
        {
            z.Write(data);
        }
        var res = ms.ToArray();

        // Common.PrintPythonStyle(data);
        // Common.PrintPythonStyle(lend);
        // Console.WriteLine($"adler: {adler}");
        // Common.PrintPythonStyle(adlerBytes);
        // Console.WriteLine($"header: {BitConverter.ToString(header)}");
        // Common.PrintPythonStyle(final);

        return [.. lend, .. adlerBytes, .. res];
    }
}

internal class MdxRecordBlock(List<OffsetTableEntry> offsetTable, int compressionType, string version)
    : MdxBlock(offsetTable, compressionType, version)
{
    public override byte[] GetIndexEntry()
    {
        // Console.WriteLine("Called GetIndexEntry on MDXRECORDBLOCK");
        // Console.WriteLine($"    compSize {_compSize}; decompsize {_decompSize}");
        if (_version != "2.0")
        {
            throw new NotImplementedException();
        }

        // Big-endian 64-bit values
        return
        [
            .. Common.ToBigEndian((ulong)_compSize),
            .. Common.ToBigEndian((ulong)_decompSize),
        ];
    }

    // rg: get_record_null
    // We overwrite "return entry.RecordNull"
    protected override byte[] GetBlockEntry(OffsetTableEntry entry, string version)
    {
        byte[] record = ReadRecord(entry.FilePath, entry.RecordPos, (int)entry.RecordSize, entry.IsMdd);
        entry.RecordNull = record;
        return record;
    }

    /// <summary>
    /// Helper method: read from file and null-terminate
    /// </summary>
    private static byte[] ReadRecord(string filePath, long pos, int size, bool isMdd)
    {
        if (size < 1) throw new ArgumentException("Size must be >= 1", nameof(size));

        byte[] record = new byte[size];
        using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read);

        fs.Seek(pos, SeekOrigin.Begin);
        if (isMdd)
        {
            // For MDD, just read the whole record
            int bytesRead = fs.Read(record, 0, size);
            if (bytesRead < size)
            {
                // Trim if fewer bytes were read
                Array.Resize(ref record, bytesRead);
            }
        }
        else
        {
            // For MDX, read size-1 bytes and append null byte
            int bytesRead = fs.Read(record, 0, size - 1);
            record[bytesRead] = 0; // null-terminate
        }

        // Console.WriteLine($"[ReadRecord] Record length: {record.Length}");

        return record;
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

    protected override byte[] GetBlockEntry(OffsetTableEntry entry, string version)
    {
        Debug.Assert(version == "2.0");
        return
        [
            .. Common.ToBigEndian((ulong)entry.Offset),
            .. entry.KeyNull,
        ];
    }

    // Approximate for version 2.0
    public override int LenBlockEntry(OffsetTableEntry entry)
    {
        return 8 + entry.KeyNull.Length;
    }

    public override byte[] GetIndexEntry()
    {
        Debug.Assert(_version == "2.0");
        return
        [
            .. Common.ToBigEndian((ulong)_numEntries),
            .. Common.ToBigEndian((ushort)_firstKeyLen),
            .. _firstKey,
            .. Common.ToBigEndian((ushort)_lastKeyLen),
            .. _lastKey,
            .. Common.ToBigEndian((ulong)_compSize),
            .. Common.ToBigEndian((ulong)_decompSize),
        ];
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
            var keyNull = _innerEncoding.GetBytes(item.Key + "\0");
            var keyLen = keyEnc.Length / _encodingLength;

            var recordNull = _innerEncoding.GetBytes(item.Path);

            var tableEntry = new OffsetTableEntry
            {
                Key = keyEnc,
                KeyNull = keyNull,
                KeyLen = keyLen,
                RecordNull = recordNull,
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
        var decompData = new List<byte>();
        foreach (var block in _keyBlocks)
        {
            var indexEntry = block.GetIndexEntry();
            _logger.LogIndexEntry(indexEntry);
            decompData.AddRange(indexEntry);
        }

        var decompArray = decompData.ToArray();
        _keybIndexDecompSize = decompArray.Length;
        _keybIndex = MdxBlock.MdxCompress(decompArray, _compressionType);
        _keybIndexCompSize = _keybIndex.Length;
    }

    private void BuildRecordbIndex()
    {
        List<byte> indexData = [];
        foreach (var block in _recordBlocks)
        {
            indexData.AddRange(block.GetIndexEntry());
        }
        _recordbIndex = [.. indexData];
        _recordbIndexSize = _recordbIndex.Length;
    }

    public void Write(Stream outfile)
    {
        WriteHeader(outfile);
        WriteKeySection(outfile);
        WriteRecordSection(outfile);
    }

    private void WriteHeader(Stream stream)
    {
        const string encrypted = "No";
        const string registerByStr = "";
        var now = DateTime.Today;

        var header = _isMdd
            ? // MDD header
            $"""
            <Library_Data
            GeneratedByEngineVersion="{_version}"
            RequiredEngineVersion="{_version}"
            Encrypted="{encrypted}"
            Encoding=""
            Format=""
            CreationDate="{now.Year}-{now.Month}-{now.Day}"
            KeyCaseSensitive="No"
            Stripkey="No"
            Description="{EscapeHtml(_description)}"
            Title="{EscapeHtml(_title)}"
            RegisterBy="{registerByStr}"
            />
            """
            : // MDX header
            $"""
            <Dictionary
            GeneratedByEngineVersion="{_version}"
            RequiredEngineVersion="{_version}"
            Encrypted="{encrypted}"
            Encoding="UTF-8"
            Format="Html"
            Stripkey="Yes"
            CreationDate="{now.Year}-{now.Month}-{now.Day}"
            Compact="Yes"
            Compat="Yes"
            KeyCaseSensitive="No"
            Description="{EscapeHtml(_description)}"
            Title="{EscapeHtml(_title)}"
            DataSourceFormat="106"
            StyleSheet=""
            Left2Right="Yes"
            RegisterBy="{registerByStr}"
            />
            """;

        header = header.ReplaceLineEndings(" ") + "\r\n\0";

        // Console.WriteLine($"{headerString}");
        // Console.WriteLine($"header str: {headerString.Length}");

        // Encode to UTF-16 LE (must be identical to python .encode("utf_16_le")
        ReadOnlySpan<byte> headerBytes = Encoding.Unicode.GetBytes(header);
        // Console.WriteLine($"header bytes: {headerBytes.Length}");
        // Console.WriteLine("        " + string.Join(" ", headerBytes.Select(b => b.ToString("X2"))));

        // Write header length (big-endian)
        var lengthBytes = Common.ToBigEndian((uint)headerBytes.Length);
        stream.Write(lengthBytes);

        // Write header string
        stream.Write(headerBytes);

        // Write Adler32 checksum (little-endian)
        uint checksum = Common.Adler32(headerBytes);
        var checksumBytes = Common.ToLittleEndian(checksum);

        stream.Write(checksumBytes);
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

        long keyblocksTotal = _keyBlocks.Sum(static b => b.BlockData.Length);

        ReadOnlySpan<byte> preamble =
        [
            .. Common.ToBigEndian((ulong)_keyBlocks.Count),
            .. Common.ToBigEndian((ulong)_numEntries),
            .. Common.ToBigEndian((ulong)_keybIndexDecompSize),
            .. Common.ToBigEndian((ulong)_keybIndexCompSize),
            .. Common.ToBigEndian((ulong)keyblocksTotal),
        ];

        var preambleChecksum = Common.Adler32(preamble);
        var checksumBytes = Common.ToBigEndian(preambleChecksum);

        outfile.Write(preamble);
        outfile.Write(checksumBytes);
        outfile.Write(_keybIndex, 0, _keybIndex.Length);

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

        ReadOnlySpan<byte> preamble =
        [
            .. Common.ToBigEndian((ulong)_recordBlocks.Count),
            .. Common.ToBigEndian((ulong)_numEntries),
            .. Common.ToBigEndian((ulong)_recordbIndexSize),
            .. Common.ToBigEndian((ulong)recordblocksTotal),
        ];

        outfile.Write(preamble);
        outfile.Write(_recordbIndex, 0, _recordbIndex.Length);

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
    public static readonly char[] PunctuationChars = [.. "!\"#$%&'()*+,-./:;<=>?@[\\]^_`{|}~"];

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
