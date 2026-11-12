using System.Runtime.Versioning;
using HultUI.Core.Accessibility;
using HultUI.Core.Builders;
using HultUI.Core.Elements;
using HultUI.Core.Layout;
using HultUI.Core.Primitives;
using HultUI.Platform.Accessibility.Windows;
using Xunit;
using TextB = HultUI.Core.Builders.Text;

namespace HultUI.Headless.Tests;

/// <summary>
/// A live resize tells a reader the bounds once, when the size has rested (plan 17 round 12 step 3; the
/// user's yes to question 47). The coalesced layout refresh came at the drag's first step, then every
/// 100 ms of it, then once at its end, and each raised a BoundingRectangle change for every element
/// that moved, answered by 13 provider calls and 13 QueryInterface calls on the UI thread (Vesper: 1,596
/// raises, 20,930 calls a resize of ten steps). Now it waits until the size has not changed for 100 ms:
/// one refresh, each moved node told once, the tree what a build makes then.
/// </summary>
public class A11yLiveResizeTests
{
    private static (BoxElement Root, TextElement Footer) Scene()
    {
        var rows = new Element[30];
        for (int i = 0; i < rows.Length; i++)
            // Centred, so every label moves with the width.
            rows[i] = Flex.Row(gap: 8).FlexShrink(0).Padding(new Thickness(2, 8)).JustifyContent(JustifyContent.Center).Children(
                TextB.New($"{i}").FontSize(12f).Width(Dimension.Px(40)), TextB.New($"row {i} label").FontSize(12f));
        var footer = TextB.New("footer").FontSize(12f);
        var root = Flex.Column(gap: 4).FlexGrow(1).Children(
            TextB.New("header").FontSize(12f),
            ScrollView.New().FlexGrow(1).Children(Flex.Column().FlexShrink(0).Children(rows)),
            footer);
        return (root, footer);
    }

    /// <summary>Ten steps of a drag a frame apart (16 ms each, 160 ms in all): nothing is told while the
    /// size changes; 100 ms after the last step one refresh tells each node that moved once, with the
    /// box a build makes at the size it came to rest at.</summary>
    [Fact]
    public void ALiveResize_TellsTheBoundsOnce_WhenTheSizeRests()
    {
        using var session = HeadlessSession.Create(800, 600);
        var window = session.Window.Window;
        var bridge = session.Window.InstallAccessibilityBridge();
        var (root, footer) = Scene();
        session.Window.SetRoot(root);
        session.PumpFrames(2);
        session.PumpFrame(TimeSpan.FromMilliseconds(300));
        var before = Boxes(session.Window.AccessibilityTree!);
        bridge.ClearDiffs();
        int trees = window.AccessibilityTreesBuilt, walks = window.AccessibilityLayoutRefreshesInPlace;

        for (int i = 1; i <= 10; i++)
        {
            session.Window.SetClientSize(800 + 16 * i, 600 + 10 * i);
            session.PumpFrame(TimeSpan.FromMilliseconds(16));
        }
        Assert.Equal(0, window.AccessibilityLayoutRefreshesInPlace - walks);
        Assert.Empty(Told(bridge));

        session.PumpFrame(TimeSpan.FromMilliseconds(120));
        session.PumpFrame(TimeSpan.FromMilliseconds(120));

        Assert.Equal(0, window.AccessibilityTreesBuilt - trees);
        Assert.Equal(1, window.AccessibilityLayoutRefreshesInPlace - walks);
        var after = Boxes(session.Window.AccessibilityTree!);
        Assert.Equal(after, Boxes(bridge.CurrentTree!));
        var moved = after.Where(a => before.TryGetValue(a.Key, out var b) && b != a.Value).Select(a => a.Key).Order().ToList();
        Assert.Contains(footer.Id.Value, moved);
        Assert.True(moved.Count > 30, $"only {moved.Count} nodes moved");
        Assert.Equal(moved, Told(bridge).Order().ToList());   // each moved node told once
    }

    /// <summary>A single change of size (a maximize, a snap) is told as the drag's end is: one refresh
    /// 100 ms later, in a frame the window draws for it.</summary>
    [Fact]
    public void OneChangeOfSize_IsTold100msLater_InOneRefresh()
    {
        using var session = HeadlessSession.Create(800, 600);
        var window = session.Window.Window;
        var bridge = session.Window.InstallAccessibilityBridge();
        var (root, footer) = Scene();
        session.Window.SetRoot(root);
        session.PumpFrames(2);
        session.PumpFrame(TimeSpan.FromMilliseconds(300));
        bridge.ClearDiffs();
        int walks = window.AccessibilityLayoutRefreshesInPlace;

        session.Window.SetClientSize(1000, 700);
        session.PumpFrame(TimeSpan.FromMilliseconds(16));
        Assert.Empty(Told(bridge));
        session.PumpFrame(TimeSpan.FromMilliseconds(50));
        Assert.Empty(Told(bridge));
        session.PumpFrame(TimeSpan.FromMilliseconds(50));

        Assert.Equal(1, window.AccessibilityLayoutRefreshesInPlace - walks);
        Assert.Contains(footer.Id.Value, Told(bridge));
        Assert.Equal(Boxes(session.Window.AccessibilityTree!), Boxes(bridge.CurrentTree!));
    }

    /// <summary>With UI Automation on a hidden window: the drag raises one BoundingRectangle change for
    /// each element that moved, after it rests, where every 100 ms of it raised them all again.</summary>
    [WindowsFact]
    [SupportedOSPlatform("windows")]
    public void Uia_ALiveResize_RaisesEachMovedElementsBoundsOnce()
    {
        using var hwnd = TestHwnd.Create();
        using var session = HeadlessSession.Create(800, 600);
        using var uia = new WindowsAccessibilityBridge(hwnd.Handle, new NoHost());
        var raised = new List<UiaRaised>();
        uia.Raised = raised.Add;
        session.Window.InstallAccessibilityBridge(uia);
        var (root, footer) = Scene();
        session.Window.SetRoot(root);
        session.PumpFrames(2);
        session.PumpFrame(TimeSpan.FromMilliseconds(300));
        var before = Boxes(session.Window.AccessibilityTree!);
        raised.Clear();

        for (int i = 1; i <= 10; i++)
        {
            session.Window.SetClientSize(800 + 16 * i, 600 + 10 * i);
            session.PumpFrame(TimeSpan.FromMilliseconds(16));
        }
        Assert.Empty(raised);
        session.PumpFrame(TimeSpan.FromMilliseconds(120));
        session.PumpFrame(TimeSpan.FromMilliseconds(120));

        var after = Boxes(session.Window.AccessibilityTree!);
        var moved = after.Where(a => before.TryGetValue(a.Key, out var b) && b != a.Value).Select(a => a.Key).Order().ToList();
        var bounds = raised.Where(r => r.Kind == UiaRaisedKind.Property && r.Id == UiaInterop.UIA_BoundingRectanglePropertyId)
            .Select(r => r.Element!.Value.Value).Order().ToList();
        Assert.Contains(footer.Id.Value, bounds);
        Assert.Equal(moved, bounds);
        Assert.All(raised, r => Assert.Equal(UiaInterop.UIA_BoundingRectanglePropertyId, r.Id));
    }

    /// <summary>The nodes told a BoundingRect change, by element id, in the order told.</summary>
    private static List<ulong> Told(HeadlessAccessibilityBridge bridge) =>
    [
        .. bridge.Diffs.SelectMany(d => d.PropertyChanged)
            .Where(c => c.PropertyName == AccessibilityProperties.BoundingRect)
            .Select(c => c.Node.ElementId.Value),
    ];

    private static Dictionary<ulong, Rect> Boxes(AccessibilityNode root)
    {
        var boxes = new Dictionary<ulong, Rect>();
        Walk(root);
        return boxes;

        void Walk(AccessibilityNode n)
        {
            boxes[n.ElementId.Value] = n.BoundingRect;
            foreach (var c in n.Children) Walk(c);
        }
    }

    /// <summary>A host with nothing behind it: the bridge's events, not actions a client asks for.</summary>
    private sealed class NoHost : IUiaHostBridge
    {
        public void InvokeElement(ElementId id) { }
        public void ToggleElement(ElementId id) { }
        public void SelectElement(ElementId id) { }
        public void SetValue(ElementId id, string value) { }
        public void RequestFocus(ElementId id) { }
        public float ContentScale => 1f;
        public string? GetValue(ElementId id) => null;
        public bool? GetToggleState(ElementId id) => null;
        public int GetItemCount(ElementId listId) => 0;
        public string? GetItemLabel(ElementId listId, int index) => null;
        public bool GetItemSelected(ElementId listId, int index) => false;
        public int GetSelectedIndex(ElementId listId) => -1;
        public bool GetCanSelectMultiple(ElementId listId) => false;
        public IReadOnlyList<int> GetSelectedIndices(ElementId listId) => [];
        public void SelectItemOnly(ElementId listId, int index) { }
        public void SetItemSelected(ElementId listId, int index, bool selected) { }
        public int FindItemIndex(ElementId listId, int startAfter, AccessibleItemQuery query) => -1;
        public bool GetItemOffscreen(ElementId listId, int index) => false;
        public bool TryGetItemLayout(ElementId listId, int index, out Rect layoutRect) { layoutRect = default; return false; }
        public bool TryGetScrollInfo(ElementId id, out ScrollInfo info) { info = default; return false; }
        public void SetScrollPercent(ElementId id, double horizontalPercent, double verticalPercent) { }
        public void ScrollByAmount(ElementId id, int horizontalAmount, int verticalAmount) { }
        public bool TryGetRangeValue(ElementId id, out RangeValueInfo info) { info = default; return false; }
        public void SetRangeValue(ElementId id, double value) { }
    }
}
