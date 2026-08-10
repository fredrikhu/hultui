namespace VesperCommander;

/// <summary>Parses "archive-descended" paths — a real path that continues *inside* an
/// archive file, e.g. <c>C:\dev\assets.zip\textures</c> → (<c>C:\dev\assets.zip</c>,
/// <c>"textures"</c>). Real paths return null and fall straight through to the normal
/// filesystem, so nothing off the archive path changes behavior.</summary>
public static class ArchivePath
{
    private static readonly string[] Exts =
        [".zip", ".7z", ".rar", ".gz", ".tar", ".bz2", ".xz", ".tgz"];

    public static bool IsArchiveFile(string path) =>
        Exts.Contains(Path.GetExtension(path).ToLowerInvariant());

    /// <summary>Split at the first path segment that is an archive file on disk; null if
    /// the path doesn't descend into one. The extension check gates the disk hit, so a
    /// normal path never touches the filesystem here.</summary>
    public static (string ArchiveFile, string Internal)? TryParse(string path)
    {
        if (string.IsNullOrEmpty(path)) return null;
        char sep = Path.DirectorySeparatorChar;
        string norm = path.Replace(Path.AltDirectorySeparatorChar, sep);
        int idx = 0;
        while (true)
        {
            int next = norm.IndexOf(sep, idx);
            string candidate = next < 0 ? norm : norm[..next];
            if (candidate.Length > 0 && IsArchiveFile(candidate) && File.Exists(candidate))
                return (candidate, next < 0 ? "" : norm[(next + 1)..].TrimEnd(sep));
            if (next < 0) return null;
            idx = next + 1;
        }
    }

    public static bool InArchive(string path) => TryParse(path) != null;

    /// <summary>Join an archive file with an internal path (backslash-separated).</summary>
    public static string Combine(string archiveFile, string inner) =>
        inner.Length == 0 ? archiveFile : archiveFile + Path.DirectorySeparatorChar + inner;

    /// <summary>
    /// An archive member's stored path, cleaned into a relative path we are willing to use —
    /// or null when it is one we are not.
    ///
    /// <para>Nothing constrains what a zip puts in its name field. A member called
    /// <c>../../evil.txt</c> is a valid archive and a well-known attack ("zip slip"): listed
    /// naively it becomes a folder called "..", and extracting that folder walks the
    /// destination path back out of itself and writes wherever the archive chose. The BCL's
    /// <c>ZipFile.ExtractToDirectory</c> refuses such an entry; we don't use it — we enumerate
    /// members and join paths ourselves — so the check has to live here.</para>
    ///
    /// <para>Rejected: any <c>..</c> segment, and anything rooted — a leading slash, a UNC
    /// <c>\\server\share</c>, or a drive prefix, including the drive-<i>relative</i>
    /// <c>C:evil.txt</c> that resolves against the process's current directory on that drive.
    /// Accepted and normalized away: <c>.</c> segments and doubled slashes, which ordinary tar
    /// archives really do contain. A trailing slash is kept, since it is how a zip marks a
    /// directory entry.</para>
    /// </summary>
    public static string? SafeMemberPath(string? stored)
    {
        if (string.IsNullOrWhiteSpace(stored)) return null;
        var s = stored.Replace('\\', '/');
        if (s[0] == '/') return null;                       // "/etc/x", and "//server/share/x"
        if (s.Length >= 2 && s[1] == ':') return null;      // "C:/x" and the drive-relative "C:x"

        bool directory = s[^1] == '/';
        var kept = new List<string>();
        foreach (var segment in s.Split('/'))
        {
            if (segment.Length == 0 || segment == ".") continue;
            if (segment == "..") return null;
            kept.Add(segment);
        }
        if (kept.Count == 0) return null;
        return string.Join('/', kept) + (directory ? "/" : "");
    }

    /// <summary>The parent of an archive location: the containing real folder at the
    /// archive root, else the enclosing internal folder.</summary>
    public static string ParentOf(string archiveFile, string internalPath)
    {
        if (internalPath.Length == 0)
            return Path.GetDirectoryName(archiveFile) ?? archiveFile;
        int slash = internalPath.LastIndexOf(Path.DirectorySeparatorChar);
        return slash < 0 ? archiveFile : Combine(archiveFile, internalPath[..slash]);
    }
}
