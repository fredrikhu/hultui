using HultUI.Core.Elements;
using HultUI.Core.Layout;
using HultUI.Core.Primitives;
using HultUI.Core.Rendering;
using Xunit;

namespace HultUI.Core.Tests.Accessibility;

/// <summary>
/// What tells the window that a layout pass moved something a screen reader is told about
/// (<see cref="Element.TakeMovedBoxes"/>, plan 17, round 3, step 15): an element whose box on
/// screen, or whose padding or border, is not what it was at the last look. Boxes are written
/// several times in a pass (measured, then placed, then shifted with their parent), so it is the
/// box at the look that counts, not the writes on the way.
/// </summary>
public class MovedBoxesTests
{
    private static LayoutResult Box(float x, float y, float w, float h, float padding = 0) =>
        new(0, 0, w, h, new Thickness(padding, padding, padding, padding), default, default) { AbsoluteX = x, AbsoluteY = y };

    /// <summary>A root with a column of two leaves, every box taken.</summary>
    private static (BoxElement Root, BoxElement Column, BoxElement A, BoxElement B) Tree()
    {
        var root = BoxElement.New();
        var column = BoxElement.New();
        var a = BoxElement.New();
        var b = BoxElement.New();
        column.AddChild(a);
        column.AddChild(b);
        root.AddChild(column);
        root.Layout = Box(0, 0, 800, 600);
        column.Layout = Box(0, 0, 800, 40);
        a.Layout = Box(0, 0, 800, 20);
        b.Layout = Box(0, 20, 800, 20);
        Assert.True(root.TakeMovedBoxes()); // new boxes: moved from nothing
        Assert.False(root.TakeMovedBoxes());
        return (root, column, a, b);
    }

    [Fact]
    public void ABoxWrittenAgainAsItWas_IsNoMove()
    {
        var (root, _, a, _) = Tree();
        a.Layout = Box(0, 0, 800, 20);
        Assert.False(root.TakeMovedBoxes());
    }

    [Fact]
    public void ABoxThatMoves_IsAMove_Once()
    {
        var (root, _, _, b) = Tree();
        b.Layout = Box(0, 24, 800, 20);
        Assert.True(root.TakeMovedBoxes());
        Assert.False(root.TakeMovedBoxes());
    }

    [Fact]
    public void ABoxWrittenElsewhereAndBackBeforeTheLook_IsNoMove()
    {
        // As a layout pass does: measured at the size offered, then placed at the size it gets.
        var (root, _, a, _) = Tree();
        a.Layout = Box(0, 0, 400, 37) with { X = 0 };
        a.Layout = Box(0, 0, 800, 20);
        Assert.False(root.TakeMovedBoxes());
    }

    [Fact]
    public void APaddingChange_IsAMove_EvenWithTheSameBox()
    {
        // A list's items and a scroll view's clip are placed in its content box. The layout pass
        // reads padding and border from the style in one place, and says when they change there.
        var leaf = BoxElement.New();
        leaf.Box.Width = 200;
        leaf.Box.Height = 40;
        var root = BoxElement.New();
        root.AddChild(leaf);
        var engine = new LayoutEngine(new StubMeasureContext());
        engine.PerformLayout(root, 400, 300);
        Assert.True(root.TakeMovedBoxes());
        engine.PerformLayout(root, 400, 300);
        Assert.False(root.TakeMovedBoxes());

        leaf.Box.Padding = new Thickness(4);
        leaf.MarkLayoutDirty();
        engine.PerformLayout(root, 400, 300);

        Assert.Equal(200, leaf.Layout.Width); // the same box
        Assert.True(root.TakeMovedBoxes());
        Assert.False(root.TakeMovedBoxes());
    }

    [Fact]
    public void AMovedElementThatJoinsAnotherParent_IsStillFound()
    {
        var (root, column, _, _) = Tree();
        var detached = BoxElement.New();
        detached.Layout = Box(5, 5, 10, 10); // marked, with no parent to tell
        Assert.False(root.TakeMovedBoxes());

        column.AddChild(detached);
        Assert.True(root.TakeMovedBoxes());

        // And taken: moving it back is a move again, from where the look saw it.
        column.RemoveChild(detached);
        detached.Layout = Box(0, 0, 0, 0);
        column.InsertChild(0, detached);
        Assert.True(root.TakeMovedBoxes());
    }

    [Fact]
    public void AnElementUnderAnUnmovedParent_IsFoundThroughIt()
    {
        var (root, column, _, b) = Tree();
        b.Layout = Box(0, 21, 800, 20);
        column.Layout = Box(0, 0, 800, 40); // the parent written again as it was
        Assert.True(root.TakeMovedBoxes());
    }
}
