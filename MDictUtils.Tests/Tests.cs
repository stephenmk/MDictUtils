using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using MDictUtils.Build.Offset;
using Xunit;

namespace MDictUtils.Tests;

public class MDictWriterTests
{
    [Fact]
    public void Constructor_WithEmptyDictionary_Succeeds()
    {
        var entries = new List<MDictEntry>();
        var writer = new MDictWriter(entries);
        Assert.NotNull(writer);
    }

    [Fact]
    public void Write_CreatesValidFile()
    {
        var entries = new List<MDictEntry>();
        var metadata = new MDictMetadata(
            Title: "Test Dictionary",
            Description: "A test dictionary");

        var writer = new MDictWriter(entries, metadata);
        var outputPath = Path.GetTempFileName();

        try
        {
            writer.Write(outputPath);
            Assert.True(File.Exists(outputPath));
            var fileInfo = new FileInfo(outputPath);
            Assert.True(fileInfo.Length > 0, "File should not be empty");
        }
        finally
        {
            if (File.Exists(outputPath))
                File.Delete(outputPath);
        }
    }

    [Fact]
    public void Write_WithUTF8Encoding_CreatesFile()
    {
        var entries = new List<MDictEntry>();
        var writer = new MDictWriter(entries, new(Encoding: "utf8"));
        var outputPath = Path.GetTempFileName();

        try
        {
            writer.Write(outputPath);
            Assert.True(File.Exists(outputPath));
        }
        finally
        {
            if (File.Exists(outputPath))
                File.Delete(outputPath);
        }
    }
}

public class MDictSorterTests
{
    [Fact]
    public void TestMDictRegexStrip()
    {
        const string expected = "cc100";
        char[] punctuationChars = [.. KeyComparer.PunctuationChars, ' '];
        foreach (var punctuation in punctuationChars)
        {
            var test = expected.Insert(3, punctuation.ToString());
            var actual = KeyComparer.RegexStrip.Replace(test, "");
            Assert.Equal(expected, actual);
        }
    }

    [Fact]
    public void TestMDictSorting_OracleOrder()
    {
        List<(string Key, int ExpectedIndex)> items =
        [
            ("ångström", 3),
            ("@cc-100", 2),
            ("banana", 1),
            ("apple", 0),
        ];

        var expected = new List<(string Key, int ExpectedIndex)>(items);
        expected.Sort((a, b) => a.ExpectedIndex.CompareTo(b.ExpectedIndex));

        var comparer = new MdxKeyComparer();
        items.Sort((a, b) => comparer.Compare(a.Key, b.Key));

        for (int i = 0; i < items.Count; i++)
        {
            Assert.Equal(expected[i].Key, items[i].Key);
            Assert.Equal(expected[i].ExpectedIndex, items[i].ExpectedIndex);
        }
    }
}

public class Adler32Tests
{
    [Fact]
    public void Adler_StaticMethod_Test()
    {
        string[] parts =
        [
            "asdf",
            "12341234asdfasdf",
        ];

        uint[] expected =
        [
            0x040f019f, // big endian
            0x224e04d1,
        ];

        int i = 0;
        foreach (var part in parts)
        {
            byte[] data = Encoding.UTF8.GetBytes(part);
            uint adlerValue = Common.Adler32(data);
            Assert.Equal(expected[i], adlerValue);
            i++;
        }

        byte[] data2 =
        [
            0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x02,
            0x00,0x05, (byte)'a', (byte)'p', (byte)'p', (byte)'l', (byte)'e',
            0x00,0x00,
            0x06, (byte)'b', (byte)'a', (byte)'n', (byte)'a', (byte)'n', (byte)'a',
            0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,
            0x21,  // <- THIS is the correct byte, 0x21 = '!'
            0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x1d
        ];
        uint adlerValue2 = Common.Adler32(data2); // Replace with your static method
        Assert.Equal((uint)1872954559, adlerValue2);
        // python -c "import zlib, struct; data = b'\x00\x00\x00\x00\x00\x00\x00\x02\x00\x05apple\x00\x00\x06banana\x00\x00\x00\x00\x00\x00\x00\x00!\x00\x00\x00\x00\x00\x00\x00\x1d'; checksum = zlib.adler32(data) & 0xffffffff; print('Little-endian:', struct.pack('<L', checksum)); print('Big-endian:', struct.pack('>L', checksum)); print(checksum)"
        //
        // Little-endian: b'\xbf\x04\xa3o'
        // Big-endian: b'o\xa3\x04\xbf'
        // 1872954559
    }
}

public class HeaderTests
{
    [Fact]
    public void GetHeaderString_ShouldNotReplaceLineEndingsInTitle()
    {
        const string title = "Title\r\n\n[2026-04-04]";
        var metadata = new MDictMetadata(Title: title);
        var writer = new MDictWriter([], metadata);
        var header = writer.GetHeaderString();
        Assert.Contains($"Title=\"{title}\"", header);
        // Should not have newlines between elements
        Assert.Contains("<Dictionary GeneratedByEngineVersion=\"2.0\" RequiredEngineVersion=\"2.0\"", header);
        Assert.EndsWith("\r\n\0", header);
    }
}

// Pack and Unpack should be reversable
public class DoUndoTests
{
    private static void AssertContentEqual(string expected, string actual)
    {
        string normalizedExpected = expected.Replace("\r\n", "\n").Replace("\r", "\n").TrimEnd('\n');
        string normalizedActual = actual.Replace("\r\n", "\n").Replace("\r", "\n").TrimEnd('\n');
        Assert.Equal(normalizedExpected, normalizedActual);
    }

    public static TheoryData<string> TestContents => new()
        {
            """
            single
            Just one entry.
            </>
            """,

            """
            apple
            A fruit that grows on trees.
            </>
            banana
            A long yellow fruit.
            </>
            @cc-100
            xxx
            </>
            """,
        };

    [Theory]
    [MemberData(nameof(TestContents))]
    public void DoUndo_PackAndUnpackMdx_ProducesIdenticalFile(string testContent)
    {
        const bool isMdd = false;

        string tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);
        string originalDictPath = Path.Combine(tempDir, "dict1.txt");
        string outMdxPath = Path.Combine(tempDir, "out.mdx");
        string extractedDictPath = Path.Combine(tempDir, "out.mdx.txt");

        try
        {
            // Create dict1.txt
            File.WriteAllText(originalDictPath, testContent);

            // Pack it into out.mdx
            var packedEntries = MDictPacker.PackMdxTxt(originalDictPath);
            var writer = new MDictWriter(packedEntries, new(IsMdd: isMdd));
            writer.Write(outMdxPath);

            // Unpack out1.mdx to tempDir and compare normalized
            MDictPacker.Unpack(tempDir, outMdxPath, isMdd: isMdd);
            Assert.True(File.Exists(extractedDictPath), "Extracted file should exist");
            string extractedContent = File.ReadAllText(extractedDictPath);
            AssertContentEqual(testContent, extractedContent);
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
    }

    [Theory]
    [MemberData(nameof(TestContents))]
    public void DoUndo_PackAndUnpackMdd_ProducesIdenticalFile(string testContent)
    {
        const bool isMdd = true;

        string tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);
        string originalStubPath = Path.Combine(tempDir, "dict1.txt");
        string outMddPath = Path.Combine(tempDir, "out.mdd");
        string extractedStubPath = Path.Combine(tempDir, "dict1.txt");

        try
        {
            // Create dict1.txt
            File.WriteAllText(originalStubPath, testContent);

            // Pack it into out.mdd
            var packedEntries = MDictPacker.PackMddFile(originalStubPath);
            var writer = new MDictWriter(packedEntries, new(IsMdd: isMdd));
            writer.Write(outMddPath);

            // Unpack out1.mdd to tempDir and compare normalized
            MDictPacker.Unpack(tempDir, outMddPath, isMdd: isMdd);
            Assert.True(File.Exists(extractedStubPath), "Extracted file should exist");
            string extractedContent = File.ReadAllText(extractedStubPath);
            AssertContentEqual(testContent, extractedContent);
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
    }


    public static TheoryData<string[]> MultipleFileTestContents =>
        [
            [
                """
                apple
                A fruit that grows on trees.
                </>
                banana
                A long yellow fruit.
                </>
                """,

                """
                cherry
                A small round red fruit.
                </>
                """
            ],

            [
                """
                apple
                A fruit that grows on trees.
                </>
                """,

                """
                banana
                A long yellow fruit.
                </>
                """,

                """
                cherry
                A small round red fruit.
                </>
                """
            ]
        ];

    [Theory]
    [MemberData(nameof(MultipleFileTestContents))]
    public void DoUndo_PackAndUnpackMdxWithMultipleFiles_ProducesIdenticalFiles(string[] contents)
    {
        const bool isMdd = false;

        // This should be the result since the fixture keys are sorted
        string combinedOriginal = string.Join("\n", contents);

        string tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);
        string sourceDir = Path.Combine(tempDir, "source");
        Directory.CreateDirectory(sourceDir);
        string outMdxPath = Path.Combine(tempDir, "out.mdx");
        string extractPath = Path.Combine(tempDir, "out.mdx.txt");

        try
        {
            // Create dictX.txt
            for (int i = 0; i < contents.Length; i++)
            {
                string filePath = Path.Combine(sourceDir, $"dict{i + 1}.txt");
                File.WriteAllText(filePath, contents[i]);
            }

            // Pack the entire source directory into out.mdx
            var packedEntries = MDictPacker.PackMdxTxt(sourceDir);
            var writer = new MDictWriter(packedEntries, new(IsMdd: isMdd));
            writer.Write(outMdxPath);

            // Unpack out.mdx and compare normalized
            MDictPacker.Unpack(tempDir, outMdxPath, isMdd: isMdd);
            Assert.True(File.Exists(extractPath), "Extracted file should exist");
            string extractedContent = File.ReadAllText(extractPath);
            AssertContentEqual(combinedOriginal, extractedContent);
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
    }
}

public class Ripemd128Tests
{
    // https://emn178.github.io/online-tools/ripemd-128/
    [Theory]
    [InlineData("", "cdf26213a150dc3ecb610f18f6b38b46")]
    [InlineData("a", "86be7afa339d0fc7cfc785e72f578d33")]
    [InlineData("abc", "c14a12199c66e4ba84636b0f69144c77")]
    [InlineData("message digest", "9e327b3d6e523062afc1132d7df9d1b8")]
    [InlineData("The quick brown fox jumps over the lazy dog", "3fa9b57f053c053fbe2735b2380db596")]
    public void TestKnownVectors(string input, string expected)
    {
        byte[] inputBytes = Encoding.UTF8.GetBytes(input);
        Span<byte> hash = stackalloc byte[16];
        int size = Ripemd128.ComputeHash(inputBytes, hash);
        string hexHash = Convert.ToHexString(hash[..size]).ToLowerInvariant();
        Assert.Equal(expected, hexHash);
    }
}
