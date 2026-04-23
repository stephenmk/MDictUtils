using System.Text;

namespace MDictUtils;

public abstract record MDictWriterOptions
{
    public int DesiredKeyBlockSize { get; set; } = 32_768;
    public int DesiredRecordBlockSize { get; set; } = 65_536;
    public MDictCompressionType CompressionType { get; set; } = MDictCompressionType.ZLib;
}

public sealed record MdxWriterOptions : MDictWriterOptions
{
    public MDictKeyEncodingType KeyEncoding { get; set; } = MDictKeyEncodingType.Utf8;
}

public sealed record MddWriterOptions : MDictWriterOptions;

public enum MDictCompressionType : uint
{
    None = 0,
    LZO = 1,
    ZLib = 2,
}

public enum MDictKeyEncodingType : byte
{
    Utf8,
    Utf16,
}

internal static class MDictKeyEncodingTypeExtensions
{
    public static Encoding ToEncoding(this MDictKeyEncodingType type)
        => type switch
        {
            MDictKeyEncodingType.Utf8 => Encoding.UTF8,
            MDictKeyEncodingType.Utf16 => Encoding.Unicode,
            _ => throw new NotSupportedException("Unknown encoding. Supported: utf8, utf16")
        };

    /// <summary>
    /// Gets the number of bytes per character in the encoding type.
    /// </summary>
    public static int ToEncodingLength(this MDictKeyEncodingType type)
        => type switch
        {
            MDictKeyEncodingType.Utf8 => 1,
            MDictKeyEncodingType.Utf16 => 2,
            _ => throw new NotSupportedException("Unknown encoding. Supported: utf8, utf16")
        };
}
