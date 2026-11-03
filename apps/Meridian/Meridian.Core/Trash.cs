using System.Runtime.InteropServices;

namespace Meridian.Core;

/// <summary>
/// Where a file or folder deleted in the app goes: the OS's own bin, so a delete made by
/// mistake comes back from there, or nowhere when the platform has none — and then the app
/// says the delete is permanent before it makes it.
/// </summary>
public interface IFileTrash
{
    /// <summary>What the user calls it — "Recycle Bin", "Trash" — or null when this platform
    /// has none and <see cref="Delete"/> removes the path for good.</summary>
    string? Name { get; }

    /// <summary>Moves a file, or a folder with everything in it, to the bin (or deletes it when
    /// <see cref="Name"/> is null). Null when it went; otherwise why not, and the path is still there.</summary>
    string? Delete(string path);
}

public static class FileTrash
{
    /// <summary>The Recycle Bin on Windows, the freedesktop.org trash on Linux and the BSDs,
    /// none elsewhere.</summary>
    public static IFileTrash Default() =>
        OperatingSystem.IsWindows() ? new RecycleBin()
        : OperatingSystem.IsLinux() || OperatingSystem.IsFreeBSD() ? new FreedesktopTrash(FreedesktopTrash.HomeTrash())
        : new NoTrash();

    /// <summary>Removes a path for good: the fallback when the bin refuses it.</summary>
    public static string? DeletePermanently(string path)
    {
        try
        {
            if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
            else File.Delete(path);
            return null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return e.Message;
        }
    }
}

/// <summary>No bin: a delete is permanent, which the confirmation says.</summary>
public sealed class NoTrash : IFileTrash
{
    public string? Name => null;
    public string? Delete(string path) => FileTrash.DeletePermanently(path);
}

/// <summary>
/// The Windows Recycle Bin, through SHFileOperationW with FOF_ALLOWUNDO — Vesper Commander's
/// route. On a drive that has no bin (a network share) the shell would delete for good, so
/// FOF_WANTNUKEWARNING lets the shell ask first instead of doing that silently.
/// </summary>
public sealed partial class RecycleBin : IFileTrash
{
    public string? Name => "Recycle Bin";

    public string? Delete(string path)
    {
        if (!OperatingSystem.IsWindows()) return "No Recycle Bin on this platform";
        if (!File.Exists(path) && !Directory.Exists(path)) return "It is not there any more";
        // pFrom is a list of paths, each ending in a null, the list in a second one.
        var from = Marshal.StringToHGlobalUni(Path.GetFullPath(path) + "\0\0");
        try
        {
            var op = new ShFileOpStruct
            {
                wFunc = FoDelete,
                pFrom = from,
                fFlags = FofAllowUndo | FofNoConfirmation | FofSilent | FofNoErrorUi | FofWantNukeWarning,
            };
            int rc = SHFileOperationW(ref op);
            if (rc != 0)
                // Win32 codes, mostly: 5 and 0x78 access denied, 0x20 and 0x21 something has it open.
                return rc is 0x20 or 0x21 ? "Another program has it open"
                    : rc is 5 or 0x78 ? "Access is denied"
                    : $"The shell refused (0x{rc:X})";
            if (op.fAnyOperationsAborted != 0) return "Cancelled";
            return null;
        }
        finally
        {
            Marshal.FreeHGlobal(from);
        }
    }

    private const uint FoDelete = 0x0003;
    private const ushort FofSilent = 0x0004;
    private const ushort FofNoConfirmation = 0x0010;
    private const ushort FofAllowUndo = 0x0040;
    private const ushort FofNoErrorUi = 0x0400;
    private const ushort FofWantNukeWarning = 0x4000;

    // Blittable, so the call needs no runtime marshalling (the app is published AOT).
    [StructLayout(LayoutKind.Sequential)]
    private struct ShFileOpStruct
    {
        public IntPtr hwnd;
        public uint wFunc;
        public IntPtr pFrom;
        public IntPtr pTo;
        public ushort fFlags;
        public int fAnyOperationsAborted;
        public IntPtr hNameMappings;
        public IntPtr lpszProgressTitle;
    }

    [LibraryImport("shell32.dll", EntryPoint = "SHFileOperationW")]
    private static partial int SHFileOperationW(ref ShFileOpStruct op);
}

/// <summary>
/// The freedesktop.org trash (Trash spec 1.0), which GNOME Files, Dolphin and the other Linux file
/// managers show and restore from: the path moves to <c>files/</c> under a name free in both
/// <c>files/</c> and <c>info/</c>, and <c>info/&lt;name&gt;.trashinfo</c> records where it came from
/// and when. Only the home trash: a path on another file system than the trash cannot be moved
/// there, and the app then offers to delete it for good instead.
/// </summary>
public sealed class FreedesktopTrash(string trashDir) : IFileTrash
{
    public string? Name => "Trash";

    /// <summary><c>$XDG_DATA_HOME/Trash</c>, or <c>~/.local/share/Trash</c>.</summary>
    public static string HomeTrash()
    {
        var dataHome = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
        if (string.IsNullOrEmpty(dataHome) || !Path.IsPathRooted(dataHome))
            dataHome = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share");
        return Path.Combine(dataHome, "Trash");
    }

    public string? Delete(string path)
    {
        string full = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar);
        bool isDir = Directory.Exists(full);
        if (!isDir && !File.Exists(full)) return "It is not there any more";
        string files = Path.Combine(trashDir, "files"), info = Path.Combine(trashDir, "info");
        string? infoFile = null;
        try
        {
            Directory.CreateDirectory(files);
            Directory.CreateDirectory(info);
            // The .trashinfo is made first, with CreateNew: that claims the name against
            // another program trashing a file of the same name at the same moment.
            string name = Path.GetFileName(full);
            string stem = isDir ? name : Path.GetFileNameWithoutExtension(name), ext = isDir ? "" : Path.GetExtension(name);
            for (int n = 1; ; n++)
            {
                string candidate = n == 1 ? name : $"{stem}.{n}{ext}";
                if (Path.Exists(Path.Combine(files, candidate))) continue;
                try
                {
                    infoFile = Path.Combine(info, candidate + ".trashinfo");
                    using (var stream = new FileStream(infoFile, FileMode.CreateNew))
                    using (var writer = new StreamWriter(stream))
                        writer.Write($"[Trash Info]\nPath={EscapePath(full)}\nDeletionDate={DateTime.Now:yyyy-MM-ddTHH:mm:ss}\n");
                }
                catch (IOException) when (File.Exists(infoFile))
                {
                    infoFile = null;
                    continue;
                }
                string target = Path.Combine(files, candidate);
                if (isDir) Directory.Move(full, target);
                else File.Move(full, target);
                return null;
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            if (infoFile != null) try { File.Delete(infoFile); } catch (IOException) { }
            return e.Message;
        }
    }

    /// <summary>The spec's <c>Path=</c>: the absolute path, percent-escaped as a URL path is,
    /// slashes kept.</summary>
    internal static string EscapePath(string path) =>
        string.Join('/', path.Split('/').Select(Uri.EscapeDataString));
}
