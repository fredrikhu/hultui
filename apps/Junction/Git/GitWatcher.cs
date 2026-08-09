namespace Junction.Git;

/// <summary>
/// Watches a git repository's key files and working tree for changes,
/// firing a debounced callback when something changes. This enables
/// auto-refresh when external tools (editors, terminal git commands)
/// modify the repo.
///
/// Watches:
///   • .git/HEAD           — branch switches, rebase
///   • .git/index          — staging changes from external commands
///   • .git/refs/**        — branch/tag creation, deletion, updates
///   • Working tree        — file edits by external editors
///
/// All events are debounced into a single callback with a configurable
/// delay (default 300ms) so that multi-file git operations don't cause
/// a storm of refreshes.
/// </summary>
public sealed class GitWatcher : IDisposable
{
    private readonly FileSystemWatcher _gitDirWatcher;
    private readonly FileSystemWatcher _refsWatcher;
    private readonly FileSystemWatcher _workTreeWatcher;
    private readonly Action _onChange;
    private readonly int _debounceMs;
    private CancellationTokenSource? _debounceCts;
    private readonly object _lock = new();

    public GitWatcher(string repoPath, Action onChange, int debounceMs = 300)
    {
        _onChange = onChange;
        _debounceMs = debounceMs;

        var gitDir = Path.Combine(repoPath, ".git");

        // Watch HEAD and index — these are files directly in .git/
        _gitDirWatcher = new FileSystemWatcher(gitDir)
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName,
            EnableRaisingEvents = true,
            IncludeSubdirectories = false,
        };
        _gitDirWatcher.Changed += OnGitDirChanged;
        _gitDirWatcher.Created += OnGitDirChanged;

        // Watch refs/ recursively — branches, tags, remote tracking refs
        var refsDir = Path.Combine(gitDir, "refs");
        _refsWatcher = new FileSystemWatcher(refsDir)
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName,
            EnableRaisingEvents = true,
            IncludeSubdirectories = true,
        };
        _refsWatcher.Changed += OnFileChanged;
        _refsWatcher.Created += OnFileChanged;
        _refsWatcher.Deleted += OnFileChanged;
        _refsWatcher.Renamed += OnFileChanged;

        // Watch working tree — exclude .git/ directory
        _workTreeWatcher = new FileSystemWatcher(repoPath)
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName
                         | NotifyFilters.DirectoryName,
            EnableRaisingEvents = true,
            IncludeSubdirectories = true,
        };
        _workTreeWatcher.Changed += OnWorkTreeChanged;
        _workTreeWatcher.Created += OnWorkTreeChanged;
        _workTreeWatcher.Deleted += OnWorkTreeChanged;
        _workTreeWatcher.Renamed += OnWorkTreeChanged;
    }

    private void OnGitDirChanged(object sender, FileSystemEventArgs e)
    {
        // Only care about HEAD and index
        var name = Path.GetFileName(e.FullPath);
        if (name is "HEAD" or "index" or "MERGE_HEAD" or "REBASE_HEAD"
            or "COMMIT_EDITMSG")
        {
            ScheduleRefresh();
        }
    }

    private void OnWorkTreeChanged(object sender, FileSystemEventArgs e)
    {
        // Ignore changes inside .git/
        if (e.FullPath.Contains(Path.DirectorySeparatorChar + ".git" + Path.DirectorySeparatorChar)
            || e.FullPath.EndsWith(Path.DirectorySeparatorChar + ".git"))
            return;

        ScheduleRefresh();
    }

    private void OnFileChanged(object sender, FileSystemEventArgs e)
    {
        ScheduleRefresh();
    }

    private void ScheduleRefresh()
    {
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
                    if (!token.IsCancellationRequested)
                        HultUI.Application.InvokeOnMainThread(_onChange);
                }
                catch (TaskCanceledException) { }
            }, token);
        }
    }

    public void Dispose()
    {
        _gitDirWatcher.EnableRaisingEvents = false;
        _refsWatcher.EnableRaisingEvents = false;
        _workTreeWatcher.EnableRaisingEvents = false;
        _gitDirWatcher.Dispose();
        _refsWatcher.Dispose();
        _workTreeWatcher.Dispose();
        _debounceCts?.Cancel();
        _debounceCts?.Dispose();
    }
}
