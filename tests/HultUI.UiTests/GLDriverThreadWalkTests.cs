using System.Globalization;
using System.Text.RegularExpressions;
using Xunit;
using Xunit.Abstractions;

namespace HultUI.UiTests;

/// <summary>
/// NVIDIA's GL driver on Windows walks every thread on the system once a process, on its own
/// thread, after the first present that follows a thread starting in the process, and the UI
/// thread's next GL call waits it out: ~55 ms. Left alone, that was the second frame of an app's
/// first request (Meridian's first send: 61–63 ms where a repeat send takes 4–6). The framework
/// sets the walk off once the window has settled (0.2 s after its first frame) and rests, with a
/// frame of its own (<c>SDL3Window.StartDriverThreadWalk</c>), so a request's threads cost no frame
/// and the app's own start-up frames wait for nothing. In a process of its own
/// (<c>HultUI.UiTests.VulkanProbe --first-thread</c>): the walk comes once a process, and this
/// process has had its own long since.
/// </summary>
[Collection("ui")]
public sealed partial class GLDriverThreadWalkTests(ITestOutputHelper output)
{
    [UiFact]
    public void OnNvidiaGL_AnAppsFirstThreads_CostNoFrame()
    {
        if (Probe("--first-thread") is not { } run) return;

        var (first, second) = FramesAfterThePoolGrew(run);
        // A frame here takes 1–6 ms; one waiting out the walk 50–70.
        Assert.True(Math.Max(first, second) < 25, $"a frame after the first threads took {first} and {second} ms: the driver walked then");
        // The walk's own frame comes once the window has settled, not with the next frame after
        // the first, which in an app is its start-up work (Junction's repository view, ~95 ms on).
        // The window's own first events draw a frame of their own ~10 ms after the first.
        var settled = FramesDrawnUnasked(run).Where(t => t >= 100).ToList();
        Assert.True(settled.Count == 1, $"frames drawn unasked from 100 ms: {settled.Count} (one is the walk's)\n{run}");
        Assert.True(settled[0] >= 150, $"the walk's frame came {settled[0]} ms after the first: before the window settled");
    }

    [UiFact]
    public void OnNvidiaGL_TheWalkWaits_ForTheWindowToRest()
    {
        // Busy well past the settle delay (0.2 s): a new frame every pass for 0.8 s, then 0.4 s of rest.
        if (Probe("--first-thread-busy") is not { } run) return;

        // Set off while the window drew, the walk's frame would have been one of the work's and
        // none would come in the rest.
        var unasked = FramesDrawnUnasked(run);
        Assert.True(unasked.Count == 1, $"frames drawn unasked in the rest after the work: {unasked.Count} (one is the walk's)\n{run}");
        var (first, second) = FramesAfterThePoolGrew(run);
        Assert.True(Math.Max(first, second) < 25, $"a frame after the first threads took {first} and {second} ms: the driver walked then");
    }

    /// <summary>The probe's output, or null where there is nothing to check (not Windows, not NVIDIA's GL).</summary>
    private string? Probe(string mode)
    {
        if (!OperatingSystem.IsWindows()) return null;   // the walk was seen on Windows only
        // The bridge off: a reader's threads would start the walk before the probe's own.
        var run = VulkanProbeTests.RunProbe(new() { ["HULTUI_A11Y"] = "0" }, "--renderer=default " + mode);
        output.WriteLine(run.Output);
        Assert.True(run.ExitCode == 0, run.Output);
        if (!run.Output.Contains("vendor=\"NVIDIA", StringComparison.Ordinal))
        {
            output.WriteLine("Not NVIDIA's GL driver: nothing to check.");
            return null;
        }
        return run.Output;
    }

    private static (double First, double Second) FramesAfterThePoolGrew(string run)
    {
        var times = FramesAfterThePoolGrewLine().Match(run);
        Assert.True(times.Success, run);
        return (double.Parse(times.Groups[1].Value, CultureInfo.InvariantCulture),
            double.Parse(times.Groups[2].Value, CultureInfo.InvariantCulture));
    }

    /// <summary>When, in ms after the first frame, the loop drew a frame the probe did not ask for.</summary>
    private static List<double> FramesDrawnUnasked(string run)
    {
        var line = FramesDrawnUnaskedLine().Match(run);
        Assert.True(line.Success, run);
        return line.Groups[1].Value.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(t => double.Parse(t, CultureInfo.InvariantCulture)).ToList();
    }

    [GeneratedRegex(@"frames after the pool grew ([\d.]+) ms, ([\d.]+) ms")]
    private static partial Regex FramesAfterThePoolGrewLine();

    [GeneratedRegex(@"frames drawn unasked from \d+ ms: \d+, at ([\d. ]*) ms after the first")]
    private static partial Regex FramesDrawnUnaskedLine();
}
