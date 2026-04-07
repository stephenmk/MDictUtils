using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Web;
using System.Text.RegularExpressions;
using System.IO.Compression;

using D = System.Collections.Generic.List<Lib.MDictEntry>;

namespace Lib
{
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

    // Reader, actually
    public static class MDictPacker
    {
        // https://github.com/liuyug/mdict-utils/blob/master/mdict_utils/writer.py#L425
        public static D PackMdxTxt(string source, Encoding encoding = null, Action<int> callback = null, HashSet<string> keys = null)
        {
            encoding ??= Encoding.UTF8;
            D dictionary = [];
            List<string> sources = [];
            int nullLength = encoding.GetByteCount("\0");

            if (File.Exists(source))
                sources.Add(source);
            else if (Directory.Exists(source))
                sources.AddRange(Directory.GetFiles(source, "*.txt"));

            foreach (var path in sources)
            {
                byte[] fileBytes = File.ReadAllBytes(path);
                long pos = 0, offset = 0;
                string key = null;
                int lineNum = 0;

                long i = 0;
                while (i < fileBytes.Length)
                {
                    // Read a line (detect LF or CRLF)
                    long lineStart = i;
                    while (i < fileBytes.Length && fileBytes[i] != 10 && fileBytes[i] != 13) i++;
                    long lineEnd = i;

                    // Detect newline length
                    if (i < fileBytes.Length && fileBytes[i] == 13) i++;
                    if (i < fileBytes.Length && fileBytes[i] == 10) i++;

                    int lineLength = (int)(lineEnd - lineStart);
                    string line = encoding.GetString(fileBytes, (int)lineStart, lineLength).Trim();
                    lineNum++;

                    if (line.Length == 0)
                    {
                        if (key == null)
                            throw new Exception($"Error at line {lineNum}: {path}");
                        continue;
                    }

                    if (line == "</>")
                    {
                        if (key == null || offset == pos)
                            throw new Exception($"Error at line {lineNum}: {path}");

                        long size = offset - pos + nullLength;
                        if (keys?.Contains(key) != false)
                        {
                            dictionary.Add(new MDictEntry
                            {
                                Key = key,
                                Pos = pos,
                                Path = path,
                                Size = size
                            });
                        }
                        key = null;
                        callback?.Invoke(1);
                    }
                    else if (key == null)
                    {
                        key = line;
                        pos = i; // start of definition
                        offset = pos;
                    }
                    else
                    {
                        offset = i; // keep updating
                    }
                }
            }

            Console.WriteLine($"Read {dictionary.Count} entries");
            return dictionary;
        }
    }

    internal class OffsetTableEntry
    {
        public byte[] Key { get; set; }
        public byte[] KeyNull { get; set; }
        public int KeyLen { get; set; }
        public long Offset { get; set; }
        public byte[] RecordNull { get; set; }

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

            static void PrintPythonStyle(byte[] data)
            {
                Console.WriteLine("        " + string.Join(" ", data.Select(b => b.ToString("X2"))));

                string pythonStyle = "b'" + string.Concat(data.Select(b =>
                {
                    // Printable ASCII range: 0x20 (space) to 0x7E (~)
                    if (b >= 0x20 && b <= 0x7E)
                    {
                        if (b == (byte)'\\' || b == (byte)'\'')
                            return "\\" + (char)b;       // escape backslash and single quote
                        else
                            return ((char)b).ToString();
                    }
                    else
                    {
                        return "\\x" + b.ToString("x2");
                    }
                })) + "'";

                Console.WriteLine("        " + pythonStyle);
            }

            Console.WriteLine("[Debug] Calling MdxBlock...");

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
            // PrintPythonStyle(decompArray);

            _compData = MdxCompress(decompArray, compressionType);
            _compSize = _compData.Length;
            // Console.WriteLine($"[Debug] Compressed array length (_compSize): {_compSize}");

            _version = version;
            Console.WriteLine("[Debug] MdxBlock initialization complete.\n");
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

            static void PrintPythonStyle(byte[] data)
            {
                Console.WriteLine("        " + string.Join(" ", data.Select(b => b.ToString("X2"))));

                string pythonStyle = "b'" + string.Concat(data.Select(b =>
                {
                    // Printable ASCII range: 0x20 (space) to 0x7E (~)
                    if (b >= 0x20 && b <= 0x7E)
                    {
                        if (b == (byte)'\\' || b == (byte)'\'')
                            return "\\" + (char)b;       // escape backslash and single quote
                        else
                            return ((char)b).ToString();
                    }
                    else
                    {
                        return "\\x" + b.ToString("x2");
                    }
                })) + "'";

                Console.WriteLine("        " + pythonStyle);
            }


            // Compression type (little-endian)
            byte[] lend = BitConverter.GetBytes(compressionType); // <L in Python
            if (!BitConverter.IsLittleEndian) Array.Reverse(lend);

            uint adler = Adler32(data);
            byte[] adlerBytes = BitConverter.GetBytes(adler);
            if (BitConverter.IsLittleEndian) Array.Reverse(adlerBytes); // Python uses >L

            // Adler32 checksum (big-endian)
            byte[] header = [.. lend.Concat(adlerBytes)];

            using var ms = new MemoryStream();
            using (var z = new ZLibStream(ms, CompressionLevel.Optimal, leaveOpen: true))
            {
                z.Write(data, 0, data.Length);
            }
            var res = ms.ToArray();

            // PrintPythonStyle(data);
            // PrintPythonStyle(lend);
            // Console.WriteLine($"adler: {adler}");
            // PrintPythonStyle(adlerBytes);
            // Console.WriteLine($"header: {BitConverter.ToString(header)}");
            // PrintPythonStyle(final);

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
                result.AddRange(ToBigEndian((ulong)_compSize));
                result.AddRange(ToBigEndian((ulong)_decompSize));
            }
            else
            {
                throw new NotImplementedException();
            }

            return [.. result];
        }

        // We overwrite "return entry.RecordNull"
        protected override byte[] GetBlockEntry(OffsetTableEntry entry, string version)
        {
            // Read record from the file and store it
            byte[] record = ReadRecord(entry.FilePath, entry.RecordPos, (int)entry.RecordSize);
            entry.RecordNull = record;
            return record;
        }

        // Helper method: read from file and null-terminate
        private static byte[] ReadRecord(string filePath, long pos, int size)
        {
            if (size < 1) throw new ArgumentException("Size must be >= 1", nameof(size));

            byte[] record = new byte[size];
            using (var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read))
            {
                fs.Seek(pos, SeekOrigin.Begin);
                int bytesRead = fs.Read(record, 0, size - 1); // read record content
                record[bytesRead] = 0; // append null byte
            }

            return record;
        }


        public override int LenBlockEntry(OffsetTableEntry entry)
        {
            return (int)entry.RecordSize; // TODO: fix cast
        }

        private static byte[] ToBigEndian(ulong value)
        {
            var bytes = BitConverter.GetBytes(value);
            if (BitConverter.IsLittleEndian)
                Array.Reverse(bytes);
            return bytes;
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

            if (version == "2.0")
            {
                _firstKey = offsetTable[0].KeyNull;
                _lastKey = offsetTable[^1].KeyNull;
            }
            else
            {
                _firstKey = offsetTable[0].Key;
                _lastKey = offsetTable[^1].Key;
            }

            _firstKeyLen = offsetTable[0].KeyLen;
            _lastKeyLen = offsetTable[^1].KeyLen;
        }

        protected override byte[] GetBlockEntry(OffsetTableEntry entry, string version)
        {
            List<byte> result = [];

            if (version == "2.0")
            {
                result.AddRange(ToBigEndian((ulong)entry.Offset));
            }
            else
            {
                result.AddRange(ToBigEndian((uint)entry.Offset));
            }

            result.AddRange(entry.KeyNull);
            return [.. result];
        }

        public override int LenBlockEntry(OffsetTableEntry entry)
        {
            return 8 + entry.KeyNull.Length; // Approximate for version 2.0
        }

        public override byte[] GetIndexEntry()
        {
            // Console.WriteLine("Called GetIndexEntry on MDXKEYBLOCK");
            // Console.WriteLine($"    compSize {_compSize}; decompsize {_decompSize}");
            List<byte> result = [];

            if (_version == "2.0")
            {
                result.AddRange(ToBigEndian((ulong)_numEntries));
                result.AddRange(ToBigEndian((ushort)_firstKeyLen));
                result.AddRange(_firstKey);
                result.AddRange(ToBigEndian((ushort)_lastKeyLen));
                result.AddRange(_lastKey);
                result.AddRange(ToBigEndian((ulong)_compSize));
                result.AddRange(ToBigEndian((ulong)_decompSize));
            }
            else
            {
                throw new NotImplementedException();
            }

            return [.. result];
        }

        private static byte[] ToBigEndian(ulong value)
        {
            var bytes = BitConverter.GetBytes(value);
            if (BitConverter.IsLittleEndian)
                Array.Reverse(bytes);
            return bytes;
        }

        private static byte[] ToBigEndian(uint value)
        {
            var bytes = BitConverter.GetBytes(value);
            if (BitConverter.IsLittleEndian)
                Array.Reverse(bytes);
            return bytes;
        }

        private static byte[] ToBigEndian(ushort value)
        {
            var bytes = BitConverter.GetBytes(value);
            if (BitConverter.IsLittleEndian)
                Array.Reverse(bytes);
            return bytes;
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
        private readonly int _encodingLength;

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
                          string version = "2.0")
        {
            _numEntries = dictionary.Count;
            _title = title;
            _description = description;
            _blockSize = blockSize;
            _compressionType = compressionType;
            _version = version;

            // Set encoding
            encoding = encoding.ToLower();
            Debug.Assert(encoding == "utf8");
            if (encoding == "utf8" || encoding == "utf-8")
            {
                _encoding = Encoding.UTF8;
                _encodingLength = 1;
            }
            else if (encoding == "utf16" || encoding == "utf-16")
            {
                _encoding = Encoding.Unicode; // UTF-16 LE
                _encodingLength = 2;
            }
            else
            {
                throw new ArgumentException("Unknown encoding. Supported: utf8, utf16");
            }

            if (version != "2.0" && version != "1.2")
            {
                throw new ArgumentException("Unknown version. Supported: 2.0, 1.2");
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

        // private void BuildOffsetTableBase(D dictionary)
        // {
        //     var items = dictionary.OrderBy(kvp => kvp.Key).ToList();
        //
        //     _offsetTable = new List<OffsetTableEntry>();
        //     long offset = 0;
        //
        //     foreach (var item in items)
        //     {
        //         var keyEnc = _encoding.GetBytes(item.Key);
        //         var keyNull = _encoding.GetBytes(item.Key + "\0");
        //         var keyLen = keyEnc.Length / _encodingLength;
        //
        //         var recordNull = _encoding.GetBytes(item.Value + "\0");
        //
        //         _offsetTable.Add(new OffsetTableEntry
        //         {
        //             Key = keyEnc,
        //             KeyNull = keyNull,
        //             KeyLen = keyLen,
        //             RecordNull = recordNull,
        //             Offset = offset
        //         });
        //
        //         offset += recordNull.Length;
        //     }
        //
        //
        //
        //     _totalRecordLen = offset;
        // }

        private void BuildOffsetTable(D dictionary)
        {
            // [!"#$%&\'()*+,-./:;<=>?@[\\]^_`{|}~ ]+
            var regexStrip = new Regex($"[{Regex.Escape("!\"#$%&'()*+,-./:;<=>?@[\\]^_`{{|}}~")} ]+");

            var items = dictionary.ToList();

            items.Sort((a, b) =>
            {
                string k1 = regexStrip.Replace(a.Key.ToLower(), "");
                string k2 = regexStrip.Replace(b.Key.ToLower(), "");

                int cmp = string.Compare(k1, k2);
                if (cmp != 0) return cmp;

                // reverse length (longer first)
                if (k1.Length != k2.Length)
                    return k2.Length.CompareTo(k1.Length);

                // strip punctuation (approximation)
                k1 = k1.TrimEnd('.', ',', '!', '?', ':', ';');
                k2 = k2.TrimEnd('.', ',', '!', '?', ':', ';');

                return string.CompareOrdinal(k2, k1);
            });

            _offsetTable = [];
            long offset = 0;

            foreach (var item in items)
            {
                Console.WriteLine($"dict item: {item}");
                // _python_encoding = UTF8
                var keyEnc = Encoding.UTF8.GetBytes(item.Key);
                var keyNull = Encoding.UTF8.GetBytes(item.Key + "\0");
                var keyLen = keyEnc.Length; // encoding_length = 1

                var recordNull = Encoding.UTF8.GetBytes(item.Path);

                var tableEntry = new OffsetTableEntry
                {
                    Key = keyEnc,
                    KeyNull = keyNull,
                    KeyLen = keyLen,
                    RecordNull = recordNull,
                    Offset = offset,
                    RecordSize = item.Size,
                    RecordPos = item.Pos,
                    FilePath = item.Path
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
            //         if (valuePreview.Length > 40)
            //             valuePreview = valuePreview.Substring(0, 40) + "...";
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
                Console.WriteLine($"[split blocks] {ind} {t}");
                if (t != null)
                {

                    Console.WriteLine($"[split blocks] lenFunc {lenFunc(t)}");
                }

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
                    foreach (var entry in blockEntries)
                    {
                        Console.WriteLine($"[split flush] {entry}");
                    }
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

        private void BuildKeyBlocks() => _keyBlocks = SplitBlocks<MdxKeyBlock>(
                (entries, comp, ver) => new MdxKeyBlock(entries, comp, ver),
                (entry) => 8 + entry.KeyNull.Length
            );

        private void BuildRecordBlocks() => _recordBlocks = SplitBlocks<MdxRecordBlock>(
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

            string headerString = string.Format(
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
                // _encoding,
                "UTF-8",
                DateTime.Today.Year,
                DateTime.Today.Month,
                DateTime.Today.Day,
                HttpUtility.HtmlAttributeEncode(_description),
                HttpUtility.HtmlAttributeEncode(_title),
                registerByStr
            );
            Console.WriteLine($"{headerString}");
            Console.WriteLine($"header str: {headerString.Length}");

            // Encode to UTF-16 LE (must be identical to python .encode("utf_16_le")
            byte[] headerBytes = Encoding.Unicode.GetBytes(headerString);
            Console.WriteLine($"header bytes: {headerBytes.Length}");
            Console.WriteLine("        " + string.Join(" ", headerBytes.Select(b => b.ToString("X2"))));

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

        private void WriteKeySection(Stream outfile)
        {
            long keyblocksTotal = _keyBlocks.Sum(b => b.GetBlock().Length);

            if (_version == "2.0")
            {
                var preamble = new List<byte>();
                preamble.AddRange(ToBigEndian((ulong)_keyBlocks.Count));
                preamble.AddRange(ToBigEndian((ulong)_numEntries));
                preamble.AddRange(ToBigEndian((ulong)_keybIndexDecompSize));
                preamble.AddRange(ToBigEndian((ulong)_keybIndexCompSize));
                preamble.AddRange(ToBigEndian((ulong)keyblocksTotal));

                var preambleArray = preamble.ToArray();
                var preambleChecksum = MdxBlock.Adler32(preambleArray);
                var checksumBytes = ToBigEndian(preambleChecksum);

                outfile.Write(preambleArray, 0, preambleArray.Length);
                outfile.Write(checksumBytes, 0, checksumBytes.Length);
            }
            else
            {
                var preamble = new List<byte>();
                preamble.AddRange(ToBigEndian((uint)_keyBlocks.Count));
                preamble.AddRange(ToBigEndian((uint)_numEntries));
                preamble.AddRange(ToBigEndian((uint)_keybIndexDecompSize));
                preamble.AddRange(ToBigEndian((uint)keyblocksTotal));

                var preambleArray = preamble.ToArray();
                outfile.Write(preambleArray, 0, preambleArray.Length);
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
                preamble.AddRange(ToBigEndian((ulong)_recordBlocks.Count));
                preamble.AddRange(ToBigEndian((ulong)_numEntries));
                preamble.AddRange(ToBigEndian((ulong)_recordbIndexSize));
                preamble.AddRange(ToBigEndian((ulong)recordblocksTotal));
            }
            else
            {
                preamble.AddRange(ToBigEndian((uint)_recordBlocks.Count));
                preamble.AddRange(ToBigEndian((uint)_numEntries));
                preamble.AddRange(ToBigEndian((uint)_recordbIndexSize));
                preamble.AddRange(ToBigEndian((uint)recordblocksTotal));
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

        private static byte[] ToBigEndian(ulong value)
        {
            var bytes = BitConverter.GetBytes(value);
            if (BitConverter.IsLittleEndian)
                Array.Reverse(bytes);
            return bytes;
        }

        private static byte[] ToBigEndian(uint value)
        {
            var bytes = BitConverter.GetBytes(value);
            if (BitConverter.IsLittleEndian)
                Array.Reverse(bytes);
            return bytes;
        }
    }
}
