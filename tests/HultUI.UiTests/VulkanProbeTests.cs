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
    private static (int ExitCode, string Output) RunProbe(Dictionary<string, string?> environment)
    {
        var start = new ProcessStartInfo(SamplePath.VulkanProbe())
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        // An app's request may fall back only when HULTUI_RENDERER has not asked.
        foreach (var name in new[] { "HULTUI_RENDERER", "HULTUI_VULKAN_CAPTURE", "HULTUI_VULKAN_CAPTURE_FRAME",
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
