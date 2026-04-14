using System;
using System.Buffers;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Collections.ObjectModel;
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

    public long MdxKeyBlockEntryLength => 8 + KeyNull.Length;
    public long MdxRecordBlockEntryLength => RecordSize;

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
    protected ImmutableArray<byte> _compData;
    protected long _compSize;

    protected MdxBlock(ReadOnlySpan<OffsetTableEntry> offsetTableEntries, int compressionType)
    {
        if (compressionType != 2)
            throw new NotSupportedException();

        // Console.WriteLine("[Debug] Calling MdxBlock...");

        long longDecompDataSize = offsetTableEntries.Sum(BlockEntryLength);
        int decompDataSize = Convert.ToInt32(longDecompDataSize);
        var decompData = _arrayPool.Rent(decompDataSize);

        var maxBlockSize = offsetTableEntries.Max(BlockEntryLength);
        var blockBuffer = maxBlockSize < 256
            ? stackalloc byte[(int)maxBlockSize]
            : new byte[maxBlockSize];

        int totalSize = 0;
        foreach (var entry in offsetTableEntries)
        {
            int blockSize = GetBlockEntry(entry, blockBuffer);
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

        _arrayPool.Return(decompData);
    }

    public ReadOnlySpan<byte> BlockData => _compData.AsSpan();

    public abstract void GetIndexEntry(Span<byte> buffer);
    protected abstract int GetBlockEntry(OffsetTableEntry entry, Span<byte> buffer);
    public abstract long BlockEntryLength(OffsetTableEntry entry);
    public abstract int IndexEntryLength { get; }

    // Called in MdxBlock init
    public static ImmutableArray<byte> MdxCompress(ReadOnlySpan<byte> data, int compressionType)
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

        ImmutableArray<byte> compressed = [.. lend, .. adlerBytes, .. buffer.AsSpan(..size)];
        _arrayPool.Return(buffer);

        // Console.WriteLine($"adler: {adler}");
        // Console.WriteLine($"header: {BitConverter.ToString(header)}");

        return compressed;
    }
}

internal class MdxRecordBlock(ReadOnlySpan<OffsetTableEntry> offsetTable, int compressionType)
    : MdxBlock(offsetTable, compressionType)
{
    public override int IndexEntryLength => 16;

    public override long BlockEntryLength(OffsetTableEntry entry)
        => entry.MdxRecordBlockEntryLength;

    public override void GetIndexEntry(Span<byte> buffer)
    {
        // Console.WriteLine("Called GetIndexEntry on MDXRECORDBLOCK");
        // Console.WriteLine($"    compSize {_compSize}; decompsize {_decompSize}");
        // if (_version != "2.0")
        //     throw new NotImplementedException();

        Debug.Assert(buffer.Length == IndexEntryLength);

        // Big-endian 64-bit values
        Common.ToBigEndian((ulong)_compSize, buffer[..8]);
        Common.ToBigEndian((ulong)_decompSize, buffer[8..16]);
    }

    // rg: get_record_null
    // We overwrite "return entry.RecordNull"
    protected override int GetBlockEntry(OffsetTableEntry entry, Span<byte> buffer)
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

    public MdxKeyBlock(ReadOnlySpan<OffsetTableEntry> offsetTable, int compressionType)
        : base(offsetTable, compressionType)
    {
        _numEntries = offsetTable.Length;
        _firstKey = offsetTable[0].KeyNull;
        _lastKey = offsetTable[^1].KeyNull;
        _firstKeyLen = offsetTable[0].KeyLen;
        _lastKeyLen = offsetTable[^1].KeyLen;
    }

    protected override int GetBlockEntry(OffsetTableEntry entry, Span<byte> buffer)
    {
        Common.ToBigEndian((ulong)entry.Offset, buffer[..8]);
        entry.KeyNull.CopyTo(buffer[8..]);
        return 8 + entry.KeyNull.Length;
    }

    // Approximate for version 2.0
    public override long BlockEntryLength(OffsetTableEntry entry)
        => entry.MdxKeyBlockEntryLength;

    public override int IndexEntryLength
        => 8 + 2 + _firstKey.Length + 2 + _lastKey.Length + 8 + 8;

    public override void GetIndexEntry(Span<byte> buffer)
    {
        // Debug.Assert(_version == "2.0");
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

internal sealed record EncodingSettings(
    Encoding InnerEncoding, // _python_encoding in the original
    Encoding Encoding,
    int EncodingLength);

internal sealed record KeyBlockIndex(ImmutableArray<byte> CompressedBytes, long DecompSize)
{
    public int CompressedSize => CompressedBytes.Length;
}

internal sealed record RecordBlockIndex(ImmutableArray<byte> Bytes)
{
    public int Size => Bytes.Length;
}

internal sealed record OffsetTable(ImmutableArray<OffsetTableEntry> Entries)
{
    public long TotalRecordLength => Entries.Sum(static e => e.RecordSize);
}

internal sealed record MDictData
(
    MDictWriterOptions Metadata,
    int EntryCount,
    OffsetTable OffsetTable,
    ReadOnlyCollection<MdxKeyBlock> KeyBlocks,
    ReadOnlyCollection<MdxRecordBlock> RecordBlocks,
    KeyBlockIndex KeyBlockIndex,
    RecordBlockIndex RecordBlockIndex
);

public sealed class MDictWriter
{
    private readonly MDictData _data;

    public MDictWriter(List<MDictEntry> entries, MDictWriterOptions? opt = null)
    {
        opt ??= new();

        IMDictWriterLogger logger = opt.Logging
            ? new MDictWriterLogger()
            : new MDictWriterDummyLogger();

        if (opt.Version != "2.0")
        {
            throw new ArgumentException("Unknown version. Supported: 2.0");
        }

        var offsetTable = BuildOffsetTable(entries, opt);
        logger.LogOffsetTable(offsetTable);

        logger.LogBeginBuildingKeyBlocks();
        var keyBlocks = BuildKeyBlocks(offsetTable, opt).AsReadOnly();
        logger.LogKeyBlocks(opt.KeySize, keyBlocks);

        logger.LogBeginBuildingKeybIndex();
        var keyBlockIndex = BuildKeyBlockIndex(keyBlocks, logger, opt.CompressionType);
        logger.LogKeyBlockIndex(keyBlockIndex);

        var recordBlocks = BuildRecordBlocks(offsetTable, opt).AsReadOnly();
        logger.LogRecordBlocks(recordBlocks);

        var recordBlockIndex = BuildRecordBlockIndex(recordBlocks);
        logger.LogRecordIndex(recordBlockIndex);

        _data = new(
            opt,
            entries.Count,
            offsetTable,
            keyBlocks,
            recordBlocks,
            keyBlockIndex,
            recordBlockIndex);

        logger.LogInitializationComplete();
    }

    private EncodingSettings GetEncodingSettings(MDictWriterOptions opt)
    {
        var encoding = opt.Encoding.ToLower();
        Debug.Assert(encoding == "utf8");

        if (opt.IsMdd || encoding == "utf16" || encoding == "utf-16")
        {
            return new(
                InnerEncoding: Encoding.Unicode,
                Encoding: Encoding.Unicode,
                EncodingLength: 2);
        }
        else if (encoding == "utf8" || encoding == "utf-8")
        {
            return new(
                InnerEncoding: Encoding.UTF8,
                Encoding: Encoding.UTF8,
                EncodingLength: 1);
        }
        else
        {
            throw new ArgumentException("Unknown encoding. Supported: utf8, utf16");
        }
    }

    private OffsetTable BuildOffsetTable(List<MDictEntry> entries, MDictWriterOptions opt)
    {
        entries.Sort((a, b) => MDictKeyComparer.Compare(a.Key, b.Key, opt.IsMdd));

        var encodingSettings = GetEncodingSettings(opt);
        var arrayBuilder = ImmutableArray.CreateBuilder<OffsetTableEntry>(entries.Count);
        long currentOffset = 0;

        foreach (var item in entries)
        {
            // Console.WriteLine($"dict item: {item}");
            var keyEnc = encodingSettings.InnerEncoding.GetBytes(item.Key);
            var keyNull = encodingSettings.InnerEncoding.GetBytes($"{item.Key}\0");
            var keyLen = keyEnc.Length / encodingSettings.EncodingLength;

            // var recordNull = encodingSettings.InnerEncoding.GetBytes(item.Path);

            var tableEntry = new OffsetTableEntry
            {
                Key = keyEnc,
                KeyNull = keyNull,
                KeyLen = keyLen,
                // RecordNull = recordNull,
                Offset = currentOffset,
                RecordSize = item.Size,
                RecordPos = item.Pos,
                FilePath = item.Path,
                IsMdd = opt.IsMdd,
            };
            arrayBuilder.Add(tableEntry);

            currentOffset += item.Size;
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

        return new OffsetTable(arrayBuilder.MoveToImmutable());
    }

    private List<T> SplitBlocks<T>(Func<ReadOnlySpan<OffsetTableEntry>, int, T> blockConstructor,
                                   Func<OffsetTableEntry, long> lenFunc,
                                   OffsetTable offsetTable,
                                   int blockSize,
                                   int compressionType) where T : MdxBlock
    {
        var blocks = new List<T>();
        int thisBlockStart = 0;
        long curSize = 0;

        for (int ind = 0; ind <= offsetTable.Entries.Length; ind++)
        {
            var offsetTableEntry = (ind == offsetTable.Entries.Length)
                ? null
                : offsetTable.Entries[ind];

            bool flush = false;

            if (ind == 0)
            {
                flush = false;
            }
            else if (offsetTableEntry == null)
            {
                flush = true;
            }
            else if (curSize + lenFunc(offsetTableEntry) > blockSize)
            {
                flush = true;
            }

            if (flush)
            {
                var blockEntries = offsetTable.Entries.AsSpan(thisBlockStart..ind);
                // foreach (var entry in blockEntries)
                // {
                //     Console.WriteLine($"[split flush] {entry}");
                // }
                var block = blockConstructor(blockEntries, compressionType);
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

    private List<MdxKeyBlock> BuildKeyBlocks(OffsetTable offsetTable, MDictWriterOptions opt)
        => SplitBlocks
        (
            static (entries, comp) => new MdxKeyBlock(entries, comp),
            static (entry) => entry.MdxKeyBlockEntryLength,
            offsetTable,
            opt.KeySize,
            opt.CompressionType
        );

    private List<MdxRecordBlock> BuildRecordBlocks(OffsetTable offsetTable, MDictWriterOptions opt)
        => SplitBlocks
        (
            static (entries, comp) => new MdxRecordBlock(entries, comp),
            static (entry) => entry.MdxRecordBlockEntryLength,
            offsetTable,
            opt.BlockSize,
            opt.CompressionType
        );

    private KeyBlockIndex BuildKeyBlockIndex(ReadOnlyCollection<MdxKeyBlock> keyBlocks, IMDictWriterLogger logger, int compressionType)
    {
        if (keyBlocks is [])
            return new([], 0);

        var arrayPool = ArrayPool<byte>.Shared;

        int decompDataTotalSize = keyBlocks.Sum(static b => b.IndexEntryLength);
        var decompArray = arrayPool.Rent(decompDataTotalSize);
        var decompData = decompArray.AsSpan(..decompDataTotalSize);

        int maxBlockSize = keyBlocks.Max(static b => b.IndexEntryLength);
        var blockBuffer = maxBlockSize < 256
            ? stackalloc byte[maxBlockSize]
            : new byte[maxBlockSize];

        int bytesWritten = 0;
        foreach (var block in keyBlocks)
        {
            var indexEntry = blockBuffer[..block.IndexEntryLength];
            block.GetIndexEntry(indexEntry);
            logger.LogIndexEntry(indexEntry);

            var destination = decompData.Slice(bytesWritten, indexEntry.Length);
            indexEntry.CopyTo(destination);
            bytesWritten += indexEntry.Length;
        }
        Debug.Assert(bytesWritten == decompDataTotalSize);

        var compressedBytes = MdxBlock.MdxCompress(decompData, compressionType);

        KeyBlockIndex index = new(
            CompressedBytes: compressedBytes,
            DecompSize: bytesWritten);

        arrayPool.Return(decompArray);

        return index;
    }

    private RecordBlockIndex BuildRecordBlockIndex(ReadOnlyCollection<MdxRecordBlock> recordBlocks)
    {
        if (recordBlocks is [])
            return new([]);

        int indexSize = recordBlocks.Sum(static b => b.IndexEntryLength);
        var indexBuilder = ImmutableArray.CreateBuilder<byte>(indexSize);

        int maxBlockSize = recordBlocks.Max(static b => b.IndexEntryLength);
        var blockBuffer = maxBlockSize < 256
            ? stackalloc byte[maxBlockSize]
            : new byte[maxBlockSize];

        int bytesWritten = 0;
        foreach (var block in recordBlocks)
        {
            var indexEntry = blockBuffer[..block.IndexEntryLength];
            block.GetIndexEntry(indexEntry);

            indexBuilder.AddRange(indexEntry);
            bytesWritten += indexEntry.Length;
        }
        Debug.Assert(bytesWritten == indexSize);

        return new(indexBuilder.MoveToImmutable());
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

        if (_data.Metadata.IsMdd)
        {
            append($"""  <Library_Data                                           """);
            append($"""  GeneratedByEngineVersion="{_data.Metadata.Version}"     """);
            append($"""  RequiredEngineVersion="{_data.Metadata.Version}"        """);
            append($"""  Encrypted="{encrypted}"                                 """);
            append($"""  Encoding=""                                             """);
            append($"""  Format=""                                               """);
            append($"""  CreationDate="{now.Year}-{now.Month}-{now.Day}"         """);
            append($"""  KeyCaseSensitive="No"                                   """);
            append($"""  Stripkey="No"                                           """);
            append($"""  Description="{EscapeHtml(_data.Metadata.Description)}"  """);
            append($"""  Title="{EscapeHtml(_data.Metadata.Title)}"              """);
            append($"""  RegisterBy="{registerByStr}"                            """);
        }
        else
        {
            append($"""  <Dictionary                                             """);
            append($"""  GeneratedByEngineVersion="{_data.Metadata.Version}"     """);
            append($"""  RequiredEngineVersion="{_data.Metadata.Version}"        """);
            append($"""  Encrypted="{encrypted}"                                 """);
            append($"""  Encoding="{encoding}"                                   """);
            append($"""  Format="Html"                                           """);
            append($"""  Stripkey="Yes"                                          """);
            append($"""  CreationDate="{now.Year}-{now.Month}-{now.Day}"         """);
            append($"""  Compact="Yes"                                           """);
            append($"""  Compat="Yes"                                            """);
            append($"""  KeyCaseSensitive="No"                                   """);
            append($"""  Description="{EscapeHtml(_data.Metadata.Description)}"  """);
            append($"""  Title="{EscapeHtml(_data.Metadata.Title)}"              """);
            append($"""  DataSourceFormat="106"                                  """);
            append($"""  StyleSheet=""                                           """);
            append($"""  Left2Right="Yes"                                        """);
            append($"""  RegisterBy="{registerByStr}"                            """);
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
        if (_data.Metadata.Version != "2.0")
        {
            throw new NotImplementedException();
        }

        long keyBlocksTotalValue = _data.KeyBlocks.Sum(static b => b.BlockData.Length);

        Span<byte> preamble = stackalloc byte[5 * 8]; // Five 8-byte buffers
        var r = new SpanReader<byte>(preamble) { ReadSize = 8 };

        Common.ToBigEndian((ulong)_data.KeyBlocks.Count, r.Read());
        Common.ToBigEndian((ulong)_data.EntryCount, r.Read());
        Common.ToBigEndian((ulong)_data.KeyBlockIndex.DecompSize, r.Read());
        Common.ToBigEndian((ulong)_data.KeyBlockIndex.CompressedSize, r.Read());
        Common.ToBigEndian((ulong)keyBlocksTotalValue, r.Read());

        uint checksumValue = Common.Adler32(preamble);
        Span<byte> checksum = stackalloc byte[4];
        Common.ToBigEndian(checksumValue, checksum);

        outfile.Write(preamble);
        outfile.Write(checksum);
        outfile.Write(_data.KeyBlockIndex.CompressedBytes.AsSpan());

        foreach (var block in _data.KeyBlocks)
        {
            outfile.Write(block.BlockData);
        }
    }

    private void WriteRecordSection(Stream outfile)
    {
        if (_data.Metadata.Version != "2.0")
        {
            throw new NotImplementedException();
        }

        long recordblocksTotal = _data.RecordBlocks.Sum(static b => b.BlockData.Length);

        Span<byte> preamble = stackalloc byte[4 * 8]; // Four 8-byte buffers
        var r = new SpanReader<byte>(preamble) { ReadSize = 8 };

        Common.ToBigEndian((ulong)_data.RecordBlocks.Count, r.Read());
        Common.ToBigEndian((ulong)_data.EntryCount, r.Read());
        Common.ToBigEndian((ulong)_data.RecordBlockIndex.Size, r.Read());
        Common.ToBigEndian((ulong)recordblocksTotal, r.Read());

        outfile.Write(preamble);
        outfile.Write(_data.RecordBlockIndex.Bytes.AsSpan());

        foreach (var block in _data.RecordBlocks)
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
