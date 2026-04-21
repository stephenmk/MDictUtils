using System.Threading.Channels;
using MDictUtils.BuildModels;
using Microsoft.Extensions.Logging;

namespace MDictUtils.Write;

internal sealed partial class RecordsWriter(ILogger<RecordsWriter> logger)
{
    private const int IndexPreambleSize = 4 * 8; // Four 8-byte buffers

    public async Task WriteAsync(OffsetTable offsetTable, ChannelReader<RecordBlock> channel, Stream outfile)
    {
        var blockCount = offsetTable.RecordBlockRanges.Length;
        var entryCount = offsetTable.Length;

        var indexStartPosition = outfile.Position;
        var indexSize = blockCount * 16; // 8 bytes for compressed size, 8 bytes for decomp size per block.
        var index = new byte[indexSize];

        // Skip over the index sections for now.
        outfile.Seek(IndexPreambleSize + indexSize, SeekOrigin.Current);

        var totalSize = await WriteOutputAsync(outfile, channel, index);

        LogBlocks(blockCount, avgSize: blockCount == 0 ? 0 : totalSize / blockCount);

        // Return to the start of the index sections.
        outfile.Seek(indexStartPosition, SeekOrigin.Begin);
        var preamble = GetIndexPreamble(blockCount, entryCount, indexSize, totalSize);
        outfile.Write(preamble);
        outfile.Write(index.AsSpan());
    }

    /// <summary>
    /// Read all blocks from the channel, calculate the index data, and write the blocks to disk.
    /// </summary>
    async Task<long> WriteOutputAsync(Stream outfile, ChannelReader<RecordBlock> channel, byte[] index)
    {
        long totalSize = 0;
        var blockCount = index.Length / 16;
        var blocks = new RecordBlock?[blockCount];
        int order = 0;

        await foreach (var recordBlock in channel.ReadAllAsync())
        {
            blocks[recordBlock.Id] = recordBlock;

            // Ensure that blocks are always written in sequential order.
            while (blocks[order] is RecordBlock block) // (not null)
            {
                var writeTask = outfile.WriteAsync(block.Bytes);
                totalSize += block.Bytes.Length;

                int start = order * 16;
                block.CopyIndexEntryTo(index.AsSpan(start, 16));

                blocks[order] = null;
                order++;

                await writeTask;
                block.Dispose();

                if (order == blockCount)
                    break;
            }
        }

        return totalSize;
    }

    private ReadOnlySpan<byte> GetIndexPreamble(int blockCount, int entryCount, int indexSize, long totalSize)
    {
        Span<byte> preamble = new byte[IndexPreambleSize];
        var r = new SpanReader<byte>(preamble) { ReadSize = 8 };

        Common.ToBigEndian((ulong)blockCount, r.Read());
        Common.ToBigEndian((ulong)entryCount, r.Read());
        Common.ToBigEndian((ulong)indexSize, r.Read()); // Redundant? Always equal to blockCount * 16.
        Common.ToBigEndian((ulong)totalSize, r.Read());

        return preamble;
    }

    [LoggerMessage(LogLevel.Debug,
    "Built {Count} record blocks of average size {AvgSize:N0}")]
    private partial void LogBlocks(int count, long avgSize);
}
