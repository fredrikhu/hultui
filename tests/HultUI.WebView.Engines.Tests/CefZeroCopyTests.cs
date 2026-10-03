using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using HultUI.Core.Rendering;
using HultUI.WebView.Cef;
using Xunit;

namespace HultUI.WebView.Engines.Tests;

/// <summary>Skips unless CEF is here and a Wayland compositor is reachable: zero-copy is
/// Wayland's only (step 5.5), and CEF draws on the GPU only on a window system's platform.</summary>
public sealed class WaylandCefFactAttribute : FactAttribute
{
    public WaylandCefFactAttribute()
    {
        if (new CefFactAttribute().Skip is { } skip) Skip = skip;
        else if (Environment.GetEnvironmentVariable("WAYLAND_DISPLAY") is not { Length: > 0 }) Skip = "No Wayland compositor (WAYLAND_DISPLAY).";
    }
}

/// <summary>
/// Step 5.5: a page's frames on the GPU on Wayland. End to end in processes of their own (CEF
/// takes its window system once per process, and this one runs headless): the engine's copy
/// into its ring and a GL context's import of it; and seam W7, two pages in one Vulkan window,
/// checked in a capture of what it presented. In this process, which path a placement picks.
/// </summary>
public sealed class CefZeroCopyTests
{
    private sealed class TestHost : IWebViewHost
    {
        public PixelFrameSink Pixels { get; } = new();
        public IGpuFrameRenderer? Renderer;
        public void UseGpuRenderer(IGpuFrameRenderer renderer) => Renderer = renderer;
        public void RequestRepaint() { }
        public void Post(Action action) => ThreadPool.QueueUserWorkItem(_ => action());
    }

    [WaylandCefFact]
    public void OnWayland_FramesTravelOnTheGpu_TheSelectsListToo()
    {
        var run = CefEngineTests.RunCefProcess(Guid.NewGuid().ToString("N"), "--zero-copy", "640", "400", "2");
        Assert.True(run.ExitCode == 0, run.Output);
        Assert.Contains("renderer registered: True", run.Output);
        Assert.Contains("page 0: shows its colour, zero-copy", run.Output);
        Assert.Contains("page 1: shows its colour, zero-copy", run.Output);
        // A still page changed by script sends its next frame as well.
        Assert.Contains("after a change by script: shows it", run.Output);
        // CEF's popup arrives as a shared texture of its own, drawn over the page in one frame.
        Assert.Matches(@"the select's list: drawn over the page \(.*\), popups [1-9]", run.Output);
        Assert.Contains("done", run.Output);
    }

    [WaylandCefFact]
    public void TwoPages_InOneVulkanWindow_BothReachTheScreen()
    {
        if (!NativeLibrary.TryLoad("libvulkan.so.1", out var vulkan)) return;   // no loader: nothing to check
        NativeLibrary.Free(vulkan);
        var capture = Path.Combine(Path.GetTempPath(), $"hultui-cef-webviews-{Guid.NewGuid():N}.ppm");
        try
        {
            var run = RunProbe(capture);
            Assert.True(run.ExitCode == 0, run.Output);
            Assert.True(File.Exists(capture), "no capture was written:\n" + run.Output);
            var (width, _, pixels) = ReadPpm(capture);
            // The layout is 480×240 at the window's top left, whatever size the compositor gave it.
            var left = Pixel(pixels, width, 120, 120);
            var right = Pixel(pixels, width, 360, 120);
            Assert.True(left is { R: < 60, G: > 200, B: < 60 }, $"left page was {left}, expected green\n{run.Output}");
            Assert.True(right is { R: > 200, G: < 60, B: > 200 }, $"right page was {right}, expected magenta\n{run.Output}");
            // Each page its own shared image, through the engine's zero-copy path, not pixels.
            Assert.Contains("vulkan interop: source 2 of this window", run.Output);
            Assert.Equal(2, run.Output.Split('\n').Count(l => l.Contains("page frames: zero-copy")));
            Assert.Contains("vulkan interop: a source gave its image back (1 left)", run.Output);
            Assert.Contains("probe: done", run.Output);
        }
        finally { File.Delete(capture); }
    }

    /// <summary>This process's CEF runs headless, whichever test starts it: a Wayland
    /// placement here must not be the first.</summary>
    private static void HeadlessFirst() => Assert.True(CefRuntime.Start(windowSystem: null).Wait(TimeSpan.FromSeconds(30)));

    [CefFact]
    public void AWaylandPlacement_RegistersTheGpuRenderer_AndX11KeepsPixels()
    {
        HeadlessFirst();
        using var engine = new CefEngine();
        var wayland = new TestHost();
        var x11 = new TestHost();
        using var onWayland = engine.CreateView(new WebViewOptions());
        using var onX11 = engine.CreateView(new WebViewOptions());
        onWayland.Attach(wayland);
        onX11.Attach(x11);
        onWayland.SetPlacement(new WebViewPlacement(0, new PixelRect(0, 0, 300, 200), 1f) { WindowSystem = "wayland" });
        onX11.SetPlacement(new WebViewPlacement(0, new PixelRect(0, 0, 300, 200), 1f) { WindowSystem = "x11" });
        Assert.IsType<CefFrames>(wayland.Renderer);
        Assert.Null(x11.Renderer);
    }

    [CefFact]
    public void WithoutSharedTextures_AGpuSurfaceStillGetsThePage()
    {
        // This process's CEF is headless, so no shared textures, as when the engine has no GL
        // of its own: the renderer is registered, and its frames come as pixels to upload.
        HeadlessFirst();
        using var engine = new CefEngine();
        var host = new TestHost();
        var events = new ConcurrentQueue<WebEngineEvent>();
        using var view = (CefView)engine.CreateView(new WebViewOptions());
        view.Event += events.Enqueue;
        view.Event += CefPolicyTests.AllowNavigation;
        view.Attach(host);
        view.SetPlacement(new WebViewPlacement(0, new PixelRect(0, 0, 300, 200), 1f) { WindowSystem = "wayland" });
        var frames = Assert.IsType<CefFrames>(host.Renderer);
        frames.OnModeDecided(GpuSinkMode.Gpu);
        var clock = Stopwatch.StartNew();
        while (!events.Any(e => e is WebEngineEvent.Ready) && clock.Elapsed.TotalSeconds < 30) Thread.Sleep(20);
        view.NavigateToString("<title>t</title><body style='background:#0f0'>");
        while (!(frames.TryGetFrameSize(out int w, out _) && w == 300) && clock.Elapsed.TotalSeconds < 30) Thread.Sleep(20);
        Assert.True(frames.Uploading);
        Assert.True(frames.TryGetFrameSize(out int width, out int height));
        Assert.Equal((300, 200), (width, height));
        Assert.False(host.Pixels.IsLoaded);   // the page's own sink is for a surface that wants pixels
    }

    private static (int ExitCode, string Output) RunProbe(string capture)
    {
        var tfm = new DirectoryInfo(AppContext.BaseDirectory);
        var config = tfm.Parent!;
        var tests = config.Parent!.Parent!.Parent!;
        var probe = Path.Combine(tests.FullName, "HultUI.UiTests.VulkanProbe", "bin", config.Name, tfm.Name, "HultUI.UiTests.VulkanProbe");
        Assert.True(File.Exists(probe), "not built: " + probe);
        var start = new ProcessStartInfo(probe, "--webviews") { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var name in new[] { "HULTUI_RENDERER", "HULTUI_CEF_ZERO_COPY", "HULTUI_CEF_SWITCHES" }) start.Environment.Remove(name);
        start.Environment["HULTUI_VULKAN_CAPTURE"] = capture;
        start.Environment["HULTUI_VULKAN_CAPTURE_FRAME"] = "300";
        start.Environment["HULTUI_STARTUP"] = "1";
        start.Environment["HULTUI_CEF_LOG"] = "info";
        start.Environment["SDL_WINDOW_ACTIVATE_WHEN_SHOWN"] = "0";
        using var p = Process.Start(start)!;
        var stdout = p.StandardOutput.ReadToEndAsync();
        var stderr = p.StandardError.ReadToEndAsync();
        if (!p.WaitForExit(60_000))
        {
            p.Kill(entireProcessTree: true);
            return (-1, "timed out");
        }
        return (p.ExitCode, stdout.Result + stderr.Result);
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
        at++;
        return (width, height, file[at..]);
    }

    private static (byte R, byte G, byte B) Pixel(byte[] pixels, int width, int x, int y)
    {
        int i = (y * width + x) * 3;
        return (pixels[i], pixels[i + 1], pixels[i + 2]);
    }
}
