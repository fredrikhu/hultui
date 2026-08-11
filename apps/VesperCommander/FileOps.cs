using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace VesperCommander;

/// <summary>
/// The actual filesystem mutations behind copy / move / delete. All methods run
/// on the UI thread for now (operations in the test flows are small); a later
/// pass moves large transfers off-thread with progress. Every method returns the
/// error message on failure, or null on success, so the UI can surface it.
/// </summary>
public static class FileOps
{
    /// <summary>True when <paramref name="path"/> already exists as a file or folder.</summary>
    public static bool Exists(string path) => File.Exists(path) || Directory.Exists(path);

    /// <summary>
    /// The full path of <paramref name="name"/> inside <paramref name="destDir"/>, or null
    /// when joining the two would land outside it.
    ///
    /// <para>Every write in this file goes through here. A name is usually just a file name,
    /// but not always — the sync commit deliberately passes a path relative to the
    /// destination, so a plain "no separators" rule would break it. What actually matters is
    /// where the join <i>resolves</i>, which is what this checks.</para>
    ///
    /// <para>The archive reader already refuses a traversing member
    /// (<see cref="ArchivePath.SafeMemberPath"/>); this is the second line, so that a future
    /// caller inventing a new way to reach the transfer body cannot reopen the hole.</para>
    /// </summary>
    public static string? ContainedPath(string destDir, string name)
    {
        try
        {
            var root = Path.GetFullPath(destDir);
            // Combine, not concatenate: a rooted name replaces the root outright, and
            // GetFullPath then puts it somewhere the prefix test will reject.
            var full = Path.GetFullPath(Path.Combine(root, name));
            var prefix = root.EndsWith(Path.DirectorySeparatorChar) ? root : root + Path.DirectorySeparatorChar;
            var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            return full.StartsWith(prefix, comparison) ? full : null;
        }
        catch { return null; }   // an unusable name — refuse it rather than guess
    }

    /// <summary>The message shown when a write is refused for leaving its destination. Short
    /// on purpose: it lands in a per-item error column.</summary>
    private const string EscapesDest = "refused — it would be written outside the destination";

    // ── Access-denied tagging ────────────────────────────────────────────
    // Ops report failure as a message string, and callers need to tell "you lack the
    // rights" (offer elevation) from "the disk is full" (don't). Rather than widen every
    // signature — the streamed copy recurses and returns through lambdas — a denied error
    // is returned with this prefix. Callers ask IsDenied / strip it with Clean. The tag is
    // set from the exception type + HRESULT, never from message text, so it survives
    // localized OS strings.
    private const string DeniedTag = "[denied] ";

    /// <summary>True when <paramref name="error"/> came from a permission failure — the
    /// signal to offer "retry as administrator".</summary>
    public static bool IsDenied(string? error) => error != null && error.StartsWith(DeniedTag, StringComparison.Ordinal);

    /// <summary>An error string with the internal denied tag stripped, for display.</summary>
    public static string? Clean(string? error) => IsDenied(error) ? error![DeniedTag.Length..] : error;

    /// <summary>Turn an exception into an error string, tagged when it is a permission
    /// failure (<c>UnauthorizedAccessException</c>, or an IO error carrying Win32
    /// ERROR_ACCESS_DENIED / ERROR_PRIVILEGE_NOT_HELD, or EACCES/EPERM on Unix).</summary>
    private static string Fail(Exception ex) => IsDeniedException(ex) ? DeniedTag + ex.Message : ex.Message;

    private static bool IsDeniedException(Exception ex)
    {
        if (ex is UnauthorizedAccessException) return true;
        // Windows wraps a Win32 code as HRESULT 0x8007xxxx: 5 = ACCESS_DENIED,
        // 1314 = PRIVILEGE_NOT_HELD (e.g. creating a symlink without the right).
        const int facilityWin32 = unchecked((int)0x80070000);
        if ((ex.HResult & unchecked((int)0xFFFF0000)) == facilityWin32)
            return (ex.HResult & 0xFFFF) is 5 or 1314;
        // On Unix, IOException carries the raw errno: 1 = EPERM, 13 = EACCES.
        return ex is IOException && ex.HResult is 1 or 13;
    }

    /// <summary>Build a <see cref="FileEntry"/> for an arbitrary path — used by clipboard
    /// paste, whose sources aren't part of any pane listing.</summary>
    public static FileEntry EntryFromPath(string path)
    {
        bool isDir = Directory.Exists(path);
        long size = 0;
        DateTime mtime = DateTime.Now;
        try
        {
            if (isDir) mtime = new DirectoryInfo(path).LastWriteTime;
            else { var fi = new FileInfo(path); size = fi.Length; mtime = fi.LastWriteTime; }
        }
        catch { /* best-effort metadata */ }
        string name = Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        return new FileEntry(name, path, isDir, size, mtime);
    }

    /// <summary>Open a file with the OS default handler (Enter on a file). Windows
    /// shell-executes; macOS uses <c>open</c>, Linux <c>xdg-open</c>.</summary>
    public static void OpenInShell(string path)
    {
        try
        {
            if (OperatingSystem.IsWindows())
                Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
            else if (ProgramPath.Resolve(OperatingSystem.IsMacOS() ? "open" : "xdg-open") is { } opener)
                Process.Start(opener, [path]);
        }
        catch { /* no handler / launch failed — nothing sensible to do */ }
    }

    /// <summary>Launch an external editor for <paramref name="filePath"/> from a command
    /// template (F4). The template is tokenized (double quotes group a token), then each
    /// token has its placeholders substituted: <c>{file}</c>, <c>{line}</c>, <c>{dir}</c>.
    /// The first token is the program; the rest are passed as separate arguments (so the
    /// launcher, not the template author, handles quoting paths with spaces). A template
    /// with no <c>{file}</c> gets the path appended as a final argument. A blank template
    /// or a launch failure falls back to the OS default handler.</summary>
    public static void LaunchEditor(string template, string filePath, int line = 1)
    {
        var inv = BuildEditorInvocation(template, filePath, line);
        if (inv is not (var exe, var args))   // blank/empty template ⇒ OS default handler
        {
            OpenInShell(filePath);
            return;
        }
        // Resolved to a full path first: ShellExecute searches the current directory, so a
        // bare "code" would run a code.exe left in the folder Vesper was launched from.
        // ProgramPath still finds launchers like VS Code's code.cmd, via PATHEXT.
        if (ProgramPath.Resolve(exe) is not { } program) { OpenInShell(filePath); return; }
        try { Process.Start(ShellLaunch(program, args)); }
        catch { OpenInShell(filePath); }
    }

    /// <summary>A <see cref="ProcessStartInfo"/> for launching an installed program with
    /// arguments. UseShellExecute so a batch launcher runs at all; the ArgumentList quotes
    /// each argument, so a token like <c>{file}</c> with spaces stays intact.
    /// <para>Batch launchers are asked to start hidden. A <c>.cmd</c> is run by cmd.exe,
    /// which brings a console window with it — VS Code's <c>code</c> is <c>code.cmd</c>, so
    /// opening a file in it put a terminal on screen. The editor the batch file goes on to
    /// start is a separate process and shows normally.</para></summary>
    internal static ProcessStartInfo ShellLaunch(string program, IEnumerable<string> args, string? workingDir = null)
    {
        var psi = new ProcessStartInfo(program) { UseShellExecute = true };
        if (workingDir != null) psi.WorkingDirectory = workingDir;
        // Only batch files. The hidden style reaches a GUI program as its initial show
        // state, and one that honours it would start invisible.
        if (IsBatchLauncher(program)) psi.WindowStyle = ProcessWindowStyle.Hidden;
        foreach (var a in args) psi.ArgumentList.Add(a);
        return psi;
    }

    /// <summary>True for the file types Windows runs through a command interpreter, which
    /// is what puts a console window on screen.</summary>
    internal static bool IsBatchLauncher(string program) =>
        program.EndsWith(".cmd", StringComparison.OrdinalIgnoreCase)
        || program.EndsWith(".bat", StringComparison.OrdinalIgnoreCase);

    /// <summary>Run a typed command in a terminal at <paramref name="dir"/> (the command
    /// line). Windows opens a cmd window that stays open (/k); other OSes run via sh.</summary>
    public static void RunInTerminal(string command, string dir)
    {
        try
        {
            if (OperatingSystem.IsWindows())
            {
                if (Comspec() is not { } shell) return;
                Process.Start(new ProcessStartInfo(shell, "/k " + command)
                { UseShellExecute = true, WorkingDirectory = dir });
            }
            else
                Process.Start(new ProcessStartInfo("/bin/sh", "-c \"" + command.Replace("\"", "\\\"") + "; exec $SHELL\"")
                { UseShellExecute = false, WorkingDirectory = dir });
        }
        catch { /* best-effort */ }
    }

    /// <summary>The full path of the Windows command interpreter — <c>COMSPEC</c> when it
    /// resolves, else <c>cmd.exe</c> found on PATH. Never the bare name: ShellExecute would
    /// search the current directory for it first (see <see cref="ProgramPath"/>).</summary>
    private static string? Comspec() =>
        ProgramPath.Resolve(Environment.GetEnvironmentVariable("COMSPEC")) ?? ProgramPath.Resolve("cmd.exe");

    /// <summary>Open a terminal at <paramref name="dir"/> using the configured template
    /// (e.g. <c>wt.exe -d {dir}</c>); falls back to the OS default shell.</summary>
    public static void OpenTerminal(string template, string dir)
    {
        if (!string.IsNullOrWhiteSpace(template))
        {
            var tokens = Tokenize(template);
            if (tokens.Count > 0 && ProgramPath.Resolve(tokens[0].Replace("{dir}", dir)) is { } program)
            {
                try
                {
                    var psi = new ProcessStartInfo(program) { UseShellExecute = true, WorkingDirectory = dir };
                    for (int i = 1; i < tokens.Count; i++) psi.ArgumentList.Add(tokens[i].Replace("{dir}", dir));
                    Process.Start(psi);
                    return;
                }
                catch { /* fall through to the default shell */ }
            }
        }
        try
        {
            if (OperatingSystem.IsWindows())
            {
                if (Comspec() is { } shell)
                    Process.Start(new ProcessStartInfo(shell) { UseShellExecute = true, WorkingDirectory = dir });
            }
            else if (OperatingSystem.IsMacOS())
            {
                if (ProgramPath.Resolve("open") is { } open)
                    Process.Start(new ProcessStartInfo(open, ["-a", "Terminal", dir]));
            }
            else if (ProgramPath.Resolve("x-terminal-emulator") is { } term)
                Process.Start(new ProcessStartInfo(term) { UseShellExecute = false, WorkingDirectory = dir });
        }
        catch { /* best-effort */ }
    }

    /// <summary>Open the configured git client at <paramref name="dir"/> (a command
    /// template, <c>{dir}</c> substituted, launched with the folder as its working
    /// directory). No-op when the template is blank. Best-effort.</summary>
    public static void OpenGitClient(string template, string dir)
    {
        if (string.IsNullOrWhiteSpace(template)) return;
        var tokens = Tokenize(template);
        if (tokens.Count == 0) return;
        if (ProgramPath.Resolve(tokens[0].Replace("{dir}", dir)) is not { } program) return;
        try
        {
            Process.Start(ShellLaunch(program,
                tokens.Skip(1).Select(t => t.Replace("{dir}", dir)), workingDir: dir));
        }
        catch { /* best-effort */ }
    }

    /// <summary>Launch an external diff (e.g. <c>code --diff left right</c>) for the
    /// VS Code DiffTool option. Best-effort; failures are swallowed.</summary>
    public static void LaunchDiff(string exe, string left, string right)
    {
        if (ProgramPath.Resolve(exe) is not { } program) return;
        try { Process.Start(ShellLaunch(program, ["--diff", left, right])); }
        catch { /* best-effort */ }
    }

    /// <summary>Resolve a command template into a program + argument list, without
    /// launching (pure, so it's unit-testable). Tokenizes the template (double quotes
    /// group a token), substitutes <c>{file}</c> / <c>{line}</c> / <c>{dir}</c> in each
    /// token, and appends the file path as a final argument when the template has no
    /// <c>{file}</c>. Returns null for a blank template (caller falls back to the shell).</summary>
    public static (string Exe, IReadOnlyList<string> Args)? BuildEditorInvocation(
        string template, string filePath, int line = 1)
    {
        if (string.IsNullOrWhiteSpace(template)) return null;
        var tokens = Tokenize(template);
        if (tokens.Count == 0) return null;

        string dir = Path.GetDirectoryName(filePath) ?? "";
        string Sub(string t) => t
            .Replace("{file}", filePath)
            .Replace("{line}", line.ToString(System.Globalization.CultureInfo.InvariantCulture))
            .Replace("{dir}", dir);

        var args = new List<string>();
        for (int i = 1; i < tokens.Count; i++) args.Add(Sub(tokens[i]));
        if (!template.Contains("{file}")) args.Add(filePath);
        return (Sub(tokens[0]), args);
    }

    /// <summary>Split a command line into tokens, treating a double-quoted run as one
    /// token (the quotes are stripped). Whitespace outside quotes separates tokens.</summary>
    private static List<string> Tokenize(string s)
    {
        var tokens = new List<string>();
        var sb = new StringBuilder();
        bool inQuotes = false;
        foreach (char c in s)
        {
            if (c == '"') { inQuotes = !inQuotes; continue; }
            if (char.IsWhiteSpace(c) && !inQuotes)
            {
                if (sb.Length > 0) { tokens.Add(sb.ToString()); sb.Clear(); }
            }
            else sb.Append(c);
        }
        if (sb.Length > 0) tokens.Add(sb.ToString());
        return tokens;
    }

    /// <summary>Copy an entry into <paramref name="destDir"/> (recursively for folders).</summary>
    public static string? Copy(FileEntry e, string destDir, bool overwrite)
    {
        if (ContainedPath(destDir, e.Name) is not { } dest) return EscapesDest;
        try
        {
            if (e.IsDir) CopyDirectory(e.FullPath, dest, overwrite);
            else File.Copy(e.FullPath, dest, overwrite);
            return null;
        }
        catch (Exception ex) { return Fail(ex); }
    }

    /// <summary>True when two paths live on the same volume — a same-volume move is an
    /// instant rename, cross-volume must copy then delete.</summary>
    public static bool SameVolume(string a, string b)
    {
        var ra = Path.GetPathRoot(Path.GetFullPath(a));
        var rb = Path.GetPathRoot(Path.GetFullPath(b));
        return string.Equals(ra, rb, OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
    }

    /// <summary>Move an entry into <paramref name="destDir"/>. When
    /// <paramref name="overwrite"/> and the target exists, it is recycled first.</summary>
    public static string? Move(FileEntry e, string destDir, bool overwrite)
    {
        if (ContainedPath(destDir, e.Name) is not { } dest) return EscapesDest;
        try
        {
            if (Exists(dest))
            {
                if (!overwrite) return "target exists";
                Recycle(dest, Directory.Exists(dest));
            }
            if (e.IsDir) Directory.Move(e.FullPath, dest);
            else File.Move(e.FullPath, dest);
            return null;
        }
        catch (Exception ex) { return Fail(ex); }
    }

    /// <summary>Rename an entry in place (Shift+F6). Allows a case-only rename on
    /// Windows (where the target "exists" case-insensitively); any other collision
    /// is refused. Returns null on success, else the error message.</summary>
    public static string? Rename(FileEntry e, string newName)
    {
        try
        {
            var dir = Path.GetDirectoryName(e.FullPath) ?? "";
            var dest = Path.Combine(dir, newName);
            if (string.Equals(e.FullPath, dest, StringComparison.Ordinal)) return null;   // no-op
            bool caseOnly = string.Equals(e.FullPath, dest, StringComparison.OrdinalIgnoreCase);
            if (!caseOnly && Exists(dest)) return "A file or folder with that name already exists";
            if (e.IsDir) Directory.Move(e.FullPath, dest);
            else File.Move(e.FullPath, dest);
            return null;
        }
        catch (Exception ex) { return Fail(ex); }
    }

    /// <summary>Send a path to the Recycle Bin (Windows); permanent delete elsewhere.</summary>
    public static string? Recycle(string path, bool isDir)
    {
        try
        {
            if (OperatingSystem.IsWindows())
            {
                int rc = ShellRecycle(path);
                // SHFileOperation reports Win32 codes directly; 5 = ACCESS_DENIED, and
                // 0x78 (DE_ACCESSDENIEDSRC) is its own "source access denied".
                if (rc != 0)
                    return (rc is 5 or 0x78 ? DeniedTag : "") + $"delete failed (0x{rc:X})";
            }
            else
            {
                if (isDir) Directory.Delete(path, recursive: true);
                else File.Delete(path);
            }
            return null;
        }
        catch (Exception ex) { return Fail(ex); }
    }

    /// <summary>True when the source file is newer than the (existing) target — the
    /// "Keep newer" conflict rule.</summary>
    public static bool SourceNewer(string src, string dest)
    {
        try { return File.GetLastWriteTimeUtc(src) > File.GetLastWriteTimeUtc(dest); }
        catch { return false; }
    }

    /// <summary>A "name (2).ext" variant that doesn't collide in <paramref name="destDir"/>
    /// — the "Auto-rename" conflict rule.</summary>
    public static string UniqueName(string destDir, string name)
    {
        if (!Exists(Path.Combine(destDir, name))) return name;
        var stem = Path.GetFileNameWithoutExtension(name);
        var ext = Path.GetExtension(name);
        for (int n = 2; ; n++)
        {
            var candidate = $"{stem} ({n}){ext}";
            if (!Exists(Path.Combine(destDir, candidate))) return candidate;
        }
    }

    /// <summary>Permanently delete a path (used when "Delete to Recycle Bin" is off).</summary>
    public static string? DeletePermanent(string path, bool isDir)
    {
        try
        {
            if (isDir) Directory.Delete(path, recursive: true);
            else File.Delete(path);
            return null;
        }
        catch (Exception ex) { return Fail(ex); }
    }

    private static void CopyDirectory(string src, string dest, bool overwrite)
    {
        Directory.CreateDirectory(dest);
        foreach (var file in Directory.EnumerateFiles(src))
            File.Copy(file, Path.Combine(dest, Path.GetFileName(file)), overwrite);
        foreach (var dir in Directory.EnumerateDirectories(src))
            CopyDirectory(dir, Path.Combine(dest, Path.GetFileName(dir)), overwrite);
    }

    // ── Streamed copy with progress + cancel (for the copy-progress overlay) ─
    private const int ChunkSize = 1 << 18;   // 256 KB

    /// <summary>Copy an entry into <paramref name="destDir"/>, reporting bytes to
    /// <paramref name="onBytes"/> and aborting when <paramref name="cancelled"/>
    /// returns true. Runs on a background thread. Returns an error message, the
    /// literal "cancelled", or null on success.</summary>
    public static string? CopyStreamed(FileEntry e, string destDir, bool overwrite,
        Action<long> onBytes, Func<bool> cancelled, bool sevenZip = false, string? password = null)
    {
        if (ContainedPath(destDir, e.Name) is not { } dest) return EscapesDest;
        return e.IsDir
            ? CopyDirStreamed(e.FullPath, dest, overwrite, onBytes, cancelled, sevenZip, password)
            : CopyFileStreamed(e.FullPath, dest, overwrite, onBytes, cancelled, sevenZip, password);
    }

    private static string? CopyFileStreamed(string src, string dest, bool overwrite,
        Action<long> onBytes, Func<bool> cancelled, bool sevenZip = false, string? password = null)
    {
        try
        {
            // Ensure the target folder exists — lets callers copy to a nested relative
            // target (the sync commit sets an entry's Name to its relative path).
            var destDir = Path.GetDirectoryName(dest);
            if (!string.IsNullOrEmpty(destDir)) Directory.CreateDirectory(destDir);

            // An archive member reads from the VFS; a real file from disk.
            var ap = ArchivePath.TryParse(src);
            Stream s = ap is { } a
                ? ArchiveVfs.OpenEntry(a.ArchiveFile, a.Internal, password, sevenZip) ?? throw new IOException("archive member unreadable")
                : new FileStream(src, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, ChunkSize);
            using (s)
            using (var d = new FileStream(dest, overwrite ? FileMode.Create : FileMode.CreateNew,
                       FileAccess.Write, FileShare.None, ChunkSize))
            {
                var buf = new byte[ChunkSize];
                int n;
                while ((n = s.Read(buf, 0, buf.Length)) > 0)
                {
                    if (cancelled()) { d.Dispose(); TryDelete(dest); return "cancelled"; }
                    d.Write(buf, 0, n);
                    onBytes(n);
                }
            }
            if (ap is null) { try { File.SetLastWriteTimeUtc(dest, File.GetLastWriteTimeUtc(src)); } catch { } }
            return null;
        }
        catch (Exception ex) { return Fail(ex); }
    }

    private static string? CopyDirStreamed(string src, string dest, bool overwrite,
        Action<long> onBytes, Func<bool> cancelled, bool sevenZip = false, string? password = null)
    {
        // A folder inside an archive extracts through the VFS.
        if (ArchivePath.TryParse(src) is { } ap)
            return ExtractArchiveDir(ap.ArchiveFile, ap.Internal, dest, overwrite, sevenZip, password, onBytes, cancelled);
        try
        {
            Directory.CreateDirectory(dest);
            foreach (var f in Directory.EnumerateFiles(src))
            {
                if (cancelled()) return "cancelled";
                var err = CopyFileStreamed(f, Path.Combine(dest, Path.GetFileName(f)), overwrite, onBytes, cancelled);
                if (err != null) return err;
            }
            foreach (var dir in Directory.EnumerateDirectories(src))
            {
                if (cancelled()) return "cancelled";
                var err = CopyDirStreamed(dir, Path.Combine(dest, Path.GetFileName(dir)), overwrite, onBytes, cancelled);
                if (err != null) return err;
            }
            return null;
        }
        catch (Exception ex) { return Fail(ex); }
    }

    /// <summary>Extract a folder inside an archive to <paramref name="dest"/>, recursing
    /// through the VFS listing.</summary>
    private static string? ExtractArchiveDir(string archiveFile, string internalPath, string dest,
        bool overwrite, bool sevenZip, string? password, Action<long> onBytes, Func<bool> cancelled)
    {
        try
        {
            Directory.CreateDirectory(dest);
            var listing = ArchiveVfs.ListEntries(archiveFile, internalPath, password, sevenZip);
            foreach (var entry in listing.Entries)
            {
                if (entry.IsParent) continue;
                if (cancelled()) return "cancelled";
                if (ContainedPath(dest, entry.Name) is not { } target) return EscapesDest;
                string? err = entry.IsDir
                    ? CopyDirStreamed(entry.FullPath, target, overwrite, onBytes, cancelled, sevenZip, password)
                    : CopyFileStreamed(entry.FullPath, target, overwrite, onBytes, cancelled, sevenZip, password);
                if (err != null) return err;
            }
            return null;
        }
        catch (Exception ex) { return Fail(ex); }
    }

    /// <summary>Total byte size of an entry (recursive for folders), for progress totals.</summary>
    public static long EntrySize(FileEntry e)
    {
        if (!e.IsDir) return Math.Max(0, e.Size);
        if (ArchivePath.InArchive(e.FullPath)) return Math.Max(0, e.Size);   // archive dir — approximate
        try
        {
            long total = 0;
            foreach (var f in Directory.EnumerateFiles(e.FullPath, "*", SearchOption.AllDirectories))
            {
                try { total += new FileInfo(f).Length; } catch { }
            }
            return total;
        }
        catch { return 0; }
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }

    // ── Windows Recycle Bin (SHFileOperation) ────────────────────────────
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SHFILEOPSTRUCT
    {
        public IntPtr hwnd;
        public uint wFunc;
        [MarshalAs(UnmanagedType.LPWStr)] public string pFrom;
        [MarshalAs(UnmanagedType.LPWStr)] public string? pTo;
        public ushort fFlags;
        public int fAnyOperationsAborted;
        public IntPtr hNameMappings;
        [MarshalAs(UnmanagedType.LPWStr)] public string? lpszProgressTitle;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHFileOperation(ref SHFILEOPSTRUCT lpFileOp);

    private const uint FO_DELETE = 0x0003;
    private const ushort FOF_ALLOWUNDO = 0x0040;    // route to Recycle Bin
    private const ushort FOF_NOCONFIRMATION = 0x0010;
    private const ushort FOF_SILENT = 0x0004;
    private const ushort FOF_NOERRORUI = 0x0400;

    private static int ShellRecycle(string path)
    {
        var op = new SHFILEOPSTRUCT
        {
            wFunc = FO_DELETE,
            pFrom = path + "\0\0",   // pFrom is a double-null-terminated list
            fFlags = FOF_ALLOWUNDO | FOF_NOCONFIRMATION | FOF_SILENT | FOF_NOERRORUI,
        };
        return SHFileOperation(ref op);
    }
}
