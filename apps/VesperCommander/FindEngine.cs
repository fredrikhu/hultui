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
        bool Recursive, bool MatchCase, bool Regex, bool Hidden);

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
            return name.Contains(p, o.MatchCase ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase);
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
            byte[] bytes = File.ReadAllBytes(path);
            int scan = Math.Min(bytes.Length, 8192);
            for (int i = 0; i < scan; i++) if (bytes[i] == 0) return 0;   // binary
            string text = Encoding.UTF8.GetString(bytes);
            var cmp = o.MatchCase ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
            int count = 0, idx = 0;
            while ((idx = text.IndexOf(o.ContainingText, idx, cmp)) >= 0) { count++; idx += o.ContainingText.Length; }
            return count;
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
                }
                onProgress(scanned, scanned + stack.Count);
            }
        }, ct);
    }
}
