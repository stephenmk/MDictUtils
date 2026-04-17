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

    public override ImmutableArray<RecordBlock> Build(OffsetTable offsetTable, int desiredBlockSize)
    {
        var pathToTotalEntryCount = offsetTable.GetFilePathToTotalEntryCount();
        using var fileStreams = new FileStreams(pathToTotalEntryCount);
        _fileStreams = fileStreams;
        return BuildBlocks(offsetTable, desiredBlockSize);
    }

    protected override long GetByteCount(OffsetTableEntry entry)
        => entry.RecordSize;

    protected override RecordBlock BlockConstructor(int order, ReadOnlySpan<OffsetTableEntry> entries)
    {
        var block = GetCompressedBlock(entries);
        return new(order, block);
    }

    protected override int WriteBytes(OffsetTableEntry entry, Span<byte> buffer)
    {
        int size = Convert.ToInt32(GetByteCount(entry));
        if (size < 1)
            throw new InvalidDataException("Size must be >= 1");

        var stream = _fileStreams!.GetStream(entry.FilePath);
        stream.Seek(entry.RecordPos, SeekOrigin.Begin);

        // For MDX, read size-1 bytes and append null byte
        stream.ReadExactly(buffer[..(size - 1)]);
        buffer[size - 1] = 0; // null-terminate

        _fileStreams.UpdateEntryCount(entry.FilePath);

        return size;
    }
}
