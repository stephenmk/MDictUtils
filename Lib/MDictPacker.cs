using System.Text;

namespace Lib;

/// <summary>
/// Class for both writing (packing) and reading (unpacking)
/// </summary>
public static class MDictPacker
{
    // python does not include the BOM in the title/description
    // so we do the same to allow for oracle testing (but it should not matter really)
    private static readonly UTF8Encoding UTF8NoBOM = new(false);

    public static void Unpack(string target, string source, bool isMdd)
    {
        // This creates intermediate folders, in case target = d1/d2/folder
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

        if (header.TryGetValue("Description", out var description) && description.Length > 0)
        {
            string descPath = Path.Combine(target, $"{basename}.description.html");
            // Console.WriteLine($"[UnpackMdx] Writing description to {descPath}...");
            using FileStream fs = new(descPath, FileMode.Create, FileAccess.Write);
            using StreamWriter swriter = new(fs, UTF8NoBOM);

            // f.write(b'\r\n'.join(mdx.header[b'Description'].splitlines()))
            // Force CRLF like the python model
            var lines = description.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
            swriter.Write(string.Join("\r\n", lines));
        }

        if (header.TryGetValue("Title", out var title) && title.Length > 0)
        {
            string titlePath = Path.Combine(target, $"{basename}.title.html");
            // Console.WriteLine($"[UnpackMdx] Writing title to {titlePath}...");
            File.WriteAllText(titlePath, title, UTF8NoBOM);
        }

        // We only support split - None
        // Since split is None, we just write everything to a single file
        string outPath = Path.Combine(target, $"{basename}.txt");

        using FileStream tf = new(outPath, FileMode.Create, FileAccess.Write);
        using BinaryWriter writer = new(tf);

        int itemCount = 0;

        foreach (var (key, bytes) in mdx.Items())
        {
            // if not value.strip(): continue
            if (bytes.Length == 0 || bytes.All(static b => char.IsWhiteSpace((char)b)))
            {
                continue;
            }

            itemCount++;

            byte[] keyBytes = Encoding.UTF8.GetBytes(key);
            writer.Write(keyBytes);
            writer.Write("\r\n"u8);

            writer.Write(bytes);
            if (bytes.Length == 0 || bytes[^1] != (byte)'\n')
            {
                writer.Write("\r\n"u8);
            }

            writer.Write("</>\r\n"u8);
        }
    }

    public static void UnpackMdd(string target, string source)
    {
        MDD mdd = new(source);
        var datafolder = Path.GetFullPath(target);

        foreach (var (fname, bytes) in mdd.Items())
        {
            // fname = key.decode('UTF-8').replace('\\', os.path.sep)
            // We trim at start, because Path.Combine will not combine if the second arg is a dir...
            var fnameClean = fname.TrimStart('\\').Replace('\\', Path.DirectorySeparatorChar);
            var dfname = Path.Combine(datafolder, fnameClean);
            string? dir = Path.GetDirectoryName(dfname);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }
            // Console.WriteLine($"[UnpackMdd] {datafolder} | {fnameClean} | {dfname}");
            File.WriteAllBytes(dfname, bytes);
        }

        Console.WriteLine($"Extracted {mdd.Count} entries to {target}");
    }

    // https://github.com/liuyug/mdict-utils/blob/64e15b99aca786dbf65e5a2274f85547f8029f2e/mdict_utils/writer.py#L509
    public static List<MDictEntry> PackMddFile(string source)
    {
        List<MDictEntry> entries = [];
        source = Path.GetFullPath(source);

        if (File.Exists(source))
        {
            // Single file (wtf is happening with separators?)
            long size = new FileInfo(source).Length;
            string key = "\\" + Path.GetFileName(source);
            if (Path.DirectorySeparatorChar != '\\')
                key = key.Replace(Path.DirectorySeparatorChar, '\\');

            entries.Add(new(key, Pos: 0, Path: source, size));
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

                entries.Add(new(key, Pos: 0, fpath, size));
            }
        }
        else
        {
            throw new FileNotFoundException($"Path does not exist: {source}");
        }

        return entries;
    }

    // https://github.com/liuyug/mdict-utils/blob/master/mdict_utils/writer.py#L425
    public static List<MDictEntry> PackMdxTxt(string source, Encoding? encoding = null)
    {
        encoding ??= Encoding.UTF8;
        List<MDictEntry> entries = [];
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
            string? key = null;
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
                    entries.Add(new MDictEntry(key, pos, path, size));
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

        return entries;
    }
}
