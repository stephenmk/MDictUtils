using System.Collections.ObjectModel;

namespace Lib.BuildModels;

internal sealed record MDictData
(
    MDictMetadata Metadata,
    int EntryCount,
    OffsetTable OffsetTable,
    ReadOnlyCollection<MdxKeyBlock> KeyBlocks,
    ReadOnlyCollection<MdxRecordBlock> RecordBlocks,
    KeyBlockIndex KeyBlockIndex,
    RecordBlockIndex RecordBlockIndex
)
{
    public int KeyBlocksSize => KeyBlocks.Sum(static b => b.BlockData.Length);
    public int RecordBlocksSize => RecordBlocks.Sum(static b => b.BlockData.Length);
}

internal readonly record struct KeyBlockIndex(ImmutableArray<byte> CompressedBytes, long DecompSize)
{
    public int CompressedSize => CompressedBytes.Length;
}

internal readonly record struct RecordBlockIndex(ImmutableArray<byte> Bytes)
{
    public int Size => Bytes.Length;
}

internal readonly record struct OffsetTable(ImmutableArray<OffsetTableEntry> Entries)
{
    public long TotalRecordLength => Entries.Sum(static e => e.RecordSize);
}
