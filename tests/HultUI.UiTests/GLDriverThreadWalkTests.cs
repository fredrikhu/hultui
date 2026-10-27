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
/// sets the walk off as the window first shows (<c>SDL3Window.StartDriverThreadWalk</c>), so a
/// request's threads cost no frame. In a process of its own
/// (<c>HultUI.UiTests.VulkanProbe --first-thread</c>): the walk comes once a process, and this
/// process has had its own long since.
/// </summary>
[Collection("ui")]
public sealed partial class GLDriverThreadWalkTests(ITestOutputHelper output)
{
    [UiFact]
    public void OnNvidiaGL_AnAppsFirstThreads_CostNoFrame()
    {
        if (!OperatingSystem.IsWindows()) return;   // the walk was seen on Windows only
        // The bridge off: a reader's threads would start the walk before the probe's own.
        var run = VulkanProbeTests.RunProbe(new() { ["HULTUI_A11Y"] = "0" }, "--renderer=default --first-thread");
        output.WriteLine(run.Output);
        Assert.True(run.ExitCode == 0, run.Output);
        if (!run.Output.Contains("vendor=\"NVIDIA", StringComparison.Ordinal))
        {
            output.WriteLine("Not NVIDIA's GL driver: nothing to check.");
            return;
        }

        var times = FramesAfterThePoolGrew().Match(run.Output);
        Assert.True(times.Success, run.Output);
        double first = double.Parse(times.Groups[1].Value, CultureInfo.InvariantCulture);
        double second = double.Parse(times.Groups[2].Value, CultureInfo.InvariantCulture);
        // A frame here takes 1–6 ms; one waiting out the walk 50–70.
        Assert.True(Math.Max(first, second) < 25, $"a frame after the first threads took {first} and {second} ms: the driver walked then");
    }

    [GeneratedRegex(@"frames after the pool grew ([\d.]+) ms, ([\d.]+) ms")]
    private static partial Regex FramesAfterThePoolGrew();
}
