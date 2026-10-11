using HultUI.Core.Builders;
using HultUI.Core.Components;
using HultUI.Core.Elements;
using HultUI.Core.Layout;
using HultUI.Core.Reactivity;
using HultUI.Headless;
using HultUI.Media.Testing;
using Xunit;

namespace HultUI.Media.Tests;

/// <summary>
/// A rebuild of the player that moves nothing leaves its layout standing. The root, the picture,
/// the canvas glyphs and the sliders marked their layout on every patch — eight marks a rebuild,
/// each reaching the root — so every rebuild laid the whole player out again; they now mark it
/// only when something they measure changed (Element.PatchMarksOwnLayout), as boxes, labels and
/// images already did.
/// </summary>
public class PlayerLayoutTests
{
    private static readonly MediaSource Clip = MediaSource.FromFile("clip.mp4");

    private static List<Element> MarkedForLayout(Element root)
    {
        var marked = new List<Element>();
        void Walk(Element e)
        {
            if (e.DirtyFlags.HasFlag(DirtyFlags.Layout)) marked.Add(e);
            foreach (var child in e.Children) Walk(child);
        }
        Walk(root);
        return marked;
    }

    [Fact]
    public void ARebuildThatMovesNothing_LaysNothingOutAgain()
    {
        using var session = HeadlessSession.Create(640, 400);
        using var player = new MediaPlayer(new FakeMediaBackend());
        var video = new VideoPlayer(player);
        var host = video.Mount().Width(640f).Height(400f);
        session.Application.SetRoot(Box.New().Children(host));
        player.Open(Clip);
        session.PumpFrames(3);
        int builds = video.Builds;

        // Renames the two jump buttons: the player rebuilds, and nothing in it changes size.
        video.JumpStep = TimeSpan.FromSeconds(3);

        Assert.Equal(builds + 1, video.Builds);
        var marked = MarkedForLayout(host);
        Assert.True(marked.Count == 0,
            "marked for layout: " + string.Join(", ", marked.Select(e => e.GetType().Name + " " + e.AutomationId)));
        Assert.NotNull(session.Window.FindByRole(HultUI.Core.Accessibility.AccessibilityRole.Button, "Forward 3 s"));
    }

    /// <summary>The picture measures to its frame, so a patch that hands it another player is
    /// laid out again, and one that hands it the same player is not.</summary>
    [Fact]
    public void ThePicture_IsLaidOutAgainForANewPlayer_AndOnlyThen()
    {
        using var session = HeadlessSession.Create(640, 400);
        using var small = new MediaPlayer(new FakeMediaBackend { VideoWidth = 320, VideoHeight = 180 });
        using var large = new MediaPlayer(new FakeMediaBackend { VideoWidth = 400, VideoHeight = 300 });
        var picture = new Picture(small);
        // Not stretched across the window: the picture is as wide as its frame.
        session.Application.SetRoot(Flex.Row().AlignItems(AlignItems.FlexStart).Children(picture.Mount()));
        small.Open(Clip);
        large.Open(Clip);
        session.PumpFrames(3);
        var element = (VideoElement)session.Window.FindByAutomationId("picture")!.Element;
        Assert.Equal(320f, element.Layout.Width);

        picture.Rebuild();
        Assert.False(element.DirtyFlags.HasFlag(DirtyFlags.Layout), "an unchanged patch marked the picture's layout");

        picture.Player.Value = large;
        Assert.True(element.DirtyFlags.HasFlag(DirtyFlags.Layout), "a new player did not mark the picture's layout");
        session.PumpFrame();
        Assert.Equal(400f, element.Layout.Width);
        Assert.Equal(300f, element.Layout.Height);
    }

    private sealed class Picture(MediaPlayer player) : Component
    {
        public Signal<MediaPlayer> Player { get; } = new(player);

        public void Rebuild() => MarkDirty();

        public override Element Build() => Video.New(Player.Value).AutomationId("picture");
    }
}
