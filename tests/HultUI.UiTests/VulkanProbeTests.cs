using System.Diagnostics;
using System.Runtime.InteropServices;
using Xunit;

namespace HultUI.UiTests;

/// <summary>
/// The Vulkan renderer on a real window, in a process of its own
/// (<c>HultUI.UiTests.VulkanProbe</c>). The renderer is chosen once per process, and the
/// Vulkan loader reads its fault-injection variables when it starts. The headless driver
/// renders through a raster surface and never touches this renderer, so a swapchain capture
/// is the only direct evidence that it draws, and a real driver the only way to reach the
/// fallback.
/// </summary>
[Collection("ui")]
public sealed class VulkanProbeTests
{
    /// <summary>Skips unless UI tests are on and a Vulkan loader is installed.</summary>
    private sealed class VulkanUiFactAttribute : FactAttribute
    {
        public VulkanUiFactAttribute()
        {
            if (!UiTestGate.Enabled) Skip = UiTestGate.Reason;
            else if (!NativeLibrary.TryLoad("vulkan-1.dll", out _)) Skip = "No Vulkan loader (vulkan-1.dll).";
        }
    }

    /// <summary>What the loader sees when every driver is filtered out: a driver, but no GPU.</summary>
    private static readonly Dictionary<string, string?> NoUsableGpu = new() { ["VK_LOADER_VENDOR_ID_FILTER"] = "0x1234" };

    [VulkanUiFact]
    public void AVulkanWindow_PresentsWhatItDrew()
    {
        string capture = Path.Combine(Path.GetTempPath(), $"hultui-vulkan-probe-{Guid.NewGuid():N}.ppm");
        try
        {
            var run = RunProbe(new() { ["HULTUI_VULKAN_CAPTURE"] = capture });

            Assert.True(run.ExitCode == 0, run.Output);
            Assert.Contains(" vulkan device=", run.Output);
            Assert.True(File.Exists(capture), "no capture was written:\n" + run.Output);

            // The first frame to reuse a swapchain image, which is where stale state shows.
            var (width, height, pixels) = ReadPpm(capture);
            var left = Pixel(pixels, width, width / 4, height / 2);
            var right = Pixel(pixels, width, width * 3 / 4, height / 2);
            Assert.True(left is { R: > 200, G: < 60, B: < 60 }, $"left half was {left}, expected red");
            Assert.True(right is { R: < 60, G: < 60, B: > 200 }, $"right half was {right}, expected blue");
        }
        finally
        {
            File.Delete(capture);
        }
    }

    /// <summary>
    /// Two web pages side by side in one Vulkan window: two GPU sources, each rendering into a
    /// shared image of its own (seam W7). With one image per window the second was refused and
    /// its half of the window stayed empty.
    /// </summary>
    [VulkanUiFact]
    public void TwoGpuSources_InOneVulkanWindow_BothReachTheScreen()
    {
        if (!OperatingSystem.IsWindows()) return;
        string capture = Path.Combine(Path.GetTempPath(), $"hultui-vulkan-webviews-{Guid.NewGuid():N}.ppm");
        try
        {
            var run = RunProbe(new()
            {
                ["HULTUI_VULKAN_CAPTURE"] = capture,
                // Counted from the swapchain build, frames drawn while the pages load included:
                // at 60 the capture came before they had drawn and showed an empty window. They
                // are ready in well under a second, some 40 frames at most.
                ["HULTUI_VULKAN_CAPTURE_FRAME"] = "300",
                ["HULTUI_STARTUP"] = "1",
            }, "--webviews");

            Assert.True(run.ExitCode == 0, run.Output);
            Assert.Contains("probe: both pages drew", run.Output);
            Assert.True(File.Exists(capture), "no capture was written:\n" + run.Output);

            var (width, height, pixels) = ReadPpm(capture);
            var left = Pixel(pixels, width, width / 4, height / 2);
            var right = Pixel(pixels, width, width * 3 / 4, height / 2);
            Assert.True(left is { R: < 60, G: > 200, B: < 60 }, $"left page was {left}, expected green\n{run.Output}");
            Assert.True(right is { R: > 200, G: < 60, B: > 200 }, $"right page was {right}, expected magenta\n{run.Output}");
            // Both went through the shared-image path, not the pixel fallback that would pass
            // this test without the seam.
            Assert.Contains("vulkan interop: source 2 of this window", run.Output);
            // A disposed page gives its image back; the other keeps its own.
            Assert.Contains("vulkan interop: a source gave its image back (1 left)", run.Output);
            Assert.Contains("probe: done", run.Output);
        }
        finally
        {
            File.Delete(capture);
        }
    }

    /// <summary>
    /// A page and a video side by side in one Vulkan window: WebView2's capture and mpv's
    /// hidden GL context, each in a shared image of its own (seam W7). The clip is mpv's own
    /// lavfi colour source, so the capture is checked by pixel. Skipped where no libmpv is found.
    /// </summary>
    [VulkanUiFact]
    public void APageAndAVideo_InOneVulkanWindow_BothReachTheScreen()
    {
        if (!OperatingSystem.IsWindows() || !HultUI.Media.Mpv.MpvBackend.IsAvailable) return;
        string capture = Path.Combine(Path.GetTempPath(), $"hultui-vulkan-video-{Guid.NewGuid():N}.ppm");
        // Not the probe's lavfi colour source: the bundled LGPL libmpv has no lavfi ("unrecognized
        // file format", seen). Every build reads Y4M.
        string clip = Path.Combine(Path.GetTempPath(), $"hultui-vulkan-video-{Guid.NewGuid():N}.y4m");
        try
        {
            WriteMagentaClip(clip);
            var run = RunProbe(new()
            {
                ["HULTUI_VULKAN_CAPTURE"] = capture,
                ["HULTUI_VULKAN_CAPTURE_FRAME"] = "300",
                ["HULTUI_STARTUP"] = "1",
            }, $"--page-and-video \"--clip={clip}\"");

            Assert.True(run.ExitCode == 0, run.Output);
            Assert.Contains("probe: the page and the video drew", run.Output);
            Assert.True(File.Exists(capture), "no capture was written:\n" + run.Output);
            var (width, height, pixels) = ReadPpm(capture);
            var page = Pixel(pixels, width, width / 4, height / 2);
            var video = Pixel(pixels, width, width * 3 / 4, height / 2);
            Assert.True(page is { R: < 60, G: > 200, B: < 60 }, $"the page was {page}, expected green\n{run.Output}");
            Assert.True(video is { R: > 200, G: < 60, B: > 200 }, $"the video was {video}, expected magenta\n{run.Output}");
            Assert.Contains("vulkan interop: source 2 of this window", run.Output);
        }
        finally
        {
            File.Delete(capture);
            File.Delete(clip);
        }
    }

    /// <summary>
    /// Twelve seconds of solid magenta, 320x240 at two frames a second, as Y4M: longer than the
    /// probe runs, so the capture is of a playing video rather than one that has ended. The
    /// planes are magenta in limited-range BT.601, which mpv assumes for an untagged clip this
    /// small: (255, 0, 255) is Y 106, Cb 202, Cr 222.
    /// </summary>
    private static void WriteMagentaClip(string path)
    {
        const int width = 320, height = 240, frames = 24;
        using var file = File.Create(path);
        file.Write(System.Text.Encoding.ASCII.GetBytes($"YUV4MPEG2 W{width} H{height} F2:1 Ip A1:1 C420jpeg\n"));
        var frame = new byte[width * height * 3 / 2];
        Array.Fill(frame, (byte)106, 0, width * height);
        Array.Fill(frame, (byte)202, width * height, width * height / 4);
        Array.Fill(frame, (byte)222, width * height * 5 / 4, width * height / 4);
        var marker = System.Text.Encoding.ASCII.GetBytes("FRAME\n");
        for (int i = 0; i < frames; i++)
        {
            file.Write(marker);
            file.Write(frame);
        }
    }

    /// <summary>
    /// A resize on the half-float swapchain under MAILBOX, the default on an HDR display, is
    /// drawn without the 100 ms wait. Handed over as <c>oldSwapchain</c>, the old swapchain made
    /// the first present after every rebuild wait about 100 ms on NVIDIA (610.62), and a resize
    /// took a median of 112 ms to draw; destroyed first, 29. A driver that never waited passes
    /// either way.
    /// </summary>
    [VulkanUiFact]
    public void AHalfFloatMailboxResize_IsNotHeldUpByTheSwapchainItReplaces()
    {
        if (!OperatingSystem.IsWindows()) return;   // the probe resizes through SetWindowPos
        const int resizes = 20;
        var run = RunProbe(new()
        {
            ["HULTUI_VULKAN_F16"] = "1",
            ["HULTUI_VULKAN_PRESENT"] = "mailbox",
            ["HULTUI_VULKAN_FIXED_EXTENT"] = null,
            ["HULTUI_STARTUP"] = "1",
        }, $"--resizes={resizes}");

        Assert.True(run.ExitCode == 0, run.Output);
        // A half-float MAILBOX rebuild per size, or the time below says nothing.
        Assert.Contains("vulkan present mode Mailbox", run.Output);
        int builds = System.Text.RegularExpressions.Regex.Matches(run.Output,
            @"vulkan swapchain \d+x\d+ x\d+ R16G16B16A16Sfloat").Count;
        Assert.True(builds > resizes, $"{builds} half-float swapchain builds for {resizes} resizes:\n{run.Output}");

        var time = System.Text.RegularExpressions.Regex.Match(run.Output, @"resizes, median ([\d.]+) ms");
        Assert.True(time.Success, run.Output);
        double median = double.Parse(time.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
        // Halfway between the two: a destroy and a create cost about 34 ms, the wait 100 more.
        Assert.True(median < 70, $"a resize took a median of {median:0.0} ms to draw:\n{run.Output}");
    }

    /// <summary>
    /// A resize frees the half-float offscreen it replaces. Left to Skia's scratch pool, which
    /// keeps a texture for a later surface of exactly its size, every size the window passed
    /// through held its offscreen (width × height × 8 bytes) until the context's 256 MB budget
    /// pushed it out: 203 MB after a 40-step drag. The white scale is forced above 1 so the
    /// frame is drawn through the offscreen on any display that offers scRGB.
    /// </summary>
    [VulkanUiFact]
    public void AResize_FreesTheHalfFloatOffscreenItReplaces()
    {
        if (!OperatingSystem.IsWindows()) return;   // the probe resizes through SetWindowPos
        const int resizes = 10;
        var run = RunProbe(new()
        {
            ["HULTUI_VULKAN_F16"] = "1",
            ["HULTUI_VULKAN_WHITE_SCALE"] = "2",
            ["HULTUI_VULKAN_SCALED_SPACE"] = null,
            ["HULTUI_VULKAN_FIXED_EXTENT"] = null,
            ["HULTUI_STARTUP"] = "1",
        }, $"--resizes={resizes} --new-sizes");

        Assert.True(run.ExitCode == 0, run.Output);
        // Skia's cache after the first frame at each new size, the offscreen in use included.
        var purged = System.Text.RegularExpressions.Regex.Matches(run.Output,
            @"vulkan scratch purged in [\d.]+ ms with a (\d+)x(\d+) offscreen: skia cache ([\d.]+) MB");
        Assert.True(purged.Count >= resizes, $"{purged.Count} purges for {resizes} new sizes:\n{run.Output}");

        static double Mb(int width, int height) => width * (double)height * 8 / 1048576;
        // What Skia holds besides the offscreen in use.
        var besides = purged.Select(m => double.Parse(m.Groups[3].Value, System.Globalization.CultureInfo.InvariantCulture)
            - Mb(int.Parse(m.Groups[1].Value), int.Parse(m.Groups[2].Value))).ToArray();
        double smallest = purged.Min(m => Mb(int.Parse(m.Groups[1].Value), int.Parse(m.Groups[2].Value)));
        // Kept, the old offscreens add up: 5 to 7 MB more with every size here.
        Assert.True(besides.Max() - besides[0] < smallest,
            $"Skia held {besides.Max() - besides[0]:0.0} MB more besides its offscreen after {resizes} new " +
            $"sizes than after the first, against {smallest:0.0} MB for one offscreen:\n{run.Output}");
    }

    /// <summary>
    /// A driver that cannot give a device here, as in a remote session, a VM or a filtered loader.
    /// SDL creates the Vulkan window without complaint, and the renderer then fails to start.
    /// </summary>
    [VulkanUiFact]
    public void WithADriverButNoUsableGpu_AnAppThatAskedForVulkanGetsGL()
    {
        var run = RunProbe(NoUsableGpu);

        Assert.True(run.ExitCode == 0, run.Output);
        Assert.Contains("The Vulkan renderer could not start", run.Output);
        AssertOnGL(run.Output);
    }

    /// <summary>No Vulkan driver at all. SDL refuses the Vulkan window itself.</summary>
    [VulkanUiFact]
    public void WithNoVulkanDriver_AnAppThatAskedForVulkanGetsGL()
    {
        string nowhere = Path.Combine(Path.GetTempPath(), "hultui-no-such-vulkan-driver.json");
        var run = RunProbe(new() { ["VK_DRIVER_FILES"] = nowhere, ["VK_ICD_FILENAMES"] = nowhere });

        Assert.True(run.ExitCode == 0, run.Output);
        Assert.Contains("Vulkan window creation failed", run.Output);
        AssertOnGL(run.Output);
    }

    /// <summary>
    /// <c>HULTUI_RENDERER=vulkan</c> is how the design docs test the renderer, so a failure then
    /// is the answer, not a reason to hand back GL: a silent fallback would make every such run
    /// a false negative.
    /// </summary>
    [VulkanUiFact]
    public void WhenTheEnvironmentAskedForVulkan_AFailureIsNotHiddenByAFallback()
    {
        var run = RunProbe(new(NoUsableGpu) { ["HULTUI_RENDERER"] = "vulkan" });

        Assert.True(run.ExitCode != 0, "the probe exited cleanly:\n" + run.Output);
        Assert.Contains("no Vulkan physical device", run.Output);
        Assert.DoesNotContain("falling back to OpenGL", run.Output);
        Assert.DoesNotContain("probe: done", run.Output);
    }

    private static void AssertOnGL(string output)
    {
        Assert.Contains("probe: done", output);
        Assert.DoesNotContain(" vulkan device=", output);
        Assert.Contains("gfx: video=", output);
        Assert.Contains(" renderer=", output);   // the GL description names the GL renderer
    }

    /// <summary>
    /// Runs the probe with <paramref name="environment"/> on top of this process's, minus the
    /// variables that would change what is being tested. A null value removes one.
    /// </summary>
    private static (int ExitCode, string Output) RunProbe(Dictionary<string, string?> environment, string arguments = "")
    {
        var start = new ProcessStartInfo(SamplePath.VulkanProbe(), arguments)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        // An app's request may fall back only when HULTUI_RENDERER has not asked.
        foreach (var name in new[] { "HULTUI_RENDERER", "HULTUI_VULKAN_CAPTURE", "HULTUI_VULKAN_CAPTURE_FRAME", "HULTUI_STARTUP",
                     "VK_LOADER_VENDOR_ID_FILTER", "VK_DRIVER_FILES", "VK_ICD_FILENAMES" })
            start.Environment.Remove(name);
        start.Environment["SDL_WINDOW_ACTIVATE_WHEN_SHOWN"] = "0";   // do not take the foreground
        foreach (var (name, value) in environment)
        {
            if (value is null) start.Environment.Remove(name);
            else start.Environment[name] = value;
        }

        using var process = Process.Start(start) ?? throw new InvalidOperationException("the probe did not start");
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(TimeSpan.FromSeconds(60)))
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException("the probe did not exit within 60 s");
        }
        return (process.ExitCode, stdout.Result + stderr.Result);
    }

    private static (int Width, int Height, byte[] Pixels) ReadPpm(string path)
    {
        byte[] file = File.ReadAllBytes(path);
        int at = 0;
        string Token()
        {
            while (char.IsWhiteSpace((char)file[at])) at++;
            int from = at;
            while (!char.IsWhiteSpace((char)file[at])) at++;
            return System.Text.Encoding.ASCII.GetString(file, from, at - from);
        }
        Assert.Equal("P6", Token());
        int width = int.Parse(Token()), height = int.Parse(Token());
        Assert.Equal("255", Token());
        at++;   // the single whitespace byte before the pixels
        return (width, height, file[at..]);
    }

    private static (byte R, byte G, byte B) Pixel(byte[] pixels, int width, int x, int y)
    {
        int i = (y * width + x) * 3;
        return (pixels[i], pixels[i + 1], pixels[i + 2]);
    }
}
