namespace Lib.Build.Offset;

internal class MddKeyComparer : KeyComparer
{
    public override int Compare(ReadOnlySpan<char> k1, ReadOnlySpan<char> k2)
    {
        int cmp = k1.CompareTo(k2, StringComparison.OrdinalIgnoreCase);
        if (cmp != 0)
            return cmp;

        // reverse length (longer first) - compare on current k1/k2
        if (k1.Length != k2.Length)
            return k2.Length.CompareTo(k1.Length);

        // trim punctuation
        k1 = k1.TrimEnd(PunctuationChars);
        k2 = k2.TrimEnd(PunctuationChars);

        return k2.CompareTo(k1, StringComparison.OrdinalIgnoreCase);
    }
}
