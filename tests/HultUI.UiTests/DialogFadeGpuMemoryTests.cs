using System.Globalization;
using System.Text.RegularExpressions;
using Xunit;
using Xunit.Abstractions;

namespace HultUI.UiTests;

/// <summary>
/// A dialog's fade-in keeps no window-sized texture. It used to fade the full-window scrim's opacity, so every fade
/// frame drew the whole window through an offscreen layer, and on GL Skia kept that layer's texture in its cache for
/// the window's life: the first command palette open took 18 MB of video memory in a 2160x1350 window, 32 MB
/// maximized on a 4K screen (plan 17, round 16). Now the scrim's colour fades by itself and the panel fades in a
/// layer of its own size. Measured in a process of its own (<c>HultUI.UiTests.VulkanProbe --dialog-fade</c>), as
/// the process's local video memory from DXGI: a driver keeps the memory a process freed, so an earlier test's
/// layers would hide this one's.
/// </summary>
[Collection("ui")]
public sealed partial class DialogFadeGpuMemoryTests(ITestOutputHelper output)
{
    [UiFact]
    public void OnGL_TheFirstDialogOpen_GrowsVideoMemoryByThePanelsLayerAtMost()
    {
        if (!OperatingSystem.IsWindows()) return;   // DXGI's count of a process's video memory
        // The bridge off: a reader's threads and trees are not what this counts.
        var run = VulkanProbeTests.RunProbe(new() { ["HULTUI_A11Y"] = "0" }, "--renderer=default --dialog-fade");
        output.WriteLine(run.Output);
        Assert.True(run.ExitCode == 0, run.Output);

        var m = Result().Match(run.Output);
        Assert.True(m.Success, run.Output);
        static double D(Group g) => double.Parse(g.Value, CultureInfo.InvariantCulture);
        int windowW = (int)D(m.Groups["ww"]), windowH = (int)D(m.Groups["wh"]);
        int panelW = (int)D(m.Groups["pw"]), panelH = (int)D(m.Groups["ph"]);
        double before = D(m.Groups["before"]), first = D(m.Groups["first"]), second = D(m.Groups["second"]);

        // Skia makes a layer's texture at an approximate size (GrResourceProvider::MakeApprox), and the group
        // is padded a pixel each side for pixel snapping. The driver takes as much again beside it (NVIDIA:
        // 2160x1350 at 150 %, a 1024x1024 layer, +8.0 MB; the window-sized fade's 3072x1536 layer was +18.0).
        // 1 MB more is the driver's own bookkeeping, which this does not pin.
        double windowLayer = Megabytes(Approx(windowW), Approx(windowH));
        double panelLayer = 2 * Megabytes(Approx(panelW + 2), Approx(panelH + 2));
        Assert.True(panelLayer + 1 < windowLayer,
            $"the panel's layer and as much again ({panelLayer:0.0} MB) would be no smaller than a window-sized layer ({windowLayer:0.0} MB): the test cannot tell them apart");
        Assert.True(first - before <= panelLayer + 1,
            $"the first open grew local video memory by {first - before:0.0} MB in a {windowW}x{windowH} window, more than the {panelW}x{panelH} panel's layer and as much again ({panelLayer:0.0} MB); a window-sized layer is {windowLayer:0.0} MB");
        // The second open draws through the layer the first one left.
        Assert.True(second - first <= 0.5, $"the second open grew local video memory by {second - first:0.0} MB");
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

    [GeneratedRegex(@"probe: dialog-fade window (?<ww>\d+)x(?<wh>\d+) panel (?<pw>\d+)x(?<ph>\d+) local before (?<before>[\d.]+) first (?<first>[\d.]+) second (?<second>[\d.]+) MB")]
    private static partial Regex Result();
}
