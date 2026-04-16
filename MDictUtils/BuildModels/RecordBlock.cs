using System.Diagnostics;

namespace MDictUtils.BuildModels;

internal sealed class RecordBlock(int order, CompressedBlock block) : MDictBlock(order, block)
{
    public override int IndexEntryLength => 16;

    public override void CopyIndexEntryTo(Span<byte> buffer)
    {
        Debug.Assert(buffer.Length == IndexEntryLength);

        Common.ToBigEndian((ulong)_block.Size, buffer[..8]);
        Common.ToBigEndian((ulong)_block.DecompSize, buffer[8..16]);
    }
}
