using HultUI.Core.Builders;
using HultUI.Core.Primitives;
using HultUI.Core.Rendering;
using HultUI.Headless;
using HultUI.Media.Testing;
using Xunit;

namespace HultUI.Media.Tests;

/// <summary>
/// The still over the scrubber. The player has no idea where a preview comes from — an
/// app answers for a position — so what these pin is the asking: only while the pointer
/// is on the track, at the position it is on, and nothing at all when the app has no
/// frame to give.
/// </summary>
public class VideoPreviewTests
{
    private sealed class Fixture : IDisposable
    {
        public HeadlessSession Session { get; }
        public FakeMediaBackend Backend { get; } = new() { Duration = TimeSpan.FromMinutes(10) };
        public MediaPlayer Player { get; }
        public VideoPlayer VideoPlayer { get; }
        public List<TimeSpan> Asked { get; } = [];

        public Fixture(VideoPreviewFrame? frame)
        {
            Session = HeadlessSession.Create(640, 400);
            Player = new MediaPlayer(Backend);
            VideoPlayer = new VideoPlayer(Player)
            {
                AutoHideControls = false,
                PreviewFrame = at => { Asked.Add(at); return frame; },
            };
            Session.Application.SetRoot(Box.New().Children(VideoPlayer.Mount().Width(640f).Height(400f)));
            Player.Open(MediaSource.FromFile("clip.mp4"));
            Session.PumpFrames(2);
        }

        /// <summary>Moves the pointer onto the seek track, a fraction of the way along it.</summary>
        public void HoverScrubber(double fraction)
        {
            var seek = Session.Window.FindByAutomationId("video-seek")
                ?? throw new InvalidOperationException("no scrubber:\n" + Session.Window.DumpAccessibilityTree());
            var box = seek.Element.Layout.AbsoluteContentBox;
            Session.Window.MoveMouse(box.X + (float)(box.Width * fraction), box.Y + box.Height / 2f);
            Session.PumpFrames(2);
        }

        public HeadlessElement? Card => Session.Window.FindByAutomationId("video-preview-card");

        public void Dispose() => Session.Dispose();
    }

    private sealed class StubImage(Size size) : IImageSource
    {
        public Size IntrinsicSize => size;
        public bool IsLoaded => true;
        public string? AlternateText => null;
    }

    [Fact]
    public void PointingAtTheTrackShowsTheFrameForThatPosition()
    {
        var sheet = new StubImage(new Size(1600, 720));
        using var f = new Fixture(new VideoPreviewFrame(sheet, new Rect(320, 0, 320, 180), TimeSpan.FromMinutes(3)));

        Assert.Null(f.Card);           // nothing until the pointer is on the track

        f.HoverScrubber(0.5);

        Assert.NotNull(f.Card);
        Assert.NotEmpty(f.Asked);
        // Halfway along ten minutes, give or take the thumb's own width.
        Assert.InRange(f.Asked[^1].TotalMinutes, 4.0, 6.0);
        // The label reads the frame's own time, not the pointer's: a still every ten
        // seconds cannot honestly claim 4:57.
        Assert.True(Shows(f.Session, "3:00"), f.Session.Window.DumpAccessibilityTree());
    }

    [Fact]
    public void ThePreviewSitsOverThePointItIsPreviewing()
    {
        using var f = new Fixture(new VideoPreviewFrame(new StubImage(new Size(320, 180))));

        f.HoverScrubber(0.25);
        f.Session.PumpFrames(2);          // the geometry comes from the laid-out frame

        var card = f.Card!.Element.Layout.AbsoluteBorderBox;
        var track = f.Session.Window.FindByAutomationId("video-seek")!.Element.Layout.AbsoluteContentBox;
        float wanted = track.X + track.Width * 0.25f;

        // Centred on the hovered point. The refs a build registers point at the elements
        // it just made, whose layout has not run — reading the track from those put this
        // card in the corner, which is what this pins.
        Assert.InRange(card.X + card.Width / 2f, wanted - 30f, wanted + 30f);
    }

    [Fact]
    public void AnAppWithNoFrameForThatSecondShowsNothing()
    {
        using var f = new Fixture(frame: null);

        f.HoverScrubber(0.5);

        Assert.NotEmpty(f.Asked);      // it was asked
        Assert.Null(f.Card);           // and said nothing, so nothing is drawn
    }

    [Fact]
    public void LeavingTheTrackTakesThePreviewWithIt()
    {
        using var f = new Fixture(new VideoPreviewFrame(new StubImage(new Size(320, 180))));
        f.HoverScrubber(0.3);
        Assert.NotNull(f.Card);

        // Off the track, onto the picture.
        f.Session.Window.MoveMouse(320, 80);
        f.Session.PumpFrames(2);

        Assert.Null(f.Card);
    }

    private static bool Shows(HeadlessSession session, string text)
    {
        var dump = session.Window.DumpAccessibilityTree();
        return dump.Contains(text, StringComparison.Ordinal);
    }
}
