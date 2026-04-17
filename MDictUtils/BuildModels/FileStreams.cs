using System.Collections.Concurrent;
using System.Collections.Frozen;
using System.IO.MemoryMappedFiles;
using ThreadKey = (string Filepath, int ThreadId);

namespace MDictUtils.BuildModels;

internal sealed class FileStreams(Dictionary<string, int> pathToTotalEntryCount) : IDisposable
{
    private readonly FrozenDictionary<string, int> _pathToTotalEntryCount = pathToTotalEntryCount.ToFrozenDictionary();
    private readonly ConcurrentDictionary<string, int> _pathToEntryCount = [];
    private readonly ConcurrentDictionary<string, MemoryMappedFile> _filepathToFile = [];
    private readonly ConcurrentDictionary<ThreadKey, MemoryMappedViewStream> _threadKeyToStream = [];
    private bool _isDisposed = false;

    /// <summary>
    /// Get a thread-safe, memory-mapped view stream for a file.
    /// </summary>
    public MemoryMappedViewStream GetStream(string filepath)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);

        // Different threads cannot share the same view stream.
        ThreadKey key = (filepath, Environment.CurrentManagedThreadId);

        return _threadKeyToStream
            .GetOrAdd(key, InitializeStream);
    }

    /// <summary>
    /// Update the number of entries read from a file. Dispose of the file if all entries are read.
    /// </summary>
    public void UpdateEntryCount(string filepath)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
        var count = _pathToEntryCount.AddOrUpdate
        (
            key: filepath,
            addValue: 1,
            updateValueFactory: static (key, current) => current + 1
        );
        if (count == _pathToTotalEntryCount[filepath])
        {
            DisposeFile(filepath);
        }
    }

    private MemoryMappedViewStream InitializeStream(ThreadKey key)
    {
        var file = _filepathToFile.GetOrAdd(key.Filepath, InitializeFile);
        return file.CreateViewStream(0, 0, MemoryMappedFileAccess.Read);
    }

    private static MemoryMappedFile InitializeFile(string filepath)
        => MemoryMappedFile
            .CreateFromFile(filepath, FileMode.Open, null, 0, MemoryMappedFileAccess.Read);

    private void DisposeFile(string filepath)
    {
        foreach (var (key, stream) in _threadKeyToStream)
        {
            if (filepath.Equals(key.Filepath, StringComparison.Ordinal))
            {
                stream.Dispose();
            }
        }
        var file = _filepathToFile[filepath];
        file.Dispose();
    }

    void IDisposable.Dispose()
    {
        if (_isDisposed)
            return;

        foreach (var stream in _threadKeyToStream.Values)
            stream.Dispose();
        foreach (var file in _filepathToFile.Values)
            file.Dispose();

        _isDisposed = true;
    }
}
