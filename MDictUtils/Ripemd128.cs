using System.Diagnostics;
using Org.BouncyCastle.Crypto.Digests;

namespace MDictUtils;

internal static class Ripemd128
{
    // Wrapper in case we end up implementing it ourselves
    public static int ComputeHash(ReadOnlySpan<byte> message, Span<byte> hash)
    {
        return ComputeRipeMd128Hash(message, hash);
    }

    /// Source for <see cref="RipeMD128Digest"/>
    /// https://github.com/bcgit/bc-csharp/blob/4b87b5e7d6b42d1028838efe356730411446a8f5/crypto/src/crypto/digests/RipeMD128Digest.cs#L11
    private static int ComputeRipeMd128Hash(ReadOnlySpan<byte> message, Span<byte> hash)
    {
        var ripemd128 = new RipeMD128Digest();
        ripemd128.BlockUpdate(message);
        int size = ripemd128.GetDigestSize();
        ripemd128.DoFinal(hash[..size]);
        return size;
    }

    // Shouldn't be here
    // https://github.com/liuyug/mdict-utils/blob/64e15b99aca786dbf65e5a2274f85547f8029f2e/mdict_utils/base/readmdict.py#L58
    public static void FastDecrypt(ReadOnlySpan<byte> data, ReadOnlySpan<byte> key, Span<byte> result)
    {
        Debug.Assert(data.Length == result.Length);
        byte previous = 0x36;

        for (int i = 0; i < data.Length; i++)
        {
            byte current = data[i];

            // Rotate nibbles: (b >> 4 | b << 4) & 0xff
            byte t = (byte)(((current >> 4) | (current << 4)) & 0xFF);

            t = (byte)(t ^ previous ^ (i & 0xFF) ^ key[i % key.Length]);

            previous = current;
            result[i] = t;
        }
    }
}
