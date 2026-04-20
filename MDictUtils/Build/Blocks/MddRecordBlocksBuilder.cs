using System.Diagnostics;
using System.Threading.Channels;
using MDictUtils.BuildModels;
using Microsoft.Extensions.Logging;

namespace MDictUtils.Build.Blocks;

internal sealed class MddRecordBlocksBuilder
(
    ILogger<MddRecordBlocksBuilder> logger,
    IBlockCompressor blockCompressor
)
    : RecordBlocksBuilder(logger, blockCompressor)
{
    public override async Task BuildAsync(OffsetTable offsetTable, ChannelWriter<(int, RecordBlock)> channel)
        => await BuildBlocksAsync(offsetTable, channel);

    protected override void WriteBytes(OffsetTableEntry entry, Span<byte> buffer)
    {
        Debug.Assert(entry.RecordPos == 0);

        /// TODO: This size check is not enforced when packing the files.
        /// <see cref="MDictPacker.PackMddFile"/>
        if (entry.RecordSize < 1)
            throw new InvalidDataException("Size must be >= 1");

        // For MDD, each file is opened only once and read entirely.
        using var fs = new FileStream(entry.FilePath, FileMode.Open, FileAccess.Read);
        fs.Seek(entry.RecordPos, SeekOrigin.Begin);

        // Unless somebody changed the file since we last checked it,
        // we should read exactly the expected amount of bytes.
        fs.ReadExactly(buffer);
    }
}
