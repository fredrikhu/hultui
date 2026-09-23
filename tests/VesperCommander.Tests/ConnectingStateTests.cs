using System;
using System.IO;
using System.Reflection;
using System.Threading;
using HultUI.Core.Events;
using HultUI.Headless;
using Xunit;

namespace VesperCommander.Tests;

/// <summary>
/// The pane's <see cref="PaneState.Connecting"/> block: what a pane shows while it is waiting
/// on a folder it is not already displaying.
///
/// <para>The panes keep the last folder's rows while a new one loads, which is right for a
/// local read that lands in a frame and wrong for a share that may never answer — the rows
/// belong to somewhere else, and they are operable. So the state turns on the two things that
/// distinguish those cases: whether the folder being waited for is the one on screen, and
/// whether the wait has gone on long enough to be worth a word.</para>
///
/// <para>Asserted on the state and the wiring rather than on eventual pixels. A pane that had
/// the data but never re-rendered would satisfy any test that pumped until the content showed
/// up — the polling would be doing the un-sticking that the idle app cannot do for itself.</para>
/// </summary>
public class ConnectingStateTests
{
    [Fact]
    public void WaitingOnADifferentFolderShowsTheConnectingBlock()
    {
        using var fx = new Fixture(connectingDelayMs: 0);

        fx.Navigate('L', fx.Sub);

        Assert.Equal(PaneState.Connecting, fx.State('L'));
        Assert.Equal(fx.Root, fx.ShownDir('L'));   // the rows still belong to where we came from
    }

    /// <summary>And it goes away by itself once the folder lands — the state is a description of
    /// the wait, not something that has to be cleared.</summary>
    [Fact]
    public void ItClearsWhenTheFolderArrives()
    {
        using var fx = new Fixture(connectingDelayMs: 0);

        fx.Navigate('L', fx.Sub);
        Assert.Equal(PaneState.Connecting, fx.State('L'));

        fx.Land();   // the producer runs and its value applies

        Assert.Equal(PaneState.Ok, fx.State('L'));
        Assert.Equal(fx.Sub, fx.ShownDir('L'));
    }

    /// <summary>Refreshing the folder already on screen keeps its rows and says nothing. There
    /// is nothing to wait for that is not already there, and replacing a good listing with a
    /// spinner on every Ctrl+R would be a flicker with no information in it.</summary>
    [Fact]
    public void RefreshingTheFolderOnScreenDoesNotShowIt()
    {
        using var fx = new Fixture(connectingDelayMs: 0);

        fx.Press(KeyCode.R, KeyModifiers.Ctrl);

        Assert.NotEqual(PaneState.Connecting, fx.State('L'));
    }

    /// <summary>The threshold keeps it off a local folder entirely: a read that lands inside it
    /// never gets a spinner, so the state belongs to slow things only. Virtual time makes this a
    /// count of frames rather than a race with whatever else the machine is doing.</summary>
    [Fact]
    public void AFolderThatLandsInsideTheThresholdNeverShowsIt()
    {
        using var fx = new Fixture();   // the real 350 ms default

        fx.Navigate('L', fx.Sub);

        Assert.NotEqual(PaneState.Connecting, fx.State('L'));
    }

    [Fact]
    public void PastTheThresholdTheSameWaitDoesShowIt()
    {
        using var fx = new Fixture();

        fx.Navigate('L', fx.Sub);                                 // the load goes out…
        fx.Session.AdvanceTime(TimeSpan.FromMilliseconds(400));   // …and is still out 400 ms on

        Assert.Equal(PaneState.Connecting, fx.State('L'));
    }

    // ── Wiring ───────────────────────────────────────────────────────────

    /// <summary>Navigating away cancels the listing being left. The walk may not stop at once —
    /// a directory read is not cancellable mid-syscall — but the token is what a long walk
    /// checks between entries, so this is the signal that has to be sent.</summary>
    [Fact]
    public void NavigatingAwayCancelsTheListingItLeaves()
    {
        using var fx = new Fixture();
        var leaving = fx.LoadCts('L');
        Assert.NotNull(leaving);
        Assert.False(leaving!.IsCancellationRequested);

        fx.Navigate('L', fx.Sub);

        Assert.True(leaving.IsCancellationRequested);
        Assert.NotSame(leaving, fx.LoadCts('L'));   // and the new load got its own
    }

    /// <summary>Cancel in the connecting block gives up and goes back to the folder the pane was
    /// showing, rather than leaving it stranded on one that never answered.</summary>
    [Fact]
    public void CancelReturnsToTheFolderThePaneWasShowing()
    {
        using var fx = new Fixture(connectingDelayMs: 0);
        fx.Navigate('L', fx.Sub);
        Assert.Equal(PaneState.Connecting, fx.State('L'));

        fx.Invoke("CancelPaneLoad", 'L');

        Assert.Equal(fx.Root, fx.Dir('L'));
    }

    /// <summary>A pane that is still connecting has not loaded. <c>ListingsReady</c> is the seam
    /// every app-hosting test waits on, so a Connecting pane claiming to be ready would let the
    /// whole suite start asserting against a folder nobody has read yet.</summary>
    [Fact]
    public void ConnectingIsNotReady()
    {
        using var fx = new Fixture(connectingDelayMs: 0, settle: false);

        Assert.Equal(PaneState.Connecting, fx.State('L'));
        Assert.False(fx.App.ListingsReady);
    }

    /// <summary>It renders, and not merely as an enum: the block puts a Cancel in the pane.
    /// (The review shortcut Ctrl+Shift+N forces the same state without a server.)</summary>
    [Fact]
    public void TheBlockActuallyRendersWithItsCancel()
    {
        using var fx = new Fixture();

        fx.Press(KeyCode.N, KeyModifiers.Ctrl | KeyModifiers.Shift);

        Assert.Contains("Connecting to", fx.Session.Window.DumpAccessibilityTree());
    }

    // ── Fixture ──────────────────────────────────────────────────────────

    /// <summary>Temp tree (root/sub) + a hosted CommanderApp pinned at root, settled.</summary>
    private sealed class Fixture : IDisposable
    {
        public readonly string Root, Sub;
        public readonly HeadlessSession Session;
        public readonly CommanderApp App;

        public Fixture(int? connectingDelayMs = null, bool settle = true)
        {
            Root = Path.Combine(Path.GetTempPath(), "vesper-conn-" + Guid.NewGuid().ToString("N"));
            Sub = Path.Combine(Root, "sub");
            Directory.CreateDirectory(Sub);
            File.WriteAllText(Path.Combine(Root, "a.txt"), "x");
            File.WriteAllText(Path.Combine(Sub, "b.txt"), "x");

            Session = HeadlessSession.Create(1240, 820);
            App = new CommanderApp(Session.Application, Session.Window.Window, Root);
            if (connectingDelayMs is { } ms) App.ConnectingDelayMs = ms;
            Session.Application.SetRoot(App.Mount());
            // Pin both panes regardless of any restored session, then let the first listings
            // land so every test starts from a settled pane rather than the cold-start wait.
            Invoke("NavigateTo", 'L', Root, null, false);
            Invoke("NavigateTo", 'R', Root, null, false);
            if (settle) Session.PumpUntil(() => App.ListingsReady);
        }

        /// <summary>Move a pane, without pumping. The rebuild that <c>NavigateTo</c> marks runs
        /// synchronously, so the load is already in flight when this returns — and one pump
        /// afterwards runs the producer *and* applies its value, which is why the state is
        /// observable here and not after a frame.</summary>
        public void Navigate(char side, string dir) => Invoke("NavigateTo", side, dir, null, false);

        /// <summary>The one pump that completes whatever <see cref="Navigate"/> started.</summary>
        public void Land() => Session.PumpFrame();

        public void Press(KeyCode key, KeyModifiers modifiers = KeyModifiers.None)
        {
            Session.Window.PressKey(key, modifiers);
            Session.PumpFrame();
        }

        public PaneState State(char side) => (PaneState)Method("PaneStateOf").Invoke(App, [side])!;

        public string Dir(char side) => (string)Method("Dir").Invoke(App, [side])!;

        public string? ShownDir(char side) => ((string?[])Field("_shownDir"))[side == 'L' ? 0 : 1];

        public CancellationTokenSource? LoadCts(char side)
            => ((CancellationTokenSource?[])Field("_loadCts"))[side == 'L' ? 0 : 1];

        public void Invoke(string method, params object?[] args) => Method(method).Invoke(App, args);

        private static MethodInfo Method(string name)
            => typeof(CommanderApp).GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance)!;

        private object Field(string name)
            => typeof(CommanderApp).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!
                .GetValue(App)!;

        public void Dispose()
        {
            (App as IDisposable)?.Dispose();
            Session.Dispose();
            try { Directory.Delete(Root, true); } catch { /* best-effort temp cleanup */ }
        }
    }
}
