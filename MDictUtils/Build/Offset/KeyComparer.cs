using System.Text.RegularExpressions;

namespace MDictUtils.Build.Offset;

internal abstract partial class KeyComparer : IKeyComparer
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

    public abstract int Compare(ReadOnlySpan<char> k1, ReadOnlySpan<char> k2);
}
