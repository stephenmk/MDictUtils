using System.Diagnostics;
using System.Threading.Channels;
using MDictUtils.BuildModels;
using Microsoft.Extensions.Logging;

namespace MDictUtils.Build.Blocks;

internal sealed class KeyBlocksBuilder
(
    ILogger<KeyBlocksBuilder> logger,
    IBlockCompressor blockCompressor
)
    : BlocksBuilder<KeyBlock>(logger, blockCompressor)
{
    public async Task<ReadOnlyMemory<KeyBlock>> BuildAsync(OffsetTable offsetTable)
    {
        var blockCount = offsetTable.KeyBlockRanges.Length;
        var blocks = new KeyBlock[blockCount];
        var channel = Channel.CreateUnbounded<KeyBlock>();

        var readTask = ReadKeyBlocksAsync(blocks, channel);
        var buildTask = BuildBlocksAsync(offsetTable, channel);

        await Task.WhenAll(readTask, buildTask);

        LogBlocks(blocks);

        return blocks;
    }

    private static async Task ReadKeyBlocksAsync(KeyBlock[] blocks, ChannelReader<KeyBlock> channel)
    {
        await foreach (var block in channel.ReadAllAsync())
        {
            blocks[block.Id] = block;
        }
    }

    protected override int GetByteCount(OffsetTableEntry entry)
        => entry.KeyDataSize;

    protected override ImmutableArray<Range> GetBlockRanges(OffsetTable offsetTable)
        => offsetTable.KeyBlockRanges;

    protected override async Task<KeyBlock> BlockConstructorAsync(int id, ReadOnlyMemory<OffsetTableEntry> entries)
    {
        var block = await GetCompressedBlockAsync(entries);
        return new(id, block, entries.Span);
    }

    protected override async Task WriteBytesAsync(OffsetTableEntry entry, Memory<byte> buffer)
    {
        Common.ToBigEndian((ulong)entry.Offset, buffer.Span[..8]);
        entry.NullTerminatedKeyBytes.CopyTo(buffer.Span[8..]);
    }

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
