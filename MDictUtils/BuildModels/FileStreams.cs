using System.Diagnostics;
using System.IO.MemoryMappedFiles;

namespace MDictUtils.BuildModels;

internal sealed class FileStreams(int maxOpenStreams = 128) : IDisposable
{
    private readonly int _maxOpenStreams = maxOpenStreams;
    private readonly Dictionary<string, MemoryMappedViewStream> _filepathToStream = [];
    private readonly List<MemoryMappedFile> _files = [];
    private bool _isDisposed = false;

    public MemoryMappedViewStream GetStream(string filepath)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);

        if (_filepathToStream.TryGetValue(filepath, out var stream))
            return stream;

        return InitializeStream(filepath);
    }

    private MemoryMappedViewStream InitializeStream(string filepath)
    {
        Debug.Assert(!_filepathToStream.ContainsKey(filepath));
        Debug.Assert(_filepathToStream.Count == _files.Count);

        // Sanity check. Please don't use this many files.
        if (_files.Count >= _maxOpenStreams)
            DisposeStreams();

        var file = MemoryMappedFile
            .CreateFromFile(filepath, FileMode.Open, null, 0, MemoryMappedFileAccess.Read);

        var stream = file
            .CreateViewStream(0, 0, MemoryMappedFileAccess.Read);

        _files.Add(file);
        _filepathToStream[filepath] = stream;

        return stream;
    }

    private void DisposeStreams()
    {
        foreach (var stream in _filepathToStream.Values)
            stream.Dispose();
        foreach (var file in _files)
            file.Dispose();

        _filepathToStream.Clear();
        _files.Clear();
    }

    void IDisposable.Dispose()
    {
        if (_isDisposed)
            return;

        DisposeStreams();
        _isDisposed = true;
    }
}
