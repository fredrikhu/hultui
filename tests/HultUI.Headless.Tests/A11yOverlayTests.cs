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

    /// <summary>The recording bridge, counting the patches it is handed.</summary>
    private sealed class Patching(HeadlessAccessibilityBridge inner) : IAccessibilityBridge
    {
        public int Patches;
        public bool IsActive => true;
        public void ApplyChanges(TreeDiff diff, AccessibilityNode root) => inner.ApplyChanges(diff, root);
        public void ApplyPatchedProperties(TreeDiff patch, AccessibilityNode root) { Patches++; inner.ApplyChanges(patch, root); }
        public void NotifyFocusChanged(AccessibilityNode? focused) => inner.NotifyFocusChanged(focused);
    }

    private static (HeadlessSession, HeadlessAccessibilityBridge, Patching) MountPatching(Element root)
    {
        var session = HeadlessSession.Create(800, 600);
        var recording = new HeadlessAccessibilityBridge();
        var bridge = session.Window.InstallAccessibilityBridge(new Patching(recording));
        session.Window.SetRoot(root);
        session.PumpFrames(2);
        for (int i = 0; i < 2; i++) session.PumpFrame(TimeSpan.FromMilliseconds(200));
        return (session, recording, bridge);
    }

    [Fact]
    public void ALeafsOwnChangeInAnOpenOverlay_IsPatched_WithNoTreeBuilt()
    {
        var (session, bridge, patching) = MountPatching(Flex.Column().Children(TextB.New("Main"), Button.New("Open")));
        using var _ = session;
        var check = Checkbox.New(false, "Remember me");
        var input = TextInput.New("").Width(Dimension.Px(200));
        var menuish = Box.New().Width(Dimension.Px(120)).Height(Dimension.Px(24)).AccessibleName("Section");
        menuish.AccessibleExpanded = false;
        session.Window.Window.ShowOverlay(Flex.Column(gap: 4).Padding(8).Children(TextB.New("Sign in"), input, check, menuish), 50, 50);
        session.PumpFrame();
        session.PumpFrame(TimeSpan.FromMilliseconds(200));
        int trees = session.Window.AccessibilityTreesBuilt;

        check.IsChecked = true;
        session.PumpFrame();
        Assert.True(Find(bridge.CurrentTree, n => n.ElementId == check.Id)!.IsToggled);
        input.Focus();
        session.Window.TypeText("ab");
        session.PumpFrame();
        Assert.Equal("ab", Find(bridge.CurrentTree, n => n.ElementId == input.Id)!.Value);
        menuish.AccessibleExpanded = true;
        session.PumpFrame();
        Assert.True(Find(bridge.CurrentTree, n => n.ElementId == menuish.Id)!.Expanded);

        Assert.Equal(trees, session.Window.AccessibilityTreesBuilt);
        Assert.True(patching.Patches >= 3, $"{patching.Patches} patches");
        AssertFresh(session, bridge);
    }

    [Fact]
    public void AnOverlayThatIsOneLeaf_IsPatchedInItsPlace()
    {
        var (session, bridge, patching) = MountPatching(Flex.Column().Children(TextB.New("Main")));
        using var _ = session;
        var badge = Canvas.New().Width(Dimension.Px(24)).Height(Dimension.Px(24));
        badge.AccessibleName = "Recording";
        session.Window.Window.ShowOverlay(badge, 10, 10);
        session.PumpFrame();
        session.PumpFrame(TimeSpan.FromMilliseconds(200));
        int trees = session.Window.AccessibilityTreesBuilt;

        badge.AccessibleName = "Paused";
        session.PumpFrame();

        Assert.Equal(trees, session.Window.AccessibilityTreesBuilt);
        Assert.Equal(1, patching.Patches);
        Assert.Equal("Paused", Find(bridge.CurrentTree, n => n.ElementId == badge.Id)!.Name);
        AssertFresh(session, bridge);
    }

    [Fact]
    public void AChangeInAClosedOverlay_IsNotTold()
    {
        var (session, _, patching) = MountPatching(Flex.Column().Children(TextB.New("Main")));
        using var __ = session;
        var check = Checkbox.New(false, "Remember me");
        var handle = session.Window.Window.ShowOverlay(Flex.Column().Padding(8).Children(check), 50, 50);
        session.PumpFrame();
        session.Window.Window.CloseOverlay(handle);
        session.PumpFrame();
        session.PumpFrame(TimeSpan.FromMilliseconds(200));
        int trees = session.Window.AccessibilityTreesBuilt;

        check.IsChecked = true;
        Assert.False(session.PumpFrame());
        Assert.Equal((trees, 0), (session.Window.AccessibilityTreesBuilt, patching.Patches));
    }

    /// <summary>The selected state of each item node of the list <paramref name="list"/> in a tree.</summary>
    private static string Selection(AccessibilityNode? tree, Element list) =>
        string.Concat(Find(tree, n => n.ElementId == list.Id)!.Children.Select(c => c.IsSelected == true ? "x" : "."));

    private static Element? OverlayContent(HeadlessSession session) => session.Window.OverlayContents.LastOrDefault();

    /// <summary>Whole trees and overlay parts built so far.</summary>
    private static int Built(HeadlessSession session) =>
        session.Window.AccessibilityTreesBuilt + session.Window.Window.AccessibilityOverlayPartsBuilt;

    [Fact]
    public void AChangeOfAnOpenOverlaysChildren_BuildsOnlyItsPart()
    {
        var rows = new Element[200];
        for (int i = 0; i < rows.Length; i++) rows[i] = TextB.New($"row {i}");
        var (session, bridge) = Mount(Flex.Column().Children(rows));
        using var _ = session;
        var choices = new Choices();
        session.Window.Window.ShowOverlay(Flex.Column().Padding(8).Children(TextB.New("Pick"), choices.Mount()), 50, 50);
        session.PumpFrame();
        session.PumpFrame(TimeSpan.FromMilliseconds(200));
        var applied = bridge.CurrentTree!;
        var firstRow = applied.Children[0];
        int trees = session.Window.AccessibilityTreesBuilt, parts = session.Window.Window.AccessibilityOverlayPartsBuilt;

        choices.Count.Value = 5;
        session.PumpFrame();

        // The overlay's part was built again and put in the applied tree; the window's 200 rows were not.
        Assert.Equal((trees, parts + 1), (session.Window.AccessibilityTreesBuilt, session.Window.Window.AccessibilityOverlayPartsBuilt));
        Assert.Same(applied, bridge.CurrentTree);
        Assert.Same(firstRow, bridge.CurrentTree!.Children[0]);
        Assert.NotNull(Find(bridge.CurrentTree, n => n.Name == "Choice 4"));
        AssertFresh(session, bridge);
    }

    [Fact]
    public void APaletteKeystroke_BuildsOnlyThePalettesPart()
    {
        var rows = new Element[200];
        for (int i = 0; i < rows.Length; i++) rows[i] = TextB.New($"row {i}");
        var (session, bridge) = Mount(Flex.Column().Children(rows));
        using var _ = session;
        string[] words = ["Save", "Save all", "Open file", "Open folder", "Close", "Close all", "Settings", "Search", "Replace", "Run"];
        var palette = new CommandPalette(() => words.Select(w => new PaletteCommand(w, () => { })));
        palette.Open(session.Window.Window);
        session.PumpFrame();
        session.PumpFrame(TimeSpan.FromMilliseconds(200));
        int trees = session.Window.AccessibilityTreesBuilt, parts = session.Window.Window.AccessibilityOverlayPartsBuilt;

        foreach (var key in new[] { "s", "a", "v" })
        {
            session.Window.TypeText(key);
            session.PumpFrame();
            AssertFresh(session, bridge);
        }
        session.Window.PressKey(HultUI.Core.Events.KeyCode.Backspace);
        session.PumpFrame();
        for (int i = 0; i < 2; i++) session.PumpFrame(TimeSpan.FromMilliseconds(120));
        AssertFresh(session, bridge);

        Assert.Equal(trees, session.Window.AccessibilityTreesBuilt);
        Assert.True(session.Window.Window.AccessibilityOverlayPartsBuilt >= parts + 4);
    }

    [Fact]
    public void AContextMenusSelection_IsToldAsItMoves_WithNoTreeBuilt()
    {
        var (session, bridge, patching) = MountPatching(Flex.Column().Children(TextB.New("Main")));
        using var _ = session;
        var items = new List<ContextMenuItem>
        {
            new() { Label = "Copy", Action = () => { } },
            new() { IsSeparator = true },
            new() { Label = "Disabled", IsDisabled = true, Action = () => { } },
            new() { Label = "Paste", Action = () => { } },
            new() { Label = "Delete", Action = () => { } },
        };
        session.Window.Window.OpenContextMenu(items, new Point(100, 80));
        session.PumpFrame();
        session.PumpFrame(TimeSpan.FromMilliseconds(200));
        var menu = OverlayContent(session)!;
        int trees = session.Window.AccessibilityTreesBuilt;
        string Told() => Selection(bridge.CurrentTree, menu);
        string Fresh() => Selection(session.Window.Window.BuildAccessibilityTree(), menu);

        // Separators are not items; a disabled one is, but the keys pass it by.
        session.Window.PressKey(HultUI.Core.Events.KeyCode.Down);
        session.PumpFrame();
        Assert.Equal(("x...", "x..."), (Told(), Fresh()));
        session.Window.PressKey(HultUI.Core.Events.KeyCode.Down);
        session.PumpFrame();
        Assert.Equal(("..x.", "..x."), (Told(), Fresh()));
        session.Window.PressKey(HultUI.Core.Events.KeyCode.Up);
        session.Window.PressKey(HultUI.Core.Events.KeyCode.Up);
        session.PumpFrame();
        Assert.Equal(("...x", "...x"), (Told(), Fresh()));

        // The pointer: onto an item, then off the menu.
        var box = menu.WindowBorderBox;
        session.Window.MoveMouse(box.X + box.Width / 2, box.Y + 12);
        session.PumpFrame();
        Assert.Equal(("x...", "x..."), (Told(), Fresh()));
        session.Window.MoveMouse(box.Right + 40, box.Bottom + 40);
        session.PumpFrame();
        Assert.Equal(("....", "...."), (Told(), Fresh()));

        Assert.Equal(trees, session.Window.AccessibilityTreesBuilt);
        Assert.Equal(5, patching.Patches);
    }

    [Fact]
    public void ADropdownsListSelection_IsToldAsItMoves()
    {
        var dropdown = Dropdown.New(["Alpha", "Bravo", "Charlie", "Delta"], selectedIndex: 1).Width(Dimension.Px(160));
        var (session, bridge, patching) = MountPatching(Flex.Column().Padding(8).Children(TextB.New("Pick one"), dropdown));
        using var _ = session;
        session.Window.Click(dropdown);
        session.PumpFrame();
        session.PumpFrame(TimeSpan.FromMilliseconds(200));
        var list = OverlayContent(session)!;
        Assert.Equal(".x..", Selection(bridge.CurrentTree, list));
        int trees = session.Window.AccessibilityTreesBuilt;

        session.Window.PressKey(HultUI.Core.Events.KeyCode.Down);
        session.PumpFrame();
        Assert.Equal("..x.", Selection(bridge.CurrentTree, list));
        session.Window.PressKey(HultUI.Core.Events.KeyCode.End);
        session.PumpFrame();
        Assert.Equal("...x", Selection(bridge.CurrentTree, list));
        session.Window.PressKey(HultUI.Core.Events.KeyCode.Home);
        session.PumpFrame();
        Assert.Equal("x...", Selection(bridge.CurrentTree, list));

        Assert.Equal(trees, session.Window.AccessibilityTreesBuilt);
        Assert.Equal(3, patching.Patches);
        Assert.Equal("x...", Selection(session.Window.Window.BuildAccessibilityTree(), list));
    }

    [Fact]
    public void AMenusItems_HaveTheirBounds_InTheTreeBuiltAsItOpens()
    {
        var (session, bridge) = Mount(Flex.Column().Children(TextB.New("Main")));
        using var _ = session;
        var items = new List<ContextMenuItem>
        {
            new() { Label = "Copy", ShortcutHint = "Ctrl+C", Action = () => { } },
            new() { IsSeparator = true },
            new() { Label = "Paste", Action = () => { } },
            new() { Label = "More", Children = [new() { Label = "Alpha", Action = () => { } }] },
        };
        session.Window.Window.OpenContextMenu(items, new Point(100, 80));
        session.PumpFrame();

        // The tree is built in the frame after the menu opened, before the menu is first drawn: its
        // items' places were worked out only as it drew, and every item had a zero-height box.
        var menu = OverlayContent(session)!;
        var nodes = Find(bridge.CurrentTree, n => n.ElementId == menu.Id)!.Children;
        Assert.Equal(3, nodes.Count);
        var box = menu.WindowBorderBox;
        float bottom = box.Y;
        foreach (var node in nodes)
        {
            Assert.True(node.BoundingRect.Height > 10, $"'{node.Name}' is {node.BoundingRect.Height} high");
            Assert.True(node.BoundingRect.Y >= bottom, $"'{node.Name}' overlaps the item above it");
            bottom = node.BoundingRect.Bottom;
        }
        Assert.True(bottom <= box.Bottom);
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
        int built = Built(session);

        choices.Count.Value = 3;
        for (int i = 0; i < 20; i++)
            Assert.False(session.PumpFrame(), "a minimized window drew a frame");

        Assert.Equal(built + 1, Built(session));
        Assert.NotNull(Find(bridge.CurrentTree, n => n.Name == "Choice 2"));
        session.Window.Minimized = false;
        Assert.True(session.PumpFrame(), "the restore drew nothing");
        Assert.Equal(built + 1, Built(session));
    }
}
