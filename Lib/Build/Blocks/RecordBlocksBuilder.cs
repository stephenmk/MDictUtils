using Lib.BuildModels;
using Microsoft.Extensions.Logging;

namespace Lib.Build.Blocks;

internal abstract class RecordBlocksBuilder
(
    ILogger<RecordBlocksBuilder> logger,
    IBlockCompressor blockCompressor
)
    : BlocksBuilder<RecordBlock>(logger, blockCompressor), IRecordBlocksBuilder
{
    public abstract List<RecordBlock> Build(OffsetTable offsetTable, int blockSize, FileStreams fileStreams);
}
