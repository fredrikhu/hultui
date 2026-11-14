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

    /// <summary>A virtual list of buttons, the first of them the signal's: its rows are elements only
    /// from the frame that realizes them.</summary>
    private sealed class Buttons : HultUI.Core.Components.Component
    {
        public readonly HultUI.Core.Reactivity.Signal<int> First = new(0);

        public override Element Build()
        {
            var list = VirtualListElement<int>.New().ItemHeight(30)
                .Items(Enumerable.Range(First.Value, 100).ToArray())
                .RenderWith((i, _) => Button.New($"Row {i}"))
                .Width(Dimension.Px(200)).Height(Dimension.Px(150));
            list.RowsAreComposite = true;
            return list;
        }
    }

    [Fact]
    public void AVirtualListInAnOverlay_IsInThePartBuiltInItsFrame_OnOpeningAndOnEachChange()
    {
        var (session, bridge) = Mount(Flex.Column().Children(TextB.New("Main")));
        using var _ = session;
        var buttons = new Buttons();
        session.Window.Window.ShowOverlay(Flex.Column().Padding(8).Children(buttons.Mount()), 50, 50);
        session.PumpFrame();
        Assert.NotNull(Find(bridge.CurrentTree, n => n.Name == "Row 0"));
        session.PumpFrame(TimeSpan.FromMilliseconds(200));
        int trees = session.Window.AccessibilityTreesBuilt;

        for (int first = 10; first <= 30; first += 10)
        {
            buttons.First.Value = first;
            session.PumpFrame();
            Assert.NotNull(Find(bridge.CurrentTree, n => n.Name == $"Row {first}"));
            Assert.Null(Find(bridge.CurrentTree, n => n.Name == $"Row {first - 10}"));
            AssertFresh(session, bridge);
        }
        Assert.Equal(trees, session.Window.AccessibilityTreesBuilt);
    }

    [Fact]
    public void ASelectedStateInAnOpenOverlay_BuildsOnlyItsPart_InThatFrame()
    {
        var rows = new Element[200];
        for (int i = 0; i < rows.Length; i++) rows[i] = TextB.New($"row {i}");
        var (session, bridge) = Mount(Flex.Column().Children(rows));
        using var _ = session;
        var one = Button.New("One").AccessibleSelected(true);
        var two = Button.New("Two").AccessibleSelected(false);
        session.Window.Window.ShowOverlay(Flex.Column().Padding(8).Children(one, two), 50, 50);
        session.PumpFrame();
        session.PumpFrame(TimeSpan.FromMilliseconds(200));
        int trees = session.Window.AccessibilityTreesBuilt, parts = session.Window.Window.AccessibilityOverlayPartsBuilt;
        bridge.ClearDiffs();

        one.AccessibleSelected = false;
        two.AccessibleSelected = true;
        session.PumpFrame();

        Assert.Equal((trees, parts + 1), (session.Window.AccessibilityTreesBuilt, session.Window.Window.AccessibilityOverlayPartsBuilt));
        Assert.Equal(2, Assert.Single(bridge.Diffs).PropertyChanged.Count(c => c.PropertyName == AccessibilityProperties.IsSelected));
        Assert.True(Find(bridge.CurrentTree, n => n.ElementId == two.Id)!.IsSelected);
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
    public void AStreamOfChangesInAnOverlay_DoesNotHoldOffTheWindowsOwnRefresh()
    {
        var grow = Box.New().Width(Dimension.Px(20)).Height(Dimension.Px(10));
        var after = Button.New("After");
        var marker = Box.New().Width(Dimension.Px(4)).Height(Dimension.Px(4)).AccessibleName("marker");
        var (session, bridge) = Mount(Flex.Column().Children(Flex.Row(gap: 4).Children(grow, after), marker));
        using var _ = session;
        var choices = new Choices();
        session.Window.Window.ShowOverlay(Flex.Column().Padding(8).Children(choices.Mount()), 300, 50);
        session.PumpFrame();
        session.PumpFrame(TimeSpan.FromMilliseconds(200));

        // A tree built just now, then a move in the window that waits for its refresh...
        marker.AutomationId = "rebuilt";
        session.PumpFrame(TimeSpan.FromMilliseconds(16));
        grow.Box.Width = Dimension.Px(120);
        grow.MarkLayoutDirty();
        // ...while the overlay changes on every frame, its part built each time.
        for (int i = 0; i < 20; i++)
        {
            choices.Count.Value = 2 + i % 3;
            session.PumpFrame(TimeSpan.FromMilliseconds(16));
        }

        // The window's refresh came due in those 320 ms: each part built put it off.
        float x = Find(session.Window.Window.BuildAccessibilityTree(), n => n.ElementId == after.Id)!.BoundingRect.X;
        Assert.Equal(x, Find(bridge.CurrentTree, n => n.ElementId == after.Id)!.BoundingRect.X, 0.01);
        AssertFresh(session, bridge);
    }

    [Fact]
    public void AnOverlayMovingEveryFrame_HasItsPartBuiltOnTheCoalescedSchedule()
    {
        var (session, bridge) = Mount(Flex.Column().Children(TextB.New("Main")));
        using var _ = session;
        var grow = Box.New().Width(Dimension.Px(20)).Height(Dimension.Px(10));
        session.Window.Window.ShowOverlay(Flex.Row(gap: 4).Padding(8).Children(grow, Button.New("After")), 300, 50);
        session.PumpFrame();
        session.PumpFrame(TimeSpan.FromMilliseconds(200));
        int parts = session.Window.Window.AccessibilityOverlayPartsBuilt;

        // An animation inside a dialog: 20 frames 16 ms apart, the layout changed in each.
        for (int i = 1; i <= 20; i++)
        {
            grow.Box.Width = Dimension.Px(20 + i * 5);
            grow.MarkLayoutDirty();
            session.PumpFrame(TimeSpan.FromMilliseconds(16));
        }
        session.PumpFrame(TimeSpan.FromMilliseconds(120));

        // The leading edge, about every 100 ms, the trailing edge: not 20.
        Assert.InRange(session.Window.Window.AccessibilityOverlayPartsBuilt - parts, 3, 6);
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

    // ── An overlay opening or closing (plan 17, round 14) ──
    //
    // Each opening and each closing built the whole window's tree for the overlay's part: over
    // Junction's History (1,114 nodes) a context menu's 14 nodes 101.7 KB to open and 99.0 to close,
    // the palette's 55 106.5 and 99.0. Now the part is added under the root node, or taken out.

    private static Element[] Labels(int n)
    {
        var rows = new Element[n];
        for (int i = 0; i < n; i++) rows[i] = TextB.New($"row {i}");
        return rows;
    }

    private static int Count(AccessibilityNode node) => 1 + node.Children.Sum(Count);

    [Fact]
    public void AnOverlayOpeningAndClosing_AddsAndTakesOutOnlyItsPart_WithNoTreeBuilt()
    {
        var (session, bridge) = Mount(Flex.Column().Children(Labels(200)));
        using var _ = session;
        var applied = bridge.CurrentTree!;
        var firstRow = applied.Children[0];
        int trees = session.Window.AccessibilityTreesBuilt, parts = session.Window.Window.AccessibilityOverlayPartsBuilt;
        bridge.ClearDiffs();
        var content = Flex.Column(gap: 4).Padding(8).Children(TextB.New("Pick"), Button.New("One"), Checkbox.New(false, "Two"));

        var handle = session.Window.Window.ShowOverlay(content, 50, 50);
        session.PumpFrame();

        Assert.Equal((trees, parts + 1), (session.Window.AccessibilityTreesBuilt, session.Window.Window.AccessibilityOverlayPartsBuilt));
        Assert.Same(applied, bridge.CurrentTree);
        Assert.Same(firstRow, applied.Children[0]);
        var part = Find(applied, n => n.ElementId == content.Id)!;
        Assert.Same(part, applied.Children[^1]);
        var opened = Assert.Single(bridge.Diffs);
        Assert.Equal((part, applied), (opened.Added[0].Node, opened.Added[0].Parent));
        Assert.Equal(Count(part), opened.Added.Count);
        Assert.Empty(opened.Removed);
        AssertFresh(session, bridge);
        bridge.ClearDiffs();

        session.Window.Window.CloseOverlay(handle);
        session.PumpFrame();

        Assert.Equal(trees, session.Window.AccessibilityTreesBuilt);
        Assert.Same(applied, bridge.CurrentTree);
        Assert.Same(firstRow, applied.Children[0]);
        Assert.Null(Find(applied, n => n.ElementId == content.Id));
        var closed = Assert.Single(bridge.Diffs);
        Assert.Empty(closed.Added);
        Assert.Equal(Count(part), closed.Removed.Count);
        // Deepest first, the part's own node last, under the root node as it was.
        var top = closed.Removed[^1];
        Assert.Same(part, top.Node);
        Assert.Equal(applied.ElementId, top.Parent!.ElementId);
        Assert.Contains(part, top.Parent.Children);
        AssertFresh(session, bridge);
    }

    [Fact]
    public void APalettesFieldTakingTheFocusAsItOpens_IsTold_AndSoIsTheFocusGoingBack()
    {
        var before = Button.New("Before");
        var (session, bridge) = Mount(Flex.Column().Children(before, Flex.Column().Children(Labels(200))));
        using var _ = session;
        before.Focus();
        session.PumpFrame();
        int trees = session.Window.AccessibilityTreesBuilt;
        long visited = session.Window.Window.AccessibilityFocusNodesVisited;
        var palette = new CommandPalette(() => Enumerable.Range(0, 12).Select(i => new PaletteCommand($"Command {i}", () => { })));

        palette.Open(session.Window.Window);
        session.PumpFrame();

        Assert.Equal(trees, session.Window.AccessibilityTreesBuilt);
        var field = bridge.FocusedNode;
        Assert.NotNull(field);
        Assert.True(field!.IsFocused);
        Assert.Same(field, Find(bridge.CurrentTree, n => n.ElementId == field.ElementId));
        Assert.NotNull(Find(bridge.CurrentTree!.Children[^1], n => n.ElementId == field.ElementId));
        Assert.False(Find(bridge.CurrentTree, n => n.ElementId == before.Id)!.IsFocused);
        AssertFresh(session, bridge);

        session.Window.PressKey(HultUI.Core.Events.KeyCode.Escape);
        session.PumpFrame();

        Assert.Equal(trees, session.Window.AccessibilityTreesBuilt);
        Assert.Equal(before.Id, bridge.FocusedNode!.ElementId);
        Assert.True(bridge.FocusedNode.IsFocused);
        Assert.Null(Find(bridge.CurrentTree, n => n.ElementId == field.ElementId));
        AssertFresh(session, bridge);
        // The field's node was looked for in no walk of the window's 200 rows: as the palette opened (no
        // node of it yet), nor as it closed (its part still there, found down from it).
        Assert.InRange(session.Window.Window.AccessibilityFocusNodesVisited - visited, 1, 40);
    }

    [Fact]
    public void AnOverlayComingOrGoing_InAFrameThatBuildsTheTree_IsInThatTree()
    {
        var marker = Box.New().Width(Dimension.Px(4)).Height(Dimension.Px(4)).AccessibleName("marker");
        var (session, bridge) = Mount(Flex.Column().Children(marker, Flex.Column().Children(Labels(50))));
        using var _ = session;
        int trees = session.Window.AccessibilityTreesBuilt;
        var content = Flex.Column().Padding(8).Children(Button.New("Inside"));

        // A change no patch or walk tells, in the frame of the opening, and of the closing.
        var handle = session.Window.Window.ShowOverlay(content, 50, 50);
        marker.AutomationId = "rebuilt";
        session.PumpFrame();
        Assert.Equal(trees + 1, session.Window.AccessibilityTreesBuilt);
        Assert.NotNull(Find(bridge.CurrentTree, n => n.ElementId == content.Id));
        AssertFresh(session, bridge);

        session.Window.Window.CloseOverlay(handle);
        marker.AutomationId = "rebuilt again";
        session.PumpFrame();
        Assert.Equal(trees + 2, session.Window.AccessibilityTreesBuilt);
        Assert.Null(Find(bridge.CurrentTree, n => n.ElementId == content.Id));
        AssertFresh(session, bridge);
    }

    [Fact]
    public void AnOverlayComingOrGoing_InAFrameThatWalksTheElements_IsTakenByTheWalk()
    {
        var grow = Box.New().Width(Dimension.Px(20)).Height(Dimension.Px(10));
        var row = Flex.Row(gap: 4).Children(grow, Button.New("After"));
        var (session, bridge) = Mount(Flex.Column().Children(row, Flex.Column().Children(Labels(50))));
        using var _ = session;
        int trees = session.Window.AccessibilityTreesBuilt, walks = session.Window.Window.AccessibilityLayoutRefreshesInPlace;
        var content = Flex.Column().Padding(8).Children(Button.New("Inside"));

        // A layout refresh's leading edge in the frame of the opening: the walk takes the part come.
        var handle = session.Window.Window.ShowOverlay(content, 50, 50);
        grow.Box.Width = Dimension.Px(120);
        grow.MarkLayoutDirty();
        session.PumpFrame(TimeSpan.FromMilliseconds(16));
        Assert.Equal((trees, walks + 1), (session.Window.AccessibilityTreesBuilt, session.Window.Window.AccessibilityLayoutRefreshesInPlace));
        Assert.NotNull(Find(bridge.CurrentTree, n => n.ElementId == content.Id));
        AssertFresh(session, bridge);

        // And the part gone in the frame of the closing, the window's elements changed in it too.
        session.PumpFrame(TimeSpan.FromMilliseconds(200));
        session.Window.Window.CloseOverlay(handle);
        row.AddChild(Button.New("New"));
        session.PumpFrame(TimeSpan.FromMilliseconds(16));
        Assert.Equal((trees, walks + 2), (session.Window.AccessibilityTreesBuilt, session.Window.Window.AccessibilityLayoutRefreshesInPlace));
        Assert.Null(Find(bridge.CurrentTree, n => n.ElementId == content.Id));
        Assert.NotNull(Find(bridge.CurrentTree, n => n.Name == "New"));
        AssertFresh(session, bridge);
    }

    [Fact]
    public void AnOverlayOpenedAndClosedBetweenTwoFrames_TellsNothing()
    {
        var (session, bridge) = Mount(Flex.Column().Children(Labels(20)));
        using var _ = session;
        int built = Built(session);
        bridge.ClearDiffs();

        var handle = session.Window.Window.ShowOverlay(Flex.Column().Padding(8).Children(Button.New("Gone")), 50, 50);
        session.Window.Window.CloseOverlay(handle);
        session.PumpFrame();

        Assert.Equal(built, Built(session));
        Assert.Empty(bridge.Diffs);
        AssertFresh(session, bridge);
    }
}
