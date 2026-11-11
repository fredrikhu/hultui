using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using HultUI.Core.Builders;
using HultUI.Core.Elements;
using HultUI.Core.Platform;
using HultUI.Core.Primitives;
using HultUI.Core.Styling;
using HultUI.Platform.SDL3;
using Xunit;
using Xunit.Abstractions;

namespace HultUI.UiTests;

/// <summary>
/// The custom frame keeps <c>WS_SYSMENU</c> off for the window's whole life. With it, Windows 11
/// checks the Snap Layouts flyout against the standard caption band (45 px from the window's top
/// at 150 %), shorter than a 40-dip maximize button, so a pointer that enters the button from
/// below misses the flyout until about a third of the way in. Install strips the style, but SDL
/// writes it back whenever it recomputes the window's style — on every change that leaves
/// maximized or minimized: a restore, full screen from maximized, a placement that moves a
/// maximized window — and the window kept it (found by hand on 2026-11-08, Meridian and Vesper;
/// plan 17, round 9, step 16). Each change below reads the style back: never <c>WS_SYSMENU</c> on
/// the custom frame, always on the default one; the caption and the maximize button hit-test as
/// before; and Alt+Space and a caption right-click still open the system menu, which needs the
/// style lent for the menu and taken back after it.
/// </summary>
[Collection("ui")] // one Application per process, and SDL is process-global
public sealed unsafe class CustomFrameSystemMenuTests(ITestOutputHelper output)
{
    public static readonly TheoryData<WindowStyle> Styles = new() { WindowStyle.Borderless, WindowStyle.Default };

    private const int GWL_STYLE = -16;
    private const long WS_SYSMENU = 0x00080000;
    private const uint WM_NCHITTEST = 0x0084;
    private const int HTCAPTION = 2;
    private const int HTMAXBUTTON = 9;

    private const float TitleBarDip = 40;
    private const float ButtonDip = 46;

    [UiTheory]
    [MemberData(nameof(Styles))]
    public void EveryWindowStateChange_LeavesTheSystemMenuStyleAsTheFrameNeedsIt(WindowStyle style)
    {
        using var guard = ForegroundGuard.Begin();
        bool custom = style == WindowStyle.Borderless;
        var areas = Monitors.WorkAreas();
        var area = areas[0];
        var normal = new WindowPlacement((int)area.X + 80, (int)area.Y + 80, 800, 600, false);

        using var app = new Application(new SdlWindowingPlatform(), "system menu", 640, 480, style);
        app.SetRoot(TitleBarRoot());
        var window = app.PrimaryWindow;
        var hwnd = window.NativeHandle;
        window.Placement = normal;
        app.RunFrame(TimeSpan.FromMilliseconds(16));   // the first frame shows it

        var wrong = new List<string>();
        void Check(string name)
        {
            long s = GetWindowLongPtrW(hwnd, GWL_STYLE);
            bool sysMenu = (s & WS_SYSMENU) != 0;
            output.WriteLine($"{style} {name}: style {s:X8}{(sysMenu ? " WS_SYSMENU" : "")}");
            if (sysMenu == custom)
                wrong.Add($"{name}: style {s:X8} {(custom ? "has" : "lost")} WS_SYSMENU");
            if (custom && !window.IsFullscreen && !IsIconic(hwnd))
                CheckHitTests(window, name, wrong);
        }
        void Do(string name, Action change)
        {
            change();
            // Posted on the custom frame (WM_SYSCOMMAND): the frame's pump dispatches it.
            app.RunFrame(TimeSpan.Zero, forceRender: true);
            Check(name);
        }

        Check("shown");
        Do("maximize", window.Maximize);
        Do("restore from maximized", window.Restore);
        Do("minimize", window.Minimize);
        Do("restore from minimized", window.Restore);
        Do("full screen", () => window.SetFullscreen(true));
        Do("leave full screen", () => window.SetFullscreen(false));
        Do("maximize again", window.Maximize);
        Do("full screen from maximized", () => window.SetFullscreen(true));
        Do("leave full screen, maximized", () => window.SetFullscreen(false));
        Do("restore again", window.Restore);
        if (areas.Count > 1)
        {
            var other = areas[1];
            var there = new WindowPlacement((int)other.X + 60, (int)other.Y + 60, 800, 600, false);
            Do("move to another monitor", () => window.Placement = there);
            Do("maximized on that monitor", () => window.Placement = there with { Maximized = true });
            Do("back, maximized", () => window.Placement = normal with { Maximized = true });
            Do("restore on the first monitor", window.Restore);
        }
        else output.WriteLine("one monitor: the monitor moves were not run");

        if (custom)
        {
            // Alt+Space and a caption right-click, as the window gets them.
            CheckSystemMenu(app, window, "Alt+Space", WM_SYSKEYDOWN, VK_SPACE, 0x20000001, wrong);
            CheckSystemMenu(app, window, "caption right-click", WM_NCRBUTTONUP, HTCAPTION, 0, wrong);
        }

        window.Close();
        Assert.True(wrong.Count == 0, $"{style}:{Environment.NewLine}{string.Join(Environment.NewLine, wrong)}");
    }

    /// <summary>A 40-dip title bar that drags the window, with a maximize button and a close-sized
    /// box at its right end, over a filled body.</summary>
    private static Element TitleBarRoot() =>
        Box.New().FlexGrow(1).Background(new Color(40, 80, 120)).Children(
            Flex.Row().Height(TitleBarDip).WindowDrag().Background(new Color(30, 30, 30)).Children(
                Box.New().FlexGrow(1),
                Box.New().Width(ButtonDip).Height(TitleBarDip).Background(new Color(60, 60, 60)).WindowMaximizeButton(),
                Box.New().Width(ButtonDip).Height(TitleBarDip)));

    /// <summary>The maximize button answers HTMAXBUTTON over its whole height — its middle and two
    /// dips above its bottom, where the standard caption band no longer reaches — and the title bar
    /// beside it answers HTCAPTION.</summary>
    private static void CheckHitTests(Window window, string name, List<string> wrong)
    {
        float width = window.LayoutWidth;
        float buttonX = width - ButtonDip - ButtonDip / 2;
        void Expect(string where, float x, float y, int expected)
        {
            int got = HitTest(window, x, y);
            if (got != expected) wrong.Add($"{name}: {where} hit-tests {got}, not {expected}");
        }
        Expect("the maximize button's middle", buttonX, TitleBarDip / 2, HTMAXBUTTON);
        Expect("the maximize button's bottom", buttonX, TitleBarDip - 2, HTMAXBUTTON);
        Expect("the title bar", 100, TitleBarDip / 2, HTCAPTION);
    }

    private static int HitTest(Window window, float xDip, float yDip)
    {
        var hwnd = window.NativeHandle;
        var pt = new POINT { X = (int)(xDip * window.DisplayScale), Y = (int)(yDip * window.DisplayScale) };
        ClientToScreen(hwnd, ref pt);
        nint lParam = (nint)(((pt.Y & 0xFFFF) << 16) | (pt.X & 0xFFFF));
        return (int)SendMessageW(hwnd, WM_NCHITTEST, 0, lParam);
    }

    // ──── The system menu ────

    private const uint WM_SYSKEYDOWN = 0x0104;
    private const uint WM_NCRBUTTONUP = 0x00A5;
    private const int VK_SPACE = 0x20;
    private const uint GUI_INMENUMODE = 0x0004;
    private const uint GUI_POPUPMENUMODE = 0x0010;
    private const nuint MenuTimer = 0x5150;

    private static nint s_menuWindow;
    private static bool s_menuSeen;
    private static long s_styleInMenu;
    private static bool s_systemMenuInMenu;

    /// <summary>
    /// Posts the message that opens the system menu and, from a timer the menu's modal loop
    /// dispatches, reads the style and the system menu while it is open, then closes it. The style
    /// is lent for the menu — <c>GetSystemMenu</c> answers nothing without it — and gone after.
    /// </summary>
    private void CheckSystemMenu(Application app, Window window, string name, uint message, int wParam, nint lParam, List<string> wrong)
    {
        var hwnd = window.NativeHandle;
        s_menuWindow = hwnd;
        s_menuSeen = false;
        s_styleInMenu = 0;
        s_systemMenuInMenu = false;
        delegate* unmanaged[Stdcall]<nint, uint, nuint, uint, void> tick = &OnMenuTimer;
        Assert.True(SetTimer(hwnd, MenuTimer, 10, (nint)tick) != 0, "SetTimer failed");
        try
        {
            if (message == WM_NCRBUTTONUP)
            {
                var pt = new POINT { X = 100, Y = (int)(TitleBarDip / 2 * window.DisplayScale) };
                ClientToScreen(hwnd, ref pt);
                lParam = (nint)(((pt.Y & 0xFFFF) << 16) | (pt.X & 0xFFFF));
            }
            PostMessageW(hwnd, message, (nuint)wParam, lParam);
            for (int i = 0; i < 20 && !s_menuSeen; i++)
                app.RunFrame(TimeSpan.FromMilliseconds(10), forceRender: true);
        }
        finally
        {
            KillTimer(hwnd, MenuTimer);
        }

        long after = GetWindowLongPtrW(hwnd, GWL_STYLE);
        output.WriteLine($"{name}: menu seen {s_menuSeen}, style in the menu {s_styleInMenu:X8}, after {after:X8}");
        if (!s_menuSeen) wrong.Add($"{name}: no system menu opened");
        else
        {
            if ((s_styleInMenu & WS_SYSMENU) == 0) wrong.Add($"{name}: the style was not lent for the menu ({s_styleInMenu:X8})");
            if (!s_systemMenuInMenu) wrong.Add($"{name}: GetSystemMenu answered nothing in the menu");
        }
        if ((after & WS_SYSMENU) != 0) wrong.Add($"{name}: WS_SYSMENU stayed after the menu ({after:X8})");
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static void OnMenuTimer(nint hwnd, uint msg, nuint id, uint time)
    {
        var info = new GUITHREADINFO { cbSize = (uint)sizeof(GUITHREADINFO) };
        if (!GetGUIThreadInfo(GetCurrentThreadId(), &info)) return;
        if ((info.flags & (GUI_INMENUMODE | GUI_POPUPMENUMODE)) == 0 || info.hwndMenuOwner != s_menuWindow) return;
        s_styleInMenu = GetWindowLongPtrW(s_menuWindow, GWL_STYLE);
        s_systemMenuInMenu = GetSystemMenu(s_menuWindow, false) != 0;
        s_menuSeen = true;
        EndMenu();
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X, Y; }

    [StructLayout(LayoutKind.Sequential)]
    private struct GUITHREADINFO
    {
        public uint cbSize, flags;
        public nint hwndActive, hwndFocus, hwndCapture, hwndMenuOwner, hwndMoveSize, hwndCaret;
        public RECT rcCaret;
    }

    [DllImport("user32")] private static extern long GetWindowLongPtrW(nint hwnd, int index);
    [DllImport("user32")] private static extern bool ClientToScreen(nint hwnd, ref POINT point);
    [DllImport("user32")] private static extern nint SendMessageW(nint hwnd, uint msg, nuint wParam, nint lParam);
    [DllImport("user32")] private static extern bool PostMessageW(nint hwnd, uint msg, nuint wParam, nint lParam);
    [DllImport("user32")] private static extern bool IsIconic(nint hwnd);
    [DllImport("user32")] private static extern nuint SetTimer(nint hwnd, nuint id, uint elapse, nint proc);
    [DllImport("user32")] private static extern bool KillTimer(nint hwnd, nuint id);
    [DllImport("user32")] private static extern bool GetGUIThreadInfo(uint thread, GUITHREADINFO* info);
    [DllImport("user32")] private static extern nint GetSystemMenu(nint hwnd, bool revert);
    [DllImport("user32")] private static extern bool EndMenu();
    [DllImport("kernel32")] private static extern uint GetCurrentThreadId();
}
