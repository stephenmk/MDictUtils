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
    private readonly static ArrayPool<Range> _rangePool = ArrayPool<Range>.Shared;
    private readonly static string _typeName = typeof(T).Name;

    protected abstract T BlockConstructor(ReadOnlySpan<OffsetTableEntry> entries);
    protected abstract long GetByteCount(OffsetTableEntry entry);
    protected abstract int WriteBytes(OffsetTableEntry entry, Span<byte> buffer);

    protected ImmutableArray<T> BuildBlocks(OffsetTable offsetTable, int desiredBlockSize)
    {
        LogBeginBuilding(_typeName);

        var ranges = _rangePool.Rent(offsetTable.Length);
        var partitionCount = PartitionTable(offsetTable, desiredBlockSize, ranges);
        var blocksBuilder = new T[partitionCount];

        Parallel.For(0, partitionCount, i =>
        {
            var range = ranges[i];
            var entries = offsetTable.AsSpan(range);
            var block = BlockConstructor(entries);
            blocksBuilder[i] = block;
        });

        var blocks = ImmutableArray.Create(blocksBuilder);
        _rangePool.Return(ranges);
        LogBlocks(desiredBlockSize, blocks);

        return blocks;
    }

    private int PartitionTable(OffsetTable offsetTable, int desiredBlockSize, Span<Range> ranges)
    {
        int partitionCount = 0;
        int start = 0;
        long blockSize = 0;

        for (int end = 0; end <= offsetTable.Length; end++)
        {
            var offsetTableEntry = (end == offsetTable.Length)
                ? null
                : offsetTable.Entries[end];

            bool flush;
            if (end == 0)
                flush = false;
            else if (offsetTableEntry == null)
                flush = true;
            else if (blockSize + GetByteCount(offsetTableEntry) > desiredBlockSize)
                flush = true;
            else
                flush = false;

            if (flush)
            {
                ranges[partitionCount++] = start..end;
                blockSize = 0;
                start = end;
            }

            if (offsetTableEntry is not null)
                blockSize += GetByteCount(offsetTableEntry);
        }

        return partitionCount;
    }

    protected CompressedBlock GetCompressedBlock(ReadOnlySpan<OffsetTableEntry> entries)
    {
        int totalSize = Convert.ToInt32(entries.Sum(GetByteCount));
        var uncompressed = _arrayPool.Rent(totalSize);

        int position = 0;
        foreach (var entry in entries)
        {
            var buffer = uncompressed.AsSpan(start: position);
            int size = WriteBytes(entry, buffer);
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
    private void LogBlocks(int desiredBlockSize, IList<T> blocks)
    {
        logger.LogDebug("Desired block size set to {BlockSize}", desiredBlockSize);
        logger.LogDebug("Built {Count} blocks.", blocks.Count);

        if (blocks is not IList<KeyBlock>)
            return;

        foreach (var block in blocks)
        {
            logger.LogDebug("KeyBlock: {Block}", block);
        }
    }
}
