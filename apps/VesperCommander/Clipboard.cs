using System.Runtime.InteropServices;
using System.Text;

namespace VesperCommander;

/// <summary>File clipboard for cut/copy/paste. On Windows it reads and writes the
/// shell's <c>CF_HDROP</c> + "Preferred DropEffect" formats so cut/copy interops with
/// Explorer in both directions. An in-process mirror is kept as the source of truth so
/// the app's own paste still works when the OS clipboard is unavailable (headless tests,
/// non-Windows). Move vs copy rides on the DropEffect (Explorer's convention).</summary>
public static class Clipboard
{
    private static List<string> _files = new();
    private static bool _cut;

    public static void SetFiles(IReadOnlyList<string> paths, bool cut)
    {
        _files = paths.ToList();
        _cut = cut;
        if (OperatingSystem.IsWindows())
            try { WinSetFiles(paths, cut); } catch { /* best-effort; in-process mirror stands in */ }
    }

    /// <summary>The current file clipboard. Prefers the OS clipboard on Windows (to
    /// catch a cut/copy done in Explorer), else the in-process mirror.</summary>
    public static bool TryGetFiles(out IReadOnlyList<string> paths, out bool cut)
    {
        if (OperatingSystem.IsWindows())
        {
            try
            {
                if (WinTryGetFiles(out var wp, out var wc) && wp.Count > 0)
                { paths = wp; cut = wc; return true; }
            }
            catch { /* fall back to the in-process mirror */ }
        }
        paths = _files; cut = _cut;
        return _files.Count > 0;
    }

    public static void Clear()
    {
        _files = new();
        _cut = false;
        if (OperatingSystem.IsWindows())
            try { WinClear(); } catch { /* best-effort */ }
    }

    // ── Win32 CF_HDROP + Preferred DropEffect ────────────────────────────
    private const uint CF_HDROP = 15;
    private const uint GMEM_MOVEABLE = 0x0002;
    private const int DROPEFFECT_COPY = 1, DROPEFFECT_MOVE = 2;

    [DllImport("user32.dll", SetLastError = true)] private static extern bool OpenClipboard(IntPtr hWnd);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool CloseClipboard();
    [DllImport("user32.dll", SetLastError = true)] private static extern bool EmptyClipboard();
    [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr SetClipboardData(uint fmt, IntPtr h);
    [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr GetClipboardData(uint fmt);
    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)] private static extern uint RegisterClipboardFormat(string name);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern IntPtr GlobalAlloc(uint flags, UIntPtr bytes);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern IntPtr GlobalLock(IntPtr h);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool GlobalUnlock(IntPtr h);
    [DllImport("shell32.dll", SetLastError = true, CharSet = CharSet.Unicode)] private static extern uint DragQueryFile(IntPtr hDrop, uint i, StringBuilder? file, uint cch);

    [StructLayout(LayoutKind.Sequential)]
    private struct DROPFILES { public uint pFiles; public int ptX; public int ptY; public int fNC; public int fWide; }

    private static void WinSetFiles(IReadOnlyList<string> paths, bool cut)
    {
        if (!OpenClipboard(IntPtr.Zero)) return;
        try
        {
            EmptyClipboard();

            // CF_HDROP: a DROPFILES header followed by the wide, double-null-terminated
            // path list. pFiles is the byte offset from the header start to that list.
            var list = new StringBuilder();
            foreach (var p in paths) { list.Append(p); list.Append('\0'); }
            list.Append('\0');
            byte[] fileBytes = Encoding.Unicode.GetBytes(list.ToString());
            int headerSize = Marshal.SizeOf<DROPFILES>();
            IntPtr hDrop = GlobalAlloc(GMEM_MOVEABLE, (UIntPtr)(headerSize + fileBytes.Length));
            IntPtr p0 = GlobalLock(hDrop);
            try
            {
                Marshal.StructureToPtr(new DROPFILES { pFiles = (uint)headerSize, fWide = 1 }, p0, false);
                Marshal.Copy(fileBytes, 0, p0 + headerSize, fileBytes.Length);
            }
            finally { GlobalUnlock(hDrop); }
            SetClipboardData(CF_HDROP, hDrop);

            // Preferred DropEffect: a single DWORD (move vs copy).
            uint effFmt = RegisterClipboardFormat("Preferred DropEffect");
            IntPtr hEff = GlobalAlloc(GMEM_MOVEABLE, (UIntPtr)4);
            IntPtr e0 = GlobalLock(hEff);
            try { Marshal.WriteInt32(e0, cut ? DROPEFFECT_MOVE : DROPEFFECT_COPY); }
            finally { GlobalUnlock(hEff); }
            SetClipboardData(effFmt, hEff);
        }
        finally { CloseClipboard(); }
    }

    private static bool WinTryGetFiles(out IReadOnlyList<string> paths, out bool cut)
    {
        var result = new List<string>();
        cut = false;
        paths = result;
        if (!OpenClipboard(IntPtr.Zero)) return false;
        try
        {
            IntPtr hDrop = GetClipboardData(CF_HDROP);
            if (hDrop == IntPtr.Zero) return false;

            uint count = DragQueryFile(hDrop, 0xFFFFFFFF, null, 0);
            for (uint i = 0; i < count; i++)
            {
                uint len = DragQueryFile(hDrop, i, null, 0);
                var sb = new StringBuilder((int)len + 1);
                DragQueryFile(hDrop, i, sb, len + 1);
                result.Add(sb.ToString());
            }

            // A DropEffect of MOVE (without COPY) means the source was cut.
            uint effFmt = RegisterClipboardFormat("Preferred DropEffect");
            IntPtr hEff = GetClipboardData(effFmt);
            if (hEff != IntPtr.Zero)
            {
                IntPtr e0 = GlobalLock(hEff);
                if (e0 != IntPtr.Zero)
                {
                    try
                    {
                        int eff = Marshal.ReadInt32(e0);
                        cut = (eff & DROPEFFECT_MOVE) != 0 && (eff & DROPEFFECT_COPY) == 0;
                    }
                    finally { GlobalUnlock(hEff); }
                }
            }
            return result.Count > 0;
        }
        finally { CloseClipboard(); }
    }

    private static void WinClear()
    {
        if (!OpenClipboard(IntPtr.Zero)) return;
        try { EmptyClipboard(); } finally { CloseClipboard(); }
    }
}
