using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using HultUI.WebView.WebView2.Interop;

namespace HultUI.WebView.WebView2;

/// <summary>
/// Tells a page when the window it is drawn in moves on screen.
/// </summary>
/// <remarks>
/// <para>A composited page is drawn by the app, but the browser still has to know where on
/// screen it is: it opens its own popups there (a <c>&lt;select&gt;</c>'s list, a date
/// picker), places the input method's candidates, and reports every element's rectangle to
/// assistive technology in screen coordinates. It learns the page's place in the window from
/// its bounds, and the window's place on screen only when told
/// (<c>NotifyParentWindowPositionChanged</c>). HultUI has no moved event — nothing it draws
/// itself depends on where the window is — so the engine watches the window directly.</para>
/// <para>A comctl32 subclass, which stacks with any other hook on the window and unhooks in
/// any order. The window's own thread installs and removes it, which is where a view is made
/// and disposed.</para>
/// </remarks>
internal static unsafe class ParentWindowWatch
{
    private const uint WM_NCDESTROY = 0x0082, WM_WINDOWPOSCHANGED = 0x0047;
    private const uint SWP_NOMOVE = 0x0002;
    private const nuint SubclassId = 0x48554C57; // "HULW"

    private static readonly object Gate = new();
    private static readonly Dictionary<nint, Action[]> Watchers = [];

    /// <summary>Calls <paramref name="moved"/> on the window's thread each time it changes position. Dispose to stop.</summary>
    public static IDisposable Watch(nint window, Action moved)
    {
        lock (Gate)
        {
            if (Watchers.TryGetValue(window, out var existing))
                Watchers[window] = [.. existing, moved];
            else if (Native.SetWindowSubclass(window, &Proc, SubclassId, 0))
                Watchers[window] = [moved];
            // A window that cannot be subclassed (another thread's) goes unwatched: the page
            // still works, and is told its position whenever its bounds change.
        }
        return new Registration(window, moved);
    }

    private static void Remove(nint window, Action moved)
    {
        lock (Gate)
        {
            if (!Watchers.TryGetValue(window, out var existing)) return;
            Action[] rest = [.. existing.Where(a => !ReferenceEquals(a, moved))];
            if (rest.Length > 0) { Watchers[window] = rest; return; }
            Watchers.Remove(window);
            Native.RemoveWindowSubclass(window, &Proc, SubclassId);
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static nint Proc(nint hwnd, uint message, nint wParam, nint lParam, nuint id, nuint data)
    {
        try
        {
            if (message == WM_WINDOWPOSCHANGED && (((Native.WINDOWPOS*)lParam)->Flags & SWP_NOMOVE) == 0)
            {
                Action[]? watchers;
                lock (Gate) Watchers.TryGetValue(hwnd, out watchers);
                if (watchers is not null)
                    foreach (var moved in watchers) moved();
            }
            else if (message == WM_NCDESTROY)
            {
                lock (Gate) Watchers.Remove(hwnd);
                Native.RemoveWindowSubclass(hwnd, &Proc, SubclassId);
            }
        }
        catch
        {
            // Nothing may escape into the window procedure; a page told late is only late.
        }
        return Native.DefSubclassProc(hwnd, message, wParam, lParam);
    }

    private sealed class Registration(nint window, Action moved) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0) Remove(window, moved);
        }
    }
}
