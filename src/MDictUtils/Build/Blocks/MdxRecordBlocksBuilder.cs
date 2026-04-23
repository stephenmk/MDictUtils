using System.Threading.Channels;
using MDictUtils.BuildModels;
using Microsoft.Extensions.Logging;

namespace MDictUtils.Build.Blocks;

internal sealed class MdxRecordBlocksBuilder
(
    ILogger<MdxRecordBlocksBuilder> logger,
    IBlockCompressor blockCompressor,
    BuildOptions options
)
    : RecordBlocksBuilder(logger, blockCompressor)
{
    private FileStreams? _fileStreams;

    public override async Task BuildAsync(OffsetTable offsetTable, ChannelWriter<RecordBlock> channel)
    {
        var pathToTotalEntryCount = offsetTable.GetFilePathToTotalEntryCount();
        using var fileStreams = new FileStreams(pathToTotalEntryCount);
        _fileStreams = fileStreams;
        await BuildBlocksAsync(offsetTable, channel);
    }

    protected override async Task WriteBytesAsync(OffsetTableEntry entry, Memory<byte> buffer)
    {
        int size = buffer.Length;
        int charByteCount = options.KeyEncodingLength;

        /// By design, we expect a minimum size to account for the null-termination character.
        /// <see cref="MDictPacker.PackMdx"/>
        if (size < charByteCount)
            throw new InvalidDataException($"Size must be >= {charByteCount}");

        var stream = _fileStreams!.GetStream(entry.FilePath);
        stream.Seek(entry.RecordPos, SeekOrigin.Begin);

        // For MDX, read the record bytes and append null character
        var recordLength = size - charByteCount;
        await stream.ReadExactlyAsync(buffer[..recordLength]);

        for (int i = recordLength; i < size; i++)
        {
            buffer.Span[i] = 0; // null-terminate
        }

        _fileStreams.UpdateEntryCount(entry.FilePath);
    }
}
