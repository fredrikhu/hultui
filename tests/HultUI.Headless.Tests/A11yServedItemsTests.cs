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

    // ── UI Automation ──

    /// <summary>A session with the UIA bridge on a hidden window, its events recorded.</summary>
    [SupportedOSPlatform("windows")]
    private sealed class UiaRig : IDisposable
    {
        private readonly TestHwnd _hwnd = TestHwnd.Create();
        public readonly HeadlessSession Session = HeadlessSession.Create(800, 600);
        public readonly WindowsAccessibilityBridge Bridge;
        public readonly List<UiaRaised> Raised = [];

        public UiaRig()
        {
            var window = Session.Window.Window;
            var router = new AccessibilityActionRouter(window);
            var host = new UiaHostBridge(window, router);
            window.SetUiaHostBridge(host);
            window.SetActionRouter(router);
            Bridge = new WindowsAccessibilityBridge(_hwnd.Handle, host);
            Bridge.Raised = Raised.Add;
            Session.Window.InstallAccessibilityBridge(new Active(Bridge));
        }

        public void Dispose()
        {
            Bridge.Dispose();
            Session.Dispose();
            _hwnd.Dispose();
        }
    }

    /// <summary>The UIA events of the 40 moves of the walk over a list of <paramref name="count"/>
    /// items, as (kind, id, item index), and the trees they built.</summary>
    [SupportedOSPlatform("windows")]
    private static (List<(UiaRaisedKind, int, int)> Raised, int Builds) UiaWalk(int count)
    {
        using var rig = new UiaRig();
        var list = List(count);
        var root = Mount(rig.Session, list);
        list.SelectedIndex = 3;
        rig.Session.PumpFrame();
        int builds = root.Builds;
        rig.Raised.Clear();
        for (int i = 0; i < 40; i++)
        {
            list.SelectedIndex = WalkIndex(i);
            rig.Session.PumpFrame();
        }
        var raised = rig.Raised.Select(r => (r.Kind, r.Id,
            r.Element is { } e && VirtualItemId.TryDecode(e, out var owner, out int index) && owner == list.Id ? index : -1)).ToList();
        return (raised, root.Builds - builds);
    }

    [WindowsFact]
    [SupportedOSPlatform("windows")]
    public void UIA_FortyMovesOver3000Items_RaiseWhatTheyRaiseOver1500()
    {
        var (eager, _) = UiaWalk(1_500);
        var (served, builds) = UiaWalk(3_000);

        Assert.Equal(80, eager.Count);
        Assert.All(eager, r => Assert.Equal((UiaRaisedKind.Property, IsSelectedProperty), (r.Item1, r.Item2)));
        // The same events on the same items, in the same order: the item that had the selection and
        // the one that has it, in item order.
        Assert.Equal(eager, served);
        Assert.Equal([(UiaRaisedKind.Property, IsSelectedProperty, 3), (UiaRaisedKind.Property, IsSelectedProperty, 4)], served.Take(2));
        Assert.Equal(0, builds);
    }

    [SupportedOSPlatform("windows")]
    private static T Logic<T>(IntPtr native) where T : class
    {
        Assert.NotEqual(IntPtr.Zero, native);
        try { return Assert.IsType<T>(NativeProvider.LogicOf(native)); }
        finally { Marshal.Release(native); }
    }

    [SupportedOSPlatform("windows")]
    private static bool Selected(INativeSimple provider)
    {
        Assert.Equal(0, provider.GetPropertyValue(IsSelectedProperty, out var value));
        Assert.Equal(UiaVariant.VT_BOOL, value.Vt);
        return value.ShortValue != 0;
    }

    [SupportedOSPlatform("windows")]
    private static string? Name(INativeSimple provider)
    {
        Assert.Equal(0, provider.GetPropertyValue(UiaInterop.UIA_NamePropertyId, out var value));
        try { return value.AsString(); }
        finally { value.Free(); }
    }

    [WindowsFact]
    [SupportedOSPlatform("windows")]
    public void UIA_ItemsServedOnDemand_AnswerFromTheListNode_AsItemNodesWould()
    {
        // Where an item node would be: the same list at 1,500 items has one for item 0.
        UiaRect eagerRect;
        using (var eagerRig = new UiaRig())
        {
            var eager = List(1_500);
            Mount(eagerRig.Session, eager);
            var eagerItem = Logic<UiaFragment>(eagerRig.Bridge.NativeFragment(Item(eager, 0)));
            Assert.Equal(0, eagerItem.GetBoundingRectangle(out eagerRect));
        }

        using var rig = new UiaRig();
        var list = List(3_000);
        Mount(rig.Session, list);
        list.SelectedIndex = 1_500;
        rig.Session.PumpFrame();
        var bridge = rig.Bridge;
        var listWrapper = Logic<UiaFragment>(bridge.NativeFragment(list.Id));

        // First, last and middle: the items a reader reaches.
        Assert.Equal(0, listWrapper.Navigate(UiaInterop.NavigateDirection_FirstChild, out var first));
        var item0 = Logic<UiaVirtualItem>(first);
        Assert.Equal(0, item0.Index);
        Assert.Equal(0, listWrapper.Navigate(UiaInterop.NavigateDirection_LastChild, out var last));
        Assert.Equal(2_999, Logic<UiaVirtualItem>(last).Index);
        var middle = Logic<UiaVirtualItem>(bridge.ItemSimpleProvider(list.Id, 1_500));
        Assert.Equal("item 1500", Name(middle));
        Assert.True(Selected(middle));
        Assert.False(Selected(item0));
        Assert.Equal("item 0", Name(item0));

        Assert.Equal(0, item0.GetBoundingRectangle(out var servedRect));
        Assert.Equal((eagerRect.Left, eagerRect.Top, eagerRect.Width, eagerRect.Height),
            (servedRect.Left, servedRect.Top, servedRect.Width, servedRect.Height));

        // The container's selection and a search for the selected item, from the node.
        Assert.Equal([1_500], bridge.GetSelectedIndices(list.Id));
        var found = Logic<UiaVirtualItem>(bridge.FindItemByProperty(list.Id, -1, IsSelectedProperty, null));
        Assert.Equal(1_500, found.Index);
        Assert.Equal(IntPtr.Zero, bridge.FindItemByProperty(list.Id, 1_500, IsSelectedProperty, null));

        // Told before it is read: a client that reads the item on the event reads its state now.
        rig.Raised.Clear();
        list.SelectedIndex = 1_501;
        rig.Session.PumpFrame();
        Assert.Equal(
        [
            new UiaRaised(UiaRaisedKind.Property, IsSelectedProperty, Item(list, 1_500)),
            new UiaRaised(UiaRaisedKind.Property, IsSelectedProperty, Item(list, 1_501)),
        ], rig.Raised);
        Assert.False(Selected(middle));
        Assert.True(Selected(Logic<UiaVirtualItem>(bridge.ItemSimpleProvider(list.Id, 1_501))));
    }

    [WindowsFact]
    [SupportedOSPlatform("windows")]
    public void UIA_AMultiSelection_IsToldOnEachItemThatChanged()
    {
        using var rig = new UiaRig();
        var list = List(3_000);
        list.CanSelectMultiple = true;
        Mount(rig.Session, list);
        rig.Raised.Clear();

        var source = (IAccessibleItemSource)list;
        source.SetItemSelected(2_999, true);
        source.SetItemSelected(7, true);
        rig.Session.PumpFrame();

        Assert.Equal(
        [
            new UiaRaised(UiaRaisedKind.Property, IsSelectedProperty, Item(list, 7)),
            new UiaRaised(UiaRaisedKind.Property, IsSelectedProperty, Item(list, 2_999)),
        ], rig.Raised);
        Assert.Equal([7, 2_999], rig.Bridge.GetSelectedIndices(list.Id));
    }

    [WindowsFact]
    [SupportedOSPlatform("windows")]
    public void UIA_AnItemAClientHolds_IsGone_WhenTheListShrinksPastIt()
    {
        using var rig = new UiaRig();
        var list = List(3_000);
        Mount(rig.Session, list);
        var bridge = rig.Bridge;
        var held = Logic<UiaVirtualItem>(bridge.ItemSimpleProvider(list.Id, 2_999));
        var kept = Logic<UiaVirtualItem>(bridge.ItemSimpleProvider(list.Id, 5));
        Assert.Equal("item 2999", Name(held));
        rig.Raised.Clear();

        list.Items = Items(2_500);
        Settle(rig.Session);

        Assert.Contains(new UiaRaised(UiaRaisedKind.Structure, UiaInterop.StructureChangeType_ChildrenBulkRemoved, list.Id), rig.Raised);
        Assert.Equal(ElementNotAvailable, held.GetPropertyValue(UiaInterop.UIA_NamePropertyId, out _));
        Assert.Equal(ElementNotAvailable, held.GetBoundingRectangle(out _));
        Assert.Equal(ElementNotAvailable, held.Navigate(UiaInterop.NavigateDirection_Parent, out _));
        Assert.Equal("item 5", Name(kept));
        Assert.Equal(2_500, bridge.GetVirtualItemCount(list.Id));

        rig.Raised.Clear();
        list.Items = Items(3_100);
        Settle(rig.Session);
        Assert.Contains(new UiaRaised(UiaRaisedKind.Structure, UiaInterop.StructureChangeType_ChildrenBulkAdded, list.Id), rig.Raised);
        Assert.Equal("item 2999", Name(held));

        // Under 2,000 items the list has item nodes again: a provider held from before answers
        // from its item's node, which has its runtime id.
        list.Items = Items(1_500);
        list.SelectedIndex = 5;
        Settle(rig.Session);
        Assert.Equal("item 5", Name(kept));
        Assert.True(Selected(kept));
        Assert.Equal(ElementNotAvailable, held.GetPropertyValue(UiaInterop.UIA_NamePropertyId, out _));
    }

    // ── AT-SPI ──

    private sealed class Signals : IAtSpiSignals
    {
        public readonly List<string> Lines = [];
        public static string Short(string path) => path[(path.LastIndexOf('/') + 1)..];
        public void EmitChildrenChanged(string parentPath, string childPath, int index, bool isAdd) =>
            Lines.Add($"children-changed:{(isAdd ? "add" : "remove")} {Short(parentPath)} {Short(childPath)} {index}");
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

    private static string PathOf(ElementId id) => AtSpiConstants.AccessiblePathPrefix + id.Value;

    /// <summary>The AT-SPI signals of the 40 moves of the walk over a list of <paramref name="count"/>
    /// items, with each item named by its index.</summary>
    private static List<string> AtSpiWalk(int count)
    {
        using var session = HeadlessSession.Create(800, 600);
        var signals = new Signals();
        using var bridge = new LinuxAccessibilityBridge(signals);
        session.Window.InstallAccessibilityBridge(new Active(bridge));
        var list = List(count);
        Mount(session, list);
        list.SelectedIndex = 3;
        session.PumpFrame();
        signals.Lines.Clear();
        for (int i = 0; i < 40; i++)
        {
            list.SelectedIndex = WalkIndex(i);
            session.PumpFrame();
        }
        return signals.Lines.Select(line =>
        {
            var parts = line.Split(' ');
            return ulong.TryParse(parts[1], out var id) && VirtualItemId.TryDecode(new ElementId(id), out _, out int index)
                ? $"{parts[0]} item{index} {parts[2]}" : line;
        }).ToList();
    }

    [Fact]
    public void ATSPI_FortyMovesOver3000Items_SendWhatTheySendOver1500()
    {
        var eager = AtSpiWalk(1_500);
        var served = AtSpiWalk(3_000);

        Assert.Equal(80, eager.Count);
        Assert.Equal(eager, served);
        Assert.Equal(["state-changed:selected item3 0", "state-changed:selected item4 1"], served.Take(2));
    }

    [Fact]
    public void ATSPI_AListServedOnDemand_ServesItsItems_WhereItemNodesWouldBe()
    {
        // Where an item node would be: the same list at 1,500 items has one for item 0.
        (int, int, int, int) eagerBox;
        uint eagerStates;
        using (var eagerSession = HeadlessSession.Create(800, 600))
        using (var eagerBridge = new LinuxAccessibilityBridge(new Signals()))
        {
            eagerSession.Window.InstallAccessibilityBridge(new Active(eagerBridge));
            var eager = List(1_500);
            Mount(eagerSession, eager);
            var eagerItem = ((IAtSpiTree)eagerBridge).Find(PathOf(Item(eager, 0)))!;
            (eagerBox, eagerStates) = (eagerItem.Box, AtSpiMarshal.StateBits(eagerItem));
        }

        using var session = HeadlessSession.Create(800, 600);
        var signals = new Signals();
        using var bridge = new LinuxAccessibilityBridge(signals);
        session.Window.InstallAccessibilityBridge(new Active(bridge));
        var list = List(3_000);
        Mount(session, list);
        list.SelectedIndex = 1_500;
        session.PumpFrame();
        var tree = (IAtSpiTree)bridge;
        int published = tree.All.Count;

        var listAcc = tree.Find(PathOf(list.Id))!;
        Assert.Equal(3_000, listAcc.ChildCount);
        Assert.Null(listAcc.ChildPathAt(3_000));
        Assert.Null(listAcc.ChildPathAt(-1));
        foreach (int index in new[] { 0, 1_500, 2_999 })
        {
            var path = listAcc.ChildPathAt(index);
            Assert.Equal(PathOf(Item(list, index)), path);
            var item = tree.Find(path!)!;
            Assert.Same(item, tree.Find(path!));   // made once, kept
            Assert.Equal($"item {index}", item.Name);
            Assert.Equal(AtSpiConstants.RoleListItem, item.Role);
            Assert.Equal((listAcc.Path, index), (item.ParentPath, item.IndexInParent));
            Assert.True(item.Selectable);
            Assert.Equal(index == 1_500, item.Selected);
            Assert.Equal(index != 0, item.Offscreen);
            Assert.Equal(0, item.ChildCount);
            Assert.Equal("click", item.ActionName);
        }
        // Asked for, not published: the snapshot is the list's node and the window's.
        Assert.Equal(published, tree.All.Count);

        Assert.Equal(eagerBox, tree.Find(PathOf(Item(list, 0)))!.Box);
        Assert.Equal(eagerStates, AtSpiMarshal.StateBits(tree.Find(PathOf(Item(list, 0)))!));

        // An item's object reads its state when asked: the move a signal told.
        var held = tree.Find(PathOf(Item(list, 1_500)))!;
        signals.Lines.Clear();
        list.SelectedIndex = 0;
        session.PumpFrame();
        Assert.Equal([$"state-changed:selected {Item(list, 0).Value} 1", $"state-changed:selected {Item(list, 1_500).Value} 0"], signals.Lines);
        Assert.False(held.Selected);
        Assert.True(tree.Find(PathOf(Item(list, 0)))!.Selected);
    }

    [Fact]
    public void ATSPI_AnItemAClientHolds_GoesWhenTheListShrinksPastIt_AndOnlyThatOne()
    {
        using var session = HeadlessSession.Create(800, 600);
        var signals = new Signals();
        using var bridge = new LinuxAccessibilityBridge(signals);
        session.Window.InstallAccessibilityBridge(new Active(bridge));
        var list = List(3_000);
        Mount(session, list);
        var tree = (IAtSpiTree)bridge;
        var listAcc = tree.Find(PathOf(list.Id))!;
        var held = tree.Find(listAcc.ChildPathAt(2_999)!)!;
        var kept = tree.Find(listAcc.ChildPathAt(5)!)!;
        // Named in a signal: a client may hold it as well.
        list.SelectedIndex = 2_600;
        session.PumpFrame();
        signals.Lines.Clear();

        list.Items = Items(2_500);
        Settle(session);

        string Id(int index) => Item(list, index).Value.ToString();
        Assert.Equal(
        [
            $"cache-remove {Id(2_600)}",
            $"cache-remove {Id(2_999)}",
            $"children-changed:remove {list.Id.Value} {Id(2_500)} 2500",
            $"cache-add {list.Id.Value}",
        ], signals.Lines.Where(l => !l.StartsWith("state-changed", StringComparison.Ordinal)));
        Assert.Null(tree.Find(held.Path));
        Assert.Same(kept, tree.Find(kept.Path));
        Assert.Equal(2_500, tree.Find(PathOf(list.Id))!.ChildCount);

        // Grown again: the selection, past the end until now, is an item's again.
        signals.Lines.Clear();
        list.Items = Items(3_100);
        Settle(session);
        Assert.Equal(
        [
            $"children-changed:add {list.Id.Value} {Id(2_500)} 2500",
            $"cache-add {list.Id.Value}",
            $"state-changed:selected {Id(2_600)} 1",
        ], signals.Lines);
        Assert.Equal(3_100, tree.Find(PathOf(list.Id))!.ChildCount);
        Assert.Equal("item 2999", tree.Find(held.Path)!.Name);
    }
}
