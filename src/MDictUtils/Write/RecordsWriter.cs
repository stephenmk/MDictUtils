using System.Diagnostics;
using System.Threading.Channels;
using MDictUtils.Build.Index;
using MDictUtils.BuildModels;
using Microsoft.Extensions.Logging;

namespace MDictUtils.Write;

internal sealed partial class RecordsWriter(ILogger<RecordsWriter> logger)
{
    public async Task WriteAsync(OffsetTable offsetTable, ChannelReader<RecordBlock> channel, Stream outfile)
    {
        using var indexBuilder = new RecordBlockIndexBuilder(offsetTable);

        // Skip over the index sections for now.
        var indexStartPosition = outfile.Position;
        outfile.Seek(indexBuilder.IndexSize, SeekOrigin.Current);

        await WriteOutputAsync(channel, outfile, indexBuilder);

        // Return to the start of the index sections.
        outfile.Seek(indexStartPosition, SeekOrigin.Begin);
        await indexBuilder.WriteAsync(outfile);
    }

    /// <summary>
    /// Read all blocks from the channel, calculate the index data, and write the blocks to disk.
    /// </summary>
    private async Task WriteOutputAsync(ChannelReader<RecordBlock> reader, Stream outfile, RecordBlockIndexBuilder indexBuilder)
    {
        var blockCount = indexBuilder.BlockCount;
        var blocks = new RecordBlock?[blockCount];
        int order = 0;

        await foreach (var recordBlock in reader.ReadAllAsync())
        {
            blocks[recordBlock.Id] = recordBlock;
            LogBlockReceived(recordBlock.Id, order, reader.Count);

            // Ensure that blocks are always written in sequential order.
            while (blocks[order] is RecordBlock block) // (not null)
            {
                LogBlockWrite(block.Id);
                await outfile.WriteAsync(block.Bytes);
                indexBuilder.ReadBlock(block);

                block.Dispose();
                blocks[order] = null;
                order++;

                if (order == blockCount)
                    break;
            }
        }

        LogBlocks(indexBuilder.BlockCount, indexBuilder.AverageRecordSize);
    }

    [LoggerMessage(LogLevel.Debug,
    "Built {Count} record blocks of average size {AvgSize:N0}")]
    private partial void LogBlocks(int count, long avgSize);

    [Conditional("DEBUG")]
    [LoggerMessage(LogLevel.Trace,
    "Received block #{Id}; waiting for block #{WaitId}; channel contains {Count} blocks")]
    private partial void LogBlockReceived(int id, int waitId, int count);

    [Conditional("DEBUG")]
    [LoggerMessage(LogLevel.Trace, "Writing block #{Id}")]
    private partial void LogBlockWrite(int id);
}
