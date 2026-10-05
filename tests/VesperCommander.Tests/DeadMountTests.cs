using System;
using System.IO;
using System.Linq;
using System.Threading;
using HultUI.Core.Events;
using Xunit;
using Xunit.Abstractions;

namespace VesperCommander.Tests;

/// <summary>Runs only by hand, against a network mount that has stopped answering:
/// <c>VESPER_DEAD_MOUNT</c> names a folder under one. <c>scripts/dead-mount-test.sh</c> makes
/// one (sshfs to a private sshd, then the sshfs process stopped) and runs these.</summary>
public sealed class DeadMountFactAttribute : FactAttribute
{
    public static readonly string? Target = Environment.GetEnvironmentVariable("VESPER_DEAD_MOUNT");

    public DeadMountFactAttribute()
    {
        if (string.IsNullOrEmpty(Target)) Skip = "Run by hand: scripts/dead-mount-test.sh";
    }
}

/// <summary>
/// A path under a network mount whose server has gone, on Linux. Vesper keeps the window
/// working on a dead Windows share by treating UNC paths and mapped drive letters as remote and
/// asking about them off the UI thread, under a time limit. A Linux network mount — NFS, SMB,
/// sshfs — was none of those, so it was asked about on the UI thread, where a mount that does
/// not answer holds the call, and the window, for as long as the kernel waits: a hard NFS mount
/// waits for ever.
/// </summary>
public class DeadMountTests(ITestOutputHelper output)
{
    /// <summary>Each step runs on a thread of its own, the test's UI thread, so that a step that
    /// hangs fails the test instead of hanging the run. The thread is left behind, stuck, with its
    /// session, so each of these needs a test run of its own (the script runs them one by one).</summary>
    private static (bool Returned, Exception? Failure) Within(TimeSpan limit, Action<GotoPathTests.Fixture> step,
        Action<GotoPathTests.Fixture>? after = null)
    {
        Exception? failure = null;
        var done = new ManualResetEventSlim();
        var ui = new Thread(() =>
        {
            GotoPathTests.Fixture? fx = null;
            try
            {
                fx = new GotoPathTests.Fixture();
                step(fx);
            }
            catch (Exception e) { failure = e; }
            finally { done.Set(); }
            try { if (fx is not null) after?.Invoke(fx); } catch { }
        }) { IsBackground = true };
        ui.Start();
        return (done.Wait(limit), failure);
    }

    [DeadMountFact]
    public void TheMountIsANetworkOne()
    {
        var target = DeadMountFactAttribute.Target!;
        // From /proc/self/mountinfo only: IsReady or a size would ask the dead mount itself.
        var mount = DriveInfo.GetDrives()
            .Where(d => target.StartsWith(d.Name.TrimEnd('/') + "/", StringComparison.Ordinal))
            .OrderByDescending(d => d.Name.Length)
            .First();
        output.WriteLine($"{mount.Name}: format {mount.DriveFormat}, type {mount.DriveType}");
        Assert.True(PathProbe.IsRemote(target), $"{target} is not treated as remote ({mount.DriveFormat}, {mount.DriveType})");
    }

    [DeadMountFact]
    public void GoingToAPathUnderItKeepsTheWindowWorking()
    {
        bool handedOff = false;
        var (returned, failure) = Within(TimeSpan.FromSeconds(5), fx =>
        {
            fx.OpenPrompt();
            fx.Session.Window.TypeText(DeadMountFactAttribute.Target!);
            fx.Session.Window.KeyDown(KeyCode.Enter);
            handedOff = fx.Probe != null;
        }, after: fx => fx.Session.Window.KeyDown(KeyCode.Escape));

        Assert.True(returned, "Enter in the go-to prompt is still waiting on the mount: the window is frozen");
        Assert.True(failure is null, failure?.ToString());
        Assert.True(handedOff, "the path was resolved on the UI thread rather than handed off");
    }

    /// <summary>The drive picker lists every mount, the dead one included, and asked each whether
    /// it was ready and how big it was: questions only the mount can answer.</summary>
    [DeadMountFact]
    public void OpeningTheDrivePickerKeepsTheWindowWorking()
    {
        var (returned, failure) = Within(TimeSpan.FromSeconds(5),
            fx => fx.Session.Window.PressKey(KeyCode.F1, KeyModifiers.Alt),
            after: fx => fx.Session.Window.KeyDown(KeyCode.Escape));

        Assert.True(returned, "Alt+F1 is still waiting on the mount: the window is frozen");
        Assert.True(failure is null, failure?.ToString());
    }
}
