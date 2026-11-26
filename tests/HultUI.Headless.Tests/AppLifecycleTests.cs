using HultUI.Core.Builders;
using HultUI.Core.Elements;
using HultUI.Core.Platform;
using HultUI.Core.Rendering;
using Xunit;
using TextB = HultUI.Core.Builders.Text;

namespace HultUI.Headless.Tests;

/// <summary>
/// The app lifecycle (docs/design/APP-LIFECYCLE.md), driven as Android drives it through SDL:
/// the session's <see cref="HeadlessSession.EnterBackground"/>,
/// <see cref="HeadlessSession.EnterForeground"/> and <see cref="HeadlessSession.SignalLowMemory"/>
/// raise the application's events at once, on the UI thread. In the background nothing is drawn
/// while posted work still runs; a return draws every window twice; a low-memory signal empties
/// HultUI's own caches before the app hears of it.
/// </summary>
public sealed class AppLifecycleTests
{
    private static (HeadlessSession Session, TextElement Label) Started()
    {
        var session = HeadlessSession.Create(width: 400, height: 300);
        var label = TextB.New("before");
        session.Window.SetRoot(Flex.Column(gap: 8).Children(
            label,
            TextB.New("A paragraph of ordinary words, shaped once and kept in the word cache.")));
        Assert.True(session.PumpFrame(), "the first frame drew nothing");
        return (session, label);
    }

    [Fact]
    public void ASession_StartsInTheForeground()
    {
        using var session = HeadlessSession.Create(width: 400, height: 300);

        Assert.Equal(AppLifecycleState.Foreground, session.Application.LifecycleState);
    }

    [Fact]
    public void EnterBackground_IsToldAtOnce_OnTheUiThread_WithTheStateAlreadyBackground()
    {
        var (session, _) = Started();
        using var _session = session;
        int uiThread = Environment.CurrentManagedThreadId;
        var told = new List<(AppLifecycleState State, int Thread)>();
        session.Application.EnteringBackground +=
            () => told.Add((session.Application.LifecycleState, Environment.CurrentManagedThreadId));

        session.EnterBackground();
        session.EnterBackground();   // already there: nothing more

        Assert.Equal([(AppLifecycleState.Background, uiThread)], told);
        Assert.Equal(AppLifecycleState.Background, session.Application.LifecycleState);
    }

    [Fact]
    public void InTheBackground_NothingIsDrawn_WhilePostedWorkRuns()
    {
        var (session, label) = Started();
        using var _session = session;
        session.EnterBackground();

        // A network reply landing while the app is away, changing the tree.
        bool ran = false;
        Application.InvokeOnMainThread(() =>
        {
            ran = true;
            label.Content = "changed while away";
        });
        for (int i = 0; i < 10; i++)
            Assert.False(session.PumpFrame(), "a frame was drawn in the background");

        Assert.True(ran, "posted work did not run in the background");
        // The change waits, dirty, for the return.
        Assert.NotEqual(DirtyFlags.None, session.Window.Window.Root!.DirtyFlags & DirtyFlags.Paint);
    }

    [Fact]
    public void InTheBackground_AMinimizedWindowsForcedFrame_IsNotDrawnEither()
    {
        var (session, label) = Started();
        using var _session = session;
        session.EnterBackground();
        label.Content = "changed while away";

        // Nothing at all may reach the GPU: Android has taken the surface, and iOS ends an app
        // that draws off screen. RenderDirtyWindows(force) is what a continuous redraw uses.
        Assert.False(session.Application.RenderDirtyWindows(force: true));
    }

    [Fact]
    public void EnterForeground_IsToldBeforeTheFirstFrame_ThenEveryWindowIsDrawnTwice()
    {
        var (session, label) = Started();
        using var _session = session;
        session.EnterBackground();
        label.Content = "changed while away";
        session.PumpFrame();
        var told = new List<AppLifecycleState>();
        session.Application.EnteredForeground += () => told.Add(session.Application.LifecycleState);

        session.EnterForeground();

        Assert.Equal([AppLifecycleState.Foreground], told);
        // Android's first frame into the new surface does not show, so the window settles:
        // drawn now, and once more in the next frame interval, then rests.
        Assert.True(session.PumpFrame(), "the return drew nothing");
        Assert.True(session.PumpFrame(), "the return drew only once");
        Assert.False(session.PumpFrame(), "the window kept drawing after it settled");
    }

    [Fact]
    public void EnterForeground_WhenAlreadyThere_TellsNothing_AndDrawsNothing()
    {
        var (session, _) = Started();
        using var _session = session;
        int told = 0;
        session.Application.EnteredForeground += () => told++;

        session.EnterForeground();

        Assert.Equal(0, told);
        Assert.False(session.PumpFrame());
    }

    [Fact]
    public void AResize_Settles_WhereTheSurfaceLosesItsFirstFrame()
    {
        var (session, _) = Started();
        using var _session = session;
        bool was = Window.SurfaceLosesFirstFrame;
        try
        {
            // Android: after a rotation the first frame showed at the old size, the next was right.
            Window.SurfaceLosesFirstFrame = true;
            session.Window.SetClientSize(300, 400);
            Assert.True(session.PumpFrame());
            Assert.True(session.PumpFrame(), "a rotation drew only once");
            Assert.False(session.PumpFrame());

            // The desktop: one frame per resize, as before.
            Window.SurfaceLosesFirstFrame = false;
            session.Window.SetClientSize(400, 300);
            Assert.True(session.PumpFrame());
            Assert.False(session.PumpFrame(), "a desktop resize drew twice");
        }
        finally
        {
            Window.SurfaceLosesFirstFrame = was;
        }
    }

    [Fact]
    public void LowMemory_EmptiesTheTextCaches_BeforeTheAppIsTold()
    {
        var (session, label) = Started();
        using var _session = session;
        var text = session.Platform.TextServices!;
        Assert.True(text.CachedWordCount > 0, "nothing was shaped to begin with");
        var wordsWhenTold = new List<int>();
        session.Application.LowMemory += () => wordsWhenTold.Add(text.CachedWordCount);

        session.SignalLowMemory();

        Assert.Equal([0], wordsWhenTold);
        // Text is shaped again as it is next laid out.
        label.Content = "after the trim";
        Assert.True(session.PumpFrame());
        Assert.True(text.CachedWordCount > 0);
    }

    [Fact]
    public void LowMemory_InTheBackground_IsToldToo_AndDrawsNothing()
    {
        var (session, _) = Started();
        using var _session = session;
        session.EnterBackground();
        int told = 0;
        session.Application.LowMemory += () => told++;

        session.SignalLowMemory();

        Assert.Equal(1, told);
        Assert.False(session.PumpFrame());
    }
}
