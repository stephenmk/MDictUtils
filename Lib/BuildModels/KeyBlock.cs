using System.Diagnostics;
using System.Text;

namespace Lib.BuildModels;

internal sealed class KeyBlock : MDictBlock
{
    private readonly int _numEntries;
    private readonly OffsetEntryKey _firstKey;
    private readonly OffsetEntryKey _lastKey;

    public KeyBlock(CompressedBlock block, ReadOnlySpan<OffsetTableEntry> offsetTable) : base(block)
    {
        _numEntries = offsetTable.Length;
        _firstKey = new(offsetTable[0]);
        _lastKey = new(offsetTable[^1]);
    }

    public override int IndexEntryLength
        => 8 + 2 + _firstKey.KNull.Length + 2 + _lastKey.KNull.Length + 8 + 8;

    public override void CopyIndexEntryTo(Span<byte> buffer)
    {
        Debug.Assert(buffer.Length == IndexEntryLength);

        var r = new SpanReader<byte>(buffer);

        Common.ToBigEndian((ulong)_numEntries, r.Read(8));
        Common.ToBigEndian((ushort)_firstKey.KLength, r.Read(2));
        _firstKey.KNull.CopyTo(r.Read(_firstKey.KNull.Length));
        Common.ToBigEndian((ushort)_lastKey.KLength, r.Read(2));
        _lastKey.KNull.CopyTo(r.Read(_lastKey.KNull.Length));
        Common.ToBigEndian((ulong)_block.Size, r.Read(8));
        Common.ToBigEndian((ulong)_block.DecompSize, r.Read(8));
    }

    public override string ToString()
        => $"NumEntries={_numEntries}, FirstKey='{_firstKey}', LastKey='{_lastKey}'";

    private readonly record struct OffsetEntryKey
    {
        public readonly ImmutableArray<byte> KNull { get; }
        public readonly int KLength { get; }

        public OffsetEntryKey(OffsetTableEntry entry)
        {
            KNull = entry.KeyNull;
            KLength = entry.KeyLen;
        }

        public override string ToString()
            => Encoding.UTF8.GetString(KNull.AsSpan(..KLength));
    }
}
