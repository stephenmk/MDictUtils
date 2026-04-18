using System.Text;

namespace MDictUtils.BuildModels;

internal sealed class OffsetTableEntry
{
    /// <summary>
    /// Bytes of the null-terminated entry key.
    /// Written immediately after this entry's "offset" value in the MDX/MDD file.
    /// For example, the key "apple" would have 6 bytes in UTF-8 (5 character bytes + 1 null character byte).
    /// In UTF-16, it would have twice as many (12) bytes.
    /// </summary>
    public required ImmutableArray<byte> NullTerminatedKeyBytes { get; init; }

    /// <summary>
    /// The number of encoded characters in the entry key (not including the appended null character).
    /// For example, the key "apple" contains five characters in both UTF-8 and UTF-16.
    /// </summary>
    public required int KeyCharacterCount { get; init; }

    /// <summary>
    /// The "offset" value of this entry.
    /// Written to the 8 bytes preceding this entry's null-appended key.
    /// </summary>
    public required long Offset { get; init; }

    /// <summary>
    /// Size of this entry's data in the file from which it is sourced.
    /// </summary>
    public required long RecordSize { get; init; }

    /// <summary>
    /// Position of this entry's data in the file from which this entry is sourced.
    /// </summary>
    public required long RecordPos { get; init; }

    /// <summary>
    /// Path of the file from which this entry is sourced.
    /// </summary>
    public required string FilePath { get; init; }

    /// <summary>
    /// Size of the uncompressed key data for this entry ("offset" value + null-appended key).
    /// </summary>
    public int KeyDataSize => 8 + NullTerminatedKeyBytes.Length;

    public override string ToString()
    {
        static string BytesToString(ReadOnlySpan<byte> bytes)
            => bytes.IsEmpty ? "null" : Encoding.UTF8.GetString(bytes);

        var sb = new StringBuilder();
        sb.Append("OffsetTableEntry(");
        sb.Append($"KeyLen={KeyCharacterCount}, ");
        sb.Append($"Offset={Offset}, ");
        sb.Append($"RecordPos={RecordPos}, ");
        sb.Append($"RecordSize={RecordSize}, ");
        sb.Append($"KeyNull='{BytesToString(NullTerminatedKeyBytes.AsSpan())}', ");
        sb.Append(')');
        return sb.ToString();
    }
}
