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

/// <summary>The state a pane's listing is in — drives the empty/offline/denied views.</summary>
public enum PaneState { Ok, Empty, Offline, Denied }

/// <summary>A directory listing plus the state it resolved to.</summary>
public sealed record DirListing(PaneState State, IReadOnlyList<FileEntry> Entries)
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
    /// sizes, so the UI can dim them.</summary>
    public static IReadOnlyList<DriveEntry> Drives()
    {
        var list = new List<DriveEntry>();
        foreach (var d in DriveInfo.GetDrives())
        {
            try
            {
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
    public static DirListing List(string path, bool showHidden = false,
        SortMode sort = SortMode.Name, bool dirsFirst = true, bool descending = false,
        bool sevenZip = false, string? password = null)
    {
        // A path that descends into an archive lists through the VFS instead of disk.
        if (ArchivePath.TryParse(path) is { } ap)
            return ListArchive(ap.ArchiveFile, ap.Internal, sort, dirsFirst, descending, sevenZip, password);

        var dirs = new List<FileEntry>();
        var files = new List<FileEntry>();

        try
        {
            // Unready volume (unmounted / disconnected) ⇒ offline.
            var root = Path.GetPathRoot(path);
            if (!string.IsNullOrEmpty(root))
            {
                try { if (!new DriveInfo(root).IsReady) return new(PaneState.Offline, []); }
                catch { /* UNC / non-drive root — fall through to enumeration */ }
            }

            var dir = new DirectoryInfo(path);
            foreach (var info in dir.EnumerateFileSystemInfos())
            {
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
        var parent = Directory.GetParent(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        if (parent != null)
            result.Add(new FileEntry("..", parent.FullName, IsDir: true, 0, DateTime.Now, IsParent: true));

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

    /// <summary>List one level inside an archive through the VFS, sorted like a folder.
    /// Non-Ok statuses (empty / corrupt / needs-7-Zip / encrypted) map onto the pane state.</summary>
    private static DirListing ListArchive(string archiveFile, string internalPath,
        SortMode sort, bool dirsFirst, bool descending, bool sevenZip, string? password)
    {
        var al = ArchiveVfs.ListEntries(archiveFile, internalPath, password, sevenZip);
        if (al.Status is not (ArchiveStatus.Ok or ArchiveStatus.Empty))
            return new(PaneState.Denied, al.Entries);   // distinct archive-error views land in a later phase
        if (al.Status == ArchiveStatus.Empty)
            return new(PaneState.Empty, al.Entries);

        var cmp = Comparator(sort, descending);
        var parent = al.Entries.Where(e => e.IsParent).ToList();
        var dirs = al.Entries.Where(e => e.IsDir && !e.IsParent).ToList();
        var files = al.Entries.Where(e => !e.IsDir).ToList();
        var result = new List<FileEntry>(al.Entries.Count);
        result.AddRange(parent);
        if (dirsFirst) { dirs.Sort(cmp); files.Sort(cmp); result.AddRange(dirs); result.AddRange(files); }
        else { var all = dirs.Concat(files).ToList(); all.Sort(cmp); result.AddRange(all); }
        return new(PaneState.Ok, result);
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
            for (int i = 0; i < read; i++) if (probe[i] == 0) return "";   // NUL ⇒ binary
            stream.Position = 0;

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
