// The AT-SPI bridge is Linux's, but with its signals recorded instead of sent it runs anywhere:
// nothing here reaches a bus.
#pragma warning disable CA1416
using System.Globalization;
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
/// A slider, a progress bar or a number field that moved told nothing: its node carried no value
/// and AT-SPI offered no Value interface, so a keyboard user moving VideoPlayer's Seek slider heard
/// nothing. As Chromium: a UIA RangeValue.Value property change (and Minimum, Maximum, the steps
/// when they change), AT-SPI's Value interface and an accessible-value signal with the number. A
/// range can move on every frame (a download, a playback, a key held down), so the window tells it
/// on the leading edge, then at most every 250 ms, then once with the value it came to rest at.
/// </summary>
public class A11yRangeValueTests
{
    private const int RangeValue = UiaInterop.UIA_RangeValueValuePropertyId;

    private sealed class Active(IAccessibilityBridge inner) : IAccessibilityBridge
    {
        public bool IsActive => true;
        public void ApplyChanges(TreeDiff diff, AccessibilityNode root) => inner.ApplyChanges(diff, root);
        public void NotifyFocusChanged(AccessibilityNode? focused) => inner.NotifyFocusChanged(focused);
        public void NotifyFocusChanged(AccessibilityNode? focused, AccessibilityNode? previous) => inner.NotifyFocusChanged(focused, previous);
    }

    private static (SliderElement Slider, ProgressBarElement Bar, NumericUpDownElement Number, Element Root) Fixture()
    {
        var slider = Slider.New(50, 0, 100).Width(Dimension.Px(300)).AccessibleName("Level");
        var bar = ProgressBar.New().Width(Dimension.Px(300));
        var number = NumericUpDown.New(5, 0, 10);
        var rows = Flex.Column().FlexShrink(0);
        for (int i = 0; i < 50; i++) rows.AddChild(TextB.New($"row {i}"));
        return (slider, bar, number, Flex.Column(gap: 8).Padding(16).Children(slider, bar, number, rows));
    }

    private static void Mount(HeadlessSession session, Element root)
    {
        session.Window.SetRoot(root);
        session.PumpFrames(2);
        // A quiet spell, so the next change is on the leading edge.
        session.PumpFrame(TimeSpan.FromMilliseconds(400));
    }

    // ── UIA ──

    [SupportedOSPlatform("windows")]
    private sealed class UiaRig : IDisposable
    {
        private readonly TestHwnd _hwnd = TestHwnd.Create();
        public HeadlessSession Session { get; } = HeadlessSession.Create(600, 900);
        public WindowsAccessibilityBridge Bridge { get; }
        public readonly List<(UiaRaised Raised, double Value)> Raised = [];
        private readonly Dictionary<ElementId, Element> _elements = [];

        public UiaRig()
        {
            var window = Session.Window.Window;
            var router = new AccessibilityActionRouter(window);
            var host = new UiaHostBridge(window, router);
            window.SetUiaHostBridge(host);
            window.SetActionRouter(router);
            Bridge = new WindowsAccessibilityBridge(_hwnd.Handle, host);
            // Each range event with the value it carried, read from the node it was raised for.
            Bridge.Raised = r =>
            {
                double value = double.NaN;
                if (r.Kind == UiaRaisedKind.Property && r.Element is { } id && Wrapper(id) is { } w)
                {
                    w.GetPropertyValue(r.Id, out var v);
                    if (v.Vt == UiaVariant.VT_R8) value = v.DoubleValue;
                    v.Free();
                }
                Raised.Add((r, value));
            };
            Session.Window.InstallAccessibilityBridge(new Active(Bridge));
        }

        public UiaFragment? Wrapper(ElementId id)
        {
            var native = Bridge.NativeFragment(id);
            if (native == IntPtr.Zero) return null;
            try { return NativeProvider.LogicOf(native) as UiaFragment; }
            finally { Marshal.Release(native); }
        }

        public List<double> RangeEvents(Element element, int property = RangeValue) =>
            [.. Raised.Where(r => r.Raised.Kind == UiaRaisedKind.Property && r.Raised.Id == property && r.Raised.Element == element.Id)
                .Select(r => r.Value)];

        public void Dispose()
        {
            Bridge.Dispose();
            Session.Dispose();
            _hwnd.Dispose();
        }
    }

    [WindowsFact]
    [SupportedOSPlatform("windows")]
    public void UIA_AKeyOnASlider_IsARangeValueChange_AndKeysHeldDownAreToldAsTheyComeToRest()
    {
        using var rig = new UiaRig();
        var (slider, bar, _, root) = Fixture();
        Mount(rig.Session, root);
        slider.Focus();
        rig.Session.PumpFrame(TimeSpan.FromMilliseconds(400));
        rig.Raised.Clear();

        rig.Session.Window.PressKey(KeyCode.Right);
        rig.Session.PumpFrame();
        Assert.Equal([51.0], rig.RangeEvents(slider));   // at once

        // Held down: a key every frame. Told at most every 250 ms, then where it stopped.
        for (int i = 0; i < 30; i++)
        {
            rig.Session.Window.PressKey(KeyCode.Right);
            rig.Session.PumpFrame();
        }
        rig.Session.PumpFrames(30);
        var told = rig.RangeEvents(slider);
        Assert.InRange(told.Count, 3, 4);   // 500 ms of keys: the first, two or three on the way
        Assert.Equal(81.0, told[^1]);
        Assert.Equal(81.0, ((UiaFragment)rig.Wrapper(slider.Id)!).GetPropertyValue(RangeValue, out var v) == 0 ? v.DoubleValue : -1);

        // Its bounds: a Maximum change.
        rig.Raised.Clear();
        bar.Maximum = 2;
        rig.Session.PumpFrames(20);
        Assert.Equal([2.0], rig.RangeEvents(bar, UiaInterop.UIA_RangeValueMaximumPropertyId));
        Assert.Empty(rig.RangeEvents(bar));   // the value did not move
    }

    [WindowsFact]
    [SupportedOSPlatform("windows")]
    public void UIA_AProgressBarMovingEveryFrame_IsToldFourTimesASecond_NotEveryFrame()
    {
        using var rig = new UiaRig();
        var (_, bar, _, root) = Fixture();
        Mount(rig.Session, root);
        rig.Raised.Clear();

        // Two seconds of frames, the bar moving on each: 120 changes.
        for (int i = 1; i <= 120; i++)
        {
            bar.Value = i / 200.0;
            rig.Session.PumpFrame();
        }
        rig.Session.PumpFrames(30);
        var told = rig.RangeEvents(bar);
        Assert.InRange(told.Count, 8, 10);
        Assert.Equal(0.6, told[^1], 6);
    }

    // ── AT-SPI ──

    private sealed class Signals : IAtSpiSignals
    {
        public readonly List<string> Lines = [];
        private static string Short(string path) =>
            path.StartsWith(AtSpiConstants.AccessiblePathPrefix, StringComparison.Ordinal)
                ? path[AtSpiConstants.AccessiblePathPrefix.Length..] : "root";
        public void EmitChildrenChanged(string parentPath, string childPath, int index, bool isAdd) => Lines.Add($"children-changed {Short(parentPath)}");
        public void EmitPropertyChange(string path, string atSpiProperty, string newValue) => Lines.Add($"property-change:{atSpiProperty} {Short(path)} '{newValue}'");
        public void EmitValueChanged(string path, double value) =>
            Lines.Add($"property-change:accessible-value {Short(path)} {value.ToString(CultureInfo.InvariantCulture)}");
        public void EmitRoleChange(string path, uint role) => Lines.Add($"property-change:accessible-role {Short(path)}");
        public void EmitStateChanged(string path, string state, bool active) => Lines.Add($"state-changed:{state} {Short(path)} {(active ? 1 : 0)}");
        public void EmitTextChanged(string path, bool insert, int offset, string text) => Lines.Add($"text-changed {Short(path)}");
        public void EmitTextCaretMoved(string path, int offset) => Lines.Add($"text-caret-moved {Short(path)}");
        public void EmitTextSelectionChanged(string path) => Lines.Add($"text-selection-changed {Short(path)}");
        public void EmitAnnouncement(string path, string text, int politeness) => Lines.Add($"announcement {Short(path)}");
        public void EmitCacheAdd(IAtSpiTree tree, AtSpiAccessible node) => Lines.Add($"cache-add {Short(node.Path)}");
        public void EmitCacheRemove(string path) => Lines.Add($"cache-remove {Short(path)}");
    }

    private static AtSpiAccessible Find(LinuxAccessibilityBridge bridge, Element element) =>
        ((IAtSpiTree)bridge).Find(AtSpiConstants.AccessiblePathPrefix + element.Id.Value)
        ?? throw new InvalidOperationException($"no object for {element}");

    [Fact]
    public void ATSPI_ARangeOffersTheValueInterface_ToldByAccessibleValue_AndSetThroughIt()
    {
        using var session = HeadlessSession.Create(600, 900);
        var signals = new Signals();
        var host = session.Window.InstallActionRouter();
        using var bridge = new LinuxAccessibilityBridge(signals, host);
        session.Window.InstallAccessibilityBridge(new Active(bridge));
        var (slider, bar, number, root) = Fixture();
        Mount(session, root);

        foreach (var element in new Element[] { slider, bar, number })
            Assert.Contains(AtSpiConstants.ValueInterface, Find(bridge, element).Interfaces);
        Assert.Equal(new AccessibleRange(50, 0, 100, 1, 10, false), Find(bridge, slider).Range);
        Assert.DoesNotContain(AtSpiConstants.ValueInterface, Find(bridge, root.Children[3].Children[0]).Interfaces);

        signals.Lines.Clear();
        slider.Value = 60;
        session.PumpFrame();
        Assert.Equal([$"property-change:accessible-value {slider.Id.Value} 60"], signals.Lines);
        Assert.Equal(60, Find(bridge, slider).Range!.Value.Value);

        // A number field's text and its number change together: one accessible-value, the number.
        signals.Lines.Clear();
        number.Focus();
        session.PumpFrame(TimeSpan.FromMilliseconds(300));
        signals.Lines.Clear();
        session.Window.PressKey(KeyCode.Up);
        session.PumpFrame();
        Assert.Equal([$"property-change:accessible-value {number.Id.Value} 6"], signals.Lines);

        // Value.CurrentValue set by a client: through the action host, on the UI thread.
        Assert.True(((IAtSpiTree)bridge).SetCurrentValue(Find(bridge, slider), 25));
        session.PumpFrames(20);
        Assert.Equal(25, slider.Value);
        Assert.Contains($"property-change:accessible-value {slider.Id.Value} 25", signals.Lines);
        Assert.False(((IAtSpiTree)bridge).SetCurrentValue(Find(bridge, bar), 0.5));   // read-only
    }
}
