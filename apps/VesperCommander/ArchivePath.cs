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
