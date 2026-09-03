using System.Runtime.InteropServices;
using FlaUI.Core.AutomationElements;

namespace HultUI.UiTests;

/// <summary>
/// Drives a HultUI tree hosted in a WinForms control by posting Win32 messages to the
/// control's own window handle.
/// </summary>
/// <remarks>
/// Posted, never synthesised. A posted message lands in the target thread's queue and
/// is dispatched by that application's own message loop, which is the same path a real
/// keystroke takes — but it does not move the developer's mouse, does not need the
/// window to be foreground, and cannot be stolen by whatever else is on screen. That is
/// also why these tests do not use <c>SendKeys</c> or FlaUI's input helpers, both of
/// which drive the physical input queue and are flaky on a shared machine.
///
/// <para>The handle comes from UI Automation rather than from knowledge of the sample:
/// HultUI's own providers report no native handle, so the first ancestor that does is
/// the hosting control.</para>
/// </remarks>
internal static class HostedInput
{
    private const uint WM_MOUSEMOVE = 0x0200;
    private const uint WM_LBUTTONDOWN = 0x0201;
    private const uint WM_LBUTTONUP = 0x0202;
    private const uint WM_KEYDOWN = 0x0100;
    private const uint WM_KEYUP = 0x0101;
    private const uint WM_CHAR = 0x0102;

    internal const int VK_BACK = 0x08;
    internal const int VK_TAB = 0x09;

    [DllImport("user32.dll")]
    private static extern bool PostMessageW(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool ScreenToClient(IntPtr hWnd, ref NativePoint point);

    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern bool BringWindowToTop(IntPtr hWnd);

    /// <summary>
    /// Makes a window genuinely foreground, or says why the test cannot continue.
    /// </summary>
    /// <remarks>
    /// Typing works on a background window, because a posted <c>WM_CHAR</c> reaches the
    /// control's window procedure whatever the desktop thinks is active. Tab does not:
    /// it runs through WinForms' dialog-key handling, which starts from the control that
    /// holds Win32 focus — and <c>SetFocus</c> quietly fails for a thread that is not the
    /// foreground one. So a Tab test that merely showed the window would pass or fail on
    /// whether something else happened to hold the foreground.
    /// </remarks>
    internal static void EnsureForeground(IntPtr window)
    {
        for (int attempt = 0; attempt < 20; attempt++)
        {
            if (GetForegroundWindow() == window) return;
            BringWindowToTop(window);
            SetForegroundWindow(window);
            Thread.Sleep(100);
        }

        throw new InvalidOperationException(
            "Could not bring the sample window to the foreground — Windows refuses to hand it over " +
            "when another application is being interacted with. Keyboard traversal cannot be tested " +
            "from a background window; re-run without touching the machine.");
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint { public int X, Y; }

    private static IntPtr Lparam(int x, int y) => (nint)((y << 16) | (x & 0xFFFF));

    /// <summary>The nearest ancestor owning a real HWND — the hosting control.</summary>
    internal static IntPtr HostHandle(AutomationElement element)
    {
        for (var e = element; e is not null; e = e.Parent)
        {
            try
            {
                var handle = e.Properties.NativeWindowHandle.ValueOrDefault;
                if (handle != IntPtr.Zero) return handle;
            }
            catch (Exception)
            {
                // HultUI's providers do not implement NativeWindowHandle; keep walking.
            }
        }
        return IntPtr.Zero;
    }

    /// <summary>Clicks an element by posting to its host, in that host's client coordinates.</summary>
    internal static void Click(AutomationElement element)
    {
        var host = HostHandle(element);
        if (host == IntPtr.Zero) throw new InvalidOperationException("No hosting HWND above the element.");

        var rect = element.BoundingRectangle;
        var point = new NativePoint { X = (int)rect.X + 6, Y = (int)rect.Y + (int)rect.Height / 2 };
        ScreenToClient(host, ref point);

        var position = Lparam(point.X, point.Y);
        PostMessageW(host, WM_MOUSEMOVE, IntPtr.Zero, position);
        PostMessageW(host, WM_LBUTTONDOWN, 1, position);
        PostMessageW(host, WM_LBUTTONUP, IntPtr.Zero, position);
    }

    /// <summary>Types text into whatever the hosted tree has focused.</summary>
    internal static void Type(IntPtr host, string text)
    {
        foreach (char c in text) PostMessageW(host, WM_CHAR, c, IntPtr.Zero);
    }

    /// <summary>Presses and releases a virtual key.</summary>
    internal static void Press(IntPtr host, int virtualKey)
    {
        PostMessageW(host, WM_KEYDOWN, virtualKey, IntPtr.Zero);
        PostMessageW(host, WM_KEYUP, virtualKey, IntPtr.Zero);
    }

    /// <summary>
    /// An element's text, whichever way its provider offers it. HultUI text inputs
    /// expose a Value pattern; plain text elements carry it as the Name.
    /// </summary>
    internal static string Read(AutomationElement element)
    {
        try
        {
            if (element.Patterns.Value.TryGetPattern(out var value))
                return value.Value.ValueOrDefault ?? "";
        }
        catch (Exception) { /* fall through to Name */ }

        try { return element.Name ?? ""; }
        catch (Exception) { return ""; }
    }
}
