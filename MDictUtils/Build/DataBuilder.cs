using System.Threading.Channels;
using MDictUtils.Build.Blocks;
using MDictUtils.Build.Index;
using MDictUtils.Build.Offset;
using MDictUtils.BuildModels;

namespace MDictUtils.Build;

internal sealed class DataBuilder
(
    OffsetTableBuilder offsetTableBuilder,
    KeyBlockIndexBuilder keyBlockIndexBuilder,
    KeyBlocksBuilder keyBlocksBuilder,
    IRecordBlocksBuilder recordBlocksBuilder
)
    : IDataBuilder
{
    public OffsetTable BuildOffsetTable(List<MDictEntry> entries)
        => offsetTableBuilder.Build(entries);

    public async Task<KeyData> BuildKeyDataAsync(OffsetTable offsetTable)
    {
        var keyBlocks = await keyBlocksBuilder.BuildAsync(offsetTable);
        var keyBlockIndex = keyBlockIndexBuilder.Build(keyBlocks);
        return new KeyData(offsetTable.Length, keyBlockIndex, keyBlocks);
    }

    public Task BuildRecordBlocksAsync(OffsetTable offsetTable, ChannelWriter<(int, RecordBlock)> writer)
        => recordBlocksBuilder.BuildAsync(offsetTable, writer);
}
