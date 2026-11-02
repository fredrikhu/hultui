using System.Runtime.InteropServices;
using HultUI.Core.Accessibility;
using HultUI.Core.Builders;
using HultUI.Core.Components;
using HultUI.Core.Elements;
using HultUI.Core.Events;
using HultUI.Core.Layout;
using HultUI.Core.Primitives;
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
}
