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

        public Fixture(VideoPreviewFrame? frame) : this(_ => frame) { }

        public Fixture(Func<TimeSpan, VideoPreviewFrame?> frames)
        {
            Session = HeadlessSession.Create(640, 400);
            Player = new MediaPlayer(Backend);
            VideoPlayer = new VideoPlayer(Player)
            {
                AutoHideControls = false,
                PreviewFrame = at => { Asked.Add(at); return frames(at); },
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
    public void ChapterMarksCutTheScrubberIntoChapters()
    {
        using var f = new Fixture(frame: null);
        var seek = () => (HultUI.Core.Elements.SliderElement)f.Session.Window
            .FindByAutomationId("video-seek")!.Element;

        // Nothing handed over: one continuous track, as a scrubber has always been.
        Assert.Null(seek().Segments);

        f.VideoPlayer.ChapterMarks = [TimeSpan.Zero, TimeSpan.FromMinutes(2), TimeSpan.FromMinutes(7)];
        f.Session.PumpFrames(2);

        Assert.Equal([0d, 120d, 420d], seek().Segments);
        // The track fills to the pointer on a scrubber, which an ordinary slider does not.
        Assert.True(seek().ShowHoverFill);
    }

    /// <summary>The scrubber reports the time under the pointer once per frame that may be drawn
    /// (plan 17 round 3), and the card on screen takes it in place (round 4): the player is asked
    /// for a preview once for all the moves of a frame, at the last of them, and is not built
    /// again at all. A 1,000 Hz mouse built it about sixteen times a frame, then once a frame
    /// (about 42 KB a frame, with a card or without).</summary>
    [Fact]
    public void PointingAlongTheScrubber_AsksOnceAFrame_AtTheLastPosition_AndBuildsNothing()
    {
        using var f = new Fixture(new VideoPreviewFrame(new StubImage(new Size(320, 180))));
        f.HoverScrubber(0.2);
        f.Asked.Clear();
        int builds = f.VideoPlayer.Builds;
        var seek = (Core.Elements.SliderElement)f.Session.Window.FindByAutomationId("video-seek")!.Element;
        var box = seek.Layout.AbsoluteContentBox;
        float y = box.Y + box.Height / 2f, x = 0;

        for (int i = 1; i <= 8; i++)
            f.Session.Window.MoveMouse(x = box.X + box.Width * (0.2f + 0.05f * i), y);
        Assert.Empty(f.Asked);

        f.Session.PumpFrame();
        Assert.Equal(builds, f.VideoPlayer.Builds);
        double expected = (x - box.X - seek.ThumbRadius) / (box.Width - 2 * seek.ThumbRadius)
            * f.Backend.Duration!.Value.TotalSeconds;
        var asked = Assert.Single(f.Asked);
        Assert.Equal(expected, asked.TotalSeconds, 0);
        Assert.NotNull(f.Card);
    }

    /// <summary>A still for every minute of the ten: each its own picture, labelled with its minute.</summary>
    private static (StubImage[] Stills, Func<TimeSpan, VideoPreviewFrame?> Frames) EveryMinute()
    {
        var stills = Enumerable.Range(0, 11).Select(_ => new StubImage(new Size(320, 180))).ToArray();
        return (stills, at =>
        {
            int minute = Math.Clamp((int)Math.Round(at.TotalMinutes), 0, 10);
            return new VideoPreviewFrame(stills[minute], null, TimeSpan.FromMinutes(minute));
        });
    }

    [Fact]
    public void MovingAlongTheTrack_MovesTheCardOnScreen_WithTheStillAndTimeForThePoint()
    {
        var (stills, frames) = EveryMinute();
        using var f = new Fixture(frames);
        f.HoverScrubber(0.2);
        var card = f.Card!.Element;
        int builds = f.VideoPlayer.Builds;

        f.HoverScrubber(0.7);

        // The same card, not one made again, and the player not built again either.
        Assert.Same(card, f.Card!.Element);
        Assert.Equal(builds, f.VideoPlayer.Builds);
        int minute = (int)Math.Round(f.Asked[^1].TotalMinutes);
        Assert.InRange(minute, 6, 8);
        var picture = card.Children.OfType<Core.Elements.ImageElement>().Single();
        Assert.Same(stills[minute], picture.Source);
        Assert.Equal($"{minute}:00", card.Children.OfType<Core.Elements.TextElement>().Single().Content);
        Assert.True(Shows(f.Session, $"{minute}:00"), f.Session.Window.DumpAccessibilityTree());

        // Over the new point, as a card built there would stand.
        f.Session.PumpFrame();
        var box = card.Layout.AbsoluteBorderBox;
        var track = f.Session.Window.FindByAutomationId("video-seek")!.Element.Layout.AbsoluteContentBox;
        float wanted = track.X + track.Width * 0.7f;
        Assert.InRange(box.X + box.Width / 2f, wanted - 30f, wanted + 30f);
    }

    [Fact]
    public void TheCardComesAndGoesWithTheStillsTheAppHas()
    {
        // Stills for the first half only: the card goes over the second half and comes back.
        var (_, frames) = EveryMinute();
        using var f = new Fixture(at => at < TimeSpan.FromMinutes(5) ? frames(at) : null);

        f.HoverScrubber(0.2);
        Assert.NotNull(f.Card);
        Assert.True(Shows(f.Session, "2:00"), f.Session.Window.DumpAccessibilityTree());

        f.HoverScrubber(0.8);
        Assert.Null(f.Card);

        f.HoverScrubber(0.3);
        Assert.NotNull(f.Card);
        Assert.True(Shows(f.Session, "3:00"), f.Session.Window.DumpAccessibilityTree());
    }

    [Fact]
    public void AHiddenBarTakesTheCardWithIt_AndItsReturnBringsItBack()
    {
        var (_, frames) = EveryMinute();
        using var f = new Fixture(frames);
        f.VideoPlayer.AutoHideControls = true;
        f.VideoPlayer.AutoHideDelay = TimeSpan.FromMilliseconds(100);
        f.VideoPlayer.FadeDuration = TimeSpan.FromMilliseconds(50);
        f.Player.Play();
        f.HoverScrubber(0.4);
        Assert.NotNull(f.Card);

        // The pointer rests on the track; the bar fades out, and the card goes with it.
        f.Session.PumpUntil(() => !f.VideoPlayer.ControlsVisible, TimeSpan.FromSeconds(5));
        f.Session.PumpFrames(2);
        Assert.Null(f.Card);

        // A move brings the bar back, and the card with it.
        f.HoverScrubber(0.45);
        Assert.NotNull(f.Card);
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

    /// <summary>A still the app had no answer for arrives while the pointer rests on the track:
    /// nothing asks again until the pointer moves, unless the app says so. Reel's background
    /// decoder and Aperture's sheet downloads land this way.</summary>
    [Fact]
    public void RefreshPreview_ShowsAStillThatArrivedUnderARestingPointer_AndTheNextOneInPlace()
    {
        StubImage? arrived = null;
        using var f = new Fixture(_ => arrived is { } still ? new VideoPreviewFrame(still, null, TimeSpan.FromMinutes(1)) : null);
        f.HoverScrubber(0.3);
        Assert.Null(f.Card);

        arrived = new StubImage(new Size(320, 180));
        f.Session.PumpFrames(2);
        Assert.Null(f.Card);              // nobody asked again

        f.VideoPlayer.RefreshPreview();
        f.Session.PumpFrame();
        var card = f.Card?.Element;
        Assert.NotNull(card);
        Assert.True(Shows(f.Session, "1:00"), f.Session.Window.DumpAccessibilityTree());

        // A nearer still: the card on screen takes it, without a build.
        int builds = f.VideoPlayer.Builds;
        var nearer = new StubImage(new Size(320, 180));
        arrived = nearer;
        f.VideoPlayer.RefreshPreview();
        f.Session.PumpFrame();
        Assert.Same(card, f.Card!.Element);
        Assert.Same(nearer, card!.Children.OfType<Core.Elements.ImageElement>().Single().Source);
        Assert.Equal(builds, f.VideoPlayer.Builds);
    }

    [Fact]
    public void RefreshPreview_WithNothingHovered_AsksNothingAndBuildsNothing()
    {
        var (_, frames) = EveryMinute();
        using var f = new Fixture(frames);
        int builds = f.VideoPlayer.Builds;

        for (int i = 0; i < 120; i++) f.VideoPlayer.RefreshPreview();
        f.Session.PumpFrames(2);

        Assert.Empty(f.Asked);
        Assert.Equal(builds, f.VideoPlayer.Builds);
        Assert.Null(f.Card);
    }

    [Fact]
    public void RefreshPreview_UnderAHiddenBar_BuildsNothing()
    {
        // The bar has faded with the pointer resting on the track: a still arriving then has no
        // card to go in, and is not a reason to build the player.
        var (_, frames) = EveryMinute();
        using var f = new Fixture(frames);
        f.VideoPlayer.AutoHideControls = true;
        f.VideoPlayer.AutoHideDelay = TimeSpan.FromMilliseconds(100);
        f.VideoPlayer.FadeDuration = TimeSpan.FromMilliseconds(50);
        f.Player.Play();
        f.HoverScrubber(0.4);
        f.Session.PumpUntil(() => !f.VideoPlayer.ControlsVisible, TimeSpan.FromSeconds(5));
        f.Session.PumpFrames(2);
        int builds = f.VideoPlayer.Builds;

        for (int i = 0; i < 10; i++) f.VideoPlayer.RefreshPreview();
        f.Session.PumpFrames(2);

        Assert.Equal(builds, f.VideoPlayer.Builds);
        Assert.Null(f.Card);
    }

    private static bool Shows(HeadlessSession session, string text)
    {
        var dump = session.Window.DumpAccessibilityTree();
        return dump.Contains(text, StringComparison.Ordinal);
    }
}
