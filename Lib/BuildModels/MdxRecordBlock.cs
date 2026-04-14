using System.Diagnostics;

namespace Lib.BuildModels;

internal class MdxRecordBlock(ReadOnlySpan<OffsetTableEntry> offsetTable, int compressionType)
    : MdxBlock(offsetTable, compressionType)
{
    public override int IndexEntryLength => 16;

    public override long BlockEntryLength(OffsetTableEntry entry)
        => entry.MdxRecordBlockEntryLength;

    public override void GetIndexEntry(Span<byte> buffer)
    {
        // Console.WriteLine("Called GetIndexEntry on MDXRECORDBLOCK");
        // Console.WriteLine($"    compSize {_compSize}; decompsize {_decompSize}");
        // if (_version != "2.0")
        //     throw new NotImplementedException();

        Debug.Assert(buffer.Length == IndexEntryLength);

        // Big-endian 64-bit values
        Common.ToBigEndian((ulong)_blockData.CompressedSize, buffer[..8]);
        Common.ToBigEndian((ulong)_blockData.DecompSize, buffer[8..16]);
    }

    // rg: get_record_null
    // We overwrite "return entry.RecordNull"
    protected override int GetBlockEntry(OffsetTableEntry entry, Span<byte> buffer)
    {
        int size = ReadRecord(entry.FilePath, entry.RecordPos, (int)entry.RecordSize, entry.IsMdd, buffer);
        // entry.RecordNull = buffer[..size].ToArray();
        return size;
    }

    /// <summary>
    /// Helper method: read from file and null-terminate
    /// </summary>
    private static int ReadRecord(string filePath, long pos, int size, bool isMdd, Span<byte> buffer)
    {
        if (size < 1) throw new ArgumentException("Size must be >= 1", nameof(size));

        // We're repeatedly opening these files and seeking to positions within them.
        // That's probably very time consuming.
        using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read);

        fs.Seek(pos, SeekOrigin.Begin);
        if (isMdd)
        {
            int totalRead = 0;
            while (true)
            {
                int bytesRead = fs.Read(buffer[totalRead..]);
                totalRead += bytesRead;
                if (bytesRead == 0)
                    break;
            }
            // For MDD, apparently fewer bytes than the expected size might be read?
            return totalRead;
        }
        else
        {
            // For MDX, read size-1 bytes and append null byte
            fs.ReadExactly(buffer[..(size - 1)]);
            buffer[size - 1] = 0; // null-terminate
            return size;
        }
    }
}
