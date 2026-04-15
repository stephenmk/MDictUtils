using System.Diagnostics;
using Lib.BuildModels;
using Microsoft.Extensions.Logging;

namespace Lib.Build.Blocks;

internal sealed class MddRecordBlocksBuilder
(
    ILogger<MddRecordBlocksBuilder> logger,
    IBlockCompressor blockCompressor
)
    : RecordBlocksBuilder(logger, blockCompressor)
{
    public override List<RecordBlock> Build(OffsetTable offsetTable, int blockSize, FileStreams _)
        => BuildBlocks(offsetTable, blockSize);

    protected override long GetByteCount(OffsetTableEntry entry)
        => entry.RecordBlockLength;

    protected override RecordBlock BlockConstructor(ReadOnlySpan<OffsetTableEntry> entries)
    {
        var block = GetCompressedBlock(entries);
        return new(block);
    }

    protected override int WriteBytes(OffsetTableEntry entry, Span<byte> buffer)
    {
        Debug.Assert(entry.RecordPos == 0);

        if (entry.RecordSize < 1)
            throw new InvalidDataException("Size must be >= 1");

        // For MDD, each file is opened only once and read entirely.
        using var fs = new FileStream(entry.FilePath, FileMode.Open, FileAccess.Read);
        fs.Seek(entry.RecordPos, SeekOrigin.Begin);

        int totalRead = 0;
        while (true)
        {
            int bytesRead = fs.Read(buffer[totalRead..]);
            totalRead += bytesRead;
            if (bytesRead == 0)
                break;
        }

        // For MDD, apparently fewer bytes than the expected size might be read?
        return totalRead;
    }
}
