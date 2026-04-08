using System;
using System.Collections.Generic;
using System.CommandLine;
using System.Globalization;
using System.IO;
using System.Text;
using Lib;

namespace Cli;

static class Program
{
    class Args
    {
        public string MdictPath { get; set; }
        public string AddPath { get; set; }
        public string TitlePath { get; set; }
        public string DescriptionPath { get; set; }
        public bool ExtractFlag { get; set; }
        public bool MetaFlag { get; set; }
        public bool IsMdd { get; set; }

        public override string ToString()
        {
            return $@"Args {{
    MdictPath = {MdictPath},
    AddPath = {AddPath},
    TitlePath = {TitlePath},
    DescriptionPath = {DescriptionPath},
    ExtractFlag = {ExtractFlag},
    MetaFlag = {MetaFlag},
    IsMdd = {IsMdd}
}}";
        }
    }

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
        Option<bool> metaFlag = new("--meta", "-m")
        {
            Description = "Show mdx/mdd meta information",
        };

        rootCommand.Arguments.Add(mdictPath);
        rootCommand.Options.Add(addPath);
        rootCommand.Options.Add(titlePath);
        rootCommand.Options.Add(descriptionPath);
        rootCommand.Options.Add(extractFlag);
        rootCommand.Options.Add(metaFlag);

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
                    // WARN: We enter here with empty input, f.e. cli -f (since f is not a flag!)
                    Console.WriteLine("Folders are not yet supported");
                    return 1;
                default:
                    Console.WriteLine(
                        $"Unsupported file type: '{extension}'. Only .mdx and .mdd are allowed.");
                    return 1;
            }

            // TODO: if we are mdx, we should only accept txt as in --add

            var parsedAddPath = parseResult.GetValue(addPath);
            var parsedTitlePath = parseResult.GetValue(titlePath);
            var parsedDescriptionPath = parseResult.GetValue(descriptionPath);
            var parsedExtractFlag = parseResult.GetValue(extractFlag);
            var parsedMetaFlag = parseResult.GetValue(metaFlag);

            if (CheckPath(parsedAddPath) != 0) return 1;
            if (CheckPath(parsedTitlePath) != 0) return 1;
            if (CheckPath(parsedDescriptionPath) != 0) return 1;

            if (!string.IsNullOrEmpty(parsedTitlePath) && Path.GetExtension(parsedTitlePath) != ".html")
            {
                Console.WriteLine($"Path '{parsedTitlePath}' should point to html");
                return 1;
            }
            if (!string.IsNullOrEmpty(parsedDescriptionPath) && Path.GetExtension(parsedDescriptionPath) != ".html")
            {
                Console.WriteLine($"Path '{parsedDescriptionPath}' should point to html");
                return 1;
            }

            Args arguments = new()
            {
                MdictPath = parsedMdictPath,
                AddPath = parsedAddPath,
                TitlePath = parsedTitlePath,
                DescriptionPath = parsedDescriptionPath,
                ExtractFlag = parsedExtractFlag,
                MetaFlag = parsedMetaFlag,
                IsMdd = isMdd
            };

            Run(arguments);
            return 0;
        });

        ParseResult parseResult = rootCommand.Parse(args);
        return parseResult.Invoke();
    }

    static int CheckPath(string path)
    {
        if (path != null && !File.Exists(path) && !Directory.Exists(path))
        {
            Console.WriteLine($"Path does not exist: {path}");
            return 1;
        }
        return 0;
    }

    static void Run(Args args)
    {
        Console.Error.WriteLine(args);

        if (args.AddPath != null)
        {
            List<MDictEntry> packed = args.IsMdd
                ? MDictPacker.PackMddFile(args.AddPath)
                : MDictPacker.PackMdxTxt(args.AddPath);

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

            MDictWriter writer = new(packed, title, description, isMdd: args.IsMdd);
            using var outFile = File.Open(args.MdictPath, FileMode.Create);

            writer.Write(outFile);
        }
        else if (args.ExtractFlag)
        {
            // TODO: maybe be able to pass it (the original uses --dir)
            var target = Directory.GetCurrentDirectory();
            target = Path.GetFullPath(target);
            MDictPacker.Unpack(target, args.MdictPath, args.IsMdd);
        }
        else if (args.MetaFlag)
        {
            MDict m = args.IsMdd ? new MDD(args.MdictPath) : new MDX(args.MdictPath);
            Console.WriteLine("Version: \"2.0\""); // le hardcode
            Console.WriteLine($"Record: \"{m.Count}\"");
            foreach ((string key, string value) in m.Header)
            {
                // Not sure why this was done in the original, it seems worse to me...
                var keyTitled = CultureInfo.CurrentCulture.TextInfo.ToTitleCase(key);
                Console.WriteLine($"{keyTitled}: \"{value}\"");
            }
        }
        else
        {
            Console.WriteLine("Unreachable ^TM");
        }
    }
}
