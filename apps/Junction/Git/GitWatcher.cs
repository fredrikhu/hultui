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
    /// <summary>What the window heard: a change that is told as it is (the working tree, a
    /// ref), and the index rewritten, which is told only when its entries changed. Whether
    /// <see cref="OnDue"/> is reading the index outside the lock. Guarded by <see cref="_lock"/>.</summary>
    private bool _windowTold, _windowIndex, _busy;
    /// <summary>A hash of the index's entries when it was last read (see <see cref="IndexPrint"/>),
    /// or null before the first read. Only <see cref="OnDue"/> touches it, one at a time.</summary>
    private ulong? _indexPrint;
    /// <summary>The length of an object id in the index: 20 bytes, 32 in a SHA-256 repository;
    /// 0 until the config is read.</summary>
    private int _oidLength;
    private readonly string _gitDir;

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

        var gitDir = _gitDir = Path.Combine(repoPath, ".git");

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
        if (name == "index") ScheduleIndex();
        else if (IsRefsFile(name)) ScheduleRefresh(refs: true);
    }

    // Git writes HEAD, the config, the packed refs and the index to a lock file and renames it
    // over the old one, which a Changed or Created on the name never reports: a checkout was
    // seen only through the working-tree files it changed, a remote added not at all, and
    // staging from a terminal or an editor (`git add`, `restore --staged`, a submodule bump)
    // never. The index is rewritten by reads too: `git status` (optional locks on, as outside
    // the tests) refreshes the file times it holds, as after a file rewritten with the same
    // bytes, so its rename is told only when the entries changed (OnDue).
    private void OnGitDirRenamed(object sender, RenamedEventArgs e)
    {
        var name = Path.GetFileName(e.FullPath);
        if (name == "index") ScheduleIndex();
        else if (IsRefsFile(name)) ScheduleRefresh(refs: true);
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
            _refsMoved |= refs;
            _windowTold = true;
            Heard();
        }
    }

    private void ScheduleIndex()
    {
        lock (_lock)
        {
            if (_disposed) return;
            _windowIndex = true;
            Heard();
        }
    }

    /// <summary>Counts an event and moves the window's end on. Under <see cref="_lock"/>.</summary>
    private void Heard()
    {
        _heard++;
        // The window closes a debounce after the last event. The timer is set once a window;
        // OnDue moves it on while events keep coming, rather than every event doing so.
        _due = Environment.TickCount64 + _debounceMs;
        if (!_armed)
        {
            _armed = true;
            _debounce.Change(TimeSpan.FromMilliseconds(_debounceMs), Timeout.InfiniteTimeSpan);
        }
    }

    /// <summary>The debounce timer fired (on the thread pool): if an event came since it was
    /// set, wait out the rest of that event's debounce; otherwise tell what the window heard.
    /// An index rewritten with the entries it had (file times refreshed, by this app's own
    /// `git status` as much as anyone's) is not a change anything shows.</summary>
    private void OnDue()
    {
        bool moved, told, index;
        long heard;
        lock (_lock)
        {
            if (_disposed || !_armed) return;   // stopped by Forget or Dispose
            long wait = _due - Environment.TickCount64;
            if (wait > 0 || _busy)
            {
                _debounce.Change(TimeSpan.FromMilliseconds(wait > 0 ? wait : _debounceMs), Timeout.InfiniteTimeSpan);
                return;
            }
            _armed = false;
            told = _windowTold;
            index = _windowIndex;
            if (!told && !index) return;
            _windowTold = _windowIndex = false;
            moved = _refsMoved;
            _refsMoved = false;
            heard = _heard;
            if (told) _posted = heard;
            _busy = index;
        }
        if (index)
        {
            bool changed;
            try { changed = IndexChanged(); }
            finally { lock (_lock) _busy = false; }
            if (!changed && !told) return;
        }
        lock (_lock) _posted = Math.Max(_posted, heard);
        HultUI.Application.InvokeOnMainThread(() => Tell(moved, heard));
    }

    /// <summary>Reads the index and says whether its entries differ from the last read (or
    /// could not be compared). Off the UI thread.</summary>
    private bool IndexChanged()
    {
        if (_oidLength == 0) _oidLength = ObjectIdLength(_gitDir);
        var print = IndexPrint(Path.Combine(_gitDir, "index"), _oidLength);
        bool changed = print is null || _indexPrint is null || print != _indexPrint;
        _indexPrint = print;
        return changed;
    }

    /// <summary>20 bytes, or 32 when the repository's objects are SHA-256
    /// (<c>extensions.objectFormat</c>).</summary>
    private static int ObjectIdLength(string gitDir)
    {
        try
        {
            foreach (var line in File.ReadLines(Path.Combine(gitDir, "config")))
            {
                var t = line.AsSpan().Trim();
                if (t.StartsWith("objectformat", StringComparison.OrdinalIgnoreCase)
                    && t.Contains("sha256", StringComparison.OrdinalIgnoreCase))
                    return 32;
            }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        return 20;
    }

    /// <summary>
    /// A hash of what the index's entries hold — each one's path, mode, object id and flags
    /// (stage, assume-unchanged, skip-worktree, intent-to-add) — leaving out the file times,
    /// inode and size git keeps to know a file is unchanged, which a status refreshes; and the
    /// extensions after the entries (the cache tree, the untracked cache), which say nothing
    /// staged. Null when the file is missing, unreadable or not an index git wrote. Read with
    /// every share mode, so git can rename over it meanwhile.
    /// </summary>
    internal static ulong? IndexPrint(string path, int oidLength)
    {
        byte[] buffer = System.Buffers.ArrayPool<byte>.Shared.Rent(64 * 1024);
        try
        {
            using var file = new FileStream(path, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete, bufferSize: 0);
            var r = new IndexReader(file, buffer);
            if (!r.Fill(12)) return null;
            var header = r.Span(12);
            if (!header[..4].SequenceEqual("DIRC"u8)) return null;
            uint version = System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(header[4..]);
            uint count = System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(header[8..]);
            if (version is < 2 or > 4) return null;
            ulong hash = Fnv(FnvOffset, header[4..12]);
            r.Skip(12);
            int stat = 40, fixedLength = stat + oidLength + 2;
            for (uint i = 0; i < count; i++)
            {
                if (!r.Fill(fixedLength + 2)) return null;
                var entry = r.Span(fixedLength + 2);
                hash = Fnv(hash, entry.Slice(24, 4));                  // mode
                hash = Fnv(hash, entry.Slice(stat, oidLength + 2));    // object id, flags
                int head = fixedLength;
                if ((entry[stat + oidLength] & 0x40) != 0)             // extended flags follow
                {
                    if (version < 3) return null;
                    hash = Fnv(hash, entry.Slice(fixedLength, 2));
                    head += 2;
                }
                r.Skip(head);
                if (version == 4)
                {
                    // How much of the previous entry's path to drop (a varint), then the rest.
                    for (int n = 0; ; n++)
                    {
                        if (n == 10 || !r.Fill(1)) return null;
                        byte b = r.Span(1)[0];
                        hash = Fnv(hash, r.Span(1));
                        r.Skip(1);
                        if ((b & 0x80) == 0) break;
                    }
                    if (r.HashToNul(ref hash) < 0) return null;
                }
                else
                {
                    int name = r.HashToNul(ref hash);              // with its NUL
                    if (name < 0) return null;
                    int padded = (head + name - 1 + 8) & ~7;        // git pads each entry to 8 bytes
                    if (!r.Fill(padded - head - name)) return null;
                    r.Skip(padded - head - name);
                }
            }
            return hash;
        }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
        finally { System.Buffers.ArrayPool<byte>.Shared.Return(buffer); }
    }

    private const ulong FnvOffset = 14695981039346656037UL;

    private static ulong Fnv(ulong hash, ReadOnlySpan<byte> bytes)
    {
        foreach (byte b in bytes) hash = (hash ^ b) * 1099511628211UL;
        return hash;
    }

    /// <summary>Reads a file through one buffer, keeping what is not consumed yet at its start.</summary>
    private struct IndexReader(Stream stream, byte[] buffer)
    {
        private int _start, _end;

        /// <summary>Whether <paramref name="count"/> bytes are buffered, reading more if not.</summary>
        public bool Fill(int count)
        {
            if (_end - _start >= count) return true;
            if (count > buffer.Length) return false;
            if (_start > 0)
            {
                Buffer.BlockCopy(buffer, _start, buffer, 0, _end - _start);
                _end -= _start;
                _start = 0;
            }
            while (_end < count)
            {
                int n = stream.Read(buffer, _end, buffer.Length - _end);
                if (n == 0) return false;
                _end += n;
            }
            return true;
        }

        public readonly ReadOnlySpan<byte> Span(int count) => buffer.AsSpan(_start, count);

        public void Skip(int count) => _start += count;

        /// <summary>Hashes the bytes up to and including the next NUL and consumes them; returns
        /// how many, or -1 when the file ends first or the name is longer than the buffer.</summary>
        public int HashToNul(ref ulong hash)
        {
            for (int scanned = 0; ;)
            {
                int at = buffer.AsSpan(_start + scanned, _end - _start - scanned).IndexOf((byte)0);
                if (at >= 0)
                {
                    int length = scanned + at + 1;
                    hash = Fnv(hash, buffer.AsSpan(_start, length));
                    _start += length;
                    return length;
                }
                scanned = _end - _start;
                if (!Fill(scanned + 1)) return -1;
            }
        }
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
    /// change made after this call is lost, the app's or anyone else's. Returns whether a change
    /// heard in the working tree or the refs was still untold; an index write alone is not one
    /// (the caller's own staging writes it, and its entries are told only if they changed).
    /// Call on the UI thread.
    /// </summary>
    public bool Forget()
    {
        lock (_lock)
        {
            bool untold = _windowTold || _posted > Math.Max(_told, _forgotten);
            _forgotten = _heard;
            _refsMoved = false;
            // An index write forgotten is not read either: the next one is compared with what
            // was read before it, so a change the reload may have missed is never taken as seen.
            _windowTold = _windowIndex = false;
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
