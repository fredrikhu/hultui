using System.Reflection;
using System.Runtime.InteropServices;
using HultUI.Core.Builders;
using HultUI.Core.Elements;
using HultUI.Core.Primitives;
using HultUI.Core.Rendering;
using HultUI.Platform.SDL3;
using SDL;
using Xunit;
using Xunit.Abstractions;
using static SDL.SDL3;

namespace HultUI.UiTests;

/// <summary>
/// The frames a real window draws from SDL's event watch, the one place that runs while Win32
/// holds the thread in its modal size/move loop. Every RESIZED, PIXEL_SIZE_CHANGED and EXPOSED
/// drew a full frame there, relaid out and raised Resized: a SetWindowPos step raises the first
/// two for the same new size, and growing adds a WM_PAINT for the area it exposed, so a step
/// drew three frames at one size; and SDL's live-resize timer sent an EXPOSED every ~10 ms while
/// an edge was held still, each a frame of nothing new (plan 17, round 2,
/// gl-vk-resize-duplicate-frames). The frame at a new size must still always draw — nothing
/// comes round again inside the modal loop (docs/design/HDR.md).
/// </summary>
[Collection("ui")]
public sealed class LiveResizeFrameTests(ITestOutputHelper output)
{
    private const uint NoMove = 0x0002, NoZOrder = 0x0004, NoActivate = 0x0010;

    [DllImport("user32.dll")] private static extern bool SetWindowPos(nint hwnd, nint after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll")] private static extern bool InvalidateRect(nint hwnd, nint rect, bool erase);
    [DllImport("user32.dll")] private static extern bool UpdateWindow(nint hwnd);
    [DllImport("user32.dll")] private static extern bool GetClientRect(nint hwnd, out Rect32 rect);

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect32 { public int Left, Top, Right, Bottom; }

    private sealed class Counter
    {
        public int Frames;
        public readonly List<(int W, int H)> Sizes = [];
    }

    private static (Application App, nint Hwnd, Counter Counter) Open(Func<Element, Element> scene)
    {
        SDL_SetHint(SDL_HINT_WINDOW_ACTIVATE_WHEN_SHOWN, "0");   // never take the foreground
        // No accessibility bridge: with a client listening (one always is on a Windows desktop)
        // a layout change asks for a coalesced refresh frame 100 ms later, which may land inside
        // a step and is not what these count.
        string? a11y = Environment.GetEnvironmentVariable("HULTUI_A11Y");
        Environment.SetEnvironmentVariable("HULTUI_A11Y", "0");
        Application app;
        try { app = new Application(new SdlWindowingPlatform(), "live resize", 400, 300); }
        finally { Environment.SetEnvironmentVariable("HULTUI_A11Y", a11y); }
        var counter = new Counter();
        nint hwnd = app.PrimaryWindow.NativeHandle;
        app.SetRoot(scene(Canvas.New(_ =>
        {
            counter.Frames++;
            GetClientRect(hwnd, out var r);
            counter.Sizes.Add((r.Right - r.Left, r.Bottom - r.Top));
        }).Width(1f).Height(1f)));
        for (int i = 0; i < 3; i++) app.RunFrame(TimeSpan.FromMilliseconds(16), forceRender: true);   // shown
        // Settled: nothing left over from the first frames.
        var settle = System.Diagnostics.Stopwatch.StartNew();
        while (settle.ElapsedMilliseconds < 400) { app.RunFrame(TimeSpan.FromMilliseconds(16)); Thread.Sleep(10); }
        return (app, hwnd, counter);
    }

    private static Element Plain(Element counter) =>
        Flex.Column().FlexGrow(1).Background(Color.FromHex("#202020")).Children(
            Box.New().Width(120f).Height(40f).Background(Color.FromHex("#5b8def")), counter);

    [UiFact]
    public void EachStepOfAResize_DrawsOneFrame_AtItsNewSize()
    {
        var (app, hwnd, counter) = Open(Plain);
        using var _ = app;
        GetClientRect(hwnd, out var start);
        int width = start.Right - start.Left, height = start.Bottom - start.Top;

        // Growing steps (each exposes new area, which Windows asks to have painted), then shrinking.
        const int steps = 16;
        int inside = 0, after = 0, wrongSize = 0, stepsWithout = 0;
        for (int i = 0; i < steps; i++)
        {
            int w = i < steps / 2 ? 400 + 24 * (i + 1) : 400 + 24 * (steps - 1 - i);
            int h = i < steps / 2 ? 300 + 16 * (i + 1) : 300 + 16 * (steps - 1 - i);
            int before = counter.Frames, sized = counter.Sizes.Count;
            SetWindowPos(hwnd, 0, 0, 0, w, h, NoMove | NoZOrder | NoActivate);
            int drawn = counter.Frames - before;
            inside += drawn;
            if (drawn == 0) stepsWithout++;
            GetClientRect(hwnd, out var now);
            for (int k = sized; k < counter.Sizes.Count; k++)
                if (counter.Sizes[k] != (now.Right - now.Left, now.Bottom - now.Top)) wrongSize++;

            int settled = counter.Frames;
            for (int f = 0; f < 3; f++) app.RunFrame(TimeSpan.FromMilliseconds(16));
            after += counter.Frames - settled;
        }

        output.WriteLine($"{inside} frames inside {steps} SetWindowPos steps ({stepsWithout} without one), " +
            $"{wrongSize} at an old size, {after} in the loop passes after them");
        // The frame at the new size, drawn before SetWindowPos returns, every time.
        Assert.Equal(0, stepsWithout);
        Assert.Equal(0, wrongSize);
        // And only that one: not one more for PIXEL_SIZE_CHANGED, nor for WM_PAINT.
        Assert.True(inside == steps, $"{inside} frames inside {steps} steps");
        Assert.True(after == 0, $"{after} frames in the loop passes after {steps} steps");
    }

    [UiFact]
    public void AWindowWindowsAsksToRepaint_IsDrawn()
    {
        var (app, hwnd, counter) = Open(Plain);
        using var _ = app;
        const int times = 8;
        int before = counter.Frames;
        for (int i = 0; i < times; i++)
        {
            // WM_PAINT, sent now: SDL's EXPOSED with data1 0.
            InvalidateRect(hwnd, 0, false);
            UpdateWindow(hwnd);
        }
        int drawn = counter.Frames - before;
        output.WriteLine($"{drawn} frames for {times} repaints");
        Assert.True(drawn == times, $"{drawn} frames for {times} repaints");
    }

    [UiFact]
    public void TheLiveResizeTimer_DrawsOnlyAWindowWithSomethingNew()
    {
        // SDL's live-resize timer: EXPOSED with data1 1, every ~10 ms while Win32 runs its modal
        // size/move loop — a held edge or a window being moved. Pushed by hand here: a drag
        // cannot be made without the mouse. SDL_PushEvent runs the event watch, as SDL's own does.
        BoxElement? box = null;
        var (app, hwnd, counter) = Open(c =>
        {
            box = Box.New().Width(120f).Height(40f).Background(Color.FromHex("#5b8def"));
            return Flex.Column().FlexGrow(1).Background(Color.FromHex("#202020")).Children(box, c);
        });
        using var _ = app;
        var window = (SDL3Window)typeof(Window).GetProperty("PlatformWindow", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(app.PrimaryWindow)!;

        int Tick(int times, Action? between = null)
        {
            int before = counter.Frames;
            for (int i = 0; i < times; i++)
            {
                between?.Invoke();
                PushLiveResizeExpose(window.WindowId);
            }
            return counter.Frames - before;
        }

        // Nothing changed: nothing to draw.
        int idle = Tick(10);
        // Something changed meanwhile.
        int dirty = Tick(10, () => box!.MarkPaintDirty());
        // Work posted from another thread is waiting, which the held loop cannot run: a playing
        // video's next frame, a web page's. A draw takes the newest picture from a source that
        // renders in it (mpv's render thread is the UI thread).
        bool ran = false;
        Application.InvokeOnMainThread(() => ran = true);
        int posted = Tick(10);
        app.RunFrame(TimeSpan.FromMilliseconds(16));
        int afterDrain = Tick(10);

        output.WriteLine($"timer ticks drew: idle {idle}, after a change {dirty}, with a post waiting {posted}, " +
            $"after it ran {afterDrain} (of 10 each)");
        Assert.True(ran);
        Assert.Equal(0, idle);
        Assert.Equal(10, dirty);
        Assert.Equal(10, posted);
        Assert.Equal(0, afterDrain);
    }

    private static unsafe void PushLiveResizeExpose(SDL_WindowID window)
    {
        SDL_Event evt = default;
        evt.window.type = SDL_EventType.SDL_EVENT_WINDOW_EXPOSED;
        evt.window.windowID = window;
        evt.window.data1 = 1;
        SDL_PushEvent(&evt);
    }
}
