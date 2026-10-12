using HultUI.Core.Animation;
using HultUI.Core.Builders;
using HultUI.Core.Components;
using HultUI.Core.Elements;
using HultUI.Core.Primitives;
using HultUI.Core.Reactivity;
using Xunit;

namespace HultUI.Headless.Tests;

/// <summary>
/// <c>Application.Run</c> steps animations once per frame it may draw, by all the time since the
/// last step, like a browser's requestAnimationFrame (plan 17, reactivity-tween-tick-per-frame).
/// It stepped them on every pass of the loop, and every event that ends a wait early is a pass:
/// a tween writing a signal a Build reads rebuilt that component about once a millisecond under
/// a 1,000 Hz mouse or a playing video — ~13 times for each frame Reel drew. These run the real
/// loop on the virtual clock (<see cref="HeadlessSession.RunLoop"/>), with an event every
/// millisecond.
/// </summary>
public class TweenStepTests
{
    private static readonly TimeSpan Millisecond = TimeSpan.FromMilliseconds(1);

    /// <summary>A video player's bar, in small: Build reads the signal a fade writes.</summary>
    private sealed class Fader : Component
    {
        public readonly Signal<float> Level = new(0f);
        public int Builds;

        public override Element Build()
        {
            Builds++;
            return Box.New().Width(Dimension.Px(120)).Height(Dimension.Px(20)).Background(Color.Blue).Opacity(Level.Value);
        }
    }

    /// <summary>Junction's spinner: a looping tween that writes a field and marks the component dirty.</summary>
    private sealed class Spinner : Component
    {
        private float _phase;
        public int Builds;

        public void Step(float phase)
        {
            _phase = phase;
            MarkDirty();
        }

        public override Element Build()
        {
            Builds++;
            return Box.New().Width(Dimension.Px(16)).Height(Dimension.Px(16)).Background(Color.Blue)
                .Transform(Transform2D.Rotate(_phase * 360f));
        }
    }

    /// <summary>A session showing <paramref name="content"/> beside a canvas counting frames drawn.</summary>
    private sealed class Loop : IDisposable
    {
        private int _frames;
        public HeadlessSession Session { get; } = HeadlessSession.Create(300, 200);
        public int Frames => _frames;
        public int Posts;

        public Loop(Element content)
        {
            Session.Window.SetRoot(Flex.Column().Children(
                content,
                Canvas.New(_ => _frames++).Width(Dimension.Px(1)).Height(Dimension.Px(1))));
            Session.PumpFrame();
        }

        public long Now => Session.Time.GetTimestamp();

        public double MillisecondsSince(long timestamp) => Session.Time.GetElapsedTime(timestamp).TotalMilliseconds;

        /// <summary>The real loop until <paramref name="until"/>, with a worker posting to the UI
        /// thread every millisecond.</summary>
        public void Storm(Func<bool> until) =>
            Session.RunLoop(until, eventEvery: Millisecond, onEvent: () => Application.InvokeOnMainThread(() => Posts++));

        public void Dispose() => Session.Dispose();
    }

    [Fact]
    public void ATweenWritingASignal_RebuildsOncePerFrameDrawn_WhileEventsArriveEveryMillisecond()
    {
        var fader = new Fader();
        using var loop = new Loop(fader.Mount());
        int builds = fader.Builds, frames = loop.Frames;
        bool done = false;
        Animate.Value(0f, 1f, v => fader.Level.Value = v).Duration(0.3f).Ease(Easing.Linear)
            .OnComplete(() => done = true).Start();

        loop.Storm(() => done);

        int rebuilds = fader.Builds - builds, drawn = loop.Frames - frames;
        Assert.True(loop.Posts >= 290, $"only {loop.Posts} posts in 300 ms");
        Assert.InRange(drawn, 17, 20);
        // One per frame drawn, and the last write, whose frame comes after the run.
        Assert.True(rebuilds <= drawn + 1, $"{rebuilds} rebuilds for {drawn} frames drawn");
    }

    [Fact]
    public void ATweenCallingMarkDirty_RebuildsOncePerFrameDrawn_WhileEventsArriveEveryMillisecond()
    {
        var spinner = new Spinner();
        using var loop = new Loop(spinner.Mount());
        int builds = spinner.Builds, frames = loop.Frames;
        var spin = Animate.Value(0f, 1f, spinner.Step).Duration(0.9f).Ease(Easing.Linear).Loop().Start();
        long start = loop.Now;

        try { loop.Storm(() => loop.MillisecondsSince(start) >= 500); }
        finally { spin.Cancel(); }

        int rebuilds = spinner.Builds - builds, drawn = loop.Frames - frames;
        Assert.InRange(drawn, 29, 32);
        Assert.True(rebuilds <= drawn + 1, $"{rebuilds} rebuilds for {drawn} frames drawn");
    }

    [Theory]
    [InlineData(false)]   // an element's tween: MarkPaintDirty on every step
    [InlineData(true)]    // a tween writing a signal a Build reads
    public void OnA120HzDisplay_ATweenIsDrawnEveryInterval_WithNoEventsBetweenFrames(bool throughASignal)
    {
        // Nothing is dirty between a tween's frames until it steps, and it steps when its frame
        // is due: the loop must wait for that deadline, not for its idle 16 ms, or the tween is
        // drawn at ~60 frames a second whatever the display does.
        var fader = new Fader();
        var box = Box.New().Width(Dimension.Px(120)).Height(Dimension.Px(20));
        using var loop = new Loop(throughASignal ? fader.Mount() : box);
        loop.Session.Window.RefreshRate = 120;
        int frames = loop.Frames;
        bool done = false;
        Action<float> step = throughASignal
            ? v => fader.Level.Value = v
            : v => { box.Background(new Color((byte)(v * 255), 0, 0)); box.MarkPaintDirty(); };
        Animate.Value(0f, 1f, step).Duration(1f).Ease(Easing.Linear).OnComplete(() => done = true).Start();

        loop.Session.RunLoop(() => done);

        Assert.InRange(loop.Frames - frames, 114, 122);
    }

    [Fact]
    public void ATween_StillTakesItsDuration_AndItsLastValueIsDrawn()
    {
        // Each step takes all the time since the last one: a tween stepped once a frame by one
        // pass's millisecond would take sixteen times as long.
        var fader = new Fader();
        using var loop = new Loop(fader.Mount());
        double completedAfter = -1;
        int drawnBeforeEnd = -1;
        long start = loop.Now;
        Animate.Value(0f, 1f, v => fader.Level.Value = v).Duration(0.3f).Ease(Easing.Linear)
            .OnComplete(() => { completedAfter = loop.MillisecondsSince(start); drawnBeforeEnd = loop.Frames; }).Start();

        // Until a frame has been drawn after the last step.
        loop.Storm(() => completedAfter >= 0 && loop.Frames > drawnBeforeEnd);

        // OnComplete runs on the first frame at or past 300 ms: at most an interval late.
        Assert.InRange(completedAfter, 300, 300 + 1000.0 / 60 + 1);
        Assert.Equal(1f, fader.Level.Peek());
        var box = (BoxElement)fader.Mount().Children[0];
        Assert.Equal(1f, box.Opacity);
    }

    [Fact]
    public void WhileNothingIsDrawn_AnimationsStepAtMostOncePerInterval()
    {
        // A pure delay — the tooltip's, a player's auto-hide — on a window with nothing to draw.
        // Events still end every wait; the steps come once a display interval.
        using var loop = new Loop(Box.New().Width(Dimension.Px(10)).Height(Dimension.Px(10)));
        int steps = 0, frames = loop.Frames;
        var timer = Animate.Value(0f, 1f, _ => steps++).Duration(10f).Start();
        long start = loop.Now;

        try { loop.Storm(() => loop.MillisecondsSince(start) >= 500); }
        finally { timer.Cancel(); }

        Assert.Equal(frames, loop.Frames);   // nothing was dirty, nothing drawn
        Assert.InRange(steps, 25, 32);       // 500 ms at 60 Hz, not one per millisecond
    }

    [Fact]
    public void AMinimizedWindow_RunsItsTweensToTheEnd_StepPerInterval_AndDrawsAfterTheRestore()
    {
        var fader = new Fader();
        using var loop = new Loop(fader.Mount());
        loop.Session.Window.Minimized = true;
        int builds = fader.Builds, frames = loop.Frames;
        bool done = false;
        long start = loop.Now;
        Animate.Value(0f, 1f, v => fader.Level.Value = v).Duration(0.3f).Ease(Easing.Linear)
            .OnComplete(() => done = true).Start();

        loop.Storm(() => done);

        // Nothing drawn, the fade at its end on time, and its steps no more often than frames
        // would have been.
        Assert.Equal(frames, loop.Frames);
        Assert.InRange(loop.MillisecondsSince(start), 300, 300 + 1000.0 / 60 + 1);
        Assert.Equal(1f, fader.Level.Peek());
        Assert.True(fader.Builds - builds <= 20, $"{fader.Builds - builds} rebuilds in 300 ms minimized");

        loop.Session.Window.Minimized = false;
        loop.Session.RunLoop(() => loop.Frames > frames);
        Assert.Equal(1f, ((BoxElement)fader.Mount().Children[0]).Opacity);
    }

    private sealed class Notifier : Component
    {
        public readonly ToastHost Toast;
        public int Builds;

        public Notifier() => Toast = new ToastHost(MarkDirty);

        public override Element Build()
        {
            Builds++;
            var root = Box.New().Width(Dimension.Px(300)).Height(Dimension.Px(150));
            if (Toast.Build() is { } strip) root.AddChild(strip);
            return root;
        }
    }

    [Fact]
    public void AToast_FadesInHoldsAndGoes_WhileTheWindowIsMinimized()
    {
        // The owner's MarkDirty, as Aperture, Junction and Meridian wire it: it rebuilds where the
        // toast appears and where it goes, and the fades write the toast on screen.
        var owner = new Notifier();
        using var loop = new Loop(owner.Mount());
        loop.Session.Window.Minimized = true;
        int builds = owner.Builds;
        long start = loop.Now;

        owner.Toast.Flash("Saved");
        loop.Storm(() => !owner.Toast.IsShowing);

        // Held 1.7 s from the flash (the 0.12 s fade-in runs inside it), then 0.18 s out: the
        // hold and the fade each end on the first step at or past their time. Each fade's clock
        // starts with a step at its start value, the loop's.
        Assert.InRange(loop.MillisecondsSince(start), 1880, 1880 + 2 * (1000.0 / 60) + 1);
        Assert.Equal(2, owner.Builds - builds);
    }
}
