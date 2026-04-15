using System.Collections.ObjectModel;
using Lib.Build.Blocks;
using Lib.Build.Index;
using Lib.Build.Offset;
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

        ReadOnlyCollection<RecordBlock> recordBlocks;
        using (var fileStreams = new FileStreams())
        {
            recordBlocks = recordBlocksBuilder
                .Build(offsetTable, m.BlockSize, fileStreams)
                .AsReadOnly();
        }

        var recordBlockIndex = recordBlockIndexBuilder
            .Build(recordBlocks);

        if (logger.IsEnabled(LogLevel.Debug))
            logger.LogDebug("Initialization complete.");

        return new MDictData
        (
            Title: m.Title,
            Description: m.Description,
            Version: m.Version,
            IsMdd: m.IsMdd,
            EntryCount: entries.Count,
            KeyBlocks: keyBlocks,
            RecordBlocks: recordBlocks,
            KeyBlockIndex: keyBlockIndex,
            RecordBlockIndex: recordBlockIndex
        );
    }
}
