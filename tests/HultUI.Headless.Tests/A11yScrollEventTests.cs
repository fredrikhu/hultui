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
/// What a scroll tells a screen reader. Every rebuild during a scroll raised a UIA
/// BoundingRectangle event for every node on screen (133 per rebuild over 1,000 rows), each with
/// no value. As Chromium does (browser_accessibility_manager_win.cc: a scroller's
/// SCROLL_VERTICAL/HORIZONTAL_POSITION_CHANGED is a ScrollPercent property change on it, and no
/// node under it is told it moved), the bridge now raises one VerticalScrollPercent or
/// HorizontalScrollPercent change on the scroll view, and bounds events only for nodes that moved
/// within it. A reader that asks still gets current bounds. On AT-SPI a scroll sent nothing
/// before and sends nothing now, as Chromium and GTK.
/// </summary>
public class A11yScrollEventTests
{
    private const int VerticalScrollPercent = UiaInterop.UIA_ScrollVerticalScrollPercentPropertyId;
    private const int BoundingRectangle = UiaInterop.UIA_BoundingRectanglePropertyId;

    private sealed class Active(IAccessibilityBridge inner) : IAccessibilityBridge
    {
        public bool IsActive => true;
        public void ApplyChanges(TreeDiff diff, AccessibilityNode root) => inner.ApplyChanges(diff, root);
        public void NotifyFocusChanged(AccessibilityNode? focused) => inner.NotifyFocusChanged(focused);
        public void NotifyFocusChanged(AccessibilityNode? focused, AccessibilityNode? previous) => inner.NotifyFocusChanged(focused, previous);
    }

    // 40 named rows of 30, each with its label, in a 200-high view: 1,000 to scroll, 25 notches of 40.
    private static (ScrollViewElement View, BoxElement[] Rows, Element Root) Fixture()
    {
        var rows = Enumerable.Range(0, 40)
            .Select(i => Box.New().AccessibleName($"row {i}").Height(30f).FlexShrink(0).Children(TextB.New($"row {i}")))
            .ToArray();
        var view = ScrollView.New().Width(300f).Height(200f).Children(Flex.Column(gap: 0).Children(rows));
        return (view, rows, Box.New().Children(view));
    }

    private static void Mount(HeadlessSession session, Element root)
    {
        session.Application.SetRoot(root);
        session.PumpFrame();
        // A quiet spell, so the next scroll's rebuild comes in its own frame.
        session.PumpFrame(TimeSpan.FromMilliseconds(200));
    }

    private static void WheelDown(HeadlessSession session) => session.Window.Wheel(150, 100, 0, -1);

    // ── UIA ──

    [SupportedOSPlatform("windows")]
    private sealed class UiaRig : IDisposable
    {
        private readonly TestHwnd _hwnd = TestHwnd.Create();
        public HeadlessSession Session { get; } = HeadlessSession.Create(400, 300);
        public WindowsAccessibilityBridge Bridge { get; }
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

        public UiaFragment Wrapper(Element element)
        {
            var native = Bridge.NativeFragment(element.Id);
            Assert.NotEqual(IntPtr.Zero, native);
            try { return Assert.IsType<UiaFragment>(NativeProvider.LogicOf(native)); }
            finally { Marshal.Release(native); }
        }

        /// <summary>What a client is told the element's box on screen is.</summary>
        public UiaRect BoundingRectangle(Element element)
        {
            Wrapper(element).GetBoundingRectangle(out var rect);
            return rect;
        }

        public bool IsOffscreen(Element element)
        {
            Wrapper(element).GetPropertyValue(UiaInterop.UIA_IsOffscreenPropertyId, out var value);
            return value.ShortValue != 0;
        }

        /// <summary>The element's box in the window's current accessibility tree, on screen.</summary>
        public UiaRect Expected(Element element)
        {
            var node = Find(Session.Window.AccessibilityTree!, element.Id)!;
            var screen = Bridge.ToScreen(node.BoundingRect);
            return new UiaRect { Left = screen.X, Top = screen.Y, Width = screen.Width, Height = screen.Height };
        }

        public void Dispose()
        {
            Bridge.Dispose();
            Session.Dispose();
            _hwnd.Dispose();
        }
    }

    private static AccessibilityNode? Find(AccessibilityNode node, ElementId id)
    {
        if (node.ElementId == id) return node;
        foreach (var child in node.Children)
            if (Find(child, id) is { } found) return found;
        return null;
    }

    [WindowsFact]
    public void UIA_AScroll_IsOneScrollPercentChange_OnTheScrollView()
    {
        using var rig = new UiaRig();
        var (view, rows, root) = Fixture();
        Mount(rig.Session, root);
        var rowBefore = rig.BoundingRectangle(rows[3]);
        Assert.False(rig.IsOffscreen(rows[0]));
        rig.Raised.Clear();

        for (int i = 0; i < 3; i++) WheelDown(rig.Session);
        rig.Session.PumpFrame();

        Assert.Equal(120, view.ScrollOffsetY);
        Assert.Equal([new UiaRaised(UiaRaisedKind.Property, VerticalScrollPercent, view.Id)], rig.Raised);
        // Nothing under the view was told it moved, yet each answers where it is now.
        Assert.Equal(rig.Expected(rows[3]), rig.BoundingRectangle(rows[3]));
        Assert.Equal(rowBefore.Top - 120, rig.BoundingRectangle(rows[3]).Top, 3);
        Assert.True(rig.IsOffscreen(rows[0]));
        Assert.False(rig.IsOffscreen(rows[8]));
    }

    [WindowsFact]
    public void UIA_ANodeThatMovesWithinTheScrollView_StillRaisesItsBounds()
    {
        using var rig = new UiaRig();
        var (view, rows, root) = Fixture();
        Mount(rig.Session, root);
        rig.Raised.Clear();

        // Row 1 grows: it and every row below it move within the view's content. The view
        // scrolls in the same frame.
        rows[1].Box.Height = 50f;
        rows[1].MarkLayoutDirty();
        WheelDown(rig.Session);
        rig.Session.PumpFrame();

        var bounds = rig.Raised.Where(e => e.Kind == UiaRaisedKind.Property && e.Id == BoundingRectangle)
            .Select(e => e.Element).ToList();
        Assert.Contains(rows[1].Id, bounds);   // resized
        Assert.Contains(rows[2].Id, bounds);   // moved within the content
        Assert.DoesNotContain(rows[0].Id, bounds);   // only scrolled
        Assert.Contains(new UiaRaised(UiaRaisedKind.Property, VerticalScrollPercent, view.Id), rig.Raised);
        Assert.Equal(rig.Expected(rows[2]), rig.BoundingRectangle(rows[2]));
    }

    // ── AT-SPI ──

    private sealed class Signals : IAtSpiSignals
    {
        public readonly List<string> Lines = [];
        public void EmitChildrenChanged(string parentPath, string childPath, int index, bool isAdd) => Lines.Add("children-changed");
        public void EmitPropertyChange(string path, string atSpiProperty, string newValue) => Lines.Add($"property-change:{atSpiProperty}");
        public void EmitRoleChange(string path, uint role) => Lines.Add("property-change:accessible-role");
        public void EmitStateChanged(string path, string state, bool active) => Lines.Add($"state-changed:{state}");
        public void EmitTextChanged(string path, bool insert, int offset, string text) => Lines.Add("text-changed");
        public void EmitTextCaretMoved(string path, int offset) => Lines.Add("text-caret-moved");
        public void EmitTextSelectionChanged(string path) => Lines.Add("text-selection-changed");
        public void EmitAnnouncement(string path, string text, int politeness) => Lines.Add("announcement");
        public void EmitCacheAdd(IAtSpiTree tree, AtSpiAccessible node) => Lines.Add("cache-add");
        public void EmitCacheRemove(string path) => Lines.Add("cache-remove");
    }

    [Fact]
    public void ATSPI_AScroll_SendsNothing_AndTheObjectsAreWhereTheyAreNow()
    {
        using var session = HeadlessSession.Create(400, 300);
        var signals = new Signals();
        using var bridge = new LinuxAccessibilityBridge(signals);
        session.Window.InstallAccessibilityBridge(new Active(bridge));
        var (view, rows, root) = Fixture();
        Mount(session, root);
        var tree = (IAtSpiTree)bridge;
        string row3 = AtSpiConstants.AccessiblePathPrefix + rows[3].Id.Value;
        int before = tree.Find(row3)!.Y;
        signals.Lines.Clear();

        for (int i = 0; i < 3; i++) WheelDown(session);
        session.PumpFrame();

        Assert.Equal(120, view.ScrollOffsetY);
        Assert.Empty(signals.Lines);
        Assert.Equal(before - 120, tree.Find(row3)!.Y);
    }
}
