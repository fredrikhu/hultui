using HultUI.Core.Animation;
using HultUI.Core.Builders;
using HultUI.Core.Elements;
using HultUI.Core.Primitives;
using Xunit;

namespace HultUI.Headless.Tests;

/// <summary>
/// What a host pumping the frame loop from its own message loop (HultUI.WinForms' timer) asks of
/// the application: when its next tick is due, and a wake for work that arrives while it waits —
/// as <c>Application.Run</c> waits for its next deadline, not every 8 ms.
/// </summary>
public class HostPumpTests
{
    private static readonly TimeSpan Interval = TimeSpan.FromMilliseconds(8);

    private sealed class Host : IDisposable
    {
        public HeadlessSession Session { get; } = HeadlessSession.Create(300, 200);
        public BoxElement Box { get; } = HultUI.Core.Builders.Box.New().Width(Dimension.Px(100)).Height(Dimension.Px(50));
        public Application App => Session.Application;

        public Host()
        {
            Session.Window.SetRoot(Flex.Column().Children(Box));
            App.HostRunFrame(TimeSpan.FromMilliseconds(16));
        }

        public void Dispose()
        {
            App.HostUnpark();
            Session.Dispose();
        }
    }

    [Fact]
    public void AnIdleApplication_OwesItsHostNoTick()
    {
        using var host = new Host();
        Assert.Null(host.App.HostNextFrameIn(Interval));
    }

    [Fact]
    public void ADirtyWindowOrAMovingTween_WantsTheNextFrame()
    {
        using var host = new Host();
        host.Box.MarkPaintDirty();
        Assert.Equal(Interval, host.App.HostNextFrameIn(Interval));

        host.App.HostRunFrame(Interval);
        var fade = Animate.Opacity(host.Box, 1f, 0.5f).Duration(0.3f).Start();
        host.App.HostRunFrame(Interval);
        Assert.Equal(Interval, host.App.HostNextFrameIn(Interval));
        fade.Cancel();
    }

    [Fact]
    public void ADelay_IsTheHostsDeadline()
    {
        using var host = new Host();
        var timer = Animate.After(2f, () => { });
        Assert.Equal(Interval, host.App.HostNextFrameIn(Interval));   // its first step

        host.App.HostRunFrame(Interval);

        Assert.Equal(2000, host.App.HostNextFrameIn(Interval)!.Value.TotalMilliseconds, 1);
        timer.Cancel();
    }

    [Fact]
    public void APostLeftByTheFrame_IsDueAtOnce()
    {
        using var host = new Host();
        Application.InvokeOnMainThread(() => { });
        Assert.Equal(TimeSpan.Zero, host.App.HostNextFrameIn(Interval));
        host.App.HostRunFrame(Interval);
        Assert.Null(host.App.HostNextFrameIn(Interval));
    }

    /// <summary>While the host waits between ticks, a change on the UI thread (input a WinForms
    /// control raised, a handler writing a signal) and a post from it wake the host, once.</summary>
    [Fact]
    public void WorkOnTheUiThreadWhileTheHostWaits_WakesIt()
    {
        using var host = new Host();
        int wakes = 0;
        host.App.HostPark(() => wakes++);
        host.Box.MarkPaintDirty();
        host.Box.MarkPaintDirty();
        Assert.Equal(1, wakes);

        host.App.HostUnpark();
        int before = host.Session.Platform.WakeCalls;
        host.App.HostPark(() => wakes++);
        Application.InvokeOnMainThread(() => { });
        Assert.Equal(before + 1, host.Session.Platform.WakeCalls);
    }
}
