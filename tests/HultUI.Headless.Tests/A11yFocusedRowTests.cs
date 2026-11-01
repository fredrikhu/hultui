// The AT-SPI bridge is Linux's, but with its signals recorded instead of sent it runs anywhere:
// nothing here reaches a bus.
#pragma warning disable CA1416
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using HultUI.Core.Accessibility;
using HultUI.Core.Builders;
using HultUI.Core.Elements;
using HultUI.Core.Events;
using HultUI.Core.Primitives;
using HultUI.Core.Reactivity;
using HultUI.Core.RichText;
using HultUI.Platform.Accessibility.Linux;
using HultUI.Platform.Accessibility.Windows;
using HultUI.Testing;
using Xunit;
using TextB = HultUI.Core.Builders.Text;

namespace HultUI.Headless.Tests;

/// <summary>
/// A list that publishes one node per item has no nodes for what its rows hold, so a control in a
/// row could take the keyboard and no reader heard of it: the focus was on nothing. Junction's
/// review composer is such a row, and what was typed there could not be reached (plan 17, round 6).
/// The row that holds the focus is now read through its elements, under its item's node, for as
/// long as it holds it. Every other row is its item, and every item is still there.
/// </summary>
public sealed class A11yFocusedRowTests : IDisposable
{
    // The editor tells a reader its caret only while one listens.
    public A11yFocusedRowTests() => AccessibilityClients.Force = true;

    public void Dispose() => AccessibilityClients.Force = null;

    private const int CardRow = 2, ReplyRow = 6;
    private const float RowHeight = 24f, CardHeight = 140f;

    /// <summary>A list of <paramref name="count"/> rows of two heights, under a button: a card with
    /// a label, an editor and a button in row 2, a row with a button in row 6.</summary>
    private sealed class Rig
    {
        public readonly Ref<RichTextEditorElement> Editor = new();
        public readonly Ref<ButtonElement> Post = new();
        public readonly Ref<ButtonElement> Reply = new();
        public TextElement Label = null!;
        public ButtonElement Above = null!;
        public VirtualListElement<int> List = null!;

        public RichTextEditorElement EditorElement => Editor.Current!;

        public Element Build(int count)
        {
            Above = Button.New("Above");
            List = VirtualListElement<int>.New()
                .ItemHeight(RowHeight)
                .Items(Enumerable.Range(0, count).ToList())
                .RowHeights((_, i) => i == CardRow ? CardHeight : RowHeight)
                .RenderWith((i, _) => Row(i))
                .AccessibleItemLabel(i => $"Item {i}")
                .FlexGrow(1);
            return Flex.Column().Width(700).Height(400).Children(Above, List);
        }

        private Element Row(int i)
        {
            if (i == CardRow)
            {
                var editor = RichTextEditor.New(RichDocument.Empty).AccessibleName("Comment").Height(Dimension.Px(60)).WithRef(Editor);
                var post = Button.New("Post").WithRef(Post);
                Label = TextB.New("New comment").FontSize(12f);
                var card = Flex.Column(gap: 4).Children(Label, editor, post);
                card.Key = "card";
                return card;
            }
            if (i == ReplyRow) return Flex.Row().Children(TextB.New("A thread").FontSize(12f), Button.New("Reply").WithRef(Reply));
            return TextB.New($"Row {i}").FontSize(12f);
        }
    }

    private static Rig Mount(HeadlessSession session, int count = 40)
    {
        var rig = new Rig();
        session.Window.SetRoot(rig.Build(count));
        Settle(session);
        return rig;
    }

    /// <summary>The frames and the time that see the layout refreshes left pending out.</summary>
    private static void Settle(HeadlessSession session)
    {
        session.PumpFrames(3);
        session.PumpFrame(TimeSpan.FromMilliseconds(120));
        session.PumpFrame(TimeSpan.FromMilliseconds(120));
    }

    private static AccessibilityNode? Find(AccessibilityNode? node, ElementId id)
    {
        if (node is null) return null;
        if (node.ElementId == id) return node;
        foreach (var child in node.Children)
            if (Find(child, id) is { } found) return found;
        return null;
    }

    private static AccessibilityNode? Find(AccessibilityNode? node, Func<AccessibilityNode, bool> match)
    {
        if (node is null) return null;
        if (match(node)) return node;
        foreach (var child in node.Children)
            if (Find(child, match) is { } found) return found;
        return null;
    }

    private static AccessibilityNode ListNode(AccessibilityNode tree, Rig rig) => Find(tree, rig.List.Id)!;

    private static void AssertFresh(HeadlessSession session, AccessibilityNode tree)
    {
        var diff = AccessibilityTreeDiffer.Diff(tree, session.Window.Window.BuildAccessibilityTree());
        Assert.True(diff.IsEmpty,
            $"the applied tree is stale: {string.Join(", ", diff.PropertyChanged.Select(c => c.PropertyName))}; {diff.Added.Count} added, {diff.Removed.Count} removed");
    }

    // ── What a reader finds ──

    [Fact]
    public void ARowThatTakesTheFocus_IsReadThroughItsElements_UnderItsItem()
    {
        using var session = HeadlessSession.Create(700, 400);
        var bridge = session.Window.InstallAccessibilityBridge();
        var rig = Mount(session);
        var before = ListNode(bridge.CurrentTree!, rig);
        Assert.Equal(40, before.Children.Count);
        Assert.All(before.Children, item => Assert.Empty(item.Children));
        Assert.Null(Find(bridge.CurrentTree, rig.EditorElement.Id));

        session.Window.Click(rig.EditorElement);
        session.PumpFrames(2);

        // Every item is still there, and the row's are under its own.
        var list = ListNode(bridge.CurrentTree!, rig);
        Assert.Equal(40, list.Children.Count);
        var item = list.Children[CardRow];
        Assert.Equal("Item 2", item.Name);
        Assert.Equal(AccessibilityRole.ListItem, item.Role);
        Assert.Equal(
            [(AccessibilityRole.Label, "New comment"), (AccessibilityRole.TextInput, "Comment"), (AccessibilityRole.Button, "Post")],
            item.Children.Select(c => (c.Role, c.Name)));
        Assert.All(list.Children.Where(c => c != item), other => Assert.Empty(other.Children));

        // The focus is on the editor's node, and a reader is told so.
        var editor = Find(bridge.CurrentTree, rig.EditorElement.Id)!;
        Assert.True(editor.IsFocused);
        Assert.Same(editor, bridge.FocusedNode);
        Assert.NotNull(editor.Text);
        AssertFresh(session, bridge.CurrentTree!);
    }

    [Fact]
    public void AKeyInTheRow_BuildsNoTree_AndIsTold()
    {
        using var session = HeadlessSession.Create(700, 400);
        var bridge = session.Window.InstallAccessibilityBridge();
        var rig = Mount(session);
        session.Window.Click(rig.EditorElement);
        Settle(session);
        var tree = bridge.CurrentTree!;
        var node = Find(tree, rig.EditorElement.Id)!;
        int trees = session.Window.AccessibilityTreesBuilt;
        bridge.ClearDiffs();

        session.Window.TypeText("x");
        session.PumpFrame();

        Assert.Equal(trees, session.Window.AccessibilityTreesBuilt);
        Assert.Same(tree, bridge.CurrentTree);
        var patch = Assert.Single(bridge.Diffs);
        Assert.Equal([AccessibilityProperties.Text, AccessibilityProperties.TextSelection],
            patch.PropertyChanged.Select(c => c.PropertyName));
        Assert.All(patch.PropertyChanged, c => Assert.Same(node, c.Node));
        Assert.Equal("x", node.Text!.Text);
        Assert.Equal(1, node.Text.Caret);

        // A caret move the same.
        bridge.ClearDiffs();
        session.Window.PressKey(KeyCode.Left);
        session.PumpFrame();
        Assert.Equal(AccessibilityProperties.TextSelection, Assert.Single(Assert.Single(bridge.Diffs).PropertyChanged).PropertyName);
        Assert.Equal(0, node.Text!.Caret);
        Assert.Equal(trees, session.Window.AccessibilityTreesBuilt);
        AssertFresh(session, tree);
    }

    [Fact]
    public void AChangeOfALeafInTheRow_IsPatchedUnderTheItem()
    {
        using var session = HeadlessSession.Create(700, 400);
        var bridge = session.Window.InstallAccessibilityBridge();
        var rig = Mount(session);
        session.Window.Click(rig.EditorElement);
        Settle(session);
        int trees = session.Window.AccessibilityTreesBuilt;
        bridge.ClearDiffs();

        rig.Label.AccessibleName = "New comment on line 12";
        session.PumpFrame();

        Assert.Equal(trees, session.Window.AccessibilityTreesBuilt);
        var change = Assert.Single(Assert.Single(bridge.Diffs).PropertyChanged);
        Assert.Equal(AccessibilityProperties.Name, change.PropertyName);
        var node = Find(bridge.CurrentTree, rig.Label.Id)!;
        Assert.Equal("New comment on line 12", node.Name);
        Assert.Same(ListNode(bridge.CurrentTree!, rig).Children[CardRow], node.Parent);
        AssertFresh(session, bridge.CurrentTree!);
    }

    [Fact]
    public void TheFocusMovingInsideTheRow_BuildsNoTree()
    {
        using var session = HeadlessSession.Create(700, 400);
        var bridge = session.Window.InstallAccessibilityBridge();
        var rig = Mount(session);
        session.Window.Click(rig.EditorElement);
        Settle(session);
        int trees = session.Window.AccessibilityTreesBuilt;

        rig.Post.Current!.Focus();
        Settle(session);

        Assert.Equal(trees, session.Window.AccessibilityTreesBuilt);
        Assert.True(Find(bridge.CurrentTree, rig.Post.Current!.Id)!.IsFocused);
        Assert.False(Find(bridge.CurrentTree, rig.EditorElement.Id)!.IsFocused);
        AssertFresh(session, bridge.CurrentTree!);
    }

    [Fact]
    public void ARowTheFocusLeaves_IsItsItemAgain_AndANextRowOpens()
    {
        using var session = HeadlessSession.Create(700, 400);
        var bridge = session.Window.InstallAccessibilityBridge();
        var rig = Mount(session);
        session.Window.Click(rig.EditorElement);
        session.PumpFrames(2);

        // Into the other row: one opens, the other closes, in one tree.
        rig.Reply.Current!.Focus();
        session.PumpFrames(2);
        var list = ListNode(bridge.CurrentTree!, rig);
        Assert.Empty(list.Children[CardRow].Children);
        Assert.Contains(list.Children[ReplyRow].Children, c => c.Name == "Reply" && c.IsFocused);
        Assert.Equal(rig.Reply.Current!.Id, bridge.FocusedNode?.ElementId);
        AssertFresh(session, bridge.CurrentTree!);

        // Out of the list: no row is open, and nothing of them is left.
        rig.Above.Focus();
        session.PumpFrames(2);
        list = ListNode(bridge.CurrentTree!, rig);
        Assert.All(list.Children, item => Assert.Empty(item.Children));
        Assert.Equal(rig.Above.Id, bridge.FocusedNode?.ElementId);
        AssertFresh(session, bridge.CurrentTree!);
    }

    [Fact]
    public void AChangeInARowWithoutTheFocus_BuildsNoTree()
    {
        using var session = HeadlessSession.Create(700, 400);
        var bridge = session.Window.InstallAccessibilityBridge();
        var rig = Mount(session);
        session.Window.Click(rig.EditorElement);
        Settle(session);
        int trees = session.Window.AccessibilityTreesBuilt;

        rig.Reply.Current!.AccessibleName = "Answer";
        session.PumpFrame();

        Assert.Equal(trees, session.Window.AccessibilityTreesBuilt);
    }

    [Fact]
    public void AKeyedRowMadeAgain_StaysOpen_WithItsEditor()
    {
        using var session = HeadlessSession.Create(700, 400);
        var bridge = session.Window.InstallAccessibilityBridge();
        var rig = Mount(session);
        session.Window.Click(rig.EditorElement);
        Settle(session);
        var editor = rig.EditorElement;

        // As a formatting bar that moved a button has it: the row made again and patched into the
        // one on screen, while a key is told in the same frame.
        session.Window.TypeText("x");
        rig.List.InvalidateRow(CardRow);
        session.PumpFrames(2);

        Assert.Same(editor, session.Window.Window.FocusedElement);
        var node = Find(bridge.CurrentTree, editor.Id)!;
        Assert.Equal("x", node.Text!.Text);
        Assert.Same(node.Parent, ListNode(bridge.CurrentTree!, rig).Children[CardRow]);
        Settle(session);
        AssertFresh(session, bridge.CurrentTree!);
    }

    // ── Scrolling ──

    [Fact]
    public void AnItem_StandsWhereItsRowIsDrawn()
    {
        using var session = HeadlessSession.Create(700, 400);
        var bridge = session.Window.InstallAccessibilityBridge();
        var rig = Mount(session);
        session.Window.Click(rig.EditorElement);
        session.PumpFrames(2);

        var content = rig.List.Layout.AbsoluteContentBox;
        var items = ListNode(bridge.CurrentTree!, rig).Children;
        Assert.Equal(content.Y + 2 * RowHeight, items[CardRow].BoundingRect.Y);
        Assert.Equal(CardHeight, items[CardRow].BoundingRect.Height);
        // The rows under the tall one are below it, and the editor is inside its item.
        Assert.Equal(content.Y + 2 * RowHeight + CardHeight, items[CardRow + 1].BoundingRect.Y);
        var editor = Find(bridge.CurrentTree, rig.EditorElement.Id)!;
        Assert.True(items[CardRow].BoundingRect.Contains(new Point(editor.BoundingRect.X + 4, editor.BoundingRect.Y + 4)));
        Assert.Equal(rig.EditorElement.WindowBorderBox.Y, editor.BoundingRect.Y);
    }

    [Fact]
    public void AWheelOverTheList_IsRefreshedInPlace_WithTheRowOpen()
    {
        using var session = HeadlessSession.Create(700, 400);
        var bridge = session.Window.InstallAccessibilityBridge();
        var rig = Mount(session);
        session.Window.Click(rig.EditorElement);
        Settle(session);
        var tree = bridge.CurrentTree!;
        int trees = session.Window.AccessibilityTreesBuilt;
        int refreshes = session.Window.Window.AccessibilityLayoutRefreshesInPlace;
        float was = Find(tree, rig.EditorElement.Id)!.BoundingRect.Y;

        var box = rig.List.WindowContentBox;
        session.Window.Wheel(box.X + 40, box.Y + 200, 0, -1);
        Settle(session);

        Assert.True(rig.List.ScrollOffsetY > 0, "the list did not scroll");
        Assert.Equal(trees, session.Window.AccessibilityTreesBuilt);
        Assert.True(session.Window.Window.AccessibilityLayoutRefreshesInPlace > refreshes,
            $"no refresh in place: {session.Window.Window.AccessibilityLayoutRefreshMismatch}");
        Assert.Same(tree, bridge.CurrentTree);
        Assert.Equal(was - rig.List.ScrollOffsetY, Find(tree, rig.EditorElement.Id)!.BoundingRect.Y);
        AssertFresh(session, tree);
    }

    [Fact]
    public void ARowInAHeldHeader_IsReadWhereItIsDrawn()
    {
        using var session = HeadlessSession.Create(700, 400);
        var bridge = session.Window.InstallAccessibilityBridge();
        var button = new Ref<ButtonElement>();
        var list = VirtualListElement<int>.New()
            .ItemHeight(RowHeight)
            .Items(Enumerable.Range(0, 200).ToList())
            .StickyRows((_, i) => i % 50 == 0)
            .RenderWith((i, _) => i % 50 == 0
                ? Flex.Row().Background(Color.White).Children(Button.New($"Header {i}").WithRef(button))
                : TextB.New($"Row {i}").FontSize(12f))
            .AccessibleItemLabel(i => $"Item {i}")
            .FlexGrow(1);
        session.Window.SetRoot(Flex.Column().Width(700).Height(400).Children(list));
        Settle(session);
        button.Current!.Focus();
        Settle(session);

        list.ScrollOffsetY = RowHeight * 10.5f;
        Settle(session);

        var node = Find(bridge.CurrentTree, button.Current!.Id)!;
        Assert.True(node.IsFocused);
        Assert.Same(ListNode0(bridge.CurrentTree!, list).Children[0], node.Parent);
        Assert.Equal(list.Layout.AbsoluteContentBox.Y, node.BoundingRect.Y, 2);
        Assert.False(node.IsOffscreen);
        AssertFresh(session, bridge.CurrentTree!);
    }

    private static AccessibilityNode ListNode0(AccessibilityNode tree, Element list) => Find(tree, list.Id)!;

    // ── A list served on demand ──

    [Fact]
    public void AListOverTheEagerLimit_HasItemNodes_WhileARowIsOpen()
    {
        using var session = HeadlessSession.Create(700, 400);
        var bridge = session.Window.InstallAccessibilityBridge();
        var rig = Mount(session, count: 3_000);
        Assert.True(ListNode(bridge.CurrentTree!, rig).IsVirtualItemContainer);

        session.Window.Click(rig.EditorElement);
        Settle(session);
        var list = ListNode(bridge.CurrentTree!, rig);
        Assert.False(list.IsVirtualItemContainer);
        Assert.Equal(3_000, list.Children.Count);
        Assert.True(Find(bridge.CurrentTree, rig.EditorElement.Id)!.IsFocused);

        // Keys, and a move of the list's selection to a row off screen, which makes no row, are
        // patched into it as into any list of nodes.
        int trees = session.Window.AccessibilityTreesBuilt;
        session.Window.TypeText("x");
        session.PumpFrame();
        rig.List.SelectedIndex = 700;
        session.PumpFrame();
        Assert.Equal(trees, session.Window.AccessibilityTreesBuilt);
        Assert.True(list.Children[700].IsSelected);
        AssertFresh(session, bridge.CurrentTree!);

        // A wheel is refreshed in place too.
        var box = rig.List.WindowContentBox;
        session.Window.Wheel(box.X + 40, box.Y + 200, 0, -1);
        Settle(session);
        Assert.Equal(trees, session.Window.AccessibilityTreesBuilt);
        AssertFresh(session, bridge.CurrentTree!);

        // The focus gone, the list is served on demand again.
        rig.Above.Focus();
        session.PumpFrames(2);
        list = ListNode(bridge.CurrentTree!, rig);
        Assert.True(list.IsVirtualItemContainer);
        Assert.Empty(list.Children);
        AssertFresh(session, bridge.CurrentTree!);
    }

    [Fact]
    public void ALabelChangingOnTheOpenRowsItem_KeepsTheRowUnderIt()
    {
        using var session = HeadlessSession.Create(700, 400);
        var bridge = session.Window.InstallAccessibilityBridge();
        var rig = Mount(session);
        session.Window.Click(rig.EditorElement);
        Settle(session);
        string label = "Item 2";
        rig.List.AccessibleLabel = i => i == CardRow ? label : $"Item {i}";
        Settle(session);

        label = "Item 2, changed";
        rig.List.InvalidateRow(CardRow);
        Settle(session);

        var item = ListNode(bridge.CurrentTree!, rig).Children[CardRow];
        Assert.Equal("Item 2, changed", item.Name);
        Assert.NotNull(Find(item, rig.EditorElement.Id));
        AssertFresh(session, bridge.CurrentTree!);
    }

    [Fact]
    public void ACompositeList_IsAsItWas()
    {
        using var session = HeadlessSession.Create(700, 400);
        var bridge = session.Window.InstallAccessibilityBridge();
        var rig = new Rig();
        var root = rig.Build(40);
        rig.List.RowsAreComposite = true;
        session.Window.SetRoot(root);
        Settle(session);
        session.Window.Click(rig.EditorElement);
        session.PumpFrames(2);

        var list = ListNode(bridge.CurrentTree!, rig);
        Assert.DoesNotContain(list.Children, c => c.Role == AccessibilityRole.ListItem);
        Assert.Same(list, Find(bridge.CurrentTree, rig.EditorElement.Id)!.Parent);
    }

    // ── UIA ──

    /// <summary>A real bridge, reported active whether or not a client listens.</summary>
    private sealed class Active(IAccessibilityBridge inner) : IAccessibilityBridge
    {
        public bool IsActive => true;
        public void ApplyChanges(TreeDiff diff, AccessibilityNode root) => inner.ApplyChanges(diff, root);
        public void ApplyPatchedProperties(TreeDiff patch, AccessibilityNode root) => inner.ApplyPatchedProperties(patch, root);
        public void NotifyFocusChanged(AccessibilityNode? focused) => inner.NotifyFocusChanged(focused);
        public void NotifyFocusChanged(AccessibilityNode? focused, AccessibilityNode? previous) => inner.NotifyFocusChanged(focused, previous);
    }

    [SupportedOSPlatform("windows")]
    private sealed class UiaRig : IDisposable
    {
        private readonly TestHwnd _hwnd = TestHwnd.Create();
        public HeadlessSession Session { get; } = HeadlessSession.Create(700, 400);
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

        private T WithRoot<T>(Func<UiaFragmentRoot, T> read)
        {
            var root = Bridge.NativeRootFragmentRoot();
            try { return read(Assert.IsType<UiaFragmentRoot>(NativeProvider.LogicOf(root))); }
            finally { Marshal.Release(root); }
        }

        /// <summary>The element UIA's GetFocus answers, or null.</summary>
        public ElementId? GetFocus() => WithRoot(root =>
        {
            Assert.Equal(0, root.GetFocus(out var focus));
            return Id(focus);
        });

        /// <summary>The element UIA finds at a point in the window.</summary>
        public ElementId? At(Point point) => WithRoot(root =>
        {
            Assert.Equal(0, root.ElementProviderFromPoint(point.X, point.Y, out var hit));
            return Id(hit);
        });

        /// <summary>The element UIA's parent step from <paramref name="id"/> reaches.</summary>
        public ElementId? Parent(ElementId id)
        {
            var native = Bridge.NativeFragment(id);
            Assert.NotEqual(IntPtr.Zero, native);
            try
            {
                var fragment = Assert.IsType<UiaFragment>(NativeProvider.LogicOf(native));
                Assert.Equal(0, fragment.Navigate(UiaInterop.NavigateDirection_Parent, out var parent));
                return Id(parent);
            }
            finally { Marshal.Release(native); }
        }

        private static ElementId? Id(IntPtr native)
        {
            if (native == IntPtr.Zero) return null;
            try { return Assert.IsType<UiaFragment>(NativeProvider.LogicOf(native)).ElementId; }
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
    [SupportedOSPlatform("windows")]
    public void UIA_TheFocusLandsOnTheEditor_ItsTextCanBeRead_AndAKeyIsTold()
    {
        using var rig = new UiaRig();
        var row = Mount(rig.Session);
        rig.Session.Window.Click(row.EditorElement);
        rig.Session.PumpFrames(2);
        var editor = row.EditorElement.Id;

        // A focus event on the editor, once its node is there, and GetFocus answers it.
        Assert.Contains(new UiaRaised(UiaRaisedKind.Event, UiaInterop.UIA_AutomationFocusChangedEventId, editor), rig.Raised);
        Assert.Equal(editor, rig.GetFocus());
        // A client walks up from it through the row's item to the list.
        var item = rig.Parent(editor);
        Assert.Equal(VirtualItemId.For(row.List.Id, CardRow), item);
        Assert.Equal(row.List.Id, rig.Parent(item!.Value));

        Settle(rig.Session);
        int trees = rig.Session.Window.AccessibilityTreesBuilt;
        rig.Raised.Clear();
        rig.Session.Window.TypeText("x");
        rig.Session.PumpFrame();

        Assert.Equal(
        [
            new UiaRaised(UiaRaisedKind.Event, UiaInterop.UIA_Text_TextChangedEventId, editor),
            new UiaRaised(UiaRaisedKind.Event, UiaInterop.UIA_Text_TextSelectionChangedEventId, editor),
        ], rig.Raised);
        Assert.Equal(trees, rig.Session.Window.AccessibilityTreesBuilt);
        // A client's thread reads what was typed and where the caret is.
        var text = Task.Run(() => rig.Bridge.GetText(editor)).Result!;
        Assert.Equal(("x", 1), (text.Text, text.Caret));
    }

    [WindowsFact]
    [SupportedOSPlatform("windows")]
    public void UIA_APointOverTheEditor_FindsTheEditor()
    {
        using var rig = new UiaRig();
        var row = Mount(rig.Session);
        rig.Session.Window.Click(row.EditorElement);
        rig.Session.PumpFrames(2);

        var box = row.EditorElement.WindowBorderBox;
        Assert.Equal(row.EditorElement.Id, rig.At(new Point(box.X + 10, box.Y + 10)));
    }

    // ── AT-SPI ──

    private sealed class Signals : IAtSpiSignals
    {
        public readonly List<string> Lines = [];
        private static string Short(string path) =>
            path.StartsWith(AtSpiConstants.AccessiblePathPrefix, StringComparison.Ordinal)
                ? path[AtSpiConstants.AccessiblePathPrefix.Length..] : "root";
        public void EmitChildrenChanged(string parentPath, string childPath, int index, bool isAdd) => Lines.Add($"children-changed {Short(parentPath)}");
        public void EmitPropertyChange(string path, string atSpiProperty, string newValue) => Lines.Add($"property-change:{atSpiProperty} {Short(path)}");
        public void EmitValueChanged(string path, double value) => Lines.Add($"property-change:accessible-value {Short(path)}");
        public void EmitRoleChange(string path, uint role) => Lines.Add($"property-change:accessible-role {Short(path)}");
        public void EmitStateChanged(string path, string state, bool active) => Lines.Add($"state-changed:{state} {Short(path)} {(active ? 1 : 0)}");
        public void EmitTextChanged(string path, bool insert, int offset, string text) => Lines.Add($"text-changed:{(insert ? "insert" : "delete")} {Short(path)} {offset} {text}");
        public void EmitTextCaretMoved(string path, int offset) => Lines.Add($"text-caret-moved {Short(path)} {offset}");
        public void EmitTextSelectionChanged(string path) => Lines.Add($"text-selection-changed {Short(path)}");
        public void EmitAnnouncement(string path, string text, int politeness) => Lines.Add($"announcement {Short(path)}");
        public void EmitCacheAdd(IAtSpiTree tree, AtSpiAccessible node) => Lines.Add($"cache-add {Short(node.Path)}");
        public void EmitCacheRemove(string path) => Lines.Add($"cache-remove {Short(path)}");
    }

    private static AtSpiAccessible? ObjectOf(LinuxAccessibilityBridge bridge, Element element) =>
        ((IAtSpiTree)bridge).Find(AtSpiConstants.AccessiblePathPrefix + element.Id.Value);

    [Fact]
    public void ATSPI_TheEditorHasAnObject_TheFocusIsOnIt_AndAKeyIsTold()
    {
        using var session = HeadlessSession.Create(700, 400);
        var signals = new Signals();
        using var bridge = new LinuxAccessibilityBridge(signals);
        session.Window.InstallAccessibilityBridge(new Active(bridge));
        var row = Mount(session);
        Assert.Null(ObjectOf(bridge, row.EditorElement));

        session.Window.Click(row.EditorElement);
        session.PumpFrames(2);

        ulong id = row.EditorElement.Id.Value;
        var published = ObjectOf(bridge, row.EditorElement);
        Assert.NotNull(published);
        Assert.True(published.Focused);
        Assert.Contains($"state-changed:focused {id} 1", signals.Lines);

        Settle(session);
        int trees = session.Window.AccessibilityTreesBuilt;
        signals.Lines.Clear();
        session.Window.TypeText("x");
        session.PumpFrame();
        session.Window.PressKey(KeyCode.Left);
        session.PumpFrame();

        Assert.Equal(
        [
            $"text-changed:insert {id} 0 x",
            $"text-caret-moved {id} 1",
            $"text-caret-moved {id} 0",
        ], signals.Lines);
        Assert.Same(published, ObjectOf(bridge, row.EditorElement));
        Assert.Equal(trees, session.Window.AccessibilityTreesBuilt);
        Assert.Equal("x", published.CurrentText!.Text);
    }

    [Fact]
    public void ATSPI_ARowTheFocusLeaves_LosesItsObjects()
    {
        using var session = HeadlessSession.Create(700, 400);
        var signals = new Signals();
        using var bridge = new LinuxAccessibilityBridge(signals);
        session.Window.InstallAccessibilityBridge(new Active(bridge));
        var row = Mount(session);
        session.Window.Click(row.EditorElement);
        session.PumpFrames(2);
        Assert.NotNull(ObjectOf(bridge, row.EditorElement));

        row.Above.Focus();
        session.PumpFrames(2);

        Assert.Null(ObjectOf(bridge, row.EditorElement));
        Assert.True(ObjectOf(bridge, row.Above)!.Focused);
    }
}
