using Microsoft.Extensions.DependencyInjection;

namespace MDictUtils.Creation;

public sealed class MddCreator(string? filepath = null) : MDictCreator(filepath)
{
    /// <summary>
    /// Add a media record to the MDD file.
    /// </summary>
    /// <param name="key">Dictionary media URI, e.g. `\images\1.png` or `\audio\sample.mp3`.</param>
    /// <param name="path">Path to the media data on disk, e.g. `/home/user/media/sample.mp3`.</param>
    public void AddEntry(string key, string path)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);

        var info = new FileInfo(path);
        var size = Convert.ToInt32(info.Length); // Files >2GB are not supported.

        _entries.Add(new(key, path, 0, size));
    }

    /// <summary>
    /// Add a media record to the MDD file.
    /// </summary>
    /// <param name="key">Dictionary media URI, e.g. `\images\1.png` or `\audio\sample.mp3`.</param>
    /// <param name="bytes">The media data in bytes.</param>
    public async Task AddEntryAsync(string key, ReadOnlyMemory<byte> bytes)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);

        var size = bytes.Length;
        _entries.Add(new(key, _filepath, _currentPosition, size));
        _currentPosition += size;
        await _stream.WriteAsync(bytes);
    }

    /// <summary>
    /// Write all added entries to a new MDD file.
    /// </summary>
    public async Task WriteAsync(MddHeader header, string outputFile, Action<MddWriterOptions>? configure = null)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);

        var writer = GetWriter(configure);
        await _stream.FlushAsync();
        await writer.WriteAsync(header, _entries, outputFile);
    }

    private static IMddWriter GetWriter(Action<MddWriterOptions>? configure)
        => new ServiceCollection()
            .AddMddWriter(configure)
            .BuildServiceProvider()
            .GetRequiredService<IMddWriter>();
}
