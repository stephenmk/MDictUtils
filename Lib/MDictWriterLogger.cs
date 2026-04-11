using System;
using System.Collections.Generic;

namespace Lib;

internal interface IMDictWriterLogger
{
    void LogBeginBuildingKeybIndex();
    void LogBeginBuildingKeyBlocks();
    void LogBlockSizeReset(int blockSize);
    void LogIndexEntry(ReadOnlySpan<byte> indexEntry);
    void LogInitializationComplete();
    void LogKeybIndex(long decompressedSize, long compressedSize);
    void LogKeyBlocks(int blockSize, IReadOnlyList<MdxKeyBlock> keyBlocks);
    void LogOffsetTable(IReadOnlyList<OffsetTableEntry> table, long totalRecordLen);
    void LogRecordBlocks(IReadOnlyList<MdxRecordBlock> recordBlocks);
    void LogRecordIndex(long size);
}

internal sealed class MDictWriterLogger : IMDictWriterLogger
{
    private static void WriteSeparator()
        => Console.Error.WriteLine("=========================");

    private static void WriteMessage(string message)
        => Console.Error.WriteLine($"[Writer] {message}");

    public void LogOffsetTable(IReadOnlyList<OffsetTableEntry> table, long totalRecordLen)
    {
        WriteMessage("Offset table built.");
        WriteMessage($"Total entries: {table.Count}, record length {totalRecordLen}");
        WriteSeparator();
    }

    public void LogBeginBuildingKeyBlocks()
    {
        WriteMessage("Building KeyBlocks");
    }

    public void LogKeyBlocks(int blockSize, IReadOnlyList<MdxKeyBlock> keyBlocks)
    {
        WriteMessage($"Block size set to {blockSize}");
        WriteMessage($"Built {keyBlocks.Count} key blocks.");
        foreach (var keyBlock in keyBlocks)
        {
            Console.Error.WriteLine($"* KeyBlock: {keyBlock}");
        }
    }

    public void LogBlockSizeReset(int blockSize)
    {
        WriteMessage($"Block size reset to {blockSize}");
        WriteSeparator();
    }

    public void LogBeginBuildingKeybIndex()
    {
        WriteMessage("Building KeybIndex");
    }

    public void LogIndexEntry(ReadOnlySpan<byte> indexEntry)
    {
        var bytes = new string[indexEntry.Length];
        for (int i = 0; i < indexEntry.Length; i++)
        {
            bytes[i] = $"{indexEntry[i]:X2}";
        }
        var displayBytes = string.Join(" ", bytes);
        Console.Error.WriteLine($"entry {displayBytes}");
    }

    public void LogKeybIndex(long decompressedSize, long compressedSize)
    {
        WriteMessage($"Key index built: decompressed={decompressedSize}, compressed={compressedSize}");
        WriteSeparator();
    }

    public void LogRecordBlocks(IReadOnlyList<MdxRecordBlock> recordBlocks)
    {
        WriteMessage($"Built {recordBlocks.Count} record blocks.");
        WriteMessage($"Built {recordBlocks}."); // TODO: this only prints the type of the collection.
        WriteSeparator();
    }

    public void LogRecordIndex(long size)
    {
        WriteMessage($"Record index built: size={size}");
        WriteSeparator();
    }

    public void LogInitializationComplete()
    {
        WriteMessage("Initialization complete.\n");
    }
}

internal sealed class MDictWriterDummyLogger : IMDictWriterLogger
{
    public void LogBeginBuildingKeybIndex() { }
    public void LogBeginBuildingKeyBlocks() { }
    public void LogBlockSizeReset(int _) { }
    public void LogIndexEntry(ReadOnlySpan<byte> _) { }
    public void LogInitializationComplete() { }
    public void LogKeybIndex(long _, long __) { }
    public void LogKeyBlocks(int _, IReadOnlyList<MdxKeyBlock> __) { }
    public void LogOffsetTable(IReadOnlyList<OffsetTableEntry> _, long __) { }
    public void LogRecordBlocks(IReadOnlyList<MdxRecordBlock> _) { }
    public void LogRecordIndex(long _) { }
}
