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
    /// in its order, each with what its previous node says of it — the nodes added under their parents,
    /// those removed with their place among their parent's children as they were, the parents
    /// reordered, the properties changed; applied, the tree is the build's, and each node added is the
    /// tree's.</summary>
    private static AccessibilityTreeBuilder.LayoutRefresh RefreshLikeABuild(AccessibilityNode tree, Element root)
    {
        var built = AccessibilityTreeBuilder.Build(root);
        var diff = AccessibilityTreeDiffer.Diff(tree, built);
        var expected = Lines(diff.Added, diff.Removed, diff.Reordered, diff.PropertyChanged);
        var refresh = new AccessibilityTreeBuilder.LayoutRefresh();
        Assert.True(AccessibilityTreeBuilder.TryRefreshInPlace(tree, root, NoOverlays, null, refresh), refresh.Mismatch);
        Assert.Equal(expected, Lines(refresh.Added, refresh.Removed, refresh.Reordered, refresh.Changes));
        Assert.Equal(diff.Added.Count + diff.Removed.Count + diff.Reordered.Count > 0, refresh.Structural);
        refresh.Apply();
        Assert.Equal(Dump(built), Dump(tree));
        foreach (var added in refresh.Added)
            Assert.Same(added.Node, Find(tree, added.Node.ElementId));
        Assert.Empty(refresh.Pending);
        Assert.Empty(refresh.Unfound);
        return refresh;
    }

    private static List<string> Lines(List<AddedNode> added, List<RemovedNode> removed, List<AccessibilityNode> reordered,
        List<PropertyChange> changes) =>
    [
        .. added.Select(a => $"+ {a.Node.ElementId.Value} {a.Node.Name} under {a.Parent?.ElementId.Value}"),
        .. removed.Select(r => $"- {r.Node.ElementId.Value} {r.Node.Name} under {r.Parent?.ElementId.Value} at {IndexIn(r.Parent, r.Node)}"),
        .. reordered.Select(r => $"r {r.ElementId.Value}"),
        .. changes.Select(Line),
    ];

    private static int IndexIn(AccessibilityNode? parent, AccessibilityNode node)
    {
        if (parent is null) return -1;
        for (int i = 0; i < parent.Children.Count; i++)
            if (parent.Children[i].ElementId == node.ElementId) return i;
        return -1;
    }

    private static AccessibilityNode? Find(AccessibilityNode node, ElementId id)
    {
        if (node.ElementId == id) return node;
        foreach (var child in node.Children)
            if (Find(child, id) is { } found) return found;
        return null;
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

        Assert.Equal("a node with children changed", Refuse((_, view, _, _) => view.AccessibleName = "the view"));
        // Whether a node is focusable no diff compares: only a new tree carries it.
        Assert.Equal("a change no diff tells", Refuse((_, _, _, rows) => rows[2].IsFocusable = true));
    }

    private static BoxElement Row(string name, float height = 20)
    {
        var row = BoxElement.New();
        row.AccessibleName = name;
        row.Box.Width = 300;
        row.Box.Height = height;
        return row;
    }

    [Fact]
    public void RowsAddedAndGone_AreBuiltAndRemoved_AndTheRestKept()
    {
        var (root, _, column, rows) = Fixture();
        var tree = AccessibilityTreeBuilder.Build(root);
        var viewNode = tree.Children[0];
        var kept = viewNode.Children.ToArray();
        var oldChildren = viewNode.Children;

        column.InsertChild(3, Row("new at 3"));
        column.RemoveChild(rows[10]);
        column.AddChild(Row("new at the end"));
        Layout(root);
        var refresh = RefreshLikeABuild(tree, root);

        Assert.Equal(["new at 3", "new at the end"], refresh.Added.Select(a => a.Node.Name));
        Assert.All(refresh.Added, a => Assert.Same(viewNode, a.Parent));
        var gone = Assert.Single(refresh.Removed);
        Assert.Same(kept[10], gone.Node);
        // The parent as it was, with the children it had: the old array, never written.
        Assert.NotSame(viewNode, gone.Parent);
        Assert.Same(kept[10], gone.Parent!.Children[10]);
        Assert.Same(viewNode.Parent, gone.Parent.Parent);
        Assert.Empty(refresh.Reordered);
        // The rows that stay are the same nodes; rows 4–9 moved down a row in their content.
        Assert.Same(viewNode, tree.Children[0]);
        Assert.Equal(31, viewNode.Children.Count);
        Assert.Same(kept[0], viewNode.Children[0]);
        Assert.Same(kept[11], viewNode.Children[11]);
        Assert.Equal(30, oldChildren.Count);
        Assert.Contains(refresh.Changes, c => c.PropertyName == AccessibilityProperties.BoundingRect && c.Node == kept[4]);
    }

    [Fact]
    public void RowsInAnotherOrder_AreReordered_AndKept()
    {
        var (root, _, column, rows) = Fixture();
        var tree = AccessibilityTreeBuilder.Build(root);
        var viewNode = tree.Children[0];
        var kept = viewNode.Children.ToArray();

        column.RemoveChild(rows[2]);
        column.InsertChild(6, rows[2]);
        Layout(root);
        var refresh = RefreshLikeABuild(tree, root);

        Assert.Empty(refresh.Added);
        Assert.Empty(refresh.Removed);
        Assert.Same(viewNode, Assert.Single(refresh.Reordered));
        Assert.Same(kept[2], viewNode.Children[6]);
        Assert.Same(kept[3], viewNode.Children[2]);
    }

    [Fact]
    public void ARowMadeAgain_IsRemovedAndAddedInItsPlace_AndALeafAfterIt_RemadeAtItsNewPlace()
    {
        var (root, _, column, rows) = Fixture();
        var tree = AccessibilityTreeBuilder.Build(root);
        var viewNode = tree.Children[0];

        column.RemoveChild(rows[4]);
        column.InsertChild(4, Row("row 4"));
        column.InsertChild(1, Row("new at 1"));
        rows[8].AccessibleName = "row eight";
        Layout(root);
        var refresh = RefreshLikeABuild(tree, root);

        Assert.Equal(["new at 1", "row 4"], refresh.Added.Select(a => a.Node.Name));
        Assert.Equal("row 4", Assert.Single(refresh.Removed).Node.Name);
        // Row 8 is at 9 among the children as a build makes them, and its node made again is there.
        var (_, index, made) = Assert.Single(refresh.Remade);
        Assert.Equal(9, index);
        Assert.Same(made, viewNode.Children[9]);
        Assert.Equal("row eight", viewNode.Children[9].Name);
    }

    [Fact]
    public void ABoxThatGainsAName_HasANodeWithItsChildren_AndTheirOldNodesGo()
    {
        var (root, _, column, rows) = Fixture();
        var tree = AccessibilityTreeBuilder.Build(root);

        column.AccessibleName = "the rows";
        var refresh = RefreshLikeABuild(tree, root);

        // The box's node and its 30 rows' are new; the rows' old nodes, under the view's, are gone.
        Assert.Equal(31, refresh.Added.Count);
        Assert.Equal(30, refresh.Removed.Count);
        Assert.Equal("the rows", tree.Children[0].Children.Single().Name);
        Assert.Equal(rows.Length, tree.Children[0].Children[0].Children.Count);
    }

    [Fact]
    public void TheLastRowsGone_AreRemoved_InTheirOrder()
    {
        var (root, _, column, rows) = Fixture();
        var tree = AccessibilityTreeBuilder.Build(root);

        for (int i = 25; i < rows.Length; i++) column.RemoveChild(rows[i]);
        Layout(root);
        var refresh = RefreshLikeABuild(tree, root);

        Assert.Equal(Enumerable.Range(25, 5).Select(i => $"row {i}"), refresh.Removed.Select(r => r.Node.Name));
        Assert.Empty(refresh.Added);
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
