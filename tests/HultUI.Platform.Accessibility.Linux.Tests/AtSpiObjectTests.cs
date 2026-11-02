using HultUI.Core.Accessibility;
using HultUI.Core.Elements;
using HultUI.Platform.Accessibility.Linux;
using HultUI.Testing;
using Xunit;

namespace HultUI.Platform.Accessibility.Linux.Tests;

/// <summary>
/// The bridge keeps one object per node from apply to apply, by element, as UI Automation's bridge
/// keeps its wrappers (plan 17, round 6): an apply points each object at its node in the new tree
/// and takes what changed, makes objects only for new nodes, keeps a parent's list of children while
/// its children are the same elements, and lets go of the objects of nodes that went. Each apply
/// made every object again, about 265 bytes a node: 797 KB to tell a dialog's label ticked over
/// 3,006 nodes.
/// </summary>
public class AtSpiObjectTests
{
    /// <summary>Keeps a measured allocation on the heap, where the bridge's are.</summary>
    private static object? s_kept;

    /// <summary>Sends nothing anywhere: what an apply allocates is the bridge's own.</summary>
    private sealed class NoSignals : IAtSpiSignals
    {
        public void EmitChildrenChanged(string parentPath, string childPath, int index, bool isAdd) { }
        public void EmitPropertyChange(string path, string atSpiProperty, string newValue) { }
        public void EmitValueChanged(string path, double value) { }
        public void EmitRoleChange(string path, uint role) { }
        public void EmitStateChanged(string path, string state, bool active) { }
        public void EmitTextChanged(string path, bool insert, int offset, string text) { }
        public void EmitTextCaretMoved(string path, int offset) { }
        public void EmitTextSelectionChanged(string path) { }
        public void EmitAnnouncement(string path, string text, int politeness) { }
        public void EmitCacheAdd(IAtSpiTree tree, AtSpiAccessible node) { }
        public void EmitCacheRemove(string path) { }
    }

    /// <summary>A root over rows of a number and a label, as a long settings or file list is:
    /// the rows' boxes are flattened, so the root has every number and label group as a child.</summary>
    private static BoxElement Rows(int rows, out List<TextElement> labels)
    {
        labels = [];
        var root = BoxElement.New();
        root.Role = AccessibilityRole.Application;
        for (int i = 0; i < rows; i++) root.AddChild(Row(i, labels));
        return root;
    }

    private static BoxElement Row(int i, List<TextElement> labels)
    {
        var row = BoxElement.New();
        row.AddChild(TextElement.New($"{i}"));
        var group = BoxElement.New();
        group.Role = AccessibilityRole.Group;
        var label = TextElement.New($"row {i}");
        labels.Add(label);
        group.AddChild(label);
        row.AddChild(group);
        return row;
    }

    private static AccessibilityNode Tree(Element root) => AccessibilityTreeBuilder.Build(root);

    private static AccessibilityNode? Find(AccessibilityNode node, ElementId id)
    {
        if (node.ElementId == id) return node;
        foreach (var child in node.Children)
            if (Find(child, id) is { } found) return found;
        return null;
    }

    private static void Apply(LinuxAccessibilityBridge bridge, ref AccessibilityNode? applied, AccessibilityNode tree)
    {
        bridge.ApplyChanges(AccessibilityTreeDiffer.Diff(applied, tree), tree);
        applied = tree;
    }

    [Fact]
    public void AChild_IsPublishedAtTheVeryStringItsParentListsItAt()
    {
        var bridge = new LinuxAccessibilityBridge(new SignalRecorder());
        bridge.Publish(Tree(Rows(50, out _)));
        var tree = (IAtSpiTree)bridge;

        int children = 0;
        foreach (var acc in tree.All)
            foreach (var path in acc.ChildPaths)
            {
                Assert.Same(path, tree.Find(path)!.Path);
                Assert.Same(acc.Path, tree.Find(path)!.ParentPath);
                children++;
            }
        Assert.Equal(tree.All.Count - 1, children);
    }

    [Fact]
    public void APath_IsOneString()
    {
        // An app's ids run into the thousands; the runtime keeps the strings of numbers below 300.
        var id = new ElementId(1_234_567);
        string path = AtSpiAccessible.PathFor(id);
        Assert.Equal("/org/a11y/atspi/accessible/1234567", path);

        long made = Allocations.Least(() => s_kept = AtSpiAccessible.PathFor(id));
        long oneString = Allocations.Least(() => s_kept = new string('/', path.Length));
        // Not the id's own string and then the path.
        Assert.Equal(oneString, made);
    }

    [Fact]
    public void AnApply_KeepsEveryObject_PointedAtItsNodeInTheNewTree()
    {
        var root = Rows(50, out var labels);
        var bridge = new LinuxAccessibilityBridge(new SignalRecorder());
        AccessibilityNode? applied = null;
        Apply(bridge, ref applied, Tree(root));
        var tree = (IAtSpiTree)bridge;
        var before = tree.All.ToDictionary(a => a.Path);

        // A new tree, every node in it made again, one label renamed.
        labels[7].Content = "renamed";
        var next = Tree(root);
        Apply(bridge, ref applied, next);

        Assert.Equal(before.Count, tree.All.Count);
        foreach (var acc in tree.All)
        {
            Assert.Same(before[acc.Path], acc);
            Assert.Same(acc.IsRoot ? next : Find(next, acc.ElementId), acc.Node);
        }
        Assert.Equal("renamed", tree.Find(AtSpiAccessible.PathFor(labels[7].Id))!.Name);
    }

    [Fact]
    public void AnApply_AllocatesForWhatChanged_NotForTheTree()
    {
        // The same change told over 200 rows and over 2,000: a label renamed, and back. Every object
        // was made again on each apply, about 265 bytes a node.
        long Bytes(int rows)
        {
            var root = Rows(rows, out var labels);
            var bridge = new LinuxAccessibilityBridge(new NoSignals());
            AccessibilityNode? applied = null;
            Apply(bridge, ref applied, Tree(root));
            labels[rows / 2].Content = "A";
            var a = Tree(root);
            labels[rows / 2].Content = "B";
            var b = Tree(root);
            Apply(bridge, ref applied, a);
            // Made up front: the round times the bridge, not the differ.
            var toB = AccessibilityTreeDiffer.Diff(a, b);
            var toA = AccessibilityTreeDiffer.Diff(b, a);
            return Allocations.Least(() =>
            {
                bridge.ApplyChanges(toB, b);
                bridge.ApplyChanges(toA, a);
            });
        }

        long few = Bytes(200), many = Bytes(2_000);
        Assert.True(many <= few, $"{few} B over 200 rows, {many} B over 2,000");
        Assert.True(many < 1_024, $"{many} B for two applies of one name");
    }

    [Fact]
    public void AParent_KeepsItsListOfChildren_WhileItsChildrenAreTheSameElements()
    {
        var root = Rows(20, out var labels);
        var bridge = new LinuxAccessibilityBridge(new NoSignals());
        AccessibilityNode? applied = null;
        Apply(bridge, ref applied, Tree(root));
        var tree = (IAtSpiTree)bridge;
        var rows = tree.Root!.ChildPaths;
        var group = tree.Find(AtSpiAccessible.PathFor(labels[3].Id))!.ParentPath;
        var groupChildren = tree.Find(group)!.ChildPaths;

        labels[3].Content = "renamed";
        Apply(bridge, ref applied, Tree(root));
        Assert.Same(rows, tree.Root!.ChildPaths);
        Assert.Same(groupChildren, tree.Find(group)!.ChildPaths);

        // A row more (its number and its group): the root's list is made again, in order; the rest
        // are kept.
        root.AddChild(Row(20, labels));
        Apply(bridge, ref applied, Tree(root));
        Assert.NotSame(rows, tree.Root!.ChildPaths);
        Assert.Equal(rows.Count + 2, tree.Root!.ChildPaths.Count);
        Assert.Equal(rows, tree.Root!.ChildPaths.Take(rows.Count));
        Assert.Same(groupChildren, tree.Find(group)!.ChildPaths);
    }

    [Fact]
    public void AGoneNodesObject_AnswersNothing_AndLetsGoOfItsNode()
    {
        var root = Rows(5, out var labels);
        var bridge = new LinuxAccessibilityBridge(new SignalRecorder());
        AccessibilityNode? applied = null;
        Apply(bridge, ref applied, Tree(root));
        var tree = (IAtSpiTree)bridge;
        string path = AtSpiAccessible.PathFor(labels[2].Id);
        var held = tree.Find(path)!;   // as a handler on the bus thread may hold it
        int objects = tree.All.Count;

        root.RemoveChild(root.Children[2]);
        Apply(bridge, ref applied, Tree(root));

        Assert.Null(tree.Find(path));
        Assert.DoesNotContain(tree.All, a => a.Path == path);
        Assert.Equal(objects - 3, tree.All.Count);   // the row's number, its group, its label
        // Its node reaches the whole tree it was in, through its parents.
        Assert.Null(held.Node);
    }

    [Fact]
    public void ATreeOfOtherElements_LetsGoOfEveryObjectOfTheOld()
    {
        // A tree from nothing (the bridge active again, or another root) removes nothing in its diff:
        // the objects the walk does not reach go all the same.
        var first = Rows(10, out var oldLabels);
        var bridge = new LinuxAccessibilityBridge(new SignalRecorder());
        var tree = (IAtSpiTree)bridge;
        AccessibilityNode? applied = null;
        Apply(bridge, ref applied, Tree(first));
        var held = tree.Find(AtSpiAccessible.PathFor(oldLabels[4].Id))!;

        var second = Rows(3, out _);
        AccessibilityNode? none = null;
        Apply(bridge, ref none, Tree(second));

        Assert.Equal(1 + 3 * 3, tree.All.Count);
        Assert.All(oldLabels, l => Assert.Null(tree.Find(AtSpiAccessible.PathFor(l.Id))));
        Assert.Null(held.Node);
    }

    [Fact]
    public void AReaderOnAnotherThread_ReadsAListOfChildrenWhole()
    {
        // 200 children, their order reversed on every apply. A handler on the bus thread copies the
        // root's children while the UI thread applies: it must find the list in one order or the other,
        // never part of each, so a list that changes is replaced, not written over.
        var root = BoxElement.New();
        root.Role = AccessibilityRole.Application;
        var kids = new List<Element>();
        for (int i = 0; i < 200; i++)
        {
            var child = TextElement.New($"item {i}");
            kids.Add(child);
            root.AddChild(child);
        }
        var forward = Tree(root);
        kids.Reverse();
        root.ReplaceChildren([.. kids]);
        var backward = Tree(root);
        var bridge = new LinuxAccessibilityBridge(new NoSignals());
        AccessibilityNode? applied = null;
        // The state the reader starts from is one it accepts.
        Apply(bridge, ref applied, forward);
        var tree = (IAtSpiTree)bridge;
        string[] one = [.. tree.Root!.ChildPaths], other = [.. Enumerable.Reverse(one)];
        var toBackward = AccessibilityTreeDiffer.Diff(forward, backward);
        var toForward = AccessibilityTreeDiffer.Diff(backward, forward);

        int torn = 0, reads = 0;
        var done = new ManualResetEventSlim();
        var reader = new Thread(() =>
        {
            var copy = new string[200];
            while (!done.IsSet)
            {
                var list = tree.Root!.ChildPaths;
                if (list.Count != copy.Length) { torn++; continue; }
                for (int i = 0; i < copy.Length; i++) copy[i] = list[i];
                if (!copy.AsSpan().SequenceEqual(one) && !copy.AsSpan().SequenceEqual(other)) torn++;
                reads++;
            }
        }) { IsBackground = true };
        reader.Start();
        for (int i = 0; i < 2_000; i++)
        {
            bridge.ApplyChanges(toBackward, backward);
            bridge.ApplyChanges(toForward, forward);
        }
        done.Set();
        reader.Join();

        Assert.True(reads > 0);
        Assert.Equal(0, torn);
    }
}
