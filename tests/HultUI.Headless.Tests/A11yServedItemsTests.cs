// The AT-SPI bridge is Linux's, but with its signals recorded instead of sent it runs anywhere:
// nothing here reaches a bus.
#pragma warning disable CA1416
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using HultUI.Core.Accessibility;
using HultUI.Core.Builders;
using HultUI.Core.Elements;
using HultUI.Core.Primitives;
using HultUI.Platform.Accessibility.Linux;
using HultUI.Platform.Accessibility.Windows;
using Xunit;
using TextB = HultUI.Core.Builders.Text;

namespace HultUI.Headless.Tests;

/// <summary>
/// A list over 2,000 items serves its items on demand: the tree has the list's node and none for its
/// items. Until plan 17's round 5 (step 14) a reader could not reach them on AT-SPI (the list's
/// object had no children: ChildCount 0, two objects published for 3,000 items), and no move of the
/// selection was told on either bridge: 40 moves over 3,000 items told 0 times, where 1,500 items got
/// 80 IsSelected on UIA and 80 state-changed:selected on AT-SPI. Now the list node carries its items'
/// number and selection (<see cref="AccessibilityNode.Served"/>), a move is told as its own change
/// (<see cref="AccessibilityProperties.ItemSelection"/>), and each bridge tells it on the items, as it
/// tells an item node's.
/// </summary>
public class A11yServedItemsTests
{
    private const int IsSelectedProperty = UiaInterop.UIA_SelectionItemIsSelectedPropertyId;

    /// <summary>UIA_E_ELEMENTNOTAVAILABLE.</summary>
    private const int ElementNotAvailable = unchecked((int)0x80040201);

    /// <summary>A real bridge, reported active whether or not a client listens.</summary>
    private sealed class Active(IAccessibilityBridge inner) : IAccessibilityBridge
    {
        public bool IsActive => true;
        public void ApplyChanges(TreeDiff diff, AccessibilityNode root) => inner.ApplyChanges(diff, root);
        public void ApplyPatchedProperties(TreeDiff patch, AccessibilityNode root) => inner.ApplyPatchedProperties(patch, root);
        public void NotifyFocusChanged(AccessibilityNode? focused) => inner.NotifyFocusChanged(focused);
        public void NotifyFocusChanged(AccessibilityNode? focused, AccessibilityNode? previous) => inner.NotifyFocusChanged(focused, previous);
    }

    /// <summary>A column that counts the accessibility trees built over it (the builder asks the
    /// root for its text source once per build; a refresh made in place asks it too).</summary>
    private sealed class CountingRoot : BoxElement
    {
        public int Builds;

        internal override IAccessibleTextSource? AccessibleTextSource
        {
            get
            {
                Builds++;
                return null;
            }
        }
    }

    private static VirtualListElement<string> List(int count)
    {
        var items = Enumerable.Range(0, count).Select(i => $"item {i}").ToList();
        return VirtualListElement<string>.New().ItemHeight(24)
            .RenderWith(t => Flex.Row().Padding(new Thickness(2, 8)).Children(TextB.New(t).FontSize(12f)))
            .Items(items);
    }

    private static IReadOnlyList<string> Items(int count) => Enumerable.Range(0, count).Select(i => $"item {i}").ToList();

    private static ElementId Item(Element list, int index) => VirtualItemId.For(list.Id, index);

    /// <summary>Mounts <paramref name="list"/> under a counting root and lets the layout refresh the
    /// mount left pending come and go, so the next frame is a quiet one.</summary>
    private static CountingRoot Mount(HeadlessSession session, Element list)
    {
        var root = new CountingRoot();
        root.AddChild(TextB.New("status"));
        root.AddChild(list.FlexGrow(1));
        session.Window.SetRoot(root);
        session.PumpFrames(2);
        session.PumpFrame(TimeSpan.FromMilliseconds(120));
        session.PumpFrame(TimeSpan.FromMilliseconds(120));
        return root;
    }

    /// <summary>Lets every coalesced refresh come due: what a change of the list's items waits for.</summary>
    private static void Settle(HeadlessSession session)
    {
        for (int i = 0; i < 3; i++) session.PumpFrame(TimeSpan.FromMilliseconds(120));
    }

    /// <summary>The 40 moves of the walk, each one up or down from the last, between 3 and 13: no move
    /// scrolls.</summary>
    private static int WalkIndex(int step)
    {
        int k = (step + 1) % 20;
        return 3 + (k <= 10 ? k : 20 - k);
    }

    private static AccessibilityNode ListNode(AccessibilityNode tree, Element list) =>
        Find(tree, list.Id) ?? throw new InvalidOperationException("the list has no node");

    private static AccessibilityNode? Find(AccessibilityNode node, ElementId id)
    {
        if (node.ElementId == id) return node;
        foreach (var child in node.Children)
            if (Find(child, id) is { } found) return found;
        return null;
    }

    // ── What a move tells, through the recording bridge ──

    [Fact]
    public void AMove_IsToldOnTheListNode_InPlace_WithNoTreeBuilt()
    {
        using var session = HeadlessSession.Create(800, 600);
        var bridge = session.Window.InstallAccessibilityBridge();
        var list = List(3_000);
        var root = Mount(session, list);
        list.SelectedIndex = 3;
        session.PumpFrame();
        var tree = bridge.CurrentTree!;
        var node = ListNode(tree, list);
        Assert.True(node.IsVirtualItemContainer);
        Assert.Equal(3_000, node.Served.Count);
        Assert.Equal([3], node.Served.SelectedArray);
        int builds = root.Builds;
        bridge.ClearDiffs();

        list.SelectedIndex = 4;
        session.PumpFrame();

        Assert.Equal(builds, root.Builds);
        Assert.Same(tree, bridge.CurrentTree);
        var patch = Assert.Single(bridge.Diffs);
        Assert.Empty(patch.Added);
        Assert.Empty(patch.Removed);
        var change = Assert.Single(patch.PropertyChanged);
        Assert.Equal((node, AccessibilityProperties.ItemSelection), (change.Node, change.PropertyName));
        Assert.Equal([3], change.Previous!.Served.SelectedArray);
        Assert.Equal([4], node.Served.SelectedArray);

        // The next rebuild compares against the node as told: it does not tell the move again.
        bridge.ClearDiffs();
        root.AddChild(TextB.New("more"));
        session.PumpFrame();
        Assert.DoesNotContain(bridge.Diffs.SelectMany(d => d.PropertyChanged),
            c => c.PropertyName == AccessibilityProperties.ItemSelection);
        Assert.Equal([4], ListNode(bridge.CurrentTree!, list).Served.SelectedArray);
    }

    [Fact]
    public void AMoveThatScrolls_IsToldWithTheScroll_ByTheRefreshMadeInPlace()
    {
        // 120 ms frames: the scroll's layout refresh comes due in the move's frame and tells both,
        // the list's scroll first, as a diff lists them.
        using var session = HeadlessSession.Create(800, 300);
        var bridge = session.Window.InstallAccessibilityBridge();
        var list = List(3_000);
        var root = Mount(session, list);
        int builds = root.Builds;
        int trees = session.Window.AccessibilityTreesBuilt;

        for (int i = 1; i <= 20; i++)
        {
            bridge.ClearDiffs();
            list.SelectedIndex = i * 3;
            list.ScrollRangeIntoView(i * 3 * 24f, 24f);
            session.PumpFrame(TimeSpan.FromMilliseconds(120));
            var told = bridge.Diffs.SelectMany(d => d.PropertyChanged).Select(c => (c.Node.ElementId, c.PropertyName)).ToList();
            Assert.Contains((list.Id, AccessibilityProperties.ItemSelection), told);
            Assert.Equal([i * 3], ListNode(bridge.CurrentTree!, list).Served.SelectedArray);
        }
        Assert.Equal(trees, session.Window.AccessibilityTreesBuilt);
        Assert.True(session.Window.Window.AccessibilityLayoutRefreshesInPlace > 0);
    }

    [Fact]
    public void ItsItemsNumber_IsToldOnTheListNode_WhenTheItemsChange()
    {
        using var session = HeadlessSession.Create(800, 600);
        var bridge = session.Window.InstallAccessibilityBridge();
        var list = List(3_000);
        Mount(session, list);
        bridge.ClearDiffs();

        list.Items = Items(2_500);
        Settle(session);

        var change = Assert.Single(bridge.Diffs.SelectMany(d => d.PropertyChanged),
            c => c.PropertyName == AccessibilityProperties.ItemCount);
        Assert.Equal(list.Id, change.Node.ElementId);
        Assert.Equal(3_000, change.Previous!.Served.Count);
        Assert.Equal(2_500, ListNode(bridge.CurrentTree!, list).Served.Count);
    }
}
