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
    private readonly static ArrayPool<byte> _arrayPool = ArrayPool<byte>.Shared;

    public CompressedBlock Build(ImmutableArray<KeyBlock> keyBlocks)
    {
        if (keyBlocks is [])
            return new([], 0);

        int totalSize = keyBlocks.Sum(static b => b.IndexEntryLength);
        var uncompressed = _arrayPool.Rent(totalSize);

        int position = 0;
        foreach (var block in keyBlocks)
        {
            var size = block.IndexEntryLength;
            var buffer = uncompressed.AsSpan(position, size);
            block.CopyIndexEntryTo(buffer);
            LogIndexEntry(buffer);
            position += size;
        }

        var compressed = blockCompressor
            .Compress(uncompressed.AsSpan(..position));

        _arrayPool.Return(uncompressed);

        CompressedBlock index = new(
            Bytes: compressed,
            DecompSize: position);

        Debug.Assert(position == totalSize);
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

    [LoggerMessage(LogLevel.Debug, "Entry: {EntryData}")]
    private partial void LogEntryData(string entryData);

    [LoggerMessage(LogLevel.Debug,
    "Key index built: decompressed={DecompSize}, compressed={CompSize}")]
    private partial void LogIndexBuilt(long decompSize, int compSize);
}
