using HultUI.Core.Animation;
using HultUI.Core.Builders;
using HultUI.Core.Elements;
using HultUI.Core.Primitives;
using Xunit;

namespace HultUI.Headless.Tests;

/// <summary>
/// <see cref="HeadlessSession.RunLoop"/> runs <c>Application.Run</c>'s own loop on the virtual
/// clock: a wait is virtual time moving on, and an event source ends waits early as arriving
/// events do. These pin that the loop it runs behaves as the real one does on a display:
/// paced renders, posts drained, a run that ends.
/// </summary>
public class RunLoopTests
{
    /// <summary>A session showing <paramref name="content"/> and a 1 px canvas that counts the
    /// frames drawn, after its first frame.</summary>
    private sealed class Counted : IDisposable
    {
        private int _frames;
        public HeadlessSession Session { get; } = HeadlessSession.Create(300, 200);
        public int Frames => _frames;

        public Counted(Element? content = null)
        {
            Session.Window.SetRoot(Box.New().Children(
                content ?? Box.New().Width(Dimension.Px(100)).Height(Dimension.Px(50)),
                Canvas.New(_ => _frames++).Width(Dimension.Px(1)).Height(Dimension.Px(1))));
            Session.PumpFrame();
        }

        public double MillisecondsSince(long timestamp) => Session.Time.GetElapsedTime(timestamp).TotalMilliseconds;

        public void Dispose() => Session.Dispose();
    }

    [Fact]
    public void ADirtyWindow_RendersAtTheDisplayRate_OnVirtualTime()
    {
        var box = Box.New().Width(Dimension.Px(100)).Height(Dimension.Px(50));
        using var run = new Counted(box);
        bool done = false;
        Animate.Value(0f, 1f, v => { box.Background(new Color((byte)(v * 255), 0, 0)); box.MarkPaintDirty(); })
            .Duration(1f).Ease(Easing.Linear).OnComplete(() => done = true).Start();
        int before = run.Frames;
        long start = run.Session.Time.GetTimestamp();

        run.Session.RunLoop(() => done);

        // One second of a tween at 60 Hz: the loop renders once per interval, not once per pass.
        Assert.InRange(run.MillisecondsSince(start), 990, 1040);
        Assert.InRange(run.Frames - before, 58, 62);
    }

    [Fact]
    public void AnEventSource_EndsWaitsEarly_AndItsPostsAreDrained()
    {
        using var run = new Counted();
        int posted = 0, drained = 0;
        long start = run.Session.Time.GetTimestamp();

        run.Session.RunLoop(() => drained >= 100, eventEvery: TimeSpan.FromMilliseconds(1),
            onEvent: () => { posted++; Application.InvokeOnMainThread(() => drained++); });

        // An idle loop waits for its next deadline, here none; an event every millisecond ends
        // each wait after one, and the next pass drains what it posted.
        Assert.InRange(run.MillisecondsSince(start), 100, 102);
        Assert.InRange(posted, 100, 101);
    }

    [Fact]
    public void WithoutEvents_AnIdleLoopParksForTheRestOfTheRun()
    {
        using var run = new Counted();
        int passes = 0;
        long start = run.Session.Time.GetTimestamp();

        // An idle loop owns no deadline and parks until something arrives; nothing can, so its
        // wait runs past the run's limit and the condition, asked once more, fails the run. It
        // used to park 16 ms at a time: ten passes in 160 ms.
        Assert.Throws<TimeoutException>(() => run.Session.RunLoop(() => ++passes > 10, limit: TimeSpan.FromSeconds(1)));

        Assert.Equal(2, passes);
        Assert.InRange(run.MillisecondsSince(start), 1000, 1001);
    }

    [Fact]
    public void ARunWhoseConditionIsNeverMet_Fails_AfterItsLimit()
    {
        using var run = new Counted();
        long start = run.Session.Time.GetTimestamp();

        Assert.Throws<TimeoutException>(() => run.Session.RunLoop(() => false, limit: TimeSpan.FromSeconds(1)));
        Assert.InRange(run.MillisecondsSince(start), 1000, 1001);
    }

    [Fact]
    public void ClosingTheWindow_EndsTheRun()
    {
        using var run = new Counted();
        Animate.Value(0f, 1f, _ => { }).Duration(0.2f).OnComplete(() => run.Session.Window.Window.Close()).Start();
        long start = run.Session.Time.GetTimestamp();

        run.Session.RunLoop(() => false);

        Assert.False(run.Session.Application.IsRunning);
        // As the app's own close button does, in a pass: the run ends with it, rather than
        // parking to the end of the run's 60 s first.
        Assert.InRange(run.MillisecondsSince(start), 200, 240);
    }

    [Fact]
    public void AWindowClosedByAnEvent_EndsTheRunAtOnce_WithoutParkingFirst()
    {
        // An event that closes the window — a posted WM_CLOSE, the task bar's Close — is handled
        // by the next pass. That pass must end the loop before the loop waits: with no window
        // left, nothing would end the wait, and the app stayed running, parked, windowless, until
        // some unrelated wake (plan 17, round 4: FlaUI killed the VirtualList sample after its
        // 5 s close timeout; a second WM_CLOSE ended it at once).
        using var run = new Counted();
        bool posted = false;
        long start = run.Session.Time.GetTimestamp();

        run.Session.RunLoop(() => false, eventEvery: TimeSpan.FromSeconds(10), onEvent: () =>
        {
            if (posted) return;
            posted = true;
            Application.InvokeOnMainThread(() => run.Session.Window.Window.Close());
        });

        Assert.False(run.Session.Application.IsRunning);
        // The close arrived at 10 s; the next event, which used to end the wait, at 20 s.
        Assert.InRange(run.MillisecondsSince(start), 10_000, 10_001);
    }
}
