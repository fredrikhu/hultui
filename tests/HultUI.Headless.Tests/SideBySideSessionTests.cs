using HultUI.Core.Animation;
using HultUI.Core.Builders;
using HultUI.Core.Threading;
using Xunit;

namespace HultUI.Headless.Tests;

// Inside a HultUI.* namespace bare `Text` binds to the HultUI.Text namespace.
using Text = HultUI.Core.Builders.Text;

/// <summary>
/// Headless sessions side by side: two desks of one system in one test, each its own
/// application. Each keeps its own main-thread queue, synchronization context, tweens and
/// AsyncLoader queue; what a test drives (a pump, input, a root, <see cref="HeadlessSession.Enter"/>)
/// says which one the code on the thread runs for, and work that code starts carries it along.
/// And a session a test never disposed no longer poisons the process: the next one is created
/// beside it.
/// </summary>
public class SideBySideSessionTests
{
    [Fact]
    public void TwoSessions_EachPumpsItsOwnFramesAndPosts()
    {
        using var a = HeadlessSession.Create();
        using var b = HeadlessSession.Create();
        a.Window.SetRoot(Box.New());
        b.Window.SetRoot(Box.New());
        Assert.True(a.PumpFrame());
        Assert.True(b.PumpFrame());

        var ran = new List<string>();
        a.Application.Post(() => ran.Add("a"));
        b.Application.Post(() => ran.Add("b"));
        a.Enter();
        Application.InvokeOnMainThread(() => ran.Add("a, static"));
        b.Enter();
        Application.InvokeOnMainThread(() => ran.Add("b, static"));

        b.PumpFrame();
        Assert.Equal(["b", "b, static"], ran);
        a.PumpFrame();
        Assert.Equal(["b", "b, static", "a", "a, static"], ran);

        // Each on its own clock.
        a.AdvanceTime(TimeSpan.FromSeconds(5));
        Assert.True(a.Time.GetUtcNow() - b.Time.GetUtcNow() >= TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void WorkACodeStarted_PostsToItsApplication_WhicheverIsCurrentWhenItLands()
    {
        using var a = HeadlessSession.Create();
        using var b = HeadlessSession.Create();

        // A worker started while A runs, which posts after the test has moved on to B: a desk's
        // server push, a file watcher.
        a.Enter();
        var gate = new TaskCompletionSource();
        bool workerRan = false;
        var worker = Task.Run(async () =>
        {
            await gate.Task;
            Application.InvokeOnMainThread(() => workerRan = true);
        });

        // And an await continuation of code A ran: it resumes on A's queue.
        bool resumed = false;
        async Task ResumeAsync()
        {
            await Task.Yield();
            resumed = true;
        }
        var continuation = ResumeAsync();

        b.Enter();
        gate.SetResult();
        // Not awaited: the test's own continuation would go to B's queue, which nothing drains.
        Assert.True(worker.Wait(TimeSpan.FromSeconds(10)));

        b.PumpFrames(3);
        Assert.False(workerRan);
        Assert.False(resumed);

        a.PumpFrame();
        Assert.True(workerRan);
        Assert.True(resumed);
        Assert.True(continuation.IsCompleted);
    }

    [Fact]
    public void Tweens_StepWithTheirOwnSessionsFrames()
    {
        using var a = HeadlessSession.Create();
        using var b = HeadlessSession.Create();

        a.Enter();
        float aValue = 0f;
        Animate.Value(0f, 1f, v => aValue = v).Duration(1f).Start();

        // B's frames do not step A's tween, and B is idle with it running.
        b.PumpFrames(30);
        Assert.Equal(0f, aValue);
        Assert.False(AnimationManager.HasActiveAnimations);

        a.PumpFrame(TimeSpan.FromSeconds(0.5));
        Assert.InRange(aValue, 0.01f, 0.99f);
        Assert.True(AnimationManager.HasActiveAnimations);

        // Disposing B leaves A's tween running.
        b.Dispose();
        a.PumpFrame(TimeSpan.FromSeconds(1));
        Assert.Equal(1f, aValue);
    }

    [Fact]
    public void LoadsLandInTheSessionThatAskedForThem()
    {
        using var a = HeadlessSession.Create();
        using var b = HeadlessSession.Create();

        a.Enter();
        var loader = new AsyncLoader<string>("empty");
        loader.Load("key", () => "loaded");

        b.PumpFrames(3);
        Assert.Equal("empty", loader.Value);

        a.PumpFrame();
        Assert.Equal("loaded", loader.Value);
    }

    [Fact]
    public void DisposingOne_LeavesTheOtherWorking()
    {
        var outer = SynchronizationContext.Current;
        using var a = HeadlessSession.Create();
        var b = HeadlessSession.Create();
        a.Window.SetRoot(Box.New());
        b.Window.SetRoot(Box.New());
        b.PumpFrame();

        b.Dispose();

        // The thread is A's again: its context, its binding, its posts.
        Assert.Same(a.Application, ((UIThreadSynchronizationContext)SynchronizationContext.Current!).Owner);
        Assert.Equal(Environment.CurrentManagedThreadId, UIThread.BoundThreadId);
        bool ran = false;
        Application.InvokeOnMainThread(() => ran = true);
        Assert.True(a.PumpFrame());
        Assert.True(ran);

        // A post to B itself goes nowhere, not to A.
        Assert.Throws<ObjectDisposedException>(() => b.Enter());
        bool stray = false;
        b.Application.Post(() => stray = true);
        a.PumpFrame();
        Assert.False(stray);

        // And the last one out gives the thread back what it had before either.
        a.Dispose();
        Assert.Same(outer, SynchronizationContext.Current);
        Assert.Null(UIThread.BoundThreadId);
    }

    [Fact]
    public void ALeakedSession_DoesNotBreakTheNextCreate()
    {
        // A test whose teardown threw before Dispose: its session stays alive for the rest of
        // the run, on a thread the next test may not share.
        HeadlessSession? leaked = null;
        var thread = new Thread(() =>
        {
            leaked = HeadlessSession.Create();
            leaked.Window.SetRoot(Box.New());
            leaked.PumpFrame();
        });
        thread.Start();
        thread.Join();

        try
        {
            using var next = HeadlessSession.Create();
            next.Window.SetRoot(Text.New("next"));
            Assert.True(next.PumpFrame());
            Assert.Equal(Environment.CurrentManagedThreadId, UIThread.BoundThreadId);

            bool ran = false;
            Application.InvokeOnMainThread(() => ran = true);
            next.PumpFrame();
            Assert.True(ran);
            Assert.NotNull(next.Window.FindByText("next"));
        }
        finally
        {
            leaked!.Dispose();
        }
        Assert.Null(UIThread.BoundThreadId);
    }

    [Fact]
    public void ALeakedSessionOnThisThread_DoesNotBreakTheNextCreate()
    {
        var leaked = HeadlessSession.Create();
        try
        {
            using var next = HeadlessSession.Create();
            next.Window.SetRoot(Box.New());
            Assert.True(next.PumpFrame());
            Assert.Same(next.Application,
                ((UIThreadSynchronizationContext)SynchronizationContext.Current!).Owner);
        }
        finally
        {
            leaked.Dispose();
        }
    }
}
