using HultUI.Core.Animation;
using HultUI.Core.Builders;
using HultUI.Core.Primitives;
using Xunit;

namespace HultUI.Headless.Tests;

public class HeadlessSessionTests
{
    [Fact]
    public void CreatePumpDispose_LeavesNoStateBehind()
    {
        // Two sequential sessions must not observe each other. The second
        // create would throw (or leak tweens/windows) if the first left
        // Application.Current, animations, or the sync context dirty.
        for (int i = 0; i < 2; i++)
        {
            using var session = HeadlessSession.Create(width: 400, height: 300);
            session.Window.SetRoot(Box.New().Background(Color.Blue));
            Assert.True(session.PumpFrame());
        }
        Assert.False(AnimationManager.HasActiveAnimations);
    }

    [Fact]
    public void Created_IsRaisedForEachNewSession_OnTheCreatingThread()
    {
        var seen = new List<(HeadlessSession Session, int Thread)>();
        void Record(HeadlessSession s) { lock (seen) seen.Add((s, Environment.CurrentManagedThreadId)); }
        HeadlessSession.Created += Record;
        try
        {
            using var session = HeadlessSession.Create(width: 200, height: 100);
            lock (seen) Assert.Contains((session, Environment.CurrentManagedThreadId), seen);
        }
        finally { HeadlessSession.Created -= Record; }
    }

    [Fact]
    public void PumpFrame_RespectsDirtyTracking()
    {
        using var session = HeadlessSession.Create();
        session.Window.SetRoot(Box.New().Background(Color.Red));

        Assert.True(session.PumpFrame());   // first frame: root dirty
        Assert.False(session.PumpFrame());  // idle UI: nothing to render
        Assert.True(session.PumpFrame(session.DefaultFrameDelta, forceRender: true));
    }

    [Fact]
    public void PumpFrame_DrainsInvokeOnMainThread()
    {
        using var session = HeadlessSession.Create();
        session.Window.SetRoot(Box.New());

        bool ran = false;
        Application.InvokeOnMainThread(() => ran = true);
        Assert.False(ran);

        session.PumpFrame();
        Assert.True(ran);
    }

    [Fact]
    public void PumpUntilCompleted_ResumesAwaitContinuationOnPump()
    {
        using var session = HeadlessSession.Create();
        session.Window.SetRoot(Box.New());

        bool continuationRan = false;
        async Task WorkAsync()
        {
            await Task.Delay(10);
            // Resumes via the UI-thread sync context → drained by the pump.
            continuationRan = true;
        }

        var task = WorkAsync();
        session.PumpUntilCompleted(task);
        Assert.True(continuationRan);
    }

    [Fact]
    public void AnimationCompletesAfterExactVirtualDuration()
    {
        using var session = HeadlessSession.Create();
        session.Window.SetRoot(Box.New());
        session.PumpFrame();

        float value = -1f;
        Animate.Value(0f, 100f, v => value = v).Duration(1f).Ease(Easing.Linear).Start();

        // 0.5 is exactly representable in binary float, so two half-second
        // pumps accumulate to exactly the 1 s duration — no wall clock, no
        // float drift. Machine speed is irrelevant.
        session.PumpFrame(TimeSpan.FromSeconds(0.5));
        Assert.Equal(50f, value);

        session.PumpFrame(TimeSpan.FromSeconds(0.5));
        Assert.Equal(100f, value);
        Assert.False(AnimationManager.HasActiveAnimations);
    }

    [Fact]
    public void PumpUntil_TimesOutOnImpossibleCondition()
    {
        using var session = HeadlessSession.Create();
        session.Window.SetRoot(Box.New());

        Assert.Throws<TimeoutException>(
            () => session.PumpUntil(() => false, TimeSpan.FromMilliseconds(100)));
    }

    [Fact]
    public void SetClientSize_RelayoutsImmediately()
    {
        using var session = HeadlessSession.Create(width: 400, height: 300);
        // A box that fills the window.
        var box = Box.New().Background(Color.Green);
        session.Window.SetRoot(box);
        session.PumpFrame();

        bool resizedFired = false;
        session.Window.Window.Resized += () => resizedFired = true;

        session.Window.SetClientSize(640, 480);
        Assert.True(resizedFired);
        Assert.Equal(640f, session.Window.Window.LayoutWidth);
        Assert.Equal(480f, session.Window.Window.LayoutHeight);

        // The next pump renders the new size.
        Assert.True(session.PumpFrame());
    }

    [Fact]
    public void DisplayScale_RendersAtScaledPixelSize()
    {
        using var session = HeadlessSession.Create(width: 100, height: 100, scale: 2f);
        session.Window.SetRoot(Box.New().Background(Color.Red));
        session.PumpFrame();

        using var image = session.Window.CapturePixels();
        Assert.Equal(200, image.Width);
        Assert.Equal(200, image.Height);
    }

    [Fact]
    public void CapturePixels_ShowsRenderedContent()
    {
        using var session = HeadlessSession.Create(width: 50, height: 50);
        session.Window.SetRoot(Box.New().Background(Color.Red));
        session.PumpFrame();

        using var image = session.Window.CapturePixels();
        using var bitmap = SkiaSharp.SKBitmap.FromImage(image);
        var center = bitmap.GetPixel(25, 25);
        Assert.Equal(255, center.Red);
        Assert.Equal(0, center.Green);
        Assert.Equal(0, center.Blue);
    }

    [Fact]
    public void PostFrameCallback_RunsAfterNextPump()
    {
        using var session = HeadlessSession.Create();
        session.Window.SetRoot(Box.New());
        session.PumpFrame();

        bool ran = false;
        session.Window.Window.RequestPostFrameCallback(() => ran = true);
        Assert.False(ran);

        // Registering a post-frame callback forces a frame even on an idle UI.
        Assert.True(session.PumpFrame());
        Assert.True(ran);
    }

    [Fact]
    public void MultiWindow_PumpRendersBoth()
    {
        using var session = HeadlessSession.Create(width: 200, height: 200);
        var second = session.CreateWindow("second", 100, 100);

        session.Window.SetRoot(Box.New().Background(Color.Red));
        second.SetRoot(Box.New().Background(Color.Blue));

        Assert.True(session.PumpFrame());

        using var a = session.Window.CapturePixels();
        using var b = second.CapturePixels();
        Assert.Equal(200, a.Width);
        Assert.Equal(100, b.Width);
    }

    [Fact]
    public void NoSdlNativeLibraryLoadedHeadless()
    {
        using var session = HeadlessSession.Create();
        session.Window.SetRoot(Box.New());
        session.PumpFrame();

        // Exact native library names — the managed HultUI.Platform.SDL3.dll
        // assembly also appears in the module list and must not match.
        bool sdlLoaded = System.Diagnostics.Process.GetCurrentProcess().Modules
            .Cast<System.Diagnostics.ProcessModule>()
            .Any(m => m.ModuleName.Equals("SDL3.dll", StringComparison.OrdinalIgnoreCase)
                   || m.ModuleName.StartsWith("libSDL3", StringComparison.OrdinalIgnoreCase));
        Assert.False(sdlLoaded, "A headless session must not load the SDL3 native library.");
    }
}
