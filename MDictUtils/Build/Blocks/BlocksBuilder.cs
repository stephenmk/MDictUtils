using System.Buffers;
using System.Diagnostics;
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
    private readonly static ArrayPool<byte> _arrayPool = ArrayPool<byte>.Shared;
    private readonly static string _typeName = typeof(T).Name;

    protected abstract T BlockConstructor(ReadOnlySpan<OffsetTableEntry> entries);
    protected abstract int GetByteCount(OffsetTableEntry entry);
    protected abstract void WriteBytes(OffsetTableEntry entry, Span<byte> buffer);
    protected abstract ImmutableArray<Range> GetBlockRanges(OffsetTable offsetTable);

    protected ImmutableArray<T> BuildBlocks(OffsetTable offsetTable)
    {
        LogBeginBuilding(_typeName);

        var ranges = GetBlockRanges(offsetTable);
        var blockCount = ranges.Length;
        var blocksBuilder = new T[blockCount];

        Parallel.For(0, blockCount, i =>
        {
            var range = ranges[i];
            var entries = offsetTable.AsSpan(range);
            var block = BlockConstructor(entries);
            blocksBuilder[i] = block;
        });

        var blocks = ImmutableArray.Create(blocksBuilder);
        LogBlocks(blocks);

        return blocks;
    }

    protected CompressedBlock GetCompressedBlock(ReadOnlySpan<OffsetTableEntry> entries)
    {
        int totalSize = entries.Sum(GetByteCount);
        var uncompressed = _arrayPool.Rent(totalSize);

        int position = 0;
        foreach (var entry in entries)
        {
            var size = GetByteCount(entry);
            var buffer = uncompressed.AsSpan(start: position, size);
            WriteBytes(entry, buffer);
            position += size;
        }

        var compressed = blockCompressor
            .Compress(uncompressed.AsSpan(..position));

        _arrayPool.Return(uncompressed);
        Debug.Assert(totalSize == position);

        return new(compressed, DecompSize: position);
    }

    [LoggerMessage(LogLevel.Debug, "Building blocks of type {Type}")]
    private partial void LogBeginBuilding(string type);

    [Conditional("DEBUG")]
    private void LogBlocks(IList<T> blocks)
    {
        // Average() throws an exception if the count is 0.
        var avg = blocks.Count > 0
            ? blocks.Average(static b => b.Bytes.Length)
            : 0;

        logger.LogDebug("Built {Count} blocks.", blocks.Count);
        logger.LogDebug("Average block size {Avg:N0} bytes", avg);

        if (blocks is not IList<KeyBlock>)
            return;

        foreach (var block in blocks)
        {
            logger.LogDebug("KeyBlock: {Block}", block);
        }
    }
}
