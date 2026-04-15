using System.Text;

namespace Lib.BuildModels;

internal class OffsetTableEntry
{
    // public required byte[] Key { get; init; }
    public required ImmutableArray<byte> KeyNull { get; init; }
    public required int KeyLen { get; init; }
    public required long Offset { get; init; }
    // public required byte[] RecordNull { get; set; }
    // public required bool IsMdd { get; init; }
    public required long RecordSize { get; init; }
    public required long RecordPos { get; init; }

    // Weird stuff from get_record_null()
    public required string FilePath { get; init; }

    public long KeyBlockLength => 8 + KeyNull.Length;
    public long RecordBlockLength => RecordSize;

    public override string ToString()
    {
        static string BytesToString(ReadOnlySpan<byte> bytes)
            => bytes.IsEmpty ? "null" : Encoding.UTF8.GetString(bytes);

        var sb = new StringBuilder();
        sb.Append("OffsetTableEntry(");
        sb.Append($"KeyLen={KeyLen}, ");
        sb.Append($"Offset={Offset}, ");
        sb.Append($"RecordPos={RecordPos}, ");
        sb.Append($"RecordSize={RecordSize}, ");
        // sb.Append($"IsMdd='{IsMdd}', ");
        // sb.Append($"Key='{BytesToString(Key)}', ");
        sb.Append($"KeyNull='{BytesToString(KeyNull.AsSpan())}', ");
        // sb.Append($"RecordNull='{BytesToString(RecordNull)}'");
        sb.Append(')');
        return sb.ToString();
    }
}
