namespace VesperCommander;

/// <summary>
/// Turns a program name into a full path before <c>Process.Start</c> ever sees it.
///
/// <para><b>Why.</b> Handing a bare name like <c>7z.exe</c> to <c>Process.Start</c> leaves the
/// lookup to Windows, and <c>CreateProcess</c> searches the calling process's <i>current
/// directory</i> ahead of <c>PATH</c>. Vesper's current directory is whatever folder it was
/// launched from, so a <c>7z.exe</c> sitting in that folder would win over the installed
/// 7-Zip — and the 7-Zip probe runs unprompted at startup, which turns "browse a folder
/// someone sent you" into arbitrary code execution. <c>ShellExecute</c> searches the current
/// directory too, so the terminal and editor launches have the same exposure.</para>
///
/// <para>The fix is to do the lookup ourselves and never pass a bare name: only rooted
/// <c>PATH</c> entries are searched, so neither the current directory nor a relative
/// <c>PATH</c> entry can supply the binary. A caller that names an explicit path still gets
/// it — that is a deliberate choice in a settings template, not an ambient one.</para>
/// </summary>
public static class ProgramPath
{
    /// <summary>The full path of <paramref name="nameOrPath"/>, or null when it can't be
    /// found. A bare name is looked up in the rooted entries of <c>PATH</c> (trying each
    /// <c>PATHEXT</c> suffix on Windows); anything that looks like a path must be <i>fully
    /// qualified</i> to be accepted.</summary>
    public static string? Resolve(string? nameOrPath)
    {
        if (string.IsNullOrWhiteSpace(nameOrPath)) return null;
        var name = nameOrPath.Trim().Trim('"');
        if (name.Length == 0) return null;

        if (Path.IsPathRooted(name) || name.IndexOfAny(Separators) >= 0)
        {
            // Fully qualified only. A relative path — "./7z.exe", or the drive-relative
            // "C:7z.exe" that IsPathRooted happily accepts — resolves against a current
            // directory that whoever wrote the setting had no say in, which is the same
            // ambient lookup this class exists to remove. Nothing in the settings UI asks
            // for one.
            if (!Path.IsPathFullyQualified(name)) return null;
            try { return FirstExisting(Path.GetFullPath(name)); }
            catch { return null; }   // malformed path — not something to launch
        }

        foreach (var dir in SearchDirectories())
        {
            string candidate;
            try { candidate = Path.Combine(dir, name); }
            catch { continue; }      // an unusable PATH entry
            if (FirstExisting(candidate) is { } hit) return hit;
        }
        return null;
    }

    private static readonly char[] Separators =
        [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar];

    /// <summary>The <c>PATH</c> entries worth searching. Relative entries — including a bare
    /// "." — are dropped: they resolve against the current directory, which is the thing this
    /// class exists to keep out of the lookup.</summary>
    private static IEnumerable<string> SearchDirectories()
    {
        var path = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (var raw in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var dir = raw.Trim().Trim('"');
            if (dir.Length == 0 || !Path.IsPathRooted(dir)) continue;
            yield return dir;
        }
    }

    /// <summary>The candidate itself, or — on Windows, for a name with no extension — the
    /// first <c>PATHEXT</c> variant that exists. This is what lets a template say <c>code</c>
    /// and still find <c>code.cmd</c>.</summary>
    private static string? FirstExisting(string candidate)
    {
        if (!OperatingSystem.IsWindows() || Path.HasExtension(candidate))
            return File.Exists(candidate) ? candidate : null;

        foreach (var ext in PathExtensions())
        {
            var withExt = candidate + ext;
            if (File.Exists(withExt)) return withExt;
        }
        return File.Exists(candidate) ? candidate : null;
    }

    private static IEnumerable<string> PathExtensions()
    {
        var pathext = Environment.GetEnvironmentVariable("PATHEXT");
        if (string.IsNullOrWhiteSpace(pathext)) pathext = ".COM;.EXE;.BAT;.CMD";
        foreach (var raw in pathext.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var ext = raw.Trim();
            if (ext.Length > 1 && ext[0] == '.') yield return ext;
        }
    }
}
