using System.Diagnostics;
using System.Runtime.InteropServices;

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

    /// <summary>Open a file with the OS default handler (Enter on a file). Windows
    /// shell-executes; macOS uses <c>open</c>, Linux <c>xdg-open</c>.</summary>
    public static void OpenInShell(string path)
    {
        try
        {
            if (OperatingSystem.IsWindows())
                Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
            else if (OperatingSystem.IsMacOS())
                Process.Start("open", [path]);
            else
                Process.Start("xdg-open", [path]);
        }
        catch { /* no handler / launch failed — nothing sensible to do */ }
    }

    /// <summary>Copy an entry into <paramref name="destDir"/> (recursively for folders).</summary>
    public static string? Copy(FileEntry e, string destDir, bool overwrite)
    {
        try
        {
            var dest = Path.Combine(destDir, e.Name);
            if (e.IsDir) CopyDirectory(e.FullPath, dest, overwrite);
            else File.Copy(e.FullPath, dest, overwrite);
            return null;
        }
        catch (Exception ex) { return ex.Message; }
    }

    /// <summary>Move an entry into <paramref name="destDir"/>. When
    /// <paramref name="overwrite"/> and the target exists, it is recycled first.</summary>
    public static string? Move(FileEntry e, string destDir, bool overwrite)
    {
        try
        {
            var dest = Path.Combine(destDir, e.Name);
            if (Exists(dest))
            {
                if (!overwrite) return "target exists";
                Recycle(dest, Directory.Exists(dest));
            }
            if (e.IsDir) Directory.Move(e.FullPath, dest);
            else File.Move(e.FullPath, dest);
            return null;
        }
        catch (Exception ex) { return ex.Message; }
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
        catch (Exception ex) { return ex.Message; }
    }

    /// <summary>Send a path to the Recycle Bin (Windows); permanent delete elsewhere.</summary>
    public static string? Recycle(string path, bool isDir)
    {
        try
        {
            if (OperatingSystem.IsWindows())
            {
                int rc = ShellRecycle(path);
                if (rc != 0) return $"delete failed (0x{rc:X})";
            }
            else
            {
                if (isDir) Directory.Delete(path, recursive: true);
                else File.Delete(path);
            }
            return null;
        }
        catch (Exception ex) { return ex.Message; }
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
        catch (Exception ex) { return ex.Message; }
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
        Action<long> onBytes, Func<bool> cancelled)
    {
        var dest = Path.Combine(destDir, e.Name);
        return e.IsDir
            ? CopyDirStreamed(e.FullPath, dest, overwrite, onBytes, cancelled)
            : CopyFileStreamed(e.FullPath, dest, overwrite, onBytes, cancelled);
    }

    private static string? CopyFileStreamed(string src, string dest, bool overwrite,
        Action<long> onBytes, Func<bool> cancelled)
    {
        try
        {
            using var s = new FileStream(src, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, ChunkSize);
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
            try { File.SetLastWriteTimeUtc(dest, File.GetLastWriteTimeUtc(src)); } catch { }
            return null;
        }
        catch (Exception ex) { return ex.Message; }
    }

    private static string? CopyDirStreamed(string src, string dest, bool overwrite,
        Action<long> onBytes, Func<bool> cancelled)
    {
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
        catch (Exception ex) { return ex.Message; }
    }

    /// <summary>Total byte size of an entry (recursive for folders), for progress totals.</summary>
    public static long EntrySize(FileEntry e)
    {
        if (!e.IsDir) return e.Size;
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
