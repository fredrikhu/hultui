using HultUI.Core.Builders;
using HultUI.Core.Layout;
using HultUI.Core.Primitives;
using HultUI.Headless;
using HultUI.Media.Testing;
using Xunit;

namespace HultUI.Media.Tests;

/// <summary>
/// <see cref="VideoElement"/> under the headless host: it repaints itself when a frame
/// arrives, measures to the video's aspect ratio, names itself after the source, and
/// stops listening when it leaves the tree.
/// </summary>
public class VideoElementTests
{
    private static readonly MediaSource Clip = MediaSource.FromFile("/media/talk.mkv");

    private static HeadlessSession Recording() =>
        HeadlessSession.Create(400, 300, options: new HeadlessOptions { RecordDrawCalls = true });

    [Fact]
    public void EachFrame_IsPainted_WithoutMarkingAnythingByHand()
    {
        using var session = Recording();
        var backend = new FakeMediaBackend { FrameRate = 10 };
        using var player = new MediaPlayer(backend);
        session.Application.SetRoot(Box.New().Children(Video.New(player)));
        session.PumpFrame();
        // Visited before any frame exists — that visit is how the source learns its
        // surface — but with nothing to show.
        Assert.Contains("(not loaded)", session.Window.RenderLog.ToString());

        player.Open(Clip);                     // publishes frame 1
        session.Window.RenderLog.Clear();
        Assert.True(session.PumpFrame());
        Assert.Contains("version=1", session.Window.RenderLog.ToString());

        player.Play();
        backend.Advance(TimeSpan.FromMilliseconds(100));   // frame 2
        session.Window.RenderLog.Clear();
        Assert.True(session.PumpFrame());
        Assert.Contains("version=2", session.Window.RenderLog.ToString());

        // Idle: nothing arrived, nothing repaints.
        Assert.False(session.PumpFrame());
    }

    [Fact]
    public void Measure_FitsTheAspectRatio_IntoTheAvailableWidth()
    {
        using var session = HeadlessSession.Create(400, 300);
        var backend = new FakeMediaBackend { VideoWidth = 1920, VideoHeight = 1080 };
        using var player = new MediaPlayer(backend);
        var video = Video.New(player);
        session.Application.SetRoot(Box.New().Children(video));
        player.Open(Clip);
        session.PumpFrame();

        Assert.Equal(400, video.Layout.Width, 0.5);
        Assert.Equal(225, video.Layout.Height, 0.5);
    }

    [Fact]
    public void Measure_NeverScalesUp()
    {
        using var session = HeadlessSession.Create(400, 300);
        var backend = new FakeMediaBackend { VideoWidth = 160, VideoHeight = 90 };
        using var player = new MediaPlayer(backend);
        var video = Video.New(player);
        // A column stretches its children sideways by default; opt out so the
        // element's own measure is what lands in the layout.
        session.Application.SetRoot(Box.New().AlignItems(AlignItems.FlexStart).Children(video));
        player.Open(Clip);
        session.PumpFrame();

        Assert.Equal(160, video.Layout.Width, 0.5);
        Assert.Equal(90, video.Layout.Height, 0.5);
    }

    [Fact]
    public void AChangeOfFrameSize_Relayouts()
    {
        using var session = HeadlessSession.Create(400, 300);
        var backend = new FakeMediaBackend { VideoWidth = 1920, VideoHeight = 1080 };
        using var player = new MediaPlayer(backend);
        var video = Video.New(player);
        session.Application.SetRoot(Box.New().AlignItems(AlignItems.FlexStart).Children(video));
        player.Open(Clip);
        session.PumpFrame();
        Assert.Equal(225, video.Layout.Height, 0.5);

        backend.VideoWidth = 1000;             // the stream switched to 1:1
        backend.VideoHeight = 1000;
        backend.PublishFrame();
        session.PumpFrame();

        Assert.Equal(300, video.Layout.Height, 0.5);
        Assert.Equal(300, video.Layout.Width, 0.5);
    }

    [Fact]
    public void TheAccessibleName_IsTheSourcesDisplayName()
    {
        var backend = new FakeMediaBackend();
        using var player = new MediaPlayer(backend);
        player.Open(Clip);

        var video = Video.New(player);

        Assert.Equal("talk.mkv", video.AccessibleName);
        Assert.Equal(Core.Accessibility.AccessibilityRole.Image, video.Role);
    }

    [Fact]
    public void Listening_FollowsMounting()
    {
        using var session = Recording();
        var backend = new FakeMediaBackend();
        using var player = new MediaPlayer(backend);
        var video = Video.New(player);
        Assert.False(video.IsListeningForFrames);    // built, not yet in a tree

        var box = Box.New().Children(video);
        session.Application.SetRoot(box);
        Assert.True(video.IsListeningForFrames);

        // What a rebuild that drops the element does. (Replacing the whole root with
        // SetRoot does not unmount the old tree; that is the window's contract, not
        // this element's.)
        box.RemoveChild(video);
        Assert.False(video.IsListeningForFrames);

        // A frame now touches nothing that is on screen.
        player.Open(Clip);
        session.Window.RenderLog.Clear();
        session.PumpFrame();
        Assert.DoesNotContain("DrawImage", session.Window.RenderLog.ToString());
    }

    [Fact]
    public void SwappingThePlayer_FollowsTheNewOnesFrames()
    {
        using var session = Recording();
        var first = new FakeMediaBackend();
        var second = new FakeMediaBackend { FrameRate = 10 };
        using var one = new MediaPlayer(first);
        using var two = new MediaPlayer(second);
        var video = Video.New(one);
        session.Application.SetRoot(Box.New().Children(video));
        one.Open(Clip);
        session.PumpFrame();

        video.Player = two;
        two.Open(MediaSource.FromFile("other.mp4"));
        two.Play();
        second.Advance(TimeSpan.FromMilliseconds(100));   // two's version 2
        first.PublishFrame();                              // one's version 2, ignored
        session.Window.RenderLog.Clear();
        session.PumpFrame();

        var log = session.Window.RenderLog.ToString();
        Assert.Contains("version=2", log);
        Assert.Same(two.Frames, video.Source);
    }
}
