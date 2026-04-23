using System.Buffers;
using System.Text;

namespace MDictUtils.Write;

internal sealed class HeaderWriter
{
    private static readonly MemoryPool<byte> _memoryPool = MemoryPool<byte>.Shared;

    public async Task WriteAsync(Stream stream, MDictHeader header)
    {
        var xmlString = header.ToString();
        var xmlSize = Encoding.Unicode.GetByteCount(xmlString);
        var headerSize = xmlSize + 8;

        using var memoryOwner = _memoryPool.Rent(headerSize);

        var headerBytes = memoryOwner.Memory[..headerSize];

        var sizeBytes = headerBytes.Span[..4];
        var xmlBytes = headerBytes.Span[4..^4];
        var checksumBytes = headerBytes.Span[^4..headerSize];

        // Size
        Common.ToBigEndian((uint)xmlSize, sizeBytes);

        // XML
        Encoding.Unicode.GetBytes(xmlString, xmlBytes);

        // Checksum
        uint checksum = Common.Adler32(xmlBytes);
        Common.ToLittleEndian(checksum, checksumBytes);

        // Output
        await stream.WriteAsync(headerBytes);
    }
}
