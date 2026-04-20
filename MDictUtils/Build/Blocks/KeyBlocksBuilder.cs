using System.Diagnostics;
using System.Threading.Channels;
using MDictUtils.BuildModels;
using Microsoft.Extensions.Logging;
using OrderedBlock = (int Order, MDictUtils.BuildModels.KeyBlock Block);

namespace MDictUtils.Build.Blocks;

internal sealed class KeyBlocksBuilder
(
    ILogger<KeyBlocksBuilder> logger,
    IBlockCompressor blockCompressor
)
    : BlocksBuilder<KeyBlock>(logger, blockCompressor)
{
    public async Task<ImmutableArray<KeyBlock>> BuildAsync(OffsetTable offsetTable)
    {
        var blockCount = offsetTable.KeyBlockRanges.Length;
        var blocks = new KeyBlock[blockCount];
        var channel = Channel.CreateUnbounded<OrderedBlock>();

        var readTask = ReadKeyBlocksAsync(blocks, channel);
        var buildTask = BuildBlocksAsync(offsetTable, channel);

        await Task.WhenAll(readTask, buildTask);

        LogBlocks(blocks);

        return ImmutableArray.Create(blocks);
    }

    private static async Task ReadKeyBlocksAsync(KeyBlock[] blocks, ChannelReader<OrderedBlock> channel)
    {
        await foreach (var (i, block) in channel.ReadAllAsync())
        {
            blocks[i] = block;
        }
    }

    protected override KeyBlock BlockConstructor(ReadOnlySpan<OffsetTableEntry> entries)
    {
        var block = GetCompressedBlock(entries);
        return new(block, entries);
    }

    protected override int GetByteCount(OffsetTableEntry entry)
        => entry.KeyDataSize;

    protected override void WriteBytes(OffsetTableEntry entry, Span<byte> buffer)
    {
        Common.ToBigEndian((ulong)entry.Offset, buffer[..8]);
        entry.NullTerminatedKeyBytes.CopyTo(buffer[8..]);
    }

    protected override ImmutableArray<Range> GetBlockRanges(OffsetTable offsetTable)
        => offsetTable.KeyBlockRanges;

    [Conditional("DEBUG")]
    private void LogBlocks(IList<KeyBlock> blocks)
    {
        // Average() throws an exception if the count is 0.
        var avg = blocks.Count > 0
            ? blocks.Average(static b => b.Bytes.Length)
            : 0;

        logger.LogDebug("Built {Count} key blocks.", blocks.Count);
        logger.LogDebug("Average key block size {Avg:N0} bytes", avg);

        foreach (var block in blocks)
        {
            logger.LogDebug("KeyBlock: {Block}", block);
        }
    }
}
