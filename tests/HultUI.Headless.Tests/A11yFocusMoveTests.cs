// The AT-SPI bridge is Linux's, but with its signals recorded instead of sent it runs anywhere:
// nothing here reaches a bus.
#pragma warning disable CA1416
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using HultUI.Core.Accessibility;
using HultUI.Core.Builders;
using HultUI.Core.Elements;
using HultUI.Core.Events;
using HultUI.Platform.Accessibility.Linux;
using HultUI.Platform.Accessibility.Windows;
using Xunit;
using TextB = HultUI.Core.Builders.Text;

namespace HultUI.Headless.Tests;

/// <summary>
/// A focus move rebuilds no accessibility tree. The bridges kept the focus state of the last
/// rebuild, so a reader asking the element that had the focus was told it still had it, and the
/// one that took it that it had not; and the next rebuild's diff (a keystroke, a toggle, any
/// change a reader sees) reported the move then, late: UIA HasKeyboardFocus events on Windows,
/// a second pair of state-changed:focused on Linux. The window now marks the move on the
/// applied tree's two nodes and the bridges take it at once.
/// </summary>
public class A11yFocusMoveTests
{
    private const int HasKeyboardFocus = UiaInterop.UIA_HasKeyboardFocusPropertyId;
    private const int FocusChanged = UiaInterop.UIA_AutomationFocusChangedEventId;

    /// <summary>Forwards to a real bridge, records the diffs, and can stay deaf until told.</summary>
    private sealed class Forwarding(IAccessibilityBridge inner) : IAccessibilityBridge
    {
        public bool Active = true;
        public readonly List<TreeDiff> Diffs = [];
        public bool IsActive => Active;
        public void ApplyChanges(TreeDiff diff, AccessibilityNode root)
        {
            Diffs.Add(diff);
            inner.ApplyChanges(diff, root);
        }
        public void NotifyFocusChanged(AccessibilityNode? focused) => inner.NotifyFocusChanged(focused);
        public void NotifyFocusChanged(AccessibilityNode? focused, AccessibilityNode? previous) => inner.NotifyFocusChanged(focused, previous);
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

    private static (TextInputElement[] Fields, Element Root) Form()
    {
        var fields = new TextInputElement[3];
        var rows = new Element[fields.Length];
        for (int i = 0; i < fields.Length; i++)
        {
            fields[i] = TextInput.New($"value {i}");
            rows[i] = Flex.Row(gap: 8).Children(TextB.New($"Field {i}"), fields[i]);
        }
        return (fields, Flex.Column().Children(rows));
    }

    private static string Id(Element element) => element.Id.Value.ToString();

    // ── UIA ──

    [SupportedOSPlatform("windows")]
    private sealed class UiaRig : IDisposable
    {
        private readonly TestHwnd _hwnd = TestHwnd.Create();
        public HeadlessSession Session { get; } = HeadlessSession.Create(800, 600);
        public WindowsAccessibilityBridge Bridge { get; }
        public Forwarding Forward { get; }
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
            Forward = Session.Window.InstallAccessibilityBridge(new Forwarding(Bridge));
        }

        /// <summary>What the element's wrapper answers for HasKeyboardFocus.</summary>
        public bool HasFocus(Element element)
        {
            var native = Bridge.NativeFragment(element.Id);
            Assert.NotEqual(IntPtr.Zero, native);
            try
            {
                var wrapper = Assert.IsType<UiaFragment>(NativeProvider.LogicOf(native));
                wrapper.GetPropertyValue(HasKeyboardFocus, out var value);
                Assert.Equal(UiaVariant.VT_BOOL, value.Vt);
                return value.ShortValue != 0;
            }
            finally { Marshal.Release(native); }
        }

        public void Dispose()
        {
            Bridge.Dispose();
            Session.Dispose();
            _hwnd.Dispose();
        }
    }

    [WindowsFact]
    public void UIA_ATab_ChangesBothFocusStates_AtOnce_AndTheNextRebuildDoesNotTellItAgain()
    {
        using var rig = new UiaRig();
        var (fields, root) = Form();
        rig.Session.Window.SetRoot(root);
        rig.Session.PumpFrames(2);
        rig.Session.Window.PressKey(KeyCode.Tab);
        rig.Session.PumpFrame();
        Assert.True(rig.HasFocus(fields[0]));
        rig.Raised.Clear();

        rig.Session.Window.PressKey(KeyCode.Tab);
        rig.Session.PumpFrame();

        Assert.False(rig.HasFocus(fields[0]));
        Assert.True(rig.HasFocus(fields[1]));
        // The state changes go out with the focus event, before it.
        Assert.Equal(
        [
            new UiaRaised(UiaRaisedKind.Property, HasKeyboardFocus, fields[0].Id),
            new UiaRaised(UiaRaisedKind.Property, HasKeyboardFocus, fields[1].Id),
            new UiaRaised(UiaRaisedKind.Event, FocusChanged, fields[1].Id),
        ], rig.Raised);

        // A change a reader sees rebuilds the tree: its diff carries the edit, not the move.
        rig.Raised.Clear();
        rig.Forward.Diffs.Clear();
        rig.Session.Window.TypeText("x");
        rig.Session.PumpFrame();
        Assert.NotEmpty(rig.Forward.Diffs);
        Assert.DoesNotContain(rig.Forward.Diffs.SelectMany(d => d.PropertyChanged),
            c => c.PropertyName == AccessibilityProperties.IsFocused);
        Assert.DoesNotContain(rig.Raised, e => e.Id == HasKeyboardFocus);
        Assert.False(rig.HasFocus(fields[0]));
        Assert.True(rig.HasFocus(fields[1]));
    }

    [WindowsFact]
    public void UIA_AClientThatAttachesLate_IsToldTheFirstMoveToo()
    {
        // The tree a reader attaches to marks the field that already had the focus; the bridge
        // was never told of that one. The first Tab must still clear it.
        using var rig = new UiaRig();
        rig.Forward.Active = false;
        var (fields, root) = Form();
        rig.Session.Window.SetRoot(root);
        rig.Session.PumpFrames(2);
        rig.Session.Window.PressKey(KeyCode.Tab);
        rig.Session.PumpFrame();
        rig.Forward.Active = true;
        rig.Session.PumpFrame();
        Assert.True(rig.HasFocus(fields[0]));
        rig.Raised.Clear();

        rig.Session.Window.PressKey(KeyCode.Tab);
        rig.Session.PumpFrame();

        Assert.False(rig.HasFocus(fields[0]));
        Assert.True(rig.HasFocus(fields[1]));
        Assert.Equal(
        [
            new UiaRaised(UiaRaisedKind.Property, HasKeyboardFocus, fields[0].Id),
            new UiaRaised(UiaRaisedKind.Property, HasKeyboardFocus, fields[1].Id),
            new UiaRaised(UiaRaisedKind.Event, FocusChanged, fields[1].Id),
        ], rig.Raised);
    }

    // ── AT-SPI ──

    private sealed class AtSpiRig : IDisposable
    {
        public HeadlessSession Session { get; } = HeadlessSession.Create(800, 600);
        public readonly Signals Signals = new();
        public LinuxAccessibilityBridge Bridge { get; }
        public Forwarding Forward { get; }

        public AtSpiRig()
        {
            Bridge = new LinuxAccessibilityBridge(Signals);
            Forward = Session.Window.InstallAccessibilityBridge(new Forwarding(Bridge));
        }

        public bool Focused(Element element) =>
            ((IAtSpiTree)Bridge).Find(AtSpiConstants.AccessiblePathPrefix + element.Id.Value)!.Focused;

        public void Dispose()
        {
            Bridge.Dispose();
            Session.Dispose();
        }
    }

    [Fact]
    public void ATSPI_ATab_ChangesBothFocusStates_AtOnce_AndTheNextRebuildDoesNotTellItAgain()
    {
        using var rig = new AtSpiRig();
        var (fields, root) = Form();
        rig.Session.Window.SetRoot(root);
        rig.Session.PumpFrames(2);
        rig.Session.Window.PressKey(KeyCode.Tab);
        rig.Session.PumpFrame();
        Assert.True(rig.Focused(fields[0]));
        rig.Signals.Lines.Clear();

        rig.Session.Window.PressKey(KeyCode.Tab);
        rig.Session.PumpFrame();

        Assert.False(rig.Focused(fields[0]));
        Assert.True(rig.Focused(fields[1]));
        Assert.Equal([$"state-changed:focused {Id(fields[0])} 0", $"state-changed:focused {Id(fields[1])} 1"], rig.Signals.Lines);

        // The next rebuild sent both again.
        rig.Signals.Lines.Clear();
        rig.Session.Window.TypeText("x");
        rig.Session.PumpFrame();
        Assert.NotEmpty(rig.Signals.Lines);
        Assert.DoesNotContain(rig.Signals.Lines, l => l.StartsWith("state-changed:focused", StringComparison.Ordinal));
        Assert.False(rig.Focused(fields[0]));
        Assert.True(rig.Focused(fields[1]));
    }

    [Fact]
    public void ATSPI_AClientThatAttachesLate_IsToldTheFirstMoveToo()
    {
        using var rig = new AtSpiRig();
        rig.Forward.Active = false;
        var (fields, root) = Form();
        rig.Session.Window.SetRoot(root);
        rig.Session.PumpFrames(2);
        rig.Session.Window.PressKey(KeyCode.Tab);
        rig.Session.PumpFrame();
        rig.Forward.Active = true;
        rig.Session.PumpFrame();
        Assert.True(rig.Focused(fields[0]));
        rig.Signals.Lines.Clear();

        rig.Session.Window.PressKey(KeyCode.Tab);
        rig.Session.PumpFrame();

        Assert.False(rig.Focused(fields[0]));
        Assert.True(rig.Focused(fields[1]));
        Assert.Equal([$"state-changed:focused {Id(fields[0])} 0", $"state-changed:focused {Id(fields[1])} 1"], rig.Signals.Lines);
    }
}
