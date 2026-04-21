using System.Buffers;
using System.Text;
using MDictUtils.Extensions;

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
    ReadOnlyMemory<KeyBlock> KeyBlocks
)
{
    public int KeyBlocksSize => KeyBlocks.Span.Sum(static b => b.Bytes.Length);
}

internal readonly record struct CompressedBlock(IMemoryOwner<byte> MemoryOwner, int Size, int DecompSize)
{
    public ReadOnlyMemory<byte> Bytes => MemoryOwner.Memory[..Size];
    public void Dispose() => MemoryOwner.Dispose();
}

internal readonly record struct OffsetTable
(
    ImmutableArray<OffsetTableEntry> Entries,
    ImmutableArray<Range> KeyBlockRanges,
    ImmutableArray<Range> RecordBlockRanges
)
{
    public int Length => Entries.Length;
    public ReadOnlyMemory<OffsetTableEntry> AsMemory(Range range) => Entries.AsMemory()[range];
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
