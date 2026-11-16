#pragma warning disable CA1416
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using HultUI.Core.Accessibility;
using HultUI.Core.Builders;
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
    private sealed class Active(IAccessibilityBridge inner) : IAccessibilityBridge
    {
        public bool IsActive => true;
        public void ApplyChanges(TreeDiff diff, AccessibilityNode root) => inner.ApplyChanges(diff, root);
        public void ApplyPatchedProperties(TreeDiff patch, AccessibilityNode root) => inner.ApplyPatchedProperties(patch, root);
        public void NotifyFocusChanged(AccessibilityNode? focused) => inner.NotifyFocusChanged(focused);
        public void NotifyFocusChanged(AccessibilityNode? focused, AccessibilityNode? previous) => inner.NotifyFocusChanged(focused, previous);
        public void NotifyChangesApplied(AccessibilityNode root) => inner.NotifyChangesApplied(root);
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
        int count = AccessibilityTreeBuilder.MaxEagerVirtualItems + 500;
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
        Assert.Equal(
        [
            Prop(IsSelected, Item(menu, 0)),
            Prop(IsSelected, Item(menu, 1)),
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

        public MenuBarFixture()
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
            Document.Focus();
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
        Assert.Equal(
        [
            Selected(Item(menu, 0), false), Selected(Item(menu, 1), true),
            Focused(Item(menu, 0), false), Focused(Item(menu, 1), true),
        ], rig.Signals.States());

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
}
