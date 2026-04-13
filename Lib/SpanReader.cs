using System;

namespace Lib;

internal ref struct SpanReader<T>
{
    private readonly Span<T> _data;
    private int _start;
    public int? ReadSize { get; set; }

    public SpanReader(Span<T> data)
    {
        _data = data;
        _start = 0;
    }

    public Span<T> Read(int length)
    {
        int end = _start + length;
        Range range = new(_start, end);
        _start = end;
        return _data[range];
    }

    public Span<T> Read()
        => ReadSize.HasValue
            ? Read(ReadSize.Value)
            : throw new InvalidOperationException();
}
