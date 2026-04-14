using System.Buffers;
using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;

namespace Lib;

public partial class MDict
{
    private static readonly ArrayPool<byte> _arrayPool = ArrayPool<byte>.Shared;
    protected string _fname;
    protected Encoding _encoding;
    // protected byte[] _encryptedKey;
    protected float _version;
    protected int _numberWidth;
    // protected string _numberFormat;
    protected int _encrypt;
    protected Dictionary<string, (string, string)> _stylesheet = [];
    protected List<(long keyId, string keyText)> _keyList;
    protected long _keyBlockOffset;
    protected long _recordBlockOffset;
    protected int _numEntries;

    protected readonly Dictionary<string, string> _header;

    // Ignore passcode for now
    public MDict(string fname, Encoding encoding)
    {
        _fname = fname;
        _encoding = encoding;
        _header = ReadHeader();
        _version = 2.0F; // hardcode it, useless anyway
        _keyList = ReadKeys();
    }

    public int Count => _numEntries;
    public Dictionary<string, string> Header => _header;

    public IEnumerable<(string, byte[])> Items() => ReadRecords();

    // overwriten for MDX
    public virtual byte[] TreatRecordData(ReadOnlySpan<byte> data) => data.ToArray();

    // _read_records_v1v2
    // https://github.com/liuyug/mdict-utils/blob/64e15b99aca786dbf65e5a2274f85547f8029f2e/mdict_utils/base/readmdict.py#L563
    protected IEnumerable<(string, byte[])> ReadRecords()
    {
        using var fs = new FileStream(_fname, FileMode.Open, FileAccess.Read);
        fs.Seek(_recordBlockOffset, SeekOrigin.Begin);
        using var br = new BinaryReader(fs);

        // Read record block header
        long numRecordBlocks = ReadNumber(br);
        long numEntries = ReadNumber(br);
        if (numEntries != _numEntries)
            throw new InvalidDataException($"Number of entries {numEntries} does not match _numEntries {_numEntries}.");

        long recordBlockInfoSize = ReadNumber(br);
        long recordBlockSize = ReadNumber(br);

        // Read record block info
        List<(long, long)> recordBlockInfoList = new((int)numRecordBlocks);
        long sizeCounter = 0;
        long maxCompressedSize = 0;
        long maxDecompressedSize = 0;

        for (int i = 0; i < numRecordBlocks; i++)
        {
            long compressedSize = ReadNumber(br);
            long decompressedSize = ReadNumber(br);
            maxCompressedSize = long.Max(compressedSize, maxCompressedSize);
            maxDecompressedSize = long.Max(decompressedSize, maxDecompressedSize);
            recordBlockInfoList.Add((compressedSize, decompressedSize));
            sizeCounter += _numberWidth * 2; // two numbers per block
        }
        if (sizeCounter != recordBlockInfoSize)
            throw new InvalidDataException("Record block info size mismatch.");

        // List<(string, byte[])> records = new(recordBlockInfoList.Count);
        long offset = 0;
        int keyIndex = 0;
        sizeCounter = 0;

        var compressedBuffer = _arrayPool.Rent((int)maxCompressedSize);
        var decompressedBuffer = _arrayPool.Rent((int)maxDecompressedSize);

        foreach (var (compressedSize, decompSize) in recordBlockInfoList)
        {
            var compressedBlock = compressedBuffer.AsSpan(..(int)compressedSize);
            br.ReadExactly(compressedBlock);

            int size = Convert.ToInt32(decompSize);
            var recordBlock = decompressedBuffer.AsSpan(..size);

            DecodeBlock(compressedBlock, recordBlock);
            // Console.WriteLine(
            //     $"[ReadRecords]\ncompressedBlock = {BitConverter.ToString(compressedBlock)}\n" +
            //     $"recordBlock = {Encoding.UTF8.GetString(recordBlock)}"
            // );

            while (keyIndex < _keyList.Count)
            {
                var (recordStart, keyText) = _keyList[keyIndex];
                // Console.WriteLine($"[ReadRecords] recordStart {recordStart}, keyText {keyText}");

                // If the current record starts beyond this block, break
                if (recordStart - offset >= size)
                {
                    break;
                }

                long recordEnd = (keyIndex < _keyList.Count - 1)
                    ? _keyList[keyIndex + 1].keyId
                    : size + offset;

                keyIndex++;
                int start = (int)(recordStart - offset);
                int length = (int)(recordEnd - offset - start);
                // Must create a span again because the runtime will complain
                // if spans are reused across the `yield return` boundary.
                var data = decompressedBuffer.AsSpan(..size).Slice(start, length);

                yield return (keyText, TreatRecordData(data));
            }

            offset += size;
            sizeCounter += compressedSize;
        }

        _arrayPool.Return(compressedBuffer);
        _arrayPool.Return(decompressedBuffer);

        if (sizeCounter != recordBlockSize)
            throw new InvalidDataException("Record block size mismatch.");
    }

    // def _read_number(self, f):
    //     return unpack(self._number_format, f.read(self._number_width))[0]
    protected long ReadNumber(BinaryReader br)
    {
        // _numberWidth is either 4 or 8
        Span<byte> bytes = stackalloc byte[_numberWidth];
        br.ReadExactly(bytes);
        return (_numberWidth == 4)
            ? Common.ReadBigEndian<uint>(bytes, true)
            : (long)Common.ReadBigEndian<ulong>(bytes, true);
    }

    protected static long ReadNumber(ReadOnlySpan<byte> buffer)
        => (buffer.Length == 4)
        ? Common.ReadBigEndian<uint>(buffer, true)
        : (long)Common.ReadBigEndian<ulong>(buffer, true);

    [GeneratedRegex(@"(\w+)=""(.*?)""", RegexOptions.Singleline)]
    private static partial Regex HeaderKeyValuesRegex { get; }

    protected Dictionary<string, string> ParseHeader(string headerText)
    {
        Dictionary<string, string> dict = [];
        foreach (Match match in HeaderKeyValuesRegex.Matches(headerText))
        {
            dict[match.Groups[1].Value] = UnescapeEntities(match.Groups[2].Value);
        }
        return dict;
    }

    protected virtual string UnescapeEntities(string value)
    {
        return value.Replace("&lt;", "<").Replace("&gt;", ">").Replace("&quot;", "\"").Replace("&amp;", "&");
    }

    protected Dictionary<string, string> ReadHeader()
    {
        using var fs = new FileStream(_fname, FileMode.Open, FileAccess.Read);
        using var br = new BinaryReader(fs);

        int headerBytesSize = Common.ReadBigEndian<int>(br.ReadBytes(4), false);
        byte[] headerBytes = br.ReadBytes(headerBytesSize);

        // Adler32 checksum of header
        uint adler32 = Common.ReadLittleEndian<uint>(br.ReadBytes(4), true);

        if (adler32 != Common.Adler32(headerBytes))
            throw new InvalidDataException("Header Adler32 checksum mismatch.");

        _keyBlockOffset = fs.Position;

        // decode header text
        string headerText;
        if (headerBytes.Length >= 2 && headerBytes[^2] == 0 && headerBytes[^1] == 0)
        {
            headerText = Encoding.Unicode.GetString(headerBytes, 0, headerBytes.Length - 2);
        }
        else
        {
            headerText = Encoding.UTF8.GetString(headerBytes, 0, headerBytes.Length - 1);
        }

        // parse XML-like tags into dictionary
        var headerTag = ParseHeader(headerText);

        // TODO: detect encoding?

        // encryption flag
        if (headerTag.TryGetValue("Encrypted", out var encryptedValue))
        {
            if (encryptedValue.Equals("No", StringComparison.OrdinalIgnoreCase))
            {
                _encrypt = 0;
            }
            else if (encryptedValue.Equals("Yes", StringComparison.OrdinalIgnoreCase))
            {
                _encrypt = 1;
            }
            else
            {
                _encrypt = int.Parse(encryptedValue);
            }
        }
        else
        {
            _encrypt = 0;
        }

        if (_encrypt != 0)
        {
            Console.WriteLine($"Encryption detected. Kind: {_encrypt}");
        }
        if (_encrypt != 0 && _encrypt != 2)
        {
            throw new InvalidDataException($"Encryted data with level {_encrypt}, unsupported");
        }


        // TODO: stylesheet parsing
        _stylesheet = [];
        if (headerTag.TryGetValue("StyleSheet", out var styleSheetValue))
        {
            string[] lines = styleSheetValue.Split(["\r\n", "\n"], StringSplitOptions.None);
            for (int i = 0; i + 2 < lines.Length; i += 3)
            {
                _stylesheet[lines[i]] = (lines[i + 1], lines[i + 2]);
            }
        }

        // version and number width
        _version = float.Parse(headerTag["GeneratedByEngineVersion"]);
        if (_version < 2.0)
        {
            _numberWidth = 4;
        }
        else
        {
            _numberWidth = 8;
            if (_version >= 3.0)
                _encoding = Encoding.UTF8;
        }

        return headerTag;
    }

    // _read_keys_v1v2
    public List<(long, string)> ReadKeys()
    {
        using FileStream f = new(_fname, FileMode.Open, FileAccess.Read);
        f.Seek(_keyBlockOffset, SeekOrigin.Begin);

        int numBytes = (_version >= 2.0) ? 8 * 5 : 4 * 4;
        Span<byte> block = stackalloc byte[numBytes];
        f.ReadExactly(block);

        if ((_encrypt & 1) != 0)
        {
            throw new InvalidDataException("Encryted data with level 1, unsupported");
        }

        var r = new SpanReader<byte>(block) { ReadSize = _numberWidth };

        long numKeyBlocks = ReadNumber(r.Read());
        _numEntries = (int)ReadNumber(r.Read());
        long keyBlockInfoDecompSize = (_version >= 2.0) ? ReadNumber(r.Read()) : 0;
        int keyBlockInfoSize = (int)ReadNumber(r.Read());
        int keyBlockSize = (int)ReadNumber(r.Read());

        if (_version >= 2.0)
        {
            Span<byte> adlerBytes = stackalloc byte[4];
            f.ReadExactly(adlerBytes);
            uint adler32 = Common.ReadBigEndian<uint>(adlerBytes, true);
            Debug.Assert(adler32 == Common.Adler32(block));
        }

        // Read key block info
        byte[] buffer = _arrayPool.Rent(keyBlockInfoSize);
        var keyBlockInfo = buffer.AsSpan(..keyBlockInfoSize);
        f.ReadExactly(keyBlockInfo);
        List<(long, long)> keyBlockInfoList = DecodeKeyBlockInfo(keyBlockInfo, keyBlockInfoDecompSize);
        Debug.Assert(numKeyBlocks == keyBlockInfoList.Count);
        _arrayPool.Return(buffer);

        // Read and extract key block
        buffer = _arrayPool.Rent(keyBlockSize);
        var keyBlockCompressed = buffer.AsSpan(..keyBlockSize);
        f.ReadExactly(keyBlockCompressed);
        List<(long, string)> keyList = DecodeKeyBlock(keyBlockCompressed, keyBlockInfoList);
        _arrayPool.Return(buffer);

        _recordBlockOffset = f.Position;

        return keyList;
    }

    // _decode_key_block_info
    protected List<(long, long)> DecodeKeyBlockInfo(ReadOnlySpan<byte> keyBlockInfoCompressed, long decompSize)
    {
        ReadOnlySpan<byte> keyBlockInfo;

        if (_version >= 2)
        {
            // SAFETY: check header bytes
            if (keyBlockInfoCompressed.Length < 4)
            {
                throw new InvalidDataException($"""
                    Key block info is too short.
                    Expected at least 4 bytes.
                    Got {keyBlockInfoCompressed.Length} bytes.
                    """);
            }

            if (keyBlockInfoCompressed is not [0x02, 0x00, 0x00, 0x00, ..])
            {
                var actual = string.Join(
                    separator: ", ",
                    values: keyBlockInfoCompressed[..4]
                        .ToArray()
                        .Select(static b => $"{b:X2}"));

                throw new InvalidDataException($"""
                    Key block info header mismatch.
                    Expected: [0x02, 0x00, 0x00, 0x00, ..]
                    Actual:   [{actual}, ..]"
                    """);
            }

            ReadOnlySpan<byte> compressed;
            if ((_encrypt & 0x02) != 0)
            {
                // decrypt if needed
                // https://github.com/liuyug/mdict-utils/blob/master/mdict_utils/base/readmdict.py#L199
                //
                // key = ripemd128(key_block_info_compressed[4:8] + pack(b'<L', 0x3695))
                // key_block_info_compressed = key_block_info_compressed[:8] + _fast_decrypt(key_block_info_compressed[8:], key)
                Span<byte> message = stackalloc byte[8];
                Span<byte> hash = stackalloc byte[16]; // RIPEMD-128 is 16 bytes
                keyBlockInfoCompressed[4..8].CopyTo(message[..4]);
                Common.ToLittleEndian(0x3695u, message[4..8]);

                var hashSize = Ripemd128.ComputeHash(message, hash);
                var key = hash[..hashSize];

                var decryptedSize = keyBlockInfoCompressed.Length - 8;
                byte[] decrypted = _arrayPool.Rent(decryptedSize);
                var result = decrypted.AsSpan(..decryptedSize);

                Ripemd128.FastDecrypt(keyBlockInfoCompressed[8..], key, result);
                byte[] bytes = [.. keyBlockInfoCompressed[..8], .. result];
                compressed = bytes;
                _arrayPool.Return(decrypted);
            }
            else
            {
                compressed = keyBlockInfoCompressed;
            }

            // decompress zlib
            var data = compressed[8..];
            var buffer = new byte[(int)decompSize];
            ZLibCompression.Decompress(data, buffer);
            keyBlockInfo = buffer;

            Span<byte> checksumBuffer = stackalloc byte[4];
            compressed[4..8].CopyTo(checksumBuffer);

            uint adler32 = Common.ReadBigEndian<uint>(checksumBuffer, true);
            if (adler32 != Common.Adler32(keyBlockInfo))
                throw new InvalidDataException("Key block info Adler32 mismatch.");
        }
        else
        {
            keyBlockInfo = keyBlockInfoCompressed;
        }

        // decode key block info
        List<(long, long)> keyBlockInfoList = [];
        int numEntries = 0;
        int i = 0;
        int byteWidth = (_version >= 2) ? 2 : 1;
        int textTerm = (_version >= 2) ? 1 : 0;

        while (i < keyBlockInfo.Length)
        {
            // number of entries in current key block
            numEntries += (int)ReadNumber(keyBlockInfo.Slice(i, _numberWidth));
            i += _numberWidth;

            int textHeadSize = (byteWidth == 2)
                ? Common.ReadBigEndian<ushort>(keyBlockInfo.Slice(i, 2), true)
                : keyBlockInfo[i];
            i += byteWidth;

            if (_encoding != Encoding.Unicode)
                i += textHeadSize + textTerm;
            else
                i += (textHeadSize + textTerm) * 2;

            int textTailSize = (byteWidth == 2)
                ? Common.ReadBigEndian<ushort>(keyBlockInfo.Slice(i, 2), true)
                : keyBlockInfo[i];
            i += byteWidth;

            if (_encoding != Encoding.Unicode)
                i += textTailSize + textTerm;
            else
                i += (textTailSize + textTerm) * 2;

            long keyBlockCompressedSize = ReadNumber(keyBlockInfo.Slice(i, _numberWidth));
            i += _numberWidth;
            long keyBlockDecompressedSize = ReadNumber(keyBlockInfo.Slice(i, _numberWidth));
            i += _numberWidth;

            keyBlockInfoList.Add((keyBlockCompressedSize, keyBlockDecompressedSize));
        }

        Debug.Assert(numEntries == _numEntries);

        return keyBlockInfoList;
    }

    // _decode_key_block
    protected List<(long, string)> DecodeKeyBlock(ReadOnlySpan<byte> keyBlockCompressed, List<(long, long)> keyBlockInfoList)
    {
        if (keyBlockInfoList is [])
            return [];

        List<(long, string)> keyList = new(keyBlockInfoList.Count);
        long maxDecompSize = keyBlockInfoList.Max(static k => k.Item2);
        byte[] buffer = _arrayPool.Rent((int)maxDecompSize);
        int offset = 0;

        foreach (var (compSize, decompSize) in keyBlockInfoList)
        {
            int size = Convert.ToInt32(compSize);
            var compressed = keyBlockCompressed.Slice(offset, size);
            var decompressed = buffer.AsSpan(..(int)decompSize);
            DecodeBlock(compressed, decompressed);
            keyList.AddRange(SplitKeyBlock(decompressed));
            offset += size;
        }
        _arrayPool.Return(buffer);
        return keyList;
    }

    protected static void DecodeBlock(ReadOnlySpan<byte> input, Span<byte> output)
    {
        Debug.Assert(input.Length >= 8, "Block too small");

        uint info = Common.ReadLittleEndian<uint>(input[..4], true);
        int compressionMethod = (int)(info & 0xF);
        // int encryptionMethod = (int)((info >> 4) & 0xF);
        int encryptionSize = (int)((info >> 8) & 0xFF);

        // ---- adler32 (big-endian) ----
        Span<byte> adlerBytes = stackalloc byte[4];
        input[4..8].CopyTo(adlerBytes);
        uint adler32 = Common.ReadBigEndian<uint>(adlerBytes, true);

        // ---- encryption key ---- (SKIP)
        var data = input[8..];
        Debug.Assert(encryptionSize <= data.Length, "Invalid encryption size");

        // ---- decrypt ---- (assume no encryption)
        Debug.Assert(compressionMethod == 2);
        ZLibCompression.Decompress(data, output);

        Debug.Assert(adler32 == Common.Adler32(output), "Adler32 mismatch after decompression");
    }

    public List<(long, string)> SplitKeyBlock(ReadOnlySpan<byte> keyBlock)
    {
        Debug.Assert(keyBlock.Length >= _numberWidth, "Key block is too short");

        List<(long, string)> keyList = [];
        int keyStartIndex = 0;

        Span<byte> idBytesBuffer = stackalloc byte[8];

        ReadOnlySpan<byte> utf16delimiter = [0x00, 0x00];
        ReadOnlySpan<byte> utf8delimiter = [0x00];

        while (keyStartIndex < keyBlock.Length)
        {
            Debug.Assert(keyStartIndex + _numberWidth <= keyBlock.Length, "Unexpected end of key block while reading key ID");

            var idBytes = idBytesBuffer[.._numberWidth];
            keyBlock.Slice(keyStartIndex, _numberWidth).CopyTo(idBytes);

            long keyId = (_numberWidth == 4)
                ? Common.ReadBigEndian<int>(idBytes, false)
                : Common.ReadBigEndian<long>(idBytes, false);

            var delimiter = _encoding == Encoding.Unicode
                ? utf16delimiter
                : utf8delimiter;
            int width = delimiter.Length;

            // Find the end of the key text
            int i = keyStartIndex + _numberWidth;
            int keyEndIndex = -1;
            while (i <= keyBlock.Length - width)
            {
                if (keyBlock.Slice(i, width).SequenceEqual(delimiter))
                {
                    keyEndIndex = i;
                    break;
                }
                i += width;
            }
            Debug.Assert(keyEndIndex != -1, "Delimiter not found in key block");

            // Extract key text
            var keyTextBytes = keyBlock[(keyStartIndex + _numberWidth)..keyEndIndex];

            // Decode to string (ignore errors like Python)
            // Similar to TreatRecordData in the trim?
            string keyText = _encoding.GetString(keyTextBytes).Trim('\0').Trim();
            keyList.Add((keyId, keyText));
            keyStartIndex = keyEndIndex + width;

            Debug.Assert(!string.IsNullOrEmpty(keyText), "Key text is empty");
            Debug.Assert(keyStartIndex <= keyBlock.Length, "Key start index past end of block");
        }

        Debug.Assert(keyList.Count > 0, "No keys were found in the key block");

        return keyList;
    }
}

public class MDD(string fname) : MDict(fname, Encoding.Unicode)
{
}

public class MDX(string fname) : MDict(fname, Encoding.UTF8)
{
    public override byte[] TreatRecordData(ReadOnlySpan<byte> data)
    {
        if (_encoding != Encoding.UTF8)
        {
            // For Encoding.Unicode, trimming the \0 bytes
            // might(?) accidentally cut a character in half
            // (since UTF-16 uses two bytes per character).
            throw new InvalidOperationException();
        }

        return data.Trim((byte)'\0').ToArray();
    }
}
