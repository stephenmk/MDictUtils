using System.Buffers;

namespace MDictUtils.Extensions;

internal static class ArrayPoolExtensions
{
    public static T[] Rent<T>(this ArrayPool<T> arrayPool, int minimumLength, ref T[]? referenceArray)
    {
        var arrayFromPool = arrayPool.Rent(minimumLength);
        referenceArray = arrayFromPool;
        return arrayFromPool;
    }
}
