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
}
