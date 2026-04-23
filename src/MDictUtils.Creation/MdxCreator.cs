using System.Text;
using Microsoft.Extensions.DependencyInjection;

namespace MDictUtils.Creation;

public sealed class MdxCreator : MDictCreator
{
    public async Task AddEntryAsync(string key, string body)
    {
        var bytes = Encoding.UTF8.GetBytes(body);
        await AddEntryAsync(key, bytes);
    }

    public async Task AddEntryAsync(string key, ReadOnlyMemory<byte> body)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);

        var size = body.Length;
        _entries.Add(new(key, _filepath, _currentPosition, size + 1)); // Add one extra byte for the null-terminator
        _currentPosition += size;
        await _stream.WriteAsync(body);
    }

    public async Task WriteAsync(MdxHeader header, string outputFile, Action<MdxWriterOptions>? configure = null)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);

        var mdxWriter = GetWriter(configure);
        await _stream.FlushAsync();
        await mdxWriter.WriteAsync(header, _entries, outputFile);
    }

    private static IMdxWriter GetWriter(Action<MdxWriterOptions>? configure)
        => new ServiceCollection()
            .AddMdxWriter(configure)
            .BuildServiceProvider()
            .GetRequiredService<IMdxWriter>();
}
