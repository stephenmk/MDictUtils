using System.Buffers;
using System.Diagnostics;
using MDictUtils.BuildModels;

namespace MDictUtils.Build.Compression;

internal sealed class ZLibBlockCompressor(BuildOptions options) : IBlockCompressor
{
    private static readonly ArrayPool<byte> _arrayPool = ArrayPool<byte>.Shared;
    private static readonly MemoryPool<byte> _memoryPool = MemoryPool<byte>.Shared;

    /// <summary>
    /// CompressionType = 2 expressed in little-endian bytes. See <see cref="MDict.DecodeKeyBlockInfo"/>.
    /// </summary>
    private static ReadOnlySpan<byte> CompressionTypeBytes => [0x02, 0x00, 0x00, 0x00];

    public async Task<CompressedBlock> CompressAsync(ReadOnlyMemory<byte> data)
    {
        // It's possible for compressed data to be larger than the uncompressed.
        // See: https://zlib.net/zlib_tech.html
        // "For the default settings, ... five bytes per 16 KB block (about 0.03%)"
        // So we have to rent a size a little bit larger.
        var buffer = _arrayPool.Rent(data.Length + (data.Length * 5 / 16_000) + 32);
        var size = await ZLibCompression.CompressAsync(data, buffer, options.CompressionLevel);

        uint checksum = Common.Adler32(data.Span);
        var compressedSize = size + 8;
        var memoryOwner = _memoryPool.Rent(compressedSize);
        var compressed = memoryOwner.Memory.Span[..compressedSize];

        CompressionTypeBytes.CopyTo(compressed[..4]);
        Common.ToBigEndian(checksum, compressed[4..8]);
        buffer.AsSpan(..size).CopyTo(compressed[8..]);

        // ZLib also appends the same Adler32 checksum to the final 4 bytes.
        Debug.Assert(compressed[4..8].SequenceEqual(compressed[^4..]));

        _arrayPool.Return(buffer);

        return new(memoryOwner, compressedSize, data.Length);
    }
}
