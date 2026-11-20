using System.Globalization;
using HultUI;
using HultUI.Core.Builders;
using HultUI.Core.Elements;
using HultUI.Core.Layout;
using HultUI.Core.Primitives;
using HultUI.Core.Rendering;
using HultUI.Media;
using HultUI.Media.Testing;

/// <summary>
/// --bar-fade (Vulkan, or GL with --renderer=default): a video player filling a window Reel's size (1280x760, 1920x1140
/// pixels at 150 %), playing a FakeMediaBackend, its bar left to hide and fade out, then paused and played again for a
/// second fade, each frame drawn at 1/120 s. Prints the process's local video memory (DXGI) before the first fade,
/// after it and after the second, the window's and the bar's size in pixels, and Skia's cache after the first fade
/// (Windows). A fade through a window-sized layer left that layer's texture in Skia's cache for the window's life:
/// 12 MB at 1920x1140 (plan 17, round 17).
/// </summary>
internal static class BarFadeProbe
{
    private static readonly TimeSpan Frame = TimeSpan.FromSeconds(1 / 120.0);

    public static int Run(RendererKind renderer)
    {
        using var app = Foreground.NoActivate(new Application("vulkan-probe", 1280, 760, renderer: renderer));
        var backend = new FakeMediaBackend { Duration = TimeSpan.FromMinutes(5) };
        using var player = new MediaPlayer(backend);
        var video = new VideoPlayer(player) { AutoHideDelay = TimeSpan.FromSeconds(0.25) };
        // In a box, as an app holds it: the component's host as the window's root itself was never mounted, so the
        // player never started its hide countdown (headless too).
        var root = Box.New().Width(Dimension.Percent(100)).Height(Dimension.Percent(100))
            .Children(video.Mount().Width(Dimension.Percent(100)).Height(Dimension.Percent(100)));
        app.SetRoot(root);
        player.Open(MediaSource.FromFile("clip.mp4"));
        // Paused, the bar is up and whole: everything but the fade is drawn, and allocated, first.
        for (int i = 0; i < 12; i++)
            app.RunFrame(TimeSpan.FromMilliseconds(16), forceRender: true);

        var window = app.PrimaryWindow;
        double before = DialogFadeProbe.LocalMB();
        player.Play();                      // hides 0.25 s later and fades out over 0.3 s
        Fade(app);
        double first = DialogFadeProbe.LocalMB();
        string skiaFirst = DialogFadeProbe.Skia(app);
        bool hidden = !video.ControlsVisible;

        player.Pause();                     // the bar back, whole
        for (int i = 0; i < 6; i++)
            app.RunFrame(Frame, forceRender: true);
        player.Play();
        Fade(app);
        double second = DialogFadeProbe.LocalMB();

        float scale = window.DisplayScale;
        var bar = Find(root, "video-controls")?.Layout.AbsoluteBorderBox ?? default;
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"probe: bar-fade window {MathF.Round(window.LayoutWidth * scale)}x{MathF.Round(window.LayoutHeight * scale)} bar {MathF.Round(bar.Width * scale)}x{MathF.Round(bar.Height * scale)} hidden {hidden} local before {before:0.00} first {first:0.00} second {second:0.00} MB; skia {skiaFirst}"));
        return 0;
    }

    // The hide's delay, the fade and a few frames past it, each frame forced: the probe's own clock.
    private static void Fade(Application app)
    {
        for (int i = 0; i < 84; i++)
            app.RunFrame(Frame, forceRender: true);
    }

    private static Element? Find(Element element, string automationId)
    {
        if (element.AutomationId == automationId) return element;
        foreach (var child in element.Children)
            if (Find(child, automationId) is { } found) return found;
        return null;
    }
}
