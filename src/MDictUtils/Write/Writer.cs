using System.Threading.Channels;
using MDictUtils.Build;
using MDictUtils.BuildModels;

namespace MDictUtils.Write;

internal sealed class Writer
(
    IDataBuilder dataBuilder,
    HeaderWriter headerWriter,
    KeysWriter keysWriter,
    RecordsWriter recordsWriter
)
    : IMdxWriter, IMddWriter
{
    public async Task WriteAsync(MdxHeader header, List<MDictEntry> entries, string outputFile)
        => await WriteAsync((MDictHeader)header, entries, outputFile);

    public async Task WriteAsync(MddHeader header, List<MDictEntry> entries, string outputFile)
        => await WriteAsync((MDictHeader)header, entries, outputFile);

    private async Task WriteAsync(MDictHeader header, List<MDictEntry> entries, string outputFile)
    {
        if (header.Version != "2.0")
            throw new NotSupportedException("Unknown version. Supported: 2.0");

        await using var stream = new FileStream(outputFile, FileMode.Create, FileAccess.Write, FileShare.None);
        await headerWriter.WriteAsync(stream, header);

        var offsetTable = dataBuilder.BuildOffsetTable(entries);

        // Process key data.
        var keyData = await dataBuilder.BuildKeyDataAsync(offsetTable);
        await keysWriter.WriteAsync(stream, keyData);

        // Concurrently read, compress, and write record data to the disk.
        // This is where the heavy lifting happens.
        var channel = GetRecordBlockChannel();
        var buildTask = dataBuilder.BuildRecordBlocksAsync(offsetTable, channel);
        var writeTask = recordsWriter.WriteAsync(offsetTable, channel, stream);
        await Task.WhenAll(buildTask, writeTask);
    }

    private static Channel<RecordBlock> GetRecordBlockChannel()
    {
        // Producing the record blocks is the bottleneck, so we
        // expect the channel to be near empty most of the time.
        // The purpose of the bounded capacity is to prevent
        // excessive memory usage in exceptional circumstances.
        var option = new BoundedChannelOptions(1024)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleWriter = false,
            SingleReader = true,
        };
        return Channel.CreateBounded<RecordBlock>(option);
    }
}
