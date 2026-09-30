using System.Runtime.InteropServices;
using HultUI.WebView.WebView2;
using Xunit;

namespace HultUI.WebView.Engines.Tests;

/// <summary>
/// <see cref="ParentWindowWatch"/> against a real window: a page is told when the window it
/// is drawn in moves, and only then. The browser keeps the page's screen position from the
/// last time it was told, so a missed move leaves its popups and every rectangle it reports
/// to a screen reader where the window used to be.
/// </summary>
public class ParentWindowWatchTests
{
    private const uint SWP_NOSIZE = 0x0001, SWP_NOMOVE = 0x0002, SWP_NOZORDER = 0x0004, SWP_NOACTIVATE = 0x0010;

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateWindowExW(int exStyle, string cls, string? name, int style,
        int x, int y, int w, int h, IntPtr parent, IntPtr menu, IntPtr inst, IntPtr param);

    [DllImport("user32.dll")] private static extern bool DestroyWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, uint flags);

    /// <summary>A hidden popup of a system class, owned by the calling thread — which is what may subclass it.</summary>
    private static IntPtr Window()
    {
        var window = CreateWindowExW(0, "STATIC", null, unchecked((int)0x80000000) /* WS_POPUP */,
            10, 10, 50, 50, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
        Assert.NotEqual(IntPtr.Zero, window);
        return window;
    }

    private static void MoveTo(IntPtr window, int x, int y) =>
        SetWindowPos(window, IntPtr.Zero, x, y, 0, 0, SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE);

    private static void Resize(IntPtr window, int width, int height) =>
        SetWindowPos(window, IntPtr.Zero, 0, 0, width, height, SWP_NOMOVE | SWP_NOZORDER | SWP_NOACTIVATE);

    [Fact]
    public void AMove_IsReported_AndAResizeInPlaceIsNot()
    {
        if (!OperatingSystem.IsWindows()) return;
        var window = Window();
        int moves = 0;
        using var watch = ParentWindowWatch.Watch(window, () => moves++);
        try
        {
            MoveTo(window, 40, 60);
            Assert.Equal(1, moves);

            // The page's bounds say where it is in the window; a new size is theirs to carry.
            Resize(window, 80, 80);
            Assert.Equal(1, moves);

            MoveTo(window, 45, 60);
            Assert.Equal(2, moves);
        }
        finally { DestroyWindow(window); }
    }

    [Fact]
    public void EachPageInAWindow_IsTold_UntilItStopsWatching()
    {
        if (!OperatingSystem.IsWindows()) return;
        var window = Window();
        int first = 0, second = 0;
        var a = ParentWindowWatch.Watch(window, () => first++);
        var b = ParentWindowWatch.Watch(window, () => second++);
        try
        {
            MoveTo(window, 40, 60);
            Assert.Equal((1, 1), (first, second));

            a.Dispose();
            MoveTo(window, 50, 60);
            Assert.Equal((1, 2), (first, second));

            // The last one out removes the hook; the window goes on working, unwatched.
            b.Dispose();
            MoveTo(window, 60, 60);
            Assert.Equal((1, 2), (first, second));

            // And a page that comes later is told again.
            using var c = ParentWindowWatch.Watch(window, () => first++);
            MoveTo(window, 70, 60);
            Assert.Equal((2, 2), (first, second));
        }
        finally { DestroyWindow(window); }
    }

    [Fact]
    public void AWindowDestroyedWhileWatched_LeavesNothingBehind()
    {
        if (!OperatingSystem.IsWindows()) return;
        var window = Window();
        int moves = 0;
        var watch = ParentWindowWatch.Watch(window, () => moves++);

        DestroyWindow(window);
        watch.Dispose(); // after the window is gone: must not throw, or touch a dead handle's hook

        // Windows reuses handles. A new window must start unwatched.
        var next = Window();
        try
        {
            MoveTo(next, 40, 60);
            Assert.Equal(0, moves);
        }
        finally { DestroyWindow(next); }
    }

    [Fact]
    public void AThrowingWatcher_DoesNotReachTheWindowProcedure()
    {
        if (!OperatingSystem.IsWindows()) return;
        var window = Window();
        using var watch = ParentWindowWatch.Watch(window, () => throw new InvalidOperationException("from a page"));
        try
        {
            // An exception crossing into user32 would take the process down, not fail a test.
            MoveTo(window, 40, 60);
        }
        finally { DestroyWindow(window); }
    }
}
