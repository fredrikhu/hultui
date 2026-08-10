using System.Runtime.InteropServices;

namespace VesperCommander;

/// <summary>
/// The Windows shell Properties sheet — the same dialog Explorer opens on Alt+Enter,
/// with its General / Security / Details / Previous Versions tabs. Invoked through
/// <c>ShellExecuteEx</c> with the <c>properties</c> verb, which needs
/// <c>SEE_MASK_INVOKEIDLIST</c> because the verb lives on the item's shell context menu
/// rather than in its file association.
/// </summary>
/// <remarks>
/// Windows-only by nature: there is no cross-platform equivalent to hand off to, so
/// <see cref="IsSupported"/> is false elsewhere and the caller posts a notice instead.
/// </remarks>
internal static class ShellProperties
{
    public static bool IsSupported => OperatingSystem.IsWindows();

    /// <summary>Open the Properties sheet for <paramref name="path"/>. Returns false when
    /// the platform or the path rules it out; a true return only means the sheet was handed
    /// to the shell, since it is raised on a thread of the shell's own making.</summary>
    public static bool Show(string path)
    {
        if (!OperatingSystem.IsWindows()) return false;
        if (ShellTarget(path) is not { } target) return false;

        // Owner window, read here on the UI thread: it centres the sheet over Vesper and
        // keeps it above us. GetActiveWindow is per-thread, so the worker below can't ask.
        IntPtr owner = GetActiveWindow();

        // Off the UI thread, for two reasons. The call can block while the shell loads
        // whatever property-sheet extensions are registered for the file type — third-party
        // code, reaching over the network if the file is on a share — and Program.cs uses
        // top-level statements, so the main thread is MTA while the shell namespace wants STA.
        var t = new Thread(() => Invoke(target, owner)) { IsBackground = true, Name = "shell-properties" };
        t.SetApartmentState(ApartmentState.STA);
        t.Start();
        return true;
    }

    /// <summary>The path in the shape <c>ShellExecuteEx</c> wants, or null when there is
    /// nothing to show. A volume root must keep its trailing separator — bare <c>"C:"</c> is
    /// the drive-relative current directory, not the drive — while every other path must
    /// lose it, since <c>"C:\dir\"</c> does not bind to a shell item.</summary>
    internal static string? ShellTarget(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        if (string.Equals(System.IO.Path.GetPathRoot(path), path, StringComparison.Ordinal)) return path;
        var trimmed = path.TrimEnd(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar);
        return trimmed.Length == 0 ? null : trimmed;
    }

    /// <summary>Raise the sheet and let this thread end. Measured on Windows 11: the shell
    /// hosts the property sheet on a thread it creates inside our process, not on the
    /// caller's — so the sheet outlives this method, and a message pump here would have
    /// nothing to dispatch. <c>SEE_MASK_NOASYNC</c> is what makes leaving safe: it tells the
    /// shell the caller is about to go away, so the invocation completes here rather than on
    /// a helper that would be torn down with the thread. Were a future Windows to host the
    /// sheet on the invoking thread instead, it would vanish the moment this returns — the
    /// fix then is a pump loop over <c>IsDialogMessage</c>, not a longer sleep.</summary>
    private static void Invoke(string path, IntPtr owner)
    {
        try
        {
            var info = new SHELLEXECUTEINFO
            {
                cbSize = Marshal.SizeOf<SHELLEXECUTEINFO>(),
                fMask = SEE_MASK_INVOKEIDLIST | SEE_MASK_NOASYNC,
                hwnd = owner,
                lpVerb = "properties",
                lpFile = path,
                nShow = SW_SHOW,
            };
            ShellExecuteExW(ref info);
        }
        catch { /* the sheet is a convenience — never take the app down for it */ }
    }

    // ── Interop ──────────────────────────────────────────────────────────
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SHELLEXECUTEINFO
    {
        public int cbSize;
        public uint fMask;
        public IntPtr hwnd;
        [MarshalAs(UnmanagedType.LPWStr)] public string? lpVerb;
        [MarshalAs(UnmanagedType.LPWStr)] public string? lpFile;
        [MarshalAs(UnmanagedType.LPWStr)] public string? lpParameters;
        [MarshalAs(UnmanagedType.LPWStr)] public string? lpDirectory;
        public int nShow;
        public IntPtr hInstApp;
        public IntPtr lpIDList;
        [MarshalAs(UnmanagedType.LPWStr)] public string? lpClass;
        public IntPtr hkeyClass;
        public uint dwHotKey;
        public IntPtr hIcon;        // union: hIcon / hMonitor
        public IntPtr hProcess;
    }

    private const uint SEE_MASK_INVOKEIDLIST = 0x0000000C;
    private const uint SEE_MASK_NOASYNC = 0x00000100;
    private const int SW_SHOW = 5;

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool ShellExecuteExW(ref SHELLEXECUTEINFO lpExecInfo);

    [DllImport("user32.dll")]
    private static extern IntPtr GetActiveWindow();
}
