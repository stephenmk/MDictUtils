using MDictUtils.Build.Blocks;
using MDictUtils.Build.Index;
using MDictUtils.Build.Offset;
using MDictUtils.BuildModels;
using Microsoft.Extensions.Logging;

namespace MDictUtils.Build;

internal sealed class MDictDataBuilder
(
    ILogger<MDictDataBuilder> logger,
    OffsetTableBuilder offsetTableBuilder,
    KeyBlockIndexBuilder keyBlockIndexBuilder,
    KeyBlocksBuilder keyBlocksBuilder,
    RecordBlockIndexBuilder recordBlockIndexBuilder,
    IRecordBlocksBuilder recordBlocksBuilder
)
    : IMDictDataBuilder
{
    public MDictData BuildData(List<MDictEntry> entries, MDictMetadata m)
    {
        var offsetTable = offsetTableBuilder
            .Build(entries, m);

        var keyBlocks = keyBlocksBuilder
            .Build(offsetTable, m.KeySize)
            .AsReadOnly();

        var keyBlockIndex = keyBlockIndexBuilder
            .Build(keyBlocks);

        var recordBlocks = recordBlocksBuilder
            .Build(offsetTable, m.BlockSize)
            .AsReadOnly();

        var recordBlockIndex = recordBlockIndexBuilder
            .Build(recordBlocks);

        if (logger.IsEnabled(LogLevel.Debug))
            logger.LogDebug("Initialization complete.");

        return new MDictData
        (
            m.Title,
            m.Description,
            m.Version,
            m.IsMdd,
            entries.Count,
            keyBlocks,
            recordBlocks,
            keyBlockIndex,
            recordBlockIndex
        );
    }
}
