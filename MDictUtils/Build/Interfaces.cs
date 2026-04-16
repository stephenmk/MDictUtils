using MDictUtils.BuildModels;

namespace MDictUtils.Build;

internal interface IMDictDataBuilder
{
    public MDictData BuildData(List<MDictEntry> entries, MDictMetadata metadata);
}

internal interface IBlockCompressor
{
    ImmutableArray<byte> Compress(ReadOnlySpan<byte> data);
}

internal interface IRecordBlocksBuilder
{
    List<RecordBlock> Build(OffsetTable offsetTable, int blockSize);
}

internal interface IKeyComparer
{
    int Compare(ReadOnlySpan<char> k1, ReadOnlySpan<char> k2);
}
