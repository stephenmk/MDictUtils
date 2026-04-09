using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.IO.Compression;

using D = System.Collections.Generic.List<Lib.MDictEntry>;

namespace Lib;

// Tbh this is the Writer part

public class MDictEntry
{
    public string Key { get; set; }
    public long Pos { get; set; }
    public string Path { get; set; }
    public long Size { get; set; }

    public override string ToString()
    {
        return $"Key=\"{Key}\", Pos={Pos}, Size={Size}";
    }
}

internal class OffsetTableEntry
{
    public byte[] Key { get; set; }
    public byte[] KeyNull { get; set; }
    public int KeyLen { get; set; }
    public long Offset { get; set; }
    public byte[] RecordNull { get; set; }
    public bool IsMdd { get; set; }

    // This are sort of hidden in inheritance
    public long RecordSize { get; set; }
    public long RecordPos { get; set; }

    // Weird stuff from get_record_null()
    public string FilePath { get; set; }

    public override string ToString()
    {
        static string BytesToString(byte[] arr)
        {
            if (arr == null || arr.Length == 0) return "null";
            return Encoding.UTF8.GetString(arr);
        }

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

// # Abstract base class for MdxRecordBlock and MdxKeyBlock.
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

    public byte[] GetBlock()
    {
        return _compData;
    }

    public abstract byte[] GetIndexEntry();
    protected abstract byte[] GetBlockEntry(OffsetTableEntry entry, string version);
    public abstract int LenBlockEntry(OffsetTableEntry entry);

    // Called in MdxBlock init
    public static byte[] MdxCompress(byte[] data, int compressionType)
    {
        if (compressionType != 2)
            throw new NotSupportedException("Only compressionType=2 (Zlib) is supported in this version.");

        // Compression type (little-endian)
        byte[] lend = BitConverter.GetBytes(compressionType); // <L in Python
        if (!BitConverter.IsLittleEndian) Array.Reverse(lend);

        uint adler = Adler32(data);
        byte[] adlerBytes = BitConverter.GetBytes(adler);
        if (BitConverter.IsLittleEndian) Array.Reverse(adlerBytes); // Python uses >L

        // Adler32 checksum (big-endian)
        byte[] header = [.. lend.Concat(adlerBytes)];

        using var ms = new MemoryStream();

        // return header + zlib.compress(data)
        // python default is -1 == 6 , see: https://docs.python.org/3/library/zlib.html#zlib.Z_DEFAULT_COMPRESSION
        // c# are cooked, custom-made levels, and may not correspond to anything
        // https://learn.microsoft.com/en-us/dotnet/api/system.io.compression.compressionlevel?view=net-10.0
        //
        // There is no reliable way to get the same exact bytes, so live with that
        using (var z = new ZLibStream(ms, CompressionLevel.Optimal, leaveOpen: true))
        {
            z.Write(data, 0, data.Length);
        }
        var res = ms.ToArray();

        // Common.PrintPythonStyle(data);
        // Common.PrintPythonStyle(lend);
        // Console.WriteLine($"adler: {adler}");
        // Common.PrintPythonStyle(adlerBytes);
        // Console.WriteLine($"header: {BitConverter.ToString(header)}");
        // Common.PrintPythonStyle(final);

        return [.. header, .. res];
    }

    // Check zlib implementation...
    //
    // https://github.com/madler/zlib/blob/f9dd6009be3ed32415edf1e89d1bc38380ecb95d/adler32.c#L128
    // https://gist.github.com/AristurtleDev/316358b3f87fd995923b79350be342f5
    //
    // header = (struct.pack(b"<L", compression_type) + 
    //          struct.pack(b">L", zlib.adler32(data) & 0xffffffff)) #depending on python version, zlib.adler32 may return a signed number. 
    private const uint BASE = 65521;
    private const int NMAX = 5552;

    public static uint Adler32(byte[] buf)
    {
        if (buf == null) return 1;

        uint adler = 1;
        uint sum2 = 0;

        int len = buf.Length;
        int index = 0;

        while (len > 0)
        {
            int blockLen = len < NMAX ? len : NMAX;
            len -= blockLen;

            while (blockLen >= 16)
            {
                for (int i = 0; i < 16; i++)
                {
                    adler += buf[index++];
                    sum2 += adler;
                }
                blockLen -= 16;
            }

            while (blockLen-- > 0)
            {
                adler += buf[index++];
                sum2 += adler;
            }

            adler %= BASE;
            sum2 %= BASE;
        }

        return (sum2 << 16) | adler;
    }
}

internal class MdxRecordBlock(List<OffsetTableEntry> offsetTable, int compressionType, string version) : MdxBlock(offsetTable, compressionType, version)
{
    public override byte[] GetIndexEntry()
    {
        // Console.WriteLine("Called GetIndexEntry on MDXRECORDBLOCK");
        // Console.WriteLine($"    compSize {_compSize}; decompsize {_decompSize}");

        List<byte> result = [];

        if (_version == "2.0")
        {
            // Big-endian 64-bit values
            result.AddRange(Common.ToBigEndian((ulong)_compSize));
            result.AddRange(Common.ToBigEndian((ulong)_decompSize));
        }
        else
        {
            throw new NotImplementedException();
        }

        return [.. result];
    }

    // rg: get_record_null
    // We overwrite "return entry.RecordNull"
    protected override byte[] GetBlockEntry(OffsetTableEntry entry, string version)
    {
        byte[] record = ReadRecord(entry.FilePath, entry.RecordPos, (int)entry.RecordSize, entry.IsMdd);
        entry.RecordNull = record;
        return record;
    }

    // Helper method: read from file and null-terminate
    private static byte[] ReadRecord(string filePath, long pos, int size, bool isMdd)
    {
        if (size < 1) throw new ArgumentException("Size must be >= 1", nameof(size));

        byte[] record = new byte[size];
        using (var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read))
        {
            fs.Seek(pos, SeekOrigin.Begin);
            if (isMdd)
            {
                // For MDD, just read the whole record
                record = new byte[size];
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
                record = new byte[size];
                int bytesRead = fs.Read(record, 0, size - 1);
                record[bytesRead] = 0; // null-terminate
            }
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
        string firstKeyStr = _firstKey != null ? _encoding.GetString(_firstKey, 0, _firstKeyLen) : "";
        string lastKeyStr = _lastKey != null ? _encoding.GetString(_lastKey, 0, _lastKeyLen) : "";
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
        List<byte> result = [];
        Debug.Assert(version == "2.0");
        result.AddRange(Common.ToBigEndian((ulong)entry.Offset));
        result.AddRange(entry.KeyNull);
        return [.. result];
    }

    // Approximate for version 2.0
    public override int LenBlockEntry(OffsetTableEntry entry)
    {
        return 8 + entry.KeyNull.Length;
    }

    public override byte[] GetIndexEntry()
    {
        List<byte> result = [];
        Debug.Assert(_version == "2.0");

        result.AddRange(Common.ToBigEndian((ulong)_numEntries));
        result.AddRange(Common.ToBigEndian((ushort)_firstKeyLen));
        result.AddRange(_firstKey);
        result.AddRange(Common.ToBigEndian((ushort)_lastKeyLen));
        result.AddRange(_lastKey);
        result.AddRange(Common.ToBigEndian((ulong)_compSize));
        result.AddRange(Common.ToBigEndian((ulong)_decompSize));

        return [.. result];
    }
}

public class MDictWriter
{
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

    private List<OffsetTableEntry> _offsetTable;
    private List<MdxKeyBlock> _keyBlocks;
    private List<MdxRecordBlock> _recordBlocks;
    private byte[] _keybIndex;
    private long _keybIndexCompSize;
    private long _keybIndexDecompSize;
    private byte[] _recordbIndex;
    private long _recordbIndexSize;
    private long _totalRecordLen;

    public MDictWriter(D dictionary,
                      string title = "",
                      string description = "",
                      int keySize = 32768,
                      int blockSize = 65536,
                      string encoding = "utf8",
                      int compressionType = 2,
                      string version = "2.0",
                      bool isMdd = false)
    {
        _numEntries = dictionary.Count;
        _title = title;
        _description = description;
        _blockSize = blockSize;
        _compressionType = compressionType;
        _version = version;
        _isMdd = isMdd;

        // Set encoding
        encoding = encoding.ToLower();
        Debug.Assert(encoding == "utf8");
        if (isMdd || encoding == "utf16" || encoding == "utf-16")
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

        if (version != "2.0")
        {
            throw new ArgumentException("Unknown version. Supported: 2.0");
        }

        BuildOffsetTable(dictionary);
        Console.WriteLine("[Writer] Offset table built.");
        Console.WriteLine($"[Writer] Total entries: {_offsetTable.Count}, record length {_totalRecordLen}");
        Console.WriteLine("=========================");

        Console.WriteLine("[Writer] Building KeyBlocks");
        _blockSize = keySize;
        BuildKeyBlocks();
        Console.WriteLine($"[Writer] Block size set to {_blockSize}");
        Console.WriteLine($"[Writer] Built {_keyBlocks.Count} key blocks.");
        foreach (var item in _keyBlocks) { Console.WriteLine($"* KeyBlock: {item}"); }

        _blockSize = blockSize;
        Console.WriteLine($"[Writer] Block size reset to {_blockSize}");
        Console.WriteLine("=========================");

        Console.WriteLine("[Writer] Building KeybIndex");
        BuildKeybIndex();
        Console.WriteLine($"[Writer] Key index built: decompressed={_keybIndexDecompSize}, compressed={_keybIndexCompSize}");
        Console.WriteLine("=========================");

        BuildRecordBlocks();
        Console.WriteLine($"[Writer] Built {_recordBlocks.Count} record blocks.");
        Console.WriteLine($"[Writer] Built {_recordBlocks}.");
        Console.WriteLine("=========================");

        BuildRecordbIndex();
        Console.WriteLine($"[Writer] Record index built: size={_recordbIndexSize}");
        Console.WriteLine("=========================");

        Console.WriteLine("[Writer] Initialization complete.\n");
    }

    // We could merge this two at some point
    // Also internal so we can test it
    // [!"#$%&\'()*+,-./:;<=>?@[\\]^_`{|}~ ]+
    internal static readonly Regex _regexStrip = new(@"[!\""#$%&'()*+,\-./:;<=>?@\[\\\]^_`{|}~]+");
    internal static readonly char[] _punctuationChars = [.. "!\"#$%&'()*+,-./:;<=>?@[\\]^_`{|}~"];

    // To be static, we pass isMdd (instead of reading _isMdd)
    // Also internal so we can test it
    internal static int CompareMDictKeys(string key1, string key2, bool isMdd)
    {
        string k1 = key1.ToLower();
        string k2 = key2.ToLower();

        if (!isMdd)
        {
            k1 = _regexStrip.Replace(k1, "");
            k2 = _regexStrip.Replace(k2, "");
        }

        // key1 = locale.strxfrm(key1) ??
        // this was locale dependent in py, but then we don't pass our tests,
        // and it shouldn't matter anyway as long as the internal mapping works
        int cmp = string.CompareOrdinal(k1, k2);
        if (cmp != 0) return cmp;

        // reverse length (longer first) - compare on current k1/k2
        if (k1.Length != k2.Length)
            return k2.Length.CompareTo(k1.Length);

        // strip punctuation
        k1 = k1.TrimEnd(_punctuationChars);
        k2 = k2.TrimEnd(_punctuationChars);

        return string.CompareOrdinal(k2, k1);
    }

    private void BuildOffsetTable(D dictionary)
    {
        dictionary.Sort((a, b) => CompareMDictKeys(a.Key, b.Key, _isMdd));

        _offsetTable = [];
        long offset = 0;

        foreach (var item in dictionary)
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
            OffsetTableEntry t = (ind != _offsetTable.Count) ? _offsetTable[ind] : null;

            bool flush = false;

            if (ind == 0)
            {
                flush = false;
            }
            else if (ind == _offsetTable.Count)
            {
                flush = true;
            }
            else if (curSize + lenFunc(t) > _blockSize)
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

            if (t != null)
            {
                curSize += lenFunc(t);
            }
        }

        return blocks;
    }

    private void BuildKeyBlocks() => _keyBlocks = SplitBlocks(
            (entries, comp, ver) => new MdxKeyBlock(entries, comp, ver),
            (entry) => 8 + entry.KeyNull.Length
        );

    private void BuildRecordBlocks() => _recordBlocks = SplitBlocks(
            (entries, comp, ver) => new MdxRecordBlock(entries, comp, ver),
            (entry) => entry.RecordSize
        );

    private void BuildKeybIndex()
    {
        Debug.Assert(_version == "2.0");
        var decompData = new List<byte>();
        foreach (var block in _keyBlocks)
        {
            var thing = string.Join(" ", block.GetIndexEntry().Select(b => b.ToString("X2")));
            Console.WriteLine($"entry {thing}");
            decompData.AddRange(block.GetIndexEntry());

        }

        var decompArray = decompData.ToArray();
        _keybIndexDecompSize = decompArray.Length;
        _keybIndex = MdxBlock.MdxCompress(decompArray, _compressionType);
        _keybIndexCompSize = _keybIndex.Length;
    }

    private void BuildRecordbIndex()
    {
        List<byte> indexData = [];
        foreach (var block in _recordBlocks) { indexData.AddRange(block.GetIndexEntry()); }
        _recordbIndex = [.. indexData];
        _recordbIndexSize = _recordbIndex.Length;
    }

    public void Write(Stream outfile)
    {
        WriteHeader(outfile);
        WriteKeySection(outfile);
        WriteRecordSection(outfile);
    }

    public void WriteHeader(Stream stream)
    {
        const string encrypted = "No";
        const string registerByStr = "";

        string headerString;
        if (_isMdd)
        {
            headerString = string.Format(
                "<Library_Data " +
                "GeneratedByEngineVersion=\"{0}\" " +
                "RequiredEngineVersion=\"{0}\" " +
                "Encrypted=\"{1}\" " +
                "Encoding=\"\" " +
                "Format=\"\" " +
                "CreationDate=\"{2}-{3}-{4}\" " +
                "KeyCaseSensitive=\"No\" " +
                "Stripkey=\"No\" " +
                "Description=\"{5}\" " +
                "Title=\"{6}\" " +
                "RegisterBy=\"{7}\" " +
                "/>\r\n\0",
                _version,
                encrypted,
                DateTime.Today.Year,
                DateTime.Today.Month,
                DateTime.Today.Day,
                EscapeHtml(_description),
                EscapeHtml(_title),
                registerByStr
            );
        }
        else
        {
            headerString = string.Format(
                "<Dictionary " +
                "GeneratedByEngineVersion=\"{0}\" " +
                "RequiredEngineVersion=\"{0}\" " +
                "Encrypted=\"{1}\" " +
                "Encoding=\"{2}\" " +
                "Format=\"Html\" " +
                "Stripkey=\"Yes\" " +
                "CreationDate=\"{3}-{4}-{5}\" " +
                "Compact=\"Yes\" " +
                "Compat=\"Yes\" " +
                "KeyCaseSensitive=\"No\" " +
                "Description=\"{6}\" " +
                "Title=\"{7}\" " +
                "DataSourceFormat=\"106\" " +
                "StyleSheet=\"\" " +
                "Left2Right=\"Yes\" " +
                "RegisterBy=\"{8}\" " +
                "/>\r\n\0",
                _version,
                encrypted,
                "UTF-8",
                DateTime.Today.Year,
                DateTime.Today.Month,
                DateTime.Today.Day,
                EscapeHtml(_description),
                EscapeHtml(_title),
                registerByStr
           );
        }
        // Console.WriteLine($"{headerString}");
        // Console.WriteLine($"header str: {headerString.Length}");

        // Encode to UTF-16 LE (must be identical to python .encode("utf_16_le")
        byte[] headerBytes = Encoding.Unicode.GetBytes(headerString);
        // Console.WriteLine($"header bytes: {headerBytes.Length}");
        // Console.WriteLine("        " + string.Join(" ", headerBytes.Select(b => b.ToString("X2"))));

        // Write header length (big-endian)
        byte[] lengthBytes = BitConverter.GetBytes((uint)headerBytes.Length);
        if (BitConverter.IsLittleEndian)
        {
            Array.Reverse(lengthBytes);
        }
        stream.Write(lengthBytes, 0, lengthBytes.Length);

        // Write header string
        stream.Write(headerBytes, 0, headerBytes.Length);

        // Write Adler32 checksum (little-endian)
        uint checksum = MdxBlock.Adler32(headerBytes);
        byte[] checksumBytes = BitConverter.GetBytes(checksum);
        if (!BitConverter.IsLittleEndian)
        {
            Array.Reverse(checksumBytes);
        }
        stream.Write(checksumBytes, 0, checksumBytes.Length);
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
        long keyblocksTotal = _keyBlocks.Sum(b => b.GetBlock().Length);

        if (_version == "2.0")
        {
            var preamble = new List<byte>();
            preamble.AddRange(Common.ToBigEndian((ulong)_keyBlocks.Count));
            preamble.AddRange(Common.ToBigEndian((ulong)_numEntries));
            preamble.AddRange(Common.ToBigEndian((ulong)_keybIndexDecompSize));
            preamble.AddRange(Common.ToBigEndian((ulong)_keybIndexCompSize));
            preamble.AddRange(Common.ToBigEndian((ulong)keyblocksTotal));

            var preambleArray = preamble.ToArray();
            var preambleChecksum = MdxBlock.Adler32(preambleArray);
            var checksumBytes = Common.ToBigEndian(preambleChecksum);

            outfile.Write(preambleArray, 0, preambleArray.Length);
            outfile.Write(checksumBytes, 0, checksumBytes.Length);
        }
        else
        {
            throw new NotImplementedException();
        }

        outfile.Write(_keybIndex, 0, _keybIndex.Length);

        foreach (var block in _keyBlocks)
        {
            var blockData = block.GetBlock();
            outfile.Write(blockData, 0, blockData.Length);
        }
    }

    private void WriteRecordSection(Stream outfile)
    {
        long recordblocksTotal = _recordBlocks.Sum(b => b.GetBlock().Length);

        var preamble = new List<byte>();

        if (_version == "2.0")
        {
            preamble.AddRange(Common.ToBigEndian((ulong)_recordBlocks.Count));
            preamble.AddRange(Common.ToBigEndian((ulong)_numEntries));
            preamble.AddRange(Common.ToBigEndian((ulong)_recordbIndexSize));
            preamble.AddRange(Common.ToBigEndian((ulong)recordblocksTotal));
        }
        else
        {
            throw new NotImplementedException();
        }

        var preambleArray = preamble.ToArray();
        outfile.Write(preambleArray, 0, preambleArray.Length);
        outfile.Write(_recordbIndex, 0, _recordbIndex.Length);

        foreach (var block in _recordBlocks)
        {
            var blockData = block.GetBlock();
            outfile.Write(blockData, 0, blockData.Length);
        }
    }
}
