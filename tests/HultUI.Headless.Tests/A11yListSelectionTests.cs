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
/// list's <see cref="VirtualListElement{T}.SelectedIndex"/> told nothing to rebuild it. A move of
/// the selection that scrolled nothing changed no node: UIA raised nothing, AT-SPI said every item
/// was unselected, and a screen reader never read which item was selected. Each item node now
/// carries <see cref="AccessibilityNode.IsSelected"/> from its list, and the setter asks for a
/// rebuild, so a move is two IsSelected changes: the item that had the selection and the one that
/// has it.
/// </summary>
public class A11yListSelectionTests
{
    private const int IsSelectedProperty = UiaInterop.UIA_SelectionItemIsSelectedPropertyId;

    /// <summary>A real bridge, reported active whether or not a client listens.</summary>
    private sealed class Active(IAccessibilityBridge inner) : IAccessibilityBridge
    {
        public bool IsActive => true;
        public void ApplyChanges(TreeDiff diff, AccessibilityNode root) => inner.ApplyChanges(diff, root);
        public void NotifyFocusChanged(AccessibilityNode? focused) => inner.NotifyFocusChanged(focused);
        public void NotifyFocusChanged(AccessibilityNode? focused, AccessibilityNode? previous) => inner.NotifyFocusChanged(focused, previous);
    }

    /// <summary>Twenty items, all on screen: no move scrolls.</summary>
    private static VirtualListElement<string> List()
    {
        var items = Enumerable.Range(0, 20).Select(i => $"item {i}").ToList();
        return VirtualListElement<string>.New().ItemHeight(24)
            .RenderWith(t => Flex.Row().Padding(new Thickness(2, 8)).Children(TextB.New(t).FontSize(12f)))
            .Items(items);
    }

    private static ElementId Item(Element list, int index) => VirtualItemId.For(list.Id, index);

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
        session.Window.SetRoot(Flex.Column().FlexGrow(1).Children(list.FlexGrow(1)));
        session.PumpFrames(2);
        list.SelectedIndex = 3;
        session.PumpFrame();
        Assert.True(Selected(bridge, Item(list, 3)));
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
        session.Window.SetRoot(Flex.Column().FlexGrow(1).Children(list.FlexGrow(1)));
        session.PumpFrames(2);
        list.SelectedIndex = 3;
        session.PumpFrame();
        Assert.True(Find(bridge, Item(list, 3)).Selected);
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
    }

    private static AtSpiAccessible Find(LinuxAccessibilityBridge bridge, ElementId item) =>
        ((IAtSpiTree)bridge).Find(AtSpiConstants.AccessiblePathPrefix + item.Value)
        ?? throw new InvalidOperationException($"no object for {item.Value}");
}
