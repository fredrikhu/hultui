using HultUI.Core.Accessibility;
using HultUI.Core.Elements;
using HultUI.Core.Layout;
using Xunit;

namespace HultUI.Core.Tests.Accessibility;

/// <summary>
/// Coverage for <see cref="TreeDiff.CoalescedAddedParents"/> /
/// <see cref="TreeDiff.CoalescedRemovedParents"/> — the topmost-parent event
/// coalescing the Windows UIA bridge raises structure-changed events from.
/// A bulk insert of N subtrees must produce ONE notification target (the
/// pre-existing container), not one per added node: structure events are
/// re-query hints on a parent, so interior parents of a freshly added subtree
/// carry no information. (Pre-coalescing, a 10k-row create raised ~70k
/// cross-process UIA events and profiled as half the operation.)
/// </summary>
public class TreeDiffCoalescingTests
{
    private readonly LayoutEngine _layout = new(new StubMeasureContext());

    private static BoxElement AppRoot()
    {
        var root = BoxElement.New();
        root.Role = AccessibilityRole.Application;
        root.Box.Width = 400;
        root.Box.Height = 400;
        return root;
    }

    // A "row": a grouped box containing two labels, so added subtrees have
    // interior parents. The explicit role matters — a role-less box is pruned
    // from the accessibility tree and its labels would promote to the app root.
    private static BoxElement Row(string label)
    {
        var row = BoxElement.New();
        row.Role = AccessibilityRole.Group;
        row.AddChild(TextElement.New(label));
        row.AddChild(TextElement.New(label + " detail"));
        return row;
    }

    private AccessibilityNode Build(Element root)
    {
        _layout.PerformLayout(root, 400, 400);
        return AccessibilityTreeBuilder.Build(root);
    }

    [Fact]
    public void BulkInsert_CoalescesToTheOneSurvivingParent()
    {
        var root = AppRoot();
        var before = Build(root);

        for (int i = 0; i < 20; i++)
            root.AddChild(Row($"row {i}"));
        var after = Build(root);

        var diff = AccessibilityTreeDiffer.Diff(before, after);
        Assert.Equal(60, diff.Added.Count); // 20 rows × (row + 2 labels)

        var parents = diff.CoalescedAddedParents();

        // One event target: the pre-existing app root. The 40 label adds under
        // freshly added rows are interior and must not surface.
        var only = Assert.Single(parents);
        Assert.Equal(after.ElementId, only);
    }

    [Fact]
    public void BulkRemove_CoalescesToTheOneSurvivingParent()
    {
        var root = AppRoot();
        for (int i = 0; i < 20; i++)
            root.AddChild(Row($"row {i}"));
        var before = Build(root);

        while (root.Children.Count > 0)
            root.RemoveChild(root.Children[^1]);
        var after = Build(root);

        var diff = AccessibilityTreeDiffer.Diff(before, after);
        Assert.Equal(60, diff.Removed.Count);

        var parents = diff.CoalescedRemovedParents();

        var only = Assert.Single(parents);
        Assert.Equal(after.ElementId, only);
    }

    [Fact]
    public void FirstBuild_CoalescesToTheRootAlone()
    {
        var root = AppRoot();
        root.AddChild(Row("row"));
        var tree = Build(root);

        var diff = AccessibilityTreeDiffer.Diff(null, tree);

        // Everything is new, so the only boundary is the root itself (null parent).
        var only = Assert.Single(diff.CoalescedAddedParents());
        Assert.Null(only);
    }

    [Fact]
    public void AddsUnderDistinctSurvivingParents_YieldOneEntryEach()
    {
        var root = AppRoot();
        var left = Row("left");
        var right = Row("right");
        root.AddChild(left);
        root.AddChild(right);
        var before = Build(root);

        left.AddChild(TextElement.New("new in left"));
        right.AddChild(TextElement.New("new in right"));
        var after = Build(root);

        var diff = AccessibilityTreeDiffer.Diff(before, after);
        Assert.Equal(2, diff.Added.Count);

        var parents = diff.CoalescedAddedParents();

        Assert.Equal(2, parents.Count);
        Assert.All(parents, p => Assert.NotNull(p));
        Assert.NotEqual(parents[0], parents[1]);
    }

    [Fact]
    public void EmptyDiff_YieldsNoParents()
    {
        var diff = new TreeDiff();
        Assert.Empty(diff.CoalescedAddedParents());
        Assert.Empty(diff.CoalescedRemovedParents());
    }

    // ── BoundaryAdded / BoundaryRemoved: the AT-SPI variant. Unlike the parent
    //    coalescing above, these keep the boundary *entry* (child + parent), so
    //    the Linux bridge can emit one children-changed naming the specific child
    //    that a pre-existing parent gained/lost, and drop the interior storm.

    [Fact]
    public void BoundaryAdded_KeepsOnlyTheContainer_NotItsInteriorChildren()
    {
        var root = AppRoot();
        var before = Build(root);

        for (int i = 0; i < 20; i++)
            root.AddChild(Row($"row {i}"));
        var after = Build(root);

        var diff = AccessibilityTreeDiffer.Diff(before, after);
        Assert.Equal(60, diff.Added.Count); // 20 rows × (row + 2 labels)

        // Each row is a boundary child of the surviving app root; its two labels
        // are interior (their parent, the row, is itself added) and drop out.
        var boundary = diff.BoundaryAdded();
        Assert.Equal(20, boundary.Count);
        Assert.All(boundary, e => Assert.Equal(after.ElementId, e.Parent!.ElementId));
    }

    [Fact]
    public void BoundaryRemoved_KeepsOnlyTheContainer_NotItsInteriorChildren()
    {
        var root = AppRoot();
        for (int i = 0; i < 20; i++)
            root.AddChild(Row($"row {i}"));
        var before = Build(root);

        while (root.Children.Count > 0)
            root.RemoveChild(root.Children[^1]);
        var after = Build(root);

        var diff = AccessibilityTreeDiffer.Diff(before, after);
        Assert.Equal(60, diff.Removed.Count);

        var boundary = diff.BoundaryRemoved();
        Assert.Equal(20, boundary.Count);
        Assert.All(boundary, e => Assert.Equal(before.ElementId, e.Parent!.ElementId));
    }

    [Fact]
    public void BoundaryAdded_OnFirstBuild_KeepsTheRootEntryWithNoParent()
    {
        var root = AppRoot();
        root.AddChild(Row("row"));
        var tree = Build(root);

        var diff = AccessibilityTreeDiffer.Diff(null, tree);

        // The root is the only boundary (null parent); everything else is interior.
        var only = Assert.Single(diff.BoundaryAdded());
        Assert.Null(only.Parent);
        Assert.Equal(tree.ElementId, only.Node.ElementId);
    }

    [Fact]
    public void BoundaryAdded_UnderDistinctSurvivingParents_KeepsBoth()
    {
        var root = AppRoot();
        var left = Row("left");
        var right = Row("right");
        root.AddChild(left);
        root.AddChild(right);
        var before = Build(root);

        left.AddChild(TextElement.New("new in left"));
        right.AddChild(TextElement.New("new in right"));
        var after = Build(root);

        var diff = AccessibilityTreeDiffer.Diff(before, after);
        var boundary = diff.BoundaryAdded();

        Assert.Equal(2, boundary.Count);
        Assert.All(boundary, e => Assert.NotNull(e.Parent));
        Assert.NotEqual(boundary[0].Parent!.ElementId, boundary[1].Parent!.ElementId);
    }

    [Fact]
    public void BoundaryHelpers_EmptyDiff_YieldNothing()
    {
        var diff = new TreeDiff();
        Assert.Empty(diff.BoundaryAdded());
        Assert.Empty(diff.BoundaryRemoved());
    }

    /// <summary>
    /// The searches build no set of the diff's ids each time: a diff that brought 633 rows and took
    /// 633 (a key of Junction's commit file walk with a reader) built four sets of them, 30.6 KB a key
    /// with UI Automation (plan 17 round 12 step 3). Each search makes its result alone, the same
    /// result, on every thread.
    /// </summary>
    [Fact]
    public void TheSearches_BuildNoSetOfTheIds_WhenTheThreadHasSearchedBefore()
    {
        var root = AppRoot();
        var before = Build(root);
        // A list of rows, as a list of item nodes comes: one container, 633 nodes under it.
        var list = BoxElement.New();
        list.Role = AccessibilityRole.List;
        for (int i = 0; i < 211; i++)
            list.AddChild(Row($"row {i}"));
        root.AddChild(list);
        var after = Build(root);
        var added = AccessibilityTreeDiffer.Diff(before, after);
        var removed = AccessibilityTreeDiffer.Diff(after, before);
        Assert.Equal(634, added.Added.Count);
        Assert.Equal(634, removed.Removed.Count);

        // Once to warm the thread's sets, then measured.
        Search();
        long start = GC.GetAllocatedBytesForCurrentThread();
        var (parents, gone, boundary, boundaryGone) = Search();
        long bytes = GC.GetAllocatedBytesForCurrentThread() - start;

        Assert.True(bytes < 1024, $"{bytes} B for four searches over 634 ids");
        Assert.Equal(after.ElementId, Assert.Single(parents));
        Assert.Equal(after.ElementId, Assert.Single(gone));
        Assert.Equal(list.Id, Assert.Single(boundary).Node.ElementId);
        Assert.Equal(list.Id, Assert.Single(boundaryGone).Node.ElementId);
        // On another thread: the same answers, from sets of its own.
        var other = Task.Run(Search).Result;
        Assert.Equal(parents, other.Parents);
        Assert.Equal(boundary.Select(b => b.Node.ElementId), other.Boundary.Select(b => b.Node.ElementId));

        (List<ElementId?> Parents, List<ElementId?> Gone, List<AddedNode> Boundary, List<RemovedNode> BoundaryGone) Search() =>
            (added.CoalescedAddedParents(), removed.CoalescedRemovedParents(), added.BoundaryAdded(), removed.BoundaryRemoved());
    }
}
