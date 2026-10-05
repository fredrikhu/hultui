using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using HultUI.Core.Accessibility;
using HultUI.Core.Builders;
using HultUI.Core.Elements;
using HultUI.Core.Events;
using HultUI.Platform.Accessibility.Windows;
using Xunit;

namespace HultUI.Headless.Tests;

/// <summary>
/// What a reader is told about <see cref="TabsElement"/>. Its headers were clickable boxes, so
/// they read as buttons, with nothing to say which tab was open, and the panel was not
/// announced at all. Now the strip is a tab list, each header a tab that is selected or not,
/// and the content a tab panel named after the tab it shows.
/// </summary>
public class TabsAccessibilityTests
{
    private static TabsElement ThreeTabs() => Tabs.New()
        .Tab("General", () => TextElement.New("general page"))
        .Tab("Inbox", () => TextElement.New("inbox page"), badge: "3")
        .Tab("Logs", () => TextElement.New("logs page"), onClose: () => { });

    private static AccessibilityNode Only(AccessibilityNode node, AccessibilityRole role) =>
        Assert.Single(All(node), n => n.Role == role);

    private static IEnumerable<AccessibilityNode> All(AccessibilityNode node)
    {
        yield return node;
        foreach (var child in node.Children)
            foreach (var n in All(child))
                yield return n;
    }

    [Fact]
    public void TheStripIsATabList_ItsHeadersTabs_AndTheContentATabPanel()
    {
        using var session = HeadlessSession.Create(400, 200);
        var tabs = ThreeTabs();
        session.Application.SetRoot(tabs);
        session.PumpFrames(2);

        var tree = AccessibilityTreeBuilder.Build(tabs);
        var strip = Only(tree, AccessibilityRole.TabList);

        Assert.All(strip.Children, t => Assert.Equal(AccessibilityRole.Tab, t.Role));
        // Named from their text, badge included; the closable one outright, without its "×".
        Assert.Equal(["General", "Inbox 3", "Logs"], strip.Children.Select(t => t.Name));
        Assert.Equal([true, false, false], strip.Children.Select(t => t.IsSelected));
        Assert.Contains(strip.Children[2].Children, c => c.Role == AccessibilityRole.Button && c.Name == "Close Logs");
        Assert.Equal("General", Only(tree, AccessibilityRole.TabPanel).Name);
    }

    [Fact]
    public void ChoosingATab_MovesTheSelectionAndRenamesThePanel()
    {
        using var session = HeadlessSession.Create(400, 200);
        var tabs = ThreeTabs();
        session.Application.SetRoot(tabs);
        session.PumpFrames(2);

        var inbox = tabs.Children[0].Children[1];
        EventDispatcher.DispatchDirect(new ClickEvent(), inbox);
        session.PumpFrames(2);

        var tree = AccessibilityTreeBuilder.Build(tabs);
        Assert.Equal([false, true, false], Only(tree, AccessibilityRole.TabList).Children.Select(t => t.IsSelected));
        Assert.Equal("Inbox", Only(tree, AccessibilityRole.TabPanel).Name);
    }

    /// <summary>A selected state set on a live element is a change a reader hears, and the
    /// window is told at once rather than at the next layout.</summary>
    [Fact]
    public void ASelectedStateChangeIsReported()
    {
        var root = Box.New().AccessibleRole(AccessibilityRole.Application);
        var tab = Box.New().AccessibleRole(AccessibilityRole.Tab).AccessibleName("General").AccessibleSelected(false);
        root.AddChild(tab);
        var observer = new RecordingObserver();
        root.AccessibilityObserver = observer;
        var before = AccessibilityTreeBuilder.Build(root);

        tab.AccessibleSelected = true;

        Assert.Equal(AccessibilityProperties.IsSelected, Assert.Single(observer.Changes));
        var diff = AccessibilityTreeDiffer.Diff(before, AccessibilityTreeBuilder.Build(root));
        var change = Assert.Single(diff.PropertyChanged);
        Assert.Equal(AccessibilityProperties.IsSelected, change.PropertyName);
        Assert.True(change.Node.IsSelected);
    }

    /// <summary>UI Automation's side: a tab is a SelectionItem, says whether it is selected, has
    /// the strip for its container, and selecting it opens it.</summary>
    [WindowsFact]
    [SupportedOSPlatform("windows")]
    public void ATabIsASelectionItem_AndSelectingItOpensIt()
    {
        using var hwnd = TestHwnd.Create();
        using var host = HandleWindowHost.Create(hwnd.Handle);
        var bridge = Assert.IsType<WindowsAccessibilityBridge>(AccessibilityBridgeFactory.Create(host.Application.PrimaryWindow));
        try
        {
            var tabs = ThreeTabs();
            host.Application.SetRoot(tabs);
            host.Application.RunFrame(TimeSpan.Zero, forceRender: true);
            var tree = AccessibilityTreeBuilder.Build(tabs);
            bridge.ApplyChanges(AccessibilityTreeDiffer.Diff(null, tree), tree);
            var strip = Only(tree, AccessibilityRole.TabList);

            Assert.True(IsSelected(bridge, strip.Children[0]));
            Assert.False(IsSelected(bridge, strip.Children[1]));

            var inbox = Fragment(bridge, strip.Children[1]);
            Assert.Equal(0, inbox.GetPatternProvider(UiaInterop.UIA_SelectionItemPatternId, out var pattern));
            Assert.NotEqual(IntPtr.Zero, pattern);
            try
            {
                var item = Assert.IsType<UiaSelectionItemProvider>(NativeProvider.LogicOf(pattern));
                Assert.Equal(0, item.GetIsSelected(out int selected));
                Assert.Equal(0, selected);

                Assert.Equal(0, item.GetSelectionContainer(out var container));
                try { Assert.Equal(strip.ElementId, Assert.IsType<UiaFragment>(NativeProvider.LogicOf(container)).ElementId); }
                finally { Marshal.Release(container); }

                Assert.Equal(0, item.Select());
            }
            finally { Marshal.Release(pattern); }

            host.Application.RunFrame(TimeSpan.Zero, forceRender: true); // the action runs on the UI thread
            Assert.Equal(1, tabs.SelectedIndex);
        }
        finally { bridge.Dispose(); }
    }

    [SupportedOSPlatform("windows")]
    private static UiaFragment Fragment(WindowsAccessibilityBridge bridge, AccessibilityNode node)
    {
        var native = bridge.NativeFragment(node.ElementId);
        try { return Assert.IsType<UiaFragment>(NativeProvider.LogicOf(native)); }
        finally { Marshal.Release(native); }
    }

    [SupportedOSPlatform("windows")]
    private static bool IsSelected(WindowsAccessibilityBridge bridge, AccessibilityNode node)
    {
        Assert.Equal(0, Fragment(bridge, node).GetPropertyValue(UiaInterop.UIA_SelectionItemIsSelectedPropertyId, out var value));
        Assert.Equal(UiaVariant.VT_BOOL, value.Vt);
        return value.ShortValue != 0;
    }

    private sealed class RecordingObserver : IElementPropertyObserver
    {
        public List<string> Changes { get; } = [];
        public void OnElementPropertyChanged(Element element, string propertyName) => Changes.Add(propertyName);
    }
}
