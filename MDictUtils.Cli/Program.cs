using System;
using System.Collections.Generic;
using System.CommandLine;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;

namespace MDictUtils.Cli;

static class Program
{
    sealed record Args
    (
        bool Verbose,
        string MdictPath,
        string[] AddPaths,
        string? TitlePath,
        string? ExtractDirPath,
        string? DescriptionPath,
        bool ExtractFlag,
        bool MetaFlag,
        bool IsMdd
    )
    {
        public override string ToString() =>
            $$"""
            Args {
                MdictPath = {{MdictPath}},
                AddPaths = {{AddPaths}},
                TitlePath = {{TitlePath}},
                DescriptionPath = {{DescriptionPath}},
                ExtractDirPath = {{ExtractDirPath}},
                ExtractFlag = {{ExtractFlag}},
                MetaFlag = {{MetaFlag}},
                IsMdd = {{IsMdd}}
            }
            """;
    }

    // https://learn.microsoft.com/en-us/dotnet/standard/commandline/
    static int Main(string[] args)
    {
        Argument<string> mdictPath = new("mdx/mdd file")
        {
            Description = "Dictionary mdx/mdd file"
        };

        Option<bool> verboseFlag = new("--verbose", "-v")
        {
            Description = "Verbose mode: enable logging",
        };

        // TODO: should be a subcommand
        // It's not nullable, defaults to empty list
        Option<string[]> addPaths = new("--add", "-a")
        {
            Description = "Resource file to add",
            Arity = ArgumentArity.OneOrMore,
        };
        // TODO: these should should require --add or they do nothing
        // Is title and description assumed to be html? Seems so looking at the source code unpacking...
        // I guess we assume so for simplicity (we check the extension later on)
        Option<string> titlePath = new("--title")
        {
            Description = "Dictionary title html file",
        };
        Option<string> descriptionPath = new("--description")
        {
            Description = "Dictionary description html file",
        };
        // TODO: should conflict with -a tbh, in python they "get away" because of dispatch order
        // TODO: should be a subcommand
        Option<bool> extractFlag = new("--extract", "-x")
        {
            Description = "Extract mdx/mdd file",
        };
        // TODO: requires -x to work, python design is ...
        Option<string> extractDirPath = new("--dir", "-d")
        {
            Description = "Extract mdx/mdd to directory",
        };

        Option<bool> metaFlag = new("--meta", "-m")
        {
            Description = "Show mdx/mdd meta information",
        };

        RootCommand rootCommand = new("MDictUtils CLI")
        {
            mdictPath,
            verboseFlag,
            addPaths,
            titlePath,
            descriptionPath,
            extractFlag,
            extractDirPath,
            metaFlag,
        };

        rootCommand.SetAction(parseResult =>
        {
            var parsedMdictPath = parseResult.GetRequiredValue(mdictPath);
            string? extension = Path.GetExtension(parsedMdictPath);
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
                    // WARN: We enter here with empty input, f.e. cli -f (since f is not a flag!)
                    Console.Error.WriteLine("Folders are not yet supported");
                    return 1;
                default:
                    Console.Error.WriteLine(
                        $"Unsupported file type: '{extension}'. Only .mdx and .mdd are allowed.");
                    return 1;
            }

            // TODO: if we are mdx, we should only accept txt as in --add

            var parsedVerboseFlag = parseResult.GetValue(verboseFlag);
            var parsedAddPaths = parseResult.GetValue(addPaths) ?? [];
            var parsedTitlePath = parseResult.GetValue(titlePath);
            var parsedDescriptionPath = parseResult.GetValue(descriptionPath);
            var parsedExtractFlag = parseResult.GetValue(extractFlag);
            var parsedMetaFlag = parseResult.GetValue(metaFlag);
            var parsedExtractDirPath = parseResult.GetValue(extractDirPath); // does not need to exist

            foreach (string parsedAddPath in parsedAddPaths)
            {
                if (CheckPath(parsedAddPath) != 0) return 1;
            }
            if (CheckPath(parsedTitlePath) != 0) return 1;
            if (CheckPath(parsedDescriptionPath) != 0) return 1;

            if (!string.IsNullOrEmpty(parsedTitlePath) && Path.GetExtension(parsedTitlePath) != ".html")
            {
                Console.Error.WriteLine($"Path '{parsedTitlePath}' should point to html");
                return 1;
            }
            if (!string.IsNullOrEmpty(parsedDescriptionPath) && Path.GetExtension(parsedDescriptionPath) != ".html")
            {
                Console.Error.WriteLine($"Path '{parsedDescriptionPath}' should point to html");
                return 1;
            }

            Args arguments = new
            (
                Verbose: parsedVerboseFlag,
                MdictPath: parsedMdictPath,
                AddPaths: parsedAddPaths,
                TitlePath: parsedTitlePath,
                ExtractDirPath: parsedExtractDirPath,
                DescriptionPath: parsedDescriptionPath,
                ExtractFlag: parsedExtractFlag,
                MetaFlag: parsedMetaFlag,
                IsMdd: isMdd
            );

            Run(arguments);
            return 0;
        });

        ParseResult parseResult = rootCommand.Parse(args);
        return parseResult.Invoke();
    }

    static int CheckPath(string? path)
    {
        if (path != null && !File.Exists(path) && !Directory.Exists(path))
        {
            Console.Error.WriteLine($"Path does not exist: {path}");
            return 1;
        }
        return 0;
    }

    static void Run(Args args)
    {
        if (args.Verbose)
        {
            Console.Error.WriteLine(args);
        }

        if (args.AddPaths.Length > 0)
        {
            List<MDictEntry> packed = [];
            foreach (string AddPath in args.AddPaths)
            {
                List<MDictEntry> packedAtPath = args.IsMdd
                      ? MDictPacker.PackMddFile(AddPath)
                      : MDictPacker.PackMdxTxt(AddPath);
                packed.AddRange(packedAtPath);
            }

            string title = "";
            if (!string.IsNullOrEmpty(args.TitlePath))
            {
                title = File.ReadAllText(args.TitlePath, Encoding.UTF8).Trim();
            }

            string description = "";
            if (!string.IsNullOrEmpty(args.DescriptionPath))
            {
                description = File.ReadAllText(args.DescriptionPath, Encoding.UTF8).Trim();
            }

            var metadata = new MDictMetadata(
                Title: title,
                Description: description,
                IsMdd: args.IsMdd);

            MDictWriter writer = new(packed, metadata, logging: args.Verbose);

            // creates intermediate directories if needed
            // so that it works if MdictPath is a/b/thing.mdx
            var directory = Path.GetDirectoryName(args.MdictPath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }
            writer.Write(args.MdictPath);
        }
        else if (args.ExtractFlag)
        {
            var target = args.ExtractDirPath ?? Directory.GetCurrentDirectory();
            target = Path.GetFullPath(target);
            MDictPacker.Unpack(target, args.MdictPath, args.IsMdd);
        }
        else if (args.MetaFlag)
        {
            MDict m = args.IsMdd
                ? new MDD(args.MdictPath)
                : new MDX(args.MdictPath);
            Console.Error.WriteLine("Version: \"2.0\""); // le hardcode
            Console.Error.WriteLine($"Record: \"{m.Count}\"");
            foreach (var (key, value) in m.Header)
            {
                // Not sure why this was done in the original, it seems worse to me...
                var keyTitled = CultureInfo.CurrentCulture.TextInfo.ToTitleCase(key);
                Console.Error.WriteLine($"{keyTitled}: \"{value}\"");
            }
        }
        else
        {
            Console.Error.WriteLine("Unreachable ^TM");
            throw new UnreachableException();
        }
    }
}
