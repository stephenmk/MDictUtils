using System;
using System.Collections.Generic;
using System.CommandLine;
using System.IO;
using Lib;

namespace Cli;

static class Program
{
    // https://learn.microsoft.com/en-us/dotnet/standard/commandline/
    static int Main(string[] args)
    {
        RootCommand rootCommand = new("MDictUtils CLI");

        Argument<string> mdictPath = new("mdx/mdd file")
        {
            Description = "Dictionary mdx/mdd file"
        };

        // TODO: This is supposed to have >1 arity
        // TODO: should be a subcommand
        Option<string> addPath = new("--add", "-a")
        {
            Description = "Resource file to add",
        };
        // TODO: should conflict with -a tbh, in python they "get away" because of dispatch order
        // TODO: should be a subcommand
        Option<bool> extractPath = new("--extract", "-x")
        {
            Description = "Extract mdx/mdd file",
        };

        rootCommand.Arguments.Add(mdictPath);
        rootCommand.Options.Add(addPath);
        rootCommand.Options.Add(extractPath);

        rootCommand.SetAction(parseResult =>
        {
            var parsedMdictPath = parseResult.GetValue(mdictPath);
            string extension = Path.GetExtension(parsedMdictPath);
            bool isMdd;
            switch (extension)
            {
                case ".mdx":
                    isMdd = false;
                    break;
                case ".mdd":
                    isMdd = true;
                    break;
                case "":
                    Console.WriteLine("Folders are not yet supported");
                    return 1;
                default:
                    Console.WriteLine(
                        $"Unsupported file type: '{extension}'. Only .mdx and .mdd are allowed.");
                    return 1;
            }

            // TODO: if we are mdx, we should only accept txt as in --add

            var parsedAddPath = parseResult.GetValue(addPath);
            var parsedExtractFlag = parseResult.GetValue(extractPath);

            if (parsedAddPath == null && !parsedExtractFlag)
            {
                Console.WriteLine("Specify at least one operation: --add <mdx/mdd> or --extract.");
                return 1;
            }

            if (parsedAddPath != null && !File.Exists(parsedAddPath) && !Directory.Exists(parsedAddPath))
            {
                Console.WriteLine($"Path does not exist: {parsedAddPath}");
                return 1;
            }

            Run(parsedMdictPath, parsedAddPath, parsedExtractFlag, isMdd);
            return 0;
        });

        ParseResult parseResult = rootCommand.Parse(args);
        return parseResult.Invoke();
    }

    static void Run(string mdictPath, string addPath, bool extractFlag, bool isMdd)
    {
        Console.WriteLine($"mdictPath @ {mdictPath}");
        Console.WriteLine($"addPath @ {addPath}");
        Console.WriteLine($"extractFlag @ {extractFlag}");
        Console.WriteLine($"isMdd @ {isMdd}");

        if (addPath != null)
        {
            List<MDictEntry> packed = isMdd
                ? MDictPacker.PackMddFile(addPath)
                : MDictPacker.PackMdxTxt(addPath);

            // foreach (var entry in packed)
            // {
            //     Console.WriteLine($"Key: {entry.Key}");
            //     Console.WriteLine($"Path: {entry.Path}");
            //     Console.WriteLine($"Pos: {entry.Pos}");
            //     Console.WriteLine($"Size: {entry.Size}");
            //     Console.WriteLine("----------------------");
            // }

            var writer = new MDictWriter(packed, isMdd: isMdd);
            using var outFile = File.Open(mdictPath, FileMode.Create);

            writer.Write(outFile);
        }
        else if (extractFlag)
        {
            // TODO: maybe be able to pass it (the original uses --dir)
            var target = Directory.GetCurrentDirectory();
            target = Path.GetFullPath(target);
            MDictPacker.Unpack(target, mdictPath, isMdd);
        }
        else
        {
            Console.WriteLine("Unreachable ^TM");
        }
    }
}
