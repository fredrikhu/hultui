using HultUI.Core.Accessibility;
using HultUI.Core.Builders;
using HultUI.Headless;
using HultUI.Media.Testing;
using Xunit;

namespace HultUI.Media.Tests;

/// <summary>
/// What drawing the transport bar costs while it is hidden and while it fades (plan 17, round 17).
/// The bar stays mounted at opacity 0 while a film plays, and the picture repaints with every
/// frame, so whatever the bar draws there is drawn on every video frame.
/// </summary>
public class VideoPlayerBarPaintTests
{
    private sealed class Fixture : IDisposable
    {
        public HeadlessSession Session { get; }
        public FakeMediaBackend Backend { get; } = new();
        public MediaPlayer Player { get; }
        public VideoPlayer VideoPlayer { get; }

        public Fixture()
        {
            Session = HeadlessSession.Create(640, 400, options: new HeadlessOptions { RecordDrawCalls = true });
            Player = new MediaPlayer(Backend);
            VideoPlayer = new VideoPlayer(Player) { AutoHideDelay = TimeSpan.FromSeconds(0.5) };
            Session.Application.SetRoot(Box.New().Children(VideoPlayer.Mount().Width(640f).Height(400f)));
            Player.Open(MediaSource.FromFile("clip.mp4"));
            Session.PumpFrames(2);
        }

        public Core.Elements.Element Bar => Session.Window.FindByAutomationId("video-controls")!.Element;

        /// <summary>Runs frames worth <paramref name="seconds"/> of animation time.</summary>
        public void Elapse(double seconds) =>
            Session.PumpFrames((int)Math.Ceiling(seconds / Session.DefaultFrameDelta.TotalSeconds));

        /// <summary>What one frame drawn now records, as a video frame arriving would draw it.</summary>
        public string DrawFrame()
        {
            Session.Window.RenderLog.Clear();
            Session.Window.CapturePixels().Dispose();
            return Session.Window.RenderLog.ToString();
        }

        public void Dispose()
        {
            Player.Dispose();
            Session.Dispose();
        }
    }

    [Fact]
    public void TheHiddenBar_IsNotDrawn_ButStaysInTheTree()
    {
        // It was drawn at opacity 0 on every frame, its group, its times, its sliders, its glyphs.
        using var f = new Fixture();
        f.Player.Play();
        f.Elapse(1.2);                                    // hidden 0.5 s in, faded out by 0.8
        Assert.False(f.VideoPlayer.ControlsVisible);
        Assert.Equal(0f, f.Bar.Opacity);

        var log = f.DrawFrame();
        Assert.DoesNotContain("PushOpacity", log);
        Assert.DoesNotContain("0:10", log);              // the duration label

        // Paint only: a reader still finds the bar's buttons where they were.
        var pause = f.Session.Window.FindByRole(AccessibilityRole.Button, "Pause");
        Assert.NotNull(pause);
        Assert.True(pause!.Element.Layout.AbsoluteBorderBox.Height > 0);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AFadeFrame_DrawsTheBarThroughALayerOfTheBarsOwnSize(bool muted)
    {
        // The scrubber, the volume slider and the three drawn glyphs could not say where they
        // paint, so every fade frame drew the bar through a layer the window's size, and Skia
        // kept that texture for the window's life (Reel at 1920x1140: 2.19 Mpx for a 1280x52 bar).
        using var f = new Fixture();
        f.VideoPlayer.FadeDuration = TimeSpan.FromSeconds(1);
        f.Player.Muted = muted;                           // the speaker glyph has two drawings
        f.Player.Play();
        f.Session.Window.FindByAutomationId("video-seek")!.Focus();   // the scrubber's ring
        f.Elapse(0.8);                                    // hidden 0.5 s in; the fade is under way
        Assert.InRange(f.Bar.Opacity, 0.2f, 0.9f);

        var groups = f.DrawFrame().Split('\n').Where(l => l.TrimStart().StartsWith("PushOpacity", StringComparison.Ordinal)).ToList();
        var bar = f.Bar.Layout.AbsoluteBorderBox;
        var line = Assert.Single(groups);
        Assert.Contains(FormattableString.Invariant($"bounds=({bar.X:0.##},{bar.Y:0.##} {bar.Width:0.##}x{bar.Height:0.##})"), line);
    }

    /// <summary>
    /// The pause, jump and speaker glyphs draw nothing past their boxes grown by 2, the bounds
    /// they report: drawn whole on the bar, every pixel from there out to 6 past the box is the
    /// bar's own colour, at 1x and 1.5x (a pixel more, as the layer itself is padded).
    /// </summary>
    [Theory]
    [InlineData(1f, false)]
    [InlineData(1.5f, false)]
    [InlineData(1.5f, true)]
    public void TheBarsGlyphs_DrawNothingPastTheirBounds(float scale, bool muted)
    {
        using var session = HeadlessSession.Create(640, 400, scale);
        var backend = new FakeMediaBackend();
        using var player = new MediaPlayer(backend);
        var video = new VideoPlayer(player) { AutoHideControls = false };
        session.Application.SetRoot(Box.New().Children(video.Mount().Width(640f).Height(400f)));
        player.Open(MediaSource.FromFile("clip.mp4"));
        player.Muted = muted;
        player.Play();                                    // the pause glyph, not the play icon
        session.PumpFrames(3);

        var bar = session.Window.FindByAutomationId("video-controls")!.Element;
        var glyphs = new List<Core.Elements.CanvasElement>();
        var stack = new Stack<Core.Elements.Element>([bar]);
        while (stack.Count > 0)
        {
            var e = stack.Pop();
            if (e is Core.Elements.CanvasElement canvas) glyphs.Add(canvas);
            foreach (var c in e.Children) stack.Push(c);
        }
        Assert.Equal(4, glyphs.Count);                    // pause, back, forward, speaker

        using var image = session.Window.CapturePixels();
        using var pixels = image.PeekPixels()!;
        foreach (var glyph in glyphs)
        {
            var box = glyph.Layout.AbsoluteBorderBox;
            var reported = glyph.PaintBounds(null!)!.Value;
            Assert.Equal(box.Inflate(new Core.Primitives.Thickness(2f)), reported);
            // In device pixels: inside, the reported bounds and the layer's pixel of slack; outside, out to 6.
            int x0 = (int)MathF.Floor(reported.X * scale) - 1, y0 = (int)MathF.Floor(reported.Y * scale) - 1;
            int x1 = (int)MathF.Ceiling(reported.Right * scale) + 1, y1 = (int)MathF.Ceiling(reported.Bottom * scale) + 1;
            int ox0 = (int)MathF.Floor((box.X - 6) * scale), oy0 = (int)MathF.Floor((box.Y - 6) * scale);
            int ox1 = (int)MathF.Ceiling((box.Right + 6) * scale), oy1 = (int)MathF.Ceiling((box.Bottom + 6) * scale);
            var backdrop = pixels.GetPixelColor(ox0, oy0);
            int inked = 0, outside = 0;
            for (int y = oy0; y < oy1; y++)
                for (int x = ox0; x < ox1; x++)
                {
                    bool inBounds = x >= x0 && x < x1 && y >= y0 && y < y1;
                    bool ink = pixels.GetPixelColor(x, y) != backdrop;
                    if (ink && inBounds) inked++;
                    if (ink && !inBounds) outside++;
                }
            Assert.True(inked > 10, $"the glyph at {box} drew only {inked} pixels");
            Assert.True(outside == 0, $"the glyph at {box} drew {outside} pixels past its bounds {reported}");
        }
    }
}
