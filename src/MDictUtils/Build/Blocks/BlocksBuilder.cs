using System.Buffers;
using System.Threading.Channels;
using MDictUtils.BuildModels;
using MDictUtils.Extensions;
using Microsoft.Extensions.Logging;

namespace MDictUtils.Build.Blocks;

internal abstract partial class BlocksBuilder<T>
(
    ILogger<BlocksBuilder<T>> logger,
    IBlockCompressor blockCompressor
)
    where T : MDictBlock
{
    private static readonly MemoryPool<byte> _memoryPool = MemoryPool<byte>.Shared;
    private static readonly string _typeName = typeof(T).Name;

    protected abstract int GetByteCount(OffsetTableEntry entry);
    protected abstract ImmutableArray<Range> GetBlockRanges(OffsetTable offsetTable);
    protected abstract Task<T> BlockConstructorAsync(int id, ReadOnlyMemory<OffsetTableEntry> entries);
    protected abstract Task WriteBytesAsync(OffsetTableEntry entry, Memory<byte> buffer);

    protected async Task BuildBlocksAsync(OffsetTable offsetTable, ChannelWriter<T> channel)
    {
        LogBeginBuilding(_typeName);
        var blockRanges = GetBlockRanges(offsetTable);

        await Parallel.ForAsync(0, blockRanges.Length, async (i, ct) =>
        {
            var blockRange = blockRanges[i];
            var entries = offsetTable.AsMemory(blockRange);
            var block = await BlockConstructorAsync(i, entries);
            await channel.WriteAsync(block, ct);
        });

        channel.Complete();
    }

    protected async Task<CompressedBlock> GetCompressedBlockAsync(ReadOnlyMemory<OffsetTableEntry> entries)
    {
        int totalSize = entries.Span.Sum(GetByteCount);
        using var memoryOwner = _memoryPool.Rent(totalSize);
        var uncompressed = memoryOwner.Memory[..totalSize];

        int position = 0;
        for (int i = 0; i < entries.Length; i++)
        {
            var entry = entries.Span[i];
            var size = GetByteCount(entry);
            var buffer = uncompressed.Slice(start: position, size);
            await WriteBytesAsync(entry, buffer);
            position += size;
        }

        return await blockCompressor.CompressAsync(uncompressed);
    }

    [LoggerMessage(LogLevel.Debug, "Building blocks of type {Type}")]
    private partial void LogBeginBuilding(string type);
}
