namespace Meridian.Core;

/// <summary>
/// Watches a workspace for external changes to <c>.req</c>/<c>.env</c> files
/// (and folder renames), firing one debounced callback per burst — the
/// GitWatcher idiom. Paths under dot-directories (<c>.git</c>,
/// <c>.meridian</c>) are ignored.
///
/// <para>Core has no UI reference, so the callback runs on a worker thread;
/// the GUI marshals to its main thread itself.</para>
/// </summary>
public sealed class CollectionWatcher : IDisposable
{
    private readonly FileSystemWatcher _watcher;
    private readonly Action _onChange;
    private readonly int _debounceMs;
    private CancellationTokenSource? _debounceCts;
    private readonly object _lock = new();

    public CollectionWatcher(string root, Action onChange, int debounceMs = 300)
    {
        _onChange = onChange;
        _debounceMs = debounceMs;
        _watcher = new FileSystemWatcher(root)
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.DirectoryName,
            EnableRaisingEvents = true,
            IncludeSubdirectories = true,
        };
        _watcher.Changed += OnFsEvent;
        _watcher.Created += OnFsEvent;
        _watcher.Deleted += OnFsEvent;
        _watcher.Renamed += OnFsEvent;
    }

    private void OnFsEvent(object sender, FileSystemEventArgs e)
    {
        if (!Relevant(e.FullPath)) return;
        lock (_lock)
        {
            _debounceCts?.Cancel();
            _debounceCts = new CancellationTokenSource();
            var token = _debounceCts.Token;
            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(_debounceMs, token);
                    if (!token.IsCancellationRequested) _onChange();
                }
                catch (TaskCanceledException) { }
            }, token);
        }
    }

    /// <summary>A .req/.env file, or a directory event (renames/deletes reshape
    /// the tree) — but never anything under a dot-directory.</summary>
    internal static bool Relevant(string fullPath)
    {
        foreach (var segment in fullPath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
            if (segment.Length > 1 && segment.StartsWith('.') && segment is not "." and not "..")
                return false;

        var ext = Path.GetExtension(fullPath);
        return ext.Length == 0   // likely a directory
            || ext.Equals(".req", StringComparison.OrdinalIgnoreCase)
            || ext.Equals(".env", StringComparison.OrdinalIgnoreCase);
    }

    public void Dispose()
    {
        _watcher.EnableRaisingEvents = false;
        _watcher.Dispose();
        lock (_lock) { _debounceCts?.Cancel(); _debounceCts?.Dispose(); _debounceCts = null; }
    }
}
