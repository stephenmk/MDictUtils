using MDictUtils.BuildModels;
using Microsoft.Extensions.Logging;

namespace MDictUtils.Build.Blocks;

internal sealed class MdxRecordBlocksBuilder
(
    ILogger<MdxRecordBlocksBuilder> logger,
    IBlockCompressor blockCompressor
)
    : RecordBlocksBuilder(logger, blockCompressor)
{
    private FileStreams? _fileStreams;

    public override List<RecordBlock> Build(OffsetTable offsetTable, int blockSize)
    {
        using var fileStreams = new FileStreams();
        _fileStreams = fileStreams;
        return BuildBlocks(offsetTable, blockSize);
    }

    protected override long GetByteCount(OffsetTableEntry entry)
        => entry.RecordSize;

    protected override RecordBlock BlockConstructor(ReadOnlySpan<OffsetTableEntry> entries)
    {
        var block = GetCompressedBlock(entries);
        return new(block);
    }

    protected override int WriteBytes(OffsetTableEntry entry, Span<byte> buffer)
    {
        int size = Convert.ToInt32(entry.RecordSize);
        if (size < 1)
            throw new InvalidDataException("Size must be >= 1");

        var stream = _fileStreams!.GetStream(entry.FilePath);
        stream.Seek(entry.RecordPos, SeekOrigin.Begin);

        // For MDX, read size-1 bytes and append null byte
        stream.ReadExactly(buffer[..(size - 1)]);
        buffer[size - 1] = 0; // null-terminate
        return size;
    }
}
