// The AT-SPI bridge is Linux's, but with its signals recorded instead of sent it runs anywhere:
// nothing here reaches a bus.
#pragma warning disable CA1416
using System.Runtime.InteropServices;
using HultUI.Core.Accessibility;
using HultUI.Core.Builders;
using HultUI.Core.Dialogs;
using HultUI.Core.Elements;
using HultUI.Core.Events;
using HultUI.Core.Layout;
using HultUI.Core.Primitives;
using HultUI.Platform.Accessibility.Linux;
using HultUI.Platform.Accessibility.Windows;
using Xunit;
using TextB = HultUI.Core.Builders.Text;

namespace HultUI.Headless.Tests;

/// <summary>
/// A box whose click closes a dialog — <see cref="Dialog.Show{TResult}"/>'s scrim, a modal an app keeps
/// in its own tree on a backdrop (Meridian's Options) — is the dialog's backdrop, not a button (plan 17,
/// round 6; the user approved what a reader hears on 2026-10-27). A box with a click is inferred to be
/// a button named from the text inside it, so a reader heard every dialog and every command palette
/// inside a button named after all of its text (2,182 characters over 2,000 commands), told again on
/// each keystroke; and a name changing inside a dialog could not be patched under that button, so a
/// label ticking in a dialog over 1,000 rows built the window's whole tree on every tick (284 KB with
/// UIA, 1,081 KB with AT-SPI).
/// </summary>
public class A11yDialogBackdropTests
{
    private static AccessibilityNode? Find(AccessibilityNode? node, Func<AccessibilityNode, bool> match)
    {
        if (node is null) return null;
        if (match(node)) return node;
        foreach (var child in node.Children)
            if (Find(child, match) is { } found) return found;
        return null;
    }

    private static AccessibilityNode? Find(AccessibilityNode? node, Element element) => Find(node, n => n.ElementId == element.Id);

    private static IEnumerable<AccessibilityNode> Walk(AccessibilityNode node)
    {
        yield return node;
        foreach (var child in node.Children)
            foreach (var n in Walk(child)) yield return n;
    }

    /// <summary>A bridge reported active, its diffs and patches kept; forwards to a real one, if any.</summary>
    private sealed class Recording(IAccessibilityBridge? inner = null) : IAccessibilityBridge
    {
        public readonly List<TreeDiff> Told = [];
        public AccessibilityNode? Tree;
        public bool IsActive => true;
        public void ApplyChanges(TreeDiff diff, AccessibilityNode root)
        {
            Told.Add(diff);
            Tree = root;
            inner?.ApplyChanges(diff, root);
        }
        public void ApplyPatchedProperties(TreeDiff patch, AccessibilityNode root)
        {
            Told.Add(patch);
            Tree = root;
            inner?.ApplyPatchedProperties(patch, root);
        }
        public void NotifyFocusChanged(AccessibilityNode? focused) => inner?.NotifyFocusChanged(focused);
        public void NotifyFocusChanged(AccessibilityNode? focused, AccessibilityNode? previous) => inner?.NotifyFocusChanged(focused, previous);

        /// <summary>The nodes whose name every change told since <paramref name="from"/> changed.</summary>
        public List<ElementId> NamesToldSince(int from) =>
            Told.Skip(from).SelectMany(d => d.PropertyChanged)
                .Where(c => c.PropertyName == AccessibilityProperties.Name).Select(c => c.Node.ElementId).ToList();
    }

    private sealed class Signals : IAtSpiSignals
    {
        public readonly List<string> Lines = [];
        public void EmitChildrenChanged(string parentPath, string childPath, int index, bool isAdd) =>
            Lines.Add($"children-changed:{(isAdd ? "add" : "remove")} {childPath}");
        public void EmitPropertyChange(string path, string atSpiProperty, string newValue) => Lines.Add($"property-change:{atSpiProperty} {path}");
        public void EmitValueChanged(string path, double value) => Lines.Add($"property-change:accessible-value {path}");
        public void EmitRoleChange(string path, uint role) => Lines.Add($"property-change:accessible-role {path}");
        public void EmitStateChanged(string path, string state, bool active) => Lines.Add($"state-changed:{state} {path}");
        public void EmitTextChanged(string path, bool insert, int offset, string text) => Lines.Add($"text-changed {path}");
        public void EmitTextCaretMoved(string path, int offset) => Lines.Add($"text-caret-moved {path}");
        public void EmitTextSelectionChanged(string path) => Lines.Add($"text-selection-changed {path}");
        public void EmitAnnouncement(string path, string text, int politeness) => Lines.Add($"announcement {path}");
        public void EmitCacheAdd(IAtSpiTree tree, AtSpiAccessible node) => Lines.Add($"cache-add {node.Path}");
        public void EmitCacheRemove(string path) => Lines.Add($"cache-remove {path}");
    }

    private static string AtSpiPath(Element element) => AtSpiConstants.AccessiblePathPrefix + element.Id.Value;

    /// <summary>A screen of 300 rows, a dialog open over it (a title, a label for a step to change, a
    /// Cancel button), past its fade-in.</summary>
    private static (BoxElement Scrim, BoxElement Panel, TextElement Tick, Task<bool> Shown) OpenDialog(HeadlessSession session)
    {
        session.Window.SetRoot(Flex.Column().FlexGrow(1).Children(
            TextB.New("Screen"), ScrollView.New().FlexGrow(1).Children(Rows(300))));
        session.PumpFrames(2);
        var tick = TextB.New("Fetching 0 of 100").FontSize(13f);
        BoxElement? panel = null;
        var shown = Dialog.Show<bool>(session.Window.Window, c => panel = Flex.Column(gap: 8).Width(Dimension.Px(360)).Padding(16)
            .Background(Color.White).Children(
                TextB.New("Fetching origin").FontSize(15f), tick,
                Button.New("Cancel").OnClick(_ => c.Close(false))));
        for (int i = 0; i < 12; i++) session.PumpFrame();   // past the 120 ms fade-in
        var scrim = Assert.IsType<BoxElement>(Assert.Single(session.Window.OverlayContents));
        return (scrim, panel!, tick, shown!);
    }

    /// <summary>A modal kept in the app's own tree, as Meridian's Options: a card of role Dialog on a
    /// backdrop over the window whose click closes it, over 300 rows.</summary>
    private static (BoxElement Backdrop, BoxElement Card, TextElement Tick, Func<bool> Open) MountInTreeModal(HeadlessSession session)
    {
        bool open = true;
        var tick = TextB.New("Saved 0").FontSize(13f);
        var card = Flex.Column(gap: 8).Width(Dimension.Px(360)).Padding(16).Background(Color.White)
            .AccessibleRole(AccessibilityRole.Dialog).AccessibleName("Options")
            .Children(TextB.New("Options").FontSize(14f), tick, Checkbox.New(false, "Wrap lines"));
        card.On<ClickEvent>(e => e.StopPropagation = true);
        var backdrop = Box.New().Width(Dimension.Percent(100)).Height(Dimension.Percent(100))
            .JustifyContent(JustifyContent.Center).AlignItems(AlignItems.Center).Children(card);
        backdrop.Position.Type = PositionType.Absolute;
        backdrop.Position.Top = Dimension.Px(0);
        backdrop.Position.Left = Dimension.Px(0);
        backdrop.On<ClickEvent>(e => { e.StopPropagation = true; open = false; });
        session.Window.SetRoot(Box.New().FlexGrow(1).Children(ScrollView.New().FlexGrow(1).Children(Rows(300)), backdrop));
        session.PumpFrames(2);
        return (backdrop, card, tick, () => open);
    }

    private static Element Rows(int count)
    {
        var rows = new Element[count];
        for (int i = 0; i < count; i++)
            rows[i] = Flex.Row(gap: 8).FlexShrink(0).Padding(4).Children(TextB.New($"{i}"), TextB.New($"row {i}"));
        return Flex.Column().FlexShrink(0).Children(rows);
    }

    /// <summary>Past the coalesced refreshes the steps before left due.</summary>
    private static void Settle(HeadlessSession session)
    {
        for (int i = 0; i < 3; i++) session.PumpFrame(TimeSpan.FromMilliseconds(200));
    }

    [Fact]
    public void ADialogsScrim_IsANodeOfNoRoleAndNoName_AndTheDialogIsAsItWas()
    {
        using var session = HeadlessSession.Create(1000, 700);
        var recording = session.Window.InstallAccessibilityBridge(new Recording());
        session.Window.SetRoot(Flex.Column().Children(TextB.New("Screen"), Button.New("Delete")));
        session.PumpFrames(2);
        _ = Dialog.Confirm(session.Window.Window, "Delete branch?", "The branch feature/tidy has 3 commits no other branch has.",
            confirmText: "Delete", destructive: true);
        for (int i = 0; i < 12; i++) session.PumpFrame();
        var scrim = Assert.Single(session.Window.OverlayContents);

        var tree = recording.Tree!;
        var scrimNode = Assert.Single(tree.Children, n => n.ElementId == scrim.Id);
        Assert.Equal(AccessibilityRole.None, scrimNode.Role);
        Assert.Null(scrimNode.Name);
        var dialog = Assert.Single(scrimNode.Children);
        Assert.Equal(AccessibilityRole.Dialog, dialog.Role);
        Assert.Equal("Delete branch?", dialog.Name);
        // Its controls are its own, each named by its label: no button reads the dialog's text.
        Assert.Equal(["Cancel", "Delete", "Delete"], Walk(tree).Where(n => n.Role == AccessibilityRole.Button).Select(n => n.Name).Order());
        Assert.DoesNotContain(Walk(tree), n => n.Name?.Contains("3 commits") == true && n.Role != AccessibilityRole.Label);
        Assert.NotNull(session.Window.FindByRole(AccessibilityRole.Dialog, "Delete branch?"));
    }

    [Fact]
    public void TheScrim_StillClosesTheDialog_OnAClickOutsideIt_AndEscapeStillCloses()
    {
        using var session = HeadlessSession.Create(1000, 700);
        session.Window.InstallAccessibilityBridge(new Recording());
        var (scrim, panel, _, shown) = OpenDialog(session);
        var box = panel.Layout.AbsoluteBorderBox;
        session.Window.Click(box.X - 40, box.Y + 10);   // the scrim, left of the panel
        session.PumpFrame();
        Assert.True(shown.IsCompleted);
        Assert.False(shown.Result);
        Assert.Empty(session.Window.OverlayContents);

        (_, _, _, shown) = OpenDialog(session);
        session.Window.PressKey(KeyCode.Escape);
        session.PumpFrame();
        Assert.True(shown.IsCompleted);
        Assert.Empty(session.Window.OverlayContents);
        Assert.True(AccessibilityTreeBuilder.IsSemantic(panel));
        Assert.Equal(AccessibilityRole.None, AccessibilityTreeBuilder.ResolveRole(scrim));
    }

    [Fact]
    public void ALabelTickingInADialog_IsPatched_NoTreeBuilt_AndOnlyItsNameIsTold()
    {
        using var session = HeadlessSession.Create(1000, 700);
        var recording = session.Window.InstallAccessibilityBridge(new Recording());
        var (scrim, _, tick, _) = OpenDialog(session);
        Settle(session);
        int trees = session.Window.AccessibilityTreesBuilt, from = recording.Told.Count;

        for (int i = 1; i <= 20; i++)
        {
            tick.Content = $"Fetching {i} of 100";
            session.PumpFrame(TimeSpan.FromMilliseconds(50));
        }
        Settle(session);

        // Before: 20 trees, and 40 names told, the scrim's (all of the dialog's text) with each.
        Assert.Equal(0, session.Window.AccessibilityTreesBuilt - trees);
        Assert.Equal(Enumerable.Repeat(tick.Id, 20), recording.NamesToldSince(from));
        Assert.Equal("Fetching 20 of 100", Find(recording.Tree, tick)?.Name);
        Assert.Null(Find(recording.Tree, scrim)?.Name);
    }

    [Fact]
    public void AnInTreeModalsBackdrop_HasNoNode_ItsCardIsItsParentsChild_AndItsLabelIsPatched()
    {
        using var session = HeadlessSession.Create(1000, 700);
        var recording = session.Window.InstallAccessibilityBridge(new Recording());
        var (backdrop, card, tick, open) = MountInTreeModal(session);
        Settle(session);

        var tree = recording.Tree!;
        Assert.Null(Find(tree, backdrop));
        var cardNode = Assert.Single(tree.Children, n => n.ElementId == card.Id);
        Assert.Equal(AccessibilityRole.Dialog, cardNode.Role);
        Assert.Equal("Options", cardNode.Name);

        int trees = session.Window.AccessibilityTreesBuilt, from = recording.Told.Count;
        for (int i = 1; i <= 20; i++)
        {
            tick.Content = $"Saved {i}";
            session.PumpFrame(TimeSpan.FromMilliseconds(50));
        }
        Settle(session);

        // Before: 20 trees, 40 names (the backdrop's with each), and every layout refresh failed in
        // place on "a node with children changed".
        Assert.Equal(0, session.Window.AccessibilityTreesBuilt - trees);
        Assert.Equal(Enumerable.Repeat(tick.Id, 20), recording.NamesToldSince(from));
        Assert.Null(session.Window.Window.AccessibilityLayoutRefreshMismatch);

        // Still the backdrop to the pointer: a click beside the card closes it.
        var box = card.Layout.AbsoluteBorderBox;
        session.Window.Click(box.X - 40, box.Y + 10);
        Assert.False(open());
    }

    [Fact]
    public void ABoxWithAClick_IsAButton_UnlessAChildOfItIsADialog()
    {
        static BoxElement Clickable(params Element[] children)
        {
            var box = Box.New().Children(children);
            box.On<ClickEvent>(_ => { });
            return box;
        }

        var card = Clickable(TextB.New("Open the card"));
        Assert.Equal(AccessibilityRole.Button, AccessibilityTreeBuilder.ResolveRole(card));

        var dialog = Box.New().AccessibleRole(AccessibilityRole.Dialog).Children(TextB.New("Inside"));
        var backdrop = Clickable(dialog);
        Assert.Equal(AccessibilityRole.None, AccessibilityTreeBuilder.ResolveRole(backdrop));
        Assert.False(AccessibilityTreeBuilder.IsSemantic(backdrop));

        // The rule looks at the box's children only: a dialog further down is inside a button.
        Assert.Equal(AccessibilityRole.Button, AccessibilityTreeBuilder.ResolveRole(Clickable(Box.New().Children(
            Box.New().AccessibleRole(AccessibilityRole.Dialog).Children(TextB.New("Deeper"))))));

        // An explicit role still wins, and a name makes the backdrop a node again.
        var group = Clickable(Box.New().AccessibleRole(AccessibilityRole.Dialog));
        group.Role = AccessibilityRole.Group;
        Assert.Equal(AccessibilityRole.Group, AccessibilityTreeBuilder.ResolveRole(group));
        var named = Clickable(Box.New().AccessibleRole(AccessibilityRole.Dialog));
        named.AccessibleName = "Close";
        Assert.True(AccessibilityTreeBuilder.IsSemantic(named));
        Assert.Equal(AccessibilityRole.None, AccessibilityTreeBuilder.ResolveRole(named));
    }

    // ── What the bridges tell ──

    /// <summary>A command palette over 400 commands, open over 300 rows, with <paramref name="bridge"/> listening.</summary>
    private static CommandPalette OpenPalette(HeadlessSession session, IAccessibilityBridge bridge)
    {
        session.Window.InstallAccessibilityBridge(bridge);
        session.Window.SetRoot(Flex.Column().FlexGrow(1).Children(
            TextB.New("Palette"), ScrollView.New().FlexGrow(1).Children(Rows(300))));
        session.PumpFrames(2);
        // Half match "b": past the 100 rows the palette shows, so its "… more" line changes with each key.
        var commands = Enumerable.Range(0, 400).Select(i => new PaletteCommand($"{(i % 2 == 0 ? "branch" : "commit")} {i}", () => { })).ToList();
        var palette = new CommandPalette(() => commands);
        palette.Open(session.Window.Window);
        for (int i = 0; i < 12; i++) session.PumpFrame();
        Settle(session);
        return palette;
    }

    /// <summary>"b" on even keys, Backspace on odd: 200 matches, then 400 again.</summary>
    private static void Key(HeadlessSession session, int key)
    {
        if (key % 2 == 0) session.Window.TypeText("b");
        else session.Window.PressKey(KeyCode.Backspace);
        session.PumpFrame(TimeSpan.FromMilliseconds(120));
    }

    /// <summary>The palette's "… more — type to narrow them down" line.</summary>
    private static TextElement MoreLine(HeadlessSession session)
    {
        static TextElement? Search(Element element)
        {
            if (element is TextElement { Content: { } text } line && text.EndsWith(" more — type to narrow them down")) return line;
            foreach (var child in element.Children)
                if (Search(child) is { } found) return found;
            return null;
        }
        return Search(Assert.Single(session.Window.OverlayContents)) ?? throw new InvalidOperationException("no \"… more\" line");
    }

    [WindowsFact]
    public void UIA_APaletteKeystroke_RaisesOneNameChange_NoneOnTheScrim_WhichIsAPaneWithNoInvoke()
    {
        using var hwnd = TestHwnd.Create();
        using var session = HeadlessSession.Create(1000, 700);
        var window = session.Window.Window;
        var router = new AccessibilityActionRouter(window);
        var host = new UiaHostBridge(window, router);
        window.SetUiaHostBridge(host);
        window.SetActionRouter(router);
        var raised = new List<UiaRaised>();
        using var bridge = new WindowsAccessibilityBridge(hwnd.Handle, host) { Raised = raised.Add };
        var palette = OpenPalette(session, new Recording(bridge));
        var scrim = Assert.Single(session.Window.OverlayContents);
        var wrapper = Wrapper(bridge, scrim);
        Assert.Equal(UiaInterop.UIA_PaneControlTypeId, IntProperty(wrapper, UiaInterop.UIA_ControlTypePropertyId));
        Assert.Equal(0, wrapper.GetPatternProvider(UiaInterop.UIA_InvokePatternId, out var invoke));
        Assert.Equal(IntPtr.Zero, invoke);
        var dialog = Wrapper(bridge, scrim.Children[0]);
        Assert.Equal(UiaInterop.UIA_WindowControlTypeId, IntProperty(dialog, UiaInterop.UIA_ControlTypePropertyId));

        for (int key = 0; key < 6; key++)
        {
            raised.Clear();
            Key(session, key);
            // Before: two, the scrim's (all of the palette's text) and the "… more" line's.
            var names = raised.Where(r => r is { Kind: UiaRaisedKind.Property, Id: UiaInterop.UIA_NamePropertyId }).ToList();
            Assert.Equal(MoreLine(session).Id, Assert.Single(names).Element);
            Assert.Equal(key % 2 == 0 ? "b" : "", palette.Query);
        }
    }

    [Fact]
    public void ATSPI_APaletteKeystroke_SendsOneNameChange_NoneOnTheScrim_WhichIsAPanelHoldingTheDialog()
    {
        var signals = new Signals();
        using var session = HeadlessSession.Create(1000, 700);
        using var atSpi = new LinuxAccessibilityBridge(signals);
        var palette = OpenPalette(session, new Recording(atSpi));
        var scrim = Assert.Single(session.Window.OverlayContents);
        var tree = (IAtSpiTree)atSpi;
        Assert.Equal(AtSpiConstants.RolePanel, tree.Find(AtSpiPath(scrim))!.Role);
        Assert.Equal("", tree.Find(AtSpiPath(scrim))!.Name);
        var dialog = tree.Find(AtSpiPath(scrim.Children[0]))!;
        Assert.Equal(AtSpiConstants.RoleDialog, dialog.Role);
        Assert.Equal("Command palette", dialog.Name);
        Assert.Equal(AtSpiPath(scrim), dialog.ParentPath);

        for (int key = 0; key < 6; key++)
        {
            signals.Lines.Clear();
            Key(session, key);
            var names = signals.Lines.Where(l => l.StartsWith("property-change:accessible-name ")).ToList();
            Assert.Equal(AtSpiPath(MoreLine(session)), Assert.Single(names).Split(' ')[1]);
            Assert.Equal(key % 2 == 0 ? "b" : "", palette.Query);
        }
    }

    private static UiaFragment Wrapper(WindowsAccessibilityBridge bridge, Element element)
    {
        var native = bridge.NativeFragment(element.Id);
        Assert.NotEqual(IntPtr.Zero, native);
        try { return Assert.IsType<UiaFragment>(NativeProvider.LogicOf(native)); }
        finally { Marshal.Release(native); }
    }

    private static int IntProperty(UiaFragment wrapper, int propertyId)
    {
        wrapper.GetPropertyValue(propertyId, out var value);
        Assert.Equal(UiaVariant.VT_I4, value.Vt);
        return value.IntValue;
    }
}
