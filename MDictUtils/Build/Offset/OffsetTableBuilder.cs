using System.Buffers;
using MDictUtils.BuildModels;
using MDictUtils.Extensions;
using Microsoft.Extensions.Logging;

namespace MDictUtils.Build.Offset;

internal sealed partial class OffsetTableBuilder
(
    ILogger<OffsetTableBuilder> logger,
    IKeyComparer keyComparer,
    BuildOptions options
)
{
    private static readonly ArrayPool<byte> _arrayPool = ArrayPool<byte>.Shared;
    private static readonly MemoryPool<Range> _rangePool = MemoryPool<Range>.Shared;

    public OffsetTable Build(List<MDictEntry> entries)
    {
        entries.Sort((a, b) => keyComparer.Compare(a.Key, b.Key));

        var tableEntries = GetTableEntries(entries);
        var keyBlockRanges = GetKeyBlockRanges(tableEntries);
        var recordBlockRanges = GetRecordBlockRanges(tableEntries);

        return new OffsetTable(tableEntries, keyBlockRanges, recordBlockRanges);
    }

    private ImmutableArray<OffsetTableEntry> GetTableEntries(List<MDictEntry> entries)
    {
        var arrayBuilder = ImmutableArray.CreateBuilder<OffsetTableEntry>(entries.Count);
        long currentOffset = 0;
        int maxKeySize = GetMaxKeySize(entries);
        int encodingLength = options.KeyEncodingLength;

        byte[]? bufferArray = null;
        var buffer = maxKeySize < 256
            ? stackalloc byte[maxKeySize]
            : _arrayPool.Rent(maxKeySize, ref bufferArray);

        foreach (var entry in entries)
        {
            var length = options.KeyEncoding.GetBytes($"{entry.Key}\0", buffer);
            var keyNull = ImmutableArray.Create(buffer[..length]);

            // Subtract the encoding length because we appended '\0'
            var keyLen = (length - encodingLength) / encodingLength;

            var tableEntry = new OffsetTableEntry
            {
                NullTerminatedKeyBytes = keyNull,
                KeyCharacterCount = keyLen,
                Offset = currentOffset,
                RecordSize = entry.Size,
                RecordPos = entry.Pos,
                FilePath = entry.Path,
            };
            arrayBuilder.Add(tableEntry);

            currentOffset += entry.Size;
        }

        if (bufferArray is not null)
            _arrayPool.Return(bufferArray);

        var tableEntries = arrayBuilder.MoveToImmutable();
        LogInfo(tableEntries.Length, currentOffset);

        return tableEntries;
    }

    private int GetMaxKeySize(List<MDictEntry> entries)
    {
        int maxKeySize = entries.Any()
            ? entries.Max(entry => options.KeyEncoding.GetByteCount(entry.Key))
            : 0;

        // Add the length of one character because
        // we'll be appending a '\0' character later.
        maxKeySize += options.KeyEncodingLength;
        return maxKeySize;
    }

    private ImmutableArray<Range> GetKeyBlockRanges(ImmutableArray<OffsetTableEntry> tableEntries)
    {
        var keyEntrySizes = tableEntries
            .Select(static e => e.KeyDataSize)
            .ToArray();
        return PartitionTable(keyEntrySizes, options.DesiredKeyBlockSize);
    }

    private ImmutableArray<Range> GetRecordBlockRanges(ImmutableArray<OffsetTableEntry> tableEntries)
    {
        var recordEntrySizes = tableEntries
            .Select(static e => e.RecordSize)
            .ToArray();
        return PartitionTable(recordEntrySizes, options.DesiredRecordBlockSize);
    }

    private ImmutableArray<Range> PartitionTable(ReadOnlySpan<int> entrySizes, int desiredBlockSize)
    {
        using var memoryOwner = _rangePool.Rent(entrySizes.Length);
        var ranges = memoryOwner.Memory.Span;
        int start = 0;
        int blockCount = 0;
        long blockSize = 0;

        for (int end = 0; end <= entrySizes.Length; end++)
        {
            int? entrySize = (end == entrySizes.Length)
                ? null
                : entrySizes[end];

            bool flush;
            if (end == 0)
                flush = false;
            else if (entrySize == null)
                flush = true;
            else if (blockSize + entrySize > desiredBlockSize)
                flush = true;
            else
                flush = false;

            if (flush)
            {
                ranges[blockCount++] = start..end;
                blockSize = 0;
                start = end;
            }

            if (entrySize.HasValue)
                blockSize += entrySize.Value;
        }

        return ImmutableArray.Create(ranges[..blockCount]);
    }

    [LoggerMessage(LogLevel.Debug,
    "Total entries: {Count}, record length {RecordLength}")]
    partial void LogInfo(int count, long RecordLength);
}
