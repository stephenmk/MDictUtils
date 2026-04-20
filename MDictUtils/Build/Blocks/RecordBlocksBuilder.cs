using System.Threading.Channels;
using MDictUtils.BuildModels;
using Microsoft.Extensions.Logging;

namespace MDictUtils.Build.Blocks;

internal abstract class RecordBlocksBuilder
(
    ILogger<RecordBlocksBuilder> logger,
    IBlockCompressor blockCompressor
)
    : BlocksBuilder<RecordBlock>(logger, blockCompressor), IRecordBlocksBuilder
{
    public abstract Task BuildAsync(OffsetTable offsetTable, ChannelWriter<(int, RecordBlock)> channel);

    protected sealed override int GetByteCount(OffsetTableEntry entry)
        => entry.RecordSize;

    protected sealed override ImmutableArray<Range> GetBlockRanges(OffsetTable offsetTable)
        => offsetTable.RecordBlockRanges;

    protected sealed override RecordBlock BlockConstructor(ReadOnlySpan<OffsetTableEntry> entries)
    {
        var block = GetCompressedBlock(entries);
        return new(block);
    }
}
