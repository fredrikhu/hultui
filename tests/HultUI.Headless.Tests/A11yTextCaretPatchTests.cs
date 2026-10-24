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
using HultUI.Core.RichText;
using HultUI.Platform.Accessibility.Linux;
using HultUI.Platform.Accessibility.Windows;
using HultUI.Testing;
using Xunit;
using TextB = HultUI.Core.Builders.Text;

namespace HultUI.Headless.Tests;

/// <summary>
/// A move of the caret or the selection in the rich text editor builds no accessibility tree
/// (plan 17, round 4). The editor asked the window to rebuild for every caret move while a reader
/// listened: each arrow key built and diffed the whole tree, 79 KB a key beside 300 rows, and the
/// AT-SPI bridge made the whole document's text to read the caret from it (475 KB a key at 5,000
/// blocks). The window now takes the editor's new version of its text, puts it on the applied
/// tree's node in place and hands the bridges the move as a patch, as it does a list's selection
/// move; AT-SPI reads the caret from the version.
/// </summary>
public sealed class A11yTextCaretPatchTests : IDisposable
{
    // The editor tells a reader its caret only while one listens.
    public A11yTextCaretPatchTests() => AccessibilityClients.Force = true;

    public void Dispose() => AccessibilityClients.Force = null;

    /// <summary>A real bridge, reported active whether or not a client listens.</summary>
    private sealed class Active(IAccessibilityBridge inner) : IAccessibilityBridge
    {
        public bool IsActive => true;
        public void ApplyChanges(TreeDiff diff, AccessibilityNode root) => inner.ApplyChanges(diff, root);
        public void ApplyPatchedProperties(TreeDiff patch, AccessibilityNode root) => inner.ApplyPatchedProperties(patch, root);
        public void NotifyFocusChanged(AccessibilityNode? focused) => inner.NotifyFocusChanged(focused);
        public void NotifyFocusChanged(AccessibilityNode? focused, AccessibilityNode? previous) => inner.NotifyFocusChanged(focused, previous);
    }

    private static RichBlock P(string text) => RichBlock.Paragraph(text);

    private static RichDocument ShortDocument() => new([
        P("first paragraph of the text"),
        P("second paragraph, a little longer than the first one is"),
        P("third"),
    ]);

    /// <summary>A label and an editor (and <paramref name="before"/> above it), the editor focused
    /// with the caret at its start, the layout refresh the mount left pending come and gone.</summary>
    private static (RichTextEditorElement Editor, TextElement Label) Mount(HeadlessSession session,
        RichDocument? doc = null, Element? before = null)
    {
        var editor = RichTextEditor.New(doc ?? ShortDocument()).Width(300f).Height(150f);
        var label = TextB.New("status");
        var root = Flex.Column(gap: 8).Padding(8).Children(label);
        if (before is not null) root.AddChild(before);
        root.AddChild(editor);
        session.Window.SetRoot(root);
        session.PumpFrames(2);
        session.Window.Click(editor);
        session.PumpFrame();
        editor.State.Select(RichSelection.Caret(0, 0));
        session.PumpFrame();
        session.PumpFrame(TimeSpan.FromMilliseconds(120));
        session.PumpFrame(TimeSpan.FromMilliseconds(120));
        return (editor, label);
    }

    private static AccessibilityNode NodeOf(AccessibilityNode tree, Element element) =>
        Find(tree, element.Id) ?? throw new InvalidOperationException("no node for the element");

    private static AccessibilityNode? Find(AccessibilityNode node, ElementId id)
    {
        if (node.ElementId == id) return node;
        foreach (var child in node.Children)
            if (Find(child, id) is { } found) return found;
        return null;
    }

    [Fact]
    public void ACaretMove_BuildsNoTree_IsMarkedOnTheAppliedTree_AndIsToldOnce()
    {
        using var session = HeadlessSession.Create(600, 400);
        var bridge = session.Window.InstallAccessibilityBridge();
        var (editor, label) = Mount(session);
        var tree = bridge.CurrentTree!;
        int trees = session.Window.AccessibilityTreesBuilt;
        bridge.ClearDiffs();

        session.Window.PressKey(KeyCode.Right);
        session.PumpFrame();

        Assert.Equal(trees, session.Window.AccessibilityTreesBuilt);
        Assert.Same(tree, bridge.CurrentTree);
        var patch = Assert.Single(bridge.Diffs);
        Assert.Empty(patch.Added);
        Assert.Empty(patch.Removed);
        Assert.Empty(patch.Reordered);
        var change = Assert.Single(patch.PropertyChanged);
        Assert.Equal(AccessibilityProperties.TextSelection, change.PropertyName);
        Assert.Same(NodeOf(tree, editor), change.Node);
        // The applied node carries the moved caret; the change's previous node the caret before.
        Assert.Equal(1, change.Node.Text!.Caret);
        Assert.Equal(0, change.Previous!.Text!.Caret);
        Assert.Equal(editor.Id, change.Previous.ElementId);

        // The next rebuild compares against the marked tree: it does not tell the move again.
        bridge.ClearDiffs();
        label.Content += "!";
        session.PumpFrame();
        Assert.Equal(trees + 1, session.Window.AccessibilityTreesBuilt);
        Assert.DoesNotContain(bridge.Diffs.SelectMany(d => d.PropertyChanged),
            c => c.PropertyName == AccessibilityProperties.TextSelection);
        Assert.Equal(1, NodeOf(bridge.CurrentTree!, editor).Text!.Caret);
    }

    [Fact]
    public void ASelection_TwoMovesInAFrame_AndAMoveThereAndBack_AreToldAsARebuildWouldTellThem()
    {
        using var session = HeadlessSession.Create(600, 400);
        var bridge = session.Window.InstallAccessibilityBridge();
        var (editor, _) = Mount(session);
        int trees = session.Window.AccessibilityTreesBuilt;
        bridge.ClearDiffs();

        session.Window.PressKey(KeyCode.Right, KeyModifiers.Shift);
        session.Window.PressKey(KeyCode.Right, KeyModifiers.Shift);
        session.PumpFrame();
        var change = Assert.Single(Assert.Single(bridge.Diffs).PropertyChanged);
        Assert.Equal(AccessibilityProperties.TextSelection, change.PropertyName);
        var text = change.Node.Text!;
        Assert.Equal((2, 0, 2), (text.Caret, text.SelectionStart, text.SelectionEnd));

        // There and back in one frame: nothing moved, nothing to tell.
        bridge.ClearDiffs();
        session.Window.PressKey(KeyCode.Right, KeyModifiers.Shift);
        session.Window.PressKey(KeyCode.Left, KeyModifiers.Shift);
        session.PumpFrame();
        Assert.Empty(bridge.Diffs);

        Assert.Equal(trees, session.Window.AccessibilityTreesBuilt);
    }

    [Fact]
    public void AnEditWithItsCaret_IsToldByARebuild_AsBefore()
    {
        using var session = HeadlessSession.Create(600, 400);
        var bridge = session.Window.InstallAccessibilityBridge();
        var (editor, _) = Mount(session);
        int trees = session.Window.AccessibilityTreesBuilt;
        bridge.ClearDiffs();

        session.Window.TypeText("x");
        session.PumpFrame();

        Assert.Equal(trees + 1, session.Window.AccessibilityTreesBuilt);
        Assert.Equal([AccessibilityProperties.Text, AccessibilityProperties.TextSelection],
            bridge.Diffs.SelectMany(d => d.PropertyChanged).Where(c => c.Node.ElementId == editor.Id).Select(c => c.PropertyName));
    }

    [Fact]
    public void ACaretMoveInAnEditorMountedInTheSameHandler_IsInTheTreeItsRebuildBuilds()
    {
        using var session = HeadlessSession.Create(600, 400);
        var bridge = session.Window.InstallAccessibilityBridge();
        var (editor, _) = Mount(session);
        int trees = session.Window.AccessibilityTreesBuilt;

        var second = RichTextEditor.New(ShortDocument()).Width(300f).Height(100f);
        ((Element)editor.Parent!).AddChild(second);
        second.State.Select(RichSelection.Caret(1, 4));
        session.PumpFrame();

        Assert.Equal(trees + 1, session.Window.AccessibilityTreesBuilt);
        Assert.Equal("first paragraph of the text\n".Length + 4, NodeOf(bridge.CurrentTree!, second).Text!.Caret);
    }

    [Fact]
    public void AListAndACaretMovedInOneFrame_AreToldInTreeOrder_InOnePatch()
    {
        using var session = HeadlessSession.Create(600, 600);
        var bridge = session.Window.InstallAccessibilityBridge();
        var list = VirtualListElement<string>.New().ItemHeight(24)
            .RenderWith(t => Flex.Row().Padding(new Thickness(2, 8)).Children(TextB.New(t).FontSize(12f)))
            .Items(Enumerable.Range(0, 5).Select(i => $"item {i}").ToList()).Height(150f);
        var (editor, label) = Mount(session, before: list);
        list.SelectedIndex = 1;
        label.Content += "!";
        session.PumpFrame();
        int trees = session.Window.AccessibilityTreesBuilt;
        bridge.ClearDiffs();

        // The caret first: a diff would still list the list's items first, above the editor.
        session.Window.PressKey(KeyCode.Right);
        list.SelectedIndex = 2;
        session.PumpFrame();

        Assert.Equal(trees, session.Window.AccessibilityTreesBuilt);
        var patch = Assert.Single(bridge.Diffs);
        Assert.Equal(
        [
            (VirtualItemId.For(list.Id, 1), AccessibilityProperties.IsSelected),
            (VirtualItemId.For(list.Id, 2), AccessibilityProperties.IsSelected),
            (editor.Id, AccessibilityProperties.TextSelection),
        ], patch.PropertyChanged.Select(c => (c.Node.ElementId, c.PropertyName)));
    }

    // ── UIA ──

    [WindowsFact]
    [SupportedOSPlatform("windows")]
    public void UIA_ACaretMove_RaisesTextSelectionChanged_WithNoTreeBuilt()
    {
        using var hwnd = TestHwnd.Create();
        using var session = HeadlessSession.Create(600, 400);
        var window = session.Window.Window;
        var router = new AccessibilityActionRouter(window);
        var host = new UiaHostBridge(window, router);
        window.SetUiaHostBridge(host);
        window.SetActionRouter(router);
        using var bridge = new WindowsAccessibilityBridge(hwnd.Handle, host);
        var raised = new List<UiaRaised>();
        bridge.Raised = raised.Add;
        session.Window.InstallAccessibilityBridge(new Active(bridge));
        var (editor, _) = Mount(session);
        int trees = session.Window.AccessibilityTreesBuilt;
        raised.Clear();

        session.Window.PressKey(KeyCode.Right);
        session.PumpFrame();
        session.Window.PressKey(KeyCode.Right, KeyModifiers.Shift);
        session.PumpFrame();

        var selectionChanged = new UiaRaised(UiaRaisedKind.Event, UiaInterop.UIA_Text_TextSelectionChangedEventId, editor.Id);
        Assert.Equal([selectionChanged, selectionChanged], raised);
        Assert.Equal(trees, session.Window.AccessibilityTreesBuilt);
        // A client's thread reads the moved caret and selection.
        var text = Task.Run(() => bridge.GetText(editor.Id)).Result!;
        Assert.Equal((2, 1, 2), (text.Caret, text.SelectionStart, text.SelectionEnd));
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

    private static AtSpiAccessible ObjectOf(LinuxAccessibilityBridge bridge, Element element) =>
        ((IAtSpiTree)bridge).Find(AtSpiConstants.AccessiblePathPrefix + element.Id.Value)
        ?? throw new InvalidOperationException($"no object for {element.Id.Value}");

    [Fact]
    public void ATSPI_ACaretMove_SendsCaretMoved_FromThePublishedSnapshot_WithNoTreeBuilt()
    {
        using var session = HeadlessSession.Create(600, 400);
        var signals = new Signals();
        using var bridge = new LinuxAccessibilityBridge(signals);
        session.Window.InstallAccessibilityBridge(new Active(bridge));
        var (editor, _) = Mount(session);
        var published = ObjectOf(bridge, editor);
        int trees = session.Window.AccessibilityTreesBuilt;
        signals.Lines.Clear();

        session.Window.PressKey(KeyCode.Right);
        session.PumpFrame();
        session.Window.PressKey(KeyCode.Right, KeyModifiers.Shift);
        session.PumpFrame();
        session.Window.PressKey(KeyCode.Left);
        session.PumpFrame();

        ulong id = editor.Id.Value;
        Assert.Equal(
        [
            $"text-caret-moved {id} 1",
            $"text-caret-moved {id} 2",
            $"text-selection-changed {id}",
            $"text-caret-moved {id} 1",
            $"text-selection-changed {id}",
        ], signals.Lines);
        Assert.Same(published, ObjectOf(bridge, editor));
        Assert.Equal(trees, session.Window.AccessibilityTreesBuilt);
    }

    [Fact]
    public void ATSPI_ACaretMoveInALongDocument_DoesNotMakeItsText()
    {
        // The caret's offset is in the text's version: the bridge made the whole document's text,
        // its lines and its boxes to read it (475 KB a key at 5,000 blocks).
        var blocks = Enumerable.Range(0, 5_000).Select(i => P($"paragraph {i} of a long document, with a few words in it")).ToList();
        using var session = HeadlessSession.Create(600, 400);
        var signals = new Signals();
        using var bridge = new LinuxAccessibilityBridge(signals);
        session.Window.InstallAccessibilityBridge(new Active(bridge));
        var (editor, _) = Mount(session, new RichDocument(blocks));
        editor.State.Select(RichSelection.Caret(2_500, 3));
        session.PumpFrame();
        int step = 0;
        void Move()
        {
            session.Window.PressKey(step++ % 2 == 0 ? KeyCode.Right : KeyCode.Left);
            session.PumpFrame();
        }
        for (int i = 0; i < 6; i++) Move();
        signals.Lines.Clear();

        long textBytes = editor.Document.Length * 2L;
        long bytes = Allocations.Least(Move, goal: textBytes / 16);

        Assert.True(bytes < textBytes / 8, $"a caret move allocated {bytes:N0} B; the text is {textBytes:N0} B");
        Assert.Contains(signals.Lines, l => l.StartsWith("text-caret-moved", StringComparison.Ordinal));
    }
}
