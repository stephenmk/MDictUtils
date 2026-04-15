using System.Buffers;
using System.Collections.ObjectModel;
using System.Diagnostics;
using Lib.BuildModels;
using Microsoft.Extensions.Logging;

namespace Lib.Build.Index;

internal partial class RecordBlockIndexBuilder(ILogger<RecordBlockIndexBuilder> logger)
{
    private readonly static ArrayPool<byte> _arrayPool = ArrayPool<byte>.Shared;

    public Block Build(ReadOnlyCollection<RecordBlock> recordBlocks)
    {
        if (recordBlocks is [])
            return new([]);

        int indexSize = recordBlocks.Sum(static b => b.IndexEntryLength);
        var indexBuilder = ImmutableArray.CreateBuilder<byte>(indexSize);

        int maxBlockSize = recordBlocks.Max(static b => b.IndexEntryLength);
        byte[]? blockArray = null;
        var blockBuffer = maxBlockSize < 256
            ? stackalloc byte[maxBlockSize]
            : (blockArray = _arrayPool.Rent(maxBlockSize));

        int bytesWritten = 0;
        foreach (var block in recordBlocks)
        {
            var indexEntry = blockBuffer[..block.IndexEntryLength];
            block.CopyIndexEntryTo(indexEntry);

            indexBuilder.AddRange(indexEntry);
            bytesWritten += indexEntry.Length;
        }

        if (blockArray is not null)
            _arrayPool.Return(blockArray);

        Debug.Assert(bytesWritten == indexSize);
        LogIndexBuilt(bytesWritten);

        return new(indexBuilder.MoveToImmutable());
    }

    [LoggerMessage(LogLevel.Debug, "Record index built: size={Size}")]
    private partial void LogIndexBuilt(int size);
}
