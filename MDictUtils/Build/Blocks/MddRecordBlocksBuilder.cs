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

    protected override long GetByteCount(OffsetTableEntry entry)
        => entry.RecordSize;

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

        // Unless somebody changed the file since we last checked it,
        // we should have read the expected amount of bytes.
        Debug.Assert(totalRead == GetByteCount(entry));

        return totalRead;
    }
}
