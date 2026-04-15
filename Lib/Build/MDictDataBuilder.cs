using Lib.BuildModels;
using Microsoft.Extensions.Logging;

namespace Lib.Build;

internal sealed class MDictDataBuilder
(
    ILogger<MDictDataBuilder> logger,
    OffsetTableBuilder offsetTableBuilder,
    KeyBlockIndexBuilder keyBlockIndexBuilder,
    KeyBlocksBuilder keyBlocksBuilder,
    RecordBlockIndexBuilder recordBlockIndexBuilder,
    RecordBlocksBuilder recordBlocksBuilder
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

        logger.LogDebug("Initialization complete.");

        return new
        (
            Metadata: m,
            EntryCount: entries.Count,
            OffsetTable: offsetTable,
            KeyBlocks: keyBlocks,
            RecordBlocks: recordBlocks,
            KeyBlockIndex: keyBlockIndex,
            RecordBlockIndex: recordBlockIndex
        );
    }
}
