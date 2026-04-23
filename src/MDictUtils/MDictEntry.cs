namespace MDictUtils;

/// <summary>
/// A single MDict entry consisting of a key and entry data located on disk.
/// </summary>
/// <param name="Key">Entry headword (MDX) or a media resource path (MDD)</param>
/// <param name="Path">Path to the file from which this entry's data is sourced.</param>
/// <param name="Pos">Position of this entry's data in the file from which it is sourced.</param>
/// <param name="Size">Size of this entry's data in the file from which it is sourced.</param>
public sealed record MDictEntry
(
    string Key,
    string Path,
    long Pos,
    int Size
)
{
    public override string ToString()
        => $"Key=\"{Key}\", Pos={Pos}, Size={Size}";
}
