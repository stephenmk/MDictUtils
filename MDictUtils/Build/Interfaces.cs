using System.Threading.Channels;
using MDictUtils.BuildModels;

namespace MDictUtils.Build;

internal interface IDataBuilder
{
    OffsetTable BuildOffsetTable(List<MDictEntry> entries);
    Task<KeyData> BuildKeyDataAsync(OffsetTable offsetTable);
    Task BuildRecordBlocksAsync(OffsetTable offsetTable, ChannelWriter<(int, RecordBlock)> writer);
}

internal interface IBlockCompressor
{
    ImmutableArray<byte> Compress(ReadOnlySpan<byte> data);
}

internal interface IRecordBlocksBuilder
{
    Task BuildAsync(OffsetTable offsetTable, ChannelWriter<(int, RecordBlock)> writer);
}

internal interface IKeyComparer
{
    int Compare(ReadOnlySpan<char> k1, ReadOnlySpan<char> k2);
}
