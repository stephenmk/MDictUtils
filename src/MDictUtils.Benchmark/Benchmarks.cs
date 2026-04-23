using System.Text;
using BenchmarkDotNet.Attributes;
using Microsoft.Extensions.DependencyInjection;

namespace MDictUtils.Benchmark;

public class Benchmarks
{
    private readonly string _tmpDirectoryPath = Path.Join(Path.GetTempPath(), Guid.NewGuid().ToString());
    private readonly string _txtFilePath = Path.GetTempFileName();
    private readonly string _mdxFilePath = Path.GetTempFileName();

    private readonly List<MDictEntry> Entries = [];
    private readonly MdxHeader Header = new();
    private readonly IMdxWriter Writer = new ServiceCollection()
        .AddMdxWriter()
        .BuildServiceProvider()
        .GetRequiredService<IMdxWriter>();

    [GlobalSetup]
    public async Task Setup()
    {
        Directory.CreateDirectory(_tmpDirectoryPath);

        // Initialize TXT file.
        await using (FileStream fs = new(_txtFilePath, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            await using StreamWriter swriter = new(fs, new UTF8Encoding(false));
            foreach (var number in Enumerable.Range(100_000, 300_000))
            {
                var key = $"{number:X}";
                swriter.WriteLine(key);
                swriter.WriteLine($"This is the definition for the entry with the key {key}.");
                swriter.WriteLine("</>");
            }
        }

        Entries.AddRange(MDictPacker.PackMdx(_txtFilePath));

        // Initialize MDX file.
        await Writer.WriteAsync(Header, Entries, _mdxFilePath);
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        if (Directory.Exists(_tmpDirectoryPath))
            Directory.Delete(_tmpDirectoryPath, recursive: true);

        if (File.Exists(_txtFilePath))
            File.Delete(_txtFilePath);

        if (File.Exists(_mdxFilePath))
            File.Delete(_mdxFilePath);
    }

    [Benchmark]
    public void BenchmarkTxtParsing()
    {
        MDictPacker.PackMdx(_txtFilePath);
    }

    [Benchmark]
    public async Task BenchmarkMdxWritingAsync()
    {
        var tempFile = Path.Join(_tmpDirectoryPath, Guid.NewGuid().ToString());

        await Writer.WriteAsync(Header, Entries, tempFile);
    }

    [Benchmark]
    public void BenchmarkMdxUnpacking()
    {
        var tempDir = Path.Join(_tmpDirectoryPath, Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);
        MDictPacker.UnpackMdx(tempDir, _mdxFilePath);
    }
}
