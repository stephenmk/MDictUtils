using System;
using System.Collections.Generic;

namespace Lib;

internal interface IMDictWriterLogger
{
    void LogBeginBuildingKeybIndex();
    void LogBeginBuildingKeyBlocks();
    void LogIndexEntry(ReadOnlySpan<byte> indexEntry);
    void LogInitializationComplete();
    void LogKeyBlockIndex(KeyBlockIndex keyBlockIndex);
    void LogKeyBlocks(int blockSize, IReadOnlyList<MdxKeyBlock> keyBlocks);
    void LogOffsetTable(OffsetTable offsetTable);
    void LogRecordBlocks(IReadOnlyList<MdxRecordBlock> recordBlocks);
    void LogRecordIndex(RecordBlockIndex recordBlockIndex);
}

internal sealed class MDictWriterLogger : IMDictWriterLogger
{
    private static void WriteSeparator()
        => Console.Error.WriteLine("=========================");

    private static void WriteMessage(string message)
        => Console.Error.WriteLine($"[Writer] {message}");

    public void LogOffsetTable(OffsetTable table)
    {
        WriteMessage("Offset table built.");
        WriteMessage($"Total entries: {table.Entries.Length}, record length {table.TotalRecordLength}");
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

    public void LogKeyBlockIndex(KeyBlockIndex keyBlockIndex)
    {
        WriteMessage($"Key index built: decompressed={keyBlockIndex.DecompSize}, compressed={keyBlockIndex.CompressedSize}");
        WriteSeparator();
    }

    public void LogRecordBlocks(IReadOnlyList<MdxRecordBlock> recordBlocks)
    {
        WriteMessage($"Built {recordBlocks.Count} record blocks.");
        WriteSeparator();
    }

    public void LogRecordIndex(RecordBlockIndex recordBlockIndex)
    {
        WriteMessage($"Record index built: size={recordBlockIndex.Size}");
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
    public void LogIndexEntry(ReadOnlySpan<byte> _) { }
    public void LogInitializationComplete() { }
    public void LogKeyBlockIndex(KeyBlockIndex _) { }
    public void LogKeyBlocks(int _, IReadOnlyList<MdxKeyBlock> __) { }
    public void LogOffsetTable(OffsetTable _) { }
    public void LogRecordBlocks(IReadOnlyList<MdxRecordBlock> _) { }
    public void LogRecordIndex(RecordBlockIndex _) { }
}
