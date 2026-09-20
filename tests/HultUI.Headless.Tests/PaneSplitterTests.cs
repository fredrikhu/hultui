using HultUI.Core.Accessibility;
using HultUI.Core.Builders;
using HultUI.Core.Components;
using HultUI.Core.Elements;
using HultUI.Core.Events;
using HultUI.Core.Layout;
using HultUI.Core.Primitives;
using Xunit;

namespace HultUI.Headless.Tests;

/// <summary>
/// The split is app state and the panes are rebuilt from it, so what these check is that
/// a drag survives the rebuild it causes — a size written onto the element would be
/// correct for one frame and then gone.
/// </summary>
public class PaneSplitterTests
{
    private sealed class TwoPanes : Component
    {
        public readonly PaneSplitter Split;
        public int Commits;
        public ComponentHost? Host;

        public TwoPanes(SplitterAxis axis = SplitterAxis.Horizontal)
        {
            Split = new PaneSplitter(MarkDirty, () => Commits++, 100, 400, 200, axis);
        }

        public override Element Build()
        {
            var handle = Split.Handle();
            if (Split.Axis == SplitterAxis.Horizontal)
            {
                var left = Box.New().Width(Dimension.Px(Split.Size)).FlexShrink(0);
                return Flex.Row().AlignItems(AlignItems.Stretch).Children(left, handle, Box.New().FlexGrow(1));
            }
            var top = Box.New().Height(Dimension.Px(Split.Size)).FlexShrink(0);
            return Flex.Column().AlignItems(AlignItems.Stretch).Children(top, handle, Box.New().FlexGrow(1));
        }

        // The live elements, not the ones the last Build returned: a rebuild patches the
        // tree in place and the returned twins are detached.
        public Element Left => Host!.Children[0].Children[0];
        public SplitterHandleElement Handle => (SplitterHandleElement)Host!.Children[0].Children[1];
    }

    private static (HeadlessSession session, TwoPanes panes) Mount(SplitterAxis axis = SplitterAxis.Horizontal)
    {
        var session = HeadlessSession.Create(width: 800, height: 600);
        var panes = new TwoPanes(axis);
        var host = panes.Mount();
        panes.Host = host;
        panes.Split.AttachDrag(host);
        session.Window.SetRoot(host);
        session.PumpFrame();
        return (session, panes);
    }

    private static Point Center(Element e)
    {
        var b = e.Layout.AbsoluteBorderBox;
        return new Point(b.X + b.Width / 2f, b.Y + b.Height / 2f);
    }

    [Fact]
    public void Dragging_ResizesThePane_AndCommitsOnce()
    {
        var (session, panes) = Mount();
        using var _ = session;
        var mid = Center(panes.Handle);

        session.Window.MouseDown(mid.X, mid.Y);
        session.Window.MoveMouse(mid.X + 30, mid.Y);
        session.PumpFrame();
        session.Window.MoveMouse(mid.X + 90, mid.Y);
        session.PumpFrame();
        Assert.True(panes.Split.IsDragging);
        Assert.Equal(0, panes.Commits);

        session.Window.MouseUp(mid.X + 90, mid.Y);
        session.PumpFrames(2);

        Assert.Equal(290, panes.Split.Size, 1);
        Assert.Equal(290, panes.Left.Layout.AbsoluteBorderBox.Width, 1);
        Assert.False(panes.Split.IsDragging);
        Assert.Equal(1, panes.Commits);
    }

    [Fact]
    public void TheDragIsClamped_SoNeitherPaneIsSqueezedAway()
    {
        var (session, panes) = Mount();
        using var _ = session;
        var mid = Center(panes.Handle);

        session.Window.MouseDown(mid.X, mid.Y);
        session.Window.MoveMouse(4, mid.Y);
        session.PumpFrames(2);
        Assert.Equal(100, panes.Split.Size, 1);
        session.Window.MoveMouse(796, mid.Y);
        session.Window.MouseUp(796, mid.Y);
        session.PumpFrames(2);
        Assert.Equal(400, panes.Split.Size, 1);
    }

    [Fact]
    public void AMoveWithNoGrab_DoesNotResize()
    {
        var (session, panes) = Mount();
        using var _ = session;
        var mid = Center(panes.Handle);
        session.Window.MoveMouse(mid.X, mid.Y);
        session.Window.MoveMouse(mid.X + 120, mid.Y);
        session.PumpFrames(2);
        Assert.Equal(200, panes.Split.Size, 1);
    }

    [Fact]
    public void ArrowKeys_MoveTheSplit_AndCommitEachStep()
    {
        var (session, panes) = Mount();
        using var _ = session;
        var mid = Center(panes.Handle);
        session.Window.Click(mid.X, mid.Y);        // focus the handle
        session.PumpFrames(2);

        session.Window.PressKey(KeyCode.Right);
        session.PumpFrame();
        Assert.Equal(210, panes.Split.Size, 1);
        session.Window.PressKey(KeyCode.Right, KeyModifiers.Shift);
        session.PumpFrame();
        Assert.Equal(260, panes.Split.Size, 1);
        session.Window.PressKey(KeyCode.Home);
        session.PumpFrame();
        Assert.Equal(100, panes.Split.Size, 1);
        Assert.Equal(3, panes.Commits);
    }

    [Fact]
    public void Vertical_DragsUpAndDown()
    {
        var (session, panes) = Mount(SplitterAxis.Vertical);
        using var _ = session;
        var mid = Center(panes.Handle);
        Assert.Equal(6, panes.Handle.Layout.AbsoluteBorderBox.Height, 1);
        Assert.Equal(800, panes.Handle.Layout.AbsoluteBorderBox.Width, 1);

        session.Window.MouseDown(mid.X, mid.Y);
        session.Window.MoveMouse(mid.X, mid.Y + 50);
        session.Window.MouseUp(mid.X, mid.Y + 50);
        session.PumpFrames(2);
        Assert.Equal(250, panes.Split.Size, 1);
        Assert.Equal(250, panes.Left.Layout.AbsoluteBorderBox.Height, 1);
    }

    [Fact]
    public void ReadsAsASeparator()
    {
        var (session, _) = Mount();
        using var s = session;
        Assert.NotNull(session.Window.FindByRole(AccessibilityRole.Separator, "Resize"));
    }
}
