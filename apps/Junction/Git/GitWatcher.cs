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
///   • .git/reftable       — every ref of a reftable repository
///   • Working tree        — file edits by external editors
///
/// All events are debounced into a single callback with a configurable
/// delay (default 300ms) so that multi-file git operations don't cause
/// a storm of refreshes. The callback says whether anything but the working
/// tree and the index changed in that window: HEAD, a ref, the config or
/// the packed refs, which are what the log, the branches, the tags and the
/// remotes are read from.
/// </summary>
public sealed class GitWatcher : IDisposable
{
    private readonly FileSystemWatcher _gitDirWatcher;
    private readonly FileSystemWatcher _refsWatcher;
    private readonly FileSystemWatcher _workTreeWatcher;
    private readonly FileSystemWatcher? _reftableWatcher;
    private readonly Action<bool> _onChange;
    private readonly int _debounceMs;
    /// <summary>The debounce: one timer, armed by the first event of a window and moved on by
    /// <see cref="OnDue"/> until <see cref="_due"/> has passed. A build writes hundreds of
    /// files a second; a new token, task and delay per event, and the cancelled delay's
    /// exception, were most of what the watcher cost (plan 17 round 14).</summary>
    private readonly ITimer _debounce;
    private readonly object _lock = new();
    /// <summary>Whether HEAD, a ref, the config or the packed refs changed since the last
    /// callback. Guarded by <see cref="_lock"/>.</summary>
    private bool _refsMoved;
    /// <summary>How many changes were heard; how many of them the last callback told; how many
    /// the last <see cref="Forget"/> covered; how many the last callback posted had heard.
    /// Guarded by <see cref="_lock"/>.</summary>
    private long _heard, _told, _forgotten, _posted;
    /// <summary>When the window closes (<see cref="Environment.TickCount64"/>): the debounce
    /// after the last event heard. Whether the timer is set to fire. Whether it was
    /// disposed. Guarded by <see cref="_lock"/>.</summary>
    private long _due;
    private bool _armed, _disposed;

    private static readonly string GitInside = Path.DirectorySeparatorChar + ".git" + Path.DirectorySeparatorChar;
    private static readonly string GitAtEnd = Path.DirectorySeparatorChar + ".git";

    /// <param name="onChange">Called on the UI thread, debounced; its argument is true when
    /// HEAD, a ref, the config or the packed refs changed, false when only the working tree or
    /// the index did.</param>
    public GitWatcher(string repoPath, Action<bool> onChange, int debounceMs = 300)
    {
        _onChange = onChange;
        _debounceMs = debounceMs;
        _debounce = TimeProvider.System.CreateTimer(static w => ((GitWatcher)w!).OnDue(), this,
            Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);

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
        _gitDirWatcher.Renamed += OnGitDirRenamed;

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

        // A reftable repository (`git init --ref-format=reftable`, `git refs migrate`) keeps every
        // ref, the branch HEAD names included, in .git/reftable: HEAD and refs/heads are stubs that
        // never change, so neither watcher above hears a checkout, a reset or a branch made. Each
        // committed ref transaction (a ref, a reflog entry, a compaction) writes a new table and
        // renames tables.list.lock over tables.list; one that writes nothing creates the lock and
        // deletes it. Only that rename says a ref moved.
        var reftableDir = Path.Combine(gitDir, "reftable");
        if (Directory.Exists(reftableDir))
        {
            _reftableWatcher = new FileSystemWatcher(reftableDir)
            {
                NotifyFilter = NotifyFilters.FileName,
                EnableRaisingEvents = true,
                IncludeSubdirectories = false,
            };
            _reftableWatcher.Renamed += OnReftableRenamed;
        }

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
        // Only care about HEAD, the index and the files beside them that a ref moving writes
        var name = Path.GetFileName(e.FullPath);
        if (name == "index") ScheduleRefresh(refs: false);
        else if (IsRefsFile(name)) ScheduleRefresh(refs: true);
    }

    // Git writes HEAD, the config and the packed refs to a lock file and renames it over the
    // old one, which a Changed or Created on the name never reports: a checkout was seen only
    // through the working-tree files it changed, a remote added not at all. Not the index:
    // `git status` rewrites it the same way on every run (unless optional locks are off), so
    // its renames would report this app's own reads back to it.
    private void OnGitDirRenamed(object sender, RenamedEventArgs e)
    {
        if (IsRefsFile(Path.GetFileName(e.FullPath))) ScheduleRefresh(refs: true);
    }

    private static bool IsRefsFile(string name) =>
        name is "HEAD" or "MERGE_HEAD" or "REBASE_HEAD" or "COMMIT_EDITMSG" or "config" or "packed-refs";

    private void OnReftableRenamed(object sender, RenamedEventArgs e)
    {
        if (e.Name == "tables.list") ScheduleRefresh(refs: true);
    }

    private void OnWorkTreeChanged(object sender, FileSystemEventArgs e)
    {
        // Ignore changes inside .git/
        if (e.FullPath.Contains(GitInside, StringComparison.Ordinal)
            || e.FullPath.EndsWith(GitAtEnd, StringComparison.Ordinal))
            return;

        ScheduleRefresh(refs: false);
    }

    // Git writes a ref to X.lock and renames that over X. A ref transaction that changes
    // nothing creates the lock and deletes it, and the folder that held it reports a Changed of
    // its own: every `git fetch` takes refs/remotes/<remote>/HEAD.lock to check the remote's
    // HEAD, so a fetch that brought nothing new read the log, the branches and the tags again.
    // A lock or a folder alone is told as a working-tree event, not dropped: a move that writes
    // only a reflog entry (`merge --abort`'s reset, `reflog expire`) shows the watcher nothing
    // else, and the reflog keys on the data generation for it. The ref's own rename or delete
    // is the ref moving.
    private void OnFileChanged(object sender, FileSystemEventArgs e)
    {
        bool lockOnly = e.Name?.EndsWith(".lock", StringComparison.Ordinal) == true
            || (e.ChangeType == WatcherChangeTypes.Changed && Directory.Exists(e.FullPath));
        ScheduleRefresh(refs: !lockOnly);
    }

    private void ScheduleRefresh(bool refs)
    {
        lock (_lock)
        {
            if (_disposed) return;
            _heard++;
            _refsMoved |= refs;
            // The window closes a debounce after the last event. The timer is set once a window;
            // OnDue moves it on while events keep coming, rather than every event doing so.
            _due = Environment.TickCount64 + _debounceMs;
            if (!_armed)
            {
                _armed = true;
                _debounce.Change(TimeSpan.FromMilliseconds(_debounceMs), Timeout.InfiniteTimeSpan);
            }
        }
    }

    /// <summary>The debounce timer fired (on the thread pool): if an event came since it was
    /// set, wait out the rest of that event's debounce; otherwise tell what the window heard.</summary>
    private void OnDue()
    {
        bool moved;
        long heard;
        lock (_lock)
        {
            if (_disposed || !_armed) return;   // stopped by Forget or Dispose
            long wait = _due - Environment.TickCount64;
            if (wait > 0)
            {
                _debounce.Change(TimeSpan.FromMilliseconds(wait), Timeout.InfiniteTimeSpan);
                return;
            }
            _armed = false;
            if (_heard <= Math.Max(_posted, _forgotten)) return;
            moved = _refsMoved;
            _refsMoved = false;
            heard = _posted = _heard;
        }
        HultUI.Application.InvokeOnMainThread(() => Tell(moved, heard));
    }

    private void Tell(bool moved, long heard)
    {
        lock (_lock)
        {
            if (_disposed) return;
            if (heard <= _forgotten) return;   // covered by a Forget made since it was posted
            _told = heard;
        }
        _onChange(moved);
    }

    /// <summary>
    /// Forgets what was heard so far, for a caller about to read the repository again in full
    /// after all of it: the app's own git command has finished and the reload that follows starts
    /// now. Every change heard before this call was made before it, so that reload reads it; the
    /// callback that would tell it 300 ms later, reading it all again, is dropped, one already on
    /// its way to the UI thread included. What is heard from now on is told as before, so no
    /// change made after this call is lost, the app's or anyone else's. Returns whether anything
    /// heard was still untold. Call on the UI thread.
    /// </summary>
    public bool Forget()
    {
        lock (_lock)
        {
            bool untold = _heard > Math.Max(_told, _forgotten);
            _forgotten = _heard;
            _refsMoved = false;
            if (_armed)
            {
                _armed = false;
                _debounce.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
            }
            return untold;
        }
    }

    public void Dispose()
    {
        _gitDirWatcher.EnableRaisingEvents = false;
        _refsWatcher.EnableRaisingEvents = false;
        _workTreeWatcher.EnableRaisingEvents = false;
        if (_reftableWatcher != null) _reftableWatcher.EnableRaisingEvents = false;
        _gitDirWatcher.Dispose();
        _refsWatcher.Dispose();
        _workTreeWatcher.Dispose();
        _reftableWatcher?.Dispose();
        lock (_lock)
        {
            _disposed = true;
            _armed = false;
        }
        _debounce.Dispose();
    }
}
