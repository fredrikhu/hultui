using HultUI.Core.Accessibility;
using HultUI.Core.Elements;
using HultUI.Platform.Accessibility.Linux;
using Xunit;

namespace HultUI.Platform.Accessibility.Linux.Tests;

/// <summary>
/// What the AT-SPI bridge sends for a diff, recorded in place of the bus. libatspi keeps a
/// cache of objects and of each one's children, fed by these signals: a cache-add places an
/// object under its parent, a cache-remove disposes it (it goes defunct and leaves its
/// parent's children), and children-changed moves entries in a parent's list.
/// </summary>
public class AtSpiSignalTests
{
    private static string Id(Element element) => element.Id.Value.ToString();

    private sealed class Driver
    {
        public readonly SignalRecorder Signals = new();
        public readonly LinuxAccessibilityBridge Bridge;
        private readonly Element _root;
        private AccessibilityNode? _tree;

        public Driver(Element root)
        {
            _root = root;
            Bridge = new LinuxAccessibilityBridge(Signals);
            Apply();
            Signals.Lines.Clear();
        }

        /// <summary>Builds the tree again and applies its difference, as a window's frame does.</summary>
        public List<string> Apply()
        {
            Signals.Lines.Clear();
            var tree = AccessibilityTreeBuilder.Build(_root);
            Bridge.ApplyChanges(AccessibilityTreeDiffer.Diff(_tree, tree), tree);
            _tree = tree;
            return [.. Signals.Lines];
        }
    }

    [Fact]
    public void AnObjectThatMovesToAnotherParent_IsMoved_NotRemoved()
    {
        // A plain box has no object: its children belong to the object above it. Named, it is
        // one, and they move under it. The bridge sent their cache-add under the box and then a
        // cache-remove, which in libatspi disposes the object it had just placed.
        var title = TextElement.New("Title");
        var a = TextElement.New("A");
        var b = TextElement.New("B");
        var group = BoxElement.New();
        group.AddChild(a);
        group.AddChild(b);
        var root = BoxElement.New();
        root.AddChild(title);
        root.AddChild(group);
        var driver = new Driver(root);

        group.AccessibleName = "Group";
        Assert.Equal(
        [
            $"cache-add {Id(group)} parent=root index=1",
            $"cache-add {Id(a)} parent={Id(group)} index=0",
            $"cache-add {Id(b)} parent={Id(group)} index=1",
            $"children-changed:add root {Id(group)} 1",
            // Each moved object leaves its old parent's list: no cache-remove does it for them.
            $"children-changed:remove root {Id(a)} 1",
            $"children-changed:remove root {Id(b)} 2",
        ], driver.Apply());

        // And back: the box is gone, and only the box is removed.
        group.AccessibleName = null;
        Assert.Equal(
        [
            $"cache-add {Id(a)} parent=root index=1",
            $"cache-add {Id(b)} parent=root index=2",
            $"cache-remove {Id(group)}",
            $"children-changed:add root {Id(a)} 1",
            $"children-changed:remove root {Id(group)} 1",
        ], driver.Apply());
    }

    [Fact]
    public void RemovedObjects_AreStillRemoved_OneNudgePerParent()
    {
        var a = TextElement.New("A");
        var b = TextElement.New("B");
        var c = TextElement.New("C");
        var root = BoxElement.New();
        root.AddChild(a);
        root.AddChild(b);
        root.AddChild(c);
        var driver = new Driver(root);

        root.RemoveChild(b);
        root.RemoveChild(c);
        Assert.Equal(
        [
            $"cache-remove {Id(b)}",
            $"cache-remove {Id(c)}",
            $"children-changed:remove root {Id(b)} 1",
        ], driver.Apply());
    }
}
