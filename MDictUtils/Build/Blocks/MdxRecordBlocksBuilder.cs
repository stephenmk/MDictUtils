using System.Threading.Channels;
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

    public override async Task BuildAsync(OffsetTable offsetTable, ChannelWriter<(int, RecordBlock)> channel)
    {
        var pathToTotalEntryCount = offsetTable.GetFilePathToTotalEntryCount();
        using var fileStreams = new FileStreams(pathToTotalEntryCount);
        _fileStreams = fileStreams;
        await BuildBlocksAsync(offsetTable, channel);
    }

    protected override void WriteBytes(OffsetTableEntry entry, Span<byte> buffer)
    {
        int size = buffer.Length;

        /// By design, we expect that the size will always be at
        /// least 1 to account for the null-termination byte.
        /// <see cref="MDictPacker.PackMdxTxt"/>
        if (size < 1)
            throw new InvalidDataException("Size must be >= 1");

        var stream = _fileStreams!.GetStream(entry.FilePath);
        stream.Seek(entry.RecordPos, SeekOrigin.Begin);

        // For MDX, read size-1 bytes and append null byte
        stream.ReadExactly(buffer[..(size - 1)]);
        buffer[size - 1] = 0; // null-terminate

        _fileStreams.UpdateEntryCount(entry.FilePath);
    }
}
