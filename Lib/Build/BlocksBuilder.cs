using System.Diagnostics;
using Lib.BuildModels;
using Microsoft.Extensions.Logging;

namespace Lib.Build;

internal abstract partial class BlocksBuilder<T>(ILogger<BlocksBuilder<T>> logger) where T : MdxBlock
{
    protected abstract T BlockConstructor(ReadOnlySpan<OffsetTableEntry> entries);
    protected abstract long EntryLength(OffsetTableEntry entry);
    private readonly static string _typeName = typeof(T).Name;

    public List<T> Build(OffsetTable offsetTable, int blockSize)
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
            else if (curSize + EntryLength(offsetTableEntry) > blockSize)
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
                curSize += EntryLength(offsetTableEntry);
        }

        LogBlocks(blockSize, blocks);

        return blocks;
    }

    [LoggerMessage(LogLevel.Debug, "Building blocks of type {Type}")]
    private partial void LogBeginBuilding(string type);

    [Conditional("DEBUG")]
    private void LogBlocks(int blockSize, List<T> blocks)
    {
        logger.LogDebug("Block size set to {BlockSize}", blockSize);
        logger.LogDebug("Built {Count} blocks.", blocks.Count);

        if (blocks is not List<MdxKeyBlock>)
            return;

        foreach (var block in blocks)
        {
            logger.LogDebug("KeyBlock: {Block}", block);
        }
    }
}
