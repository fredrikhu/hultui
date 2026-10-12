// The AT-SPI bridge is Linux's and the UIA host Windows', but with nothing reaching a bus or a
// window they run anywhere the test asks them to.
#pragma warning disable CA1416
using HultUI.Core.Accessibility;
using HultUI.Core.Animation;
using HultUI.Core.Builders;
using HultUI.Core.Elements;
using HultUI.Core.Events;
using HultUI.Core.Primitives;
using HultUI.Platform.Accessibility.Linux;
using Xunit;
using TextB = HultUI.Core.Builders.Text;

namespace HultUI.Headless.Tests;

/// <summary>
/// The idle frame loop waits for the next deadline it owns, or for a wake, where it used to park
/// 16 ms at a time: ~60 passes a second in every idle window, 37–38 minimized, each looking again
/// at whatever had been set aside for later (plan 17, round 3, runtime-idle-wakeups). So whatever
/// is set aside now names when it is due, and whatever arrives — from another thread, or from a
/// callback the wait itself runs on the UI thread — wakes the loop. These pin both halves on
/// <see cref="HeadlessSession.RunLoop"/>'s virtual clock, where an idle loop with no deadline runs
/// its wait to the end of the run.
/// </summary>
public class LoopWakeTests
{
    /// <summary>A session showing a box and a 1 px canvas that counts the frames drawn, after its
    /// first frame.</summary>
    private sealed class Run : IDisposable
    {
        private int _frames;
        public HeadlessSession Session { get; } = HeadlessSession.Create(300, 200);
        public BoxElement Box { get; } = HultUI.Core.Builders.Box.New().Width(Dimension.Px(100)).Height(Dimension.Px(50));
        public int Frames => _frames;
        public HeadlessWindowingPlatform Platform => Session.Platform;

        public Run(Element? content = null)
        {
            Session.Window.SetRoot(Flex.Column().Children(
                content ?? Box,
                Canvas.New(_ => _frames++).Width(Dimension.Px(1)).Height(Dimension.Px(1))));
            Session.PumpFrame();
        }

        public long Now => Session.Time.GetTimestamp();
        public double MsSince(long timestamp) => Session.Time.GetElapsedTime(timestamp).TotalMilliseconds;

        public void Dispose() => Session.Dispose();
    }

    /// <summary>
    /// Runs the loop until <paramref name="condition"/> holds, and returns when the pass that made
    /// it hold began, in virtual ms after <paramref name="from"/>: the condition is asked before
    /// each pass, after the wait that followed the one before, and an idle loop's wait can run to
    /// the end of the run.
    /// </summary>
    private static double RunUntil(Run run, Func<bool> condition, long from, TimeSpan limit,
        TimeSpan? eventEvery = null, Action? onEvent = null, bool eventsEndWaits = true)
    {
        long passStart = run.Now;
        double at = -1;
        run.Session.RunLoop(() =>
        {
            if (condition())
            {
                at = run.Session.Time.GetElapsedTime(from, passStart).TotalMilliseconds;
                return true;
            }
            passStart = run.Now;
            return false;
        }, eventEvery, onEvent, limit, eventsEndWaits);
        return at;
    }

    /// <summary>Polls, in real time, for work a thread-pool hop finishes.</summary>
    private static bool Eventually(Func<bool> condition)
    {
        var limit = DateTime.UtcNow.AddSeconds(5);
        while (DateTime.UtcNow < limit)
        {
            if (condition()) return true;
            Thread.Sleep(1);
        }
        return condition();
    }

    // ── An idle loop does not come round ──

    [Fact]
    public void AnIdleWindow_DoesNotWake()
    {
        using var run = new Run();
        int passes = 0;
        long start = run.Now;

        run.Session.RunLoop(() => ++passes > 1 && run.MsSince(start) >= 10_000, limit: TimeSpan.FromSeconds(10));

        // Ten seconds of virtual time: the pass that parked and the one after the run's end. At
        // 16 ms a wait this was 625 passes.
        Assert.Equal(2, passes);
        Assert.InRange(run.MsSince(start), 10_000, 10_001);
    }

    [Fact]
    public void AMinimizedDirtyWindow_DoesNotWake()
    {
        using var run = new Run();
        run.Session.Window.Minimized = true;
        run.Session.PumpFrame();
        // Junction's watcher refreshing a list while it is minimized: drawn after the restore.
        run.Box.AddChild(TextB.New("added while minimized"));
        int passes = 0;
        long start = run.Now;

        run.Session.RunLoop(() => ++passes > 1 && run.MsSince(start) >= 10_000, limit: TimeSpan.FromSeconds(10));

        // A minimized window draws nothing until its restore, whose event wakes the loop; before,
        // the loop looked at it ~60 times a second (37–38 on Windows, which coarsens the timers).
        Assert.Equal(2, passes);
        run.Session.Window.Minimized = false;
        Assert.True(run.Session.PumpFrame(), "the restore did not draw what changed while minimized");
    }

    // ── Each deadline it owns comes on time ──

    [Fact]
    public void ADelay_FiresOnItsDeadline_WakingTheLoopOnce()
    {
        using var run = new Run();
        long start = run.Now;
        double firedAt = -1;
        int passes = 0;
        Animate.Value(0f, 1f, _ => { }).Delay(1f).Duration(0f).OnComplete(() => firedAt = run.MsSince(start)).Start();

        run.Session.RunLoop(() => { passes++; return firedAt >= 0; }, limit: TimeSpan.FromSeconds(3));

        // Its first step, then the step when the delay is out. It was stepped every 16 ms, 63
        // passes for one second.
        Assert.InRange(firedAt, 1000, 1001);
        Assert.InRange(passes, 2, 3);
    }

    /// <summary>A toast's hold was a tween with a Duration and a setter that changed nothing:
    /// it stepped every frame interval, 118 steps for a 2 s toast's 20 frames.</summary>
    [Fact]
    public void AToast_WakesTheLoopForItsFramesAndOnceForItsHold()
    {
        using var run = new Run();
        var toast = new ToastHost(() => run.Box.MarkPaintDirty());
        long start = run.Now;
        int passes = 0, before = run.Frames;
        toast.Flash("Saved", seconds: 2f);

        double goneAt = RunUntil(run, () => { passes++; return !toast.IsShowing; }, start, TimeSpan.FromSeconds(5));

        // Fade in 0.12 s, hold 2 s from the flash, fade out 0.18 s: drawn at 60 Hz, two passes
        // a frame (the frame, and the end of the wait for the next interval), and one for the
        // hold. Stepped every 16 ms, the hold alone was 125 passes.
        Assert.InRange(goneAt, 2180, 2220);
        Assert.InRange(run.Frames - before, 15, 25);
        Assert.InRange(passes, run.Frames - before, 2 * (run.Frames - before) + 6);
    }

    [Fact]
    public void ATooltipsShowDelay_WakesTheLoopOnce()
    {
        var owner = Box.New().Width(Dimension.Px(100)).Height(Dimension.Px(50)).Background(Color.Gray);
        owner.TooltipText = "A tooltip";
        using var run = new Run(owner);
        float delay = run.Session.Application.Theme.Tooltip.ShowDelay;
        run.Session.Window.MoveMouse(20, 20);
        long start = run.Now;
        int passes = 0;

        double shownAt = RunUntil(run, () => { passes++; return run.Session.Window.Window.OverlayContents.Any(); },
            start, TimeSpan.FromSeconds(3));

        // Stepped every 16 ms, the delay alone was 31 passes.
        Assert.InRange(shownAt, delay * 1000, delay * 1000 + 1);
        Assert.InRange(passes, 2, 6);
    }

    /// <summary>A tween started while the others wait out a long delay takes its first step with
    /// the next frame, not when the delay is out.</summary>
    [Fact]
    public void ATweenStartedDuringALongDelay_StepsWithTheNextFrame()
    {
        using var run = new Run();
        Animate.After(5f, () => { });
        long start = run.Now;
        double firstAt = -1;
        bool started = false;

        RunUntil(run, () => firstAt >= 0, start, TimeSpan.FromSeconds(10), TimeSpan.FromMilliseconds(100), () =>
        {
            if (started) return;
            started = true;
            Animate.Value(0f, 1f, _ => { if (firstAt < 0) firstAt = run.MsSince(start); run.Box.MarkPaintDirty(); })
                .Duration(0.3f).Start();
        });

        Assert.InRange(firstAt, 100, 101);
    }

    /// <summary>The same for one started after the pass's step, by a frame that no tween drew:
    /// a post-frame callback here, a Build in an app, of a frame a post asked for.</summary>
    [Fact]
    public void ATweenStartedByAFrameDuringALongDelay_StepsWithTheNextFrame()
    {
        using var run = new Run();
        Animate.After(5f, () => { });
        long start = run.Now;
        double firstAt = -1;
        bool posted = false;

        RunUntil(run, () => firstAt >= 0, start, TimeSpan.FromSeconds(10), TimeSpan.FromMilliseconds(100), () =>
        {
            if (posted) return;
            posted = true;
            run.Box.MarkPaintDirty();
            run.Session.Window.Window.RequestPostFrameCallback(() =>
                Animate.Value(0f, 1f, _ => { if (firstAt < 0) firstAt = run.MsSince(start); run.Box.MarkPaintDirty(); })
                    .Duration(0.3f).Start());
        });

        // The frame at 100 ms started it; its first step comes with the next, at 60 Hz.
        Assert.InRange(firstAt, 116, 118);
    }

    [Fact]
    public void AnUndrawnAnimation_StepsAtMostEvery16Ms()
    {
        using var run = new Run();
        run.Session.Window.RefreshRate = 120;
        run.Session.Window.Minimized = true;
        run.Session.PumpFrame();
        int steps = 0;
        var spin = Animate.Value(0f, 1f, _ => { steps++; run.Box.MarkPaintDirty(); }).Duration(1f).Loop().Start();
        long start = run.Now;

        run.Session.RunLoop(() => run.MsSince(start) >= 1000, limit: TimeSpan.FromSeconds(2));
        spin.Cancel();

        // A minimized window draws nothing, so its tween steps as the idle wait stepped it: at
        // most every 16 ms, not at the display's 120 Hz.
        Assert.InRange(steps, 58, 64);
    }

    /// <summary>A layout-only change (a scroll) refreshes the accessibility tree on its leading
    /// edge, then at most every 100 ms, then once more on the trailing edge: due 100 ms after the
    /// leading refresh, with nothing dirty in between.</summary>
    [Fact]
    public void TheAccessibilityLayoutRefresh_ComesOnItsTrailingDeadline()
    {
        var rows = Flex.Column().FlexShrink(0);
        for (int i = 0; i < 60; i++) rows.AddChild(TextB.New($"row {i}"));
        var scroll = ScrollView.New().Height(Dimension.Px(150)).Children(rows);
        using var run = new Run(scroll);
        var bridge = run.Session.Window.InstallAccessibilityBridge();
        run.Session.PumpFrame();
        run.Session.PumpFrame(TimeSpan.FromMilliseconds(400));

        scroll.ScrollOffsetY += 30;
        run.Session.PumpFrame();
        long leading = run.Now;
        int refreshes = bridge.Diffs.Count;
        run.Session.AdvanceTime(TimeSpan.FromMilliseconds(20));
        scroll.ScrollOffsetY += 30;
        run.Session.PumpFrame(TimeSpan.Zero);
        Assert.Equal(refreshes, bridge.Diffs.Count); // the second scroll waits for the trailing edge

        double trailing = RunUntil(run, () => bridge.Diffs.Count > refreshes, leading, TimeSpan.FromSeconds(2));

        Assert.InRange(trailing, 100, 101);
    }

    /// <summary>A range's value is told on its leading edge, then at most every 250 ms, then once
    /// with the value it came to rest at.</summary>
    [Fact]
    public void TheAccessibilityRangeRefresh_ComesOnItsTrailingDeadline()
    {
        var bar = ProgressBar.New(0.1, 1).Width(Dimension.Px(200));
        using var run = new Run(bar);
        var bridge = run.Session.Window.InstallAccessibilityBridge();
        run.Session.PumpFrame();
        run.Session.PumpFrame(TimeSpan.FromMilliseconds(400));

        bar.Value = 0.2;
        run.Session.PumpFrame();
        long leading = run.Now;
        int refreshes = bridge.Diffs.Count;
        run.Session.AdvanceTime(TimeSpan.FromMilliseconds(20));
        bar.Value = 0.3;
        run.Session.PumpFrame(TimeSpan.Zero);
        Assert.Equal(refreshes, bridge.Diffs.Count);

        double trailing = RunUntil(run, () => bridge.Diffs.Count > refreshes, leading, TimeSpan.FromSeconds(2));

        Assert.InRange(trailing, 250, 251);
    }

    [Fact]
    public void AnAccessibilityDeadline_IsOwnedOnlyWhileAClientListens()
    {
        var bar = ProgressBar.New(0.1, 1).Width(Dimension.Px(200));
        using var run = new Run(bar);
        var bridge = new SwitchedBridge { Active = true };
        run.Session.Window.Window.SetAccessibilityBridge(bridge);
        run.Session.PumpFrame();
        run.Session.PumpFrame(TimeSpan.FromMilliseconds(400));
        bar.Value = 0.2;
        run.Session.PumpFrame();
        bar.Value = 0.3;
        run.Session.PumpFrame(TimeSpan.FromMilliseconds(1));

        long at = long.MaxValue;
        var kind = IdleDeadline.None;
        run.Session.Window.Window.NextDeadline(ref at, ref kind);
        Assert.Equal(IdleDeadline.A11yRange, kind);

        // The client left: the frame that would clear the refresh never comes, so the loop must
        // not wait for it (it would wake for nothing, or spin on a deadline long past).
        bridge.Active = false;
        at = long.MaxValue;
        kind = IdleDeadline.None;
        run.Session.Window.Window.NextDeadline(ref at, ref kind);
        Assert.Equal(IdleDeadline.None, kind);
    }

    [Fact]
    public void AResizeTheThrottleHeldBack_IsADeadline()
    {
        using var run = new Run();
        var window = run.Session.Window.Window;
        window.ResizeThrottleMs = 1000;
        window.PlatformWindow.LastResizeRenderTick = Environment.TickCount64;
        window.PlatformWindow.ResizePending = true;

        long at = long.MaxValue;
        var kind = IdleDeadline.None;
        window.NextDeadline(ref at, ref kind);

        Assert.Equal(IdleDeadline.Resize, kind);
        Assert.InRange((at - run.Now) * 1000.0 / run.Session.Time.TimestampFrequency, 900, 1000);
        window.PlatformWindow.ResizePending = false;
    }

    [Fact]
    public void TheInspectorsDeferredRefresh_ComesOnItsDeadline()
    {
        using var run = new Run();
        run.Session.Application.OpenInspector();
        run.Session.PumpFrame();
        run.Session.PumpFrame(TimeSpan.FromMilliseconds(400));
        var host = run.Session.Application.InspectorHost!;

        // A frame of the inspected window refreshes the panel at most every 100 ms; one within
        // that is put off to the end of it.
        run.Box.MarkPaintDirty();
        run.Session.PumpFrame();
        run.Box.MarkPaintDirty();
        run.Session.PumpFrame(TimeSpan.FromMilliseconds(20));
        long due = host.NextRefreshAt ?? throw new Xunit.Sdk.XunitException("no refresh was put off");
        Assert.InRange((due - run.Now) * 1000.0 / run.Session.Time.TimestampFrequency, 1, 100);

        double at = RunUntil(run, () => host.NextRefreshAt is null, due, TimeSpan.FromSeconds(2));

        Assert.InRange(at, 0, 1);
    }

    // ── What arrives wakes it ──

    /// <summary>
    /// Work a callback does on the UI thread inside the loop's wait — a WebView2 event, an OLE
    /// drop, a DispatcherQueue item — comes with no event of SDL's to end the wait: the events
    /// here run inside it without ending it, as such a message does.
    /// </summary>
    private static double RunUntilFrame(Run run, Action insideTheWait)
    {
        int before = run.Frames;
        bool done = false;
        return RunUntil(run, () => run.Frames > before, run.Now, TimeSpan.FromSeconds(5),
            TimeSpan.FromMilliseconds(100), () => { if (!done) { done = true; insideTheWait(); } }, eventsEndWaits: false);
    }

    [Fact]
    public void AMutationInsideTheWait_WakesTheLoop()
    {
        using var run = new Run();
        double drawnAt = RunUntilFrame(run, () => run.Box.Background(Color.FromHex("#336699")).MarkPaintDirty());
        Assert.InRange(drawnAt, 100, 101);
    }

    [Fact]
    public void ATweenStartedInsideTheWait_WakesTheLoop()
    {
        using var run = new Run();
        double drawnAt = RunUntilFrame(run, () =>
            Animate.Opacity(run.Box, 1f, 0.5f).Duration(0.2f).Start());
        Assert.InRange(drawnAt, 100, 101);
    }

    [Fact]
    public void APostFrameCallbackInsideTheWait_WakesTheLoop()
    {
        using var run = new Run();
        double drawnAt = RunUntilFrame(run, () => run.Session.Window.Window.RequestPostFrameCallback(() => { }));
        Assert.InRange(drawnAt, 100, 101);
    }

    /// <summary>An OLE drop target is called from a message the wait dispatches: what its input
    /// changes (a hover, here) wakes the loop as any mutation does.</summary>
    [Fact]
    public void InputInsideTheWait_WakesTheLoop()
    {
        var hover = Box.New().Width(Dimension.Px(100)).Height(Dimension.Px(50)).Background(Color.Gray)
            .OnHover(b => b.Background(Color.White));
        using var run = new Run(hover);
        double drawnAt = RunUntilFrame(run, () => run.Session.Window.MoveMouse(20, 20));
        Assert.InRange(drawnAt, 100, 101);
    }

    [Fact]
    public void APostFromTheUiThreadInsideTheWait_WakesTheLoop()
    {
        using var run = new Run();
        double drawnAt = RunUntilFrame(run, () =>
            Application.InvokeOnMainThread(() => run.Box.Background(Color.White).MarkPaintDirty()));
        Assert.InRange(drawnAt, 100, 101);
    }

    /// <summary>A post made on the UI thread while the loop runs does not wake it (the loop is
    /// not parked); one made after the pass drained the queue — here by a timer's completion, in
    /// a pass that draws nothing — must still run, and not when something else next wakes the
    /// loop.</summary>
    [Fact]
    public void APostMadeAfterThePassDrainedTheQueue_RunsWithTheNextPass()
    {
        using var run = new Run();
        long start = run.Now;
        bool ran = false;
        Animate.After(0.5f, () => Application.InvokeOnMainThread(() => ran = true));

        double at = RunUntil(run, () => ran, start, TimeSpan.FromSeconds(5));

        Assert.InRange(at, 500, 501);
    }

    [Fact]
    public void APostFromAnotherThread_WakesTheLoop()
    {
        using var run = new Run();
        int before = run.Platform.WakeCalls;
        Task.Run(() => Application.InvokeOnMainThread(() => { })).Wait();
        Assert.True(run.Platform.WakeCalls > before);
    }

    [Fact]
    public void AnAccessibilityAction_WakesTheLoop()
    {
        var bar = Slider.New(10, 0, 100).Width(Dimension.Px(200));
        using var run = new Run(bar);
        var router = new AccessibilityActionRouter(run.Session.Window.Window);
        int before = run.Platform.WakeCalls;

        // AT-SPI's SetCurrentValue, on the bus's receive thread.
        Task.Run(() => router.SetRangeValue(bar.Id, 40)).Wait();

        Assert.True(Eventually(() => run.Platform.WakeCalls > before), "the queued action did not wake the loop");
        Assert.True(router.HasPendingActions);
    }

    [Fact]
    public void AClientAskingForTheRoot_WakesTheLoop_UntilATreeIsBuilt()
    {
        if (!OperatingSystem.IsWindows()) return;
        using var run = new Run();
        var window = run.Session.Window.Window;
        var router = new AccessibilityActionRouter(window);
        var host = new UiaHostBridge(window, router);
        int before = run.Platform.WakeCalls;

        // WM_GETOBJECT from a client that has just attached: no tree yet.
        host.RootRequested();
        Assert.True(Eventually(() => run.Platform.WakeCalls > before), "the first ask did not wake the loop");

        run.Session.Window.InstallAccessibilityBridge();
        run.Session.PumpFrame();
        Thread.Sleep(50);
        int built = run.Platform.WakeCalls;
        host.RootRequested();
        Thread.Sleep(100);
        Assert.Equal(built, run.Platform.WakeCalls);
    }

    [Fact]
    public void TheAtSpiStatusTurningOn_WakesTheLoop()
    {
        using var run = new Run();
        var router = new AccessibilityActionRouter(run.Session.Window.Window);
        var status = new Status();
        using var bridge = new LinuxAccessibilityBridge(new NoSignals(), router, status);
        // Another window's bridge already said assistive technology is on, so the process-wide
        // flag does not change and only this bridge's own word wakes the loop for its window.
        AccessibilityClients.Report(true);
        int before = run.Platform.WakeCalls;
        try
        {
            // org.a11y.Status changed: told on a thread-pool thread (AtSpiStatusReader).
            Task.Run(() => status.Set(true)).Wait();
            Assert.True(Eventually(() => run.Platform.WakeCalls > before), "the status turning on did not wake the loop");
        }
        finally
        {
            AccessibilityClients.Report(false);
        }
    }

    [Fact]
    public void ForcingAccessibilityClientsOn_WakesTheLoop()
    {
        using var run = new Run();
        int before = run.Platform.WakeCalls;
        try
        {
            Task.Run(() => AccessibilityClients.Force = true).Wait();
            Assert.True(run.Platform.WakeCalls > before);
        }
        finally
        {
            AccessibilityClients.Force = null;
        }
    }

    // ── A tween started after the loop was idle ──

    [Fact]
    public void AHoverTransitionAfterALongIdle_DoesNotJumpToItsEnd()
    {
        using var run = new Run();
        float first = -1;
        bool started = false;

        // Five seconds with nothing to do, the loop parked, then the pointer arrives and a hover
        // transition starts (300 ms from 0 to 1, as Transition() starts one).
        run.Session.RunLoop(() => first >= 0, eventEvery: TimeSpan.FromSeconds(5), onEvent: () =>
        {
            if (started) return;
            started = true;
            Animate.Value(0f, 1f, v => { if (first < 0) first = v; run.Box.MarkPaintDirty(); })
                .Duration(0.3f).Ease(Easing.Linear).Start();
        }, limit: TimeSpan.FromSeconds(10));

        // Its clock starts at its first step: stepped by the five seconds since the last step, it
        // would draw its end at once.
        Assert.Equal(0f, first);
    }

    private sealed class SwitchedBridge : IAccessibilityBridge
    {
        public bool Active;
        public bool IsActive => Active;
        public void ApplyChanges(TreeDiff diff, AccessibilityNode root) { }
        public void NotifyFocusChanged(AccessibilityNode? focused) { }
    }

    private sealed class Status : IAtSpiStatus
    {
        private Action<bool>? _changed;
        private bool _on;
        public bool ReadStatus() => _on;
        public void WatchStatus(Action<bool> changed) => _changed += changed;
        public void Set(bool on) { _on = on; _changed?.Invoke(on); }
    }

    private sealed class NoSignals : IAtSpiSignals
    {
        public void EmitChildrenChanged(string parentPath, string childPath, int index, bool isAdd) { }
        public void EmitPropertyChange(string path, string atSpiProperty, string newValue) { }
        public void EmitValueChanged(string path, double value) { }
        public void EmitRoleChange(string path, uint role) { }
        public void EmitStateChanged(string path, string state, bool active) { }
        public void EmitTextChanged(string path, bool insert, int offset, string text) { }
        public void EmitTextCaretMoved(string path, int offset) { }
        public void EmitTextSelectionChanged(string path) { }
        public void EmitAnnouncement(string path, string text, int politeness) { }
        public void EmitCacheAdd(IAtSpiTree tree, AtSpiAccessible node) { }
        public void EmitCacheRemove(string path) { }
    }
}
