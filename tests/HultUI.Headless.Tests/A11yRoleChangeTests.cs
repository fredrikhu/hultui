// The AT-SPI bridge is Linux's, but with its signals recorded instead of sent it runs anywhere:
// nothing here reaches a bus.
#pragma warning disable CA1416
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using HultUI.Core.Accessibility;
using HultUI.Core.Builders;
using HultUI.Core.Components;
using HultUI.Core.Elements;
using HultUI.Core.Primitives;
using HultUI.Platform.Accessibility.Linux;
using HultUI.Platform.Accessibility.Windows;
using Xunit;
using TextB = HultUI.Core.Builders.Text;

namespace HultUI.Headless.Tests;

/// <summary>
/// The differ never compared a node's role, so a group that became a dialog was told nothing:
/// UIA raised nothing (since 2235e4a it answered the new control type when asked, once some other
/// change had the tree applied), and on Linux a caching client (Orca, libatspi) kept reading
/// "panel" — seen live in WSL. A role change is now told as Chromium tells ROLE_CHANGED on
/// Windows, a ControlType and a LocalizedControlType property change, and as AT-SPI tells a role
/// change, a property-change:accessible-role signal (already sent for a field masked or revealed).
/// </summary>
public class A11yRoleChangeTests
{
    private sealed class Active(IAccessibilityBridge inner) : IAccessibilityBridge
    {
        public bool IsActive => true;
        public void ApplyChanges(TreeDiff diff, AccessibilityNode root) => inner.ApplyChanges(diff, root);
        public void ApplyPatchedProperties(TreeDiff patch, AccessibilityNode root) => inner.ApplyPatchedProperties(patch, root);
        public void NotifyFocusChanged(AccessibilityNode? focused) => inner.NotifyFocusChanged(focused);
        public void NotifyFocusChanged(AccessibilityNode? focused, AccessibilityNode? previous) => inner.NotifyFocusChanged(focused, previous);
    }

    private sealed class Signals : IAtSpiSignals
    {
        public readonly List<string> Lines = [];
        private static string Short(string path) => path[(path.LastIndexOf('/') + 1)..];
        public void EmitChildrenChanged(string parentPath, string childPath, int index, bool isAdd) => Lines.Add($"children-changed {Short(childPath)}");
        public void EmitPropertyChange(string path, string atSpiProperty, string newValue) => Lines.Add($"property-change:{atSpiProperty} {Short(path)} {newValue}");
        public void EmitValueChanged(string path, double value) => Lines.Add($"property-change:accessible-value {Short(path)}");
        public void EmitRoleChange(string path, uint role) => Lines.Add($"property-change:accessible-role {Short(path)} {role}");
        public void EmitStateChanged(string path, string state, bool active) => Lines.Add($"state-changed:{state} {Short(path)} {(active ? 1 : 0)}");
        public void EmitTextChanged(string path, bool insert, int offset, string text) => Lines.Add($"text-changed {Short(path)}");
        public void EmitTextCaretMoved(string path, int offset) => Lines.Add($"text-caret-moved {Short(path)}");
        public void EmitTextSelectionChanged(string path) => Lines.Add($"text-selection-changed {Short(path)}");
        public void EmitAnnouncement(string path, string text, int politeness) => Lines.Add($"announcement {Short(path)}");
        public void EmitCacheAdd(IAtSpiTree tree, AtSpiAccessible node) => Lines.Add($"cache-add {Short(node.Path)}");
        public void EmitCacheRemove(string path) => Lines.Add($"cache-remove {Short(path)}");
    }

    /// <summary>A component whose build gives one box its role from a field: the change reaches
    /// the mounted box through the patch, as an app's does.</summary>
    private sealed class Sheet : Component
    {
        public AccessibilityRole Role = AccessibilityRole.Group;
        public readonly HultUI.Core.Reactivity.Ref<BoxElement> Panel = new();

        public override Element Build() =>
            Flex.Column().Children(
                TextB.New("Body"),
                Box.New().Width(Dimension.Px(120)).Height(Dimension.Px(40)).AccessibleRole(Role).AccessibleName("Panel")
                    .Children(TextB.New("Inside"))
                    .WithRef(Panel));

        public void Rebuild() => MarkDirty();
    }

    /// <summary>The box whose role changes, mounted and settled; <paramref name="patched"/> mounts
    /// it from a component and changes it through the patch.</summary>
    private static (BoxElement Panel, Action<AccessibilityRole> SetRole) Mount(HeadlessSession session, bool patched)
    {
        BoxElement panel;
        Action<AccessibilityRole> setRole;
        if (patched)
        {
            var sheet = new Sheet();
            session.Window.SetRoot(sheet.Mount());
            panel = sheet.Panel.Current!;
            setRole = role => { sheet.Role = role; sheet.Rebuild(); };
        }
        else
        {
            panel = Box.New().Width(Dimension.Px(120)).Height(Dimension.Px(40))
                .AccessibleRole(AccessibilityRole.Group).AccessibleName("Panel").Children(TextB.New("Inside"));
            session.Window.SetRoot(Flex.Column().Children(TextB.New("Body"), panel));
            setRole = role => panel.Role = role;
        }
        session.PumpFrames(2);
        session.PumpFrame(TimeSpan.FromMilliseconds(120));
        session.PumpFrame(TimeSpan.FromMilliseconds(120));
        return (panel, setRole);
    }

    [Fact]
    public void TheDiffer_TellsARoleChange_WithThePreviousNode()
    {
        var a = Box.New().AccessibleRole(AccessibilityRole.Group).AccessibleName("Panel");
        var root = Box.New().Children(a);
        var before = AccessibilityTreeBuilder.Build(root);
        a.Role = AccessibilityRole.Dialog;
        var after = AccessibilityTreeBuilder.Build(root);

        var diff = AccessibilityTreeDiffer.Diff(before, after);

        var change = Assert.Single(diff.PropertyChanged);
        Assert.Equal(AccessibilityProperties.Role, change.PropertyName);
        Assert.Equal(a.Id, change.Node.ElementId);
        Assert.Equal(AccessibilityRole.Dialog, change.Node.Role);
        Assert.Equal(AccessibilityRole.Group, change.Previous!.Role);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ATSPI_ARoleChange_SendsTheNewRole_AndTheObjectSaysIt(bool patched)
    {
        using var session = HeadlessSession.Create(800, 600);
        var signals = new Signals();
        using var bridge = new LinuxAccessibilityBridge(signals);
        session.Window.InstallAccessibilityBridge(new Active(bridge));
        var (panel, setRole) = Mount(session, patched);
        signals.Lines.Clear();

        setRole(AccessibilityRole.Dialog);
        session.PumpFrame();

        Assert.Equal([$"property-change:accessible-role {panel.Id.Value} {AtSpiConstants.RoleDialog}"], signals.Lines);
        Assert.Equal(AtSpiConstants.RoleDialog, Find(bridge, panel.Id).Role);

        signals.Lines.Clear();
        setRole(AccessibilityRole.Group);
        session.PumpFrame();

        Assert.Equal([$"property-change:accessible-role {panel.Id.Value} {AtSpiConstants.RolePanel}"], signals.Lines);
        Assert.Equal(AtSpiConstants.RolePanel, Find(bridge, panel.Id).Role);
    }

    [Fact]
    public void ATSPI_TheApplicationKeepsItsRole_WhateverItsRootElementsIs()
    {
        using var session = HeadlessSession.Create(800, 600);
        var signals = new Signals();
        using var bridge = new LinuxAccessibilityBridge(signals);
        session.Window.InstallAccessibilityBridge(new Active(bridge));
        var root = Flex.Column().AccessibleRole(AccessibilityRole.Group).Children(TextB.New("Body"));
        session.Window.SetRoot(root);
        session.PumpFrames(2);
        signals.Lines.Clear();

        root.Role = AccessibilityRole.Dialog;
        session.PumpFrame();

        Assert.Empty(signals.Lines);
        Assert.Equal(AtSpiConstants.RoleApplication, ((IAtSpiTree)bridge).Root!.Role);
    }

    [WindowsTheory]
    [InlineData(false)]
    [InlineData(true)]
    [SupportedOSPlatform("windows")]
    public void UIA_ARoleChange_RaisesControlTypeAndLocalizedControlType_AndAnswersTheNewType(bool patched)
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
        var (panel, setRole) = Mount(session, patched);
        Assert.Equal(UiaInterop.UIA_GroupControlTypeId, ControlType(bridge, panel.Id));
        raised.Clear();

        setRole(AccessibilityRole.Dialog);
        session.PumpFrame();

        Assert.Equal(
        [
            new UiaRaised(UiaRaisedKind.Property, UiaInterop.UIA_ControlTypePropertyId, panel.Id),
            new UiaRaised(UiaRaisedKind.Property, UiaInterop.UIA_LocalizedControlTypePropertyId, panel.Id),
        ], raised);
        Assert.Equal(UiaInterop.UIA_WindowControlTypeId, ControlType(bridge, panel.Id));

        raised.Clear();
        setRole(AccessibilityRole.Group);
        session.PumpFrame();

        Assert.Equal(2, raised.Count);
        Assert.Equal(UiaInterop.UIA_GroupControlTypeId, ControlType(bridge, panel.Id));
    }

    private static AtSpiAccessible Find(LinuxAccessibilityBridge bridge, ElementId id) =>
        ((IAtSpiTree)bridge).Find(AtSpiConstants.AccessiblePathPrefix + id.Value)
        ?? throw new InvalidOperationException($"no object for {id.Value}");

    /// <summary>What the element's wrapper answers for ControlType.</summary>
    [SupportedOSPlatform("windows")]
    private static int ControlType(WindowsAccessibilityBridge bridge, ElementId id)
    {
        var native = bridge.NativeFragment(id);
        Assert.NotEqual(IntPtr.Zero, native);
        try
        {
            var wrapper = Assert.IsType<UiaFragment>(NativeProvider.LogicOf(native));
            wrapper.GetPropertyValue(UiaInterop.UIA_ControlTypePropertyId, out var value);
            Assert.Equal(UiaVariant.VT_I4, value.Vt);
            return value.IntValue;
        }
        finally { Marshal.Release(native); }
    }
}
