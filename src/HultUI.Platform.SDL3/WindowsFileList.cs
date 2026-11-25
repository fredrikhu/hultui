using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;

namespace HultUI.Platform.SDL3;

/// <summary>
/// A list of files the way Windows passes one between applications: <c>CF_HDROP</c>, a
/// <c>DROPFILES</c> header followed by the names. The same block goes into an outbound drag
/// (<see cref="WindowsFileDrag"/>), comes out of an inbound one (<see cref="WindowsDropTarget"/>)
/// and sits on the clipboard when Explorer copies files (<see cref="WindowsClipboard"/>).
/// </summary>
/// <remarks>
/// The clipboard speaks MIME types, so it also converts the list to and from
/// <c>text/uri-list</c> (RFC 2483), what SDL hands over for copied files on Linux: one
/// <c>file:</c> URI per line, CRLF after each, the path percent-encoded so spaces and
/// non-ASCII names survive. A UNC path is a URI with the server as its host
/// (<c>file://server/share/…</c>).
/// </remarks>
[SupportedOSPlatform("windows")]
internal static unsafe partial class WindowsFileList
{
    private const uint GMEM_MOVEABLE = 0x0002;

    /// <summary>A movable global block holding <paramref name="paths"/> as CF_HDROP, owned by
    /// the caller until it hands it over; 0 for no paths or when the allocation fails.</summary>
    public static nint Allocate(IReadOnlyList<string> paths)
    {
        if (paths.Count == 0) return 0;

        int chars = 1;                                   // the list's own terminating NUL
        foreach (var p in paths) chars += p.Length + 1;

        nuint bytes = (nuint)(sizeof(DROPFILES) + chars * sizeof(char));
        nint hglobal = GlobalAlloc(GMEM_MOVEABLE, bytes);
        if (hglobal == 0) return 0;

        nint locked = GlobalLock(hglobal);
        if (locked == 0)
        {
            GlobalFree(hglobal);
            return 0;
        }

        try
        {
            var header = (DROPFILES*)locked;
            *header = default;
            header->pFiles = (uint)sizeof(DROPFILES);   // names start right after the header
            header->fWide = 1;                          // ...and are UTF-16

            var dst = (char*)((byte*)locked + sizeof(DROPFILES));
            foreach (var p in paths)
            {
                p.AsSpan().CopyTo(new Span<char>(dst, p.Length));
                dst += p.Length;
                *dst++ = '\0';
            }
            *dst = '\0';
        }
        finally { GlobalUnlock(hglobal); }

        return hglobal;
    }

    /// <summary>The paths in a CF_HDROP block. <c>DragQueryFileW</c> reads both kinds Windows
    /// writes, UTF-16 and the ANSI code page, and returns UTF-16 either way.</summary>
    public static List<string> Read(nint hdrop)
    {
        var files = new List<string>();
        uint count = DragQueryFileW(hdrop, 0xFFFFFFFF, null, 0);
        for (uint i = 0; i < count; i++)
        {
            uint length = DragQueryFileW(hdrop, i, null, 0);
            if (length == 0) continue;
            var buffer = new char[length + 1];
            fixed (char* p = buffer)
                if (DragQueryFileW(hdrop, i, p, length + 1) > 0)
                    files.Add(new string(p, 0, (int)length));
        }
        return files;
    }

    /// <summary><paramref name="paths"/> as UTF-8 <c>text/uri-list</c>.</summary>
    public static byte[] ToUriList(IEnumerable<string> paths)
    {
        var list = new StringBuilder();
        foreach (var path in paths)
        {
            // shortcut: a \\?\ path is not a URI and is skipped; Explorer does not copy them.
            if (Uri.TryCreate(path, UriKind.Absolute, out var uri) && uri.IsFile)
                list.Append(uri.AbsoluteUri).Append("\r\n");
        }
        return Encoding.UTF8.GetBytes(list.ToString());
    }

    /// <summary>The Windows paths named by the <c>file:</c> URIs in UTF-8 <c>text/uri-list</c>.
    /// Comment lines, other schemes and paths that are not Windows paths (a Linux
    /// <c>file:///home/…</c>) are left out: no Windows application could open them.</summary>
    public static List<string> FromUriList(byte[] uriList)
    {
        var paths = new List<string>();
        foreach (var line in Encoding.UTF8.GetString(uriList).Split('\n'))
        {
            var entry = line.Trim();
            if (entry.Length == 0 || entry[0] == '#') continue;
            if (Uri.TryCreate(entry, UriKind.Absolute, out var uri) && uri.IsFile
                && Path.IsPathFullyQualified(uri.LocalPath))
                paths.Add(uri.LocalPath);
        }
        return paths;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DROPFILES
    {
        public uint pFiles;      // byte offset from this header to the first name
        public int PtX, PtY;     // POINT pt — the drop point; the shell fills it in
        public int fNC;
        public int fWide;        // non-zero: the names are UTF-16
    }

    [LibraryImport("shell32.dll")]
    private static partial uint DragQueryFileW(nint drop, uint index, char* file, uint length);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial nint GlobalAlloc(uint uFlags, nuint dwBytes);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial nint GlobalFree(nint hMem);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial nint GlobalLock(nint hMem);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GlobalUnlock(nint hMem);
}
