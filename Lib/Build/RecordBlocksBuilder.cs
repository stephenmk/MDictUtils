using Lib.BuildModels;
using Microsoft.Extensions.Logging;

namespace Lib.Build;

internal sealed class RecordBlocksBuilder(ILogger<RecordBlocksBuilder> logger)
    : BlocksBuilder<MdxRecordBlock>(logger)
{
    protected override MdxRecordBlock BlockConstructor(ReadOnlySpan<OffsetTableEntry> entries)
        => new(entries);

    protected override long EntryLength(OffsetTableEntry entry)
        => entry.MdxRecordBlockEntryLength;
}
