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
/// A virtual list's items carried no selected state in the accessibility tree, and setting the
/// list's <see cref="VirtualListElement{T}.SelectedIndex"/> told nothing. A move of the selection
/// that scrolled nothing changed no node: UIA raised nothing, AT-SPI said every item was
/// unselected, and a screen reader never read which item was selected. Each item node now carries
/// <see cref="AccessibilityNode.IsSelected"/> from its list, and a move is two IsSelected changes:
/// the item that had the selection and the one that has it.
///
/// <para>A move builds no tree for it (plan 17, round 3): the window marks the two items on the
/// applied tree in place and tells the bridge, as it does a focus move. Forcing a rebuild of the
/// whole tree for it put Junction's History walk, a key every 60 ms, at 41 rebuilds per 40 keys
/// where it had 25, and its a11y share from 7 to 30 %.</para>
/// </summary>
public class A11yListSelectionTests
{
    private const int IsSelectedProperty = UiaInterop.UIA_SelectionItemIsSelectedPropertyId;

    /// <summary>A real bridge, reported active whether or not a client listens.</summary>
    private sealed class Active(IAccessibilityBridge inner) : IAccessibilityBridge
    {
        public bool IsActive => true;
        public void ApplyChanges(TreeDiff diff, AccessibilityNode root) => inner.ApplyChanges(diff, root);
        public void ApplyPatchedProperties(TreeDiff patch, AccessibilityNode root) => inner.ApplyPatchedProperties(patch, root);
        public void NotifyFocusChanged(AccessibilityNode? focused) => inner.NotifyFocusChanged(focused);
        public void NotifyFocusChanged(AccessibilityNode? focused, AccessibilityNode? previous) => inner.NotifyFocusChanged(focused, previous);
    }

    /// <summary>A column that counts the accessibility trees built over it: the builder asks each
    /// node's element for its text source once, so as the root it is asked once per build.</summary>
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

    /// <summary>Twenty items, all on screen: no move scrolls.</summary>
    private static VirtualListElement<string> List(int count = 20)
    {
        var items = Enumerable.Range(0, count).Select(i => $"item {i}").ToList();
        return VirtualListElement<string>.New().ItemHeight(24)
            .RenderWith(t => Flex.Row().Padding(new Thickness(2, 8)).Children(TextB.New(t).FontSize(12f)))
            .Items(items);
    }

    private static ElementId Item(Element list, int index) => VirtualItemId.For(list.Id, index);

    /// <summary>Mounts <paramref name="lists"/> under a counting root, a label above them, and lets
    /// the layout refresh the mount left pending come and go, so the next frame is a quiet one.</summary>
    private static (CountingRoot Root, TextElement Label) Mount(HeadlessSession session, params Element[] lists)
    {
        var root = new CountingRoot();
        var label = TextB.New("status");
        root.AddChild(label);
        foreach (var list in lists) root.AddChild(list.FlexGrow(1));
        session.Window.SetRoot(root);
        session.PumpFrames(2);
        session.PumpFrame(TimeSpan.FromMilliseconds(120));
        session.PumpFrame(TimeSpan.FromMilliseconds(120));
        return (root, label);
    }

    /// <summary>A frame that rebuilds the tree for a change of its own, so the next 100 ms are
    /// inside the coalesced layout refresh's wait: what follows is told by a patch or not at all.</summary>
    private static void RebuildNow(HeadlessSession session, TextElement label)
    {
        label.Content += "!";
        session.PumpFrame();
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

    [Fact]
    public void AMove_BuildsNoTree_AndIsMarkedOnTheAppliedTree()
    {
        using var session = HeadlessSession.Create(800, 600);
        var bridge = session.Window.InstallAccessibilityBridge();
        var list = List();
        var (root, _) = Mount(session, list);
        list.SelectedIndex = 3;
        session.PumpFrame();
        var tree = bridge.CurrentTree!;
        int builds = root.Builds;
        bridge.ClearDiffs();

        list.SelectedIndex = 4;
        session.PumpFrame();

        Assert.Equal(builds, root.Builds);
        Assert.Same(tree, bridge.CurrentTree);
        var patch = Assert.Single(bridge.Diffs);
        Assert.Empty(patch.Added);
        Assert.Empty(patch.Removed);
        Assert.Equal(
            [(Item(list, 3), AccessibilityProperties.IsSelected), (Item(list, 4), AccessibilityProperties.IsSelected)],
            patch.PropertyChanged.Select(c => (c.Node.ElementId, c.PropertyName)));
        var items = ListNode(tree, list).Children;
        Assert.All(items, (item, i) => Assert.Equal(i == 4, item.IsSelected));

        // The next rebuild compares against the marked tree: it does not tell the move again.
        bridge.ClearDiffs();
        root.AddChild(TextB.New("more"));
        session.PumpFrame();
        Assert.Equal(builds + 1, root.Builds);
        Assert.DoesNotContain(bridge.Diffs.SelectMany(d => d.PropertyChanged),
            c => c.PropertyName == AccessibilityProperties.IsSelected);
        Assert.True(ListNode(bridge.CurrentTree!, list).Children[4].IsSelected);
    }

    [WindowsFact]
    [SupportedOSPlatform("windows")]
    public void UIA_AMoveThatScrollsNothing_ChangesIsSelected_OnTheOldItemAndTheNew()
    {
        using var hwnd = TestHwnd.Create();
        using var session = HeadlessSession.Create(800, 600);
        var window = session.Window.Window;
        var router = new AccessibilityActionRouter(window);
        var host = new UiaHostBridge(window, router);
        window.SetUiaHostBridge(host);
        window.SetActionRouter(router);
        using var bridge = new WindowsAccessibilityBridge(hwnd.Handle, host);
        var raised = new List<UiaRaised>();
        bridge.Raised = raised.Add;
        session.Window.InstallAccessibilityBridge(new Active(bridge));

        var list = List();
        var (root, _) = Mount(session, list);
        list.SelectedIndex = 3;
        session.PumpFrame();
        Assert.True(Selected(bridge, Item(list, 3)));
        int builds = root.Builds;
        raised.Clear();

        list.SelectedIndex = 4;
        session.PumpFrame();

        Assert.Equal(
        [
            new UiaRaised(UiaRaisedKind.Property, IsSelectedProperty, Item(list, 3)),
            new UiaRaised(UiaRaisedKind.Property, IsSelectedProperty, Item(list, 4)),
        ], raised);
        Assert.False(Selected(bridge, Item(list, 3)));
        Assert.True(Selected(bridge, Item(list, 4)));
        Assert.False(Selected(bridge, Item(list, 5)));

        // The same index again changes nothing; none clears the one that had it.
        raised.Clear();
        list.SelectedIndex = 4;
        session.PumpFrame();
        Assert.Empty(raised);
        list.SelectedIndex = -1;
        session.PumpFrame();
        Assert.Equal([new UiaRaised(UiaRaisedKind.Property, IsSelectedProperty, Item(list, 4))], raised);
        Assert.False(Selected(bridge, Item(list, 4)));

        // None of it built a tree.
        Assert.Equal(builds, root.Builds);
    }

    /// <summary>What the item's wrapper answers for SelectionItem.IsSelected.</summary>
    [SupportedOSPlatform("windows")]
    private static bool Selected(WindowsAccessibilityBridge bridge, ElementId item)
    {
        var native = bridge.NativeFragment(item);
        Assert.NotEqual(IntPtr.Zero, native);
        try
        {
            var wrapper = Assert.IsType<UiaFragment>(NativeProvider.LogicOf(native));
            wrapper.GetPropertyValue(IsSelectedProperty, out var value);
            Assert.Equal(UiaVariant.VT_BOOL, value.Vt);
            return value.ShortValue != 0;
        }
        finally { Marshal.Release(native); }
    }

    private sealed class Signals : IAtSpiSignals
    {
        public readonly List<string> Lines = [];
        private static string Short(string path) => path[(path.LastIndexOf('/') + 1)..];
        public void EmitChildrenChanged(string parentPath, string childPath, int index, bool isAdd) => Lines.Add($"children-changed {Short(childPath)}");
        public void EmitPropertyChange(string path, string atSpiProperty, string newValue) => Lines.Add($"property-change:{atSpiProperty} {Short(path)}");
        public void EmitValueChanged(string path, double value) => Lines.Add($"property-change:accessible-value {Short(path)}");
        public void EmitRoleChange(string path, uint role) => Lines.Add($"property-change:accessible-role {Short(path)}");
        public void EmitStateChanged(string path, string state, bool active) => Lines.Add($"state-changed:{state} {Short(path)} {(active ? 1 : 0)}");
        public void EmitTextChanged(string path, bool insert, int offset, string text) => Lines.Add($"text-changed {Short(path)}");
        public void EmitTextCaretMoved(string path, int offset) => Lines.Add($"text-caret-moved {Short(path)}");
        public void EmitTextSelectionChanged(string path) => Lines.Add($"text-selection-changed {Short(path)}");
        public void EmitAnnouncement(string path, string text, int politeness) => Lines.Add($"announcement {Short(path)}");
        public void EmitCacheAdd(IAtSpiTree tree, AtSpiAccessible node) => Lines.Add($"cache-add {Short(node.Path)}");
        public void EmitCacheRemove(string path) => Lines.Add($"cache-remove {Short(path)}");
    }

    [Fact]
    public void ATSPI_AMoveThatScrollsNothing_ChangesSelected_OnTheOldItemAndTheNew()
    {
        using var session = HeadlessSession.Create(800, 600);
        var signals = new Signals();
        using var bridge = new LinuxAccessibilityBridge(signals);
        session.Window.InstallAccessibilityBridge(new Active(bridge));

        var list = List();
        var (root, _) = Mount(session, list);
        list.SelectedIndex = 3;
        session.PumpFrame();
        Assert.True(Find(bridge, Item(list, 3)).Selected);
        var item5 = Find(bridge, Item(list, 5));
        int builds = root.Builds;
        signals.Lines.Clear();

        list.SelectedIndex = 4;
        session.PumpFrame();

        Assert.Equal(
        [
            $"state-changed:selected {Item(list, 3).Value} 0",
            $"state-changed:selected {Item(list, 4).Value} 1",
        ], signals.Lines);
        Assert.False(Find(bridge, Item(list, 3)).Selected);
        Assert.True(Find(bridge, Item(list, 4)).Selected);
        Assert.True(Find(bridge, Item(list, 5)).Selectable);
        Assert.False(Find(bridge, Item(list, 5)).Selected);
        // In the published snapshot, which a move does not replace.
        Assert.Same(item5, Find(bridge, Item(list, 5)));
        Assert.Equal(builds, root.Builds);
    }

    private static AtSpiAccessible Find(LinuxAccessibilityBridge bridge, ElementId item) =>
        ((IAtSpiTree)bridge).Find(AtSpiConstants.AccessiblePathPrefix + item.Value)
        ?? throw new InvalidOperationException($"no object for {item.Value}");

    [Fact]
    public void AMoveOntoAnItemAddedInTheSameHandler_IsToldByARebuild()
    {
        // The node has 20 items and the list 21: the move cannot be marked on it, so the frame
        // builds the tree, and its diff tells the new item (selected) and the old one's change.
        using var session = HeadlessSession.Create(800, 600);
        var bridge = session.Window.InstallAccessibilityBridge();
        var list = List();
        var (root, label) = Mount(session, list);
        list.SelectedIndex = 3;
        RebuildNow(session, label);
        int builds = root.Builds;
        bridge.ClearDiffs();

        list.Items = Enumerable.Range(0, 21).Select(i => $"item {i}").ToList();
        list.SelectedIndex = 20;
        session.PumpFrame();

        Assert.Equal(builds + 1, root.Builds);
        var items = ListNode(bridge.CurrentTree!, list).Children;
        Assert.Equal(21, items.Count);
        Assert.All(items, (item, i) => Assert.Equal(i == 20, item.IsSelected));
        var diff = Assert.Single(bridge.Diffs);
        Assert.Contains(diff.Added, a => a.Node.ElementId == Item(list, 20));
        Assert.Contains(diff.PropertyChanged,
            c => c.Node.ElementId == Item(list, 3) && c.PropertyName == AccessibilityProperties.IsSelected);
    }

    [Fact]
    public void AMoveInAListMountedInTheSameHandler_IsInTheTreeItsRebuildBuilds()
    {
        using var session = HeadlessSession.Create(800, 600);
        var bridge = session.Window.InstallAccessibilityBridge();
        var (root, _) = Mount(session);
        int builds = root.Builds;

        var list = List();
        root.AddChild(list.FlexGrow(1));
        list.SelectedIndex = 7;
        session.PumpFrame();

        Assert.Equal(builds + 1, root.Builds);
        Assert.All(ListNode(bridge.CurrentTree!, list).Children, (item, i) => Assert.Equal(i == 7, item.IsSelected));
    }

    [Fact]
    public void AListOver2000Items_AMove_BuildsNoTree_AndIsToldOnTheListNode()
    {
        // Its items are served on demand, with no node each: the list node carries the selection,
        // and a move is its own change (plan 17, round 5, step 14; until then no reader was told a
        // move there). A move made the window build and diff the whole tree for an empty diff.
        using var session = HeadlessSession.Create(800, 600);
        var bridge = session.Window.InstallAccessibilityBridge();
        var list = List(3_000);
        var (root, label) = Mount(session, list);
        Assert.True(ListNode(bridge.CurrentTree!, list).IsVirtualItemContainer);
        RebuildNow(session, label);
        int builds = root.Builds;
        bridge.ClearDiffs();

        list.SelectedIndex = 5;
        session.PumpFrame();
        list.SelectedIndex = 6;
        session.PumpFrame();

        Assert.Equal(builds, root.Builds);
        Assert.Equal([(list.Id, AccessibilityProperties.ItemSelection), (list.Id, AccessibilityProperties.ItemSelection)],
            bridge.Diffs.SelectMany(d => d.PropertyChanged).Select(c => (c.Node.ElementId, c.PropertyName)));
        Assert.Equal([6], ListNode(bridge.CurrentTree!, list).Served.SelectedArray);
    }

    [Fact]
    public void AMoveOntoAnItemScrolledOut_IsToldAtOnce_AndTheScrollWhenItsRefreshIsDue()
    {
        // A walk past the bottom of the view: the move scrolls the item in. The selection is told
        // in the move's frame, from the applied tree, where the item is still out of view; the
        // scroll is told by the layout refresh 100 ms on, as a wheel turn's is, with the item's
        // bounds as they are now and no second word of the selection.
        using var session = HeadlessSession.Create(800, 300);
        var bridge = session.Window.InstallAccessibilityBridge();
        var list = List(200);
        var (root, label) = Mount(session, list);
        list.SelectedIndex = 5;
        RebuildNow(session, label);
        int builds = root.Builds;
        Assert.True(ListNode(bridge.CurrentTree!, list).Children[150].IsOffscreen);
        bridge.ClearDiffs();

        list.SelectedIndex = 150;
        list.ScrollRangeIntoView(150 * 24f, 24f);
        session.PumpFrame();

        Assert.Equal(builds, root.Builds);
        var patch = Assert.Single(bridge.Diffs);
        Assert.Equal(
            [(Item(list, 5), AccessibilityProperties.IsSelected), (Item(list, 150), AccessibilityProperties.IsSelected)],
            patch.PropertyChanged.Select(c => (c.Node.ElementId, c.PropertyName)));
        Assert.True(ListNode(bridge.CurrentTree!, list).Children[150].IsSelected);

        bridge.ClearDiffs();
        for (int i = 0; i < 8; i++) session.PumpFrame();
        Assert.Equal(builds + 1, root.Builds);
        var refresh = Assert.Single(bridge.Diffs);
        Assert.Contains(refresh.PropertyChanged,
            c => c.Node.ElementId == list.Id && c.PropertyName == AccessibilityProperties.ScrollPosition);
        Assert.DoesNotContain(refresh.PropertyChanged, c => c.PropertyName == AccessibilityProperties.IsSelected);
        var item = ListNode(bridge.CurrentTree!, list).Children[150];
        Assert.True(item.IsSelected);
        Assert.False(item.IsOffscreen);
    }

    [Fact]
    public void TwoListsMovedInOneFrame_AreToldInTreeOrder()
    {
        using var session = HeadlessSession.Create(800, 600);
        var bridge = session.Window.InstallAccessibilityBridge();
        var first = List(10);
        var second = List(10);
        var (root, label) = Mount(session, first, second);
        RebuildNow(session, label);
        int builds = root.Builds;
        bridge.ClearDiffs();

        // The second list moves first: a diff would still list the first list's items first.
        second.SelectedIndex = 2;
        first.SelectedIndex = 8;
        session.PumpFrame();

        Assert.Equal(builds, root.Builds);
        var patch = Assert.Single(bridge.Diffs);
        Assert.Equal([Item(first, 8), Item(second, 2)], patch.PropertyChanged.Select(c => c.Node.ElementId));
    }

    [Fact]
    public void AMultiSelectionChangedByAssistiveTechnology_IsMarkedToo()
    {
        using var session = HeadlessSession.Create(800, 600);
        var bridge = session.Window.InstallAccessibilityBridge();
        var list = List();
        list.CanSelectMultiple = true;
        var (root, label) = Mount(session, list);
        RebuildNow(session, label);
        int builds = root.Builds;
        bridge.ClearDiffs();

        var source = (IAccessibleItemSource)list;
        source.SetItemSelected(2, true);
        source.SetItemSelected(9, true);
        session.PumpFrame();

        Assert.Equal(builds, root.Builds);
        var patch = Assert.Single(bridge.Diffs);
        Assert.Equal([Item(list, 2), Item(list, 9)], patch.PropertyChanged.Select(c => c.Node.ElementId));
        Assert.All(ListNode(bridge.CurrentTree!, list).Children, (item, i) => Assert.Equal(i is 2 or 9, item.IsSelected));
    }
}
