using HultUI;
using HultUI.Core.Components;
using HultUI.Core.Elements;
using Xunit;

namespace HultUI.Headless.Tests;

// Inside a HultUI.* namespace bare `Text` binds to the HultUI.Text namespace.
using Text = HultUI.Core.Builders.Text;

/// <summary>
/// Under a session, AsyncLoader's thread-pool hop becomes a queue the pump drains, so a
/// load completes a fixed number of frames after it was requested instead of whenever
/// the pool got to it. Two contracts matter and both are pinned here: nothing runs
/// inline (running the producer during Load was tried and broke Junction's reveal flow,
/// which needs the value to arrive on a later frame), and nothing needs wall-clock time
/// (waiting on the pool under machine load produced a 30s timeout that was twice
/// misdiagnosed as a reactivity regression — see the note in Signal.cs).
/// </summary>
public class DeferredAsyncLoadTests
{
    [Fact]
    public void NothingRunsDuringLoad_TheValueLandsOnTheNextPump()
    {
        using var session = HeadlessSession.Create();
        var loader = new AsyncLoader<string>("empty");
        bool produced = false;

        loader.Load("key", () => { produced = true; return "loaded"; });

        Assert.False(produced);
        Assert.True(loader.IsLoading);
        Assert.Equal("empty", loader.Value);

        session.PumpFrame();

        Assert.True(produced);
        Assert.False(loader.IsLoading);
        Assert.Equal("loaded", loader.Value);
    }

    [Fact]
    public void ALoadRequestedDuringBuild_IsBuiltWithOneFrameLater()
    {
        using var session = HeadlessSession.Create();
        var loader = new AsyncLoader<string>("empty");
        var builtWith = new List<string>();

        session.Window.SetRoot(new ComponentHost(new Loading(loader, builtWith)));

        session.PumpFrame();   // build 1 requests the load and renders without it
        session.PumpFrame();   // the producer runs, the value applies, build 2 has it

        Assert.Equal(["empty", "loaded"], builtWith);
        Assert.NotNull(session.Window.GetByText("loaded"));
    }

    [Fact]
    public void ASupersededLoad_IsDroppedEvenThoughItsProducerAlreadyRan()
    {
        using var session = HeadlessSession.Create();
        var loader = new AsyncLoader<string>("empty");

        loader.Load(1, () => "one");
        loader.Load(2, () => "two");

        // Both producers run in the same drain, in order; "one" completes against a
        // newer generation and is discarded.
        session.PumpFrame();

        Assert.Equal("two", loader.Value);
        Assert.False(loader.IsLoading);
    }

    [Fact]
    public void AProducerThatThrows_ClearsTheSpinnerAndKeepsTheOldValue()
    {
        using var session = HeadlessSession.Create();
        var loader = new AsyncLoader<string>("empty");

        loader.Load("key", () => throw new InvalidOperationException("read failed"));
        session.PumpFrame();

        Assert.False(loader.IsLoading);
        Assert.Equal("empty", loader.Value);
    }

    [Fact]
    public void AFailedLoad_IsRetriedUntilItDelivers()
    {
        // The one case the loader used to give up on silently. A producer that throws had
        // its key recorded as loaded anyway, so every later Load for it was a cache hit and
        // the seed value stayed on screen for the life of the process — a Junction
        // repository frozen at "…" after one status call lost a race with git's own index
        // write. The key now advances on delivery, and a failure is retried.
        using var session = HeadlessSession.Create();
        var loader = new AsyncLoader<string>("empty");
        int attempts = 0;

        loader.Load("key", () =>
        {
            if (++attempts == 1) throw new InvalidOperationException("the writer still had it open");
            return "loaded";
        });

        session.PumpFrame();
        Assert.Equal(1, attempts);
        Assert.Equal("empty", loader.Value);

        // The one wall-clock wait in this file: the backoff is a real delay (the point is
        // to let whoever refused the read finish), so the pump has to run across it. The
        // deadline is generous and the poll is tight — it exits on the value, not the clock.
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        while (loader.Value == "empty" && DateTime.UtcNow < deadline)
        {
            session.PumpFrame();
            Thread.Sleep(20);
        }

        Assert.Equal(2, attempts);
        Assert.Equal("loaded", loader.Value);
        Assert.False(loader.IsLoading);
    }

    [Fact]
    public void AFailedLoad_DoesNotRestartOnEveryRebuild()
    {
        // The failure clears the loading flag, which is a reactive write, which rebuilds,
        // and the rebuild asks for the same key again. That has to be refused until the
        // backoff is up: a producer retried once per rebuild is a subprocess storm.
        using var session = HeadlessSession.Create();
        var loader = new AsyncLoader<string>("empty");
        int attempts = 0;

        Func<string> broken = () => { attempts++; throw new InvalidOperationException("broken"); };

        loader.Load("key", broken);
        session.PumpFrame();
        Assert.Equal(1, attempts);

        for (int i = 0; i < 5; i++)
        {
            loader.Load("key", broken);
            session.PumpFrame();
        }

        Assert.Equal(1, attempts);
    }

    [Fact]
    public void ALoadRequestedByOnLoaded_WaitsForItsOwnFrame()
    {
        // A completion that requests another load (a reveal chasing a two-step fetch)
        // must not have the second producer run in the same drain as the first — the
        // drain only takes what was pending when the frame began.
        using var session = HeadlessSession.Create();
        var first = new AsyncLoader<string>("empty");
        var second = new AsyncLoader<string>("empty");

        first.Load("a", () => "one", onLoaded: _ => second.Load("b", () => "two"));

        session.PumpFrame();
        Assert.Equal("one", first.Value);
        Assert.Equal("empty", second.Value);
        Assert.True(second.IsLoading);

        session.PumpFrame();
        Assert.Equal("two", second.Value);
    }

    [Fact]
    public void DisposingTheSession_GivesTheThreadPoolBack()
    {
        var loader = new AsyncLoader<string>("empty");

        using (HeadlessSession.Create())
        {
        }

        // No session: the hop is a real Task.Run again, and with no Application the
        // completion is dropped by InvokeOnMainThread rather than queued forever.
        var producedOffThread = new ManualResetEventSlim();
        loader.Load("key", () => { producedOffThread.Set(); return "loaded"; });

        Assert.True(producedOffThread.Wait(TimeSpan.FromSeconds(10)),
            "the producer never ran — the deferred scheduler leaked out of the session");
    }

    private sealed class Loading(AsyncLoader<string> loader, List<string> builtWith) : Component
    {
        public override Element Build()
        {
            loader.Load("key", () => "loaded");
            builtWith.Add(loader.Value);
            return Text.New(loader.Value);
        }
    }
}
