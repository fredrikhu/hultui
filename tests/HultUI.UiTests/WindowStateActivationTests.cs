using System.Runtime.InteropServices;
using HultUI.Core.Builders;
using HultUI.Core.Platform;
using HultUI.Core.Primitives;
using HultUI.Core.Styling;
using HultUI.Platform.SDL3;
using Xunit;
using Xunit.Abstractions;

namespace HultUI.UiTests;

/// <summary>
/// The framework's own window-state changes on a real window, both frames: maximize, restore,
/// full screen (entered from maximized, so it restores first and maximizes again on the way out),
/// a half-screen placement as a snap would leave it, and a maximized placement.
/// <para>SDL honours <c>SDL_HINT_WINDOW_ACTIVATE_WHEN_SHOWN=0</c> only when it shows a window;
/// maximize and restore are <c>ShowWindow</c> commands that activate, so a probe or a test that
/// set the hint still had its window asking for the foreground at each of them (plan 17, round 4).
/// With the hint, none of these may ask: the framework refuses the activation itself, and the
/// guard's hook, further down the chain, sees nothing. Without the hint — every real app — the
/// framework refuses nothing and the window asks as it always did; the guard refuses those.</para>
/// </summary>
[Collection("ui")] // one Application per process, and SDL is process-global
public sealed class WindowStateActivationTests(ITestOutputHelper output)
{
    public static readonly TheoryData<WindowStyle> Styles = new() { WindowStyle.Borderless, WindowStyle.Default };

    [UiTheory]
    [MemberData(nameof(Styles))]
    public void WithTheHint_MaximizeRestoreFullScreenAndPlacement_AskForNoActivation(WindowStyle style)
    {
        using var guard = ForegroundGuard.Begin();
        var steps = Run(style);

        foreach (var step in steps)
            Assert.True(step.Guard == 0, $"{style} {step.Name}: {step.Guard} activation(s) reached the guard");
        // The activations were asked for — the framework refused them. Without this the test
        // would pass for a sequence that never asked.
        Assert.True(steps.Sum(s => s.Framework) > 0, $"{style}: the framework refused nothing");
    }

    [UiTheory]
    [MemberData(nameof(Styles))]
    public void WithoutTheHint_TheSameCallsStillWork_AndAskAsBefore(WindowStyle style)
    {
        using var guard = ForegroundGuard.BeginWithoutTheHint();
        var steps = Run(style);

        foreach (var step in steps)
            Assert.True(step.Framework == 0, $"{style} {step.Name}: the framework refused {step.Framework} activation(s) without the hint");
        var maximize = steps.First(s => s.Name == "maximize");
        Assert.True(maximize.Guard > 0, $"{style}: maximize asked for no activation without the hint");
        // Full screen's moves leave activation as it was (SWP_NOACTIVATE): from a restored window
        // nothing asks, so a window that was not active is not made so.
        foreach (var step in steps.Where(s => s.Name.EndsWith(", restored", StringComparison.Ordinal)))
            Assert.True(step.Guard == 0, $"{style} {step.Name}: {step.Guard} activation(s) asked for");
    }

    private readonly record struct Step(string Name, int Guard, int Framework);

    /// <summary>Opens a window, puts it through every change and checks where each one left it.
    /// Returns the activations each change asked for: refused by the guard, and by the framework.</summary>
    private List<Step> Run(WindowStyle style)
    {
        var area = WorkAreas()[0];
        var normal = new WindowPlacement((int)area.X + 80, (int)area.Y + 80, 800, 600, false);
        var half = new WindowPlacement((int)area.X, (int)area.Y, (int)area.Width / 2, (int)area.Height, false);

        using var app = new Application(new SdlWindowingPlatform(), "window state", 640, 480, style);
        app.SetRoot(Box.New().Background(new Color(40, 80, 120)).FlexGrow(1));
        var window = app.PrimaryWindow;
        var hwnd = window.NativeHandle;
        window.Placement = normal;
        app.RunFrame(TimeSpan.FromMilliseconds(16));   // the first frame shows it
        Assert.True(IsWindowVisible(hwnd));

        var steps = new List<Step>();
        void Do(string name, Action change, Action check)
        {
            int guard = ForegroundGuard.Refused, framework = FrameworkRefused();
            change();
            // Posted on the custom frame (WM_SYSCOMMAND): the frame's pump dispatches it.
            app.RunFrame(TimeSpan.Zero, forceRender: true);
            check();
            var step = new Step(name, ForegroundGuard.Refused - guard, FrameworkRefused() - framework);
            output.WriteLine($"{style} {name}: guard refused {step.Guard}, framework refused {step.Framework}");
            steps.Add(step);
        }

        Do("maximize", window.Maximize, () =>
        {
            Assert.True(IsZoomed(hwnd), $"{style}: not maximized");
            AssertClientWidth(window, (int)area.Width);
        });
        Do("restore", window.Restore, () =>
        {
            Assert.False(IsZoomed(hwnd), $"{style}: still maximized");
            Assert.Equal(normal, OuterRect(hwnd));
        });
        Do("maximize again", window.Maximize, () => Assert.True(IsZoomed(hwnd)));
        Do("full screen", () => window.SetFullscreen(true), () =>
        {
            Assert.True(window.IsFullscreen);
            var m = MonitorRect(hwnd);
            Assert.Equal(new WindowPlacement(m.Left - 1, m.Top - 1, m.Right - m.Left + 2, m.Bottom - m.Top + 2, false), OuterRect(hwnd));
            AssertClientWidth(window, m.Right - m.Left + 2);
        });
        Do("leave full screen", () => window.SetFullscreen(false), () =>
        {
            Assert.False(window.IsFullscreen);
            Assert.True(IsZoomed(hwnd), $"{style}: full screen did not give the maximized window back");
        });
        Do("restore again", window.Restore, () => Assert.Equal(normal, OuterRect(hwnd)));
        // From a restored window full screen only moves it, over the monitor and back.
        Do("full screen, restored", () => window.SetFullscreen(true), () => Assert.True(window.IsFullscreen));
        Do("leave full screen, restored", () => window.SetFullscreen(false), () =>
        {
            Assert.False(window.IsFullscreen);
            Assert.Equal(normal, OuterRect(hwnd));
        });
        Do("half screen", () => window.Placement = half, () =>
        {
            Assert.Equal(half, OuterRect(hwnd));
            Assert.Equal(half, window.Placement);
        });
        Do("maximized placement", () => window.Placement = normal with { Maximized = true }, () =>
        {
            Assert.True(IsZoomed(hwnd));
            Assert.Equal(normal with { Maximized = true }, window.Placement);
        });
        Do("normal placement", () => window.Placement = normal, () => Assert.Equal(normal, OuterRect(hwnd)));
        AddMinimizeAndRaise(app, window, style, steps);

        window.Close();
        return steps;
    }

    /// <summary>Minimize, raise (which restores a minimized window first), and both again: a
    /// minimize after a raise asks for the window's activation inside the call, even through
    /// <c>SW_SHOWMINNOACTIVE</c>, and a raise asks for it unless SDL was told not to on a raise
    /// (plan 17, round 5).</summary>
    private void AddMinimizeAndRaise(Application app, Window window, WindowStyle style, List<Step> steps)
    {
        var hwnd = window.NativeHandle;
        void Do(string name, Action change, Action check)
        {
            int guard = ForegroundGuard.Refused, framework = FrameworkRefused();
            change();
            app.RunFrame(TimeSpan.Zero, forceRender: true);
            check();
            var step = new Step(name, ForegroundGuard.Refused - guard, FrameworkRefused() - framework);
            output.WriteLine($"{style} {name}: guard refused {step.Guard}, framework refused {step.Framework}");
            steps.Add(step);
        }

        Do("minimize", window.Minimize, () => Assert.True(IsIconic(hwnd), $"{style}: not minimized"));
        Do("raise", window.Raise, () => Assert.False(IsIconic(hwnd), $"{style}: still minimized"));
        Do("minimize after a raise", window.Minimize, () => Assert.True(IsIconic(hwnd), $"{style}: not minimized again"));
        Do("raise again", window.Raise, () => Assert.False(IsIconic(hwnd), $"{style}: still minimized"));
    }

    /// <summary>
    /// A process that tells SDL not to activate a window when it shows one, and no more — the
    /// shown hint without the raised one, as a probe sets it: its minimize and raise ask for no
    /// activation either; the framework refuses them.
    /// </summary>
    [UiTheory]
    [MemberData(nameof(Styles))]
    public void WithTheShownHintOnly_MinimizeAndRaise_AskForNoActivation(WindowStyle style)
    {
        using var guard = ForegroundGuard.Begin();
        // The guard also tells SDL not to activate on a raise; this process does not.
        SDL.SDL3.SDL_SetHintWithPriority(SDL.SDL3.SDL_HINT_WINDOW_ACTIVATE_WHEN_RAISED, "1", SDL.SDL_HintPriority.SDL_HINT_OVERRIDE);
        try
        {
            var area = WorkAreas()[0];
            using var app = new Application(new SdlWindowingPlatform(), "minimize and raise", 640, 480, style);
            app.SetRoot(Box.New().Background(new Color(40, 80, 120)).FlexGrow(1));
            var window = app.PrimaryWindow;
            window.Placement = new WindowPlacement((int)area.X + 80, (int)area.Y + 80, 800, 600, false);
            app.RunFrame(TimeSpan.FromMilliseconds(16));
            var steps = new List<Step>();
            AddMinimizeAndRaise(app, window, style, steps);
            window.Close();

            foreach (var step in steps)
                Assert.True(step.Guard == 0, $"{style} {step.Name}: {step.Guard} activation(s) reached the guard");
            Assert.True(steps.Sum(s => s.Framework) > 0, $"{style}: the framework refused nothing");
        }
        finally
        {
            SDL.SDL3.SDL_SetHintWithPriority(SDL.SDL3.SDL_HINT_WINDOW_ACTIVATE_WHEN_RAISED, "0", SDL.SDL_HintPriority.SDL_HINT_OVERRIDE);
        }
    }

    /// <summary>Activations the framework refused (<see cref="WindowsActivation"/>), on any thread.</summary>
    private static int FrameworkRefused() => WindowsActivation.Refused;

    /// <summary>The client area is as wide as asked, and the frame drawn after the change laid out at that width.</summary>
    private static void AssertClientWidth(Window window, int width)
    {
        GetClientRect(window.NativeHandle, out var client);
        Assert.Equal(width, client.Right - client.Left);
        Assert.Equal(width, (int)MathF.Round(window.LayoutWidth * window.DisplayScale));
    }

    private static WindowPlacement OuterRect(nint hwnd)
    {
        GetWindowRect(hwnd, out var r);
        return new WindowPlacement(r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top, false);
    }

    private static RECT MonitorRect(nint hwnd)
    {
        var info = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
        Assert.True(GetMonitorInfoW(MonitorFromWindow(hwnd, 2 /* MONITOR_DEFAULTTONEAREST */), ref info));
        return info.rcMonitor;
    }

    private static List<Rect> WorkAreas()
    {
        var areas = new List<Rect>();
        EnumDisplayMonitors(0, 0, (monitor, _, _, _) =>
        {
            var info = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
            if (GetMonitorInfoW(monitor, ref info))
                areas.Add(new Rect(info.rcWork.Left, info.rcWork.Top,
                    info.rcWork.Right - info.rcWork.Left, info.rcWork.Bottom - info.rcWork.Top));
            return true;
        }, 0);
        return areas;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MONITORINFO { public int cbSize; public RECT rcMonitor, rcWork; public uint dwFlags; }

    private delegate bool MonitorEnumProc(nint monitor, nint hdc, nint clip, nint data);

    [DllImport("user32")] private static extern bool EnumDisplayMonitors(nint hdc, nint clip, MonitorEnumProc proc, nint data);
    [DllImport("user32", EntryPoint = "GetMonitorInfoW")] private static extern bool GetMonitorInfoW(nint monitor, ref MONITORINFO info);
    [DllImport("user32")] private static extern nint MonitorFromWindow(nint hwnd, uint flags);
    [DllImport("user32")] private static extern bool GetWindowRect(nint hwnd, out RECT rect);
    [DllImport("user32")] private static extern bool GetClientRect(nint hwnd, out RECT rect);
    [DllImport("user32")] private static extern bool IsZoomed(nint hwnd);
    [DllImport("user32")] private static extern bool IsIconic(nint hwnd);
    [DllImport("user32")] private static extern bool IsWindowVisible(nint hwnd);
}
