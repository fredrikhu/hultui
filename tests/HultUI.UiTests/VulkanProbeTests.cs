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

    /// <summary>The <see cref="TheoryAttribute"/> counterpart of <see cref="VulkanUiFactAttribute"/>.</summary>
    private sealed class VulkanUiTheoryAttribute : TheoryAttribute
    {
        public VulkanUiTheoryAttribute()
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
    /// <c>HULTUI_VULKAN_FIXED_EXTENT=1</c> keeps its over-allocated swapchain through a resize that
    /// stays inside the allocation. A swapchain larger than its window is SUBOPTIMAL on every
    /// present (NVIDIA 610), and answering that with a rebuild built the swapchain again after
    /// every frame: 57 ms a step of a drag, slower than no fixed extent at all. Here the window
    /// grows by 24×16 px a step through about two 256-px buckets in each direction. A surface
    /// without ONE_TO_ONE rebuilds for every size, as it should, and is not what this checks.
    /// </summary>
    [VulkanUiFact]
    public void AFixedExtentSwapchain_IsNotRebuiltAfterEveryFrame()
    {
        if (!OperatingSystem.IsWindows()) return;   // the probe resizes through SetWindowPos
        const int resizes = 16;
        var run = RunProbe(new()
        {
            ["HULTUI_VULKAN_FIXED_EXTENT"] = "1",
            ["HULTUI_VULKAN_F16"] = null,
            ["HULTUI_VULKAN_PRESENT"] = null,
            ["HULTUI_STARTUP"] = "1",
        }, $"--resizes={resizes} --new-sizes");

        Assert.True(run.ExitCode == 0, run.Output);
        Assert.Contains("probe: done", run.Output);
        if (!run.Output.Contains("fixed extent available")) return;
        int builds = System.Text.RegularExpressions.Regex.Matches(run.Output, @"vulkan swapchain \d+x\d+ x\d+").Count;
        int oneToOne = System.Text.RegularExpressions.Regex.Matches(run.Output,
            @"vulkan swapchain \d+x\d+ x\d+ \S+ \(allocated \d+x\d+, one-to-one\)").Count;
        Assert.True(oneToOne == builds, $"{builds - oneToOne} of {builds} swapchains were not one-to-one:\n{run.Output}");
        // The start-up swapchain and one for each bucket crossed: four (2026-10-11). Rebuilt for
        // every SUBOPTIMAL, eighteen.
        Assert.True(builds < resizes / 2, $"{builds} swapchain builds for {resizes} resizes:\n{run.Output}");
    }

    /// <summary>
    /// A drag keeps an over-allocated swapchain, and its end rebuilds it at the window's size; a
    /// title-bar move builds nothing. Rebuilt for every size, the half-float swapchain was 32 ms of
    /// a 37 ms step in Aperture and Reel; kept, a drag rebuilds only when it crosses a 256-px
    /// bucket, and the end of the loop goes back to an exact swapchain, which keeps the
    /// compositor's direct path that video needs. A move runs the same loop and must not pay a
    /// rebuild at either end. Where the surface offers no ONE_TO_ONE, and in any present mode but
    /// MAILBOX, every size rebuilds and the end of the loop does nothing.
    /// </summary>
    [VulkanUiTheory]
    [InlineData("f16-mailbox")]
    [InlineData("8-bit")]
    [InlineData("fifo")]
    [InlineData("immediate")]
    public void ADrag_OverAllocatesTheSwapchain_AndItsEndRebuildsItExact(string variant)
    {
        if (!OperatingSystem.IsWindows()) return;   // Win32's size/move loop
        var run = RunProbe(new()
        {
            ["HULTUI_VULKAN_F16"] = variant == "8-bit" ? "0" : "1",
            // Only MAILBOX shows a swapchain one-to-one: under FIFO it stalled for good, under
            // IMMEDIATE a drag's first one cost 172-196 ms (NVIDIA 610.62).
            ["HULTUI_VULKAN_PRESENT"] = variant switch { "8-bit" => null, "fifo" => "fifo", "immediate" => "immediate", _ => "mailbox" },
            ["HULTUI_VULKAN_FIXED_EXTENT"] = null,
            ["HULTUI_STARTUP"] = "1",
        }, "--drag");

        Assert.True(run.ExitCode == 0, run.Output);
        Assert.Contains("probe: done", run.Output);
        var drag = System.Text.RegularExpressions.Regex.Match(run.Output,
            @"probe: drag (\d+) steps, (\d+) builds \((\d+) one-to-one\), (\d+) frames");
        var ended = System.Text.RegularExpressions.Regex.Match(run.Output,
            @"probe: drag ended, (\d+) builds \((\d+) one-to-one, last (\d+x\d+) (\w+)\), (\d+) frames, client (\d+x\d+)");
        var move = System.Text.RegularExpressions.Regex.Match(run.Output, @"probe: move (\d+) steps, (\d+) builds, (\d+) frames");
        Assert.True(drag.Success && ended.Success && move.Success, run.Output);
        int N(System.Text.RegularExpressions.Match m, int group) => int.Parse(m.Groups[group].Value);
        int steps = N(drag, 1), builds = N(drag, 2), oneToOne = N(drag, 3), frames = N(drag, 4);

        // Every step is drawn inside the loop, as ever: nothing else comes round there.
        Assert.True(frames >= steps, $"{frames} frames for {steps} steps of the drag:\n{run.Output}");
        // A move changes no size: no swapchain at either end of it.
        Assert.True(N(move, 2) == 0, $"a move built {N(move, 2)} swapchains:\n{run.Output}");

        if (variant == "fifo") Assert.Contains("vulkan present scaling: not under Fifo", run.Output);
        if (variant == "immediate") Assert.Contains("vulkan present scaling: not under Immediate", run.Output);
        if (!run.Output.Contains("fixed extent available"))
        {
            Assert.True(builds == steps && oneToOne == 0, $"{builds} builds ({oneToOne} one-to-one) for {steps} steps:\n{run.Output}");
            Assert.True(N(ended, 1) == 0, $"the end of a drag built {N(ended, 1)} swapchains:\n{run.Output}");
            return;
        }
        // The first step and each bucket crossed after it: three here, all one-to-one. Rebuilt for
        // every size, sixteen.
        Assert.True(builds >= 1 && builds == oneToOne, $"{builds} builds ({oneToOne} one-to-one) during the drag:\n{run.Output}");
        Assert.True(builds <= 4, $"{builds} swapchain builds for {steps} steps of a drag:\n{run.Output}");
        // One exact swapchain at the end, at the window's size, and its frame drawn at once.
        Assert.True(N(ended, 1) == 1 && N(ended, 2) == 0 && ended.Groups[4].Value == "exact",
            $"the end of the drag: {ended.Value}\n{run.Output}");
        Assert.Equal(ended.Groups[6].Value, ended.Groups[3].Value);
        Assert.True(N(ended, 5) >= 1, $"no frame was drawn when the drag ended:\n{run.Output}");
    }

    /// <summary>
    /// While a drag keeps its over-allocated swapchain, the half-float offscreen the frame is drawn
    /// through is kept too, made at the swapchain's size. Remade at every size, each kept step paid
    /// a new F16 surface, a synchronous submit to free the old one and a scratch purge: an 80-step
    /// drag made 82 offscreens for 5 swapchains (plan 17 round 4, vk-drag-kept-step-remakes). Each
    /// freed offscreen is followed by one scratch purge, which HULTUI_STARTUP=1 reports; the white
    /// scale is forced above 1 so the frame goes through the offscreen on any display. Only where
    /// the surface shows a swapchain one-to-one (MAILBOX): elsewhere every size rebuilds anyway.
    /// </summary>
    [VulkanUiFact]
    public void ADrag_KeepsTheHalfFloatOffscreen_WithItsSwapchain()
    {
        if (!OperatingSystem.IsWindows()) return;   // Win32's size/move loop
        var run = RunProbe(new()
        {
            ["HULTUI_VULKAN_F16"] = "1",
            ["HULTUI_VULKAN_PRESENT"] = "mailbox",
            ["HULTUI_VULKAN_WHITE_SCALE"] = "2",
            ["HULTUI_VULKAN_SCALED_SPACE"] = null,
            ["HULTUI_VULKAN_FIXED_EXTENT"] = null,
            ["HULTUI_STARTUP"] = "1",
        }, "--drag");

        Assert.True(run.ExitCode == 0, run.Output);
        Assert.Contains("probe: done", run.Output);
        if (!run.Output.Contains("fixed extent available")) return;
        var drag = System.Text.RegularExpressions.Regex.Match(run.Output,
            @"probe: drag (\d+) steps, (\d+) builds \((\d+) one-to-one\), (\d+) frames, (\d+) offscreens freed");
        Assert.True(drag.Success, run.Output);
        int steps = int.Parse(drag.Groups[1].Value), builds = int.Parse(drag.Groups[2].Value);
        int freed = int.Parse(drag.Groups[5].Value);
        // One a swapchain at most: kept for the steps the swapchain is kept. Remade per size, 16.
        Assert.True(freed <= builds, $"{freed} offscreens freed in {steps} steps with {builds} swapchain builds:\n{run.Output}");
    }

    /// <summary>
    /// Each step of a resize draws one frame, at its new size, before SetWindowPos returns. The
    /// event watch drew a whole frame for RESIZED and again for PIXEL_SIZE_CHANGED of the same
    /// size, and a growing step a third for the WM_PAINT of the area it exposed: 514 Skia and 217
    /// host submits in an 80-step drag on the half-float swapchain. Never none: nothing comes
    /// round again inside Win32's modal size loop, and a frame skipped there was the stretch.
    /// </summary>
    [VulkanUiTheory]
    [InlineData("f16-mailbox")]
    [InlineData("8-bit")]
    public void EachStepOfAResize_DrawsOneFrame(string variant)
    {
        if (!OperatingSystem.IsWindows()) return;   // the probe resizes through SetWindowPos
        const int resizes = 16;
        var run = RunProbe(new()
        {
            ["HULTUI_VULKAN_F16"] = variant == "f16-mailbox" ? "1" : "0",
            ["HULTUI_VULKAN_PRESENT"] = variant == "f16-mailbox" ? "mailbox" : null,
            ["HULTUI_VULKAN_FIXED_EXTENT"] = null,
        }, $"--resizes={resizes} --new-sizes");

        Assert.True(run.ExitCode == 0, run.Output);
        var frames = System.Text.RegularExpressions.Regex.Match(run.Output, @"probe: (\d+) frames inside (\d+) resizes, (\d+) without one");
        Assert.True(frames.Success, run.Output);
        int inside = int.Parse(frames.Groups[1].Value), without = int.Parse(frames.Groups[3].Value);
        Assert.True(without == 0, $"{without} of {resizes} resizes drew no frame inside SetWindowPos:\n{run.Output}");
        Assert.True(inside == resizes, $"{inside} frames inside {resizes} resizes:\n{run.Output}");
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
    /// Every swapchain image is reused many times, and each capture shows the frame just drawn.
    /// The renderer keeps one render target per swapchain image for the swapchain's life, which
    /// is right only while the host hands every acquired image back in the layout Skia left it
    /// in: kept without that, a three-image swapchain drew three frames and then black for ever.
    /// The fortieth frame of each swapchain is captured, after resizes and a minimize, so every
    /// image has been drawn into about a dozen times; a stale image would show an older number,
    /// a target over the wrong image another frame's.
    ///
    /// <para><b>A wrong layout does not show here.</b> With the host's acquire barrier left out,
    /// NVIDIA (610) drew every captured frame right, on both formats, while the validation layer
    /// reported <c>VUID-vkCmdDraw-None-09600</c> (the image in UNDEFINED or PRESENT_SRC_KHR where
    /// the render target expects COLOR_ATTACHMENT_OPTIMAL). So where the Khronos validation layer
    /// is installed, the run loads it (core and synchronisation) and must leave its log empty;
    /// the loader's line saying it forced the layer on is the proof it ran.</para>
    /// </summary>
    [VulkanUiTheory]
    [InlineData("f16-offscreen")]
    [InlineData("f16-scaled-space")]
    [InlineData("8-bit")]
    [InlineData("fifo")]
    [InlineData("immediate")]
    public void EveryCapture_OfAReusedImage_ShowsTheFrameJustDrawn(string variant)
    {
        string capture = Path.Combine(Path.GetTempPath(), $"hultui-vulkan-counter-{Guid.NewGuid():N}.ppm");
        string validation = Path.Combine(Path.GetTempPath(), $"hultui-vulkan-counter-{Guid.NewGuid():N}.log");
        try
        {
            var environment = new Dictionary<string, string?>
            {
                ["HULTUI_VULKAN_CAPTURE"] = capture,
                ["HULTUI_VULKAN_CAPTURE_FRAME"] = "40",
                // Says whether the surface offers ONE_TO_ONE ("fixed extent available").
                ["HULTUI_STARTUP"] = "1",
                // Nothing happens here without the layer installed (the Vulkan SDK).
                ["VK_LOADER_LAYERS_ENABLE"] = "*validation*",
                ["VK_LAYER_VALIDATE_SYNC"] = "1",
                ["VK_LAYER_LOG_FILENAME"] = validation,
                ["VK_LOADER_DEBUG"] = "warn",
                ["HULTUI_VULKAN_FIXED_EXTENT"] = null,
                ["HULTUI_VULKAN_SCALED_SPACE"] = null,
                ["HULTUI_VULKAN_PRESENT"] = null,
                ["HULTUI_VULKAN_F16"] = null,
                ["HULTUI_VULKAN_WHITE_SCALE"] = null,
            };
            // A white scale above 1 sends the half-float frame through the offscreen (or the
            // scaled colour space) on any display.
            switch (variant)
            {
                case "f16-offscreen": environment["HULTUI_VULKAN_F16"] = "1"; environment["HULTUI_VULKAN_WHITE_SCALE"] = "2"; break;
                case "f16-scaled-space":
                    environment["HULTUI_VULKAN_F16"] = "1"; environment["HULTUI_VULKAN_WHITE_SCALE"] = "2";
                    environment["HULTUI_VULKAN_SCALED_SPACE"] = "1";
                    break;
                case "8-bit": environment["HULTUI_VULKAN_F16"] = "0"; break;
                case "fifo": environment["HULTUI_VULKAN_PRESENT"] = "fifo"; break;
                case "immediate": environment["HULTUI_VULKAN_PRESENT"] = "immediate"; break;
            }

            var run = RunProbe(environment, "--frame-counter");

            Assert.True(run.ExitCode == 0, run.Output);
            Assert.Contains("probe: done", run.Output);
            Assert.Contains(" vulkan device=", run.Output);
            var captures = System.Text.RegularExpressions.Regex.Matches(run.Output, @"probe: capture (right|WRONG)");
            // One a swapchain: the first, two resizes, the restore after a minimize, and where the
            // surface shows a swapchain one-to-one, a drag's (drawn into at four sizes) and the
            // exact one its end builds.
            int expected = !OperatingSystem.IsWindows() ? 1 : run.Output.Contains("fixed extent available") ? 6 : 4;
            Assert.True(captures.Count >= expected, $"{captures.Count} captures, expected {expected}:\n{run.Output}");
            Assert.DoesNotContain("probe: capture WRONG", run.Output);

            if (run.Output.Contains("\"VK_LAYER_KHRONOS_validation\" forced enabled"))
            {
                string reported = File.Exists(validation) ? File.ReadAllText(validation) : "";
                Assert.True(reported.Length == 0, "the validation layer reported:\n" +
                    (reported.Length > 4000 ? reported[..4000] : reported));
            }
        }
        finally
        {
            File.Delete(capture);
            File.Delete(validation);
        }
    }

    /// <summary>
    /// A steady Vulkan frame allocates nothing, as a GL one does. Wrapping the swapchain image in
    /// a new render target and surface every frame cost 2,048 bytes a frame (1,819 on an 8-bit
    /// swapchain): an SKSurface, its SKCanvas and their handle-table entries, and on the
    /// half-float swapchain a snapshot SKImage of the offscreen.
    /// </summary>
    [VulkanUiTheory]
    [InlineData("f16-offscreen")]
    [InlineData("8-bit")]
    public void ASteadyFrame_AllocatesNothing(string variant)
    {
        var run = RunProbe(new()
        {
            ["HULTUI_VULKAN_F16"] = variant == "8-bit" ? "0" : "1",
            ["HULTUI_VULKAN_WHITE_SCALE"] = variant == "8-bit" ? null : "2",
            ["HULTUI_VULKAN_SCALED_SPACE"] = null,
            ["HULTUI_A11Y"] = "0",
        }, "--frame-bytes");

        Assert.True(run.ExitCode == 0, run.Output);
        Assert.Contains(" vulkan device=", run.Output);
        var bytes = System.Text.RegularExpressions.Regex.Match(run.Output, @"probe: ([\d.]+) bytes a frame");
        Assert.True(bytes.Success, run.Output);
        double perFrame = double.Parse(bytes.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
        Assert.True(perFrame < 100, $"a steady frame allocated {perFrame:0.0} bytes:\n{run.Output}");
    }

    /// <summary>
    /// A steady video frame allocates next to nothing on the UI thread, and the picture keeps
    /// changing. The headless perf suite draws no GPU source, so this is that row on a real window:
    /// a 160×90 clip at 60 fps through mpv, nothing else on screen, the least of five rounds of 60
    /// frames. On the half-float swapchain a frame cost 395 bytes, on GL 157 (plan 17 round 4): a
    /// closure and its delegate in <c>GpuFrameSink.TryRenderFrame</c> (144 B, on both), and on
    /// Vulkan a new <c>GRBackendTexture</c> (finalizable, never disposed) and <c>SKImage</c> over the
    /// shared image every frame. The surface keeps that image now, so a wrapper gone stale would
    /// show one picture for ever: the clip gets brighter frame by frame, and the capture of the
    /// swapchain a resize builds after the rounds must be brighter than the one before them. An
    /// 8-bit swapchain also wraps its image for the frame after one that drew a video (by design:
    /// an earlier barrier slowed mpv's draw), about 2.3 KB; it was 2,820 bytes in all.
    /// </summary>
    [VulkanUiTheory]
    [InlineData("f16-mailbox")]
    [InlineData("8-bit")]
    [InlineData("gl")]
    public void ASteadyVideoFrame_AllocatesAlmostNothing_AndThePictureKeepsChanging(string variant)
    {
        if (!OperatingSystem.IsWindows() || !HultUI.Media.Mpv.MpvBackend.IsAvailable) return;
        string clip = Path.Combine(Path.GetTempPath(), $"hultui-vulkan-ramp-{Guid.NewGuid():N}.y4m");
        string capture = Path.Combine(Path.GetTempPath(), $"hultui-vulkan-ramp-{Guid.NewGuid():N}.ppm");
        try
        {
            WriteRampClip(clip);
            var environment = new Dictionary<string, string?>
            {
                ["HULTUI_VULKAN_CAPTURE"] = capture,
                // Past the frames drawn while the clip loads: the first capture shows the video.
                ["HULTUI_VULKAN_CAPTURE_FRAME"] = "120",
                ["HULTUI_A11Y"] = "0",
                ["HULTUI_VULKAN_F16"] = null,
                ["HULTUI_VULKAN_PRESENT"] = null,
                ["HULTUI_VULKAN_WHITE_SCALE"] = null,
            };
            string arguments = $"--video-frame-bytes \"--clip={clip}\"";
            switch (variant)
            {
                case "f16-mailbox": environment["HULTUI_VULKAN_F16"] = "1"; environment["HULTUI_VULKAN_PRESENT"] = "mailbox"; break;
                case "8-bit": environment["HULTUI_VULKAN_F16"] = "0"; break;
                case "gl": arguments += " --renderer=default"; break;
            }

            var run = RunProbe(environment, arguments);

            Assert.True(run.ExitCode == 0, run.Output);
            Assert.Contains("probe: done", run.Output);
            Assert.Contains("Gpu mode", run.Output);
            var bytes = System.Text.RegularExpressions.Regex.Match(run.Output, @"probe: ([\d.]+) bytes a video frame");
            Assert.True(bytes.Success, run.Output);
            double perFrame = double.Parse(bytes.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
            // 35 on the half-float swapchain, 13 on GL, 2,331 on 8-bit (2026-10-24).
            double most = variant == "8-bit" ? 2500 : 100;
            Assert.True(perFrame < most, $"a steady video frame allocated {perFrame:0.0} bytes:\n{run.Output}");

            if (variant == "gl") return;   // GL has no swapchain capture
            Assert.Contains(" vulkan device=", run.Output);
            var shown = System.Text.RegularExpressions.Regex.Match(run.Output, @"probe: video brightness (\d+) then (\d+)");
            Assert.True(shown.Success, run.Output);
            int before = int.Parse(shown.Groups[1].Value), after = int.Parse(shown.Groups[2].Value);
            // Two seconds or more of a ramp that climbs 219 levels in 15 s: 18 then 142 here.
            Assert.True(after > before + 20, $"the video's brightness went from {before} to {after}:\n{run.Output}");
        }
        finally
        {
            File.Delete(clip);
            File.Delete(capture);
        }
    }

    /// <summary>
    /// Fifteen seconds of grey, 160x90 at 60 frames a second, as Y4M, from black (luma 16) to white
    /// (235) in equal steps: longer than the probe runs, and every frame brighter than the last.
    /// </summary>
    private static void WriteRampClip(string path)
    {
        const int width = 160, height = 90, frames = 900;
        using var file = File.Create(path);
        file.Write(System.Text.Encoding.ASCII.GetBytes($"YUV4MPEG2 W{width} H{height} F60:1 Ip A1:1 C420jpeg\n"));
        var luma = new byte[width * height];
        var chroma = new byte[width * height / 4];
        Array.Fill(chroma, (byte)128);
        var marker = System.Text.Encoding.ASCII.GetBytes("FRAME\n");
        for (int i = 0; i < frames; i++)
        {
            Array.Fill(luma, (byte)(16 + 219 * i / (frames - 1)));
            file.Write(marker);
            file.Write(luma);
            file.Write(chroma);
            file.Write(chroma);
        }
    }

    /// <summary>
    /// Disposed images leave no textures behind. A GPU context keeps the texture it uploaded for
    /// an image after the image is gone, as a spare for a later texture of exactly that size,
    /// until its 256 MB budget pushes it out: eight 3840×2400 photos drawn and disposed held
    /// 176 MB that way. The surface purges the spares after the next frame. Four 2048×2048
    /// images are 64 MB of textures here; the purge must leave Skia less than one of them.
    /// </summary>
    [VulkanUiFact]
    public void DisposedImages_LeaveNoTexturesBehind()
    {
        var run = RunProbe(new() { ["HULTUI_STARTUP"] = "1" }, "--disposed-images");

        Assert.True(run.ExitCode == 0, run.Output);
        Assert.Contains("probe: done", run.Output);
        var purge = System.Text.RegularExpressions.Regex.Match(run.Output,
            @"vulkan scratch purged in [\d.]+ ms with a \d+x\d+ offscreen: skia cache ([\d.]+) MB in \d+ resources \(images were disposed\)");
        Assert.True(purge.Success, "no purge after the images were disposed:\n" + run.Output);
        double held = double.Parse(purge.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
        Assert.True(held < 16, $"Skia held {held:0.0} MB after the purge; one image is 16 MB:\n{run.Output}");
        // Once: the frames after it disposed nothing more.
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(run.Output, @"\(images were disposed\)"));
    }

    /// <summary>
    /// A window that draws while minimized, then is restored and resized. On Windows a minimized
    /// Vulkan surface has no area, so the frame's swapchain went out of date and none could be
    /// built; the old one was dropped without being destroyed. Restored at the same size, the
    /// window drew 0 frames of 20 for ever, a resize failed with ErrorNativeWindowInUseKHR, and
    /// the loop retried a frame every display interval meanwhile (400 passes a second at 200 Hz).
    /// GL drew while minimized instead, every frame of an animation nobody could see. Now neither
    /// draws a minimized window, and the loop waits as if idle until the restore.
    /// </summary>
    [VulkanUiTheory]
    [InlineData("gl")]
    [InlineData("f16-mailbox")]
    [InlineData("8-bit")]
    [InlineData("fifo")]
    public void AWindowThatDrewWhileMinimized_WaitsQuietly_AndDrawsOnceRestored(string variant)
    {
        if (!OperatingSystem.IsWindows()) return;   // the probe minimizes and resizes through user32
        var environment = new Dictionary<string, string?>
        {
            ["HULTUI_VULKAN_FIXED_EXTENT"] = null,
            ["HULTUI_VULKAN_F16"] = null,
            ["HULTUI_VULKAN_PRESENT"] = null,
            ["HULTUI_STARTUP"] = "1",
        };
        string arguments = "--minimize";
        switch (variant)
        {
            case "gl": arguments += " --renderer=default"; break;
            case "f16-mailbox": environment["HULTUI_VULKAN_F16"] = "1"; environment["HULTUI_VULKAN_PRESENT"] = "mailbox"; break;
            case "8-bit": environment["HULTUI_VULKAN_F16"] = "0"; break;
            case "fifo": environment["HULTUI_VULKAN_PRESENT"] = "fifo"; break;
        }

        var run = RunProbe(environment, arguments);

        Assert.True(run.ExitCode == 0, run.Output);
        Assert.Contains("probe: done", run.Output);
        Assert.DoesNotContain("Exception", run.Output);
        if (variant == "gl") Assert.DoesNotContain(" vulkan device=", run.Output);
        else
        {
            Assert.Contains(" vulkan device=", run.Output);
            // The frames forced while minimized took the swapchain the window had.
            Assert.Contains("vulkan swapchain SKIPPED", run.Output);
        }
        const string halfFloat = @"vulkan swapchain \d+x\d+ x\d+ R16G16B16A16Sfloat";
        if (variant == "f16-mailbox") Assert.Matches(halfFloat, run.Output);
        if (variant == "8-bit") Assert.DoesNotMatch(halfFloat, run.Output);
        if (variant == "fifo") Assert.Contains("vulkan present mode Fifo", run.Output);

        var (minimizedPasses, minimizedFrames) = Stage(run.Output, "minimized");
        var (_, restoredFrames) = Stage(run.Output, "restored");
        var (_, resizedFrames) = Stage(run.Output, "resized");
        // The change made while minimized waits for the restore.
        Assert.True(minimizedFrames == 0, $"{minimizedFrames} frames drawn while minimized:\n{run.Output}");
        // An idle window's loop passes about 60 times a second. Retrying a frame every display
        // interval is two passes an interval: 400 at 200 Hz, 120 at 60.
        Assert.True(minimizedPasses < 100, $"{minimizedPasses} loop passes a second while minimized:\n{run.Output}");
        // A change every 33 ms: about 30 frames in the second after the restore, 15 after the resize.
        Assert.True(restoredFrames >= 10, $"{restoredFrames} frames in the second after the restore:\n{run.Output}");
        Assert.True(resizedFrames >= 5, $"{resizedFrames} frames in the half second after a resize:\n{run.Output}");
    }

    /// <summary>
    /// A video keeps playing in a minimized window, which is not drawn. The UI thread is mpv's
    /// render thread, so mpv waited 200 ms for each frame to be taken and then dropped it, and a
    /// player opened while minimized never loaded, its render context being made by a draw: in
    /// Reel the next file in the folder stayed silent until the restore. The window now takes
    /// the frames without drawing them, and lets a new player set up as a draw would.
    /// </summary>
    [VulkanUiTheory]
    [InlineData("gl")]
    [InlineData("f16-mailbox")]
    [InlineData("8-bit")]
    public void AVideoInAMinimizedWindow_KeepsPlaying_AndANewOneLoads(string variant)
    {
        if (!OperatingSystem.IsWindows() || !HultUI.Media.Mpv.MpvBackend.IsAvailable) return;
        // Ten seconds at 30 fps: longer than the probe runs.
        string clip = HultUI.Media.Testing.TestClip.WriteTempY4m(160, 90, fps: 30, frames: 300);
        try
        {
            var environment = new Dictionary<string, string?>
            {
                ["HULTUI_VULKAN_FIXED_EXTENT"] = null,
                ["HULTUI_VULKAN_F16"] = null,
                ["HULTUI_VULKAN_PRESENT"] = null,
            };
            string arguments = $"--minimized-video \"--clip={clip}\"";
            switch (variant)
            {
                case "gl": arguments += " --renderer=default"; break;
                case "f16-mailbox": environment["HULTUI_VULKAN_F16"] = "1"; environment["HULTUI_VULKAN_PRESENT"] = "mailbox"; break;
                case "8-bit": environment["HULTUI_VULKAN_F16"] = "0"; break;
            }

            var run = RunProbe(environment, arguments);

            Assert.True(run.ExitCode == 0, run.Output);
            Assert.Contains("probe: done", run.Output);
            Assert.DoesNotContain("Exception", run.Output);
            if (variant == "gl") Assert.DoesNotContain(" vulkan device=", run.Output);

            var minimized = System.Text.RegularExpressions.Regex.Match(run.Output,
                @"probe: minimized 1\.5 s, (\d+) frames dropped, played ([\d.]+) s");
            Assert.True(minimized.Success, run.Output);
            int dropped = int.Parse(minimized.Groups[1].Value);
            double played = double.Parse(minimized.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture);
            // Unserved, mpv dropped 42 of the 45 frames here; served, none.
            Assert.True(dropped <= 1, $"mpv dropped {dropped} frames while minimized:\n{run.Output}");
            Assert.True(played >= 1.2, $"the video played {played:0.00} s of 1.5 while minimized:\n{run.Output}");

            var opened = System.Text.RegularExpressions.Regex.Match(run.Output,
                @"probe: opened while minimized, at (-?[\d.]+) s, still minimized (\w+)");
            Assert.True(opened.Success, run.Output);
            Assert.Equal("True", opened.Groups[2].Value);
            double reached = double.Parse(opened.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
            // Unserved, it never loaded (-1); served, 1.33 s.
            Assert.True(reached >= 0.5, $"a player opened while minimized reached {reached:0.00} s in 1.5:\n{run.Output}");

            var restored = System.Text.RegularExpressions.Regex.Match(run.Output, @"probe: restored, (\d+) frames in 1 s");
            Assert.True(restored.Success, run.Output);
            Assert.True(int.Parse(restored.Groups[1].Value) >= 10, $"few frames after the restore:\n{run.Output}");
        }
        finally
        {
            File.Delete(clip);
        }
    }

    private static (int PassesPerSecond, int Frames) Stage(string output, string stage)
    {
        var match = System.Text.RegularExpressions.Regex.Match(output, $@"probe: {stage} (\d+) passes/s, (\d+) frames");
        Assert.True(match.Success, $"no '{stage}' line:\n{output}");
        return (int.Parse(match.Groups[1].Value), int.Parse(match.Groups[2].Value));
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
    /// <summary>
    /// <c>Window.SaveFrame</c> on each path a frame takes to the screen: GL's back buffer, an 8-bit
    /// swapchain and an scRGB one read back by the host, and the scRGB offscreen read by Skia.
    /// Every one has to come out as the frame looks, and the right way up: red top left, green
    /// bottom left, blue on the right.
    /// </summary>
    [VulkanUiTheory]
    [InlineData("gl")]
    [InlineData("vulkan-8bit")]
    [InlineData("vulkan-scrgb-offscreen")]
    [InlineData("vulkan-scrgb-scaled-space")]
    public void SaveFrame_WritesTheFrameAsItLooks_OnEveryPath(string path)
    {
        Dictionary<string, string?> environment = path switch
        {
            "gl" => new() { ["HULTUI_RENDERER"] = "gl" },
            "vulkan-8bit" => new() { ["HULTUI_VULKAN_F16"] = "0" },
            "vulkan-scrgb-offscreen" => new() { ["HULTUI_VULKAN_F16"] = "1", ["HULTUI_VULKAN_WHITE_SCALE"] = "3" },
            _ => new()
            {
                ["HULTUI_VULKAN_F16"] = "1", ["HULTUI_VULKAN_WHITE_SCALE"] = "3", ["HULTUI_VULKAN_SCALED_SPACE"] = "1",
            },
        };
        string png = Path.Combine(Path.GetTempPath(), $"hultui-save-frame-{Guid.NewGuid():N}.png");
        try
        {
            var run = RunProbe(environment, $"--save-frame=\"{png}\"");
            Assert.True(run.ExitCode == 0, run.Output);
            Assert.Contains("probe: frame saved", run.Output);
            if (path != "gl") Assert.Contains(" vulkan device=", run.Output);

            using var frame = SkiaSharp.SKBitmap.Decode(png);
            Assert.NotNull(frame);
            var topLeft = frame.GetPixel(frame.Width / 4, frame.Height / 4);
            var right = frame.GetPixel(frame.Width * 3 / 4, frame.Height / 2);
            var bottomLeft = frame.GetPixel(frame.Width / 4, frame.Height * 3 / 4);
            Assert.True(topLeft is { Red: > 200, Green: < 60, Blue: < 60 }, $"top left was {topLeft}, expected red\n{run.Output}");
            Assert.True(right is { Red: < 60, Green: < 60, Blue: > 200 }, $"right half was {right}, expected blue\n{run.Output}");
            Assert.True(bottomLeft is { Red: < 60, Green: > 200, Blue: < 60 }, $"bottom left was {bottomLeft}, expected green\n{run.Output}");
        }
        finally
        {
            File.Delete(png);
        }
    }

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
