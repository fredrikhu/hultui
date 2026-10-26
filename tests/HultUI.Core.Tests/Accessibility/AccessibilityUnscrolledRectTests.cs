using HultUI.Core.Accessibility;
using HultUI.Core.Elements;
using HultUI.Core.Layout;
using HultUI.Core.Primitives;
using Xunit;

namespace HultUI.Core.Tests.Accessibility;

/// <summary>
/// A node keeps its box where it sits in the content of the scroll views above it
/// (<see cref="AccessibilityNode.UnscrolledRect"/>), and its box on screen
/// (<see cref="AccessibilityNode.BoundingRect"/>) is that less the offsets of the scroll views
/// above it, read up its parents. So a scroll is one offset changed on one node: every box under
/// it reads the move from there, and nothing under it is made again. The values are the ones the
/// builder put in each node when it subtracted the scroll on its way down.
/// </summary>
public class AccessibilityUnscrolledRectTests
{
    private readonly LayoutEngine _layout = new(new StubMeasureContext());

    // A root holding a 400-high view of 30 rows, the 7th of which holds a 60-high inner view of
    // 6 rows; fractional offsets in both, as a wheel with a fractional scale leaves them.
    private (BoxElement Root, ScrollViewElement Outer, ScrollViewElement Inner) Fixture()
    {
        var root = BoxElement.New();
        root.Role = AccessibilityRole.Application;
        root.Box.Width = 300;
        root.Box.Height = 400;
        var outer = ScrollViewElement.New();
        outer.AccessibleName = "outer";
        outer.Box.Width = 300;
        outer.Box.Height = 400;
        var column = BoxElement.New();
        ScrollViewElement? inner = null;
        for (int i = 0; i < 30; i++)
        {
            if (i == 6)
            {
                inner = ScrollViewElement.New();
                inner.AccessibleName = "inner";
                inner.Box.Width = 280;
                inner.Box.Height = 60;
                var innerColumn = BoxElement.New();
                for (int k = 0; k < 6; k++) innerColumn.AddChild(Row($"inner {k}", 280, 20.25f));
                inner.AddChild(innerColumn);
                column.AddChild(inner);
                continue;
            }
            column.AddChild(Row($"row {i}", 300, 20.25f));
        }
        outer.AddChild(column);
        root.AddChild(outer);
        _layout.PerformLayout(root, 300, 400);
        outer.UpdateContentSize();
        inner!.UpdateContentSize();
        outer.ScrollOffsetY = 33.7f;
        inner.ScrollOffsetY = 17.3f;
        return (root, outer, inner);
    }

    private static BoxElement Row(string name, float width, float height)
    {
        var row = BoxElement.New();
        row.AccessibleName = name;
        row.Box.Width = width;
        row.Box.Height = height;
        return row;
    }

    [Fact]
    public void ABoxOnScreen_IsTheElementsLessTheScrollAbove_AsTheBuilderSubtractedIt()
    {
        var (root, outer, inner) = Fixture();
        var tree = AccessibilityTreeBuilder.Build(root);

        // The builder's own sum, from the root down: (0 + outer) for the outer view's content, then
        // + inner for the inner view's.
        float outerScroll = 0f + outer.ScrollOffsetY;
        float innerScroll = outerScroll + inner.ScrollOffsetY;
        foreach (var element in Elements(root))
        {
            if (element.AccessibleName is not { } name) continue;
            var node = Find(tree, name)!;
            var box = element.Layout.AbsoluteBorderBox;
            float scroll = name.StartsWith("inner ") ? innerScroll
                : name.StartsWith("row ") || name == "inner" ? outerScroll : 0f;
            Assert.Equal(new Rect(box.X, box.Y - scroll, box.Width, box.Height), node.BoundingRect);
            Assert.Equal(box, node.UnscrolledRect);
        }
        // The inner view's viewport: on screen, the outer view's scroll applied, not its own.
        var viewport = inner.Layout.AbsoluteContentBox;
        Assert.Equal(new Rect(viewport.X, viewport.Y - outerScroll, viewport.Width, viewport.Height),
            Find(tree, "inner")!.ScrollExtent!.Value.Viewport);
    }

    [Fact]
    public void AnOffsetChangedOnTheScrollViewsNode_MovesEveryBoxUnderItOnScreen_AndNoneInItsContent()
    {
        var (root, _, _) = Fixture();
        var tree = AccessibilityTreeBuilder.Build(root);
        var outerNode = Find(tree, "outer")!;
        var row = Find(tree, "row 12")!;
        var innerRow = Find(tree, "inner 3")!;
        var (rowOnScreen, innerOnScreen) = (row.BoundingRect, innerRow.BoundingRect);
        var innerViewport = Find(tree, "inner")!.ScrollExtent!.Value.Viewport;
        var (rowInContent, innerInContent) = (row.UnscrolledRect, innerRow.UnscrolledRect);

        outerNode.ScrollOffset = new Point(0, outerNode.ScrollOffset.Y + 40);

        Assert.Equal(rowOnScreen.Y - 40, row.BoundingRect.Y, 3);
        Assert.Equal(innerOnScreen.Y - 40, innerRow.BoundingRect.Y, 3);
        Assert.Equal(innerViewport.Y - 40, Find(tree, "inner")!.ScrollExtent!.Value.Viewport.Y, 3);
        Assert.Equal(rowInContent, row.UnscrolledRect);
        Assert.Equal(innerInContent, innerRow.UnscrolledRect);
        // The scroll view's own box is in the content of the views above it, which did not scroll.
        Assert.Equal(outerNode.UnscrolledRect, outerNode.BoundingRect);
    }

    [Fact]
    public void AnOverlaysPart_UnderARootThatScrolls_StaysWhereTheOverlayIs()
    {
        var root = ScrollViewElement.New();
        root.Box.Width = 300;
        root.Box.Height = 400;
        var content = BoxElement.New();
        content.Box.Width = 300;
        content.Box.Height = 2000;
        root.AddChild(content);
        _layout.PerformLayout(root, 300, 400);
        root.UpdateContentSize();
        root.ScrollOffsetY = 250;
        var menu = BoxElement.New();
        menu.Role = AccessibilityRole.Group;
        menu.Box.Width = 120;
        menu.Box.Height = 80;
        var item = Row("item", 120, 20);
        menu.AddChild(item);
        _layout.PerformLayout(menu, 120, 80);

        var tree = AccessibilityTreeBuilder.Build(root);
        tree.AddChild(AccessibilityTreeBuilder.Build(menu, null, 40, 60, tree.ScrollOffset));

        var part = tree.Children[^1];
        Assert.Equal(new Rect(40, 60, 120, 80), part.BoundingRect);
        Assert.Equal(new Rect(40, 60, 120, 20), Find(part, "item")!.BoundingRect);
    }

    private static IEnumerable<Element> Elements(Element element)
    {
        yield return element;
        foreach (var child in element.Children)
            foreach (var e in Elements(child))
                yield return e;
    }

    private static AccessibilityNode? Find(AccessibilityNode node, string name)
    {
        if (node.Name == name) return node;
        foreach (var child in node.Children)
            if (Find(child, name) is { } found) return found;
        return null;
    }
}
