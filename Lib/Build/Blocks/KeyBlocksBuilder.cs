using Lib.BuildModels;
using Microsoft.Extensions.Logging;

namespace Lib.Build.Blocks;

internal sealed class KeyBlocksBuilder
(
    ILogger<KeyBlocksBuilder> logger,
    IBlockCompressor blockCompressor
)
    : BlocksBuilder<KeyBlock>(logger, blockCompressor)
{
    public List<KeyBlock> Build(OffsetTable offsetTable, int blockSize)
        => BuildBlocks(offsetTable, blockSize);

    protected override KeyBlock BlockConstructor(ReadOnlySpan<OffsetTableEntry> entries)
    {
        var block = GetCompressedBlock(entries);
        return new(block, entries);
    }

    protected override long GetByteCount(OffsetTableEntry entry)
        => entry.KeyBlockLength;

    protected override int WriteBytes(OffsetTableEntry entry, Span<byte> buffer)
    {
        Common.ToBigEndian((ulong)entry.Offset, buffer[..8]);
        entry.KeyNull.CopyTo(buffer[8..]);
        return 8 + entry.KeyNull.Length;
    }
}
