using System.Globalization;

namespace VesperCommander;

/// <summary>One entry in a pane's directory listing.</summary>
public sealed record FileEntry(
    string Name,
    string FullPath,
    bool IsDir,
    long Size,
    DateTime Modified,
    FileAttributes Attributes = 0,
    bool IsParent = false,
    long PackedSize = -1);   // compressed size for an archive member; -1 = not in an archive

/// <summary>A mounted volume for the drive picker.</summary>
public sealed record DriveEntry(
    string Root, string Label, long Free, long Total, string Format, bool Ready);

/// <summary>The state a pane's listing is in — drives the empty/offline/denied/connecting views.
/// <see cref="Connecting"/> is the only one that is not a property of the folder: it says the
/// pane is still waiting to hear about one, which on a share can take a while.</summary>
public enum PaneState { Ok, Empty, Offline, Denied, Connecting }

/// <summary>A directory listing plus the state it resolved to. <see cref="Archive"/> is
/// set (to a non-Ok status) when a browsed archive couldn't be read — encrypted, corrupt,
/// or needing 7-Zip — so the pane can show the right message.</summary>
/// <param name="Unsafe">Archive members left out because their stored path pointed outside
/// the archive — see <see cref="ArchivePath.SafeMemberPath"/>. The pane footer says so rather
/// than quietly showing a short listing.</param>
public sealed record DirListing(PaneState State, IReadOnlyList<FileEntry> Entries, ArchiveStatus? Archive = null,
    int Unsafe = 0)
{
    public static readonly DirListing Loading = new(PaneState.Ok, []);
}

/// <summary>
/// Real-filesystem backend for the panes. Enumeration runs off the UI thread
/// (via <c>AsyncLoader</c>), so nothing here may touch UI state.
/// </summary>
public static class FileSystem
{
    /// <summary>All mounted volumes for the drive picker. Unready volumes (empty
    /// card readers, disconnected network drives) come back with Ready=false and no
    /// sizes, so the UI can dim them.
    ///
    /// <para>On Linux a network or FUSE mount is listed without being asked anything: whether
    /// it is ready, and its sizes, are questions only it can answer, and one that has stopped
    /// answering held the picker — and the window — for as long as the kernel waits, which for
    /// a hard NFS mount is for ever. It shows as a network volume with no sizes; opening it is
    /// the pane's business, off the UI thread.</para></summary>
    public static IReadOnlyList<DriveEntry> Drives()
    {
        var list = new List<DriveEntry>();
        foreach (var d in DriveInfo.GetDrives())
        {
            try
            {
                if (!OperatingSystem.IsWindows() && PathProbe.IsRemote(d.Name))
                {
                    list.Add(new DriveEntry(d.RootDirectory.FullName, DriveKindLabel(DriveType.Network), 0, 0, d.DriveFormat, Ready: true));
                    continue;
                }
                bool ready = d.IsReady;
                list.Add(new DriveEntry(
                    d.RootDirectory.FullName,
                    ready && d.VolumeLabel.Length > 0 ? d.VolumeLabel : DriveKindLabel(d.DriveType),
                    ready ? d.AvailableFreeSpace : 0,
                    ready ? d.TotalSize : 0,
                    ready ? d.DriveFormat : "",
                    ready));
            }
            catch { /* volume vanished mid-enumeration — skip it */ }
        }
        return list;
    }

    private static string DriveKindLabel(DriveType t) => t switch
    {
        DriveType.Removable => "Removable",
        DriveType.Network => "Network",
        DriveType.CDRom => "Optical",
        DriveType.Ram => "RAM disk",
        _ => "Local disk",
    };

    /// <summary>Supplies the entries a walk iterates, in place of reading the real folder.
    /// The between-entries cancellation check below only means anything partway through a long
    /// walk, and a test cannot cancel at a moment it has no way to observe — a sequence it
    /// yields itself is that moment. Null in every shipped path.</summary>
    internal static Func<string, IEnumerable<FileSystemInfo>>? EnumerateOverride;

    /// <summary>Human byte size (no "free" suffix) — for drive totals.</summary>
    public static string ByteLabel(long b)
    {
        if (b <= 0) return "—";
        double v = b;
        string[] units = ["B", "KB", "MB", "GB", "TB", "PB"];
        int u = 0;
        while (v >= 1024 && u < units.Length - 1) { v /= 1024; u++; }
        return v.ToString(v < 10 ? "0.0" : "0", System.Globalization.CultureInfo.InvariantCulture) + " " + units[u];
    }

    /// <summary>
    /// List <paramref name="path"/> sorted directories-first then files, each
    /// alphabetical (case-insensitive). A ".." parent entry leads the list unless
    /// <paramref name="path"/> is a volume root. Hidden/system entries are omitted
    /// unless <paramref name="showHidden"/>. The result carries a <see cref="PaneState"/>
    /// so the caller can render the empty / offline / denied views from real signals
    /// (an empty listing, an unready volume, or an access error) — no throwing.
    /// </summary>
    /// <param name="ct">Abandons a walk that is no longer wanted. A cancelled listing comes
    /// back as <see cref="DirListing.Loading"/> rather than throwing: <c>AsyncLoader</c> treats
    /// a throwing producer as a failure and retries it three times with backoff, so every
    /// navigation away from a slow folder would relaunch the read it just abandoned. The
    /// sentinel is never shown — the loader's generation check drops a superseded result.
    /// <para>This bounds a <i>long</i> walk, not a dead host: the enumeration syscall itself
    /// is not cancellable, so a folder that never answers keeps its thread until the operating
    /// system gives up. Reaching a host that is not there is <see cref="PathProbe"/>'s job.</para>
    /// </param>
    public static DirListing List(string path, bool showHidden = false,
        SortMode sort = SortMode.Name, bool dirsFirst = true, bool descending = false,
        bool sevenZip = false, string? password = null, CancellationToken ct = default)
    {
        if (ct.IsCancellationRequested) return DirListing.Loading;

        // A bare "\\server" is not a directory — the share below it is the first thing that is —
        // so it is listed through the network provider rather than read off a disk.
        if (PathProbe.ServerRootOf(path) is { } server)
            return ListShares(server, showHidden, sort, descending, ct);

        // A path that descends into an archive lists through the VFS instead of disk.
        if (ArchivePath.TryParse(path) is { } ap)
            return ListArchive(ap.ArchiveFile, ap.Internal, sort, dirsFirst, descending, sevenZip, password);

        var dirs = new List<FileEntry>();
        var files = new List<FileEntry>();

        try
        {
            // Unready volume (unmounted / disconnected) ⇒ offline. Skipped for anything over
            // the network: IsReady on a mapped drive whose server has gone away is a second
            // full network wait, ahead of an enumeration that is about to fail the same way.
            var root = Path.GetPathRoot(path);
            if (!string.IsNullOrEmpty(root) && !PathProbe.IsRemote(path))
            {
                try { if (!new DriveInfo(root).IsReady) return new(PaneState.Offline, []); }
                catch { /* non-drive root — fall through to enumeration */ }
            }

            var dir = new DirectoryInfo(path);
            foreach (var info in EnumerateOverride?.Invoke(path) ?? dir.EnumerateFileSystemInfos())
            {
                // Between entries, the way FindEngine.Walk and SyncEngine do it — a big
                // folder on a slow link stops as soon as nobody is waiting for it.
                if (ct.IsCancellationRequested) return DirListing.Loading;

                FileAttributes attr;
                try { attr = info.Attributes; }
                catch { continue; }   // races / permission on the entry itself — skip it

                if (!showHidden && (attr & (FileAttributes.Hidden | FileAttributes.System)) != 0)
                    continue;

                bool isDir = (attr & FileAttributes.Directory) != 0;
                long size = 0;
                DateTime mtime;
                try
                {
                    mtime = info.LastWriteTime;
                    if (!isDir && info is FileInfo fi) size = fi.Length;
                }
                catch { continue; }

                (isDir ? dirs : files).Add(new FileEntry(info.Name, info.FullName, isDir, size, mtime, attr));
            }
        }
        catch (UnauthorizedAccessException) { return new(PaneState.Denied, []); }
        catch (DirectoryNotFoundException) { return new(PaneState.Offline, []); }
        catch (DriveNotFoundException) { return new(PaneState.Offline, []); }
        catch (IOException) { return new(PaneState.Offline, []); }
        catch { return new(PaneState.Denied, []); }

        var cmp = Comparator(sort, descending);

        var result = new List<FileEntry>(dirs.Count + files.Count + 1);
        var trimmed = path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        // A share root's parent is its server, which Directory.GetParent has no answer for —
        // it stops at "\\server\share" and reports null, leaving the share with no way out.
        var parent = PathProbe.UncParentOf(trimmed) ?? Directory.GetParent(trimmed)?.FullName;
        if (parent != null)
            result.Add(new FileEntry("..", parent, IsDir: true, 0, DateTime.Now, IsParent: true));

        if (dirsFirst)
        {
            dirs.Sort(cmp);
            files.Sort(cmp);
            result.AddRange(dirs);
            result.AddRange(files);
        }
        else
        {
            var all = new List<FileEntry>(dirs.Count + files.Count);
            all.AddRange(dirs); all.AddRange(files);
            all.Sort(cmp);
            result.AddRange(all);
        }

        var state = dirs.Count == 0 && files.Count == 0 ? PaneState.Empty : PaneState.Ok;
        return new(state, result);
    }

    /// <summary>Sort a set of entries the way <see cref="List"/> sorts a folder — same
    /// comparator, same dirs-before-files grouping. Used by the fed-in search-result
    /// listing, which has entries to show but no directory to read them from. Any ".."
    /// parent in the input is dropped; the caller re-adds its own.</summary>
    public static List<FileEntry> Sort(IReadOnlyList<FileEntry> entries,
        SortMode sort = SortMode.Name, bool dirsFirst = true, bool descending = false)
    {
        var cmp = Comparator(sort, descending);
        var dirs = new List<FileEntry>();
        var files = new List<FileEntry>();
        foreach (var e in entries)
        {
            if (e.IsParent) continue;
            (e.IsDir ? dirs : files).Add(e);
        }
        var result = new List<FileEntry>(dirs.Count + files.Count);
        if (dirsFirst)
        {
            dirs.Sort(cmp); files.Sort(cmp);
            result.AddRange(dirs); result.AddRange(files);
        }
        else
        {
            result.AddRange(dirs); result.AddRange(files);
            result.Sort(cmp);
        }
        return result;
    }

    /// <summary>Sort comparator for a listing, honoring the sort key and direction.</summary>
    private static Comparison<FileEntry> Comparator(SortMode sort, bool descending)
    {
        Comparison<FileEntry> cmp = sort switch
        {
            SortMode.Ext => (a, b) =>
            {
                int c = string.Compare(Path.GetExtension(a.Name), Path.GetExtension(b.Name), StringComparison.OrdinalIgnoreCase);
                return c != 0 ? c : string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
            },
            SortMode.Size => (a, b) => a.Size.CompareTo(b.Size),
            SortMode.Date => (a, b) => a.Modified.CompareTo(b.Modified),
            _ => (a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase),
        };
        // Descending reverses within each group; ".." still leads and dirs-before-files holds.
        if (descending) { var asc = cmp; cmp = (a, b) => asc(b, a); }
        return cmp;
    }

    /// <summary>The shares <paramref name="server"/> offers, as a folder listing. No ".." — a
    /// server is the top of its tree. Hidden (<c>$</c>-suffixed) shares follow the same toggle
    /// as hidden files; the network provider rarely offers them at all.
    ///
    /// <para>The host is dialled first, so a server that is not there costs the probe's budget
    /// rather than the operating system's patience — the enumeration is a network call with no
    /// timeout of ours, exactly the shape this whole seam exists to keep bounded.</para></summary>
    private static DirListing ListShares(string server, bool showHidden, SortMode sort,
        bool descending, CancellationToken ct)
    {
        if (!PathProbe.ReachableAsync($@"\\{server}", PathProbe.ReachBudget, ct)
                .GetAwaiter().GetResult())
            return new(PaneState.Offline, []);
        if (ct.IsCancellationRequested) return DirListing.Loading;

        var listing = NetworkShares.List(server, ct);
        if (ct.IsCancellationRequested) return DirListing.Loading;

        var state = listing.Status switch
        {
            ShareListStatus.Ok => PaneState.Ok,
            ShareListStatus.Empty => PaneState.Empty,
            ShareListStatus.Denied => PaneState.Denied,
            _ => PaneState.Offline,
        };
        if (state is not (PaneState.Ok or PaneState.Empty)) return new(state, []);

        var entries = new List<FileEntry>(listing.Shares.Count);
        foreach (var name in listing.Shares)
        {
            if (!showHidden && name.EndsWith('$')) continue;
            entries.Add(new FileEntry(name, $@"\\{server}\{name}", IsDir: true, 0, DateTime.Now));
        }
        entries.Sort(Comparator(sort, descending));
        return new(entries.Count == 0 ? PaneState.Empty : PaneState.Ok, entries);
    }

    /// <summary>List one level inside an archive through the VFS, sorted like a folder.
    /// Non-Ok statuses (empty / corrupt / needs-7-Zip / encrypted) map onto the pane state.</summary>
    private static DirListing ListArchive(string archiveFile, string internalPath,
        SortMode sort, bool dirsFirst, bool descending, bool sevenZip, string? password)
    {
        var al = ArchiveVfs.ListEntries(archiveFile, internalPath, password, sevenZip);
        if (al.Status is not (ArchiveStatus.Ok or ArchiveStatus.Empty))
            return new(PaneState.Denied, al.Entries, al.Status);   // pane renders the specific message
        if (al.Status == ArchiveStatus.Empty)
            return new(PaneState.Empty, al.Entries, ArchiveStatus.Empty, al.Unsafe);

        var cmp = Comparator(sort, descending);
        var parent = al.Entries.Where(e => e.IsParent).ToList();
        var dirs = al.Entries.Where(e => e.IsDir && !e.IsParent).ToList();
        var files = al.Entries.Where(e => !e.IsDir).ToList();
        var result = new List<FileEntry>(al.Entries.Count);
        result.AddRange(parent);
        if (dirsFirst) { dirs.Sort(cmp); files.Sort(cmp); result.AddRange(dirs); result.AddRange(files); }
        else { var all = dirs.Concat(files).ToList(); all.Sort(cmp); result.AddRange(all); }
        return new(PaneState.Ok, result, null, al.Unsafe);
    }

    /// <summary>Human-readable size for the 60px size column; directories read "&lt;DIR&gt;".</summary>
    public static string SizeLabel(FileEntry e)
    {
        if (e.IsParent) return "";
        if (e.IsDir) return "<DIR>";
        return ByteCount(e.Size);
    }

    /// <summary>Compact byte size for the size column ("512 B", "1.5 M", "3 G") — the
    /// single-letter unit form the file rows use, also for a measured folder total.</summary>
    public static string ByteCount(long b)
    {
        if (b < 1000) return $"{b} B";
        double v = b;
        string[] units = ["K", "M", "G", "T", "P"];
        int u = -1;
        do { v /= 1024; u++; } while (v >= 1000 && u < units.Length - 1);
        return v.ToString(v < 10 ? "0.0" : "0", CultureInfo.InvariantCulture) + " " + units[u];
    }

    /// <summary>Recursive byte total of a folder tree (Space "measure folder"). Runs off
    /// the UI thread; calls <paramref name="report"/> with the running total as it goes,
    /// and bails when <paramref name="cancelled"/> returns true. Reparse points
    /// (junctions / symlinks) are skipped so it can't loop. Denied / vanished subtrees
    /// are quietly skipped rather than aborting the whole walk.</summary>
    public static long DirectorySize(string path, Func<bool> cancelled, Action<long> report)
    {
        long total = 0;
        var stack = new Stack<string>();
        stack.Push(path);
        while (stack.Count > 0)
        {
            if (cancelled()) break;
            var dir = stack.Pop();
            try
            {
                foreach (var info in new DirectoryInfo(dir).EnumerateFileSystemInfos())
                {
                    if (cancelled()) break;
                    FileAttributes attr;
                    try { attr = info.Attributes; }
                    catch { continue; }
                    if ((attr & FileAttributes.ReparsePoint) != 0) continue;   // don't follow links
                    if ((attr & FileAttributes.Directory) != 0)
                        stack.Push(info.FullName);
                    else if (info is FileInfo fi)
                    {
                        try { total += fi.Length; }
                        catch { continue; }
                        report(total);
                    }
                }
            }
            catch { /* denied / vanished directory — skip it */ }
        }
        return total;
    }

    /// <summary>ISO date for the 116px date column: <c>YYYY-MM-DD HH:MM</c>.</summary>
    public static string DateLabel(FileEntry e) =>
        e.IsParent ? "" : e.Modified.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);

    /// <summary>Free bytes on the volume backing <paramref name="path"/>, or -1 if unknown.</summary>
    public static long FreeBytes(string path)
    {
        try { return new DriveInfo(Path.GetPathRoot(path) ?? path).AvailableFreeSpace; }
        catch { return -1; }
    }

    /// <summary>Compact attribute string for the preview (à la <c>-a-hr</c>):
    /// archive, hidden, read-only, system.</summary>
    public static string AttrLabel(FileAttributes a)
    {
        Span<char> s = stackalloc char[4];
        s[0] = (a & FileAttributes.Archive) != 0 ? 'a' : '-';
        s[1] = (a & FileAttributes.Hidden) != 0 ? 'h' : '-';
        s[2] = (a & FileAttributes.ReadOnly) != 0 ? 'r' : '-';
        s[3] = (a & FileAttributes.System) != 0 ? 's' : '-';
        return new string(s);
    }

    private const long PreviewMaxBytes = 1024 * 1024;
    private const int PreviewMaxLines = 500;

    /// <summary>
    /// Whether the start of a file reads as text. The one binary test for everything that
    /// shows or searches a file's contents: the preview pane, the viewer, the comparison and
    /// find-in-files, so a file is text in all of them or in none.
    ///
    /// <para>A UTF-16 byte-order mark settles it, because UTF-16 cannot pass the test that
    /// follows: every ASCII character in it is followed by a NUL byte. Windows writes its own
    /// <c>desktop.ini</c> files as UTF-16 LE with a mark, and a 282-byte one used to come up
    /// "binary" in all four places.</para>
    ///
    /// <para>Otherwise a NUL in the head means binary. UTF-16 without a mark is rare enough
    /// on Windows that guessing it from byte patterns would risk showing a real binary as
    /// text with every other character a NUL.</para>
    /// </summary>
    public static bool LooksLikeText(ReadOnlySpan<byte> head)
    {
        if (HasUtf16Mark(head)) return true;
        for (int i = 0; i < head.Length; i++) if (head[i] == 0) return false;
        return true;
    }

    private static bool HasUtf16Mark(ReadOnlySpan<byte> head) =>
        head.Length >= 2 && ((head[0] == 0xFF && head[1] == 0xFE) || (head[0] == 0xFE && head[1] == 0xFF));

    /// <summary>The bytes as a string, or null when <see cref="LooksLikeText"/> says they
    /// are not text. A byte-order mark picks the encoding and is dropped; without one the
    /// bytes are read as UTF-8.</summary>
    public static string? DecodeText(byte[] bytes)
    {
        if (!LooksLikeText(bytes.AsSpan(0, Math.Min(bytes.Length, 8192)))) return null;
        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
            return System.Text.Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2);
        if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
            return System.Text.Encoding.BigEndianUnicode.GetString(bytes, 2, bytes.Length - 2);
        int start = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF ? 3 : 0;
        return System.Text.Encoding.UTF8.GetString(bytes, start, bytes.Length - start);
    }

    /// <summary>Leading lines of a text file for the (scrollable) preview pane, or ""
    /// when the file is too big, binary, or unreadable. Runs off the UI thread; the
    /// line cap bounds how much the preview will scroll through.</summary>
    public static string PreviewText(string path, long size)
    {
        if (size > PreviewMaxBytes) return "";
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            Span<byte> probe = stackalloc byte[512];
            int read = stream.Read(probe);
            if (!LooksLikeText(probe[..read])) return "";
            stream.Position = 0;

            // The reader picks the encoding from the byte-order mark, if there is one.
            using var reader = new StreamReader(stream);
            var sb = new System.Text.StringBuilder();
            string? line;
            int n = 0;
            while (n < PreviewMaxLines && (line = reader.ReadLine()) != null)
            {
                if (n > 0) sb.Append('\n');
                sb.Append(line.Length > 200 ? line[..200] : line);
                n++;
            }
            return sb.ToString();
        }
        catch { return ""; }
    }

    /// <summary>Text excerpt from an in-memory buffer (e.g. an extracted archive member) —
    /// same NUL-binary guard and line/length caps as <see cref="PreviewText"/>.</summary>
    public static string PreviewTextFromBytes(byte[] bytes)
    {
        if (!LooksLikeText(bytes.AsSpan(0, Math.Min(bytes.Length, 512)))) return "";
        try
        {
            using var reader = new StreamReader(new MemoryStream(bytes));
            var sb = new System.Text.StringBuilder();
            string? line;
            int n = 0;
            while (n < PreviewMaxLines && (line = reader.ReadLine()) != null)
            {
                if (n > 0) sb.Append('\n');
                sb.Append(line.Length > 200 ? line[..200] : line);
                n++;
            }
            return sb.ToString();
        }
        catch { return ""; }
    }

    public static string FreeLabel(string path)
    {
        long b = FreeBytes(path);
        if (b < 0) return "";
        double v = b;
        string[] units = ["B", "KB", "MB", "GB", "TB", "PB"];
        int u = 0;
        while (v >= 1024 && u < units.Length - 1) { v /= 1024; u++; }
        return v.ToString(v < 10 ? "0.0" : "0", CultureInfo.InvariantCulture) + " " + units[u] + " free";
    }
}
