using System.Buffers;
using System.Diagnostics;
using System.Text;
using Lib.BuildModels;
using Microsoft.Extensions.Logging;

namespace Lib.Build.Offset;

internal partial class OffsetTableBuilder
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
            : (bufferArray = _arrayPool.Rent(maxEncLength));

        foreach (var item in entries)
        {
            // Console.WriteLine($"dict item: {item}");

            var length = encoder.InnerEncoding.GetBytes($"{item.Key}\0", buffer);
            var keyNull = ImmutableArray.Create(buffer[..length]);

            // Subtract the encoding length because we appended '\0'
            var keyLen = (length - encoder.EncodingLength) / encoder.EncodingLength;

            // var recordNull = encodingSettings.InnerEncoding.GetBytes(item.Path);

            var tableEntry = new OffsetTableEntry
            {
                // Key = keyEnc,
                KeyNull = keyNull,
                KeyLen = keyLen,
                // RecordNull = recordNull,
                Offset = currentOffset,
                RecordSize = item.Size,
                RecordPos = item.Pos,
                FilePath = item.Path,
                // IsMdd = m.IsMdd,
            };
            arrayBuilder.Add(tableEntry);

            currentOffset += item.Size;
        }

        if (bufferArray is not null)
            _arrayPool.Return(bufferArray);

        // pretty print it here
        // {
        //     Console.WriteLine("---- Offset Table ----");
        //
        //     int index = 0;
        //     foreach (var entry in _offsetTable)
        //     {
        //         string key = _encoding.GetString(entry.Key);
        //         string keyNull = _encoding.GetString(entry.KeyNull);
        //         string recordNull = _encoding.GetString(entry.RecordNull);
        //         string valuePreview = _encoding.GetString(entry.RecordNull)
        //             .TrimEnd('\0')
        //             .Replace("\r", "")
        //             .Replace("\n", " ");
        //
        //         valuePreview = $"{valuePreview[..40]}...";
        //
        //         Console.WriteLine(
        //             $"[{index}] " +
        //             $"Key=\"{key}\", " +
        //             $"Offset={entry.Offset}, " +
        //             $"KeyNull=\"{keyNull}\", " +
        //             $"KeyLen={entry.KeyLen}, " +
        //             $"RecordNull={recordNull}, " +
        //             $"RecordPos={entry.RecordPos}, " +
        //             $"RecordSize={entry.RecordSize}, " +
        //             $"Path=\"{valuePreview}\""
        //         );
        //
        //         index++;
        //     }
        //
        //     Console.WriteLine("----------------------");
        // }

        var tableEntries = arrayBuilder.MoveToImmutable();
        LogInfo(tableEntries.Length, currentOffset);

        return new OffsetTable(tableEntries);
    }

    private sealed record EncodingSettings(
        Encoding InnerEncoding, // _python_encoding in the original
        Encoding Encoding,
        int EncodingLength);

    private static EncodingSettings GetEncodingSettings(MDictMetadata m)
    {
        var encoding = m.Encoding.ToLower();
        Debug.Assert(encoding == "utf8");

        if (m.IsMdd || encoding == "utf16" || encoding == "utf-16")
        {
            return new(
                InnerEncoding: Encoding.Unicode,
                Encoding: Encoding.Unicode,
                EncodingLength: 2);
        }
        else if (encoding == "utf8" || encoding == "utf-8")
        {
            return new(
                InnerEncoding: Encoding.UTF8,
                Encoding: Encoding.UTF8,
                EncodingLength: 1);
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
            int encLength = encoder.InnerEncoding.GetByteCount(entry.Key);
            maxEncLength = int.Max(maxEncLength, encLength);
        }
        maxEncLength += encoder.EncodingLength; // Because we'll be appending an extra '\0' character.
        return maxEncLength;
    }

    [LoggerMessage(LogLevel.Debug,
    "Total entries: {Count}, record length {RecordLength}")]
    partial void LogInfo(int count, long RecordLength);
}
