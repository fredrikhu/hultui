using System.Globalization;
using System.Text.RegularExpressions;
using Xunit;
using Xunit.Abstractions;

namespace HultUI.UiTests;

/// <summary>
/// A video player's bar fades without keeping a window-sized texture. The bar holds the scrubber, the volume slider
/// and three drawn glyphs, which could not say where they paint, so every fade frame drew the bar through an
/// offscreen layer the window's size, and Skia kept that layer's texture for the window's life: the first fade took
/// 12 MB of video memory in Reel's 1920x1140 window, 32 MB in a 3800x2100 one (plan 17, round 17). Now they report
/// their bounds and the bar fades in a layer of its own size. Measured in a process of its own
/// (<c>HultUI.UiTests.VulkanProbe --bar-fade</c>), as the process's local video memory from DXGI: a driver keeps the
/// memory a process freed, so an earlier test's layers would hide this one's.
/// </summary>
[Collection("ui")]
public sealed partial class BarFadeGpuMemoryTests(ITestOutputHelper output)
{
    [UiFact]
    public void OnGL_TheFirstBarFade_GrowsVideoMemoryByTheBarsLayerAtMost() => Check("--renderer=default --bar-fade");

    [VulkanProbeTests.VulkanUiFact]
    public void OnVulkan_TheFirstBarFade_GrowsVideoMemoryByTheBarsLayerAtMost() => Check("--bar-fade");

    private void Check(string arguments)
    {
        if (!OperatingSystem.IsWindows()) return;   // DXGI's count of a process's video memory
        // The bridge off: a reader's threads and trees are not what this counts.
        var run = VulkanProbeTests.RunProbe(new() { ["HULTUI_A11Y"] = "0" }, arguments);
        output.WriteLine(run.Output);
        Assert.True(run.ExitCode == 0, run.Output);

        var m = Result().Match(run.Output);
        Assert.True(m.Success, run.Output);
        static double D(Group g) => double.Parse(g.Value, CultureInfo.InvariantCulture);
        int windowW = (int)D(m.Groups["ww"]), windowH = (int)D(m.Groups["wh"]);
        int barW = (int)D(m.Groups["bw"]), barH = (int)D(m.Groups["bh"]);
        double before = D(m.Groups["before"]), first = D(m.Groups["first"]), second = D(m.Groups["second"]);
        Assert.True(m.Groups["hidden"].Value == "True", "the bar did not hide:\n" + run.Output);

        // Skia makes a layer's texture at an approximate size (GrResourceProvider::MakeApprox), padded a pixel each
        // side for pixel snapping, and the driver may take as much again beside it. 1 MB more is the driver's own
        // bookkeeping, which this does not pin. NVIDIA, 1920x1140 at 150 %: +12.0 MB for the window-sized layer
        // (2048x1536), +0.0 for the bar's on Vulkan.
        double windowLayer = Megabytes(Approx(windowW), Approx(windowH));
        double barLayer = 2 * Megabytes(Approx(barW + 2), Approx(barH + 2));
        Assert.True(barLayer + 1 < windowLayer,
            $"the bar's layer and as much again ({barLayer:0.0} MB) would be no smaller than a window-sized layer ({windowLayer:0.0} MB): the test cannot tell them apart");
        Assert.True(first - before <= barLayer + 1,
            $"the first fade grew local video memory by {first - before:0.0} MB in a {windowW}x{windowH} window, more than the {barW}x{barH} bar's layer and as much again ({barLayer:0.0} MB); a window-sized layer is {windowLayer:0.0} MB");
        // The second fade draws through the layer the first one left.
        Assert.True(second - first <= 0.5, $"the second fade grew local video memory by {second - first:0.0} MB");
    }

    private static double Megabytes(int width, int height) => width * (double)height * 4 / 1048576.0;

    /// <summary>Skia's approximate texture size for one dimension (GrResourceProvider::MakeApprox): at least 16, a
    /// power of two up to 1024, past that the next power of two or the one-and-a-half step before it.</summary>
    private static int Approx(int value)
    {
        value = Math.Max(16, value);
        int ceil = (int)System.Numerics.BitOperations.RoundUpToPowerOf2((uint)value);
        if (ceil == value || value <= 1024) return ceil;
        int mid = ceil / 2 + ceil / 4;
        return value <= mid ? mid : ceil;
    }

    [GeneratedRegex(@"probe: bar-fade window (?<ww>\d+)x(?<wh>\d+) bar (?<bw>\d+)x(?<bh>\d+) hidden (?<hidden>\w+) local before (?<before>[\d.]+) first (?<first>[\d.]+) second (?<second>[\d.]+) MB")]
    private static partial Regex Result();
}
