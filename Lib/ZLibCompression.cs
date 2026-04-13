using System;
using System.IO;
using System.IO.Compression;

#if ALLOW_UNSAFE_BLOCKS
using System.Runtime.InteropServices;
#endif

namespace Lib;

internal static class ZLibCompression
{

#if ALLOW_UNSAFE_BLOCKS

    public static unsafe int Compress(ReadOnlySpan<byte> input, Span<byte> output)
    {
        fixed (byte* pBuffer = &MemoryMarshal.GetReference(output))
        {
            using var ms = new UnmanagedMemoryStream(pBuffer, output.Length, output.Length, FileAccess.Write);
            using (var z = new ZLibStream(ms, CompressionLevel.Optimal, leaveOpen: true))
            {
                z.Write(input);
            }
            return Convert.ToInt32(ms.Length);
        }
    }

    public static unsafe void Decompress(ReadOnlySpan<byte> input, Span<byte> output)
    {
        fixed (byte* pBuffer = &MemoryMarshal.GetReference(input))
        {
            using var ms = new UnmanagedMemoryStream(pBuffer, input.Length, input.Length, FileAccess.Read);
            using var z = new ZLibStream(ms, CompressionMode.Decompress);

            z.ReadExactly(output);

            if (z.ReadByte() is not -1)
            {
                throw new OverflowException($"More than expected {output.Length} bytes in decompression stream");
            }
        }
    }

#else

    /// python default is -1 == 6 , see: https://docs.python.org/3/library/zlib.html#zlib.Z_DEFAULT_COMPRESSION
    /// c# are cooked, custom-made levels, and may not correspond to anything
    /// https://learn.microsoft.com/en-us/dotnet/api/system.io.compression.compressionlevel?view=net-10.0
    ///
    /// There is no reliable way to get the same exact bytes, so live with that
    public static int Compress(ReadOnlySpan<byte> input, byte[] output)
    {
        using var ms = new MemoryStream(output);
        using (var z = new ZLibStream(ms, CompressionLevel.Optimal, leaveOpen: true))
        {
            z.Write(input);
        }
        return Convert.ToInt32(ms.Length);
    }

    /// The .ToArray() allocation here is unfortunately unavoidable.
    /// See: https://github.com/dotnet/runtime/issues/24622
    /// Unless we want to enable the "unsafe" compiler flag.
    /// See: https://stackoverflow.com/a/48223990
    public static void Decompress(ReadOnlySpan<byte> input, Span<byte> output)
    {
        using var ms = new MemoryStream(input.ToArray());
        using var z = new ZLibStream(ms, CompressionMode.Decompress);

        z.ReadExactly(output);

        if (z.ReadByte() is not -1)
        {
            throw new OverflowException($"More than expected {output.Length} bytes in decompression stream");
        }
    }

#endif

}
