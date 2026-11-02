using System.Runtime.Versioning;
using HultUI.Core.Accessibility;
using HultUI.Core.Builders;
using HultUI.Core.Components;
using HultUI.Core.Elements;
using HultUI.Core.Primitives;
using HultUI.Core.Reactivity;
using HultUI.Platform.Accessibility.Linux;
using HultUI.Platform.Accessibility.Windows;
using Xunit;
using TextB = HultUI.Core.Builders.Text;

namespace HultUI.Headless.Tests;

/// <summary>
/// A virtual list whose rows answer for themselves (<see cref="VirtualListElement{T}.RowsAreComposite"/>)
/// has its rows as elements only once they are realized, and drawing realizes them after the frame's
/// accessibility phase. In the window's own tree nothing asked again: the rows of a mounted list and
/// of a change were in no tree until something else built one (Junction's Files and Commit lists,
/// Aperture's library grid). The walks that read the tree ask the list for its rows now, so a reader
/// gets them in the frame they change, or with the layout refresh when one came less than its 100 ms
/// ago; and with no reader listening, nothing but drawing realizes a row.
/// </summary>
public class A11yCompositeListTests
{
    private static AccessibilityNode? Find(AccessibilityNode? node, Func<AccessibilityNode, bool> match)
    {
        if (node is null) return null;
        if (match(node)) return node;
        foreach (var child in node.Children)
            if (Find(child, match) is { } found) return found;
        return null;
    }

    private static AccessibilityNode? Row(HeadlessAccessibilityBridge bridge, int index) =>
        Find(bridge.CurrentTree, n => n.Name == $"Row {index}");

    private static void AssertFresh(HeadlessSession session, HeadlessAccessibilityBridge bridge)
    {
        var diff = AccessibilityTreeDiffer.Diff(bridge.CurrentTree, session.Window.Window.BuildAccessibilityTree());
        Assert.True(diff.IsEmpty, $"stale: +{diff.Added.Count} -{diff.Removed.Count} "
            + string.Join(", ", diff.PropertyChanged.Select(c => $"{c.PropertyName} of {c.Node.Role} '{c.Node.Name}'")));
    }

    /// <summary>A hundred rows of one button each from <see cref="First"/>, kept while their item is
    /// equal, in a list that says its rows are real. <see cref="Made"/> counts the rows made.</summary>
    private sealed class Buttons : Component
    {
        public readonly Signal<int> First = new(0);
        public int Made;

        public override Element Build()
        {
            var list = VirtualListElement<int>.New().ItemHeight(30)
                .Items(Enumerable.Range(First.Value, 100).ToArray())
                .RenderWith((i, _) => { Made++; return Button.New($"Row {i}"); })
                .Width(Dimension.Px(200)).FlexGrow(1);
            list.RowsAreComposite = true;
            return list;
        }
    }

    private static (HeadlessSession, HeadlessAccessibilityBridge, Buttons) Mount()
    {
        var session = HeadlessSession.Create(400, 300);
        var bridge = session.Window.InstallAccessibilityBridge();
        var buttons = new Buttons();
        session.Window.SetRoot(Flex.Column().FlexGrow(1).Children(TextB.New("Title"), buttons.Mount()));
        session.PumpFrames(2);
        // Past the mount's layout refresh: nothing else asks for a tree.
        for (int i = 0; i < 2; i++) session.PumpFrame(TimeSpan.FromMilliseconds(200));
        return (session, bridge, buttons);
    }

    [Fact]
    public void Mounting_PutsTheRowsInTheFirstTree()
    {
        using var session = HeadlessSession.Create(400, 300);
        var bridge = session.Window.InstallAccessibilityBridge();
        session.Window.SetRoot(Flex.Column().FlexGrow(1).Children(TextB.New("Title"), new Buttons().Mount()));

        session.PumpFrame();

        Assert.Equal(1, session.Window.AccessibilityTreesBuilt);
        Assert.NotNull(Row(bridge, 0));
        Assert.NotNull(Row(bridge, 5));
        AssertFresh(session, bridge);
    }

    [Fact]
    public void AListMountedLater_IsInTheTreeOfItsFrame()
    {
        using var session = HeadlessSession.Create(400, 300);
        var bridge = session.Window.InstallAccessibilityBridge();
        var column = Flex.Column().FlexGrow(1).Children(TextB.New("Title"));
        session.Window.SetRoot(column);
        session.PumpFrames(2);
        session.PumpFrame(TimeSpan.FromMilliseconds(200));

        column.AddChild(new Buttons().Mount());
        session.PumpFrame();

        Assert.NotNull(Row(bridge, 0));
        AssertFresh(session, bridge);
    }

    [Fact]
    public void AListThatIsTheWindowsRoot_HasItsRowsInTheFirstTree()
    {
        using var session = HeadlessSession.Create(400, 300);
        var bridge = session.Window.InstallAccessibilityBridge();
        var list = VirtualListElement<int>.New().ItemHeight(30).Items(Enumerable.Range(0, 100).ToArray())
            .RenderWith((i, _) => Button.New($"Row {i}"));
        list.RowsAreComposite = true;
        session.Window.SetRoot(list);

        session.PumpFrame();

        Assert.NotNull(Row(bridge, 0));
        AssertFresh(session, bridge);

        // And the walk that stands for a build reads the root's rows too.
        session.PumpFrame(TimeSpan.FromMilliseconds(200));
        list.Items = Enumerable.Range(40, 100).ToArray();
        session.PumpFrame();

        Assert.NotNull(Row(bridge, 40));
        AssertFresh(session, bridge);
    }

    [Fact]
    public void ARowChange_WithNoOtherChange_IsToldInItsFrame()
    {
        var (session, bridge, buttons) = Mount();
        using var _ = session;
        Assert.NotNull(Row(bridge, 0));

        for (int first = 10; first <= 30; first += 10)
        {
            buttons.First.Value = first;
            session.PumpFrame(TimeSpan.FromMilliseconds(200));

            Assert.NotNull(Row(bridge, first));
            Assert.Null(Row(bridge, first - 10));
            AssertFresh(session, bridge);
        }
    }

    [Fact]
    public void OneRowLetGo_IsToldInItsFrame()
    {
        var names = new string[50];
        for (int i = 0; i < names.Length; i++) names[i] = $"Row {i}";
        using var session = HeadlessSession.Create(400, 300);
        var bridge = session.Window.InstallAccessibilityBridge();
        var list = VirtualListElement<int>.New().ItemHeight(30).Items(Enumerable.Range(0, 50).ToArray())
            .RenderWith((i, _) => Button.New(names[i])).FlexGrow(1);
        list.RowsAreComposite = true;
        session.Window.SetRoot(Flex.Column().FlexGrow(1).Children(list));
        session.PumpFrames(2);
        session.PumpFrame(TimeSpan.FromMilliseconds(200));

        names[2] = "Renamed";
        list.InvalidateRow(2);
        session.PumpFrame(TimeSpan.FromMilliseconds(200));

        Assert.NotNull(Find(bridge.CurrentTree, n => n.Name == "Renamed"));
        Assert.Null(Row(bridge, 2));
        Assert.NotNull(Row(bridge, 1));
        AssertFresh(session, bridge);
    }

    [Fact]
    public void ChangesInABurst_GoOnTheRefreshSchedule_NotATreeEach_AndTheLastReachesAReader()
    {
        var (session, bridge, buttons) = Mount();
        using var _ = session;
        int trees = session.Window.AccessibilityTreesBuilt;

        // The first is on the refresh's leading edge, and the rest come sooner than its 100 ms.
        for (int first = 1; first <= 5; first++)
        {
            buttons.First.Value = first;
            session.PumpFrame(TimeSpan.FromMilliseconds(16));
        }
        Assert.True(session.Window.AccessibilityTreesBuilt - trees <= 2, "a tree per change");

        session.PumpFrame(TimeSpan.FromMilliseconds(120));

        Assert.NotNull(Row(bridge, 5));
        Assert.Null(Row(bridge, 0));
        AssertFresh(session, bridge);
    }

    [Fact]
    public void AWheelTurn_BuildsNoTreePerNotch_AndTheLastRowsReachAReader()
    {
        var (session, bridge, _) = Mount();
        using var __ = session;
        int trees = session.Window.AccessibilityTreesBuilt;

        const int notches = 30;
        for (int i = 0; i < notches; i++)
        {
            session.Window.Wheel(100, 150, 0, -1);
            session.PumpFrame(TimeSpan.FromMilliseconds(16));
        }
        session.PumpFrame(TimeSpan.FromMilliseconds(120));

        // On the layout refresh's schedule, 100 ms apart: one in six notches, and the last.
        int built = session.Window.AccessibilityTreesBuilt - trees;
        Assert.True(built <= notches / 5 + 1, $"{built} trees for {notches} notches");
        Assert.Null(Row(bridge, 0));
        AssertFresh(session, bridge);
    }

    [Fact]
    public void ALastNotchInItsOwnFrame_IsToldThere()
    {
        var (session, bridge, _) = Mount();
        using var __ = session;

        // Far enough for rows that were never realized: the frame's refresh is due as it scrolls.
        session.Window.Wheel(100, 150, 0, -30);
        session.PumpFrame(TimeSpan.FromMilliseconds(200));

        Assert.Null(Row(bridge, 0));
        AssertFresh(session, bridge);
    }

    [Fact]
    public void AWindowThatGrows_ToldTheRowsThatCameIn()
    {
        var (session, bridge, _) = Mount();
        using var __ = session;
        Assert.Null(Row(bridge, 15));

        session.Window.SetClientSize(400, 600);
        session.PumpFrame(TimeSpan.FromMilliseconds(200));

        Assert.NotNull(Row(bridge, 15));
        AssertFresh(session, bridge);
    }

    [Fact]
    public void AReaderThatAttachesLater_GetsTheRows()
    {
        using var session = HeadlessSession.Create(400, 300);
        session.Window.SetRoot(Flex.Column().FlexGrow(1).Children(new Buttons().Mount()));
        session.PumpFrames(2);

        var bridge = session.Window.InstallAccessibilityBridge();
        session.PumpFrame();

        Assert.NotNull(Row(bridge, 0));
        AssertFresh(session, bridge);
    }

    /// <summary>What no drawing reaches, a list scrolled out of a page: a reader gets its rows
    /// anyway, as it gets everything else in the page, and it settles after the change.</summary>
    [Fact]
    public void AListOutOfView_HasItsRowsInTheTree_AndAChangeSettles()
    {
        using var session = HeadlessSession.Create(400, 300);
        var bridge = session.Window.InstallAccessibilityBridge();
        var buttons = new Buttons();
        var host = buttons.Mount().Height(Dimension.Px(150));
        session.Window.SetRoot(ScrollView.New().FlexGrow(1).Children(Flex.Column().Children(
            Box.New().Height(Dimension.Px(1000)).FlexShrink(0), host)));
        session.PumpFrames(2);
        for (int i = 0; i < 2; i++) session.PumpFrame(TimeSpan.FromMilliseconds(200));
        Assert.NotNull(Row(bridge, 0));

        buttons.First.Value = 20;
        session.PumpFrame(TimeSpan.FromMilliseconds(16));
        session.PumpFrame(TimeSpan.FromMilliseconds(16));
        session.PumpFrame(TimeSpan.FromMilliseconds(120));
        session.PumpFrame(TimeSpan.FromMilliseconds(120));

        Assert.NotNull(Row(bridge, 20));
        AssertFresh(session, bridge);
        int trees = session.Window.AccessibilityTreesBuilt;
        session.PumpFrames(3);
        Assert.Equal(trees, session.Window.AccessibilityTreesBuilt);
        Assert.False(session.Window.Window.NeedsRender);
    }

    /// <summary>A minimized window draws nothing, so only the tree asks for the rows.</summary>
    [Fact]
    public void AMinimizedWindow_ToldTheRowsOfAChange_AndSettles()
    {
        var (session, bridge, buttons) = Mount();
        using var _ = session;
        session.Window.Minimized = true;
        session.PumpFrame(TimeSpan.FromMilliseconds(200));

        buttons.First.Value = 20;
        session.PumpFrame(TimeSpan.FromMilliseconds(200));
        session.PumpFrame(TimeSpan.FromMilliseconds(200));

        Assert.NotNull(Row(bridge, 20));
        int trees = session.Window.AccessibilityTreesBuilt;
        for (int i = 0; i < 3; i++) session.PumpFrame(TimeSpan.FromMilliseconds(200));
        Assert.Equal(trees, session.Window.AccessibilityTreesBuilt);
    }

    [Fact]
    public void WithNoReader_OnlyDrawingRealizesARow()
    {
        // Under a window's bottom edge: no frame draws it, and nothing else may realize it.
        using var session = HeadlessSession.Create(400, 300);
        var buttons = new Buttons();
        var scroll = ScrollView.New().FlexGrow(1).Children(Flex.Column().Children(
            Box.New().Height(Dimension.Px(1000)).FlexShrink(0), buttons.Mount().Height(Dimension.Px(150))));
        session.Window.SetRoot(scroll);
        session.PumpFrames(3);
        Assert.Equal(0, buttons.Made);

        scroll.ScrollOffsetY = 1000;
        session.PumpFrames(2);
        int made = buttons.Made;
        Assert.True(made > 0);

        // A frame of nothing new realizes nothing.
        session.PumpFrames(3);
        Assert.Equal(made, buttons.Made);
    }

    [Fact]
    public void ARealizedRow_IsMadeOnce_ThoughTheTreeAskedBeforeTheFrameDrew()
    {
        var (session, _, buttons) = Mount();
        using var __ = session;
        int made = buttons.Made;

        // Row 0 goes and row 10 comes, and every other row is kept.
        buttons.First.Value = 1;
        session.PumpFrame(TimeSpan.FromMilliseconds(200));
        session.PumpFrames(2);

        Assert.True(buttons.Made - made > 0);
        int once = buttons.Made - made;
        buttons.First.Value = 2;
        session.PumpFrame(TimeSpan.FromMilliseconds(16));   // not due: drawing realizes them
        session.PumpFrame(TimeSpan.FromMilliseconds(120));
        Assert.Equal(once * 2, buttons.Made - made);
    }

    // ── The real bridges ──

    private sealed class Signals : IAtSpiSignals
    {
        public int Added, Removed, ChildrenChanged;
        public void EmitChildrenChanged(string parentPath, string childPath, int index, bool isAdd) => ChildrenChanged++;
        public void EmitPropertyChange(string path, string atSpiProperty, string newValue) { }
        public void EmitValueChanged(string path, double value) { }
        public void EmitRoleChange(string path, uint role) { }
        public void EmitStateChanged(string path, string state, bool active) { }
        public void EmitTextChanged(string path, bool insert, int offset, string text) { }
        public void EmitTextCaretMoved(string path, int offset) { }
        public void EmitTextSelectionChanged(string path) { }
        public void EmitAnnouncement(string path, string text, int politeness) { }
        public void EmitCacheAdd(IAtSpiTree tree, AtSpiAccessible node) => Added++;
        public void EmitCacheRemove(string path) => Removed++;
    }

    /// <summary>Keeps the applied tree; the real bridge behind it.</summary>
    private sealed class Forwarding(IAccessibilityBridge inner) : IAccessibilityBridge
    {
        public AccessibilityNode? Tree;
        public bool IsActive => true;
        public void ApplyChanges(TreeDiff diff, AccessibilityNode root) { Tree = root; inner.ApplyChanges(diff, root); }
        public void ApplyPatchedProperties(TreeDiff patch, AccessibilityNode root) { Tree = root; inner.ApplyPatchedProperties(patch, root); }
        public void NotifyFocusChanged(AccessibilityNode? focused) => inner.NotifyFocusChanged(focused);
        public void NotifyFocusChanged(AccessibilityNode? focused, AccessibilityNode? previous) => inner.NotifyFocusChanged(focused, previous);
    }

    [Fact]
    public void AtSpi_IsToldTheRowsOfAChange()
    {
        var signals = new Signals();
        using var atSpi = new LinuxAccessibilityBridge(signals);
        using var session = HeadlessSession.Create(400, 300);
        var bridge = session.Window.InstallAccessibilityBridge(new Forwarding(atSpi));
        var buttons = new Buttons();
        session.Window.SetRoot(Flex.Column().FlexGrow(1).Children(buttons.Mount()));
        session.PumpFrame();
        Assert.NotNull(Find(bridge.Tree, n => n.Name == "Row 0"));
        session.PumpFrame(TimeSpan.FromMilliseconds(200));

        int added = signals.Added, removed = signals.Removed;
        buttons.First.Value = 20;
        session.PumpFrame(TimeSpan.FromMilliseconds(200));

        Assert.NotNull(Find(bridge.Tree, n => n.Name == "Row 20"));
        Assert.True(signals.Added > added, "no node added");
        Assert.True(signals.Removed > removed, "no node removed");
    }

    [WindowsFact]
    [SupportedOSPlatform("windows")]
    public void Uia_IsToldTheRowsOfAChange()
    {
        using var hwnd = TestHwnd.Create();
        using var session = HeadlessSession.Create(400, 300);
        var window = session.Window.Window;
        var router = new AccessibilityActionRouter(window);
        var host = new UiaHostBridge(window, router);
        window.SetUiaHostBridge(host);
        window.SetActionRouter(router);
        var raised = new List<UiaRaised>();
        using var uia = new WindowsAccessibilityBridge(hwnd.Handle, host) { Raised = raised.Add };
        var bridge = session.Window.InstallAccessibilityBridge(new Forwarding(uia));
        var buttons = new Buttons();
        session.Window.SetRoot(Flex.Column().FlexGrow(1).Children(buttons.Mount()));
        session.PumpFrame();
        Assert.NotNull(Find(bridge.Tree, n => n.Name == "Row 0"));
        session.PumpFrame(TimeSpan.FromMilliseconds(200));

        raised.Clear();
        buttons.First.Value = 20;
        session.PumpFrame(TimeSpan.FromMilliseconds(200));

        Assert.NotNull(Find(bridge.Tree, n => n.Name == "Row 20"));
        Assert.Contains(raised, r => r.Kind == UiaRaisedKind.Structure);
    }
}
