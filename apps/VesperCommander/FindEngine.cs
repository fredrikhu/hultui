using System.Text;
using System.Text.RegularExpressions;

namespace VesperCommander;

/// <summary>Filename / content search under a folder. The matchers (<see cref="NameMatches"/>,
/// <see cref="ContentHits"/>) are pure and unit-testable; <see cref="Run"/> walks the tree on
/// a background thread and streams hits + progress back through callbacks (the caller marshals
/// to the UI). Archives are out of scope this batch.</summary>
public static class FindEngine
{
    public sealed record Options(
        string Pattern, string? ContainingText,
        bool Recursive, bool MatchCase, bool Regex, bool Hidden,
        bool Archives = false, bool SevenZip = false);

    public sealed record Hit(
        string FullPath, string Name, bool IsDir, long Size, DateTime Modified,
        string ParentDir, int ContentHits);

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
            var stack = new Stack<string>();
            stack.Push(root);
            while (stack.Count > 0 && !ct.IsCancellationRequested)
            {
                string dir = stack.Pop();
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
                    if (isDir && o.Recursive) stack.Push(info.FullName);

                    // Descend into an archive's members regardless of whether the archive
                    // file's own name matches the pattern.
                    if (o.Archives && !isDir && ArchiveVfs.IsArchiveFile(info.FullName))
                        WalkArchive(info.FullName, "", o, onHit, ct);

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
                        Path.GetDirectoryName(info.FullName) ?? "", hits));
                    // (The archive descent above runs before the name check so it always fires
                    // once — no second walk here, which would double-count a name-matched archive.)
                }
                onProgress(scanned, scanned + stack.Count);
            }
        }, ct);
    }

    /// <summary>Match an archive's members by name and, when a "containing text" is set, by
    /// extracted content. Encrypted / unreadable archives are skipped.</summary>
    private static void WalkArchive(string archiveFile, string internalPath, Options o,
        Action<Hit> onHit, CancellationToken ct)
    {
        var listing = ArchiveVfs.ListEntries(archiveFile, internalPath, null, o.SevenZip);
        if (listing.Status is not (ArchiveStatus.Ok or ArchiveStatus.Empty)) return;
        foreach (var e in listing.Entries)
        {
            if (e.IsParent || ct.IsCancellationRequested) continue;
            if (e.IsDir)
            {
                if (ArchivePath.TryParse(e.FullPath) is { } ap) WalkArchive(ap.ArchiveFile, ap.Internal, o, onHit, ct);
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
                Path.GetDirectoryName(e.FullPath) ?? "", hits));
        }
    }
}
