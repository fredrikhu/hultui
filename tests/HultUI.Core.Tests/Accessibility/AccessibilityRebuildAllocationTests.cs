using HultUI.Core.Accessibility;
using HultUI.Core.Elements;
using HultUI.Core.Events;
using HultUI.Core.Layout;
using HultUI.Core.Primitives;
using Xunit;

namespace HultUI.Core.Tests.Accessibility;

/// <summary>
/// While an assistive technology listens, every change it can see rebuilds the whole
/// accessibility tree and diffs it against the last one. What that leaves behind besides the new
/// tree is garbage on every rebuild (plan 17, a11y-build-diff-garbage): 1,000 rows of three
/// labels built 230 bytes a node and diffed 40, where the nodes themselves are 136.
/// </summary>
public class AccessibilityRebuildAllocationTests
{
    private readonly LayoutEngine _layout = new(new StubMeasureContext());

    /// <summary>Keeps a measured allocation on the heap, where the build's are.</summary>
    private static object? s_kept;

    /// <summary>
    /// An app's kind of tree: a named root; rows of labels inside decorative boxes, which the
    /// tree flattens; buttons named by their text, which keep no children; a named group with a
    /// checkbox and a nested group; and a virtual list small enough to list every item.
    /// </summary>
    private Element Scene()
    {
        var root = BoxElement.New();
        root.Role = AccessibilityRole.Application;
        root.Box.Width = 600;
        root.Box.Height = 900;

        for (int i = 0; i < 40; i++)
        {
            var row = BoxElement.New();
            row.AddChild(TextElement.New($"{i}"));
            var cell = BoxElement.New();
            cell.AddChild(TextElement.New($"row {i}"));
            row.AddChild(cell);
            var button = BoxElement.New();
            button.On<ClickEvent>(_ => { });
            button.AddChild(TextElement.New("Open"));
            row.AddChild(button);
            root.AddChild(row);
        }

        var group = BoxElement.New();
        group.AccessibleName = "Options";
        group.AddChild(CheckboxElement.New(true, "Wrap lines"));
        var inner = BoxElement.New();
        inner.Role = AccessibilityRole.Group;
        inner.AddChild(TextElement.New("Inner"));
        inner.AddChild(TextElement.New("Text"));
        group.AddChild(inner);
        root.AddChild(group);

        var list = VirtualListElement<string>.New();
        list.Box.Width = 200;
        list.Box.Height = 100;
        list.ItemHeight = 20;
        var items = new string[30];
        for (int i = 0; i < items.Length; i++) items[i] = $"item {i}";
        list.Items = items;
        list.ItemTemplate = s => TextElement.New(s);
        root.AddChild(list);

        _layout.PerformLayout(root, 600, 900);
        return root;
    }

    private static long Bytes(Action action)
    {
        long before = GC.GetAllocatedBytesForCurrentThread();
        action();
        return GC.GetAllocatedBytesForCurrentThread() - before;
    }

    [Fact]
    public void DiffingAnUnchangedTree_AllocatesNothingButTheDiff()
    {
        var root = Scene();
        var a = AccessibilityTreeBuilder.Build(root);
        var b = AccessibilityTreeBuilder.Build(root);
        Assert.True(AccessibilityTreeDiffer.Diff(a, b).IsEmpty);

        long emptyDiff = Bytes(() => s_kept = AccessibilityTreeDiffer.Diff(null, null));
        long unchanged = Bytes(() => s_kept = AccessibilityTreeDiffer.Diff(a, b));

        // The TreeDiff it returns, and not a byte per node: no index of the children, no
        // enumerator. Before, this tree's diff allocated about 10 KB.
        Assert.Equal(emptyDiff, unchanged);
    }

    [Fact]
    public void BuildingATree_AllocatesItsNodesAndOneArrayOfTheExactLengthPerParent()
    {
        var root = Scene();
        AccessibilityTreeBuilder.Build(root);   // the thread's first build sets up what it keeps

        long built = 0;
        AccessibilityNode tree = null!;
        built = Bytes(() => tree = AccessibilityTreeBuilder.Build(root));

        long node = Bytes(() => s_kept = new AccessibilityNode(default, AccessibilityRole.None, null, null, default));
        long expected = 0;
        int nodes = 0, parents = 0;
        Count(tree);
        void Count(AccessibilityNode n)
        {
            nodes++;
            expected += node;
            if (n.Children.Count > 0)
            {
                parents++;
                int length = n.Children.Count;
                expected += Bytes(() => s_kept = new AccessibilityNode[length]);
            }
            foreach (var child in n.Children) Count(child);
        }

        Assert.True(nodes > 150 && parents >= 4, $"{nodes} nodes, {parents} parents");
        // Nothing else: no list per node, no enumerator per child list, no list grown in steps,
        // no string to name a button whose one text names it.
        Assert.Equal(expected, built);
    }

    [Fact]
    public void ListItems_HaveTheIdsOfTheirListAndIndex()
    {
        var root = Scene();
        var list = (VirtualListElement<string>)root.Children[^1];
        var tree = AccessibilityTreeBuilder.Build(root);

        var items = tree.Children[^1].Children;
        Assert.Equal(30, items.Count);
        for (int i = 0; i < items.Count; i++)
        {
            Assert.Equal(VirtualItemId.For(list.Id, i), items[i].ElementId);
            Assert.Same(tree.Children[^1], items[i].Parent);
        }
    }

    [Fact]
    public void AButtonsName_JoinsItsTexts_WithSpaces()
    {
        var button = BoxElement.New();
        button.On<ClickEvent>(_ => { });
        var wrapper = BoxElement.New();
        wrapper.AddChild(TextElement.New("Save"));
        wrapper.AddChild(TextElement.New(""));
        button.AddChild(wrapper);
        button.AddChild(TextElement.New("as"));
        button.AddChild(TextElement.New("PDF"));
        Assert.Equal("Save as PDF", AccessibilityTreeBuilder.ResolveName(button));

        var single = BoxElement.New();
        single.On<ClickEvent>(_ => { });
        single.AddChild(TextElement.New(""));
        var text = TextElement.New("Open");
        single.AddChild(text);
        Assert.Equal("Open", AccessibilityTreeBuilder.ResolveName(single));

        var none = BoxElement.New();
        none.On<ClickEvent>(_ => { });
        none.AddChild(TextElement.New(""));
        Assert.Null(AccessibilityTreeBuilder.GetContentText(none));
    }

    [Fact]
    public void OverlayTrees_AddedAfterTheBuild_JoinTheRoot()
    {
        // The window adds each open overlay's tree to its root's after building it.
        var root = AccessibilityTreeBuilder.Build(Scene());
        int before = root.Children.Count;
        var overlayRoot = BoxElement.New();
        overlayRoot.Role = AccessibilityRole.Dialog;
        var overlay = AccessibilityTreeBuilder.Build(overlayRoot);
        root.AddChild(overlay);

        Assert.Equal(before + 1, root.Children.Count);
        Assert.Same(overlay, root.Children[^1]);
        Assert.Same(root, overlay.Parent);
    }
}
