namespace MDictUtils.BuildModels;

/// <summary>
/// Abstract base class for <see cref="RecordBlock"/> and <see cref="KeyBlock"/>.
/// </summary>
internal abstract class MDictBlock(int id, CompressedBlock block)
{
    public int Id { get; } = id;
    protected readonly CompressedBlock _block = block;
    public ReadOnlyMemory<byte> Bytes => _block.Bytes;
    public abstract int IndexEntryLength { get; }
    public abstract void CopyIndexEntryTo(Span<byte> buffer);
    public void Dispose() => _block.Dispose();
}
