using System.Buffers;

namespace Lib.Build.Compression;

internal sealed class ZLibBlockCompressor : IBlockCompressor
{
    public const uint CompressionType = 2;
    private readonly static ArrayPool<byte> _arrayPool = ArrayPool<byte>.Shared;

    public ImmutableArray<byte> Compress(ReadOnlySpan<byte> data)
    {
        Span<byte> compressionTypeBytes = stackalloc byte[4];
        Common.ToLittleEndian(CompressionType, compressionTypeBytes);

        uint checksum = Common.Adler32(data);
        Span<byte> checksumBytes = stackalloc byte[4];
        Common.ToBigEndian(checksum, checksumBytes);

        // It's possible for compressed data to be larger than the uncompressed.
        // See: https://zlib.net/zlib_tech.html
        // "For the default settings, ... five bytes per 16 KB block (about 0.03%)"
        // So we have to rent a size a little bit larger.
        var buffer = _arrayPool.Rent(data.Length + (data.Length * 5 / 16_000) + 32);

        var size = ZLibCompression.Compress(data, buffer);

        ImmutableArray<byte> compressed =
        [
            .. compressionTypeBytes,
            .. checksumBytes,
            .. buffer.AsSpan(..size),
        ];

        _arrayPool.Return(buffer);

        return compressed;
    }
}
