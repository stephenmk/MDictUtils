namespace MDictUtils.Build.Offset;

internal sealed class MdxKeyComparer : KeyComparer
{
    public override int Compare(ReadOnlySpan<char> k1, ReadOnlySpan<char> k2)
    {
        if (RegexStrip.IsMatch(k1))
            k1 = StripPunctuation(k1);

        if (RegexStrip.IsMatch(k2))
            k2 = StripPunctuation(k2);

        int cmp = k1.CompareTo(k2, StringComparison.OrdinalIgnoreCase);
        if (cmp != 0)
            return cmp;

        // reverse length (longer first) - compare on current k1/k2
        if (k1.Length != k2.Length)
            return k2.Length.CompareTo(k1.Length);

        return k2.CompareTo(k1, StringComparison.OrdinalIgnoreCase);
    }

    private static ReadOnlySpan<char> StripPunctuation(ReadOnlySpan<char> text)
    {
        Span<char> buffer = new char[text.Length];

        int lastIndex = 0;
        int charsWritten = 0;

        foreach (var match in RegexStrip.EnumerateMatches(text))
        {
            text[lastIndex..match.Index].CopyTo(buffer[charsWritten..]);
            charsWritten += match.Index - lastIndex;
            lastIndex = match.Index + match.Length;
        }

        text[lastIndex..text.Length].CopyTo(buffer[charsWritten..]);
        charsWritten += text.Length - lastIndex;

        return buffer[..charsWritten];
    }
}
