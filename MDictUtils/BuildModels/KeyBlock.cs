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
        private readonly ushort _characterCount;
        private readonly ImmutableArray<byte> _nullTerminatedBytes;

        public OffsetEntryKey(OffsetTableEntry entry)
        {
            // Overflow error if the key contains more than 65,535 characters.
            _characterCount = Convert.ToUInt16(entry.KeyCharacterCount);
            _nullTerminatedBytes = entry.NullTerminatedKeyBytes;
        }

        // Two bytes to store the character count.
        public int Size => 2 + _nullTerminatedBytes.Length;

        public void CopyTo(ref SpanReader<byte> reader)
        {
            Common.ToBigEndian(_characterCount, reader.Read(2));
            _nullTerminatedBytes.CopyTo(reader.Read(_nullTerminatedBytes.Length));
        }

        public override string ToString()
            => Encoding.UTF8
                .GetString(_nullTerminatedBytes.AsSpan(.._characterCount));
    }
}
