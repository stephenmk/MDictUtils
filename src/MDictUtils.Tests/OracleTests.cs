using System;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Threading.Tasks;
using MDictUtils.Creation;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MDictUtils.Tests;

/// <summary>
/// Tests that the output of this library is equivalent to the output of the "mdict-utils" Python library given the same test input.
/// </summary>
/// <remarks>
/// The comparison uses ZLib compression level = 0 (no compression) because the compressed output of ZLib is finicky.
/// </remarks>
public class OracleTests
{
    private static readonly string Title = File.ReadAllText("title.html", Encoding.UTF8).Trim();
    private static readonly string Description = File.ReadAllText("description.html", Encoding.UTF8).Trim();
    private static readonly DateOnly CreationDate = new(2026, 4, 24);

    private static readonly ReadOnlyMemory<byte> MdxOracleBytes = File.ReadAllBytes("out_no_compression.mdx");
    private static readonly ReadOnlyMemory<byte> MddOracleBytes = File.ReadAllBytes("out_no_compression.mdd");

    private static void Configure(MDictWriterOptions options)
        => options.CompressionLevel = CompressionLevel.NoCompression;

    private static void AssertContentEqual(ReadOnlyMemory<byte> oracleBytes, string newFile)
    {
        var newBytes = File.ReadAllBytes(newFile);
        Assert.Equal(oracleBytes, newBytes);
    }

    [Fact]
    public async Task CompareCreatedMdx()
    {
        var header = new MdxHeader()
        {
            Title = Title,
            Description = Description,
            CreationDate = CreationDate,
        };

        string tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);
        string outMdxPath = Path.Combine(tempDir, "out.mdx");

        using var creator = new MdxCreator();
        await creator.AddEntryAsync("apple", "A fruit that grows on trees.\n");
        await creator.AddEntryAsync("banana", "A long yellow fruit.\n");
        await creator.AddEntryAsync("@cc-100", "xxx\n");
        await creator.AddEntryAsync("potato", "a potato\n");

        await creator.WriteAsync(header, outMdxPath, Configure);

        Assert.True(File.Exists(outMdxPath), "Created file should exist");
        AssertContentEqual(MdxOracleBytes, outMdxPath);

        Directory.Delete(tempDir, recursive: true);
    }

    [Fact]
    public async Task ComparePackedMdx()
    {
        var header = new MdxHeader()
        {
            Title = Title,
            Description = Description,
            CreationDate = CreationDate,
        };

        string tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);
        string outMdxPath = Path.Combine(tempDir, "out.mdx");

        var entries = MDictPacker.PackMdx("stub.txt");
        entries.AddRange(MDictPacker.PackMdx("extra.txt"));

        var writer = new ServiceCollection()
            .AddMdxWriter(Configure)
            .AddTestLogging()
            .BuildServiceProvider()
            .GetRequiredService<IMdxWriter>();

        await writer.WriteAsync(header, entries, outMdxPath);

        Assert.True(File.Exists(outMdxPath), "Created file should exist");
        AssertContentEqual(MdxOracleBytes, outMdxPath);

        Directory.Delete(tempDir, recursive: true);
    }

    [Fact]
    public async Task CompareCreatedMdd()
    {
        var header = new MddHeader() { CreationDate = CreationDate };

        string tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);
        string outMddPath = Path.Combine(tempDir, "out.mdd");

        using var creator = new MddCreator();
        var bytes = await File.ReadAllBytesAsync("stub.txt");
        await creator.AddEntryAsync(@"\stub.txt", bytes);

        await creator.WriteAsync(header, outMddPath, Configure);

        Assert.True(File.Exists(outMddPath), "Created file should exist");
        AssertContentEqual(MddOracleBytes, outMddPath);

        Directory.Delete(tempDir, recursive: true);
    }

    [Fact]
    public async Task ComparePackedMdd()
    {
        var header = new MddHeader() { CreationDate = CreationDate };

        string tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);
        string outMddPath = Path.Combine(tempDir, "out.mdd");

        var entries = MDictPacker.PackMdd("stub.txt");

        var writer = new ServiceCollection()
            .AddMddWriter(Configure)
            .AddTestLogging()
            .BuildServiceProvider()
            .GetRequiredService<IMddWriter>();

        await writer.WriteAsync(header, entries, outMddPath);

        Assert.True(File.Exists(outMddPath), "Created file should exist");
        AssertContentEqual(MddOracleBytes, outMddPath);

        Directory.Delete(tempDir, recursive: true);
    }
}
