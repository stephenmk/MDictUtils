using System.Collections.ObjectModel;

namespace Lib.BuildModels;

internal sealed record MDictData
(
    string Title,
    string Description,
    string Version,
    bool IsMdd,
    int EntryCount,
    ReadOnlyCollection<KeyBlock> KeyBlocks,
    ReadOnlyCollection<RecordBlock> RecordBlocks,
    CompressedBlock KeyBlockIndex,
    Block RecordBlockIndex
)
{
    public int KeyBlocksSize => KeyBlocks.Sum(static b => b.Bytes.Length);
    public int RecordBlocksSize => RecordBlocks.Sum(static b => b.Bytes.Length);
}

internal readonly record struct Block(ImmutableArray<byte> Bytes)
{
    public int Size => Bytes.Length;
}

internal readonly record struct CompressedBlock(ImmutableArray<byte> Bytes, long DecompSize)
{
    public int Size => Bytes.Length;
}

internal readonly record struct OffsetTable(ImmutableArray<OffsetTableEntry> Entries)
{
    public long TotalRecordLength => Entries.Sum(static e => e.RecordSize);
}
