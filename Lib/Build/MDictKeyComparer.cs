using System.Text.RegularExpressions;

namespace Lib.Build;

internal partial class MDictKeyComparer
{
    /// <summary>
    /// https://docs.python.org/3/library/string.html#string.punctuation
    /// </summary>
    public const string PunctuationChars = "!\"#$%&'()*+,-./:;<=>?@[\\]^_`{|}~";

    /// <summary>
    /// Regex to strip the python punctuation characters, and also the space character.
    /// </summary>
    [GeneratedRegex(@"[!\""#$%&'()*+,\-./:;<=>?@\[\\\]^_`{|}~ ]+")]
    public static partial Regex RegexStrip { get; }

    public int Compare(ReadOnlySpan<char> k1, ReadOnlySpan<char> k2, bool isMdd)
    {
        if (!isMdd)
        {
            if (RegexStrip.IsMatch(k1))
                k1 = StripPunctuation(k1);

            if (RegexStrip.IsMatch(k2))
                k2 = StripPunctuation(k2);
        }

        // key1 = locale.strxfrm(key1) ??
        // this was locale dependent in py, but then we don't pass our tests,
        // and it shouldn't matter anyway as long as the internal mapping works
        int cmp = k1.CompareTo(k2, StringComparison.OrdinalIgnoreCase);

        if (cmp != 0)
            return cmp;

        // reverse length (longer first) - compare on current k1/k2
        if (k1.Length != k2.Length)
            return k2.Length.CompareTo(k1.Length);

        // trim punctuation (already stripped if this is not MDD)
        if (isMdd)
        {
            k1 = k1.TrimEnd(PunctuationChars);
            k2 = k2.TrimEnd(PunctuationChars);
        }

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
