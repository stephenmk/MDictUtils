using System.Diagnostics;
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
    public override ImmutableArray<RecordBlock> Build(OffsetTable offsetTable, int desiredBlockSize)
        => BuildBlocks(offsetTable, desiredBlockSize);

    protected override int WriteBytes(OffsetTableEntry entry, Span<byte> buffer)
    {
        Debug.Assert(entry.RecordPos == 0);

        /// TODO: This size check is not enforced when packing the files.
        /// <see cref="MDictPacker.PackMddFile"/>
        if (entry.RecordSize < 1)
            throw new InvalidDataException("Size must be >= 1");

        // For MDD, each file is opened only once and read entirely.
        using var fs = new FileStream(entry.FilePath, FileMode.Open, FileAccess.Read);
        fs.Seek(entry.RecordPos, SeekOrigin.Begin);

        int totalRead = 0;
        while (fs.Read(buffer[totalRead..]) is int bytesRead and not 0)
        {
            totalRead += bytesRead;
        }

        // Unless somebody changed the file since we last checked it,
        // we should have read the expected amount of bytes.
        Debug.Assert(totalRead == GetByteCount(entry));

        return totalRead;
    }
}
