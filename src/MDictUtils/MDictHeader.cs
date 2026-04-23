using System.Text;

namespace MDictUtils;

public abstract record MDictHeader
{
    public string Title { get; init; } = "";
    public string Description { get; init; } = "";
    public string Version { get; init; } = "2.0";
    public DateOnly CreationDate { get; init; } = DateOnly.FromDateTime(DateTime.Today);
    protected string DateString => $"{CreationDate.Year}-{CreationDate.Month}-{CreationDate.Day}";

    // Same as python: escape(self._description, quote=True),
    // System.Web.HttpUtility.HtmlAttributeEncode(s) doesn't do the trick...
    protected static string EscapeHtml(string s)
    {
        return s
            .Replace("&", "&amp;")   // Must be first
            .Replace("<", "&lt;")
            .Replace(">", "&gt;")
            .Replace("\"", "&quot;")
            .Replace("'", "&#x27;");
    }
}

public sealed record MdxHeader : MDictHeader
{
    public override string ToString()
    {
        var sb = new StringBuilder();

        void append(ReadOnlySpan<char> val)
        {
            sb.Append(val.Trim());
            sb.Append(' ');
        }

        append($"""  <Dictionary                              """);
        append($"""  GeneratedByEngineVersion="{Version}"     """);
        append($"""  RequiredEngineVersion="{Version}"        """);
        append($"""  Encrypted="No"                           """);
        append($"""  Encoding="UTF-8"                         """);
        append($"""  Format="Html"                            """);
        append($"""  Stripkey="Yes"                           """);
        append($"""  CreationDate="{DateString}"              """);
        append($"""  Compact="Yes"                            """);
        append($"""  Compat="Yes"                             """);
        append($"""  KeyCaseSensitive="No"                    """);
        append($"""  Description="{EscapeHtml(Description)}"  """);
        append($"""  Title="{EscapeHtml(Title)}"              """);
        append($"""  DataSourceFormat="106"                   """);
        append($"""  StyleSheet=""                            """);
        append($"""  Left2Right="Yes"                         """);
        append($"""  RegisterBy=""                            """);

        sb.Append("/>\r\n\0");
        return sb.ToString();
    }
}

public sealed record MddHeader : MDictHeader
{
    public override string ToString()
    {
        var sb = new StringBuilder();

        void append(ReadOnlySpan<char> val)
        {
            sb.Append(val.Trim());
            sb.Append(' ');
        }

        append($"""  <Library_Data                            """);
        append($"""  GeneratedByEngineVersion="{Version}"     """);
        append($"""  RequiredEngineVersion="{Version}"        """);
        append($"""  Encrypted="No"                           """);
        append($"""  Encoding=""                              """);
        append($"""  Format=""                                """);
        append($"""  CreationDate="{DateString}"              """);
        append($"""  KeyCaseSensitive="No"                    """);
        append($"""  Stripkey="No"                            """);
        append($"""  Description="{EscapeHtml(Description)}"  """);
        append($"""  Title="{EscapeHtml(Title)}"              """);
        append($"""  RegisterBy=""                            """);

        sb.Append("/>\r\n\0");
        return sb.ToString();
    }
}
