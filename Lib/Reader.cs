using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using System.Linq;

namespace Lib;

public partial class MDict
{
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
    public virtual byte[] TreatRecordData(byte[] data) => data;

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
        List<(long, long)> recordBlockInfoList = [];
        long sizeCounter = 0;
        for (int i = 0; i < numRecordBlocks; i++)
        {
            long compressedSize = ReadNumber(br);
            long decompressedSize = ReadNumber(br);
            recordBlockInfoList.Add((compressedSize, decompressedSize));
            sizeCounter += _numberWidth * 2; // two numbers per block
        }
        if (sizeCounter != recordBlockInfoSize)
            throw new InvalidDataException("Record block info size mismatch.");

        long offset = 0;
        int keyIndex = 0;
        sizeCounter = 0;

        foreach (var (compressedSize, decompressedSize) in recordBlockInfoList)
        {
            byte[] compressedBlock = br.ReadBytes((int)compressedSize);
            byte[] recordBlock = DecodeBlock(compressedBlock);
            // Console.WriteLine(
            //     $"[ReadRecords]\ncompressedBlock = {BitConverter.ToString(compressedBlock)}\n" +
            //     $"recordBlock = {Encoding.UTF8.GetString(recordBlock)}"
            // );

            while (keyIndex < _keyList.Count)
            {
                var (recordStart, keyText) = _keyList[keyIndex];
                // Console.WriteLine($"[ReadRecords] recordStart {recordStart}, keyText {keyText}");

                // If the current record starts beyond this block, break
                if (recordStart - offset >= recordBlock.Length)
                {
                    break;
                }

                long recordEnd = (keyIndex < _keyList.Count - 1)
                    ? _keyList[keyIndex + 1].keyId
                    : recordBlock.Length + offset;

                keyIndex++;
                int start = (int)(recordStart - offset);
                int length = (int)(recordEnd - offset - start);
                byte[] data = new byte[length];
                Array.Copy(recordBlock, start, data, 0, length);

                yield return (keyText, TreatRecordData(data));
            }

            offset += recordBlock.Length;
            sizeCounter += compressedSize;
        }

        if (sizeCounter != recordBlockSize)
            throw new InvalidDataException("Record block size mismatch.");
    }

    // def _read_number(self, f):
    //     return unpack(self._number_format, f.read(self._number_width))[0]
    protected long ReadNumber(BinaryReader br)
    {
        // _numberWidth is either 4 or 8
        Span<byte> bytes = stackalloc byte[_numberWidth];
        for (int i = 0; i < _numberWidth; i++)
        {
            bytes[i] = br.ReadByte();
        }
        return (_numberWidth == 4)
            ? Common.ReadUInt32BigEndian(bytes)
            : (long)Common.ReadUInt64BigEndian(bytes);
    }

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

        int headerBytesSize = Common.ReadInt32BigEndian(br);
        byte[] headerBytes = br.ReadBytes(headerBytesSize);

        // 4 bytes: Adler32 checksum of header, little endian
        uint adler32 = br.ReadUInt32(); // Little-endian by default in BinaryReader
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
        byte[] block = new byte[numBytes];
        _ = f.Read(block, 0, numBytes);

        if ((_encrypt & 1) != 0)
        {
            throw new NotImplementedException();
        }

        using MemoryStream sf = new(block);
        using BinaryReader reader = new(sf);

        long numKeyBlocks = ReadNumber(reader);
        _numEntries = (int)ReadNumber(reader);
        long keyBlockInfoDecompSize = (_version >= 2.0) ? ReadNumber(reader) : 0;
        long keyBlockInfoSize = ReadNumber(reader);
        long keyBlockSize = ReadNumber(reader);

        if (_version >= 2.0)
        {
            Span<byte> adlerBytes = stackalloc byte[4];
            _ = f.Read(adlerBytes);
            uint adler32 = Common.ReadUInt32BigEndian(adlerBytes);
            Debug.Assert(adler32 == Common.Adler32(block));
        }

        // Read key block info
        byte[] keyBlockInfo = new byte[keyBlockInfoSize];
        _ = f.Read(keyBlockInfo, 0, keyBlockInfo.Length);
        List<(long, long)> keyBlockInfoList = DecodeKeyBlockInfo(keyBlockInfo);
        Debug.Assert(numKeyBlocks == keyBlockInfoList.Count);

        // Read and extract key block
        byte[] keyBlockCompressed = new byte[keyBlockSize];
        _ = f.Read(keyBlockCompressed, 0, keyBlockCompressed.Length);
        List<(long, string)> keyList = DecodeKeyBlock(keyBlockCompressed, keyBlockInfoList);

        _recordBlockOffset = f.Position;

        return keyList;
    }

    // _decode_key_block_info
    protected List<(long, long)> DecodeKeyBlockInfo(byte[] keyBlockInfoCompressed)
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
                    values: keyBlockInfoCompressed
                        .Take(4)
                        .Select(static b => $"{b:X2}"));

                throw new InvalidDataException($"""
                    Key block info header mismatch.
                    Expected: [0x02, 0x00, 0x00, 0x00, ..]
                    Actual:   [{actual}, ..]"
                    """);
            }

            if ((_encrypt & 0x02) != 0)
            {
                throw new InvalidDataException("Encryted data, unsupported");
            }

            // decompress zlib
            keyBlockInfo = DecompressZlib([.. keyBlockInfoCompressed.AsSpan(8..)]);

            uint adler32 = Common.ReadUInt32BigEndian(keyBlockInfoCompressed);
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
            numEntries += (int)ReadNumber(keyBlockInfo, i, _numberWidth);
            i += _numberWidth;

            int textHeadSize = (byteWidth == 2)
                ? Common.ReadUInt16BigEndian(keyBlockInfo, i)
                : keyBlockInfo[i];
            i += byteWidth;

            if (_encoding != Encoding.Unicode)
                i += textHeadSize + textTerm;
            else
                i += (textHeadSize + textTerm) * 2;

            int textTailSize = (byteWidth == 2)
                ? Common.ReadUInt16BigEndian(keyBlockInfo, i)
                : keyBlockInfo[i];
            i += byteWidth;

            if (_encoding != Encoding.Unicode)
                i += textTailSize + textTerm;
            else
                i += (textTailSize + textTerm) * 2;

            long keyBlockCompressedSize = ReadNumber(keyBlockInfo, i, _numberWidth);
            i += _numberWidth;
            long keyBlockDecompressedSize = ReadNumber(keyBlockInfo, i, _numberWidth);
            i += _numberWidth;

            keyBlockInfoList.Add((keyBlockCompressedSize, keyBlockDecompressedSize));
        }

        Debug.Assert(numEntries == _numEntries);

        return keyBlockInfoList;
    }

    static private byte[] DecompressZlib(byte[] data)
    {
        using var input = new MemoryStream(data);
        using var z = new ZLibStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream();
        z.CopyTo(output);
        return output.ToArray();
    }

    static private long ReadNumber(ReadOnlySpan<byte> buffer, int offset, int numberWidth)
    {
        // numberWidth should always be 4 or 8
        Span<byte> slice = stackalloc byte[numberWidth];
        buffer.Slice(offset, numberWidth).CopyTo(slice);
        return (numberWidth == 4)
            ? Common.ReadUInt32BigEndian(slice)
            : (long)Common.ReadUInt64BigEndian(slice);
    }

    // _decode_key_block
    protected List<(long, string)> DecodeKeyBlock(byte[] keyBlockCompressed, List<(long, long)> keyBlockInfoList)
    {
        List<(long, string)> keyList = [];
        int offset = 0;
        foreach (var (compSize, decompSize) in keyBlockInfoList)
        {
            byte[] block = new byte[compSize];
            // key_block_compressed[offset:offset+compSize]
            Array.Copy(keyBlockCompressed, offset, block, 0, compSize);
            byte[] decompressed = DecodeBlock(block);
            keyList.AddRange(SplitKeyBlock(decompressed));
            offset += (int)compSize;
        }
        return keyList;
    }

    // decompressedSize is only used for compression_method = 1.
    // We only deal with 0, so don't pass it as an argument.
    protected static byte[] DecodeBlock(ReadOnlySpan<byte> block)
    {
        Debug.Assert(block.Length >= 8, "Block too small");

        uint info = BitConverter.ToUInt32(block); // little-endian
        int compressionMethod = (int)(info & 0xF);
        // int encryptionMethod = (int)((info >> 4) & 0xF);
        int encryptionSize = (int)((info >> 8) & 0xFF);

        // ---- adler32 (big-endian) ----
        Span<byte> adlerBytes = stackalloc byte[4];
        block[4..8].CopyTo(adlerBytes);
        uint adler32 = BitConverter.ToUInt32(Common.ToBigEndian(adlerBytes));

        // ---- encryption key ---- (SKIP)

        byte[] data = new byte[block.Length - 8];
        block[8..].CopyTo(data);

        Debug.Assert(encryptionSize <= data.Length, "Invalid encryption size");

        // ---- decrypt ---- (assume no encryption)
        var decryptedBlock = data;

        Debug.Assert(compressionMethod == 2);
        var decompressedBlock = DecompressZlib(decryptedBlock);

        Debug.Assert(adler32 == Common.Adler32(decompressedBlock), "Adler32 mismatch after decompression");

        return decompressedBlock;
    }

    public List<(long, string)> SplitKeyBlock(ReadOnlySpan<byte> keyBlock)
    {
        Debug.Assert(keyBlock.Length >= _numberWidth, "Key block is too short");

        List<(long, string)> keyList = [];
        int keyStartIndex = 0;

        Span<byte> idBytesBuffer = stackalloc byte[8];

        ReadOnlySpan<byte> unicodeDelimiter = [0x00, 0x00];
        ReadOnlySpan<byte> otherDelimiter = [0x00];

        while (keyStartIndex < keyBlock.Length)
        {
            Debug.Assert(keyStartIndex + _numberWidth <= keyBlock.Length, "Unexpected end of key block while reading key ID");

            var idBytes = idBytesBuffer[.._numberWidth];
            keyBlock.Slice(keyStartIndex, _numberWidth).CopyTo(idBytes);

            long keyId = (_numberWidth == 4)
                ? Common.ReadInt32BigEndian(idBytes)
                : Common.ReadInt64BigEndian(idBytes);

            var delimiter = _encoding == Encoding.Unicode
                ? unicodeDelimiter
                : otherDelimiter;
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
    public override byte[] TreatRecordData(byte[] data)
    {
        string text = _encoding.GetString(data);
        text = text.Trim('\0');
        return Encoding.UTF8.GetBytes(text);
    }
}
