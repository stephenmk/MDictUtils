using System.Diagnostics;
using System.Text;

namespace Lib.BuildModels;

internal class MdxKeyBlock : MdxBlock
{
    private readonly int _numEntries;
    private readonly ImmutableArray<byte> _firstKey;
    private readonly ImmutableArray<byte> _lastKey;
    private readonly int _firstKeyLen;
    private readonly int _lastKeyLen;

    public override string ToString()
    {
        var _encoding = Encoding.UTF8;
        string firstKeyStr = _encoding.GetString(_firstKey.AsSpan(.._firstKeyLen));
        string lastKeyStr = _encoding.GetString(_lastKey.AsSpan(.._lastKeyLen));
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
        Common.ToBigEndian((ulong)_blockData.CompressedSize, r.Read(8));
        Common.ToBigEndian((ulong)_blockData.DecompSize, r.Read(8));
    }
}
