using System.Reflection;
using HultUI.Core.Builders;
using HultUI.Core.Components;
using HultUI.Core.Elements;
using HultUI.Core.Layout;
using HultUI.Core.Primitives;
using HultUI.Core.Reactivity;
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

    /// <summary>The one leaf whose measurement reads the height offered (Element.IgnoresAvailableHeight
    /// is false for it): kept for any height, the video would stay as tall as the window was.</summary>
    [Fact]
    public void Measure_FollowsTheHeightOffered_WhenTheWindowGetsShorter()
    {
        using var session = HeadlessSession.Create(400, 300);
        var backend = new FakeMediaBackend { VideoWidth = 1920, VideoHeight = 1080 };
        using var player = new MediaPlayer(backend);
        var video = Video.New(player);
        // In a row the height is the cross axis: offered, never forced.
        session.Application.SetRoot(Flex.Row().AlignItems(AlignItems.FlexStart).Children(video));
        player.Open(Clip);
        session.PumpFrame();
        Assert.Equal(225, video.Layout.Height, 0.5);

        session.Window.SetClientSize(400, 150);
        session.PumpFrame();

        Assert.Equal(150, video.Layout.Height, 0.5);
        Assert.Equal(1920f * 150 / 1080, video.Layout.Width, 0.5);
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
    public void Listening_FollowsBeingOnScreen()
    {
        using var session = Recording();
        var backend = new FakeMediaBackend();
        using var player = new MediaPlayer(backend);
        var video = Video.New(player);
        Assert.False(video.IsListeningForFrames);    // built, not yet in a tree

        var box = Box.New().Children(video);
        Assert.False(video.IsListeningForFrames);    // in a tree no window shows
        session.Application.SetRoot(box);
        session.PumpFrame();
        Assert.True(video.IsListeningForFrames);

        // What a rebuild that drops the element does. (SetRoot, the exit that skips the
        // element's unmount, is AnElementLeftBehindWithoutAnUnmount_StopsListeningAtTheNextFrame.)
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

    /// <summary>A component that builds a fresh video element on every rebuild and keeps each one.</summary>
    private sealed class Rebuilding(MediaPlayer player) : Component
    {
        public Signal<int> Generation { get; } = new(0);
        public List<VideoElement> Built { get; } = [];

        public override Element Build()
        {
            _ = Generation.Value;
            var video = Video.New(player);
            Built.Add(video);
            return Box.New().Children(video);
        }
    }

    /// <summary>Who the player would call on its next frame.</summary>
    private static object?[] FrameListeners(MediaPlayer player)
    {
        var field = typeof(MediaPlayer).GetField(nameof(MediaPlayer.FrameArrived), BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("MediaPlayer.FrameArrived is no longer a field-like event");
        return ((Delegate?)field.GetValue(player))?.GetInvocationList().Select(d => d.Target).ToArray() ?? [];
    }

    private static IEnumerable<Element> Descendants(Element element)
    {
        foreach (var child in element.Children)
        {
            yield return child;
            foreach (var below in Descendants(child)) yield return below;
        }
    }

    [Fact]
    public void ARebuild_LeavesOnlyTheLiveElementListening()
    {
        using var session = Recording();
        var backend = new FakeMediaBackend();
        using var player = new MediaPlayer(backend);
        var view = new Rebuilding(player);
        session.Application.SetRoot(view.Mount());
        player.Open(Clip);
        session.PumpFrame();

        for (int i = 1; i <= 3; i++)
        {
            view.Generation.Value = i;
            session.PumpFrame();
        }

        // The first build is the live element; every later one built a twin, which got a
        // parent (and so OnMounted) from its builder, was patched into the live element
        // and thrown away — without ever being unmounted. (Opening the clip rebuilds too:
        // the builder reads the player's source for the accessible name.)
        var live = view.Built[0];
        var twins = view.Built.Skip(1).ToArray();
        Assert.True(twins.Length >= 3);
        Assert.True(session.Window.Window.IsInTree(live));
        Assert.All(twins, twin => Assert.True(twin.IsDetached));

        Assert.True(live.IsListeningForFrames);
        Assert.All(twins, twin => Assert.False(twin.IsListeningForFrames));
        Assert.Same(live, Assert.Single(FrameListeners(player)));

        // A frame reaches the live element, which repaints.
        session.Window.RenderLog.Clear();
        backend.Sink!.RequestRepaint();
        Assert.True(session.PumpFrame());
        Assert.Contains("DrawImage", session.Window.RenderLog.ToString());
    }

    [Fact]
    public void TheVideoPlayer_RebuildingAroundItsPicture_LeavesOneListener()
    {
        // The shape the twin leak was found in: VideoPlayer rebuilds its whole overlay as
        // playback starts and the bar fades, and its picture is one child of that build.
        using var session = HeadlessSession.Create(640, 400);
        var backend = new FakeMediaBackend { FrameRate = 10 };
        using var player = new MediaPlayer(backend);
        var root = Box.New().Children(new VideoPlayer(player).Mount().Width(640f).Height(400f));
        session.Application.SetRoot(root);
        player.Open(Clip);
        session.PumpFrames(2);
        player.Play();
        for (int i = 0; i < 40; i++)
        {
            backend.Advance(TimeSpan.FromMilliseconds(100));
            session.PumpFrame();
        }

        var live = Assert.Single(Descendants(root).OfType<VideoElement>());
        Assert.Same(live, Assert.Single(FrameListeners(player)));
    }

    [Fact]
    public void RemovingAnAncestor_StopsListeningAtOnce()
    {
        using var session = Recording();
        var backend = new FakeMediaBackend();
        using var player = new MediaPlayer(backend);
        var video = Video.New(player);
        var panel = Box.New().Children(video);
        var root = Box.New().Children(panel);
        session.Application.SetRoot(root);
        session.PumpFrame();
        Assert.True(video.IsListeningForFrames);

        // Removal unmounts the whole subtree, not just the panel.
        root.RemoveChild(panel);

        Assert.False(video.IsListeningForFrames);
        Assert.Empty(FrameListeners(player));
    }

    [Fact]
    public void AnElementLeftBehindWithoutAnUnmount_StopsListeningAtTheNextFrame()
    {
        using var session = Recording();
        var backend = new FakeMediaBackend();
        using var player = new MediaPlayer(backend);
        var video = Video.New(player);
        var panel = Box.New().Children(video);
        var root = Box.New().Children(panel);
        session.Application.SetRoot(root);
        session.PumpFrame();
        Assert.True(video.IsListeningForFrames);

        // SetRoot unmounts nothing, so this never reaches the video's OnUnmounted.
        session.Application.SetRoot(Box.New());
        Assert.True(video.IsListeningForFrames);

        backend.Sink!.RequestRepaint();

        Assert.False(video.IsListeningForFrames);
        Assert.Empty(FrameListeners(player));
    }
}
