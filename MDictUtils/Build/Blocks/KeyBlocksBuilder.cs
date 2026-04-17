using MDictUtils.BuildModels;
using Microsoft.Extensions.Logging;

namespace MDictUtils.Build.Blocks;

internal sealed class KeyBlocksBuilder
(
    ILogger<KeyBlocksBuilder> logger,
    IBlockCompressor blockCompressor
)
    : BlocksBuilder<KeyBlock>(logger, blockCompressor)
{
    public ImmutableArray<KeyBlock> Build(OffsetTable offsetTable, int desiredBlockSize)
        => BuildBlocks(offsetTable, desiredBlockSize);

    protected override KeyBlock BlockConstructor(int order, ReadOnlySpan<OffsetTableEntry> entries)
    {
        var block = GetCompressedBlock(entries);
        return new(order, block, entries);
    }

    protected override long GetByteCount(OffsetTableEntry entry)
        => entry.KeyBlockLength;

    protected override int WriteBytes(OffsetTableEntry entry, Span<byte> buffer)
    {
        Common.ToBigEndian((ulong)entry.Offset, buffer[..8]);
        entry.KeyNull.CopyTo(buffer[8..]);
        return entry.KeyBlockLength;
    }
}
