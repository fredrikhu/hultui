using HultUI.Core.Animation;
using HultUI.Core.Builders;
using HultUI.Core.Primitives;
using Xunit;

namespace HultUI.Headless.Tests;

/// <summary>
/// <see cref="Application.RunFrame"/> is the seam a host with its own message loop
/// drives — a WinForms or WPF application embedding HultUI, a game loop. These tests
/// pin the contract that host down: what a frame does, what it reports, and when
/// <see cref="Application.IsRunning"/> tells the host to stop.
///
/// <para>They deliberately call <c>Application.RunFrame</c> rather than
/// <c>session.PumpFrame</c>, because the point is the API a host sees rather than
/// the session wrapper around it.</para>
/// </summary>
public class RunFrameTests
{
    private static readonly TimeSpan Frame = TimeSpan.FromTicks(TimeSpan.TicksPerSecond / 60);

    [Fact]
    public void RunFrame_RendersWhenDirty_AndReportsIt()
    {
        using var session = HeadlessSession.Create();
        session.Window.SetRoot(Box.New().Background(Color.Red));

        Assert.True(session.Application.RunFrame(Frame));    // first frame: root dirty
        Assert.False(session.Application.RunFrame(Frame));   // idle UI: nothing to render
    }

    [Fact]
    public void RunFrame_Force_RendersAnIdleWindow()
    {
        using var session = HeadlessSession.Create();
        session.Window.SetRoot(Box.New().Background(Color.Blue));
        session.Application.RunFrame(Frame);

        Assert.False(session.Application.RunFrame(Frame));
        Assert.True(session.Application.RunFrame(Frame, forceRender: true));
    }

    [Fact]
    public void RunFrame_DrainsWorkPostedFromOtherThreads()
    {
        using var session = HeadlessSession.Create();
        session.Window.SetRoot(Box.New());

        bool ran = false;
        Application.InvokeOnMainThread(() => ran = true);

        Assert.False(ran);
        session.Application.RunFrame(Frame);
        Assert.True(ran);
    }

    [Fact]
    public void RunFrame_DoesNotWait()
    {
        // A host owns its own idle policy: an idle frame must return immediately
        // rather than parking the thread the way Run() does for ~16ms.
        using var session = HeadlessSession.Create();
        session.Window.SetRoot(Box.New());
        session.Application.RunFrame(Frame);

        var clock = System.Diagnostics.Stopwatch.StartNew();
        for (int i = 0; i < 20; i++)
            Assert.False(session.Application.RunFrame(Frame));

        Assert.True(clock.ElapsedMilliseconds < 100,
            $"20 idle frames took {clock.ElapsedMilliseconds}ms — RunFrame is waiting somewhere");
    }

    [Fact]
    public void IsRunning_GoesFalseWhenTheLastWindowCloses()
    {
        using var session = HeadlessSession.Create();
        session.Window.SetRoot(Box.New());
        Assert.True(session.Application.IsRunning);

        session.Window.Window.Close();
        session.Application.RunFrame(Frame);   // the frame that reaps it

        Assert.False(session.Application.IsRunning);
    }

    [Fact]
    public void IsRunning_IgnoresTheDevToolsWindow()
    {
        // The inspector must not keep a closed application alive: once the app's own
        // windows are gone there is nothing left to inspect.
        using var session = HeadlessSession.Create();
        session.Window.SetRoot(Box.New());
        session.Application.OpenInspector();
        session.PumpFrame();
        Assert.True(session.Application.IsInspectorOpen);

        session.Window.Window.Close();
        session.Application.RunFrame(Frame);

        Assert.False(session.Application.IsRunning);
    }

    [Fact]
    public void RunFrame_AfterDispose_Throws()
    {
        var session = HeadlessSession.Create();
        session.Window.SetRoot(Box.New());
        var app = session.Application;
        session.Dispose();

        Assert.Throws<ObjectDisposedException>(() => app.RunFrame(Frame));
    }

    [Fact]
    public void RunFrame_TicksAnimationsByTheDeltaItIsGiven()
    {
        using var session = HeadlessSession.Create();
        session.Window.SetRoot(Box.New());
        session.PumpFrame();

        float value = -1f;
        Animate.Value(0f, 100f, v => value = v).Duration(1f).Ease(Easing.Linear).Start();

        // A host supplies its own delta. Two exactly-representable half seconds
        // land on 50 then 100 only if RunFrame passes that delta straight through
        // rather than measuring a clock of its own.
        session.Application.RunFrame(TimeSpan.FromSeconds(0.5));
        Assert.Equal(50f, value);

        session.Application.RunFrame(TimeSpan.FromSeconds(0.5));
        Assert.Equal(100f, value);
        Assert.False(AnimationManager.HasActiveAnimations);
    }
}
