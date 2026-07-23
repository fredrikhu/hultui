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
}
