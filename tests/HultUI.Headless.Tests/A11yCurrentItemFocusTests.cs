#pragma warning disable CA1416
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using HultUI.Core.Accessibility;
using HultUI.Core.Builders;
using HultUI.Core.Components;
using HultUI.Core.Elements;
using HultUI.Core.Events;
using HultUI.Core.Primitives;
using HultUI.Platform.Accessibility.Linux;
using HultUI.Platform.Accessibility.Windows;
using Xunit;
using TextB = HultUI.Core.Builders.Text;

namespace HultUI.Headless.Tests;

/// <summary>
/// A reader speaks focus moves, and a keyboard move in a list or a menu moves its selection: the
/// element focus stays on the list, or on whatever had it while a menu is open. Narrator and NVDA said
/// nothing as the arrows walked Vesper's and Junction's lists, a menu bar's menu, a context menu and its
/// submenu (2026-11-15). As a WPF list box's item has the keyboard focus, the reader's focus is now the
/// current item of the container that holds the focus (<see cref="AccessibilityNode.CurrentItem"/>):
/// UIA's GetFocus answers it, it answers HasKeyboardFocus (and the container does not), and each move
/// raises the focus event on it, after its selected state.
/// </summary>
public class A11yCurrentItemFocusTests
{
    private const int HasKeyboardFocus = UiaInterop.UIA_HasKeyboardFocusPropertyId;
    private const int IsKeyboardFocusable = UiaInterop.UIA_IsKeyboardFocusablePropertyId;
    private const int IsSelected = UiaInterop.UIA_SelectionItemIsSelectedPropertyId;
    private const int FocusChanged = UiaInterop.UIA_AutomationFocusChangedEventId;

    /// <summary>Forwards everything to a real bridge, reported active whether or not a client listens.</summary>
    private sealed class Active(IAccessibilityBridge inner) : IAccessibilityBridge, IEagerItemsLimit
    {
        // The real bridge's number of items past which a list's are served on demand (UIA 200, AT-SPI 2,000).
        public int MaxEagerVirtualItems => AccessibilityTreeBuilder.MaxEagerVirtualItemsOf(inner);
        public bool IsActive => true;
        public void ApplyChanges(TreeDiff diff, AccessibilityNode root) => inner.ApplyChanges(diff, root);
        public void ApplyPatchedProperties(TreeDiff patch, AccessibilityNode root) => inner.ApplyPatchedProperties(patch, root);
        public void NotifyFocusChanged(AccessibilityNode? focused) => inner.NotifyFocusChanged(focused);
        public void NotifyFocusChanged(AccessibilityNode? focused, AccessibilityNode? previous) => inner.NotifyFocusChanged(focused, previous);
        public void NotifyChangesApplied(AccessibilityNode root) => inner.NotifyChangesApplied(root);
        public void NotifyWindowActivated() => inner.NotifyWindowActivated();
        public void Announce(AccessibilityNode? source, string text, AnnouncementPoliteness politeness) =>
            inner.Announce(source, text, politeness);
    }

    [SupportedOSPlatform("windows")]
    private sealed class UiaRig : IDisposable
    {
        private readonly TestHwnd _hwnd = TestHwnd.Create();
        public HeadlessSession Session { get; }
        public WindowsAccessibilityBridge Bridge { get; }
        public readonly List<UiaRaised> Raised = [];

        public UiaRig(int width = 600, int height = 500)
        {
            Session = HeadlessSession.Create(width, height);
            var window = Session.Window.Window;
            var router = new AccessibilityActionRouter(window);
            var host = new UiaHostBridge(window, router);
            window.SetUiaHostBridge(host);
            window.SetActionRouter(router);
            Bridge = new WindowsAccessibilityBridge(_hwnd.Handle, host);
            Bridge.Raised = Raised.Add;
            Session.Window.InstallAccessibilityBridge(new Active(Bridge));
        }

        /// <summary>What UIA's GetFocus on the window's root answers: a node's id, or an item's a list
        /// serves on demand; null for none.</summary>
        public ElementId? GetFocus()
        {
            var root = Bridge.NativeRootFragmentRoot();
            try
            {
                var logic = Assert.IsType<UiaFragmentRoot>(NativeProvider.LogicOf(root));
                Assert.Equal(0, logic.GetFocus(out var focus));
                if (focus == IntPtr.Zero) return null;
                try
                {
                    return NativeProvider.LogicOf(focus) switch
                    {
                        UiaFragment f => f.ElementId,
                        UiaVirtualItem v => VirtualItemId.For(v.ListId, v.Index),
                        var other => throw new InvalidOperationException($"GetFocus answered {other}"),
                    };
                }
                finally { Marshal.Release(focus); }
            }
            finally { Marshal.Release(root); }
        }

        /// <summary>What the provider of <paramref name="id"/> answers for a boolean property: a node's
        /// wrapper, or a provider of an item served on demand made as navigation makes one.</summary>
        public bool Answer(ElementId id, int property)
        {
            IntPtr native = VirtualItemId.TryDecode(id, out var listId, out int index) && Node(listId) is { IsVirtualItemContainer: true }
                ? Bridge.NativeVirtualItem(listId, index)
                : Bridge.NativeFragment(id);
            Assert.NotEqual(IntPtr.Zero, native);
            try
            {
                var simple = (INativeSimple)NativeProvider.LogicOf(native)!;
                Assert.Equal(0, simple.GetPropertyValue(property, out var value));
                Assert.Equal(UiaVariant.VT_BOOL, value.Vt);
                return value.ShortValue != 0;
            }
            finally { Marshal.Release(native); }
        }

        public bool HasFocus(ElementId id) => Answer(id, HasKeyboardFocus);
        public bool HasFocus(Element element) => HasFocus(element.Id);

        public AccessibilityNode? Node(ElementId id) => Find(Bridge.RootNode!, id);

        /// <summary>The node of the open menu at <paramref name="level"/> (0 the root menu, 1 its flyout).</summary>
        public AccessibilityNode Menu(int level = 0) =>
            Walk(Bridge.RootNode!).Where(n => n.Role == AccessibilityRole.Menu).ElementAt(level);

        /// <summary>
        /// What UI Automation's Parent steps from the reader's focus reach, up to the window's root: each
        /// provider's element (the root as null), and its native object, which a client keeps as the
        /// element's identity with its runtime id (made from the element).
        /// </summary>
        public List<(ElementId? Element, IntPtr Native)> FocusAncestors()
        {
            var chain = new List<(ElementId?, IntPtr)>();
            var root = Bridge.NativeRootFragmentRoot();
            Assert.Equal(0, ((UiaFragmentRoot)NativeProvider.LogicOf(root)!).GetFocus(out var at));
            Marshal.Release(root);
            while (at != IntPtr.Zero)
            {
                var logic = NativeProvider.LogicOf(at);
                Assert.Equal(0, ((INativeFragment)logic!).Navigate(UiaInterop.NavigateDirection_Parent, out var up));
                if (up != IntPtr.Zero)
                    chain.Add((NativeProvider.LogicOf(up) switch { UiaFragment f => f.ElementId, _ => null }, up));
                Marshal.Release(at);
                at = up;
            }
            return chain;
        }

        /// <summary>The elements the focus events went to, in order.</summary>
        public List<ElementId?> FocusEvents() =>
            Raised.Where(e => e.Kind == UiaRaisedKind.Event && e.Id == FocusChanged).Select(e => e.Element).ToList();

        public void Dispose()
        {
            Bridge.Dispose();
            Session.Dispose();
            _hwnd.Dispose();
        }
    }

    private static IEnumerable<AccessibilityNode> Walk(AccessibilityNode node)
    {
        yield return node;
        foreach (var child in node.Children)
            foreach (var n in Walk(child)) yield return n;
    }

    private static AccessibilityNode? Find(AccessibilityNode node, ElementId id) => Walk(node).FirstOrDefault(n => n.ElementId == id);

    private static ElementId Item(Element list, int index) => VirtualItemId.For(list.Id, index);

    private static UiaRaised Prop(int property, ElementId id) => new(UiaRaisedKind.Property, property, id);
    private static UiaRaised Focus(ElementId id) => new(UiaRaisedKind.Event, FocusChanged, id);

    /// <summary>A list of <paramref name="count"/> items that holds the focus itself (as Meridian's
    /// History, and Junction's since this change), between two buttons.</summary>
    private static (ButtonElement Before, VirtualListElement<string> List, ButtonElement After, Element Root) FocusableList(
        int count, int selected)
    {
        var items = Enumerable.Range(0, count).Select(i => $"file {i}").ToList();
        var list = VirtualListElement<string>.New().ItemHeight(20)
            .RenderWith(t => Flex.Row().Children(TextB.New(t).FontSize(12f)))
            .Items(items)
            .SelectedIndex(selected)
            .FlexGrow(1);
        list.IsFocusable = true;
        list.AccessibleName = "Files";
        var before = Button.New("Before");
        var after = Button.New("After");
        return (before, list, after, Flex.Column().Children(before, list, after));
    }

    private static void Settle(HeadlessSession session)
    {
        session.PumpFrames(2);
        session.PumpFrame(TimeSpan.FromMilliseconds(150));
        session.PumpFrame(TimeSpan.FromMilliseconds(150));
    }

    // ── A list of item nodes ──

    [WindowsFact]
    public void AListOfItemNodes_TheFocusEntersOnItsCurrentItem_EachMoveIsAFocusMove_AndLeavingTakesItAway()
    {
        using var rig = new UiaRig();
        var (before, list, after, root) = FocusableList(40, selected: 3);
        rig.Session.Window.SetRoot(root);
        Settle(rig.Session);
        before.Focus();
        rig.Session.PumpFrame();
        rig.Raised.Clear();

        // Tab into the list: the reader is told its current item has the focus, not the list.
        rig.Session.Window.PressKey(KeyCode.Tab);
        rig.Session.PumpFrame();
        Assert.Same(list, rig.Session.Window.Window.FocusedElement);
        Assert.Equal(
        [
            Prop(HasKeyboardFocus, before.Id),
            Prop(HasKeyboardFocus, Item(list, 3)),
            Focus(Item(list, 3)),
        ], rig.Raised);
        Assert.Equal(Item(list, 3), rig.GetFocus());
        Assert.True(rig.HasFocus(Item(list, 3)));
        Assert.True(rig.Answer(Item(list, 3), IsKeyboardFocusable));
        Assert.False(rig.HasFocus(list));
        Assert.False(rig.HasFocus(before));
        // The list is no focus stop of its own while its item stands for it, as Vesper's file list (whose
        // pane holds the focus) is not: Narrator said "History list" again on every move in a focusable
        // list, and nothing more in Vesper's.
        Assert.False(rig.Answer(list.Id, IsKeyboardFocusable));

        // A key moves the selection: told as the items' selected state, then the focus on the new one,
        // with no tree built.
        int trees = rig.Session.Window.AccessibilityTreesBuilt;
        rig.Raised.Clear();
        list.SelectedIndex = 4;
        rig.Session.PumpFrame();
        Assert.Equal(trees, rig.Session.Window.AccessibilityTreesBuilt);
        Assert.Equal(
        [
            Prop(IsSelected, Item(list, 3)),
            Prop(IsSelected, Item(list, 4)),
            Prop(HasKeyboardFocus, Item(list, 3)),
            Prop(HasKeyboardFocus, Item(list, 4)),
            Focus(Item(list, 4)),
        ], rig.Raised);
        Assert.Equal(Item(list, 4), rig.GetFocus());
        Assert.False(rig.HasFocus(Item(list, 3)));
        Assert.True(rig.HasFocus(Item(list, 4)));
        Assert.Equal(4, rig.Node(list.Id)!.CurrentItem);

        // A jump (End) is one move as well.
        rig.Raised.Clear();
        list.SelectedIndex = 39;
        rig.Session.PumpFrame();
        Assert.Equal([Item(list, 39)], rig.FocusEvents());
        Assert.Equal(Item(list, 39), rig.GetFocus());

        // A rebuild for another change tells no move.
        rig.Raised.Clear();
        after.AccessibleName = "After, renamed";
        rig.Session.PumpFrame();
        Assert.Empty(rig.FocusEvents());
        Assert.DoesNotContain(rig.Raised, e => e.Id == HasKeyboardFocus);
        Assert.Equal(Item(list, 39), rig.GetFocus());

        // Leaving: the item lets go of the focus.
        rig.Raised.Clear();
        rig.Session.Window.PressKey(KeyCode.Tab);
        rig.Session.PumpFrame();
        Assert.Same(after, rig.Session.Window.Window.FocusedElement);
        Assert.Equal(
        [
            Prop(HasKeyboardFocus, Item(list, 39)),
            Prop(HasKeyboardFocus, after.Id),
            Focus(after.Id),
        ], rig.Raised);
        Assert.False(rig.HasFocus(Item(list, 39)));
        Assert.Equal(after.Id, rig.GetFocus());

        // A move of the selection while the list does not hold the focus is no focus move.
        rig.Raised.Clear();
        list.SelectedIndex = 2;
        rig.Session.PumpFrame();
        Assert.Contains(Prop(IsSelected, Item(list, 2)), rig.Raised);
        Assert.Empty(rig.FocusEvents());
        Assert.Equal(after.Id, rig.GetFocus());
    }

    [WindowsFact]
    public void AListWithNoCurrentItem_HoldsTheFocusItself_UntilOneIsSelected()
    {
        using var rig = new UiaRig();
        var (before, list, _, root) = FocusableList(10, selected: -1);
        rig.Session.Window.SetRoot(root);
        Settle(rig.Session);
        before.Focus();
        rig.Session.PumpFrame();
        rig.Raised.Clear();

        rig.Session.Window.PressKey(KeyCode.Tab);
        rig.Session.PumpFrame();
        Assert.Equal([list.Id], rig.FocusEvents());
        Assert.Equal(list.Id, rig.GetFocus());
        Assert.True(rig.HasFocus(list));
        Assert.True(rig.Answer(list.Id, IsKeyboardFocusable));

        rig.Raised.Clear();
        list.SelectedIndex = 0;
        rig.Session.PumpFrame();
        Assert.Equal(
        [
            Prop(IsSelected, Item(list, 0)),
            Prop(HasKeyboardFocus, list.Id),
            Prop(HasKeyboardFocus, Item(list, 0)),
            Focus(Item(list, 0)),
        ], rig.Raised);
        Assert.False(rig.HasFocus(list));

        // Nothing selected again: back on the list.
        rig.Raised.Clear();
        list.SelectedIndex = -1;
        rig.Session.PumpFrame();
        Assert.Equal([list.Id], rig.FocusEvents());
        Assert.True(rig.HasFocus(list));
        Assert.False(rig.HasFocus(Item(list, 0)));
    }

    // ── A list served on demand ──

    [WindowsFact]
    public void AListServedOnDemand_TheFocusIsOnItsServedItem_AndMovesWithTheSelection()
    {
        using var rig = new UiaRig();
        int count = WindowsAccessibilityBridge.MaxEagerVirtualItems + 1_000;   // served on demand on UIA
        var (before, list, _, root) = FocusableList(count, selected: 1_000);
        rig.Session.Window.SetRoot(root);
        Settle(rig.Session);
        Assert.True(rig.Node(list.Id)!.IsVirtualItemContainer);
        before.Focus();
        rig.Session.PumpFrame();
        rig.Raised.Clear();

        rig.Session.Window.PressKey(KeyCode.Tab);
        rig.Session.PumpFrame();
        Assert.Equal(
        [
            Prop(HasKeyboardFocus, before.Id),
            Prop(HasKeyboardFocus, Item(list, 1_000)),
            Focus(Item(list, 1_000)),
        ], rig.Raised);
        Assert.Equal(Item(list, 1_000), rig.GetFocus());
        Assert.True(rig.HasFocus(Item(list, 1_000)));
        Assert.True(rig.Answer(Item(list, 1_000), IsKeyboardFocusable));
        Assert.False(rig.HasFocus(Item(list, 1_001)));
        Assert.False(rig.HasFocus(list));

        int trees = rig.Session.Window.AccessibilityTreesBuilt;
        rig.Raised.Clear();
        list.SelectedIndex = 1_001;
        rig.Session.PumpFrame();
        Assert.Equal(trees, rig.Session.Window.AccessibilityTreesBuilt);
        Assert.Equal(
        [
            Prop(IsSelected, Item(list, 1_000)),
            Prop(IsSelected, Item(list, 1_001)),
            Prop(HasKeyboardFocus, Item(list, 1_000)),
            Prop(HasKeyboardFocus, Item(list, 1_001)),
            Focus(Item(list, 1_001)),
        ], rig.Raised);
        Assert.Equal(Item(list, 1_001), rig.GetFocus());
        Assert.True(rig.HasFocus(Item(list, 1_001)));

        // Shift+Tab out of it.
        rig.Raised.Clear();
        rig.Session.Window.PressKey(KeyCode.Tab, KeyModifiers.Shift);
        rig.Session.PumpFrame();
        Assert.Equal([before.Id], rig.FocusEvents());
        Assert.False(rig.HasFocus(Item(list, 1_001)));
    }

    // ── What a reader hears when a list's contents change under its focus (plan 17 round 16) ──

    /// <summary>The name a client reads from <paramref name="id"/>'s provider: a node's wrapper, or a
    /// provider of an item served on demand made as navigation makes one.</summary>
    private static string? NameOf(UiaRig rig, ElementId id)
    {
        IntPtr native = VirtualItemId.TryDecode(id, out var listId, out int index) && rig.Node(listId) is { IsVirtualItemContainer: true }
            ? rig.Bridge.NativeVirtualItem(listId, index)
            : rig.Bridge.NativeFragment(id);
        Assert.NotEqual(IntPtr.Zero, native);
        try
        {
            Assert.Equal(0, ((INativeSimple)NativeProvider.LogicOf(native)!).GetPropertyValue(UiaInterop.UIA_NamePropertyId, out var value));
            try { return value.AsString(); }
            finally { value.Free(); }
        }
        finally { Marshal.Release(native); }
    }

    private static List<ElementId?> NameChanges(UiaRig rig) =>
        rig.Raised.Where(e => e.Kind == UiaRaisedKind.Property && e.Id == UiaInterop.UIA_NamePropertyId).Select(e => e.Element).ToList();

    /// <summary>
    /// Junction's History when a commit lands, with the reader on its first row: the list's contents
    /// change under the reader's focus, which stays on the same place. An item node's rename is told as a
    /// Name change on it; an item served on demand has no node, and told nothing until the bridge read its
    /// name again at the end of the frame. Both lists tell one Name change, on the focused item, and no focus
    /// event; a change elsewhere in the list tells the served one nothing.
    /// </summary>
    [WindowsTheory]
    [InlineData(150)]
    [InlineData(1_000)]
    public void AListsItemRenamedUnderTheReadersFocus_IsToldAsANameChange_ServedOrNot(int count)
    {
        using var rig = new UiaRig();
        var (before, list, _, root) = FocusableList(count, selected: 0);
        rig.Session.Window.SetRoot(root);
        Settle(rig.Session);
        bool served = count > WindowsAccessibilityBridge.MaxEagerVirtualItems;
        Assert.Equal(served, rig.Node(list.Id)!.IsVirtualItemContainer);
        before.Focus();
        rig.Session.PumpFrame();
        rig.Session.Window.PressKey(KeyCode.Tab);
        Settle(rig.Session);
        Assert.Equal(Item(list, 0), rig.GetFocus());
        Assert.Equal("file 0", NameOf(rig, Item(list, 0)));

        // A commit lands on row 0: one item more, the selection where it was.
        rig.Raised.Clear();
        list.Items = ["new 0", .. Enumerable.Range(0, count).Select(i => $"file {i}")];
        Settle(rig.Session);
        Assert.Equal(0, list.SelectedIndex);
        // The focused item's alone. Item nodes are each renamed, as an item's id is its place, but one no
        // client was given tells none (plan 17 round 19 step 10; before, each item node told its own).
        var renamed = NameChanges(rig);
        Assert.Equal([Item(list, 0)], renamed);
        Assert.Empty(rig.FocusEvents());
        Assert.Equal(Item(list, 0), rig.GetFocus());
        Assert.True(rig.HasFocus(Item(list, 0)));
        Assert.Equal("new 0", NameOf(rig, Item(list, 0)));

        // Relabelled in place, as Vesper's list is when a folder is entered: the same number of items.
        rig.Raised.Clear();
        list.Items = list.Items.Select(s => "other " + s).ToList();
        Settle(rig.Session);
        Assert.Equal([Item(list, 0)], NameChanges(rig));
        Assert.Empty(rig.FocusEvents());
        Assert.Equal("other new 0", NameOf(rig, Item(list, 0)));

        // Nothing of an item away from the focus that no client was given, served or an item node (since
        // plan 17 round 19 step 10): a client reads its name when it asks.
        rig.Raised.Clear();
        list.Items = list.Items.Select((s, i) => i == 50 ? "changed" : s).ToList();
        Settle(rig.Session);
        Assert.Empty(NameChanges(rig));
        Assert.Empty(rig.FocusEvents());

        // The focus moves as the items change: the focus event carries the name, and the item the focus
        // leaves is not renamed to a reader.
        rig.Raised.Clear();
        list.Items = list.Items.Select(s => "again " + s).ToList();
        list.SelectedIndex = 1;
        Settle(rig.Session);
        Assert.Equal([Item(list, 1)], rig.FocusEvents());
        Assert.Empty(NameChanges(rig));
        Assert.Equal("again other file 0", NameOf(rig, Item(list, 1)));
    }

    /// <summary>
    /// The bridge reads the name of the reader's focus at the end of every frame that applied something,
    /// on the UI thread, where a list on item rows keeps the labels it is asked for in an array as long as
    /// the list: 480 KB for 20,000 items, made again with every new list (Junction's History, 24 KB for its
    /// 1,000 commits, at every switch back to it). It reads them without keeping them
    /// (<see cref="IAccessibleItemSource.PeekItemLabel"/>): a move costs what a move cost.
    /// </summary>
    [WindowsFact]
    public void TheReadersFocusOnAServedItem_KeepsNoLabelForEachItem()
    {
        using var rig = new UiaRig();
        var list = VirtualListElement<string>.New().ItemHeight(20)
            .RenderWith((t, _) => Flex.Row().Children(TextB.New(t).FontSize(12f)))
            .Items(Enumerable.Range(0, 20_000).Select(i => $"file {i}").ToList())
            .AccessibleItemLabel(s => s)
            .SelectedIndex(0)
            .FlexGrow(1);
        list.IsFocusable = true;
        var before = Button.New("Before");
        rig.Session.Window.SetRoot(Flex.Column().Children(before, list));
        Settle(rig.Session);
        before.Focus();
        rig.Session.PumpFrame();
        long bytes = GC.GetAllocatedBytesForCurrentThread();
        rig.Session.Window.PressKey(KeyCode.Tab);
        rig.Session.PumpFrame();
        for (int i = 1; i <= 3; i++)
        {
            list.SelectedIndex = i;
            rig.Session.PumpFrame();
        }
        bytes = GC.GetAllocatedBytesForCurrentThread() - bytes;
        Assert.Equal([Item(list, 0), Item(list, 1), Item(list, 2), Item(list, 3)], rig.FocusEvents().TakeLast(4));
        Assert.True(bytes < 128 * 1024, $"{bytes / 1024} KB for the focus and 3 moves");
    }

    /// <summary>
    /// A list whose items cross UI Automation's number (Vesper's move between folders of 150 and 250
    /// files) has its tree built whole: item nodes on one side, items served on demand on the other. The
    /// reader's focus stays on its current item, by the same id, on both sides; a rename of that item at
    /// the crossing is told once, though no diff tells it (the item's node is new, or gone).
    /// </summary>
    [WindowsFact]
    public void AFocusedListCrossingUiasNumber_KeepsTheFocusOnItsItem_AndTellsItsRename()
    {
        using var rig = new UiaRig();
        int under = WindowsAccessibilityBridge.MaxEagerVirtualItems - 50, over = WindowsAccessibilityBridge.MaxEagerVirtualItems + 50;
        var (before, list, _, root) = FocusableList(under, selected: 3);
        rig.Session.Window.SetRoot(root);
        Settle(rig.Session);
        before.Focus();
        rig.Session.PumpFrame();
        rig.Session.Window.PressKey(KeyCode.Tab);
        Settle(rig.Session);
        Assert.Equal(under, rig.Node(list.Id)!.Children.Count);
        Assert.Equal(Item(list, 3), rig.GetFocus());

        // Over it, the same names: served on demand, the focus on the same item, nothing told of it.
        int trees = rig.Session.Window.AccessibilityTreesBuilt;
        rig.Raised.Clear();
        list.Items = Enumerable.Range(0, over).Select(i => $"file {i}").ToList();
        Settle(rig.Session);
        Assert.True(rig.Session.Window.AccessibilityTreesBuilt > trees);
        var node = rig.Node(list.Id)!;
        Assert.True(node.IsVirtualItemContainer);
        Assert.Empty(node.Children);
        Assert.Equal(over, node.Served.Count);
        Assert.Equal(Item(list, 3), rig.GetFocus());
        Assert.True(rig.HasFocus(Item(list, 3)));
        Assert.False(rig.HasFocus(list));
        Assert.Empty(rig.FocusEvents());
        Assert.Empty(NameChanges(rig));
        Assert.Equal("file 3", NameOf(rig, Item(list, 3)));

        // Under it again, renamed: item nodes, and the focused one's new name told once.
        rig.Raised.Clear();
        list.Items = Enumerable.Range(0, under).Select(i => $"b {i}").ToList();
        Settle(rig.Session);
        node = rig.Node(list.Id)!;
        Assert.False(node.IsVirtualItemContainer);
        Assert.Equal(under, node.Children.Count);
        Assert.Equal(Item(list, 3), rig.GetFocus());
        Assert.True(rig.HasFocus(Item(list, 3)));
        Assert.Empty(rig.FocusEvents());
        Assert.Equal([Item(list, 3)], NameChanges(rig));
        Assert.Equal("b 3", NameOf(rig, Item(list, 3)));

        // Over it again, renamed: served, told once.
        rig.Raised.Clear();
        list.Items = Enumerable.Range(0, over).Select(i => $"c {i}").ToList();
        Settle(rig.Session);
        Assert.True(rig.Node(list.Id)!.IsVirtualItemContainer);
        Assert.Equal(Item(list, 3), rig.GetFocus());
        Assert.Empty(rig.FocusEvents());
        Assert.Equal([Item(list, 3)], NameChanges(rig));
        Assert.Equal("c 3", NameOf(rig, Item(list, 3)));
    }

    // ── A multi-select list's cursor ──

    [WindowsFact]
    public void AMultiSelectList_TheFocusFollowsItsCursor_SelectedOrNot()
    {
        using var rig = new UiaRig();
        var (before, list, _, root) = FocusableList(10, selected: 0);
        list.CanSelectMultiple = true;
        rig.Session.Window.SetRoot(root);
        Settle(rig.Session);
        var source = (IAccessibleItemSource)list;
        source.SetItemSelected(0, true);
        source.SetItemSelected(5, true);
        rig.Session.PumpFrame();
        before.Focus();
        rig.Session.PumpFrame();
        rig.Session.Window.PressKey(KeyCode.Tab);
        rig.Session.PumpFrame();
        Assert.Equal(Item(list, 0), rig.GetFocus());

        // The cursor moves onto an item that is not selected: the selection is as it was, and the
        // reader is told the focus moved, with no tree built and nothing selected told.
        int trees = rig.Session.Window.AccessibilityTreesBuilt;
        rig.Raised.Clear();
        list.SelectedIndex = 3;
        rig.Session.PumpFrame();
        Assert.Equal(trees, rig.Session.Window.AccessibilityTreesBuilt);
        Assert.Equal(
        [
            Prop(HasKeyboardFocus, Item(list, 0)),
            Prop(HasKeyboardFocus, Item(list, 3)),
            Focus(Item(list, 3)),
        ], rig.Raised);
        Assert.Equal(Item(list, 3), rig.GetFocus());
        Assert.False(rig.Answer(Item(list, 3), IsSelected));
        Assert.True(rig.Answer(Item(list, 5), IsSelected));

        // And onto a selected one.
        rig.Raised.Clear();
        list.SelectedIndex = 5;
        rig.Session.PumpFrame();
        Assert.Equal([Item(list, 5)], rig.FocusEvents());
        Assert.True(rig.HasFocus(Item(list, 5)));
    }

    // ── A list that takes its pane's focus ──

    /// <summary>A focusable, named pane with a header and a list inside, as Vesper's pane holds the
    /// focus and walks its file list's cursor.</summary>
    private static (ButtonElement Before, BoxElement Pane, VirtualListElement<string> List, Element Root) Pane(bool takesFocus)
    {
        var items = Enumerable.Range(0, 30).Select(i => $"f{i:000}.txt").ToList();
        var list = VirtualListElement<string>.New().ItemHeight(20)
            .RenderWith(t => Flex.Row().Children(TextB.New(t).FontSize(12f)))
            .Items(items)
            .SelectedIndex(2)
            .TakesAncestorFocus(takesFocus)
            .FlexGrow(1);
        var pane = Flex.Column().FlexGrow(1).Children(TextB.New("Name  Size  Date"), list);
        pane.IsFocusable = true;
        pane.Role = AccessibilityRole.Group;
        pane.AccessibleName = "Left pane, Downloads";
        var before = Button.New("Before");
        return (before, pane, list, Flex.Column().Children(before, pane));
    }

    private static (BoxElement Pane, VirtualListElement<string> List) PaneOf(string name)
    {
        var list = VirtualListElement<string>.New().ItemHeight(20)
            .RenderWith(t => Flex.Row().Children(TextB.New(t).FontSize(12f)))
            .Items(Enumerable.Range(0, 30).Select(i => $"f{i:000}.txt").ToList())
            .SelectedIndex(2)
            .TakesAncestorFocus()
            .FlexGrow(1);
        var pane = Flex.Column().FlexGrow(1).Children(TextB.New("Name  Size  Date"), list);
        pane.IsFocusable = true;
        pane.Role = AccessibilityRole.Group;
        pane.AccessibleName = name;
        return (pane, list);
    }

    [WindowsFact]
    public void AListThatTakesItsPanesFocus_IsTheReadersFocusWhileThePaneHoldsIt()
    {
        using var rig = new UiaRig();
        var (before, pane, list, root) = Pane(takesFocus: true);
        rig.Session.Window.SetRoot(root);
        Settle(rig.Session);
        before.Focus();
        rig.Session.PumpFrame();
        rig.Raised.Clear();

        rig.Session.Window.PressKey(KeyCode.Tab);
        rig.Session.PumpFrame();
        Assert.Same(pane, rig.Session.Window.Window.FocusedElement);
        Assert.Equal([Item(list, 2)], rig.FocusEvents());
        Assert.Equal(Item(list, 2), rig.GetFocus());
        Assert.False(rig.HasFocus(pane));

        rig.Raised.Clear();
        list.SelectedIndex = 3;
        rig.Session.PumpFrame();
        Assert.Equal([Item(list, 3)], rig.FocusEvents());
        Assert.True(rig.HasFocus(Item(list, 3)));

        rig.Raised.Clear();
        rig.Session.Window.PressKey(KeyCode.Tab, KeyModifiers.Shift);
        rig.Session.PumpFrame();
        Assert.Equal([before.Id], rig.FocusEvents());
        Assert.False(rig.HasFocus(Item(list, 3)));
    }

    /// <summary>
    /// NVDA said the window again on a Tab between Vesper's panes, and Narrator "History list" on every
    /// move in Junction's History: a reader speaks the ancestors that differ from the last focus's. The
    /// ancestors UI Automation reaches from the reader's focus are the same providers, element for
    /// element, before and after a move, in a list that holds the focus and in one that takes its pane's,
    /// and above the panes across a Tab from one pane to the other.
    /// </summary>
    [WindowsFact]
    public void TheFocussAncestors_AreTheSameProviders_AcrossAMove_AndAboveThePanesAcrossATab()
    {
        using var rig = new UiaRig();
        var (before, list, _, root) = FocusableList(30, selected: 3);
        var (left, leftList) = PaneOf("Left pane, Downloads");
        var (right, rightList) = PaneOf("Right pane, Documents");
        rig.Session.Window.SetRoot(Flex.Column().Children(root, Flex.Row().FlexGrow(1).Children(left, right)));
        Settle(rig.Session);

        list.Focus();
        rig.Session.PumpFrame();
        var was = rig.FocusAncestors();
        Assert.Equal(list.Id, was[0].Element);
        list.SelectedIndex = 4;
        rig.Session.PumpFrame();
        Assert.Equal(Item(list, 4), rig.GetFocus());
        Assert.Equal(was, rig.FocusAncestors());

        left.Focus();
        rig.Session.PumpFrame();
        was = rig.FocusAncestors();
        Assert.Equal([leftList.Id, left.Id], was.Take(2).Select(a => a.Element));
        leftList.SelectedIndex = 5;
        rig.Session.PumpFrame();
        Assert.Equal(Item(leftList, 5), rig.GetFocus());
        Assert.Equal(was, rig.FocusAncestors());

        rig.Session.Window.PressKey(KeyCode.Tab);
        rig.Session.PumpFrame();
        Assert.Same(right, rig.Session.Window.Window.FocusedElement);
        var now = rig.FocusAncestors();
        Assert.Equal([rightList.Id, right.Id], now.Take(2).Select(a => a.Element));
        // Above the panes: the same providers.
        Assert.Equal(was.Skip(2), now.Skip(2));
    }

    [WindowsFact]
    public void AListThatDoesNotTakeItsPanesFocus_LeavesItOnThePane()
    {
        using var rig = new UiaRig();
        var (before, pane, list, root) = Pane(takesFocus: false);
        rig.Session.Window.SetRoot(root);
        Settle(rig.Session);
        before.Focus();
        rig.Session.PumpFrame();
        rig.Raised.Clear();

        rig.Session.Window.PressKey(KeyCode.Tab);
        rig.Session.PumpFrame();
        Assert.Equal([pane.Id], rig.FocusEvents());
        rig.Raised.Clear();
        list.SelectedIndex = 3;
        rig.Session.PumpFrame();
        Assert.Empty(rig.FocusEvents());
        Assert.Equal(pane.Id, rig.GetFocus());
    }

    // ── A context menu ──

    private static (ButtonElement Opener, Element Root) Opener()
    {
        var opener = Button.New("Commit actions");
        return (opener, Flex.Column().Children(opener, TextB.New("History")));
    }

    private static ContextMenuItem Entry(string label) => new() { Label = label, Action = () => { } };

    [WindowsFact]
    public void AContextMenu_HoldsTheReadersFocusWhileOpen_OnItsHighlightedEntry_AndGivesItBack()
    {
        using var rig = new UiaRig();
        var (opener, root) = Opener();
        rig.Session.Window.SetRoot(root);
        Settle(rig.Session);
        opener.Focus();
        rig.Session.PumpFrame();
        rig.Raised.Clear();

        // Opened from the keyboard, on its first entry.
        rig.Session.Window.Window.OpenMenu([Entry("Copy hash"), Entry("Cherry-pick"), ContextMenuItem.Separator(), Entry("Revert")],
            new Point(20, 40), new MenuRequest { HighlightFirst = true });
        rig.Session.PumpFrame();
        Assert.Same(opener, rig.Session.Window.Window.FocusedElement);
        var menu = rig.Menu();
        Assert.Equal([Item(menu, 0)], rig.FocusEvents());
        Assert.Equal(Item(menu, 0), rig.GetFocus());
        Assert.True(rig.HasFocus(Item(menu, 0)));
        Assert.False(rig.HasFocus(opener));

        // Down, Down (past the separator), Up.
        rig.Raised.Clear();
        rig.Session.Window.PressKey(KeyCode.Down);
        rig.Session.PumpFrame();
        // A menu's entry is no SelectionItem: the move is the focus's alone.
        Assert.Equal(
        [
            Prop(HasKeyboardFocus, Item(menu, 0)),
            Prop(HasKeyboardFocus, Item(menu, 1)),
            Focus(Item(menu, 1)),
        ], rig.Raised);
        rig.Raised.Clear();
        rig.Session.Window.PressKey(KeyCode.Down);
        rig.Session.PumpFrame();
        Assert.Equal([Item(menu, 2)], rig.FocusEvents());
        Assert.Equal("Revert", rig.Node(Item(menu, 2))!.Name);
        rig.Raised.Clear();
        rig.Session.Window.PressKey(KeyCode.Up);
        rig.Session.PumpFrame();
        Assert.Equal([Item(menu, 1)], rig.FocusEvents());
        Assert.Equal(Item(menu, 1), rig.GetFocus());

        // Escape: the reader is back on what had the focus.
        rig.Raised.Clear();
        rig.Session.Window.PressKey(KeyCode.Escape);
        rig.Session.PumpFrame();
        Assert.Equal([opener.Id], rig.FocusEvents());
        Assert.Equal(opener.Id, rig.GetFocus());
        Assert.True(rig.HasFocus(opener));
    }

    [WindowsFact]
    public void AContextMenuOpenedWithNothingHighlighted_IsTheReadersFocusItself_UntilAKeyHighlightsAnEntry()
    {
        using var rig = new UiaRig();
        var (opener, root) = Opener();
        rig.Session.Window.SetRoot(root);
        Settle(rig.Session);
        opener.Focus();
        rig.Session.PumpFrame();
        rig.Raised.Clear();

        // A right-click's menu: nothing highlighted.
        rig.Session.Window.Window.OpenContextMenu([Entry("Copy"), Entry("Paste")], new Point(20, 40));
        rig.Session.PumpFrame();
        var menu = rig.Menu();
        Assert.Equal([menu.ElementId], rig.FocusEvents());
        Assert.Equal(menu.ElementId, rig.GetFocus());

        rig.Raised.Clear();
        rig.Session.Window.PressKey(KeyCode.Down);
        rig.Session.PumpFrame();
        Assert.Equal([Item(menu, 0)], rig.FocusEvents());
        Assert.Equal(Item(menu, 0), rig.GetFocus());
    }

    private static ElementId Item(AccessibilityNode list, int index) => VirtualItemId.For(list.ElementId, index);

    [WindowsFact]
    public void ASubmenu_RightTakesTheFocusToItsFirstEntry_AndLeftBackToItsParent()
    {
        using var rig = new UiaRig();
        var (opener, root) = Opener();
        rig.Session.Window.SetRoot(root);
        Settle(rig.Session);
        opener.Focus();
        rig.Session.PumpFrame();

        rig.Session.Window.Window.OpenMenu(
            [Entry("Copy hash"), new ContextMenuItem { Label = "Quick rebase", Children = [Entry("Squash"), Entry("Drop")] }, Entry("Revert")],
            new Point(20, 40), new MenuRequest { HighlightFirst = true });
        rig.Session.PumpFrame();
        var menu = rig.Menu();
        rig.Session.Window.PressKey(KeyCode.Down);
        rig.Session.PumpFrame();
        Assert.Equal(Item(menu, 1), rig.GetFocus());

        // → opens the flyout on its first entry: the reader goes with it.
        rig.Raised.Clear();
        rig.Session.Window.PressKey(KeyCode.Right);
        rig.Session.PumpFrame();
        var flyout = rig.Menu(level: 1);
        Assert.Equal([Item(flyout, 0)], rig.FocusEvents());
        Assert.Equal(Item(flyout, 0), rig.GetFocus());
        Assert.Equal("Squash", rig.Node(Item(flyout, 0))!.Name);
        Assert.Equal(true, rig.Node(Item(menu, 1))!.Expanded);

        rig.Raised.Clear();
        rig.Session.Window.PressKey(KeyCode.Down);
        rig.Session.PumpFrame();
        Assert.Equal([Item(flyout, 1)], rig.FocusEvents());

        // ← closes it: back on its parent entry, told collapsed before the focus comes back to it.
        rig.Raised.Clear();
        rig.Session.Window.PressKey(KeyCode.Left);
        rig.Session.PumpFrame();
        Assert.Equal([Item(menu, 1)], rig.FocusEvents());
        Assert.Equal(Item(menu, 1), rig.GetFocus());
        Assert.Equal(false, rig.Node(Item(menu, 1))!.Expanded);
        int collapsed = rig.Raised.FindIndex(e => e.Id == UiaInterop.UIA_ExpandCollapseExpandCollapseStatePropertyId);
        Assert.InRange(collapsed, 0, rig.Raised.FindIndex(e => e.Id == FocusChanged));
    }

    // ── A menu bar's titles ──

    private sealed class MenuBarFixture : IDisposable
    {
        public readonly UiaRig Rig = new(900, 500);
        public readonly MenuBar Bar;
        public readonly TextInputElement Document;

        public MenuBarFixture(bool focusDocument = true)
        {
            Bar = new MenuBar(
            [
                new("&File", [Entry("Open…"), Entry("Save")]),
                new("&Edit", [Entry("Undo"), Entry("Redo")]),
                new("&View", [Entry("Zoom in"), Entry("Zoom out")]),
            ]);
            Document = TextInput.New("");
            var root = Flex.Column(gap: 0).Children(Bar.Mount(), Document);
            root.On<KeyDownEvent>(e => { if (Bar.HandleKey(e)) e.StopPropagation = true; });
            root.On<KeyUpEvent>(e => Bar.HandleKeyUp(e));
            Rig.Session.Window.SetRoot(root);
            Settle(Rig.Session);
            if (focusDocument) Document.Focus();
            Rig.Session.PumpFrames(2);
        }

        public ElementId Title(int index) => Rig.Node(BarNode().ElementId)!.Children[index].ElementId;

        public AccessibilityNode BarNode() => Walk(Rig.Bridge.RootNode!).Single(n => n.Role == AccessibilityRole.MenuBar);

        public void Press(KeyCode key, KeyModifiers modifiers = KeyModifiers.None)
        {
            Rig.Raised.Clear();
            Rig.Session.Window.PressKey(key, modifiers);
            Rig.Session.PumpFrames(2);
        }

        public void Dispose() => Rig.Dispose();
    }

    [WindowsFact]
    public void AMenuBar_LeftAndRightMoveTheReadersFocusAcrossItsTitles_AndAMenuTakesItAndGivesItBack()
    {
        using var f = new MenuBarFixture();
        var rig = f.Rig;

        // F10: the bar has the keys, on its first title: one focus event, on the title.
        f.Press(KeyCode.F10);
        Assert.Equal(f.Title(0), rig.GetFocus());
        Assert.Equal([f.Title(0)], rig.FocusEvents());
        Assert.False(rig.Answer(f.BarNode().ElementId, IsKeyboardFocusable));
        Assert.True(rig.Answer(f.Title(1), IsKeyboardFocusable));
        Assert.True(rig.HasFocus(f.Title(0)));
        Assert.True(rig.Answer(f.Title(0), IsKeyboardFocusable));
        Assert.False(rig.HasFocus(f.BarNode().ElementId));
        Assert.Equal("File", rig.Node(f.Title(0))!.Name);

        int trees = rig.Session.Window.AccessibilityTreesBuilt;
        f.Press(KeyCode.Right);
        Assert.Equal(
        [
            Prop(HasKeyboardFocus, f.Title(0)),
            Prop(HasKeyboardFocus, f.Title(1)),
            Focus(f.Title(1)),
        ], rig.Raised);
        Assert.Equal(trees, rig.Session.Window.AccessibilityTreesBuilt);
        f.Press(KeyCode.Right);
        Assert.Equal([f.Title(2)], rig.FocusEvents());
        f.Press(KeyCode.Left);
        Assert.Equal([f.Title(1)], rig.FocusEvents());
        Assert.Equal(f.Title(1), rig.GetFocus());

        // ↓ opens Edit's menu on its first entry; → goes on to View's.
        f.Press(KeyCode.Down);
        var menu = rig.Menu();
        Assert.Equal([Item(menu, 0)], rig.FocusEvents());
        Assert.Equal("Undo", rig.Node(Item(menu, 0))!.Name);
        f.Press(KeyCode.Right);
        var view = rig.Menu();
        Assert.Equal("Zoom in", rig.Node(Item(view, 0))!.Name);
        Assert.Equal(Item(view, 0), rig.GetFocus());
        Assert.Equal(Item(view, 0), rig.FocusEvents().Last());
        f.Press(KeyCode.Down);
        Assert.Equal([Item(view, 1)], rig.FocusEvents());

        // Escape: back on View's title.
        f.Press(KeyCode.Escape);
        Assert.Equal(f.Title(2), rig.GetFocus());
        Assert.Equal(f.Title(2), rig.FocusEvents().Last());

        // Escape again: the bar lets go, and the document has the focus again.
        f.Press(KeyCode.Escape);
        Assert.Same(f.Document, rig.Session.Window.Window.FocusedElement);
        Assert.Equal(f.Document.Id, rig.GetFocus());
        Assert.Equal(f.Document.Id, rig.FocusEvents().Last());
        Assert.False(rig.HasFocus(f.Title(2)));
    }

    /// <summary>
    /// The readers on the Fluent sample (2026-11-16): after Alt+V, Escape (back to the titles) and Escape
    /// (back to the page), F10, Right and Right read the right titles, and then the bar again ("Menu bar
    /// menu bar"), from a focus event on the bar itself: F10 and Alt with a letter focused the bar at
    /// once, and its title a frame later. Each step here is one focus event, on the node it ends on: a
    /// menu's entry, a title, the page; never the bar.
    /// </summary>
    [WindowsFact]
    public void AMenuBar_AltV_EscapeEscape_F10_RightRight_Escape_IsOneFocusEventAStep()
    {
        using var f = new MenuBarFixture();
        var rig = f.Rig;
        List<ElementId?> Step(KeyCode key, KeyModifiers mods = KeyModifiers.None)
        {
            f.Press(key, mods);
            return rig.FocusEvents();
        }

        var altV = Step(KeyCode.V, KeyModifiers.Alt);
        var menu = rig.Menu();
        Assert.Equal([Item(menu, 0)], altV);
        Assert.Equal("Zoom in", rig.Node(Item(menu, 0))!.Name);
        Assert.Equal([f.Title(2)], Step(KeyCode.Escape));
        Assert.Equal([f.Document.Id], Step(KeyCode.Escape));
        Assert.Equal([f.Title(0)], Step(KeyCode.F10));
        Assert.Equal([f.Title(1)], Step(KeyCode.Right));
        Assert.Equal([f.Title(2)], Step(KeyCode.Right));
        Assert.Equal([f.Document.Id], Step(KeyCode.Escape));
        Assert.True(rig.HasFocus(f.Document));
        Assert.False(rig.HasFocus(f.Title(2)));
        Assert.False(rig.HasFocus(f.BarNode().ElementId));
    }

    /// <summary>
    /// On the Fluent sample (2026-11-16) F10, Right, Right and Escape read the titles, then "Menu bar menu
    /// bar": with nothing focused before F10 (a click on the page clears the focus) the bar had nothing to
    /// give the keyboard back to and kept it, with no title current, so its own node was the reader's
    /// focus and Escape told it. The keyboard goes back to nothing now, as it was, and a reader is told no
    /// bar: the title lets go of the focus, and nothing has it (GetFocus answers the window). F10 works
    /// again from there.
    /// </summary>
    [WindowsFact]
    public void AMenuBar_F10_RightRight_Escape_WithNothingFocused_GivesTheKeysBackToNothing_AndTellsNoBar()
    {
        using var f = new MenuBarFixture(focusDocument: false);
        var rig = f.Rig;
        Assert.Null(rig.Session.Window.Window.FocusedElement);

        f.Press(KeyCode.F10);
        Assert.Equal([f.Title(0)], rig.FocusEvents());
        f.Press(KeyCode.Right);
        f.Press(KeyCode.Right);
        Assert.Equal([f.Title(2)], rig.FocusEvents());

        f.Press(KeyCode.Escape);
        Assert.Null(rig.Session.Window.Window.FocusedElement);
        Assert.Empty(rig.FocusEvents());
        Assert.Contains(Prop(HasKeyboardFocus, f.Title(2)), rig.Raised);
        Assert.False(rig.HasFocus(f.Title(2)));
        Assert.False(rig.HasFocus(f.BarNode().ElementId));
        Assert.Null(rig.GetFocus());
        Assert.Equal(-1, f.Bar.HighlightIndex);

        f.Press(KeyCode.F10);
        Assert.Equal([f.Title(0)], rig.FocusEvents());
        f.Press(KeyCode.F10);
        Assert.Null(rig.Session.Window.Window.FocusedElement);
        Assert.Empty(rig.FocusEvents());
    }

    // ── A press that moves the focus ──

    /// <summary>The Fluent sample's navigation: items that hold the focus, one selected, the selection
    /// moved by a click, which rebuilds the list (as the sample's page switch does).</summary>
    private sealed class NavList : Component
    {
        public int Current;
        public int Clicks;
        public readonly string[] Names = ["Home", "Personalization", "Gallery", "Rich text", "About"];

        public override Element Build() => Flex.Column(gap: 4).Children(
        [
            .. Names.Select((name, i) =>
            {
                var item = Flex.Row().Height(30)
                    .AccessibleRole(AccessibilityRole.ListItem).AccessibleName(name).AccessibleSelected(i == Current)
                    .OnClick(_ => { Clicks++; Current = i; MarkDirty(); })
                    .Children(TextB.New(name));
                item.IsFocusable = true;
                return (Element)item;
            }),
            Button.New("Elsewhere"),
        ]);
    }

    private static (UiaRig Rig, NavList Nav) NavRig()
    {
        var rig = new UiaRig();
        var nav = new NavList();
        rig.Session.Window.SetRoot(nav.Mount());
        Settle(rig.Session);
        NavItem(rig, 0).Focus();
        rig.Session.PumpFrame();
        rig.Raised.Clear();
        return (rig, nav);
    }

    private static Element NavItem(UiaRig rig, int index) =>
        rig.Session.Window.FindByRole(AccessibilityRole.ListItem, new NavList().Names[index])!.Element;

    private static Point Middle(Element element)
    {
        var box = element.Layout.AbsoluteBorderBox;
        return new Point(box.X + box.Width / 2, box.Y + box.Height / 2);
    }

    /// <summary>
    /// A click on the Fluent sample's "Personalization" read "Personalization, not selected, 2 of 5" in
    /// NVDA and nothing more (2026-11-16): the press moved the focus and its frame told it, and the click
    /// selected the item on the release, a property change neither reader speaks. The move is told at the
    /// end of the release's frame now, after the click's selected state: one focus event, on the item,
    /// which answers selected. Until the release the reader's focus stays where it was.
    /// </summary>
    [WindowsFact]
    public void APressThatMovesTheFocus_IsToldAtTheRelease_AfterWhatTheClickSelected()
    {
        var (rig, nav) = NavRig();
        using var _ = rig;
        var home = NavItem(rig, 0);
        var personalization = NavItem(rig, 1);
        var at = Middle(personalization);

        rig.Session.Window.MouseDown(at.X, at.Y);
        rig.Session.PumpFrame();
        Assert.Same(personalization, rig.Session.Window.Window.FocusedElement);
        Assert.Empty(rig.FocusEvents());
        Assert.DoesNotContain(rig.Raised, e => e.Id == HasKeyboardFocus);
        // The reader's focus has not moved: GetFocus and HasKeyboardFocus agree on the item it left.
        Assert.Equal(home.Id, rig.GetFocus());
        Assert.True(rig.HasFocus(home));
        Assert.False(rig.HasFocus(personalization));

        rig.Session.PumpFrames(3);
        Assert.Empty(rig.FocusEvents());

        rig.Session.Window.MouseUp(at.X, at.Y);
        rig.Session.PumpFrame();
        Assert.Equal(1, nav.Clicks);
        Assert.Same(personalization, NavItem(rig, 1));   // the rebuild patched the item it focused
        Assert.Equal([personalization.Id], rig.FocusEvents());
        int selected = rig.Raised.IndexOf(Prop(IsSelected, personalization.Id));
        Assert.InRange(selected, 0, rig.Raised.IndexOf(Focus(personalization.Id)));
        Assert.Contains(Prop(IsSelected, home.Id), rig.Raised);
        Assert.True(rig.Answer(personalization.Id, IsSelected));
        Assert.Equal(personalization.Id, rig.GetFocus());
        Assert.True(rig.HasFocus(personalization));
        Assert.False(rig.HasFocus(home));

        rig.Raised.Clear();
        rig.Session.PumpFrames(3);
        Assert.Empty(rig.FocusEvents());

        // A click in one frame, press and release together, is told once in it.
        var gallery = NavItem(rig, 2);
        rig.Session.Window.Click(Middle(gallery).X, Middle(gallery).Y);
        rig.Session.PumpFrame();
        Assert.Equal([gallery.Id], rig.FocusEvents());
        Assert.True(rig.Answer(gallery.Id, IsSelected));
    }

    /// <summary>The keyboard's moves are told in their own frame, as before: Tab from an item.</summary>
    [WindowsFact]
    public void AKeyboardMove_IsToldInItsFrame_AsBefore()
    {
        var (rig, _) = NavRig();
        using var _r = rig;
        rig.Session.Window.PressKey(KeyCode.Tab);
        rig.Session.PumpFrame();
        Assert.Equal([NavItem(rig, 1).Id], rig.FocusEvents());
    }

    /// <summary>
    /// A press whose release does not come still has its move told: when the press becomes a drag (no
    /// click comes of it, and a drag out of the window takes the release), when a key is pressed, when the
    /// window comes to the front again (the release went to another window), and once
    /// <see cref="HultUI.Window.A11yPressHoldMaxMs"/> has gone by. Each once; the release that comes late
    /// tells nothing more.
    /// </summary>
    [WindowsTheory]
    [InlineData("drag")]
    [InlineData("key")]
    [InlineData("activation")]
    [InlineData("time")]
    public void APressWhoseReleaseDoesNotCome_StillHasItsMoveTold_Once(string how)
    {
        var (rig, nav) = NavRig();
        using var _ = rig;
        var item = NavItem(rig, 3);
        item.Draggable = how == "drag";
        var at = Middle(item);
        rig.Session.Window.MouseDown(at.X, at.Y);
        rig.Session.PumpFrame();
        Assert.Same(item, rig.Session.Window.Window.FocusedElement);
        Assert.Empty(rig.FocusEvents());

        switch (how)
        {
            case "drag":
                rig.Session.Window.MoveMouse(at.X + 40, at.Y + 60);
                rig.Session.PumpFrame();
                break;
            case "key":
                rig.Session.Window.KeyDown(KeyCode.F13);
                rig.Session.PumpFrame();
                break;
            case "activation":
                rig.Session.Window.Window.RaiseActivated();
                rig.Session.PumpFrame();
                break;
            case "time":
                rig.Session.PumpFrame(TimeSpan.FromMilliseconds(HultUI.Window.A11yPressHoldMaxMs / 2));
                Assert.Empty(rig.FocusEvents());
                rig.Session.AdvanceTime(TimeSpan.FromMilliseconds(HultUI.Window.A11yPressHoldMaxMs / 2 + 1));
                rig.Session.PumpFrame();
                break;
        }
        Assert.Equal([item.Id], rig.FocusEvents());
        Assert.Equal(item.Id, rig.GetFocus());

        rig.Raised.Clear();
        rig.Session.Window.MouseUp(at.X + (how == "drag" ? 40 : 0), at.Y + (how == "drag" ? 60 : 0));
        rig.Session.PumpFrames(2);
        Assert.Empty(rig.FocusEvents());
        Assert.Equal(item.Id, rig.GetFocus());
    }

    /// <summary>An open context menu holds the reader's focus; a press on an item outside closes it and
    /// moves the focus: told once, at the release, on the item the click selected — not on what had the
    /// focus under the menu, nor at the press.</summary>
    [WindowsFact]
    public void APressOutsideAnOpenMenu_IsToldAtTheRelease_OnTheItemItFocused()
    {
        var (rig, nav) = NavRig();
        using var _ = rig;
        rig.Session.Window.Window.OpenMenu([Entry("Copy"), Entry("Paste")], new Point(300, 40), new MenuRequest { HighlightFirst = true });
        rig.Session.PumpFrames(2);
        var menu = rig.Menu();
        Assert.Equal(Item(menu, 0), rig.GetFocus());
        rig.Raised.Clear();

        var about = NavItem(rig, 4);
        var at = Middle(about);
        rig.Session.Window.MouseDown(at.X, at.Y);
        rig.Session.PumpFrames(2);
        Assert.Empty(rig.FocusEvents());
        rig.Session.Window.MouseUp(at.X, at.Y);
        rig.Session.PumpFrame();
        Assert.Equal(4, nav.Current);
        Assert.Equal([about.Id], rig.FocusEvents());
        Assert.True(rig.Answer(about.Id, IsSelected));
    }

    /// <summary>The same hold on Linux: state-changed:focused goes to the clicked item at the release,
    /// after its state-changed:selected.</summary>
    [Fact]
    public void ATSPI_APressThatMovesTheFocus_IsToldAtTheRelease_AfterWhatTheClickSelected()
    {
        using var rig = new AtSpiRig();
        var nav = new NavList();
        rig.Session.Window.SetRoot(nav.Mount());
        Settle(rig.Session);
        var home = rig.Session.Window.FindByRole(AccessibilityRole.ListItem, "Home")!.Element;
        var personalization = rig.Session.Window.FindByRole(AccessibilityRole.ListItem, "Personalization")!.Element;
        home.Focus();
        rig.Session.PumpFrame();
        rig.Signals.Lines.Clear();

        var at = Middle(personalization);
        rig.Session.Window.MouseDown(at.X, at.Y);
        rig.Session.PumpFrames(2);
        Assert.Empty(rig.Signals.States());
        rig.Session.Window.MouseUp(at.X, at.Y);
        rig.Session.PumpFrame();
        Assert.Equal(
        [
            Selected(home.Id, false), Selected(personalization.Id, true),
            Focused(home.Id, false), Focused(personalization.Id, true),
        ], rig.Signals.States());
        Assert.True(rig.Object(personalization.Id).Focused);
        Assert.False(rig.Object(home.Id).Focused);
    }

    /// <summary>
    /// A click on the window, or Alt+Tab to it, read nothing in either reader, or the window only
    /// (2026-11-16): WPF, WinForms and Explorer raise a focus event on the focused element as their
    /// window comes to the front. Each activation now raises one, on the reader's focus (here a list's
    /// current item), in the frame after it; none while no client listens.
    /// </summary>
    [WindowsFact]
    public void AWindowThatComesToTheFront_TellsTheReadersFocusAgain_OnceAnActivation()
    {
        using var rig = new UiaRig();
        var (_, list, _, root) = FocusableList(10, selected: 2);
        rig.Session.Window.SetRoot(root);
        Settle(rig.Session);
        list.Focus();
        rig.Session.PumpFrame();
        rig.Raised.Clear();

        for (int i = 0; i < 2; i++)
        {
            rig.Session.Window.Window.RaiseActivated();
            rig.Session.PumpFrame();
            Assert.Equal([Item(list, 2)], rig.FocusEvents());
            // The same node keeps the focus: no state changes, the event alone.
            Assert.DoesNotContain(rig.Raised, e => e.Id == HasKeyboardFocus);
            rig.Raised.Clear();
            rig.Session.PumpFrame();
            Assert.Empty(rig.Raised);
        }
    }

    [Fact]
    public void AWindowThatComesToTheFront_TellsNoBridgeWhileNoClientListens()
    {
        using var session = HeadlessSession.Create(300, 200);
        var bridge = session.Window.InstallAccessibilityBridge(new Counting());
        session.Window.SetRoot(Flex.Column().Children(Button.New("A")));
        session.PumpFrames(2);
        session.Window.Window.RaiseActivated();
        Assert.Equal(1, bridge.Activations);
        bridge.Listening = false;
        session.PumpFrame();
        session.Window.Window.RaiseActivated();
        Assert.Equal(1, bridge.Activations);
    }

    private sealed class Counting : IAccessibilityBridge
    {
        public bool Listening = true;
        public int Activations;
        public bool IsActive => Listening;
        public void ApplyChanges(TreeDiff diff, AccessibilityNode root) { }
        public void NotifyFocusChanged(AccessibilityNode? focused) { }
        public void NotifyWindowActivated() => Activations++;
    }

    /// <summary>
    /// NVDA read two items as Vesper entered a folder ("inbox folder", then "parent folder"): a new listing
    /// relabels a list's items in place (an item's id is its place), and the item at the cursor's old
    /// place, which had the focus, was told renamed before the focus moved to the new cursor. A frame
    /// that takes the focus off an item tells no name change of it: one focus event, on the new cursor.
    /// </summary>
    [WindowsFact]
    public void ANewListing_WithTheCursorMoved_TellsOneMove_AndNoRenameOfTheItemLeft()
    {
        using var rig = new UiaRig();
        var (_, list, _, root) = FocusableList(20, selected: 5);
        rig.Session.Window.SetRoot(root);
        Settle(rig.Session);
        list.Focus();
        rig.Session.PumpFrame();
        Assert.Equal(Item(list, 5), rig.GetFocus());
        rig.Raised.Clear();

        list.Items = Enumerable.Range(0, 30).Select(i => i == 0 ? ".." : $"inner {i}").ToList();
        list.SelectedIndex = 0;
        for (int i = 0; i < 3; i++) rig.Session.PumpFrame(TimeSpan.FromMilliseconds(150));
        Assert.Equal([Item(list, 0)], rig.FocusEvents());
        Assert.DoesNotContain(rig.Raised, e => e.Element == Item(list, 5) && e.Id == UiaInterop.UIA_NamePropertyId);
        Assert.Equal("inner 5", rig.Node(Item(list, 5))!.Name);
        Assert.Equal("..", rig.Node(Item(list, 0))!.Name);

        // A rename where the focus stays is told.
        rig.Raised.Clear();
        list.Items = Enumerable.Range(0, 30).Select(i => i == 0 ? "up" : $"inner {i}").ToList();
        for (int i = 0; i < 3; i++) rig.Session.PumpFrame(TimeSpan.FromMilliseconds(150));
        Assert.Contains(rig.Raised, e => e.Element == Item(list, 0) && e.Id == UiaInterop.UIA_NamePropertyId);
        Assert.Empty(rig.FocusEvents());
    }

    /// <summary>
    /// Vesper's folder move with a reader on its list (plan 17 round 19 step 10): a new listing relabels
    /// the list's items in place, and an item no client was given tells no rename (153 a move between
    /// folders of 151 and 191 entries, 5 ms of a 6.6 ms frame with a reader-like client). The move tells
    /// what it told: the selected states, the rows that came, the focus states and the focus event on the
    /// new cursor; and an item a client holds tells its rename.
    /// </summary>
    [WindowsFact]
    public void ANewListing_TellsNoRenameOfItemsNoClientWasGiven_AndTheMoveAsBefore()
    {
        using var rig = new UiaRig();
        var (_, list, _, root) = FocusableList(20, selected: 5);
        rig.Session.Window.SetRoot(root);
        Settle(rig.Session);
        list.Focus();
        rig.Session.PumpFrame();
        Assert.Equal(Item(list, 5), rig.GetFocus());
        Assert.False(rig.HasFocus(Item(list, 12)));   // a client reads item 12: it holds it
        rig.Raised.Clear();

        list.Items = Enumerable.Range(0, 30).Select(i => i == 0 ? ".." : $"inner {i}").ToList();
        list.SelectedIndex = 0;
        for (int i = 0; i < 3; i++) rig.Session.PumpFrame(TimeSpan.FromMilliseconds(150));
        // Before: a Name change on each of items 0–19 but 5 (the item the focus left), 19 in all.
        Assert.Equal(
        [
            new UiaRaised(UiaRaisedKind.Structure, UiaInterop.StructureChangeType_ChildrenBulkAdded, list.Id),
            Prop(IsSelected, Item(list, 0)),
            Prop(IsSelected, Item(list, 5)),
            Prop(UiaInterop.UIA_NamePropertyId, Item(list, 12)),
            Prop(HasKeyboardFocus, Item(list, 5)),
            Prop(HasKeyboardFocus, Item(list, 0)),
            Focus(Item(list, 0)),
        ], rig.Raised);
        Assert.Equal(Item(list, 0), rig.GetFocus());
        Assert.Equal("inner 7", NameOf(rig, Item(list, 7)));
    }

    /// <summary>
    /// The reader's focus on an item of a list served on demand stays on its item as the list comes down to
    /// 200 items or fewer and gets item nodes: the client holds the item as the provider served on demand,
    /// and its item node has no native object of its own. A rename of it after is told all the same.
    /// </summary>
    [WindowsFact]
    public void TheReadersFocus_HeldFromAServedList_IsToldItsRename_AfterTheListGotItemNodes()
    {
        using var rig = new UiaRig();
        var (_, list, _, root) = FocusableList(300, selected: 0);
        rig.Session.Window.SetRoot(root);
        Settle(rig.Session);
        list.Focus();
        rig.Session.PumpFrame();
        Assert.True(rig.Node(list.Id)!.IsVirtualItemContainer);
        Assert.Equal(Item(list, 0), rig.GetFocus());

        list.Items = Enumerable.Range(0, 150).Select(i => $"file {i}").ToList();
        for (int i = 0; i < 3; i++) rig.Session.PumpFrame(TimeSpan.FromMilliseconds(150));
        Assert.False(rig.Node(list.Id)!.IsVirtualItemContainer);
        rig.Raised.Clear();

        list.Items = Enumerable.Range(0, 150).Select(i => i == 0 ? "renamed" : $"file {i}").ToList();
        for (int i = 0; i < 3; i++) rig.Session.PumpFrame(TimeSpan.FromMilliseconds(150));
        Assert.Equal([Item(list, 0)], NameChanges(rig));
        Assert.Empty(rig.FocusEvents());
        Assert.Equal("renamed", NameOf(rig, Item(list, 0)));
    }

    /// <summary>What a provider answers for PositionInSet and SizeOfSet, (0, 0) for nothing.</summary>
    private static (int At, int Of) Place(UiaRig rig, ElementId id, bool served = false)
    {
        IntPtr native = served && VirtualItemId.TryDecode(id, out var listId, out int index)
            ? rig.Bridge.NativeVirtualItem(listId, index) : rig.Bridge.NativeFragment(id);
        Assert.NotEqual(IntPtr.Zero, native);
        try
        {
            var simple = (INativeSimple)NativeProvider.LogicOf(native)!;
            simple.GetPropertyValue(UiaInterop.UIA_PositionInSetPropertyId, out var at);
            simple.GetPropertyValue(UiaInterop.UIA_SizeOfSetPropertyId, out var of);
            return (at.Vt == 0 ? 0 : at.IntValue, of.Vt == 0 ? 0 : of.IntValue);
        }
        finally { Marshal.Release(native); }
    }

    /// <summary>
    /// Neither reader said "n of N" in a list or a menu, and Narrator said "Home, 1 of 6", then
    /// "Personalization, 2 of 5", then "2 of 7" for the Fluent sample's five navigation items: nothing
    /// answered PositionInSet or SizeOfSet, and UI Automation worked them out. Now an item node, an
    /// item served on demand, a menu's entry (its separators are no entries), a menu bar's title and a
    /// list item among other children (a set with no container) each answer its place and its set's size.
    /// </summary>
    [WindowsFact]
    public void Items_AnswerTheirPlaceInTheirSet()
    {
        using var rig = new UiaRig();
        var (_, list, _, listRoot) = FocusableList(40, selected: 3);
        var (_, served, _, servedRoot) = FocusableList(WindowsAccessibilityBridge.MaxEagerVirtualItems + 1_000, selected: 0);
        var nav = new Element[5];
        for (int i = 0; i < nav.Length; i++)
        {
            nav[i] = Flex.Row().Height(24).AccessibleRole(AccessibilityRole.ListItem).AccessibleName($"Page {i}")
                .Children(TextB.New($"Page {i}"));
            nav[i].AccessibleSelected = i == 1;
        }
        var (opener, openerRoot) = Opener();
        rig.Session.Window.SetRoot(Flex.Row().Children(
            Flex.Column().Children([Button.New("Back"), TextB.New("Title"), .. nav, Button.New("Close")]),
            Flex.Column().FlexGrow(1).Children(listRoot, servedRoot, openerRoot)));
        Settle(rig.Session);

        Assert.Equal((4, 40), Place(rig, Item(list, 3)));
        Assert.Equal((1_001, WindowsAccessibilityBridge.MaxEagerVirtualItems + 1_000), Place(rig, Item(served, 1_000), served: true));
        Assert.Equal((2, 5), Place(rig, nav[1].Id));
        Assert.Equal((5, 5), Place(rig, nav[4].Id));
        Assert.Equal((0, 0), Place(rig, opener.Id));

        opener.Focus();
        rig.Session.Window.Window.OpenMenu(
            [Entry("Dark mode"), Entry("Animations"), ContextMenuItem.Separator(), Entry("Personalization"), Entry("Controls gallery"), Entry("Rich text")],
            new Point(20, 40), new MenuRequest { HighlightFirst = true });
        rig.Session.PumpFrame();
        var menu = rig.Menu();
        Assert.Equal("Personalization", rig.Node(Item(menu, 2))!.Name);
        Assert.Equal((3, 5), Place(rig, Item(menu, 2)));
    }

    [WindowsFact]
    public void AMenuBarsTitles_AnswerTheirPlace()
    {
        using var f = new MenuBarFixture();
        Assert.Equal((2, 3), Place(f.Rig, f.Title(1)));
    }

    /// <summary>A menu's entries are no SelectionItems (Narrator said "selected" on every one); a list's
    /// items keep theirs. Item nodes and items served on demand alike, and no IsSelected change is
    /// raised for an entry.</summary>
    [WindowsTheory]
    [InlineData(3)]
    [InlineData(WindowsAccessibilityBridge.MaxEagerVirtualItems + 1)]   // a menu over 200 entries: served on demand on UIA
    public void AMenusEntries_AreNoSelectionItems_AndRaiseNoIsSelected(int count)
    {
        using var rig = new UiaRig();
        var (opener, root) = Opener();
        rig.Session.Window.SetRoot(root);
        Settle(rig.Session);
        opener.Focus();
        rig.Session.PumpFrame();
        rig.Session.Window.Window.OpenMenu(Enumerable.Range(0, count).Select(i => Entry($"Entry {i}")).ToList(),
            new Point(20, 40), new MenuRequest { HighlightFirst = true });
        rig.Session.PumpFrame();
        var menu = rig.Menu();
        Assert.Equal(count > WindowsAccessibilityBridge.MaxEagerVirtualItems, menu.IsVirtualItemContainer);
        rig.Raised.Clear();
        rig.Session.Window.PressKey(KeyCode.Down);
        rig.Session.PumpFrame();
        Assert.DoesNotContain(rig.Raised, e => e.Id == IsSelected);
        Assert.Equal([Item(menu, 1)], rig.FocusEvents());

        IntPtr native = menu.IsVirtualItemContainer ? rig.Bridge.NativeVirtualItem(menu.ElementId, 1) : rig.Bridge.NativeFragment(Item(menu, 1));
        try
        {
            var entry = (INativeSimple)NativeProvider.LogicOf(native)!;
            Assert.Equal(0, entry.GetPatternProvider(UiaInterop.UIA_SelectionItemPatternId, out var pattern));
            Assert.Equal(IntPtr.Zero, pattern);
            Assert.Equal(0, entry.GetPropertyValue(IsSelected, out var value));
            Assert.Equal(0, (int)value.Vt);
        }
        finally { Marshal.Release(native); }
        var menuNative = rig.Bridge.NativeFragment(menu.ElementId);
        try
        {
            var logic = (INativeSimple)NativeProvider.LogicOf(menuNative)!;
            Assert.Equal(0, logic.GetPatternProvider(UiaInterop.UIA_SelectionPatternId, out var selection));
            Assert.Equal(IntPtr.Zero, selection);
        }
        finally { Marshal.Release(menuNative); }
    }

    // ── AT-SPI ──

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

        /// <summary>The focused and selected states sent, in order.</summary>
        public List<string> States() => Lines.Where(l => l.StartsWith("state-changed:focused", StringComparison.Ordinal)
            || l.StartsWith("state-changed:selected", StringComparison.Ordinal)).ToList();
    }

    private sealed class AtSpiRig : IDisposable
    {
        public HeadlessSession Session { get; } = HeadlessSession.Create(600, 500);
        public readonly Signals Signals = new();
        public LinuxAccessibilityBridge Bridge { get; }

        public AtSpiRig()
        {
            Bridge = new LinuxAccessibilityBridge(Signals);
            Session.Window.InstallAccessibilityBridge(new Active(Bridge));
        }

        /// <summary>The object of <paramref name="id"/> as a client finds it by its path: a node's, or
        /// an item served on demand.</summary>
        public AtSpiAccessible Object(ElementId id) => ((IAtSpiTree)Bridge).Find(AtSpiAccessible.PathFor(id))!;

        public void Dispose()
        {
            Bridge.Dispose();
            Session.Dispose();
        }
    }

    private static string Focused(ElementId id, bool on) => $"state-changed:focused {id.Value} {(on ? 1 : 0)}";
    private static string Selected(ElementId id, bool on) => $"state-changed:selected {id.Value} {(on ? 1 : 0)}";

    [Theory]
    [InlineData(40)]
    [InlineData(AccessibilityTreeBuilder.MaxEagerVirtualItems + 500)]
    public void ATSPI_AList_TheFocusedStateGoesToItsCurrentItem_AndMovesWithIt(int count)
    {
        using var rig = new AtSpiRig();
        var (before, list, after, root) = FocusableList(count, selected: 3);
        rig.Session.Window.SetRoot(root);
        Settle(rig.Session);
        before.Focus();
        rig.Session.PumpFrame();
        rig.Signals.Lines.Clear();

        rig.Session.Window.PressKey(KeyCode.Tab);
        rig.Session.PumpFrame();
        Assert.Equal([Focused(before.Id, false), Focused(Item(list, 3), true)], rig.Signals.States());
        Assert.True(rig.Object(Item(list, 3)).Focused);
        Assert.True(rig.Object(Item(list, 3)).Focusable);
        Assert.False(rig.Object(list.Id).Focused);

        rig.Signals.Lines.Clear();
        list.SelectedIndex = 4;
        rig.Session.PumpFrame();
        Assert.Equal(
        [
            Selected(Item(list, 3), false), Selected(Item(list, 4), true),
            Focused(Item(list, 3), false), Focused(Item(list, 4), true),
        ], rig.Signals.States());
        Assert.False(rig.Object(Item(list, 3)).Focused);
        Assert.True(rig.Object(Item(list, 4)).Focused);

        // A rebuild for something else keeps the states: the tree marks the list's node focused.
        rig.Signals.Lines.Clear();
        after.AccessibleName = "After, renamed";
        rig.Session.PumpFrame();
        Assert.DoesNotContain(rig.Signals.Lines, l => l.StartsWith("state-changed:focused", StringComparison.Ordinal));
        Assert.True(rig.Object(Item(list, 4)).Focused);
        Assert.False(rig.Object(list.Id).Focused);

        rig.Signals.Lines.Clear();
        rig.Session.Window.PressKey(KeyCode.Tab);
        rig.Session.PumpFrame();
        Assert.Equal([Focused(Item(list, 4), false), Focused(after.Id, true)], rig.Signals.States());
        Assert.False(rig.Object(Item(list, 4)).Focused);
    }

    [Fact]
    public void ATSPI_ANewListing_WithTheCursorMoved_SendsNoRenameOfTheItemLeft()
    {
        using var rig = new AtSpiRig();
        var (_, list, _, root) = FocusableList(20, selected: 5);
        rig.Session.Window.SetRoot(root);
        Settle(rig.Session);
        list.Focus();
        rig.Session.PumpFrame();
        rig.Signals.Lines.Clear();

        list.Items = Enumerable.Range(0, 30).Select(i => i == 0 ? ".." : $"inner {i}").ToList();
        list.SelectedIndex = 0;
        for (int i = 0; i < 3; i++) rig.Session.PumpFrame(TimeSpan.FromMilliseconds(150));
        Assert.DoesNotContain($"property-change:accessible-name {Item(list, 5).Value}", rig.Signals.Lines);
        Assert.Equal([Focused(Item(list, 5), false), Focused(Item(list, 0), true)],
            rig.Signals.Lines.Where(l => l.StartsWith("state-changed:focused", StringComparison.Ordinal)));
    }

    [Fact]
    public void ATSPI_AWindowThatComesToTheFront_SendsTheFocusedStateAgain()
    {
        using var rig = new AtSpiRig();
        var (_, list, _, root) = FocusableList(10, selected: 2);
        rig.Session.Window.SetRoot(root);
        Settle(rig.Session);
        list.Focus();
        rig.Session.PumpFrame();
        rig.Signals.Lines.Clear();

        rig.Session.Window.Window.RaiseActivated();
        rig.Session.PumpFrame();
        Assert.Equal([Focused(Item(list, 2), true)], rig.Signals.States());
        rig.Signals.Lines.Clear();
        rig.Session.PumpFrame();
        Assert.Empty(rig.Signals.States());
    }

    [Fact]
    public void ATSPI_AContextMenu_TheFocusedStateWalksItsEntries_AndGoesBackToTheOpener()
    {
        using var rig = new AtSpiRig();
        var (opener, root) = Opener();
        rig.Session.Window.SetRoot(root);
        Settle(rig.Session);
        opener.Focus();
        rig.Session.PumpFrame();
        rig.Signals.Lines.Clear();

        rig.Session.Window.Window.OpenMenu([Entry("Copy hash"), Entry("Revert")], new Point(20, 40), new MenuRequest { HighlightFirst = true });
        rig.Session.PumpFrame();
        var menu = Walk(rig.Session.Window.AccessibilityTree).First(n => n.Role == AccessibilityRole.Menu);
        Assert.Equal([Focused(opener.Id, false), Focused(Item(menu, 0), true)], rig.Signals.States());

        rig.Signals.Lines.Clear();
        rig.Session.Window.PressKey(KeyCode.Down);
        rig.Session.PumpFrame();
        // A menu's entry has no selected state: the move is the focus's alone.
        Assert.Equal([Focused(Item(menu, 0), false), Focused(Item(menu, 1), true)], rig.Signals.States());
        Assert.False(rig.Object(Item(menu, 1)).Selectable);
        Assert.False(rig.Object(Item(menu, 1)).Selected);

        // Its items' ids before it closes: the menu gives its slot back as it goes.
        var entry = Item(menu, 1);
        rig.Signals.Lines.Clear();
        rig.Session.Window.PressKey(KeyCode.Escape);
        rig.Session.PumpFrame();
        Assert.Equal([Focused(entry, false), Focused(opener.Id, true)],
            rig.Signals.Lines.Where(l => l.StartsWith("state-changed:focused", StringComparison.Ordinal)));
    }

    [WindowsFact]
    public void AMenuBar_ACommandChosenFromItsMenu_GivesTheFocusBackToTheDocument_WithNoStopOnTheBar()
    {
        using var f = new MenuBarFixture();
        var rig = f.Rig;
        f.Press(KeyCode.F, KeyModifiers.Alt);
        Assert.Equal("Open…", rig.Node(rig.GetFocus()!.Value)!.Name);

        f.Press(KeyCode.Enter);
        Assert.Same(f.Document, rig.Session.Window.Window.FocusedElement);
        Assert.Equal([f.Document.Id], rig.FocusEvents());
        Assert.Equal(f.Document.Id, rig.GetFocus());
    }

    // ── A table the app sorts ──

    private sealed class SortedFiles : Component
    {
        public readonly TableView<(string Name, long Size)> Table;
        public readonly HultUI.Core.Reactivity.Ref<VirtualListElement<(string Name, long Size)>> Body = new();
        public TableSortRequest? Asked;
        public int Current = 3;
        // f3 is the eighth largest: sorted by size, largest first, it stands at row 7.
        public IReadOnlyList<(string Name, long Size)> Rows =
            [("f0", 100), ("f1", 90), ("f2", 80), ("f3", 35), ("f4", 70), ("f5", 60), ("f6", 50), ("f7", 40), ("f8", 30), ("f9", 20)];

        public SortedFiles()
        {
            Table = new TableView<(string Name, long Size)>(
            [
                new TableColumn<(string Name, long Size)> { Id = "name", Header = "Name", Fill = 1, Text = r => r.Name, CanSort = true },
                new TableColumn<(string Name, long Size)> { Id = "size", Header = "Size", Width = 90, Text = r => $"{r.Size}",
                    CanSort = true, FirstSort = SortDirection.Descending },
            ], MarkDirty)
            {
                SortMode = TableSortMode.External,
                HeadersTakeFocus = false,
                SortRequested = request => Asked = request,
            };
        }

        /// <summary>The app's answer, keeping the cursor on its item: the rows sorted, the cursor
        /// moved to the item's new row, in one build.</summary>
        public void Answer()
        {
            var cursor = Rows[Current];
            Rows = [.. Rows.OrderByDescending(r => r.Size)];
            Current = Rows.ToList().IndexOf(cursor);
            Table.SetSortKeys(Asked!.Value.Suggested);
            MarkDirty();
        }

        public override Element Build()
        {
            var body = Table.Body(Rows);
            body.IsFocusable = true;
            body.AccessibleName = "Files";
            body.CurrentIndex = Current;
            body.WithRef(Body);
            return Flex.Column(gap: 0).FlexGrow(1).Children(Table.Header(), body.FlexGrow(1));
        }
    }

    /// <summary>
    /// A re-sort asked for from the Size header while the keyboard focus is in the body, and answered
    /// keeping the cursor on its item (the app moves CurrentIndex to the item's new row in the build
    /// that shows the answer), is told as one focus move, from the item's old row to its new one, and
    /// one announcement of the sort. Pins what a reader is sent; whether a reader speaks both is for a
    /// listening test.
    /// </summary>
    [Fact]
    public void ATSPI_AReSortThatMovesTheCursor_SendsOneFocusMove_AndOneAnnouncement()
    {
        using var rig = new AtSpiRig();
        var page = new SortedFiles();
        rig.Session.Window.SetRoot(page.Mount());
        Settle(rig.Session);
        var body = page.Body.Current!;
        body.Focus();
        Settle(rig.Session);
        rig.Signals.Lines.Clear();

        // The reader's invoke of the Size header, which leaves the keyboard focus in the body (a
        // pointer press on a header that takes no focus clears the window's focus).
        var router = rig.Session.Window.InstallActionRouter();
        router.InvokeElement(rig.Session.Window.FindByAutomationId("column-size")!.Element.Id);
        rig.Session.PumpFrame();
        page.Answer();
        for (int i = 0; i < 3; i++) rig.Session.PumpFrame(TimeSpan.FromMilliseconds(150));

        Assert.Equal(7, page.Current);
        Assert.Same(body, page.Body.Current);
        Assert.Equal([Focused(Item(body, 3), false), Focused(Item(body, 7), true)],
            rig.Signals.Lines.Where(l => l.StartsWith("state-changed:focused", StringComparison.Ordinal)));
        Assert.Single(rig.Signals.Lines, l => l.StartsWith("announcement", StringComparison.Ordinal));
    }
}
