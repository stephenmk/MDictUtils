using System.Text;

namespace MDictUtils.Write;

internal sealed class HeaderWriter
{
    public async Task WriteAsync(Stream stream, MDictHeader header)
    {
        var headerString = header.ToString();

        // Encode header to little-endian UTF-16
        ReadOnlyMemory<byte> headerBytes = Encoding.Unicode.GetBytes(headerString);

        // Write header length (big-endian)
        Span<byte> lengthBytes = stackalloc byte[4];
        Common.ToBigEndian((uint)headerBytes.Length, lengthBytes);
        stream.Write(lengthBytes);

        // Write header string
        await stream.WriteAsync(headerBytes);

        // Write Adler32 checksum (little-endian)
        uint checksum = Common.Adler32(headerBytes.Span);
        Span<byte> checksumBytes = stackalloc byte[4];
        Common.ToLittleEndian(checksum, checksumBytes);

        stream.Write(checksumBytes);
    }
}
