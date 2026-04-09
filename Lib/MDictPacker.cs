using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

using D = System.Collections.Generic.List<Lib.MDictEntry>;

namespace Lib;

// Packer, does both writing (packing) and reading (unpacking)
public static class MDictPacker
{
    // python does not include the BOM in the title/description
    // so we do the same to allow for oracle testing (but it should not matter really)
    private static readonly UTF8Encoding UTF8NoBOM = new UTF8Encoding(false);

    public static void Unpack(string target, string source, bool isMdd)
    {
        // Should probably Create with parents in case target = d1/d2/folder
        if (!Directory.Exists(target))
        {
            Directory.CreateDirectory(target);
        }

        if (isMdd)
        {
            UnpackMdd(target, source);
        }
        else
        {
            UnpackMdx(target, source);
        }
    }


    public static void UnpackMdx(string target, string source)
    {
        MDX mdx = new(source);
        string basename = Path.GetFileName(source);

        Dictionary<string, string> header = mdx.Header;

        if (header.TryGetValue("Description", out string description) && description.Length > 0)
        {
            string descPath = Path.Combine(target, basename + ".description.html");
            // Console.WriteLine($"[UnpackMdx] Writing description to {descPath}...");
            using FileStream fs = new(descPath, FileMode.Create, FileAccess.Write);
            using StreamWriter swriter = new(fs, UTF8NoBOM);

            foreach (var line in description.Split(["\r\n", "\n"], StringSplitOptions.None))
            {
                swriter.WriteLine(line);
            }
        }

        if (header.TryGetValue("Title", out string title) && title.Length > 0)
        {
            string titlePath = Path.Combine(target, basename + ".title.html");
            // Console.WriteLine($"[UnpackMdx] Writing title to {titlePath}...");
            File.WriteAllText(titlePath, title, UTF8NoBOM);
        }

        // We only support split - None
        // Since split is None, we just write everything to a single file
        string outPath = Path.Combine(target, basename + ".txt");

        using FileStream tf = new(outPath, FileMode.Create, FileAccess.Write);
        using BinaryWriter writer = new(tf);

        int itemCount = 0;

        foreach ((string key, byte[] value) in mdx.Items())
        {
            // if not value.strip(): continue
            if (value == null || value.Length == 0 || IsAllWhitespace(value)) continue;

            itemCount++;

            byte[] keyBytes = Encoding.UTF8.GetBytes(key);
            writer.Write(keyBytes);
            writer.Write([.. "\r\n"u8]);

            writer.Write(value);
            if (value.Length == 0 || value[^1] != (byte)'\n')
            {
                writer.Write([.. "\r\n"u8]);
            }

            writer.Write(Encoding.UTF8.GetBytes("</>\r\n"));
        }
    }

    static bool IsAllWhitespace(byte[] data)
    {
        foreach (byte b in data)
        {
            if (!char.IsWhiteSpace((char)b))
                return false;
        }
        return true;
    }

    public static void UnpackMdd(string target, string source)
    {
        MDD mdd = new(source);

        foreach ((string fname, byte[] v) in mdd.Items())
        {
            var fnameClean = fname.TrimStart('\\'); // bug? happens in the original too
            string fullPath = Path.Combine(target, fnameClean);
            // Console.WriteLine($"UnpackMdd {fullPath} {fname} > clean > {fnameClean}");
            string dir = Path.GetDirectoryName(fullPath);
            if (!Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }
            Console.WriteLine($"[UnpackMdd] Writing record @ {fullPath}");
            File.WriteAllBytes(fullPath, v);
        }

        Console.WriteLine($"Extracted {mdd.Count} entries to {target}");
    }

    // https://github.com/liuyug/mdict-utils/blob/64e15b99aca786dbf65e5a2274f85547f8029f2e/mdict_utils/writer.py#L509
    public static D PackMddFile(string source)
    {
        D dictionary = [];
        source = Path.GetFullPath(source);

        if (File.Exists(source))
        {
            // Single file
            long size = new FileInfo(source).Length;
            string key = "\\" + Path.GetFileName(source);
            if (Path.DirectorySeparatorChar != '\\')
                key = key.Replace(Path.DirectorySeparatorChar, '\\');

            dictionary.Add(new MDictEntry
            {
                Key = key,
                Pos = 0,
                Path = source,
                Size = size
            });
        }
        else if (Directory.Exists(source))
        {
            // Directory walk
            string relpath = source;
            foreach (var fpath in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
            {
                long size = new FileInfo(fpath).Length;
                string key = "\\" + Path.GetRelativePath(relpath, fpath);
                if (Path.DirectorySeparatorChar != '\\')
                    key = key.Replace(Path.DirectorySeparatorChar, '\\');

                dictionary.Add(new MDictEntry
                {
                    Key = key,
                    Pos = 0,
                    Path = fpath,
                    Size = size
                });
            }
        }
        else
        {
            throw new FileNotFoundException($"Path does not exist: {source}");
        }

        return dictionary;
    }

    // https://github.com/liuyug/mdict-utils/blob/master/mdict_utils/writer.py#L425
    public static D PackMdxTxt(string source, Encoding encoding = null)
    {
        encoding ??= Encoding.UTF8;
        D dictionary = [];
        List<string> sources = [];
        int nullLength = encoding.GetByteCount("\0");

        if (File.Exists(source))
            sources.Add(source);
        else if (Directory.Exists(source))
            sources.AddRange(Directory.GetFiles(source, "*.txt"));

        foreach (var path in sources)
        {
            byte[] fileBytes = File.ReadAllBytes(path);
            long pos = 0, offset = 0;
            string key = null;
            int lineNum = 0;

            long i = 0;
            while (i < fileBytes.Length)
            {
                // Read a line (detect LF or CRLF)
                long lineStart = i;
                while (i < fileBytes.Length && fileBytes[i] != 10 && fileBytes[i] != 13) i++;
                long lineEnd = i;

                // Detect newline length
                if (i < fileBytes.Length && fileBytes[i] == 13) i++;
                if (i < fileBytes.Length && fileBytes[i] == 10) i++;

                int lineLength = (int)(lineEnd - lineStart);
                string line = encoding.GetString(fileBytes, (int)lineStart, lineLength).Trim();
                lineNum++;

                if (line.Length == 0)
                {
                    if (key == null)
                        throw new Exception($"Error at line {lineNum}: {path}");
                    continue;
                }

                if (line == "</>")
                {
                    if (key == null || offset == pos)
                        throw new Exception($"Error at line {lineNum}: {path}");

                    long size = offset - pos + nullLength;
                    dictionary.Add(new MDictEntry
                    {
                        Key = key,
                        Pos = pos,
                        Path = path,
                        Size = size
                    });
                    key = null;
                }
                else if (key == null)
                {
                    key = line;
                    pos = i; // start of definition
                    offset = pos;
                }
                else
                {
                    offset = i; // keep updating
                }
            }
        }

        return dictionary;
    }
}

