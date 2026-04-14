using Lib.BuildModels;
using Microsoft.Extensions.Logging;

namespace Lib.Build;

internal sealed class KeyBlocksBuilder(ILogger<KeyBlocksBuilder> logger)
    : BlocksBuilder<MdxKeyBlock>(logger)
{
    protected override MdxKeyBlock BlockConstructor(ReadOnlySpan<OffsetTableEntry> entries, int compressionType)
        => new(entries, compressionType);

    protected override long EntryLength(OffsetTableEntry entry)
        => entry.MdxKeyBlockEntryLength;
}
