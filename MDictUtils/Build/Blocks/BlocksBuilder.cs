using System.Buffers;
using System.Diagnostics;
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
    private static readonly ArrayPool<byte> _arrayPool = ArrayPool<byte>.Shared;
    private static readonly string _typeName = typeof(T).Name;

    protected abstract T BlockConstructor(ReadOnlySpan<OffsetTableEntry> entries);
    protected abstract int GetByteCount(OffsetTableEntry entry);
    protected abstract void WriteBytes(OffsetTableEntry entry, Span<byte> buffer);
    protected abstract ImmutableArray<Range> GetBlockRanges(OffsetTable offsetTable);

    protected async Task BuildBlocksAsync(OffsetTable offsetTable, ChannelWriter<(int, T)> channel)
    {
        LogBeginBuilding(_typeName);
        var blockRanges = GetBlockRanges(offsetTable);
        var enumerator = Enumerable.Range(0, blockRanges.Length);

        await Parallel.ForEachAsync(enumerator, async (i, ct) =>
        {
            var blockRange = blockRanges[i];
            var entries = offsetTable.AsSpan(blockRange);
            var block = BlockConstructor(entries);
            await channel.WriteAsync((i, block), ct);
        });

        channel.Complete();
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
}
