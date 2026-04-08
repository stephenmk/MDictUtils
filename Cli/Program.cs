using System;
using System.IO;
using Lib;

namespace Cli;

static class Program
{
    // Dummy CLI emulator
    static void Main(string[] args)
    {
        if (args.Length != 3)
        {
            Console.WriteLine("Error: Invalid arguments");
            Console.WriteLine("Usage: dotnet run -- OPATH -a IPATH");
            Environment.Exit(1);
        }

        string outputPath = args[0];
        string flag = args[1];
        string inputPath = args[2];

        if (flag != "-a")
        {
            Console.WriteLine($"Error: Invalid flag '{flag}'. Expected '-a'");
            Console.WriteLine("Usage: dotnet run -- OPATH -a IPATH");
            Environment.Exit(1);
        }

        if (!File.Exists(inputPath) && !Directory.Exists(inputPath))
        {
            Console.WriteLine($"Error: Input path does not exist: {inputPath}");
            Environment.Exit(1);
        }

        Run(outputPath, inputPath);
    }

    static void Run(string outputPath, string inputPath)
    {
        Console.WriteLine($"Reading @ {inputPath}");
        Console.WriteLine($"Writing @ {outputPath}");

        var readeded = MDictPacker.PackMdxTxt(inputPath);

        // foreach (var entry in readeded)
        // {
        //     Console.WriteLine($"Key: {entry.Key}");
        //     Console.WriteLine($"Path: {entry.Path}");
        //     Console.WriteLine($"Pos: {entry.Pos}");
        //     Console.WriteLine($"Size: {entry.Size}");
        //     Console.WriteLine("----------------------");
        // }

        var writer = new MDictWriter(readeded);
        using var outFile = File.Open(outputPath, FileMode.Create);
        writer.Write(outFile);
    }
}
