// The AT-SPI bridge is Linux's, but with its signals recorded instead of sent it runs anywhere:
// nothing here reaches a bus.
#pragma warning disable CA1416
using HultUI.Core.Accessibility;
using HultUI.Core.Builders;
using HultUI.Core.Components;
using HultUI.Core.Elements;
using HultUI.Core.Primitives;
using HultUI.Core.Reactivity;
using HultUI.Platform.Accessibility.Linux;
using Xunit;
using TextB = HultUI.Core.Builders.Text;

namespace HultUI.Headless.Tests;

/// <summary>
/// A list with a current item of its own (<see cref="VirtualListElement{T}.CurrentIndex"/>, its
/// cursor) gives the reader that item, apart from the selection: the list node's
/// <see cref="AccessibilityNode.CurrentItem"/>, which is the reader's focus while the list holds the
/// keyboard (see A11yCurrentItemFocusTests). A move builds no tree: the window marks the list's
/// node on the applied tree and tells the bridge, as it does a selection move. A list that never
/// sets a cursor keeps the selection as its current item.
/// </summary>
public class A11yListCurrentItemTests
{
    /// <summary>A real bridge, reported active whether or not a client listens.</summary>
    private sealed class Active(IAccessibilityBridge inner) : IAccessibilityBridge
    {
        public bool IsActive => true;
        public void ApplyChanges(TreeDiff diff, AccessibilityNode root) => inner.ApplyChanges(diff, root);
        public void ApplyPatchedProperties(TreeDiff patch, AccessibilityNode root) => inner.ApplyPatchedProperties(patch, root);
        public void NotifyFocusChanged(AccessibilityNode? focused) => inner.NotifyFocusChanged(focused);
        public void NotifyFocusChanged(AccessibilityNode? focused, AccessibilityNode? previous) => inner.NotifyFocusChanged(focused, previous);
        public void NotifyChangesApplied(AccessibilityNode root) => inner.NotifyChangesApplied(root);
        public void NotifyWindowActivated() => inner.NotifyWindowActivated();
    }

    /// <summary>A column that counts the accessibility trees built over it (see A11yListSelectionTests).</summary>
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

    private static List<string> Names(int count, string prefix = "item") =>
        Enumerable.Range(0, count).Select(i => $"{prefix} {i}").ToList();

    /// <summary>A focusable list on state rows, its cursor on <paramref name="current"/> (none for
    /// -1); twenty items all fit on screen, so no move scrolls.</summary>
    private static VirtualListElement<string> List(int count = 20, int current = 3, int selected = -1)
    {
        var list = VirtualListElement<string>.New().ItemHeight(24)
            .Items(Names(count))
            .AccessibleItemLabel(t => t)
            .SelectedIndex(selected)
            .RenderWithState((t, state) => Flex.Row().Padding(new Thickness(2, 8))
                .Background(state.IsCurrent ? new Color(40, 90, 200) : Color.Transparent)
                .Children(TextB.New(t).FontSize(12f)));
        if (current >= 0) list.CurrentIndex = current;
        list.IsFocusable = true;
        return list;
    }

    private static ElementId Item(Element list, int index) => VirtualItemId.For(list.Id, index);

    /// <summary>Mounts <paramref name="list"/> under a counting root with a label above it, and
    /// lets the layout refresh the mount left pending come and go.</summary>
    private static (CountingRoot Root, TextElement Label) Mount(HeadlessSession session, Element list)
    {
        var root = new CountingRoot();
        var label = TextB.New("status");
        root.AddChild(label);
        root.AddChild(list.FlexGrow(1));
        session.Window.SetRoot(root);
        Settle(session);
        return (root, label);
    }

    private static void Settle(HeadlessSession session)
    {
        session.PumpFrames(2);
        session.PumpFrame(TimeSpan.FromMilliseconds(120));
        session.PumpFrame(TimeSpan.FromMilliseconds(120));
    }

    /// <summary>A frame that rebuilds the tree for a change of its own, so the next 100 ms are
    /// inside the coalesced layout refresh's wait: what follows is told by a patch or not at all.</summary>
    private static void RebuildNow(HeadlessSession session, TextElement label)
    {
        label.Content += "!";
        session.PumpFrame();
    }

    private static AccessibilityNode? Find(AccessibilityNode node, ElementId id)
    {
        if (node.ElementId == id) return node;
        foreach (var child in node.Children)
            if (Find(child, id) is { } found) return found;
        return null;
    }

    /// <summary>The list's node on the tree the bridge was last given.</summary>
    private static AccessibilityNode ListNode(HeadlessAccessibilityBridge bridge, Element list) =>
        Find(bridge.CurrentTree!, list.Id)!;

    private static PropertyChange[] Changes(HeadlessAccessibilityBridge bridge) =>
        bridge.Diffs.SelectMany(d => d.PropertyChanged).ToArray();

    [Fact]
    public void FocusingAListWithACursor_MakesItsCurrentItemTheCursor()
    {
        using var session = HeadlessSession.Create(800, 600);
        var bridge = session.Window.InstallAccessibilityBridge();
        var list = List(selected: 9);
        Mount(session, list);

        list.Focus();
        session.PumpFrame();

        var focused = bridge.FocusedNode!;
        Assert.Equal(list.Id, focused.ElementId);
        Assert.Equal(AccessibilityRole.List, focused.Role);
        Assert.Equal(3, focused.CurrentItem);
        Assert.Equal("item 3", focused.Children[focused.CurrentItem].Name);
        Assert.True(focused.Children[9].IsSelected);
    }

    [Fact]
    public void WithoutACursor_TheCurrentItemIsTheSelection()
    {
        using var session = HeadlessSession.Create(800, 600);
        var bridge = session.Window.InstallAccessibilityBridge();
        var list = List(current: -1, selected: 5);
        Mount(session, list);
        list.Focus();
        session.PumpFrame();
        Assert.Equal(5, bridge.FocusedNode!.CurrentItem);

        list.SelectedIndex = 6;
        session.PumpFrame();

        Assert.Equal(6, ListNode(bridge, list).CurrentItem);
    }

    [Fact]
    public void ACurrentMove_BuildsNoTree_AndMovesTheFocusOnTheAppliedTree()
    {
        using var session = HeadlessSession.Create(800, 600);
        var bridge = session.Window.InstallAccessibilityBridge();
        var list = List(selected: 9);
        var (root, label) = Mount(session, list);
        list.Focus();
        session.PumpFrame();
        RebuildNow(session, label);
        var tree = bridge.CurrentTree!;
        int builds = root.Builds;
        bridge.ClearDiffs();

        list.CurrentIndex = 4;
        session.PumpFrame();

        Assert.Equal(builds, root.Builds);
        Assert.Same(tree, bridge.CurrentTree);
        Assert.Empty(Changes(bridge));
        Assert.Equal(4, Find(tree, list.Id)!.CurrentItem);
        Assert.Same(Find(tree, list.Id), bridge.FocusedNode);
        Assert.True(Find(tree, Item(list, 9))!.IsSelected);

        // The next rebuild compares against the marked tree: it does not tell the move again.
        root.AddChild(TextB.New("more"));
        session.PumpFrame();
        Assert.Equal(builds + 1, root.Builds);
        Assert.Equal(4, ListNode(bridge, list).CurrentItem);
        Assert.DoesNotContain(Changes(bridge), c => c.PropertyName == AccessibilityProperties.IsFocused);
    }

    [Fact]
    public void ACurrentMoveOnAnUnfocusedList_TellsNothing_AndBuildsNoTree()
    {
        using var session = HeadlessSession.Create(800, 600);
        var bridge = session.Window.InstallAccessibilityBridge();
        var list = List();
        var (root, label) = Mount(session, list);
        RebuildNow(session, label);
        var focusedBefore = bridge.FocusedNode;
        int builds = root.Builds;
        bridge.ClearDiffs();

        list.CurrentIndex = 4;
        session.PumpFrame();

        Assert.Equal(builds, root.Builds);
        Assert.Empty(Changes(bridge));
        Assert.Same(focusedBefore, bridge.FocusedNode);
    }

    // ── AT-SPI, with the signals recorded ──

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

        public List<string> Focus() => Lines.Where(l => l.StartsWith("state-changed:focused", StringComparison.Ordinal)).ToList();
    }

    private static string Focused(ElementId id, bool on) => $"state-changed:focused {id.Value} {(on ? 1 : 0)}";

    private static (HeadlessSession Session, Signals Signals, LinuxAccessibilityBridge Bridge) AtSpi()
    {
        var session = HeadlessSession.Create(800, 600);
        var signals = new Signals();
        var bridge = new LinuxAccessibilityBridge(signals);
        session.Window.InstallAccessibilityBridge(new Active(bridge));
        return (session, signals, bridge);
    }

    [Fact]
    public void AtSpi_ACurrentMove_SendsFocusedStateChanges_ForBothItems()
    {
        var (session, signals, bridge) = AtSpi();
        using var _ = session;
        using var __ = bridge;
        var list = List(selected: 9);
        var (_, label) = Mount(session, list);
        list.Focus();
        session.PumpFrame();
        RebuildNow(session, label);
        signals.Lines.Clear();

        list.CurrentIndex = 4;
        session.PumpFrame();

        Assert.Equal([Focused(Item(list, 3), false), Focused(Item(list, 4), true)], signals.Focus());
        Assert.DoesNotContain(signals.Lines, l => l.StartsWith("state-changed:selected", StringComparison.Ordinal));
    }

    [Fact]
    public void ACurrentMoveAndAnItemsChangeInOneFrame_TellTheFocusOnce()
    {
        var (session, signals, bridge) = AtSpi();
        using var _ = session;
        using var __ = bridge;
        var list = List();
        var (_, label) = Mount(session, list);
        list.Focus();
        session.PumpFrame();
        signals.Lines.Clear();

        // A refresh: the items change (a reader would hear item 4's new name), the cursor moves,
        // and a label above changes too, so the frame rebuilds the tree at once.
        list.Items = Names(20, "renamed");
        list.CurrentIndex = 4;
        label.Content = "refreshed";
        session.PumpFrame();

        Assert.Equal([Focused(Item(list, 3), false), Focused(Item(list, 4), true)], signals.Focus());
        Assert.Equal("renamed 4", ((IAtSpiTree)bridge).Find(AtSpiAccessible.PathFor(Item(list, 4)))!.Name);
    }

    /// <summary>A pane that lists what it was given, its cursor in its state, as an unkeyed
    /// refresh does: new items and a new cursor in one build.</summary>
    private sealed class Pane : Component
    {
        public List<string> Items = Names(20);
        public int Cursor = 3;
        public readonly Ref<VirtualListElement<string>> List = new();

        public override Element Build()
        {
            var list = VirtualListElement<string>.New().ItemHeight(24)
                .Items(Items)
                .AccessibleItemLabel(t => t)
                .CurrentIndex(Cursor)
                .RenderWithState((t, state) => Flex.Row().Children(TextB.New(t).FontSize(12f)))
                .FlexGrow(1);
            list.IsFocusable = true;
            return Flex.Column().FlexGrow(1).Children(TextB.New("status"), list.WithRef(List));
        }

        public void Refresh(List<string> items, int cursor)
        {
            Items = items;
            Cursor = cursor;
            MarkDirty();
        }
    }

    [Fact]
    public void AnUnkeyedRefreshThatMovesTheCursor_TellsTheFocusOnce()
    {
        var (session, signals, bridge) = AtSpi();
        using var _ = session;
        using var __ = bridge;
        var pane = new Pane();
        session.Window.SetRoot(pane.Mount());
        Settle(session);
        var list = pane.List.Current!;
        list.Focus();
        session.PumpFrame();
        signals.Lines.Clear();

        pane.Refresh(Names(20, "renamed"), 4);
        for (int i = 0; i < 3; i++) session.PumpFrame(TimeSpan.FromMilliseconds(150));

        Assert.Same(list, pane.List.Current);
        Assert.Equal([Focused(Item(list, 3), false), Focused(Item(list, 4), true)], signals.Focus());
        Assert.Equal("renamed 4", ((IAtSpiTree)bridge).Find(AtSpiAccessible.PathFor(Item(list, 4)))!.Name);
    }

    [Fact]
    public void OnDemandList_TheCursorIsTheListsCurrentItem()
    {
        using var session = HeadlessSession.Create(800, 600);
        var bridge = session.Window.InstallAccessibilityBridge();
        var list = List(count: 2_500, current: 7, selected: 1);
        var (root, label) = Mount(session, list);

        list.Focus();
        session.PumpFrame();
        var focused = bridge.FocusedNode!;
        Assert.Equal(list.Id, focused.ElementId);
        Assert.True(focused.IsVirtualItemContainer);
        Assert.Equal(7, focused.CurrentItem);

        RebuildNow(session, label);
        int builds = root.Builds;
        list.CurrentIndex = 8;
        session.PumpFrame();

        Assert.Equal(8, ListNode(bridge, list).CurrentItem);
        Assert.Equal(builds, root.Builds);
    }

    [Fact]
    public void TakesAncestorFocus_FollowsTheCursor()
    {
        using var session = HeadlessSession.Create(800, 600);
        var bridge = session.Window.InstallAccessibilityBridge();
        var list = List(current: 5, selected: 2);
        list.IsFocusable = false;
        list.TakesAncestorFocus = true;
        var pane = new CountingRoot { IsFocusable = true };
        var label = TextB.New("status");
        pane.AddChild(label);
        pane.AddChild(list.FlexGrow(1));
        session.Window.SetRoot(pane);
        Settle(session);
        pane.Focus();
        session.PumpFrame();

        // The pane holds the focus; the list stands for it with its cursor, not its selection.
        Assert.Equal(pane.Id, bridge.FocusedNode?.ElementId);
        Assert.Equal(5, ListNode(bridge, list).CurrentItem);

        RebuildNow(session, label);
        int builds = pane.Builds;
        list.CurrentIndex = 6;
        session.PumpFrame();

        Assert.Equal(6, ListNode(bridge, list).CurrentItem);
        Assert.Equal(builds, pane.Builds);
    }
}
