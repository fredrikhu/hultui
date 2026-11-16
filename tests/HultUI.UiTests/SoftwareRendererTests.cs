using System.Text.RegularExpressions;
using Xunit;

namespace HultUI.UiTests;

/// <summary>
/// The software renderer (<c>RendererKind.Software</c>) on a real window, through the probe of
/// <see cref="VulkanProbeTests"/>: its frame reaches the window, and an app falls back to it when
/// OpenGL cannot start. The headless driver draws into the same raster surface but never shows
/// it, so only a real window sees the copy into SDL's window surface.
/// </summary>
[Collection("ui")]
public sealed partial class SoftwareRendererTests
{
    /// <summary>An OpenGL library SDL cannot load, so it refuses an OpenGL window: a machine with
    /// no OpenGL at all.</summary>
    private static readonly Dictionary<string, string?> NoOpenGL =
        new() { ["SDL_OPENGL_LIBRARY"] = Path.Combine(Path.GetTempPath(), "hultui-no-such-gl", "opengl32.dll") };

    /// <summary>
    /// What the renderer drew (<c>Window.SaveFrame</c>) and what reached the window, read from its
    /// device context: red top left, green bottom left, blue on the right. Software only: a GL
    /// window's pixels are not always in its device context, so GL cannot be read this way.
    /// </summary>
    [UiFact]
    public void TheFrame_IsDrawnAndReachesTheWindow()
    {
        string png = Path.Combine(Path.GetTempPath(), $"hultui-software-frame-{Guid.NewGuid():N}.png");
        try
        {
            var run = VulkanProbeTests.RunProbe(
                new() { ["HULTUI_RENDERER"] = "software" }, $"--save-frame=\"{png}\" --window-pixels");
            Assert.True(run.ExitCode == 0, run.Output);
            Assert.Contains(" software window=", run.Output);

            using var frame = SkiaSharp.SKBitmap.Decode(png);
            Assert.NotNull(frame);
            AssertQuarters(
                frame.GetPixel(frame.Width / 4, frame.Height / 4),
                frame.GetPixel(frame.Width * 3 / 4, frame.Height / 2),
                frame.GetPixel(frame.Width / 4, frame.Height * 3 / 4),
                "saved", run.Output);
            AssertWindowQuarters(run.Output);
        }
        finally
        {
            File.Delete(png);
        }
    }

    /// <summary>No OpenGL driver SDL can load: SDL refuses the OpenGL window, and an app that
    /// asked for nothing gets a software window showing its frame instead of a crash.</summary>
    [UiFact]
    public void WithNoOpenGL_AnAppGetsTheSoftwareRenderer()
    {
        var run = VulkanProbeTests.RunProbe(NoOpenGL, "--renderer=default --window-pixels");

        Assert.True(run.ExitCode == 0, run.Output);
        Assert.Contains("OpenGL window creation failed", run.Output);
        Assert.Contains(" software window=", run.Output);
        AssertWindowQuarters(run.Output);
    }

    /// <summary>
    /// <c>HULTUI_RENDERER=gl</c> is how a renderer is tested, so a failure then is the answer: a
    /// quiet software window would make every such run a false negative.
    /// </summary>
    [UiFact]
    public void WhenTheEnvironmentAskedForGL_AFailureIsNotHiddenByAFallback()
    {
        var run = VulkanProbeTests.RunProbe(new(NoOpenGL) { ["HULTUI_RENDERER"] = "gl" }, "--renderer=default");

        Assert.True(run.ExitCode != 0, "the probe exited cleanly:\n" + run.Output);
        Assert.Contains("SDL_CreateWindow failed", run.Output);
        Assert.DoesNotContain("falling back to software", run.Output);
        Assert.DoesNotContain("probe: done", run.Output);
    }

    private static void AssertWindowQuarters(string output)
    {
        var match = WindowLine().Match(output);
        Assert.True(match.Success, "no window line:\n" + output);
        Assert.True(match.Groups["read"].Value == "True", "reading the window failed:\n" + output);
        AssertQuarters(Rgb(match, "topLeft"), Rgb(match, "right"), Rgb(match, "bottomLeft"), "in the window", output);
    }

    private static SkiaSharp.SKColor Rgb(Match match, string name)
    {
        var parts = match.Groups[name].Value.Split(',').Select(byte.Parse).ToArray();
        return new SkiaSharp.SKColor(parts[0], parts[1], parts[2]);
    }

    private static void AssertQuarters(SkiaSharp.SKColor topLeft, SkiaSharp.SKColor right, SkiaSharp.SKColor bottomLeft,
        string where, string output)
    {
        Assert.True(topLeft is { Red: > 200, Green: < 60, Blue: < 60 }, $"{where}, top left was {topLeft}, expected red\n{output}");
        Assert.True(right is { Red: < 60, Green: < 60, Blue: > 200 }, $"{where}, right half was {right}, expected blue\n{output}");
        Assert.True(bottomLeft is { Red: < 60, Green: > 200, Blue: < 60 }, $"{where}, bottom left was {bottomLeft}, expected green\n{output}");
    }

    [GeneratedRegex(@"probe: window read=(?<read>\w+) topLeft=(?<topLeft>[\d,]+) right=(?<right>[\d,]+) bottomLeft=(?<bottomLeft>[\d,]+)")]
    private static partial Regex WindowLine();
}
