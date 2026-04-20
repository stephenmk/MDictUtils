using MDictUtils.BuildModels;

namespace MDictUtils.Write;

/// <summary>
/// Writes the key index and key blocks to the output MDict file.
/// </summary>
/// <remarks>
/// <para>
/// Unlike the record data, the key data cannot be efficiently built and
/// written to the output file concurrently. This is because the key
/// index, unlike the record index, is compressed. The key index contains
/// the compressed and uncompressed sizes of all of the key blocks. So all
/// of the key blocks must be built and compressed and measured before the
/// key index may be built and compressed and measured.
/// </para>
/// Unlike the record data, the key data is relatively small and should
/// fit easily into our runtime memory. So there is no need to build and
/// write this data concurrently.
/// </remarks>
internal sealed class KeysWriter
{
    public void Write(Stream outfile, KeyData data)
    {
        Span<byte> preamble = stackalloc byte[5 * 8]; // Five 8-byte buffers
        var r = new SpanReader<byte>(preamble) { ReadSize = 8 };

        Common.ToBigEndian((ulong)data.KeyBlocks.Length, r.Read());
        Common.ToBigEndian((ulong)data.EntryCount, r.Read());
        Common.ToBigEndian((ulong)data.KeyBlockIndex.DecompSize, r.Read());
        Common.ToBigEndian((ulong)data.KeyBlockIndex.Size, r.Read());
        Common.ToBigEndian((ulong)data.KeyBlocksSize, r.Read());

        uint checksumValue = Common.Adler32(preamble);
        Span<byte> checksum = stackalloc byte[4];
        Common.ToBigEndian(checksumValue, checksum);

        outfile.Write(preamble);
        outfile.Write(checksum);
        outfile.Write(data.KeyBlockIndex.Bytes.AsSpan());

        foreach (var block in data.KeyBlocks)
        {
            outfile.Write(block.Bytes.AsSpan());
        }
    }
}
