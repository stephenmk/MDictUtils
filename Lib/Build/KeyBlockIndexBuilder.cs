using System.Buffers;
using System.Collections.ObjectModel;
using System.Diagnostics;
using Lib.BuildModels;
using Microsoft.Extensions.Logging;

namespace Lib.Build;

internal partial class KeyBlockIndexBuilder(ILogger<KeyBlockIndexBuilder> logger)
{
    private readonly static ArrayPool<byte> _arrayPool = ArrayPool<byte>.Shared;

    public KeyBlockIndex Build(ReadOnlyCollection<MdxKeyBlock> keyBlocks, int compressionType)
    {
        if (keyBlocks is [])
            return new([], 0);

        int decompDataTotalSize = keyBlocks.Sum(static b => b.IndexEntryLength);
        var decompArray = _arrayPool.Rent(decompDataTotalSize);
        var decompData = decompArray.AsSpan(..decompDataTotalSize);

        int maxBlockSize = keyBlocks.Max(static b => b.IndexEntryLength);
        byte[]? blockArray = null;
        var blockBuffer = maxBlockSize < 256
            ? stackalloc byte[maxBlockSize]
            : (blockArray = _arrayPool.Rent(maxBlockSize));

        int bytesWritten = 0;
        foreach (var block in keyBlocks)
        {
            var indexEntry = blockBuffer[..block.IndexEntryLength];
            block.GetIndexEntry(indexEntry);
            LogIndexEntry(indexEntry);
            var destination = decompData.Slice(bytesWritten, indexEntry.Length);
            indexEntry.CopyTo(destination);
            bytesWritten += indexEntry.Length;
        }

        if (blockArray is not null)
            _arrayPool.Return(blockArray);

        Debug.Assert(bytesWritten == decompDataTotalSize);

        var compressedBytes = MdxBlock.MdxCompress(decompData, compressionType);
        _arrayPool.Return(decompArray);

        KeyBlockIndex index = new(
            CompressedBytes: compressedBytes,
            DecompSize: bytesWritten);

        LogIndexBuilt(index.DecompSize, index.CompressedSize);

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
