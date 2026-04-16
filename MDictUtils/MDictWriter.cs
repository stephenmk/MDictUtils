using System.Text;
using MDictUtils.Build;
using MDictUtils.BuildModels;

namespace MDictUtils;

public sealed record MDictEntry(string Key, long Pos, string Path, long Size)
{
    public override string ToString()
        => $"Key=\"{Key}\", Pos={Pos}, Size={Size}";
}

#pragma warning disable format
public sealed record MDictMetadata
(
    string Title           = "",
    string Description     = "",
    int    KeySize         = 32768,
    int    BlockSize       = 65536,
    string Encoding        = "utf8",
    int    CompressionType = 2,
    string Version         = "2.0",
    bool   IsMdd           = false
);
#pragma warning restore format

public sealed class MDictWriter
{
    private readonly MDictData _data;

    public MDictWriter(List<MDictEntry> entries, MDictMetadata? metadata = null, bool logging = true)
    {
        metadata ??= new();

        if (metadata.Version != "2.0")
            throw new NotSupportedException("Unknown version. Supported: 2.0");

        var builder = DataBuilderProvider.GetDataBuilder(metadata, logging);
        _data = builder.BuildData(entries, metadata);
    }

    public void Write(string filepath)
    {
        using var stream = new FileStream(filepath, FileMode.Create, FileAccess.Write, FileShare.None);
        WriteHeader(stream);
        WriteKeySection(stream);
        WriteRecordSection(stream);
    }

    private void WriteHeader(Stream stream)
    {
        var header = GetHeaderString();

        // Encode to UTF-16 LE (must be identical to python .encode("utf_16_le")
        ReadOnlySpan<byte> headerBytes = Encoding.Unicode.GetBytes(header);
        // Console.WriteLine($"header bytes: {headerBytes.Length}");
        // Console.WriteLine("        " + string.Join(" ", headerBytes.Select(b => b.ToString("X2"))));

        // Write header length (big-endian)
        Span<byte> lengthBytes = stackalloc byte[4];
        Common.ToBigEndian((uint)headerBytes.Length, lengthBytes);
        stream.Write(lengthBytes);

        // Write header string
        stream.Write(headerBytes);

        // Write Adler32 checksum (little-endian)
        uint checksum = Common.Adler32(headerBytes);
        Span<byte> checksumBytes = stackalloc byte[4];
        Common.ToLittleEndian(checksum, checksumBytes);

        stream.Write(checksumBytes);
    }

    internal string GetHeaderString()
    {
        const string encrypted = "No";
        const string registerByStr = "";
        const string encoding = "UTF-8";

        var now = DateTime.Today;
        var sb = new StringBuilder();

        void append(ReadOnlySpan<char> val)
        {
            sb.Append(val.Trim());
            sb.Append(' ');
        }

        if (_data.IsMdd)
        {
            append($"""  <Library_Data                                    """);
            append($"""  GeneratedByEngineVersion="{_data.Version}"       """);
            append($"""  RequiredEngineVersion="{_data.Version}"          """);
            append($"""  Encrypted="{encrypted}"                          """);
            append($"""  Encoding=""                                      """);
            append($"""  Format=""                                        """);
            append($"""  CreationDate="{now.Year}-{now.Month}-{now.Day}"  """);
            append($"""  KeyCaseSensitive="No"                            """);
            append($"""  Stripkey="No"                                    """);
            append($"""  Description="{EscapeHtml(_data.Description)}"    """);
            append($"""  Title="{EscapeHtml(_data.Title)}"                """);
            append($"""  RegisterBy="{registerByStr}"                     """);
        }
        else
        {
            append($"""  <Dictionary                                      """);
            append($"""  GeneratedByEngineVersion="{_data.Version}"       """);
            append($"""  RequiredEngineVersion="{_data.Version}"          """);
            append($"""  Encrypted="{encrypted}"                          """);
            append($"""  Encoding="{encoding}"                            """);
            append($"""  Format="Html"                                    """);
            append($"""  Stripkey="Yes"                                   """);
            append($"""  CreationDate="{now.Year}-{now.Month}-{now.Day}"  """);
            append($"""  Compact="Yes"                                    """);
            append($"""  Compat="Yes"                                     """);
            append($"""  KeyCaseSensitive="No"                            """);
            append($"""  Description="{EscapeHtml(_data.Description)}"    """);
            append($"""  Title="{EscapeHtml(_data.Title)}"                """);
            append($"""  DataSourceFormat="106"                           """);
            append($"""  StyleSheet=""                                    """);
            append($"""  Left2Right="Yes"                                 """);
            append($"""  RegisterBy="{registerByStr}"                     """);
        }
        sb.Append("/>\r\n\0");
        return sb.ToString();
    }

    // Same as python: escape(self._description, quote=True),
    // System.Web.HttpUtility.HtmlAttributeEncode(s) doesn't do the trick...
    private static string EscapeHtml(string s)
    {
        return s
            .Replace("&", "&amp;")   // Must be first
            .Replace("<", "&lt;")
            .Replace(">", "&gt;")
            .Replace("\"", "&quot;")
            .Replace("'", "&#x27;");
    }

    private void WriteKeySection(Stream outfile)
    {
        Span<byte> preamble = stackalloc byte[5 * 8]; // Five 8-byte buffers
        var r = new SpanReader<byte>(preamble) { ReadSize = 8 };

        Common.ToBigEndian((ulong)_data.KeyBlocks.Count, r.Read());
        Common.ToBigEndian((ulong)_data.EntryCount, r.Read());
        Common.ToBigEndian((ulong)_data.KeyBlockIndex.DecompSize, r.Read());
        Common.ToBigEndian((ulong)_data.KeyBlockIndex.Size, r.Read());
        Common.ToBigEndian((ulong)_data.KeyBlocksSize, r.Read());

        uint checksumValue = Common.Adler32(preamble);
        Span<byte> checksum = stackalloc byte[4];
        Common.ToBigEndian(checksumValue, checksum);

        outfile.Write(preamble);
        outfile.Write(checksum);
        outfile.Write(_data.KeyBlockIndex.Bytes.AsSpan());

        foreach (var block in _data.KeyBlocks)
        {
            outfile.Write(block.Bytes.AsSpan());
        }
    }

    private void WriteRecordSection(Stream outfile)
    {
        Span<byte> preamble = stackalloc byte[4 * 8]; // Four 8-byte buffers
        var r = new SpanReader<byte>(preamble) { ReadSize = 8 };

        Common.ToBigEndian((ulong)_data.RecordBlocks.Count, r.Read());
        Common.ToBigEndian((ulong)_data.EntryCount, r.Read());
        Common.ToBigEndian((ulong)_data.RecordBlockIndex.Size, r.Read());
        Common.ToBigEndian((ulong)_data.RecordBlocksSize, r.Read());

        outfile.Write(preamble);
        outfile.Write(_data.RecordBlockIndex.Bytes.AsSpan());

        foreach (var block in _data.RecordBlocks)
        {
            outfile.Write(block.Bytes.AsSpan());
        }
    }
}
