using MDictUtils.Build;

namespace MDictUtils.Write;

internal sealed class Writer
(
    IDataBuilder dataBuilder,
    HeaderWriter headerWriter,
    KeysWriter keysWriter,
    RecordsWriter recordsWriter
)
    : IMDictWriter
{
    public void Write(MDictHeader header, List<MDictEntry> entries, string outputFile)
    {
        if (header.Version != "2.0")
            throw new NotSupportedException("Unknown version. Supported: 2.0");

        using var stream = new FileStream(outputFile, FileMode.Create, FileAccess.Write, FileShare.None);

        int bytesWritten = headerWriter.Write(stream, header);

        var offsetTable = dataBuilder.BuildOffsetTable(entries);

        var keyData = dataBuilder.BuildKeyData(offsetTable);
        bytesWritten += keysWriter.Write(stream, keyData);

        var recordData = dataBuilder.BuildRecordData(offsetTable);
        recordsWriter.Write(stream, recordData);
    }
}
