using System.Buffers;
using Lib.Build;

namespace Lib.BuildModels;

/// <summary>
/// Abstract base class for <see cref="MdxRecordBlock"/> and <see cref="MdxKeyBlock"/>.
/// </summary>
internal abstract class MdxBlock
{
    private readonly static IBlockCompressor _blockCompressor = new ZLibBlockCompressor();
    private readonly static ArrayPool<byte> _arrayPool = ArrayPool<byte>.Shared;
    protected readonly MdxBlockData _blockData;

    protected MdxBlock(ReadOnlySpan<OffsetTableEntry> offsetTableEntries)
    {
        // Console.WriteLine("[Debug] Calling MdxBlock...");

        int decompDataSize = Convert.ToInt32(offsetTableEntries.Sum(BlockEntryLength));
        var decompData = _arrayPool.Rent(decompDataSize);

        var maxBlockSize = Convert.ToInt32(offsetTableEntries.Max(BlockEntryLength));
        byte[]? blockArray = null;
        var blockBuffer = maxBlockSize < 256
            ? stackalloc byte[maxBlockSize]
            : (blockArray = _arrayPool.Rent(maxBlockSize));

        int totalSize = 0;
        foreach (var entry in offsetTableEntries)
        {
            int blockSize = GetBlockEntry(entry, blockBuffer);
            // Console.WriteLine($"[Debug] BlockEntry ({blockEntry.Length} bytes): {BitConverter.ToString(blockEntry)}");
            var source = blockBuffer[..blockSize];
            var destination = decompData.AsSpan(start: totalSize, length: blockSize);
            source.CopyTo(destination);
            totalSize += blockSize;
        }

        if (blockArray is not null)
            _arrayPool.Return(blockArray);

        // Console.WriteLine("[Debug] Building MdxBlock...");
        // Console.WriteLine($"[Debug] Decompressed array length (_decompSize): {_decompSize}");
        // Common.PrintPythonStyle(decompArray);

        var compressedData = _blockCompressor.Compress(decompData[..totalSize]);

        _blockData = new(compressedData, DecompSize: totalSize);

        // Console.WriteLine($"[Debug] Compressed array length (_compSize): {_compSize}");

        _arrayPool.Return(decompData);
    }

    protected readonly record struct MdxBlockData(ImmutableArray<byte> CompressedBytes, long DecompSize)
    {
        public int CompressedSize => CompressedBytes.Length;
    }

    public ImmutableArray<byte> BlockData => _blockData.CompressedBytes;

    public abstract void GetIndexEntry(Span<byte> buffer);
    protected abstract int GetBlockEntry(OffsetTableEntry entry, Span<byte> buffer);
    public abstract long BlockEntryLength(OffsetTableEntry entry);
    public abstract int IndexEntryLength { get; }
}
