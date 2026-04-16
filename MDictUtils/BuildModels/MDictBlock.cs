namespace MDictUtils.BuildModels;

/// <summary>
/// Abstract base class for <see cref="RecordBlock"/> and <see cref="KeyBlock"/>.
/// </summary>
internal abstract class MDictBlock(int order, CompressedBlock block)
{
    protected readonly CompressedBlock _block = block;
    public int SortOrder { get; } = order;
    public ImmutableArray<byte> Bytes => _block.Bytes;
    public abstract int IndexEntryLength { get; }
    public abstract void CopyIndexEntryTo(Span<byte> buffer);
}
