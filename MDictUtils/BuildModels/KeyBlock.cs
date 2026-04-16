using System.Diagnostics;
using System.Text;

namespace MDictUtils.BuildModels;

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
        => 8 + _firstKey.Size + _lastKey.Size + 8 + 8;

    public override void CopyIndexEntryTo(Span<byte> buffer)
    {
        Debug.Assert(buffer.Length == IndexEntryLength);

        var reader = new SpanReader<byte>(buffer);

        Common.ToBigEndian((ulong)_numEntries, reader.Read(8));
        _firstKey.CopyTo(ref reader);
        _lastKey.CopyTo(ref reader);
        Common.ToBigEndian((ulong)_block.Size, reader.Read(8));
        Common.ToBigEndian((ulong)_block.DecompSize, reader.Read(8));
    }

    public override string ToString()
        => $"NumEntries={_numEntries}, FirstKey='{_firstKey}', LastKey='{_lastKey}'";

    private readonly record struct OffsetEntryKey
    {
        public readonly int CharacterCount { get; }
        public readonly ImmutableArray<byte> NullAppendedBytes { get; }

        public OffsetEntryKey(OffsetTableEntry entry)
        {
            CharacterCount = entry.KeyLen;
            NullAppendedBytes = entry.KeyNull;
        }

        // Two bytes to store the character count.
        public int Size => 2 + NullAppendedBytes.Length;

        public void CopyTo(ref SpanReader<byte> reader)
        {
            Common.ToBigEndian((ushort)CharacterCount, reader.Read(2));
            NullAppendedBytes.CopyTo(reader.Read(NullAppendedBytes.Length));
        }

        public override string ToString()
            => Encoding.UTF8
                .GetString(NullAppendedBytes.AsSpan(..CharacterCount));
    }
}
