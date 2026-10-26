using System.Text;
using HultUI.Core.Accessibility;
using HultUI.Core.Elements;
using HultUI.Core.Layout;
using HultUI.Core.Primitives;
using Xunit;

namespace HultUI.Core.Tests.Accessibility;

/// <summary>
/// A layout refresh made in place (<see cref="AccessibilityTreeBuilder.TryRefreshInPlace"/>): it finds
/// what a build would make otherwise in the applied tree, and the changes the build's diff would report,
/// writing nothing; then, applied, the tree is the build's, field for field. Where the build would make
/// other nodes it says why and finds nothing to write. What it writes, a reader on another thread reads
/// whole: a box, a scroll view's fields.
/// </summary>
public class AccessibilityLayoutRefreshTests
{
    private readonly LayoutEngine _layout = new(new StubMeasureContext());

    // 30 rows of 20 in a 400-high view: 600 of content.
    private (BoxElement Root, ScrollViewElement View, BoxElement Column, BoxElement[] Rows) Fixture()
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
        return (root, view, column, rows);
    }

    private void Layout(BoxElement root)
    {
        _layout.PerformLayout(root, 300, 400);
        ((ScrollViewElement)root.Children[0]).UpdateContentSize();
    }

    private static readonly IReadOnlyList<(Element, float, float)> NoOverlays = [];

    /// <summary>The refresh against what a build makes now: the changes it found are the build's diff's,
    /// in its order, each with what its previous node says of it; applied, the tree is the build's.</summary>
    private static AccessibilityTreeBuilder.LayoutRefresh RefreshLikeABuild(AccessibilityNode tree, Element root)
    {
        var built = AccessibilityTreeBuilder.Build(root);
        var diff = AccessibilityTreeDiffer.Diff(tree, built);
        var expected = diff.PropertyChanged.Select(Line).ToList();
        var refresh = new AccessibilityTreeBuilder.LayoutRefresh();
        Assert.True(AccessibilityTreeBuilder.TryRefreshInPlace(tree, root, NoOverlays, null, refresh), refresh.Mismatch);
        Assert.Empty(diff.Added);
        Assert.Empty(diff.Removed);
        Assert.Equal(expected, refresh.Changes.Select(Line).ToList());
        refresh.Apply();
        Assert.Equal(Dump(built), Dump(tree));
        return refresh;
    }

    private static string Line(PropertyChange c) =>
        $"{c.PropertyName} {c.Node.ElementId.Value} prev={(c.PropertyName == AccessibilityProperties.ScrollPosition ? c.Previous?.ScrollOffset.ToString() : "")}";

    private static string Dump(AccessibilityNode node)
    {
        var sb = new StringBuilder();
        Walk(node, 0);
        return sb.ToString();

        void Walk(AccessibilityNode n, int depth)
        {
            sb.Append(' ', depth).Append(n.ElementId.Value).Append(' ').Append(n.Role).Append(' ').Append(n.Name)
                .Append(" r=").Append(n.BoundingRect).Append(" u=").Append(n.UnscrolledRect).Append(" s=").Append(n.States)
                .Append(" scroll=").Append(n.ScrollOffset).Append(" extent=").Append(n.ScrollExtent).Append('\n');
            foreach (var child in n.Children)
            {
                Assert.Same(n, child.Parent);
                Walk(child, depth + 1);
            }
        }
    }

    [Fact]
    public void AScroll_WritesTheViewsOffset_AndTheOffscreenStates_AndNoBox()
    {
        var (root, view, _, _) = Fixture();
        var tree = AccessibilityTreeBuilder.Build(root);
        var viewNode = tree.Children[0];

        view.ScrollOffsetY = 180;
        var refresh = RefreshLikeABuild(tree, root);

        var change = Assert.Single(refresh.Changes);
        Assert.Equal(AccessibilityProperties.ScrollPosition, change.PropertyName);
        Assert.Same(viewNode, change.Node);
        Assert.Equal(0, change.Previous!.ScrollOffset.Y);
        Assert.Equal(180, viewNode.ScrollOffset.Y);
        Assert.Empty(refresh.Moves);
        Assert.Empty(refresh.Remade);
        // Rows 0–8 went out of view and 20–28 came in: 18 states, and the same nodes.
        Assert.Equal(18, refresh.OffscreenFlips.Count);
        Assert.Same(viewNode, tree.Children[0]);
    }

    [Fact]
    public void ARowThatGrows_AndAScroll_MoveTheRowsBelowInTheirContent_AsABuildsDiffTells()
    {
        var (root, view, _, rows) = Fixture();
        var tree = AccessibilityTreeBuilder.Build(root);

        rows[5].Box.Height = 40;
        rows[5].MarkLayoutDirty();
        Layout(root);
        view.ScrollOffsetY = 20;
        var refresh = RefreshLikeABuild(tree, root);

        Assert.Equal(25, refresh.Moves.Count);   // row 5 resized, 6–29 moved
        Assert.Contains(refresh.Changes, c => c.PropertyName == AccessibilityProperties.BoundingRect && c.Node.Name == "row 5");
    }

    [Fact]
    public void ALeafsOwnName_MakesItsNodeAgain_InItsPlace()
    {
        var (root, _, _, rows) = Fixture();
        var tree = AccessibilityTreeBuilder.Build(root);
        var old = tree.Children[0].Children[7];

        rows[7].AccessibleName = "row seven";
        var refresh = RefreshLikeABuild(tree, root);

        var change = Assert.Single(refresh.Changes);
        Assert.Equal(AccessibilityProperties.Name, change.PropertyName);
        Assert.NotSame(old, tree.Children[0].Children[7]);
        Assert.Same(tree.Children[0].Children[7], change.Node);
        Assert.Equal("row seven", tree.Children[0].Children[7].Name);
    }

    [Fact]
    public void NothingChanged_FindsNothing()
    {
        var (root, _, _, _) = Fixture();
        var tree = AccessibilityTreeBuilder.Build(root);
        var refresh = RefreshLikeABuild(tree, root);
        Assert.Empty(refresh.Changes);
        Assert.Empty(refresh.Moves);
        Assert.Empty(refresh.OffscreenFlips);
        Assert.Empty(refresh.Scrolls);
        Assert.Empty(refresh.Remade);
    }

    [Fact]
    public void WhereABuildMakesOtherNodes_ItSaysWhy_AndFindsNothingToWrite()
    {
        string Refuse(Action<BoxElement, ScrollViewElement, BoxElement, BoxElement[]> change)
        {
            var (root, view, column, rows) = Fixture();
            var tree = AccessibilityTreeBuilder.Build(root);
            var before = Dump(tree);
            change(root, view, column, rows);
            Layout(root);
            var refresh = new AccessibilityTreeBuilder.LayoutRefresh();
            Assert.False(AccessibilityTreeBuilder.TryRefreshInPlace(tree, root, NoOverlays, null, refresh));
            refresh.Clear();
            Assert.Equal(before, Dump(tree));
            return refresh.Mismatch!;
        }

        Assert.Equal("children", Refuse((_, _, column, _) =>
        {
            var row = BoxElement.New();
            row.AccessibleName = "new";
            row.Box.Height = 20;
            column.AddChild(row);
        }));
        Assert.Equal("children", Refuse((_, _, column, rows) => column.RemoveChild(rows[3])));
        Assert.Equal("a node with children changed", Refuse((_, view, _, _) => view.AccessibleName = "the view"));
        // Whether a node is focusable no diff compares: only a new tree carries it.
        Assert.Equal("a change no diff tells", Refuse((_, _, _, rows) => rows[2].IsFocusable = true));
    }

    [Fact]
    public void ABoxMovedInPlace_IsReadWhole_OnAnotherThread()
    {
        var node = AccessibilityTreeBuilder.Build(Leaf());
        var a = new Rect(1, 2, 3, 4);
        var b = new Rect(10, 20, 30, 40);
        node.MoveTo(a);
        int torn = 0, reads = 0;
        using var stop = new ManualResetEventSlim();
        var reader = new Thread(() =>
        {
            while (!stop.IsSet)
            {
                var r = node.BoundingRect;
                if (r != a && r != b) torn++;
                reads++;
            }
        });
        reader.Start();
        for (int i = 0; i < 200_000; i++) node.MoveTo(i % 2 == 0 ? b : a);
        stop.Set();
        reader.Join();
        Assert.Equal(0, torn);
        Assert.True(reads > 0);
    }

    [Fact]
    public void AScrollViewsFieldsReplacedInPlace_AreReadAsOneSet_AndMoveTheBoxesUnderIt()
    {
        var (root, view, _, _) = Fixture();
        var tree = AccessibilityTreeBuilder.Build(root);
        var viewNode = tree.Children[0];
        var row = viewNode.Children[12];
        var vp = viewNode.ScrollExtent!.Value.Viewport;
        var (a, b) = ((new Point(0, 40), new Size(300, 600)), (new Point(0, 90), new Size(300, 900)));
        // Start from one of the two sets: a reader that runs before the first write (a busy
        // two-core runner) would otherwise count the built tree's own values as torn.
        viewNode.ReplaceScrollView(a.Item1, vp, a.Item2);
        int torn = 0, reads = 0;
        using var stop = new ManualResetEventSlim();
        var reader = new Thread(() =>
        {
            while (!stop.IsSet)
            {
                var extent = viewNode.ScrollExtent!.Value;
                float y = row.BoundingRect.Y;
                if (extent.Content != a.Item2 && extent.Content != b.Item2) torn++;
                if (y != 240 - 40 && y != 240 - 90) torn++;
                reads++;
            }
        });
        reader.Start();
        for (int i = 0; i < 100_000; i++)
        {
            var (offset, content) = i % 2 == 0 ? b : a;
            viewNode.ReplaceScrollView(offset, vp, content);
        }
        stop.Set();
        reader.Join();
        Assert.Equal(0, torn);
        Assert.True(reads > 0);
    }

    private BoxElement Leaf()
    {
        var leaf = BoxElement.New();
        leaf.AccessibleName = "leaf";
        leaf.Box.Width = 10;
        leaf.Box.Height = 10;
        _layout.PerformLayout(leaf, 10, 10);
        return leaf;
    }
}
