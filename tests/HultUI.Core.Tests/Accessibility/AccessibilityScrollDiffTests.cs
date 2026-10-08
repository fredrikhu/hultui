using HultUI.Core.Accessibility;
using HultUI.Core.Elements;
using HultUI.Core.Layout;
using Xunit;

namespace HultUI.Core.Tests.Accessibility;

/// <summary>
/// What a scroll changes in the accessibility tree's diff. Node bounds are on screen, so a scroll
/// moves every node under the scroll view; the diff reported each as a bounds change (a UIA
/// BoundingRectangle event per node on screen, 133 per rebuild over 1,000 rows). As browsers do,
/// it now reports the scroll view's scroll, and bounds only for nodes that moved within it.
/// </summary>
public class AccessibilityScrollDiffTests
{
    private readonly LayoutEngine _layout = new(new StubMeasureContext());

    // 30 rows of 20 in a 400-high view: 600 of content.
    private (BoxElement Root, ScrollViewElement View, BoxElement[] Rows) Fixture()
    {
        var root = BoxElement.New();
        root.Role = AccessibilityRole.Application;
        root.Box.Width = 300;
        root.Box.Height = 400;
        var view = ScrollViewElement.New();
        view.AccessibleName = "view";
        view.Box.Width = 300;
        view.Box.Height = 400;
        var column = BoxElement.New();
        var rows = new BoxElement[30];
        for (int i = 0; i < rows.Length; i++)
        {
            rows[i] = BoxElement.New();
            rows[i].AccessibleName = $"row {i}";
            rows[i].Box.Width = 300;
            rows[i].Box.Height = 20;
            column.AddChild(rows[i]);
        }
        view.AddChild(column);
        root.AddChild(view);
        Layout(root);
        return (root, view, rows);
    }

    private void Layout(BoxElement root)
    {
        _layout.PerformLayout(root, 300, 400);
        ((ScrollViewElement)root.Children[0]).UpdateContentSize();
    }

    private static List<string> Changes(TreeDiff diff) =>
        diff.PropertyChanged.Select(c => $"{c.PropertyName} {c.Node.Name}").ToList();

    [Fact]
    public void AScroll_IsTheScrollViewsChange_NotEachNodesBounds()
    {
        var (root, view, _) = Fixture();
        var before = AccessibilityTreeBuilder.Build(root);

        view.ScrollOffsetY = 20;
        var after = AccessibilityTreeBuilder.Build(root);
        var diff = AccessibilityTreeDiffer.Diff(before, after);

        Assert.Equal(["ScrollPosition view"], Changes(diff));
        var change = Assert.Single(diff.PropertyChanged);
        Assert.Equal(0, change.Previous!.ScrollOffset.Y);
        Assert.Equal(20, change.Node.ScrollOffset.Y);
        // The bounds are still on screen: a reader asking gets where the row is now.
        Assert.Equal(Row(before, 5).BoundingRect.Y - 20, Row(after, 5).BoundingRect.Y, 3);
        // Row 20 scrolled into view and row 0 began to leave it; neither is told, as in browsers.
        Assert.True(Row(before, 20).IsOffscreen);
        Assert.False(Row(after, 20).IsOffscreen);
    }

    [Fact]
    public void ANodeThatMovesWithinItsScrollView_StillHasNewBounds()
    {
        var (root, view, rows) = Fixture();
        var before = AccessibilityTreeBuilder.Build(root);

        // In one rebuild: row 5 grows by 20, which moves rows 6 on down within the content, and
        // the view scrolls by 20.
        rows[5].Box.Height = 40;
        rows[5].MarkLayoutDirty();
        Layout(root);
        view.ScrollOffsetY = 20;
        var after = AccessibilityTreeBuilder.Build(root);
        var changes = Changes(AccessibilityTreeDiffer.Diff(before, after));

        Assert.Contains("ScrollPosition view", changes);
        Assert.Contains("BoundingRect row 5", changes);     // resized
        Assert.Contains("BoundingRect row 6", changes);     // moved within the content
        Assert.Contains("BoundingRect row 19", changes);
        for (int i = 0; i < 5; i++)
            Assert.DoesNotContain($"BoundingRect row {i}", changes);   // only scrolled
        // Off screen before and after: not told, as before.
        Assert.DoesNotContain("BoundingRect row 25", changes);
    }

    [Fact]
    public void WithNoScroll_BoundsChangesAreAsBefore()
    {
        var (root, _, rows) = Fixture();
        var before = AccessibilityTreeBuilder.Build(root);

        rows[2].Box.Height = 30;
        rows[2].MarkLayoutDirty();
        Layout(root);
        var changes = Changes(AccessibilityTreeDiffer.Diff(before, AccessibilityTreeBuilder.Build(root)));

        Assert.DoesNotContain("ScrollPosition view", changes);
        Assert.Contains("BoundingRect row 2", changes);
        Assert.Contains("BoundingRect row 3", changes);
        Assert.DoesNotContain("BoundingRect row 1", changes);
    }

    private static AccessibilityNode Row(AccessibilityNode tree, int i) => Find(tree, $"row {i}")!;

    private static AccessibilityNode? Find(AccessibilityNode node, string name)
    {
        if (node.Name == name) return node;
        foreach (var child in node.Children)
            if (Find(child, name) is { } found) return found;
        return null;
    }
}
