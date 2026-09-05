using System.Diagnostics;
using System.Runtime.InteropServices;

namespace VesperCommander;

/// <summary>A process that has a file open. <paramref name="Name"/> is the friendliest
/// name the OS offers — "Microsoft Word" where Windows knows it, else the image name — and
/// <paramref name="Path"/> the executable, when it could be read.</summary>
public readonly record struct FileHolder(int Pid, string Name, string? Path);

/// <summary>
/// Which programs have a file open. The question comes up at exactly one moment — a delete,
/// a rename or a move has just failed with "in use" — and the OS message answers it with a
/// shrug. This asks properly.
///
/// <para>Windows has an API for precisely this: the Restart Manager, which is what Explorer's
/// "the file is open in Microsoft Word" dialog uses. It reports every process holding a
/// handle on the file, by friendly name, and it takes a list, so a folder is asked about
/// through the files in it. Linux has no API but has <c>/proc</c>: every process lists its
/// open descriptors, its working directory and its mapped files as links, and reading them
/// is the same thing <c>lsof</c> does. macOS is asked through <c>lsof</c> itself.</para>
///
/// <para>Everything here is best effort and never throws: a process that exits between the
/// list and the lookup, or one the caller has no right to inspect, is skipped. The one
/// promise is the shape of the answer — null when the platform could not say, an empty list
/// when it could and nobody has the file.</para>
/// </summary>
public static unsafe partial class FileHolders
{
    public static bool IsSupported =>
        OperatingSystem.IsWindows() || OperatingSystem.IsLinux() || OperatingSystem.IsMacOS();

    // A folder is asked about through what is in it, capped: the question "why can't I
    // delete this folder" is answered by any one open file inside it, and asking about the
    // ten-thousandth would only make the answer slower.
    private const int MaxFiles = 2000;

    /// <summary>The processes holding any of <paramref name="paths"/> open. A folder counts
    /// as itself plus everything beneath it. Null when the platform can't say; empty when
    /// nothing has them. Blocking — call it off the UI thread.</summary>
    public static List<FileHolder>? Find(IReadOnlyList<string> paths)
    {
        // The Restart Manager takes files only — a folder path fails the whole registration —
        // while /proc can name a folder outright (a shell's working directory is one).
        var files = Expand(paths, includeDirs: !OperatingSystem.IsWindows());
        if (files.Count == 0) return [];
        try
        {
            if (OperatingSystem.IsWindows()) return FindWindows(files);
            if (OperatingSystem.IsLinux()) return FindLinux(files, paths);
            if (OperatingSystem.IsMacOS()) return FindLsof(files);
        }
        catch { /* a missing DLL, a /proc that isn't one — no answer, not a crash */ }
        return null;
    }

    private static List<string> Expand(IReadOnlyList<string> paths, bool includeDirs)
    {
        var list = new List<string>();
        foreach (var p in paths)
        {
            if (list.Count >= MaxFiles) break;
            if (Directory.Exists(p))
            {
                if (includeDirs) list.Add(p);
                try
                {
                    var opts = new EnumerationOptions
                    {
                        RecurseSubdirectories = true,
                        IgnoreInaccessible = true,
                        AttributesToSkip = 0,   // hidden and system files are open by things too
                    };
                    foreach (var f in Directory.EnumerateFiles(p, "*", opts))
                    {
                        list.Add(f);
                        if (list.Count >= MaxFiles) break;
                    }
                }
                catch { /* partial is fine */ }
            }
            else if (File.Exists(p)) list.Add(p);
        }
        return list;
    }

    // ── Windows: the Restart Manager ─────────────────────────────────────

    [StructLayout(LayoutKind.Sequential)]
    private struct RM_UNIQUE_PROCESS
    {
        public int ProcessId;
        // FILETIME as two DWORDs, so the struct keeps its 4-byte packing; a long here would
        // pad the field to 8 and shift everything after it.
        public int StartLow, StartHigh;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct RM_PROCESS_INFO
    {
        public RM_UNIQUE_PROCESS Process;
        public fixed char AppName[256];           // CCH_RM_MAX_APP_NAME + 1
        public fixed char ServiceShortName[64];   // CCH_RM_MAX_SVC_NAME + 1
        public int ApplicationType;
        public uint AppStatus;
        public int TSSessionId;
        public int Restartable;
    }

    private const int ErrorMoreData = 234;
    private const int SessionKeyChars = 33;       // CCH_RM_SESSION_KEY + 1

    [LibraryImport("rstrtmgr.dll")]
    private static partial int RmStartSession(uint* session, int flags, char* sessionKey);

    [LibraryImport("rstrtmgr.dll")]
    private static partial int RmEndSession(uint session);

    [LibraryImport("rstrtmgr.dll")]
    private static partial int RmRegisterResources(uint session, uint fileCount, char** files,
        uint appCount, void* apps, uint serviceCount, char** services);

    [LibraryImport("rstrtmgr.dll")]
    private static partial int RmGetList(uint session, uint* needed, uint* count,
        RM_PROCESS_INFO* apps, uint* rebootReasons);

    private static List<FileHolder>? FindWindows(List<string> files)
    {
        if (!OperatingSystem.IsWindows()) return null;
        uint session;
        char* key = stackalloc char[SessionKeyChars + 1];
        new Span<char>(key, SessionKeyChars + 1).Clear();
        if (RmStartSession(&session, 0, key) != 0) return null;
        try
        {
            var names = new IntPtr[files.Count];
            try
            {
                for (int i = 0; i < files.Count; i++) names[i] = Marshal.StringToHGlobalUni(files[i]);
                fixed (IntPtr* p = names)
                    if (RmRegisterResources(session, (uint)files.Count, (char**)p, 0, null, 0, null) != 0)
                        return null;
            }
            finally { foreach (var n in names) if (n != IntPtr.Zero) Marshal.FreeHGlobal(n); }

            uint needed = 0, count = 0, reasons = 0;
            int rc = RmGetList(session, &needed, &count, null, &reasons);
            RM_PROCESS_INFO[] buf = [];
            // The list can grow between the size query and the fetch — a process opening the
            // file just now — so this is a loop, not a pair of calls.
            while (rc == ErrorMoreData)
            {
                buf = new RM_PROCESS_INFO[needed];
                count = needed;
                fixed (RM_PROCESS_INFO* b = buf)
                    rc = RmGetList(session, &needed, &count, b, &reasons);
            }
            if (rc != 0) return null;

            var result = new List<FileHolder>();
            var seen = new HashSet<int>();
            for (int i = 0; i < count && i < buf.Length; i++)
            {
                int pid = buf[i].Process.ProcessId;
                if (!seen.Add(pid)) continue;
                string app;
                fixed (char* c = buf[i].AppName) app = new string(c);
                result.Add(Describe(pid, app));
            }
            return result;
        }
        finally { RmEndSession(session); }
    }

    /// <summary>Fill in what the process table knows: the image name when the OS gave no
    /// friendly one, and the executable's path when it can be read (an elevated process
    /// refuses that to a normal one — then it is just the name).</summary>
    private static FileHolder Describe(int pid, string? friendly)
    {
        string? path = null, image = null;
        try
        {
            using var p = Process.GetProcessById(pid);
            image = p.ProcessName;
            try { path = p.MainModule?.FileName; } catch { /* access denied across integrity levels */ }
        }
        catch { /* gone already */ }
        string name = !string.IsNullOrWhiteSpace(friendly) ? friendly
            : !string.IsNullOrEmpty(image) ? image
            : $"process {pid}";
        // "Notepad" alone doesn't say which notepad; the image name beside it does, unless
        // they are the same word.
        if (image != null && !name.Contains(image, StringComparison.OrdinalIgnoreCase))
            name = $"{name} ({image})";
        return new FileHolder(pid, name, path);
    }

    // ── Linux: /proc ─────────────────────────────────────────────────────

    private static List<FileHolder>? FindLinux(List<string> files, IReadOnlyList<string> roots)
    {
        if (!Directory.Exists("/proc/self")) return null;
        var targets = new HashSet<string>(files.Select(Resolve), StringComparer.Ordinal);
        var dirs = roots.Where(Directory.Exists).Select(r => Resolve(r).TrimEnd('/') + "/").ToList();

        bool Matches(string? t)
        {
            if (t is null) return false;
            // A deleted-but-open file is reported as "path (deleted)"; that still counts.
            if (t.EndsWith(" (deleted)", StringComparison.Ordinal)) t = t[..^10];
            if (targets.Contains(t)) return true;
            foreach (var d in dirs) if (t.StartsWith(d, StringComparison.Ordinal)) return true;
            return false;
        }

        var result = new List<FileHolder>();
        foreach (var dir in Directory.EnumerateDirectories("/proc"))
        {
            if (!int.TryParse(Path.GetFileName(dir), out int pid)) continue;
            bool hit = false;
            try
            {
                // The working directory and the executable first — a shell sitting in the
                // folder, or the program being run — then every open descriptor, then the
                // mapped files, which is where a loaded library or a running binary shows.
                hit = Matches(Link(Path.Combine(dir, "cwd"))) || Matches(Link(Path.Combine(dir, "exe")));
                if (!hit)
                    foreach (var fd in Directory.EnumerateFileSystemEntries(Path.Combine(dir, "fd")))
                        if (Matches(Link(fd))) { hit = true; break; }
                if (!hit)
                    foreach (var line in File.ReadLines(Path.Combine(dir, "maps")))
                    {
                        int slash = line.IndexOf('/');
                        if (slash > 0 && Matches(line[slash..])) { hit = true; break; }
                    }
            }
            catch { continue; }   // another user's process, or one that just exited
            if (!hit) continue;

            string name;
            try { name = File.ReadAllText(Path.Combine(dir, "comm")).Trim(); }
            catch { name = $"process {pid}"; }
            string? exe = null;
            try { exe = Link(Path.Combine(dir, "exe")); } catch { }
            result.Add(new FileHolder(pid, name, exe));
        }
        return result;
    }

    private static string? Link(string path)
    {
        try { return new FileInfo(path).LinkTarget; }
        catch { return null; }
    }

    private static string Resolve(string path)
    {
        try
        {
            var full = Path.GetFullPath(path);
            FileSystemInfo info = Directory.Exists(full) ? new DirectoryInfo(full) : new FileInfo(full);
            return info.ResolveLinkTarget(returnFinalTarget: true)?.FullName ?? full;
        }
        catch { return path; }
    }

    // ── macOS: lsof ──────────────────────────────────────────────────────

    private static List<FileHolder>? FindLsof(List<string> files)
    {
        var psi = new ProcessStartInfo("lsof")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        // -F: one field per line, p = pid, c = command. "--" ends the switches, since a file
        // name is data and could start with a dash.
        psi.ArgumentList.Add("-Fpc");
        psi.ArgumentList.Add("--");
        foreach (var f in files) psi.ArgumentList.Add(f);

        using var p = Process.Start(psi);
        if (p is null) return null;
        var output = p.StandardOutput.ReadToEnd();
        p.StandardError.ReadToEnd();
        if (!p.WaitForExit(15_000)) { try { p.Kill(); } catch { } return null; }
        // lsof exits 1 when nothing matched, which is an answer, not a failure.

        var result = new List<FileHolder>();
        var seen = new HashSet<int>();
        int pid = -1;
        foreach (var line in output.Split('\n'))
        {
            if (line.Length < 2) continue;
            if (line[0] == 'p') int.TryParse(line.AsSpan(1), out pid);
            else if (line[0] == 'c' && pid > 0 && seen.Add(pid))
                result.Add(Describe(pid, line[1..]));
        }
        return result;
    }
}
