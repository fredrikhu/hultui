using HultUI.Core.Accessibility;
using HultUI.Core.Elements;
using HultUI.Platform.Accessibility.Linux;
using HultUI.Testing;
using Xunit;

namespace HultUI.Platform.Accessibility.Linux.Tests;

/// <summary>
/// The published view of the tree is rebuilt in full on every apply, an object for every node.
/// Each object's path was made twice (once for its parent's list of children, once for itself),
/// in two strings each (the id's own, then the path), every parent listed its children in a
/// list, and the dictionary of objects grew entry by entry: about 500 bytes a node, where the
/// object and one path are about 270 (plan 17, a11y-atspi-snapshot-diet).
/// </summary>
public class AtSpiSnapshotTests
{
    /// <summary>Keeps a measured allocation on the heap, where the snapshot's are.</summary>
    private static object? s_kept;

    /// <summary>A root over rows of a number and a label, as a long settings or file list is:
    /// the rows' boxes are flattened, so the root has every label as a child.</summary>
    private static AccessibilityNode Tree(int rows)
    {
        var root = BoxElement.New();
        root.Role = AccessibilityRole.Application;
        for (int i = 0; i < rows; i++)
        {
            var row = BoxElement.New();
            row.AddChild(TextElement.New($"{i}"));
            var group = BoxElement.New();
            group.Role = AccessibilityRole.Group;
            group.AddChild(TextElement.New($"row {i}"));
            row.AddChild(group);
            root.AddChild(row);
        }
        return AccessibilityTreeBuilder.Build(root);
    }

    [Fact]
    public void AChild_IsPublishedAtTheVeryStringItsParentListsItAt()
    {
        var bridge = new LinuxAccessibilityBridge(new SignalRecorder());
        bridge.Publish(Tree(50));
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
    public void ASnapshot_CostsAnObjectAndOnePathANode_AndADictionaryOfItsSize()
    {
        var tree = Tree(500);
        var bridge = new LinuxAccessibilityBridge(new SignalRecorder());
        bridge.Publish(tree);

        // What the snapshot must make, each part measured alone: per node its object and (but
        // for the root's, which is a constant) its path, per parent one array of its children's
        // paths, and one dictionary sized for the tree.
        int nodes = 0;
        long expected = 0;
        long objectBytes = Allocations.Least(() =>
            s_kept = AtSpiAccessible.From(tree, AtSpiConstants.RootPath, isRoot: true, AtSpiConstants.NullPath, -1, 1f, []));
        Count(tree, isRoot: true);
        void Count(AccessibilityNode node, bool isRoot)
        {
            nodes++;
            expected += objectBytes;
            if (!isRoot) expected += Allocations.Least(() => s_kept = AtSpiAccessible.PathFor(node.ElementId));
            int length = node.Children.Count;
            if (length > 0) expected += Allocations.Least(() => s_kept = new string[length]);
            foreach (var child in node.Children) Count(child, isRoot: false);
        }
        expected += Allocations.Least(() => s_kept = new Dictionary<string, AtSpiAccessible>(nodes));

        long published = Allocations.Least(() => bridge.Publish(tree), goal: expected + 64);

        Assert.True(nodes > 1_000, $"{nodes} nodes");
        // And the snapshot object that holds them, 32 bytes: nothing else, nothing per node.
        Assert.InRange(published, expected, expected + 64);
    }
}
