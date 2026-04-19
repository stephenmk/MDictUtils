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
    public ImmutableArray<KeyBlock> Build(OffsetTable offsetTable)
        => BuildBlocks(offsetTable);

    protected override KeyBlock BlockConstructor(ReadOnlySpan<OffsetTableEntry> entries)
    {
        var block = GetCompressedBlock(entries);
        return new(block, entries);
    }

    protected override int GetByteCount(OffsetTableEntry entry)
        => entry.KeyDataSize;

    protected override void WriteBytes(OffsetTableEntry entry, Span<byte> buffer)
    {
        Common.ToBigEndian((ulong)entry.Offset, buffer[..8]);
        entry.NullTerminatedKeyBytes.CopyTo(buffer[8..]);
    }

    protected override ImmutableArray<Range> GetBlockRanges(OffsetTable offsetTable)
        => offsetTable.KeyBlockRanges;
}
