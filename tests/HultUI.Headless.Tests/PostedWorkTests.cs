using HultUI.Core.Builders;
using HultUI.Core.Elements;
using HultUI.Core.Events;
using HultUI.Core.Primitives;
using HultUI.Core.Rendering;
using Xunit;

namespace HultUI.Headless.Tests;

/// <summary>
/// Work posted to the UI thread (<c>Application.InvokeOnMainThread</c>, <c>Application.Post</c>, an
/// <c>await</c>'s continuation) runs at the start of a pass of the loop, in the order it was posted,
/// for at most half a display interval a pass: at least one action, then none once the budget is
/// spent, the rest left for the next pass. The loop ran everything, what was posted while it ran
/// included, so a producer faster than the UI thread held the thread in the drain: no input handled
/// and no frame drawn until the stream stopped (a framework user's report, plan 17 round 15). A
/// session's clock moves only between passes, so a pass here runs everything unless the work moves
/// the clock itself, as these do to stand for work that takes time.
/// </summary>
public class PostedWorkTests
{
    private static readonly TimeSpan Ms = TimeSpan.FromMilliseconds(1);

    private static HeadlessSession Session(Element? root = null)
    {
        var session = HeadlessSession.Create(300, 200);
        session.Window.SetRoot(root ?? Box.New());
        session.PumpFrame();
        return session;
    }

    [Fact]
    public void APass_StopsOnceItsBudgetIsSpent_AndTheNextRunsTheRest_InOrder()
    {
        using var session = Session();
        var ran = new List<int>();
        // Each takes 3 ms; a pass may spend half of a 60 Hz interval, 8.3 ms: the third spends it.
        for (int i = 0; i < 10; i++)
        {
            int n = i;
            Application.InvokeOnMainThread(() => { ran.Add(n); session.AdvanceTime(3 * Ms); });
        }

        session.PumpFrame();
        Assert.Equal([0, 1, 2], ran);
        session.PumpFrame();
        Assert.Equal([0, 1, 2, 3, 4, 5], ran);
        session.PumpFrames(2);
        Assert.Equal(Enumerable.Range(0, 10), ran);
    }

    [Fact]
    public void APass_RunsAtLeastOneAction_HoweverLongItTakes()
    {
        using var session = Session();
        var ran = new List<int>();
        Application.InvokeOnMainThread(() => { ran.Add(0); session.AdvanceTime(100 * Ms); });
        Application.InvokeOnMainThread(() => { ran.Add(1); session.AdvanceTime(100 * Ms); });

        session.PumpFrame();
        Assert.Equal([0], ran);
        session.PumpFrame();
        Assert.Equal([0, 1], ran);
    }

    [Fact]
    public void WorkThatTakesNoTime_RunsInOnePass_WhatItPostsIncluded()
    {
        using var session = Session();
        int ran = 0;
        for (int i = 0; i < 1000; i++)
            Application.InvokeOnMainThread(() =>
            {
                ran++;
                if (ran <= 1000) Application.InvokeOnMainThread(() => ran++);
            });

        session.PumpFrame();
        Assert.Equal(2000, ran);
    }

    /// <summary>Nothing catches what an action throws: the pass ends there, and what was posted
    /// after it runs in the next one, in order. As before the budget.</summary>
    [Fact]
    public void AnActionThatThrows_LeavesTheRestQueued()
    {
        using var session = Session();
        var ran = new List<int>();
        Application.InvokeOnMainThread(() => ran.Add(0));
        Application.InvokeOnMainThread(() => throw new InvalidOperationException("posted"));
        Application.InvokeOnMainThread(() => ran.Add(2));
        Application.InvokeOnMainThread(() => ran.Add(3));

        var thrown = Assert.Throws<InvalidOperationException>(() => session.PumpFrame());
        Assert.Equal("posted", thrown.Message);
        Assert.Equal([0], ran);

        session.PumpFrame();
        Assert.Equal([0, 2, 3], ran);
    }

    /// <summary>
    /// A stream from a worker that posts faster than the UI thread runs its work: every 2 ms, work
    /// of 5 ms each, for a second. The loop used to run all of it in its first pass, 2.5 s with no
    /// frame and no input; it now leaves the drain every half interval, so frames keep coming and a
    /// key pressed 100 ms in is handled at once. The queue still grows while the stream lasts.
    /// </summary>
    [Fact]
    public void AStreamFasterThanTheUiThread_StillDrawsFrames_AndHandlesInput()
    {
        int frames = 0;
        var box = Box.New().Width(Dimension.Px(100)).Height(Dimension.Px(50));
        var root = Box.New().Children(box, Canvas.New(_ => frames++).Width(Dimension.Px(1)).Height(Dimension.Px(1)));
        using var session = Session(root);

        const int Count = 500;
        long start = session.Time.GetTimestamp();
        double Since() => session.Time.GetElapsedTime(start).TotalMilliseconds;
        double keyAtMs = -1;
        root.On<KeyDownEvent>(_ => keyAtMs = Since());
        int posted = 0, ran = 0, framesInStream = 0;
        bool pressed = false;
        void PostDue()
        {
            while (posted < Count && (posted + 1) * 2 <= Since())
            {
                posted++;
                Application.InvokeOnMainThread(Work);
            }
        }
        void Work()
        {
            ran++;
            box.Background(new Color((byte)(ran % 256), 0, 0)).MarkPaintDirty();
            session.AdvanceTime(5 * Ms);
            PostDue();
        }

        frames = 0;
        session.RunLoop(() => ran == Count, eventEvery: 2 * Ms, onEvent: () =>
            {
                PostDue();
                if (Since() < 1000) framesInStream = frames;
                if (!pressed && Since() >= 100)
                {
                    pressed = true;
                    session.Window.PressKey(KeyCode.A);
                }
            },
            limit: TimeSpan.FromSeconds(10));

        Assert.InRange(keyAtMs, 100, 140);
        // About 30: each frame's passes run a budget of work each, and a post wakes a wait.
        Assert.True(framesInStream >= 20, $"{framesInStream} frames while the stream ran for a second");
    }

    /// <summary>
    /// A live frame (Win32's modal size loop, which runs no pass) draws for a post made since the
    /// last one drew, and not again for posts that were already waiting: a pass that ran out of
    /// budget leaves its posts waiting, and those are not new.
    /// </summary>
    [Fact]
    public void PostsAPassLeft_AreNotNewToALiveFrame()
    {
        Assert.False(GpuFrameSink.MaySignalOnlyByPosting,
            "an earlier test left a GpuFrameSink undisposed, or an image holding a GPU source");
        int frames = 0;
        using var session = Session(Box.New().Children(Canvas.New(_ => frames++).Width(1f).Height(1f)));
        var live = session.Window.Window.PlatformWindow.LiveResizeFrame
            ?? throw new InvalidOperationException("the application set no live frame");
        for (int i = 0; i < 5; i++)
            Application.InvokeOnMainThread(() => session.AdvanceTime(5 * Ms));
        session.PumpFrame();   // runs two of them, 10 ms

        LiveFrameRequests.SetPoster(() => { });
        try
        {
            int before = frames;
            live();
            Assert.Equal(before, frames);   // three waiting, none of them new

            Application.InvokeOnMainThread(() => { });
            live();
            live();
            Assert.Equal(before + 1, frames);
        }
        finally
        {
            LiveFrameRequests.SetPoster(null);
            session.PumpFrames(3);   // runs the posts
        }
    }
}
