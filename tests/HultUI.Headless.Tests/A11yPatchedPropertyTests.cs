// The AT-SPI bridge is Linux's, but with its signals recorded instead of sent it runs anywhere:
// nothing here reaches a bus.
#pragma warning disable CA1416
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using HultUI.Core.Accessibility;
using HultUI.Core.Builders;
using HultUI.Core.Components;
using HultUI.Core.Elements;
using HultUI.Core.Events;
using HultUI.Core.Primitives;
using HultUI.Core.Reactivity;
using HultUI.Platform.Accessibility.Linux;
using HultUI.Platform.Accessibility.Windows;
using Xunit;
using TextB = HultUI.Core.Builders.Text;

namespace HultUI.Headless.Tests;

/// <summary>
/// An element's accessible name, description, expanded state and automation id were plain
/// properties. A component's rebuild patches them onto the elements it keeps, and since patching
/// a box, a text, an image, a scroll view or a canvas marks no layout (2688a99, 7016de4), a rebuild
/// that changed only one of them was never told: Aperture's "Quality: …" box, VideoPlayer's
/// Play/Pause canvas. A control's label (a check box's, a radio button's, a switch's) was told only
/// by the layout refresh its new size happened to bring, up to 100 ms late, and never when the
/// size did not change. Each now tells the window when it changes, as a check box's toggle does,
/// and the next frame tells the reader: exactly that change, on both bridges.
/// </summary>
public class A11yPatchedPropertyTests
{
    /// <summary>A real bridge, reported active whether or not a client listens.</summary>
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
        public void EmitChildrenChanged(string parentPath, string childPath, int index, bool isAdd) =>
            Lines.Add($"children-changed:{(isAdd ? "add" : "remove")} {Short(parentPath)} {Short(childPath)} {index}");
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

    /// <summary>A component whose build names, describes or expands one box from its fields, so a
    /// change reaches the mounted box the way an app's does: through the patch.</summary>
    private sealed class Panel : Component
    {
        public string Status = "Settings";
        public string Name = "Quality: 720p";
        public string Description = "first";
        public bool Expanded;
        public readonly Ref<BoxElement> Box = new();

        public override Element Build() =>
            Flex.Column().Children(
                TextB.New(Status),
                HultUI.Core.Builders.Box.New().Width(Dimension.Px(120)).Height(Dimension.Px(24))
                    .AccessibleName(Name).AccessibleDescription(Description).AccessibleExpanded(Expanded)
                    .WithRef(Box));

        public void Rebuild() => MarkDirty();
    }

    /// <summary>One property changed on its own: what to mount, what changes, and which element's
    /// node tells it.</summary>
    private sealed record Scene(Element Root, Element Target, Action Change)
    {
        /// <summary>A change of its own the tree is rebuilt for: by default the first text's.</summary>
        public Action? Bump { get; init; }
    }

    public static TheoryData<string> Cases => new()
    {
        "name", "description", "expanded", "collapsed", "canvas name", "name from Name", "checkbox label",
        "radio label", "switch label", "patched name", "patched description", "patched expanded",
    };

    private static Scene Make(string which)
    {
        BoxElement Column(Element target) =>
            Flex.Column(gap: 4).Children(TextB.New("Status"), target, TextB.New("Footer"));

        switch (which)
        {
            case "name":
            {
                var box = Box.New().Width(Dimension.Px(120)).Height(Dimension.Px(24)).AccessibleName("Inbox (0)");
                return new Scene(Column(box), box, () => box.AccessibleName = "Inbox (1)");
            }
            case "description":
            {
                var box = Box.New().Width(Dimension.Px(120)).Height(Dimension.Px(24))
                    .AccessibleName("Downloads").AccessibleDescription("Downloading, 10 %");
                return new Scene(Column(box), box, () => box.AccessibleDescription = "Downloading, 20 %");
            }
            case "expanded" or "collapsed":
            {
                bool open = which == "expanded";
                var box = Box.New().Width(Dimension.Px(120)).Height(Dimension.Px(24))
                    .AccessibleName("Section").AccessibleExpanded(!open);
                return new Scene(Column(box), box, () => box.AccessibleExpanded = open);
            }
            case "canvas name":
            {
                var canvas = Canvas.New().Width(Dimension.Px(24)).Height(Dimension.Px(24)).AccessibleName("Play");
                return new Scene(Column(canvas), canvas, () => canvas.AccessibleName = "Pause");
            }
            case "name from Name":
            {
                // A button with no text of its own is named by its Name.
                var box = Box.New().Width(Dimension.Px(24)).Height(Dimension.Px(24));
                box.On<ClickEvent>(_ => { });
                box.Name = "Mute";
                return new Scene(Column(box), box, () => box.Name = "Unmute");
            }
            case "checkbox label":
            {
                // A fixed width: the label's change moves nothing.
                var check = Checkbox.New(false, "Opt A").Width(Dimension.Px(160));
                return new Scene(Column(check), check, () => check.Label = "Opt B");
            }
            case "radio label":
            {
                var radio = RadioButton.New("size", false, "Small").Width(Dimension.Px(160));
                return new Scene(Column(radio), radio, () => radio.Label = "Large");
            }
            case "switch label":
            {
                var toggle = ToggleSwitch.New(false, "Wi-Fi").Width(Dimension.Px(160));
                return new Scene(Column(toggle), toggle, () => toggle.Label = "Bluetooth");
            }
            case "patched name" or "patched description" or "patched expanded":
            {
                var panel = new Panel();
                var root = panel.Mount();
                return new Scene(root, panel.Box.Current!, () =>
                {
                    if (which == "patched name") panel.Name = "Quality: 1080p";
                    else if (which == "patched description") panel.Description = "second";
                    else panel.Expanded = true;
                    panel.Rebuild();
                })
                {
                    Bump = () => { panel.Status += "!"; panel.Rebuild(); },
                };
            }
            default:
                throw new ArgumentOutOfRangeException(nameof(which));
        }
    }

    /// <summary>What UIA raises for the change: the property, on the target.</summary>
    private static int UiaProperty(string which) => which switch
    {
        "description" or "patched description" => UiaInterop.UIA_HelpTextPropertyId,
        "expanded" or "collapsed" or "patched expanded" => UiaInterop.UIA_ExpandCollapseExpandCollapseStatePropertyId,
        _ => UiaInterop.UIA_NamePropertyId,
    };

    /// <summary>What AT-SPI sends for the change, on the target's object.</summary>
    private static string AtSpiLine(string which, ElementId target) => which switch
    {
        "name" => $"property-change:accessible-name {target.Value} Inbox (1)",
        "description" => $"property-change:accessible-description {target.Value} Downloading, 20 %",
        "expanded" or "patched expanded" => $"state-changed:expanded {target.Value} 1",
        "collapsed" => $"state-changed:expanded {target.Value} 0",
        "canvas name" => $"property-change:accessible-name {target.Value} Pause",
        "name from Name" => $"property-change:accessible-name {target.Value} Unmute",
        "checkbox label" => $"property-change:accessible-name {target.Value} Opt B",
        "radio label" => $"property-change:accessible-name {target.Value} Large",
        "switch label" => $"property-change:accessible-name {target.Value} Bluetooth",
        "patched name" => $"property-change:accessible-name {target.Value} Quality: 1080p",
        "patched description" => $"property-change:accessible-description {target.Value} second",
        _ => throw new ArgumentOutOfRangeException(nameof(which)),
    };

    /// <summary>Mounts the scene and lets the mount's layout refresh come and go, then has a frame
    /// build the tree for a change of its own (the first text's), so the next 100 ms are inside
    /// the coalesced layout refresh's wait: a change the next frame tells is told for itself, not
    /// by a refresh a new size happened to bring.</summary>
    private static void Mount(HeadlessSession session, Scene scene)
    {
        session.Window.SetRoot(scene.Root);
        session.PumpFrames(2);
        session.PumpFrame(TimeSpan.FromMilliseconds(120));
        session.PumpFrame(TimeSpan.FromMilliseconds(120));
        if (scene.Bump is { } bump) bump();
        else (FirstText(scene.Root) ?? throw new InvalidOperationException("no text in the scene")).Content += "!";
        session.PumpFrame();
    }

    private static TextElement? FirstText(Element element)
    {
        if (element is TextElement text) return text;
        foreach (var child in element.Children)
            if (FirstText(child) is { } found) return found;
        return null;
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void ATSPI_AChangeOnItsOwn_IsToldInTheNextFrame(string which)
    {
        using var session = HeadlessSession.Create(800, 600);
        var signals = new Signals();
        using var bridge = new LinuxAccessibilityBridge(signals);
        session.Window.InstallAccessibilityBridge(new Active(bridge));
        var scene = Make(which);
        Mount(session, scene);
        signals.Lines.Clear();

        scene.Change();
        session.PumpFrame();

        Assert.Equal([AtSpiLine(which, scene.Target.Id)], signals.Lines);
    }

    [WindowsTheory]
    [MemberData(nameof(Cases))]
    [SupportedOSPlatform("windows")]
    public void UIA_AChangeOnItsOwn_IsToldInTheNextFrame(string which)
    {
        using var hwnd = TestHwnd.Create();
        using var session = HeadlessSession.Create(800, 600);
        using var bridge = UiaBridge(session, hwnd);
        var raised = new List<UiaRaised>();
        bridge.Raised = raised.Add;
        var scene = Make(which);
        Mount(session, scene);
        raised.Clear();

        scene.Change();
        session.PumpFrame();

        Assert.Equal([new UiaRaised(UiaRaisedKind.Property, UiaProperty(which), scene.Target.Id)], raised);
    }

    [WindowsFact]
    [SupportedOSPlatform("windows")]
    public void UIA_AnAutomationIdChangedOnItsOwn_IsAnsweredAfterTheNextFrame_WithNoEvent()
    {
        // No event: Chromium raises none for an id, and AT-SPI publishes none. But the tree that
        // carried it is applied, so UI Automation answers the new one; before, a tree whose only
        // change it was went unapplied, and the old id was answered until something else changed.
        using var hwnd = TestHwnd.Create();
        using var session = HeadlessSession.Create(800, 600);
        using var bridge = UiaBridge(session, hwnd);
        var raised = new List<UiaRaised>();
        bridge.Raised = raised.Add;
        var box = Box.New().Width(Dimension.Px(120)).Height(Dimension.Px(24)).AccessibleName("Panel").AutomationId("panel-1");
        Mount(session, new Scene(Flex.Column().Children(TextB.New("Status"), box), box, () => { }));
        Assert.Equal("panel-1", AutomationIdOf(bridge, box.Id));
        raised.Clear();

        box.AutomationId = "panel-2";
        session.PumpFrame();

        Assert.Empty(raised);
        Assert.Equal("panel-2", AutomationIdOf(bridge, box.Id));
    }

    [Fact]
    public void AChangeOnItsOwn_BuildsOneTree_AndANoChangeSetBuildsNone()
    {
        using var session = HeadlessSession.Create(800, 600);
        session.Window.InstallAccessibilityBridge();
        var box = Box.New().Width(Dimension.Px(120)).Height(Dimension.Px(24)).AccessibleName("Inbox (0)").AutomationId("inbox");
        Mount(session, new Scene(Flex.Column().Children(TextB.New("Status"), box), box, () => { }));
        int trees = session.Window.AccessibilityTreesBuilt;

        // The same values again: nothing changed, and nothing is built.
        box.AccessibleName = "Inbox (0)";
        box.AutomationId = "inbox";
        box.Role = AccessibilityRole.None;
        box.AccessibleDescription = null;
        box.AccessibleExpanded = null;
        session.PumpFrame();
        Assert.Equal(trees, session.Window.AccessibilityTreesBuilt);

        box.AccessibleName = "Inbox (1)";
        session.PumpFrame();
        Assert.Equal(trees + 1, session.Window.AccessibilityTreesBuilt);
    }

    [SupportedOSPlatform("windows")]
    private static WindowsAccessibilityBridge UiaBridge(HeadlessSession session, TestHwnd hwnd)
    {
        var window = session.Window.Window;
        var router = new AccessibilityActionRouter(window);
        var host = new UiaHostBridge(window, router);
        window.SetUiaHostBridge(host);
        window.SetActionRouter(router);
        var bridge = new WindowsAccessibilityBridge(hwnd.Handle, host);
        session.Window.InstallAccessibilityBridge(new Active(bridge));
        return bridge;
    }

    /// <summary>What the element's wrapper answers for AutomationId.</summary>
    [SupportedOSPlatform("windows")]
    private static string? AutomationIdOf(WindowsAccessibilityBridge bridge, ElementId id)
    {
        var native = bridge.NativeFragment(id);
        Assert.NotEqual(IntPtr.Zero, native);
        try
        {
            var wrapper = Assert.IsType<UiaFragment>(NativeProvider.LogicOf(native));
            wrapper.GetPropertyValue(UiaInterop.UIA_AutomationIdPropertyId, out var value);
            try { return value.Vt == UiaVariant.VT_BSTR ? value.AsString() : null; }
            finally { value.Free(); }
        }
        finally { Marshal.Release(native); }
    }
}
