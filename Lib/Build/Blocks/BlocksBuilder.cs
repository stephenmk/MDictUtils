using System.Buffers;
using System.Diagnostics;
using Lib.BuildModels;
using Microsoft.Extensions.Logging;

namespace Lib.Build.Blocks;

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
    protected abstract long GetByteCount(OffsetTableEntry entry);
    protected abstract int WriteBytes(OffsetTableEntry entry, Span<byte> buffer);

    protected List<T> BuildBlocks(OffsetTable offsetTable, int blockSize)
    {
        LogBeginBuilding(_typeName);

        var blocks = new List<T>();
        int thisBlockStart = 0;
        long curSize = 0;

        for (int ind = 0; ind <= offsetTable.Entries.Length; ind++)
        {
            var offsetTableEntry = (ind == offsetTable.Entries.Length)
                ? null
                : offsetTable.Entries[ind];

            bool flush;
            if (ind == 0)
                flush = false;
            else if (offsetTableEntry == null)
                flush = true;
            else if (curSize + GetByteCount(offsetTableEntry) > blockSize)
                flush = true;
            else
                flush = false;

            if (flush)
            {
                var blockEntries = offsetTable.Entries.AsSpan(thisBlockStart..ind);
                // foreach (var entry in blockEntries)
                // {
                //     Console.WriteLine($"[split flush] {entry}");
                // }
                var block = BlockConstructor(blockEntries);
                blocks.Add(block);
                curSize = 0;
                thisBlockStart = ind;
            }

            if (offsetTableEntry is not null)
                curSize += GetByteCount(offsetTableEntry);
        }

        LogBlocks(blockSize, blocks);

        return blocks;
    }

    protected CompressedBlock GetCompressedBlock(ReadOnlySpan<OffsetTableEntry> offsetTableEntries)
    {
        // Console.WriteLine("[Debug] Calling MdxBlock...");

        int decompDataSize = Convert.ToInt32(offsetTableEntries.Sum(GetByteCount));
        var decompData = _arrayPool.Rent(decompDataSize);

        var maxBlockSize = Convert.ToInt32(offsetTableEntries.Max(GetByteCount));
        byte[]? blockArray = null;
        var blockBuffer = maxBlockSize < 256
            ? stackalloc byte[maxBlockSize]
            : (blockArray = _arrayPool.Rent(maxBlockSize));

        int totalSize = 0;
        foreach (var entry in offsetTableEntries)
        {
            int blockSize = WriteBytes(entry, blockBuffer);
            // Console.WriteLine($"[Debug] BlockEntry ({blockEntry.Length} bytes): {BitConverter.ToString(blockEntry)}");
            var source = blockBuffer[..blockSize];
            var destination = decompData.AsSpan(start: totalSize, length: blockSize);
            source.CopyTo(destination);
            totalSize += blockSize;
        }

        if (blockArray is not null)
            _arrayPool.Return(blockArray);

        // Console.WriteLine("[Debug] Building MdxBlock...");
        // Console.WriteLine($"[Debug] Decompressed array length (_decompSize): {_decompSize}");
        // Common.PrintPythonStyle(decompArray);

        var compressedBytes = blockCompressor.Compress(decompData[..totalSize]);

        // Console.WriteLine($"[Debug] Compressed array length (_compSize): {_compSize}");

        _arrayPool.Return(decompData);

        return new(compressedBytes, DecompSize: totalSize);
    }

    [LoggerMessage(LogLevel.Debug, "Building blocks of type {Type}")]
    private partial void LogBeginBuilding(string type);

    [Conditional("DEBUG")]
    private void LogBlocks(int blockSize, List<T> blocks)
    {
        logger.LogDebug("Block size set to {BlockSize}", blockSize);
        logger.LogDebug("Built {Count} blocks.", blocks.Count);

        if (blocks is not List<KeyBlock>)
            return;

        foreach (var block in blocks)
        {
            logger.LogDebug("KeyBlock: {Block}", block);
        }
    }
}
