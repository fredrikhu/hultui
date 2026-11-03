using System.Runtime.InteropServices;
using HultUI.Core.Accessibility;
using HultUI.Core.Builders;
using HultUI.Core.Components;
using HultUI.Core.Elements;
using HultUI.Core.Events;
using HultUI.Core.Layout;
using HultUI.Core.Primitives;
using HultUI.Platform.Accessibility.Linux;
using HultUI.Platform.Accessibility.Windows;
using Xunit;
using TextB = HultUI.Core.Builders.Text;

namespace HultUI.Headless.Tests;

/// <summary>
/// A menu an app draws with elements: a box with the role <see cref="AccessibilityRole.Menu"/> of
/// <see cref="AccessibilityRole.MenuItem"/> rows, directly in the box whose click closes it (the
/// user approved what a reader hears on 2026-10-27). Without the roles the box and the menu are two
/// buttons named after every row. An entry is selected while the pointer is on it, as a context
/// menu's item is, and the move is patched into the tree, which a list of 1,000 rows beside it
/// makes worth it.
/// </summary>
public class MenuRoleTests
{
    private sealed class Pane : Component
    {
        public bool Open = true;
        public string? Ran;

        public override Element Build()
        {
            var root = Box.New().FlexGrow(1);
            root.AddChild(TextB.New("Beside it").FontSize(12f));
            if (!Open) return root;

            var layer = Box.New().Width(Dimension.Percent(100)).Height(Dimension.Percent(100));
            layer.Position.Type = PositionType.Absolute;
            layer.Position.Top = Dimension.Px(0);
            layer.Position.Left = Dimension.Px(0);
            layer.On<ClickEvent>(e => { e.StopPropagation = true; Open = false; MarkDirty(); });

            var menu = Flex.Column().Width(160).AccessibleRole(AccessibilityRole.Menu);
            menu.Position.Type = PositionType.Absolute;
            menu.Position.Left = Dimension.Px(40);
            menu.Position.Top = Dimension.Px(40);
            menu.On<ClickEvent>(e => e.StopPropagation = true);
            foreach (var label in new[] { "Add", "Clone", "Remove" })
            {
                var row = Flex.Row().Height(24).Padding(new Thickness(2, 8))
                    .AccessibleRole(AccessibilityRole.MenuItem).AccessibleName(label)
                    .Children(TextB.New(label).FontSize(12f));
                row.On<ClickEvent>(e => { e.StopPropagation = true; Ran = label; Open = false; MarkDirty(); });
                menu.AddChild(row);
            }
            layer.AddChild(menu);
            root.AddChild(layer);
            return root;
        }
    }

    private static (HeadlessSession Session, Pane Pane, HeadlessAccessibilityBridge Bridge) Mount()
    {
        var session = HeadlessSession.Create(400, 300);
        var bridge = session.Window.InstallAccessibilityBridge();
        var pane = new Pane();
        session.Application.SetRoot(pane.Mount());
        session.PumpFrames(3);
        return (session, pane, bridge);
    }

    private static IEnumerable<AccessibilityNode> Walk(AccessibilityNode node)
    {
        yield return node;
        foreach (var child in node.Children)
            foreach (var n in Walk(child)) yield return n;
    }

    private static AccessibilityNode Menu(HeadlessAccessibilityBridge bridge) =>
        Walk(bridge.CurrentTree!).Single(n => n.Role == AccessibilityRole.Menu);

    private static AccessibilityNode Entry(HeadlessAccessibilityBridge bridge, string name) =>
        Walk(bridge.CurrentTree!).Single(n => n.Role == AccessibilityRole.MenuItem && n.Name == name);

    private static float EntryY(int row) => 40 + row * 24 + 12;

    [Fact]
    public void AMenuInTheBoxThatClosesIt_IsAMenuOfItems_AndNoButton()
    {
        var (session, _, bridge) = Mount();
        using (session)
        {
            var menu = Menu(bridge);
            Assert.Equal(["Add", "Clone", "Remove"],
                menu.Children.Where(c => c.Role == AccessibilityRole.MenuItem).Select(c => c.Name));
            Assert.DoesNotContain(Walk(bridge.CurrentTree!), n => n.Role == AccessibilityRole.Button);
        }
    }

    [Fact]
    public void AClickOnAnEntry_RunsIt_AndOneBesideTheMenuClosesIt()
    {
        var (session, pane, bridge) = Mount();
        using (session)
        {
            session.Window.Click(60, EntryY(1));
            session.PumpFrames(2);
            Assert.Equal("Clone", pane.Ran);
            Assert.False(pane.Open);
            Assert.DoesNotContain(Walk(bridge.CurrentTree!), n => n.Role == AccessibilityRole.Menu);
        }

        (session, pane, bridge) = Mount();
        using (session)
        {
            session.Window.Click(380, 280);
            session.PumpFrames(2);
            Assert.Null(pane.Ran);
            Assert.False(pane.Open);
        }
    }

    /// <summary>The entry under the pointer is selected and the others say they can be; each move is
    /// a patch of the two entries, and no tree. Nothing is drawn differently for it.</summary>
    [Fact]
    public void TheEntryUnderThePointer_IsSelected_AndAMoveBuildsNoTree()
    {
        var (session, _, bridge) = Mount();
        using (session)
        {
            Assert.Equal([false, false, false], new[] { "Add", "Clone", "Remove" }.Select(n => Entry(bridge, n).IsSelected));
            int trees = session.Window.AccessibilityTreesBuilt;

            session.Window.MoveMouse(60, EntryY(0));
            session.PumpFrames(2);
            Assert.True(Entry(bridge, "Add").IsSelected);
            Assert.False(Entry(bridge, "Clone").IsSelected);

            session.Window.MoveMouse(60, EntryY(2));
            session.PumpFrames(2);
            Assert.False(Entry(bridge, "Add").IsSelected);
            Assert.True(Entry(bridge, "Remove").IsSelected);

            session.Window.MoveMouse(380, 280);
            session.PumpFrames(2);
            Assert.False(Entry(bridge, "Remove").IsSelected);

            Assert.Equal(trees, session.Window.AccessibilityTreesBuilt);
        }
    }

    /// <summary>A menu bar's title is an entry too, but of a bar: it has no selection, and the
    /// pointer over it tells nothing.</summary>
    [Fact]
    public void AMenuBarsTitle_HasNoSelection()
    {
        using var session = HeadlessSession.Create(400, 300);
        var bridge = session.Window.InstallAccessibilityBridge();
        var bar = Box.New().AccessibleRole(AccessibilityRole.MenuBar).Children(
            Box.New().Height(24).AccessibleRole(AccessibilityRole.MenuItem).AccessibleName("File"));
        session.Application.SetRoot(bar);
        session.PumpFrames(3);
        session.Window.MoveMouse(20, 12);
        session.PumpFrames(2);
        Assert.Null(Entry(bridge, "File").IsSelected);
    }

    [WindowsFact]
    public void ToUia_AMenuAndItsEntry_AreControlsOfThatType_AndAnEntryRunsAndSelects()
    {
        const int ControlType = 30003;
        using var hwnd = TestHwnd.Create();
        using var host = HandleWindowHost.Create(hwnd.Handle);
        var bridge = Assert.IsType<WindowsAccessibilityBridge>(AccessibilityBridgeFactory.Create(host.Application.PrimaryWindow));
        try
        {
            var app = new AccessibilityNode(ElementId.New(), AccessibilityRole.Application, null, null, new Rect(0, 0, 200, 100));
            var menu = new AccessibilityNode(ElementId.New(), AccessibilityRole.Menu, null, null, new Rect(0, 0, 80, 48));
            var entry = new AccessibilityNode(ElementId.New(), AccessibilityRole.MenuItem, "Add", null, new Rect(0, 0, 80, 24), isSelected: false);
            var title = new AccessibilityNode(ElementId.New(), AccessibilityRole.MenuItem, "File", null, new Rect(0, 24, 80, 24), expanded: false);
            app.AddChild(menu);
            menu.AddChild(entry);
            menu.AddChild(title);
            bridge.ApplyChanges(AccessibilityTreeDiffer.Diff(null, app), app);

            UiaFragment Fragment(AccessibilityNode node)
            {
                var native = bridge.NativeFragment(node.ElementId);
                try { return Assert.IsType<UiaFragment>(NativeProvider.LogicOf(native)); }
                finally { Marshal.Release(native); }
            }

            Assert.Equal(0, Fragment(menu).GetPropertyValue(ControlType, out var type));
            Assert.Equal(UiaInterop.UIA_MenuControlTypeId, type.IntValue);
            Assert.Equal(0, Fragment(entry).GetPropertyValue(ControlType, out type));
            Assert.Equal(UiaInterop.UIA_MenuItemControlTypeId, type.IntValue);

            Assert.Equal(0, Fragment(entry).GetPatternProvider(UiaInterop.UIA_InvokePatternId, out var invoke));
            Assert.NotEqual(IntPtr.Zero, invoke);
            Marshal.Release(invoke);
            Assert.Equal(0, Fragment(entry).GetPatternProvider(UiaInterop.UIA_SelectionItemPatternId, out var selection));
            Assert.NotEqual(IntPtr.Zero, selection);
            Marshal.Release(selection);
            var container = bridge.ItemSelectionContainer(entry.ElementId);
            Assert.NotEqual(IntPtr.Zero, container);
            Marshal.Release(container);

            // A title that opens a menu expands; it does not run.
            Assert.Equal(0, Fragment(title).GetPatternProvider(UiaInterop.UIA_InvokePatternId, out invoke));
            Assert.Equal(IntPtr.Zero, invoke);
        }
        finally { bridge.Dispose(); }
    }

    // ── A context menu, a menu bar's menu and a dropdown's popup (plan 17 round 7, step 13) ──
    //
    // All three draw their items and publish them (ContextMenuElement), which every bridge read as a
    // List of ListItems. The user approved (round 7, question 2): a context menu and a menu bar's menu
    // are a Menu of MenuItems; a dropdown's popup stays a List, as a combo box's list is.

    public enum Opened { ContextMenu, MenuBar, Dropdown }

    private static ContextMenuItem[] Choices(int count = 0)
    {
        if (count > 0)
            return [.. Enumerable.Range(0, count).Select(i => new ContextMenuItem { Label = $"Item {i}", Action = () => { } })];
        return
        [
            new() { Label = "Copy", Action = () => { } },
            ContextMenuItem.Separator(),
            new() { Label = "Paste", Action = () => { } },
            new() { Label = "More", Children = [new() { Label = "Alpha", Action = () => { } }] },
        ];
    }

    /// <summary>Opens one of the three over a label, and returns the element it publishes its items from.</summary>
    private static Element Open(HeadlessSession session, Opened what, int count = 0)
    {
        var dropdown = Dropdown.New(["Copy", "Paste", "More"], selectedIndex: 0).Width(Dimension.Px(160));
        var bar = new MenuBar([new MenuBarItem("&File", Choices(count))]);
        session.Window.SetRoot(what switch
        {
            Opened.Dropdown => Flex.Column().Padding(8).Children(dropdown),
            Opened.MenuBar => Flex.Column().Children(bar.Mount(), TextB.New("Main")),
            _ => TextB.New("Main"),
        });
        session.PumpFrames(2);
        switch (what)
        {
            case Opened.ContextMenu: session.Window.Window.OpenContextMenu(Choices(count), new Point(40, 40)); break;
            case Opened.MenuBar: bar.Open(0, fromKeyboard: true); break;
            case Opened.Dropdown: session.Window.Click(dropdown); break;
        }
        session.PumpFrames(2);
        return session.Window.OverlayContents.Single();
    }

    [Theory]
    [InlineData(Opened.ContextMenu, AccessibilityRole.Menu, AccessibilityRole.MenuItem)]
    [InlineData(Opened.MenuBar, AccessibilityRole.Menu, AccessibilityRole.MenuItem)]
    [InlineData(Opened.Dropdown, AccessibilityRole.List, AccessibilityRole.ListItem)]
    public void AContextMenuAndAMenuBarsMenu_AreMenusOfItems_AndADropdownsPopupAList(Opened what,
        AccessibilityRole role, AccessibilityRole itemRole)
    {
        using var session = HeadlessSession.Create(400, 300);
        var bridge = session.Window.InstallAccessibilityBridge();
        var popup = Open(session, what);

        var node = Walk(bridge.CurrentTree!).Single(n => n.ElementId == popup.Id);
        Assert.Equal(role, node.Role);
        Assert.Equal([("Copy", itemRole), ("Paste", itemRole), ("More", itemRole)], node.Children.Select(c => (c.Name, c.Role)));
        // Selected as before: the keyboard opened the menu bar's on its first item, the dropdown's on its value.
        Assert.Equal(what != Opened.ContextMenu, node.Children[0].IsSelected);
    }

    private sealed class Active(IAccessibilityBridge inner) : IAccessibilityBridge
    {
        public bool IsActive => true;
        public void ApplyChanges(TreeDiff diff, AccessibilityNode root) => inner.ApplyChanges(diff, root);
        public void ApplyPatchedProperties(TreeDiff patch, AccessibilityNode root) => inner.ApplyPatchedProperties(patch, root);
        public void NotifyFocusChanged(AccessibilityNode? focused) => inner.NotifyFocusChanged(focused);
        public void NotifyFocusChanged(AccessibilityNode? focused, AccessibilityNode? previous) => inner.NotifyFocusChanged(focused, previous);
    }

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

    /// <summary>AT-SPI: menu (33) and menu item (35), each item a click and selectable, as a list item
    /// was; a dropdown's popup list (31) and list item (32). Past 2,000 items a menu's are served on
    /// demand, and are menu items too.</summary>
    [Theory]
    [InlineData(Opened.ContextMenu, 0, AtSpiConstants.RoleMenu, AtSpiConstants.RoleMenuItem)]
    [InlineData(Opened.MenuBar, 0, AtSpiConstants.RoleMenu, AtSpiConstants.RoleMenuItem)]
    [InlineData(Opened.Dropdown, 0, AtSpiConstants.RoleList, AtSpiConstants.RoleListItem)]
    [InlineData(Opened.ContextMenu, 2_001, AtSpiConstants.RoleMenu, AtSpiConstants.RoleMenuItem)]
    public void ToAtSpi_AMenuIsAMenuOfMenuItems_AndADropdownsPopupAListOfListItems(Opened what, int count, uint role, uint itemRole)
    {
        using var session = HeadlessSession.Create(400, 300);
        using var bridge = new LinuxAccessibilityBridge(new NoSignals());
        session.Window.InstallAccessibilityBridge(new Active(bridge));
        var popup = Open(session, what, count);

        var tree = (IAtSpiTree)bridge;
        var menu = tree.Find(AtSpiAccessible.PathFor(popup.Id))!;
        Assert.Equal(role, menu.Role);
        Assert.Equal(count > 0 ? count : 3, menu.ChildCount);
        foreach (int i in new[] { 0, menu.ChildCount - 1 })
        {
            var item = tree.Find(menu.ChildPathAt(i)!)!;
            Assert.Equal((itemRole, "click", true), (item.Role, item.ActionName, item.Selectable));
        }
    }

    /// <summary>UIA: a context menu is Menu with MenuItems, each with Invoke and SelectionItem, as a
    /// list item had them, and the menu their selection container with Selection, Scroll and
    /// ItemContainer, as the list was; a dropdown's popup List and ListItem. Past 2,000 items a menu's
    /// are served on demand, and are MenuItems too.</summary>
    [WindowsTheory]
    [InlineData(Opened.ContextMenu, 0, UiaInterop.UIA_MenuControlTypeId, UiaInterop.UIA_MenuItemControlTypeId)]
    [InlineData(Opened.MenuBar, 0, UiaInterop.UIA_MenuControlTypeId, UiaInterop.UIA_MenuItemControlTypeId)]
    [InlineData(Opened.Dropdown, 0, UiaInterop.UIA_ListControlTypeId, UiaInterop.UIA_ListItemControlTypeId)]
    [InlineData(Opened.ContextMenu, 2_001, UiaInterop.UIA_MenuControlTypeId, UiaInterop.UIA_MenuItemControlTypeId)]
    public void ToUia_AMenuIsAMenuOfMenuItems_AndADropdownsPopupAListOfListItems(Opened what, int count, int type, int itemType)
    {
        const int ControlType = 30003;
        using var hwnd = TestHwnd.Create();
        using var session = HeadlessSession.Create(400, 300);
        var window = session.Window.Window;
        var router = new AccessibilityActionRouter(window);
        var host = new UiaHostBridge(window, router);
        window.SetUiaHostBridge(host);
        window.SetActionRouter(router);
        using var bridge = new WindowsAccessibilityBridge(hwnd.Handle, host);
        session.Window.InstallAccessibilityBridge(new Active(bridge));
        var popup = Open(session, what, count);

        INativeSimple Provider(IntPtr native)
        {
            try { return Assert.IsAssignableFrom<INativeSimple>(NativeProvider.LogicOf(native)); }
            finally { Marshal.Release(native); }
        }
        void Has(INativeSimple provider, int pattern)
        {
            Assert.Equal(0, provider.GetPatternProvider(pattern, out var p));
            Assert.NotEqual(IntPtr.Zero, p);
            Marshal.Release(p);
        }

        var menu = Provider(bridge.NativeFragment(popup.Id));
        Assert.Equal(0, menu.GetPropertyValue(ControlType, out var value));
        Assert.Equal(type, value.IntValue);
        Has(menu, UiaInterop.UIA_SelectionPatternId);
        Has(menu, UiaInterop.UIA_ScrollPatternId);
        Has(menu, UiaInterop.UIA_ItemContainerPatternId);
        foreach (int i in new[] { 0, (count > 0 ? count : 3) - 1 })
        {
            var item = Provider(bridge.NativeVirtualItem(popup.Id, i));
            Assert.Equal(0, item.GetPropertyValue(ControlType, out value));
            Assert.Equal(itemType, value.IntValue);
            // "More" opens a menu of its own: with its expanded state on the node it expands, as a
            // menu bar's title does, instead of running a command; every other entry runs one.
            bool opensMenu = what != Opened.Dropdown && count == 0 && i == 2;
            Has(item, opensMenu ? UiaInterop.UIA_ExpandCollapsePatternId : UiaInterop.UIA_InvokePatternId);
            Has(item, UiaInterop.UIA_SelectionItemPatternId);
        }
    }
}
