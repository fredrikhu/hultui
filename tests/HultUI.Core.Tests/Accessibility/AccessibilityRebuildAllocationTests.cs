using HultUI.Core.Accessibility;
using HultUI.Core.Elements;
using HultUI.Core.Events;
using HultUI.Core.Layout;
using HultUI.Core.Primitives;
using HultUI.Testing;
using Xunit;

namespace HultUI.Core.Tests.Accessibility;

/// <summary>
/// While an assistive technology listens, every change it can see rebuilds the whole
/// accessibility tree and diffs it against the last one. What that leaves behind besides the new
/// tree is garbage on every rebuild (plan 17, a11y-build-diff-garbage): 1,000 rows of three
/// labels built 230 bytes a node and diffed 40, where the nodes themselves were 136. A node is
/// 80 bytes since its rarely set fields went into an object of their own (a11y-node-extras).
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

    /// <summary>What <paramref name="action"/> allocates on this thread: the least of a few runs
    /// (<see cref="Allocations"/>), stopping at one that allocates no more than
    /// <paramref name="goal"/>.</summary>
    private static long Bytes(Action action, long goal = 0) => Allocations.Least(action, goal);

    [Fact]
    public void DiffingAnUnchangedTree_AllocatesNothingButTheDiff()
    {
        var root = Scene();
        var a = AccessibilityTreeBuilder.Build(root);
        var b = AccessibilityTreeBuilder.Build(root);
        Assert.True(AccessibilityTreeDiffer.Diff(a, b).IsEmpty);

        long emptyDiff = Bytes(() => s_kept = AccessibilityTreeDiffer.Diff(null, null));
        long unchanged = Bytes(() => s_kept = AccessibilityTreeDiffer.Diff(a, b), goal: emptyDiff);

        // The TreeDiff it returns, and not a byte per node: no index of the children, no
        // enumerator. Before, this tree's diff allocated about 10 KB.
        Assert.Equal(emptyDiff, unchanged);
    }

    [Fact]
    public void BuildingATree_AllocatesItsNodesAndOneArrayOfTheExactLengthPerParent()
    {
        var root = Scene();
        // The thread's first build sets up what it keeps; the tree is the one each build makes.
        var tree = AccessibilityTreeBuilder.Build(root);

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

        long built = Bytes(() => s_kept = AccessibilityTreeBuilder.Build(root), goal: expected);

        Assert.True(nodes > 150 && parents >= 4, $"{nodes} nodes, {parents} parents");
        // Nothing else: no list per node, no enumerator per child list, no list grown in steps,
        // no string to name a button whose one text names it.
        Assert.Equal(expected, built);
    }

    [Fact]
    public void ANode_KeepsItsRarelySetFields_InAnObjectOnlyTheNodesThatSetOneGet()
    {
        // Description, value, automation id, scroll offset, native content and text: most nodes
        // have none of them, and they were 64 bytes of every node.
        long plain = Bytes(() => s_kept = new AccessibilityNode(default, AccessibilityRole.Label, "Name", null, default));
        long described = Bytes(() => s_kept = new AccessibilityNode(default, AccessibilityRole.Label, "Name", "Help", default));
        if (IntPtr.Size == 8)
            Assert.Equal(80, plain);
        Assert.True(described > plain, $"{described} B with a description, {plain} without");

        // What the builder sets on every node of a kind leaves a node without them as it is: a
        // scroll view at its origin, an element with no native content or text.
        var node = new AccessibilityNode(default, AccessibilityRole.ScrollView, null, null, default);
        long defaults = Bytes(() =>
        {
            node.ScrollOffset = default;
            node.NativeContent = default;
            node.Text = null;
            node.TextSource = null;
        });
        Assert.Equal(0, defaults);
        Assert.Null(node.Description);
        Assert.Null(node.Value);
        Assert.Null(node.AutomationId);
        Assert.Equal(default, node.ScrollOffset);
        Assert.True(node.NativeContent.IsEmpty);
        Assert.Null(node.CurrentText);

        // And a node that has them reads them back.
        var rich = new AccessibilityNode(default, AccessibilityRole.TextInput, "Field", "Help", default,
            value: "text", automationId: "field")
        {
            ScrollOffset = new Point(3, 4),
            NativeContent = new NativeAccessibleContent(7, 8),
        };
        Assert.Equal(("Help", "text", "field"), (rich.Description, rich.Value, rich.AutomationId));
        Assert.Equal(new Point(3, 4), rich.ScrollOffset);
        Assert.Equal(new NativeAccessibleContent(7, 8), rich.NativeContent);
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
