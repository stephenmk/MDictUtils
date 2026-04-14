using System.Text;
using BenchmarkDotNet.Attributes;

namespace Lib.Benchmark;

public class Benchmarks
{
    private readonly string _tmpDirectoryPath = Path.Join(Path.GetTempPath(), Guid.NewGuid().ToString());
    private readonly string _txtFilePath = Path.GetTempFileName();
    private readonly string _mdxFilePath = Path.GetTempFileName();

    private readonly List<MDictEntry> Entries = [];
    private readonly MDictMetadata Metadata = new();

    [GlobalSetup]
    public void Setup()
    {
        Directory.CreateDirectory(_tmpDirectoryPath);

        // Initialize TXT file.
        using (FileStream fs = new(_txtFilePath, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            using StreamWriter swriter = new(fs, new UTF8Encoding(false));
            foreach (var number in Enumerable.Range(100_000, 300_000))
            {
                var key = $"{number:X}";
                swriter.WriteLine(key);
                swriter.WriteLine($"This is the definition for the entry with the key {key}.");
                swriter.WriteLine("</>");
            }
        }

        Entries.AddRange(MDictPacker.PackMdxTxt(_txtFilePath));

        // Initialize MDX file.
        using (FileStream fs = new(_mdxFilePath, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            var mdict = new MDictWriter(Entries, Metadata, logging: false);
            mdict.Write(fs);
        }
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
        MDictPacker.PackMdxTxt(_txtFilePath);
    }

    [Benchmark]
    public void BenchmarkMdxWriting()
    {
        var tempFile = Path.Join(_tmpDirectoryPath, Guid.NewGuid().ToString());
        using FileStream fs = new(tempFile, FileMode.Create, FileAccess.Write, FileShare.None);
        var mdict = new MDictWriter(Entries, Metadata, logging: false);
        mdict.Write(fs);
    }

    [Benchmark]
    public void BenchmarkMdxUnpacking()
    {
        var tempDir = Path.Join(_tmpDirectoryPath, Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);
        MDictPacker.UnpackMdx(tempDir, _mdxFilePath);
    }
}
