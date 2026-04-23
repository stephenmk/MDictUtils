using System.Buffers;
using MDictUtils.BuildModels;

namespace MDictUtils.Build.Index;

internal sealed class RecordBlockIndexBuilder : IDisposable
{
    private bool _isDisposed;
    private readonly IMemoryOwner<byte> _indexMemoryOwner;

    /// <summary>
    /// Space to store the four 8-byte numbers in the index preamble.
    /// </summary>
    private const int PreambleSize = 4 * 8;

    /// <summary>
    /// Space to store the compressed size (8 bytes) and decompressed size (8 bytes) of each record block.
    /// </summary>
    private int BlocksSize => BlockCount * 16;

    public int BlockCount { get; }
    private int EntryCount { get; }
    public int IndexSize => PreambleSize + BlocksSize;
    public long TotalRecordSize { get; private set; }
    public long AverageRecordSize => BlockCount == 0 ? 0 : TotalRecordSize / BlockCount;

    public RecordBlockIndexBuilder(OffsetTable offsetTable)
    {
        BlockCount = offsetTable.RecordBlockRanges.Length;
        EntryCount = offsetTable.Length;
        TotalRecordSize = 0;
        _indexMemoryOwner = MemoryPool<byte>.Shared.Rent(IndexSize);
    }

    public void ReadBlock(RecordBlock block)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);

        TotalRecordSize += block.Bytes.Length;
        int start = PreambleSize + (block.Id * 16);
        var destination = _indexMemoryOwner.Memory.Span.Slice(start, 16);
        block.CopyIndexEntryTo(destination);
    }

    public async Task WriteAsync(Stream stream)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);

        var preamble = _indexMemoryOwner.Memory.Span[..PreambleSize];
        var r = new SpanReader<byte>(preamble) { ReadSize = 8 };

        Common.ToBigEndian((ulong)BlockCount, r.Read());
        Common.ToBigEndian((ulong)EntryCount, r.Read());
        Common.ToBigEndian((ulong)BlocksSize, r.Read());
        Common.ToBigEndian((ulong)TotalRecordSize, r.Read());

        var bytes = _indexMemoryOwner.Memory[..IndexSize];
        await stream.WriteAsync(bytes);
    }

    void IDisposable.Dispose()
    {
        if (!_isDisposed)
        {
            _indexMemoryOwner.Dispose();
            _isDisposed = true;
        }
    }
}
