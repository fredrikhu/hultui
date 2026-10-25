using HultUI.Core.Accessibility;
using HultUI.Core.Builders;
using HultUI.Core.Elements;
using HultUI.Core.Primitives;
using Xunit;
using TextB = HultUI.Core.Builders.Text;

namespace HultUI.Headless.Tests;

/// <summary>
/// An overlay that opens or closes — a dropdown's list, a dialog, a popover — is in the
/// accessibility tree from the next frame. Opening one changed nothing the window looked at for
/// accessibility: the tree took it in only when something else had it rebuilt, which was usually
/// the control's own expanded state (a dropdown's, a date picker's). Since a leaf's expanded state
/// is patched without a rebuild (plan 17, round 4), a dropdown's list never reached a reader.
/// </summary>
public class A11yOverlayTests
{
    private static AccessibilityNode? Find(AccessibilityNode? node, Func<AccessibilityNode, bool> match)
    {
        if (node is null) return null;
        if (match(node)) return node;
        foreach (var child in node.Children)
            if (Find(child, match) is { } found) return found;
        return null;
    }

    private static (HeadlessSession, HeadlessAccessibilityBridge) Mount(Element root)
    {
        var session = HeadlessSession.Create(800, 600);
        var bridge = session.Window.InstallAccessibilityBridge();
        session.Window.SetRoot(root);
        session.PumpFrames(2);
        // Past the mount's layout refresh: nothing else asks for a tree.
        for (int i = 0; i < 2; i++) session.PumpFrame(TimeSpan.FromMilliseconds(200));
        return (session, bridge);
    }

    [Fact]
    public void AnOverlay_IsInTheTree_FromTheFrameAfterItOpens_AndGoneAfterItCloses()
    {
        var (session, bridge) = Mount(Flex.Column().Children(TextB.New("Main"), Button.New("Open")));
        using var _ = session;
        var label = TextB.New("In the overlay");

        var handle = session.Window.Window.ShowOverlay(Flex.Column().Padding(8).Children(label, Checkbox.New(false, "Option")), 50, 50);
        session.PumpFrame();
        Assert.NotNull(Find(bridge.CurrentTree, n => n.ElementId == label.Id));

        session.Window.Window.CloseOverlay(handle);
        session.PumpFrame();
        Assert.Null(Find(bridge.CurrentTree, n => n.ElementId == label.Id));
    }

    [Fact]
    public void ADropdownsList_IsInTheTree_WithItsExpandedState_WhenItOpens()
    {
        var dropdown = Dropdown.New(["Alpha", "Bravo", "Charlie"], selectedIndex: 0).Width(Dimension.Px(160));
        var (session, bridge) = Mount(Flex.Column().Padding(8).Children(TextB.New("Pick one"), dropdown));
        using var _ = session;

        session.Window.Click(dropdown);
        session.PumpFrame();

        Assert.True(Find(bridge.CurrentTree, n => n.ElementId == dropdown.Id)!.Expanded);
        Assert.NotNull(Find(bridge.CurrentTree, n => n.Name == "Charlie"));

        session.Window.PressKey(HultUI.Core.Events.KeyCode.Escape);
        session.PumpFrame();
        Assert.False(Find(bridge.CurrentTree, n => n.ElementId == dropdown.Id)!.Expanded);
        Assert.Null(Find(bridge.CurrentTree, n => n.Name == "Charlie"));
    }

    // ── Inside an open overlay (plan 17, round 5) ──
    //
    // An overlay's tree is a root of its own: no flag of its elements reaches the window's root.
    // Its style and layout ran while it was drawn, after the frame's accessibility phase, so a change
    // inside an open overlay — a component's new children, a label that moved others — built no tree
    // until something else did, and a reader kept what it saw when the overlay opened.

    /// <summary>The tree the bridge was last given is the one a fresh build makes now.</summary>
    private static void AssertFresh(HeadlessSession session, HeadlessAccessibilityBridge bridge)
    {
        var diff = AccessibilityTreeDiffer.Diff(bridge.CurrentTree, session.Window.Window.BuildAccessibilityTree());
        Assert.True(diff.IsEmpty, $"stale: +{diff.Added.Count} -{diff.Removed.Count} "
            + string.Join(", ", diff.PropertyChanged.Select(c => $"{c.PropertyName} of {c.Node.Role} '{c.Node.Name}'")));
    }

    private sealed class Choices : HultUI.Core.Components.Component
    {
        public readonly HultUI.Core.Reactivity.Signal<int> Count = new(2);

        public override Element Build()
        {
            var column = Flex.Column(gap: 4).Padding(8);
            for (int i = 0; i < Count.Value; i++) column.AddChild(Button.New($"Choice {i}"));
            return column;
        }
    }

    [Fact]
    public void ChildrenAddedOrRemovedInAnOpenOverlay_AreInTheTree_FromTheNextFrame()
    {
        var (session, bridge) = Mount(Flex.Column().Children(TextB.New("Main"), Button.New("Open")));
        using var _ = session;
        var choices = new Choices();
        session.Window.Window.ShowOverlay(Flex.Column().Padding(8).Children(TextB.New("Pick"), choices.Mount()), 50, 50);
        session.PumpFrame();
        session.PumpFrame(TimeSpan.FromMilliseconds(200));

        choices.Count.Value = 4;
        session.PumpFrame();
        Assert.NotNull(Find(bridge.CurrentTree, n => n.Name == "Choice 3"));
        AssertFresh(session, bridge);

        choices.Count.Value = 1;
        session.PumpFrame();
        Assert.Null(Find(bridge.CurrentTree, n => n.Name == "Choice 1"));
        AssertFresh(session, bridge);
    }

    [Fact]
    public void AMoveInsideAnOpenOverlay_IsToldByTheLayoutRefresh()
    {
        var (session, bridge) = Mount(Flex.Column().Children(TextB.New("Main"), Button.New("Open")));
        using var _ = session;
        var grow = Box.New().Width(Dimension.Px(20)).Height(Dimension.Px(10));
        var after = Button.New("After");
        session.Window.Window.ShowOverlay(Flex.Row(gap: 4).Padding(8).Children(grow, after), 50, 50);
        session.PumpFrame();
        session.PumpFrame(TimeSpan.FromMilliseconds(200));
        float before = Find(bridge.CurrentTree, n => n.ElementId == after.Id)!.BoundingRect.X;

        grow.Box.Width = Dimension.Px(120);
        grow.MarkLayoutDirty();
        session.PumpFrame(TimeSpan.FromMilliseconds(16));
        session.PumpFrame(TimeSpan.FromMilliseconds(120));

        Assert.Equal(before + 100, Find(bridge.CurrentTree, n => n.ElementId == after.Id)!.BoundingRect.X, 0.01);
        AssertFresh(session, bridge);
    }

    [Fact]
    public void AnAnchoredOverlay_IsWhereItIsDrawn_AfterItsAnchorScrolls()
    {
        var anchor = Button.New("Filters");
        var lines = new Element[60];
        for (int i = 0; i < lines.Length; i++) lines[i] = TextB.New($"line {i}").Padding(4);
        var page = ScrollView.New().FlexGrow(1).Children(Flex.Column().FlexShrink(0).Children(
            Flex.Column().FlexShrink(0).Children(lines[..10]), anchor, Flex.Column().FlexShrink(0).Children(lines[10..])));
        var (session, bridge) = Mount(Flex.Column().FlexGrow(1).Children(page));
        using var _ = session;
        var item = Button.New("Only open");
        var popover = new Popover(() => Flex.Column().Padding(6).Children(item));
        popover.Open(anchor);
        session.PumpFrame();
        session.PumpFrame(TimeSpan.FromMilliseconds(200));
        float before = Find(bridge.CurrentTree, n => n.ElementId == item.Id)!.BoundingRect.Y;

        // The first frame of the scroll is the refresh's leading edge: it built the tree before the
        // overlay followed its anchor, and nothing was due after it.
        session.Window.Wheel(300, 300, 0, -1);
        session.PumpFrame(TimeSpan.FromMilliseconds(16));
        for (int i = 0; i < 3; i++) session.PumpFrame(TimeSpan.FromMilliseconds(120));

        Assert.True(popover.IsOpen);
        Assert.True(Find(bridge.CurrentTree, n => n.ElementId == item.Id)!.BoundingRect.Y < before - 1, "the item's node did not move with the scroll");
        AssertFresh(session, bridge);
    }

    [Fact]
    public void AChangeInAnOverlayWhileMinimized_IsBuiltOnce()
    {
        var (session, bridge) = Mount(Flex.Column().Children(TextB.New("Main")));
        using var _ = session;
        var choices = new Choices();
        session.Window.Window.ShowOverlay(Flex.Column().Padding(8).Children(choices.Mount()), 50, 50);
        session.PumpFrame();
        session.Window.Minimized = true;
        session.PumpFrames(2);
        int trees = session.Window.AccessibilityTreesBuilt;

        choices.Count.Value = 3;
        for (int i = 0; i < 20; i++)
            Assert.False(session.PumpFrame(), "a minimized window drew a frame");

        Assert.Equal(trees + 1, session.Window.AccessibilityTreesBuilt);
        Assert.NotNull(Find(bridge.CurrentTree, n => n.Name == "Choice 2"));
        session.Window.Minimized = false;
        Assert.True(session.PumpFrame(), "the restore drew nothing");
        Assert.Equal(trees + 1, session.Window.AccessibilityTreesBuilt);
    }
}
