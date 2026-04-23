using System.Threading.Channels;
using MDictUtils.BuildModels;

namespace MDictUtils.Build;

internal interface IDataBuilder
{
    OffsetTable BuildOffsetTable(List<MDictEntry> entries);
    Task<KeyData> BuildKeyDataAsync(OffsetTable offsetTable);
    Task BuildRecordBlocksAsync(OffsetTable offsetTable, ChannelWriter<RecordBlock> writer);
}

internal interface IBlockCompressor
{
    Task<CompressedBlock> CompressAsync(ReadOnlyMemory<byte> data);
}

internal interface IRecordBlocksBuilder
{
    Task BuildAsync(OffsetTable offsetTable, ChannelWriter<RecordBlock> writer);
}

internal interface IKeyComparer
{
    int Compare(ReadOnlySpan<char> k1, ReadOnlySpan<char> k2);
}
