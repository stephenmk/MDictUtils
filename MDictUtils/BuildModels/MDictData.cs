namespace MDictUtils.BuildModels;

internal sealed record MDictData
(
    string Title,
    string Description,
    string Version,
    bool IsMdd,
    int EntryCount,
    CompressedBlock KeyBlockIndex,
    ImmutableArray<KeyBlock> KeyBlocks,
    Block RecordBlockIndex,
    ImmutableArray<RecordBlock> RecordBlocks
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
    public int Length => Entries.Length;
    public long TotalRecordLength => Entries.Sum(static e => e.RecordSize);
    public ReadOnlySpan<OffsetTableEntry> AsSpan(Range range) => Entries.AsSpan(range);
    public Dictionary<string, int> GetFilePathToTotalEntryCount()
    {
        var dict = new Dictionary<string, int>();
        foreach (var entry in Entries)
        {
            dict[entry.FilePath] =
                dict.TryGetValue(entry.FilePath, out var count)
                    ? count + 1
                    : 1;
        }
        return dict;
    }
}
