using System.Buffers;
using System.Diagnostics;
using System.Text;
using MDictUtils.BuildModels;
using MDictUtils.Extensions;
using Microsoft.Extensions.Logging;

namespace MDictUtils.Build.Offset;

internal sealed partial class OffsetTableBuilder
(
    ILogger<OffsetTableBuilder> logger,
    IKeyComparer keyComparer
)
{
    private static readonly ArrayPool<byte> _arrayPool = ArrayPool<byte>.Shared;

    public OffsetTable Build(List<MDictEntry> entries, MDictMetadata m)
    {
        entries.Sort((a, b) => keyComparer.Compare(a.Key, b.Key));

        var encoder = GetEncodingSettings(m);
        var arrayBuilder = ImmutableArray.CreateBuilder<OffsetTableEntry>(entries.Count);
        long currentOffset = 0;
        int maxEncLength = GetMaxEncLength(entries, encoder);

        byte[]? bufferArray = null;
        var buffer = maxEncLength < 256
            ? stackalloc byte[maxEncLength]
            : _arrayPool.Rent(maxEncLength, ref bufferArray);

        foreach (var entry in entries)
        {
            var length = encoder.Encoding.GetBytes($"{entry.Key}\0", buffer);
            var keyNull = ImmutableArray.Create(buffer[..length]);

            // Subtract the encoding length because we appended '\0'
            var keyLen = (length - encoder.EncodingLength) / encoder.EncodingLength;

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

        return new OffsetTable(tableEntries);
    }

    private sealed record EncodingSettings(Encoding Encoding, int EncodingLength);

    private static EncodingSettings GetEncodingSettings(MDictMetadata m)
    {
        var encoding = m.Encoding.ToLower();
        Debug.Assert(encoding == "utf8");

        if (m.IsMdd || encoding == "utf16" || encoding == "utf-16")
        {
            return new(Encoding.Unicode, EncodingLength: 2);
        }
        else if (encoding == "utf8" || encoding == "utf-8")
        {
            return new(Encoding.UTF8, EncodingLength: 1);
        }
        else
        {
            throw new NotSupportedException("Unknown encoding. Supported: utf8, utf16");
        }
    }

    private static int GetMaxEncLength(List<MDictEntry> entries, EncodingSettings encoder)
    {
        int maxEncLength = 0;
        foreach (var entry in entries)
        {
            int encLength = encoder.Encoding.GetByteCount(entry.Key);
            maxEncLength = int.Max(maxEncLength, encLength);
        }
        maxEncLength += encoder.EncodingLength; // Because we'll be appending an extra '\0' character.
        return maxEncLength;
    }

    [LoggerMessage(LogLevel.Debug,
    "Total entries: {Count}, record length {RecordLength}")]
    partial void LogInfo(int count, long RecordLength);
}
