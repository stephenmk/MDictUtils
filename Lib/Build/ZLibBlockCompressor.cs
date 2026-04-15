using System.Buffers;

namespace Lib.Build;

internal sealed class ZLibBlockCompressor : IBlockCompressor
{
    public const int CompressionType = 2;
    private readonly static ArrayPool<byte> _arrayPool = ArrayPool<byte>.Shared;

    public ImmutableArray<byte> Compress(ReadOnlySpan<byte> data)
    {
        // Compression type (little-endian)
        Span<byte> compType = stackalloc byte[4];
        Common.ToLittleEndian((uint)CompressionType, compType); // <L in Python

        // Adler32 checksum (big-endian)
        uint adler = Common.Adler32(data);
        Span<byte> adlerBytes = stackalloc byte[4];
        Common.ToBigEndian(adler, adlerBytes); // Python uses >L

        // byte[] header = [.. lend, .. adlerBytes];

        // It's possible for compressed data to be larger than the uncompressed.
        // See: https://zlib.net/zlib_tech.html
        // "For the default settings, ... five bytes per 16 KB block (about 0.03%)"
        // So we have to rent a size a little bit larger.
        var buffer = _arrayPool.Rent(data.Length + (data.Length * 5 / 16_000) + 32);

        var size = ZLibCompression.Compress(data, buffer);

        ImmutableArray<byte> compressed = [.. compType, .. adlerBytes, .. buffer.AsSpan(..size)];
        _arrayPool.Return(buffer);

        // Console.WriteLine($"adler: {adler}");
        // Console.WriteLine($"header: {BitConverter.ToString(header)}");

        return compressed;
    }
}
