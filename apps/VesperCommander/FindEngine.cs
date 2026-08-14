using System.Text;
using System.Text.RegularExpressions;

namespace VesperCommander;

/// <summary>Filename / content search under a folder. The matchers (<see cref="NameMatches"/>,
/// <see cref="ContentHits"/>) are pure and unit-testable; <see cref="Run"/> walks the tree on
/// a background thread and streams hits + progress back through callbacks (the caller marshals
/// to the UI). Archives are out of scope this batch.</summary>
public static class FindEngine
{
    /// <summary><paramref name="MaxDepth"/> counts folder levels *below* the search root:
    /// 1 searches the root and its immediate subfolders and stops, 2 goes one further, and
    /// 0 (the default) is unbounded. <paramref name="ExcludeFolders"/> is a semicolon-separated
    /// list of folder-name patterns (".git;.vs") — see <see cref="ExcludeMatchers"/>.</summary>
    public sealed record Options(
        string Pattern, string? ContainingText,
        bool Recursive, bool MatchCase, bool Regex, bool Hidden,
        bool Archives = false, bool SevenZip = false,
        int MaxDepth = 0, string? ExcludeFolders = null);

    /// <param name="Attributes">Carried through so a hit fed into a pane listing renders
    /// (and reports) exactly like the same file read from its folder — the walker already
    /// has them, and re-statting a fed-in result set would cost a syscall per row.</param>
    public sealed record Hit(
        string FullPath, string Name, bool IsDir, long Size, DateTime Modified,
        string ParentDir, int ContentHits, FileAttributes Attributes = 0);

    private const long MaxContentBytes = 4L * 1024 * 1024;

    /// <summary>True when a filename matches the pattern. An empty pattern matches all
    /// (a name-only filter or content-only search). Glob (<c>*</c> <c>?</c>) unless
    /// <see cref="Options.Regex"/>; otherwise a case-(in)sensitive substring. An invalid
    /// user regex matches nothing.</summary>
    public static bool NameMatches(string name, Options o)
    {
        var p = o.Pattern;
        if (string.IsNullOrEmpty(p)) return true;
        var rxOpts = o.MatchCase ? RegexOptions.None : RegexOptions.IgnoreCase;
        try
        {
            if (o.Regex) return Regex.IsMatch(name, p, rxOpts);
            if (p.Contains('*') || p.Contains('?'))
            {
                var glob = "^" + Regex.Escape(p).Replace("\\*", ".*").Replace("\\?", ".") + "$";
                return Regex.IsMatch(name, glob, rxOpts);
            }
            var cmp = o.MatchCase ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
            // A leading-dot query with no wildcard is an extension filter: ".zip" → *.zip
            // (EndsWith handles compound extensions like ".tar.gz" too).
            if (p.Length > 1 && p[0] == '.') return name.EndsWith(p, cmp);
            return name.Contains(p, cmp);
        }
        catch (ArgumentException) { return false; }   // malformed user regex
    }

    /// <summary>The folder-exclude list as one ready-to-match <see cref="Options"/> per pattern,
    /// so excludes match exactly the way the dialog's own pattern field does (glob, <c>?</c>,
    /// leading-dot extension, else substring). Blank entries — an empty list, stray or trailing
    /// semicolons, whitespace — are dropped; an empty pattern matches everything, and one of
    /// those in the exclude list would silently prune the whole tree.
    ///
    /// <para>Excludes are never treated as regex, even when <see cref="Options.Regex"/> is on:
    /// turning regex on for the *filename* must not change what a folder list like ".git;.vs"
    /// prunes. <see cref="Options.MatchCase"/> is honored.</para></summary>
    public static Options[] ExcludeMatchers(Options o)
    {
        if (string.IsNullOrWhiteSpace(o.ExcludeFolders)) return [];
        var parts = o.ExcludeFolders.Split(';',
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var matchers = new Options[parts.Length];
        for (int i = 0; i < parts.Length; i++) matchers[i] = o with { Pattern = parts[i], Regex = false };
        return matchers;
    }

    /// <summary>True when a folder name matches any exclude pattern. Only ever asked about
    /// folders — a file whose name happens to match an exclude is still a candidate hit.</summary>
    public static bool FolderExcluded(string name, Options[] excludes)
    {
        foreach (var e in excludes) if (NameMatches(name, e)) return true;
        return false;
    }

    /// <summary>True when a folder <paramref name="depth"/> levels below the search root may
    /// still be entered. See <see cref="Options.MaxDepth"/>; 0 is unbounded.</summary>
    private static bool WithinDepth(int depth, Options o) => o.MaxDepth <= 0 || depth <= o.MaxDepth;

    /// <summary>Occurrences of the "containing text" in a file — 0 when the filter is
    /// empty, the file is binary (a NUL in the first 8 KB), unreadable, or over 4 MB.</summary>
    public static int ContentHits(string path, Options o)
    {
        if (string.IsNullOrEmpty(o.ContainingText)) return 0;
        try
        {
            var info = new FileInfo(path);
            if (info.Length == 0 || info.Length > MaxContentBytes) return 0;
            return CountHits(File.ReadAllBytes(path), o);
        }
        catch { return 0; }
    }

    /// <summary>Occurrences of the containing text in a byte buffer, or 0 when it's binary
    /// (a NUL in the first 8 KB). Shared by the disk and archive-member paths.</summary>
    private static int CountHits(byte[] bytes, Options o)
    {
        if (string.IsNullOrEmpty(o.ContainingText)) return 0;
        int scan = Math.Min(bytes.Length, 8192);
        for (int i = 0; i < scan; i++) if (bytes[i] == 0) return 0;   // binary
        string text = Encoding.UTF8.GetString(bytes);
        var cmp = o.MatchCase ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        int count = 0, idx = 0;
        while ((idx = text.IndexOf(o.ContainingText, idx, cmp)) >= 0) { count++; idx += o.ContainingText.Length; }
        return count;
    }

    /// <summary>Content-grep an archive member by reading its (bounded) bytes via
    /// <see cref="ArchiveVfs.OpenEntry"/>. 0 when empty / oversized / binary / unreadable.</summary>
    private static int ContentHitsInArchive(FileEntry e, Options o)
    {
        if (string.IsNullOrEmpty(o.ContainingText) || e.Size <= 0 || e.Size > MaxContentBytes) return 0;
        if (ArchivePath.TryParse(e.FullPath) is not { } ap) return 0;
        try
        {
            using var s = ArchiveVfs.OpenEntry(ap.ArchiveFile, ap.Internal, null, o.SevenZip);
            if (s == null) return 0;
            using var ms = new MemoryStream();
            var buf = new byte[16 * 1024];
            int r;
            while ((r = s.Read(buf, 0, buf.Length)) > 0)
            {
                ms.Write(buf, 0, r);
                if (ms.Length > MaxContentBytes) return 0;
            }
            return CountHits(ms.ToArray(), o);
        }
        catch { return 0; }
    }

    /// <summary>Background streaming search under <paramref name="root"/>. Emits each hit and
    /// periodic (scanned, approxTotal) progress; <paramref name="onDenied"/> fires per folder
    /// that couldn't be read. Honors cancellation between entries. All callbacks run on the
    /// worker thread — the caller marshals to the UI.</summary>
    public static Task Run(string root, Options o,
        Action<Hit> onHit, Action<int, int> onProgress, Action<string> onDenied,
        CancellationToken ct)
    {
        return Task.Run(() =>
        {
            int scanned = 0;
            var excludes = ExcludeMatchers(o);
            // Each pending folder carries how many levels below the root it sits; the root is 0.
            var stack = new Stack<(string Dir, int Depth)>();
            stack.Push((root, 0));
            while (stack.Count > 0 && !ct.IsCancellationRequested)
            {
                var (dir, depth) = stack.Pop();
                scanned++;
                IEnumerable<FileSystemInfo> entries;
                try { entries = new DirectoryInfo(dir).EnumerateFileSystemInfos(); }
                catch { onDenied(dir); continue; }

                foreach (var info in entries)
                {
                    if (ct.IsCancellationRequested) break;
                    FileAttributes attr;
                    try { attr = info.Attributes; } catch { continue; }
                    bool hidden = (attr & (FileAttributes.Hidden | FileAttributes.System)) != 0;
                    if (hidden && !o.Hidden) continue;
                    bool isDir = (attr & FileAttributes.Directory) != 0;
                    // An excluded folder is pruned outright — never entered (which is the
                    // point: it's a speed feature) and never reported as a hit itself.
                    if (isDir && FolderExcluded(info.Name, excludes)) continue;
                    if (isDir && o.Recursive && WithinDepth(depth + 1, o)) stack.Push((info.FullName, depth + 1));

                    // Descend into an archive's members regardless of whether the archive
                    // file's own name matches the pattern. An archive's contents count as one
                    // level down from the folder holding it, so depth and excludes bound them
                    // exactly as they bound a subfolder.
                    if (o.Archives && !isDir && WithinDepth(depth + 1, o) && ArchiveVfs.IsArchiveFile(info.FullName))
                        WalkArchive(info.FullName, "", o, onHit, ct, depth + 1, excludes);

                    if (!NameMatches(info.Name, o)) continue;
                    int hits = 0;
                    if (!string.IsNullOrEmpty(o.ContainingText))
                    {
                        if (isDir) continue;                 // directories can't content-match
                        hits = ContentHits(info.FullName, o);
                        if (hits == 0) continue;
                    }
                    long size = 0; DateTime mtime = default;
                    try { mtime = info.LastWriteTime; if (!isDir && info is FileInfo fi) size = fi.Length; } catch { }
                    onHit(new Hit(info.FullName, info.Name, isDir, size, mtime,
                        Path.GetDirectoryName(info.FullName) ?? "", hits, attr));
                    // (The archive descent above runs before the name check so it always fires
                    // once — no second walk here, which would double-count a name-matched archive.)
                }
                onProgress(scanned, scanned + stack.Count);
            }
        }, ct);
    }

    /// <summary>Match an archive's members by name and, when a "containing text" is set, by
    /// extracted content. Encrypted / unreadable archives are skipped. <paramref name="depth"/>
    /// is how far below the search root this listing sits, continuing the on-disk count so an
    /// archive's folders are bounded like real ones.</summary>
    private static void WalkArchive(string archiveFile, string internalPath, Options o,
        Action<Hit> onHit, CancellationToken ct, int depth, Options[] excludes)
    {
        var listing = ArchiveVfs.ListEntries(archiveFile, internalPath, null, o.SevenZip);
        if (listing.Status is not (ArchiveStatus.Ok or ArchiveStatus.Empty)) return;
        foreach (var e in listing.Entries)
        {
            if (e.IsParent || ct.IsCancellationRequested) continue;
            if (e.IsDir)
            {
                // Member folders obey the same exclude list and depth bound as folders on disk.
                if (FolderExcluded(e.Name, excludes) || !WithinDepth(depth + 1, o)) continue;
                if (ArchivePath.TryParse(e.FullPath) is { } ap)
                    WalkArchive(ap.ArchiveFile, ap.Internal, o, onHit, ct, depth + 1, excludes);
                continue;
            }
            if (!NameMatches(e.Name, o)) continue;
            int hits = 0;
            if (!string.IsNullOrEmpty(o.ContainingText))
            {
                hits = ContentHitsInArchive(e, o);
                if (hits == 0) continue;
            }
            onHit(new Hit(e.FullPath, e.Name, IsDir: false, e.Size, e.Modified,
                Path.GetDirectoryName(e.FullPath) ?? "", hits, e.Attributes));
        }
    }
}
