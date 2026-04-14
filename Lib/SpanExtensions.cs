using System.Numerics;

namespace Lib;

internal static class SpanExtensions
{
    public static TNumber Sum<TItem, TNumber>(this ReadOnlySpan<TItem> items, Func<TItem, TNumber> summer)
        where TNumber : IBinaryInteger<TNumber>
    {
        TNumber sum = TNumber.Zero;
        foreach (var item in items)
        {
            sum += summer(item);
        }
        return sum;
    }

    public static TNumber Max<TItem, TNumber>(this ReadOnlySpan<TItem> items, Func<TItem, TNumber> selector)
        where TNumber : IBinaryInteger<TNumber>
    {
        TNumber max = TNumber.Zero;
        foreach (var item in items)
        {
            max = TNumber.Max(max, selector(item));
        }
        return max;
    }
}
