using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using HultUI.Core.Platform;

namespace HultUI.Platform.SDL3;

/// <summary>
/// The Windows clipboard, driven directly.
///
/// <para>SDL's Windows backend serves the first offered format's bytes whatever type is
/// asked for, so a multi-format offer reads back as one format repeated — verified with a
/// single format working and two failing in either order. That is the whole reason "copy
/// as rich text and as plain text" exists, so on Windows the clipboard goes through
/// <c>OpenClipboard</c> and friends instead. Every other platform keeps using SDL.</para>
///
/// <para>Windows has its own registry of format ids rather than MIME types, so the
/// well-known ones are mapped: text becomes <c>CF_UNICODETEXT</c> (UTF-16, which is what
/// every Windows app actually reads), and HTML becomes the registered "HTML Format" with
/// the header Word and browsers require. Anything unmapped is registered under its MIME
/// string and round-trips verbatim between HultUI apps.</para>
/// </summary>
[SupportedOSPlatform("windows")]
internal static partial class WindowsClipboard
{
    private const uint CF_UNICODETEXT = 13;
    private const uint GMEM_MOVEABLE = 0x0002;

    /// <summary>
    /// Windows hands the clipboard to one process at a time, and Explorer or a clipboard
    /// manager can be holding it for a few milliseconds. Retrying is the documented way to
    /// deal with that; giving up means the copy silently does nothing.
    /// </summary>
    private const int OpenAttempts = 10;

    public static bool TrySetData(nint hwnd, IReadOnlyDictionary<string, byte[]> data)
    {
        if (!Open(hwnd)) return false;
        try
        {
            if (!EmptyClipboard()) return false;

            foreach (var (mime, bytes) in data)
            {
                var payload = Encode(mime, bytes);
                if (payload.Length == 0) continue;

                nint handle = Copy(payload);
                if (handle == 0) continue;

                // Ownership moves to the clipboard on success; on failure it stays ours.
                if (SetClipboardData(FormatFor(mime), handle) == 0)
                    GlobalFree(handle);
            }

            return true;
        }
        finally
        {
            CloseClipboard();
        }
    }

    public static bool HasFormat(string mime) => IsClipboardFormatAvailable(FormatFor(mime));

    public static byte[]? TryGetData(nint hwnd, string mime)
    {
        uint format = FormatFor(mime);
        if (!IsClipboardFormatAvailable(format)) return null;
        if (!Open(hwnd)) return null;

        try
        {
            nint handle = GetClipboardData(format);
            if (handle == 0) return null;

            nint locked = GlobalLock(handle);
            if (locked == 0) return null;

            try
            {
                int size = (int)GlobalSize(handle);
                if (size <= 0) return [];

                var raw = new byte[size];
                Marshal.Copy(locked, raw, 0, size);
                return Decode(mime, raw);
            }
            finally
            {
                GlobalUnlock(handle);
            }
        }
        finally
        {
            CloseClipboard();
        }
    }

    public static bool Clear(nint hwnd)
    {
        if (!Open(hwnd)) return false;
        try { return EmptyClipboard(); }
        finally { CloseClipboard(); }
    }

    private static bool Open(nint hwnd)
    {
        for (int attempt = 0; attempt < OpenAttempts; attempt++)
        {
            if (OpenClipboard(hwnd)) return true;
            Thread.Sleep(10);
        }
        return false;
    }

    private static nint Copy(byte[] payload)
    {
        nint handle = GlobalAlloc(GMEM_MOVEABLE, (nuint)payload.Length);
        if (handle == 0) return 0;

        nint locked = GlobalLock(handle);
        if (locked == 0)
        {
            GlobalFree(handle);
            return 0;
        }

        try
        {
            Marshal.Copy(payload, 0, locked, payload.Length);
        }
        finally
        {
            GlobalUnlock(handle);
        }

        return handle;
    }

    // ──── MIME ↔ Windows clipboard format ────

    private static readonly Dictionary<string, uint> RegisteredFormats = new(StringComparer.Ordinal);

    private static uint FormatFor(string mime)
    {
        if (mime == ClipboardFormats.PlainText || mime == "text/plain")
            return CF_UNICODETEXT;

        lock (RegisteredFormats)
        {
            if (RegisteredFormats.TryGetValue(mime, out uint cached)) return cached;

            // "HTML Format" and "PNG" are the names Windows apps look for; registering
            // under the MIME string instead would make the data invisible to Word, the
            // browsers, and every image editor.
            string name = mime switch
            {
                ClipboardFormats.Html => "HTML Format",
                ClipboardFormats.Png => "PNG",
                _ => mime,
            };

            uint id = RegisterClipboardFormatW(name);
            RegisteredFormats[mime] = id;
            return id;
        }
    }

    private static byte[] Encode(string mime, byte[] bytes)
    {
        if (mime == ClipboardFormats.PlainText || mime == "text/plain")
        {
            // CF_UNICODETEXT is UTF-16 and null-terminated.
            var text = Encoding.UTF8.GetString(bytes);
            var utf16 = new byte[(text.Length + 1) * 2];
            Encoding.Unicode.GetBytes(text, 0, text.Length, utf16, 0);
            return utf16;
        }

        if (mime == ClipboardFormats.Html)
            return WrapHtml(Encoding.UTF8.GetString(bytes));

        return bytes;
    }

    private static byte[] Decode(string mime, byte[] raw)
    {
        if (mime == ClipboardFormats.PlainText || mime == "text/plain")
        {
            var text = Encoding.Unicode.GetString(raw);
            int nul = text.IndexOf('\0');
            if (nul >= 0) text = text[..nul];
            return Encoding.UTF8.GetBytes(text);
        }

        if (mime == ClipboardFormats.Html)
            return Encoding.UTF8.GetBytes(UnwrapHtml(Encoding.UTF8.GetString(raw)));

        return raw;
    }

    // ──── CF_HTML ────
    //
    // Windows does not put bare markup on the clipboard. It wants a header of byte offsets
    // into the payload itself, which is why the offsets are written as fixed-width zeros
    // first and patched afterwards — their own digits shift everything that follows.

    private const string HtmlHeaderTemplate =
        "Version:0.9\r\nStartHTML:{0:D10}\r\nEndHTML:{1:D10}\r\n" +
        "StartFragment:{2:D10}\r\nEndFragment:{3:D10}\r\n";

    private const string HtmlPrefix = "<html><body>\r\n<!--StartFragment-->";
    private const string HtmlSuffix = "<!--EndFragment-->\r\n</body></html>";

    private static byte[] WrapHtml(string html)
    {
        int headerLength = string.Format(HtmlHeaderTemplate, 0, 0, 0, 0).Length;
        int startHtml = headerLength;
        int startFragment = startHtml + HtmlPrefix.Length;
        int endFragment = startFragment + Encoding.UTF8.GetByteCount(html);
        int endHtml = endFragment + HtmlSuffix.Length;

        var header = string.Format(HtmlHeaderTemplate, startHtml, endHtml, startFragment, endFragment);
        return Encoding.UTF8.GetBytes(header + HtmlPrefix + html + HtmlSuffix);
    }

    /// <summary>
    /// Returns just the fragment, so what a caller reads back is what it wrote rather than
    /// the header and scaffolding Windows requires around it.
    /// </summary>
    private static string UnwrapHtml(string payload)
    {
        int start = payload.IndexOf("<!--StartFragment-->", StringComparison.Ordinal);
        int end = payload.IndexOf("<!--EndFragment-->", StringComparison.Ordinal);
        if (start < 0 || end < 0 || end < start) return payload;

        start += "<!--StartFragment-->".Length;
        return payload[start..end];
    }

    // ──── Interop ────

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool OpenClipboard(nint hWndNewOwner);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CloseClipboard();

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool EmptyClipboard();

    [LibraryImport("user32.dll", SetLastError = true)]
    private static partial nint SetClipboardData(uint uFormat, nint hMem);

    [LibraryImport("user32.dll", SetLastError = true)]
    private static partial nint GetClipboardData(uint uFormat);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool IsClipboardFormatAvailable(uint format);

    [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    private static partial uint RegisterClipboardFormatW(string lpszFormat);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial nint GlobalAlloc(uint uFlags, nuint dwBytes);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial nint GlobalFree(nint hMem);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial nint GlobalLock(nint hMem);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GlobalUnlock(nint hMem);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial nuint GlobalSize(nint hMem);
}
