using System.Text;

namespace MDictUtils.BuildModels;

internal sealed record BuildOptions
{
    public required int DesiredKeyBlockSize { get; init; }
    public required int DesiredRecordBlockSize { get; init; }
    public required Encoding KeyEncoding { get; init; }
    public required int KeyEncodingLength { get; init; }
}

internal readonly record struct KeyData
(
    int EntryCount,
    CompressedBlock KeyBlockIndex,
    ImmutableArray<KeyBlock> KeyBlocks
)
{
    public int KeyBlocksSize => KeyBlocks.Sum(static b => b.Bytes.Length);
}

internal readonly record struct RecordData
(
    int EntryCount,
    Block RecordBlockIndex,
    ImmutableArray<RecordBlock> RecordBlocks
)
{
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

internal readonly record struct OffsetTable
(
    ImmutableArray<OffsetTableEntry> Entries,
    ImmutableArray<Range> KeyBlockRanges,
    ImmutableArray<Range> RecordBlockRanges
)
{
    public int Length => Entries.Length;
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
