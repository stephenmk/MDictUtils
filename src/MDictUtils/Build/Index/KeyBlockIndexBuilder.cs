using System.Buffers;
using System.Diagnostics;
using MDictUtils.BuildModels;
using MDictUtils.Extensions;
using Microsoft.Extensions.Logging;

namespace MDictUtils.Build.Index;

internal sealed partial class KeyBlockIndexBuilder
(
    ILogger<KeyBlockIndexBuilder> logger,
    IBlockCompressor blockCompressor
)
{
    private static readonly MemoryPool<byte> _memoryPool = MemoryPool<byte>.Shared;

    public async Task<CompressedBlock> BuildAsync(ReadOnlyMemory<KeyBlock> keyBlocks)
    {
        if (keyBlocks.IsEmpty)
        {
            var compressed = _memoryPool.Rent(0);
            return new(compressed, 0, 0);
        }

        int totalSize = keyBlocks.Span.Sum(static b => b.IndexEntryLength);
        using var memoryOwner = _memoryPool.Rent(totalSize);
        var uncompressed = memoryOwner.Memory[..totalSize];

        int position = 0;
        foreach (var block in keyBlocks.Span)
        {
            var size = block.IndexEntryLength;
            var buffer = uncompressed.Span.Slice(position, size);
            block.CopyIndexEntryTo(buffer);
            LogIndexEntry(buffer);
            position += size;
        }

        var index = await blockCompressor.CompressAsync(uncompressed);
        LogIndexBuilt(index.DecompSize, index.Size);

        return index;
    }

    [Conditional("DEBUG")]
    private void LogIndexEntry(ReadOnlySpan<byte> indexEntry)
    {
        var bytes = new string[indexEntry.Length];
        for (int i = 0; i < indexEntry.Length; i++)
        {
            bytes[i] = $"{indexEntry[i]:X2}";
        }
        var entryData = string.Join(" ", bytes);
        LogEntryData(entryData);
    }

    [LoggerMessage(LogLevel.Debug, "KeyBlock index entry: {EntryData}")]
    private partial void LogEntryData(string entryData);

    [LoggerMessage(LogLevel.Debug,
    "Key index built: decompressed={DecompSize}, compressed={CompSize}")]
    private partial void LogIndexBuilt(long decompSize, int compSize);
}
