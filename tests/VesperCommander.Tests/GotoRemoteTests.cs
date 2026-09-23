using System;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using HultUI.Core.Events;
using HultUI.Headless;
using Xunit;

namespace VesperCommander.Tests;

/// <summary>
/// Ctrl+G against a path that lives on the network. The behaviour under test is that the
/// window keeps working: a share whose host never answers used to freeze the UI thread inside
/// <c>Directory.Exists</c> for as long as the operating system was willing to wait — measured
/// at 21 seconds — because the resolution ran straight off the key press.
///
/// <para>The dial is faked (<see cref="PathProbe.ConnectOverride"/>), so these are tests of
/// Vesper's flow and not of the runner's network. <see cref="PathProbeTests"/> covers the
/// probe's own rules.</para>
/// </summary>
public class GotoRemoteTests : IDisposable
{
    private const string Share = @"\\vesper-test-host\share";

    public GotoRemoteTests() => PathProbe.ResetCaches();

    public void Dispose()
    {
        PathProbe.ConnectOverride = null;
        PathProbe.ResetCaches();
    }

    /// <summary>The deterministic half: committing a remote path hands the work off and marks
    /// the dialog busy <i>within the key press</i>, with the pane still where it was. No
    /// pumping — if this needed a frame to become true, the UI thread did the waiting.</summary>
    [Fact]
    public void ARemotePathIsHandedOffWithinTheKeyPress()
    {
        PathProbe.ConnectOverride = (_, _, ct) => Task.Delay(Timeout.Infinite, ct);
        using var fx = new GotoPathTests.Fixture();
        fx.OpenPrompt();

        fx.Session.Window.TypeText(Share);
        fx.Session.Window.KeyDown(KeyCode.Enter);

        Assert.NotNull(fx.Probe);            // a probe is out…
        Assert.True(fx.PromptOpen);          // …the dialog is still up to say so…
        Assert.Equal(fx.Root, fx.Dir('L'));  // …and nothing has moved yet
    }

    /// <summary>A host that never answers still lands the pane on the share. The pane's own
    /// loader reports it offline with a Reconnect button — a better place to wait than a modal
    /// dialog, and the only place that can retry.</summary>
    [Fact]
    public void AnUnreachableHostNavigatesAnywayAndClosesThePrompt()
    {
        // An instantly-cancelled dial is what a budget that ran out looks like from here.
        PathProbe.ConnectOverride = (_, _, _) => Task.FromException(new OperationCanceledException());
        using var fx = new GotoPathTests.Fixture();
        fx.OpenPrompt();

        fx.Session.Window.TypeText(Share);
        fx.Session.Window.KeyDown(KeyCode.Enter);
        fx.Session.PumpUntil(() => fx.Probe == null);

        Assert.Equal(Share, fx.Dir('L'));
        Assert.False(fx.PromptOpen);
    }

    /// <summary>Escape during the probe is an answer too: the dialog closes, and the result —
    /// which arrives afterwards, because nothing can call the syscall off — must not navigate a
    /// pane the user has stopped asking about.</summary>
    [Fact]
    public void EscapeDuringTheProbeAbandonsIt()
    {
        var released = new TaskCompletionSource();
        PathProbe.ConnectOverride = async (_, _, ct) =>
        {
            await released.Task.WaitAsync(ct).ConfigureAwait(false);
        };

        using var fx = new GotoPathTests.Fixture();
        fx.OpenPrompt();
        fx.Session.Window.TypeText(Share);
        fx.Session.Window.KeyDown(KeyCode.Enter);
        Assert.NotNull(fx.Probe);

        fx.Session.Window.KeyDown(KeyCode.Escape);
        Assert.Null(fx.Probe);
        Assert.False(fx.PromptOpen);

        // Let the abandoned dial finish and give its result every chance to be acted on.
        released.SetResult();
        fx.Session.PumpFrames(5);

        Assert.Equal(fx.Root, fx.Dir('L'));   // it stayed abandoned
    }

    /// <summary>The invariant that keeps this a small change: a local path never touches any of
    /// it. No dial, no hand-off, and the navigation is done by the time the key press returns —
    /// which is what the five existing <see cref="GotoPathTests"/> assert without pumping.</summary>
    [Fact]
    public void ALocalPathNeitherDialsNorDefers()
    {
        int dials = 0;
        PathProbe.ConnectOverride = (_, _, _) => { dials++; return Task.CompletedTask; };
        using var fx = new GotoPathTests.Fixture();
        fx.OpenPrompt();

        fx.Session.Window.TypeText(fx.Sub);
        fx.Session.Window.KeyDown(KeyCode.Enter);

        Assert.Equal(0, dials);
        Assert.Null(fx.Probe);
        Assert.Equal(fx.Sub, fx.Dir('L'));
        Assert.False(fx.PromptOpen);
    }

    /// <summary>The command line's "cd" shares the resolution path but has no dialog to hold
    /// the outcome, so a remote target must still land rather than fall on the floor.</summary>
    [Fact]
    public void CommandLineCdReachesAShareToo()
    {
        PathProbe.ConnectOverride = (_, _, _) => Task.FromException(new OperationCanceledException());
        using var fx = new GotoPathTests.Fixture();
        fx.Invoke("OpenCommandLine", 'L');
        fx.Session.PumpUntil(() =>
            fx.Session.Window.Window.FocusedElement is HultUI.Core.Elements.TextInputElement);

        fx.Session.Window.TypeText("cd " + Share);
        fx.Session.Window.KeyDown(KeyCode.Enter);
        fx.Session.PumpUntil(() => fx.Probe == null);

        Assert.Equal(Share, fx.Dir('L'));
    }
}
