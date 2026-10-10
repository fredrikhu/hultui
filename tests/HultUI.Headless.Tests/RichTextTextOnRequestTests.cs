using HultUI.Core.Accessibility;
using HultUI.Core.Builders;
using HultUI.Core.Elements;
using HultUI.Core.Events;
using HultUI.Core.Primitives;
using HultUI.Core.RichText;
using HultUI.Testing;
using Xunit;

namespace HultUI.Headless.Tests;

/// <summary>
/// The rich text editor's text for a screen reader is made when something asks, not with every
/// tree. The tree is built on every change a reader could see, and a long document's text (a
/// 5,000-block document's is 749,017 characters) with the tables of its lines and paragraphs was
/// made on every keystroke while any client listened, read or not. The tree now takes a version,
/// which the differ compares by document and caret offsets; the text is made from it on request:
/// from the layout on the UI thread, from the document on a bridge's thread, and from the layout
/// again for the versions after a reader has read.
/// </summary>
public class RichTextTextOnRequestTests
{
    private static RichBlock P(string text) => RichBlock.Paragraph(text);

    private static (HeadlessSession Session, RichTextEditorElement Editor, HeadlessAccessibilityBridge Bridge) Mount(
        RichDocument doc, float width = 300)
    {
        var session = HeadlessSession.Create(width: 600, height: 400);
        var editor = RichTextEditor.New(doc).Width(width).Height(300);
        session.Window.SetRoot(Box.New().Width(600).Height(400).Padding(new Thickness(20)).Children(editor));
        var bridge = session.Window.InstallAccessibilityBridge();
        session.PumpFrames(2);
        session.Window.Click(editor);
        session.PumpFrame();
        editor.State.Select(RichSelection.Caret(0, 0));
        session.PumpFrame();
        return (session, editor, bridge);
    }

    private static AccessibilityNode EditorNode(HeadlessAccessibilityBridge bridge, Element editor) =>
        Find(bridge.CurrentTree!, editor.Id) ?? throw new InvalidOperationException("no node for the editor");

    private static AccessibilityNode? Find(AccessibilityNode node, ElementId id)
    {
        if (node.ElementId == id) return node;
        foreach (var child in node.Children)
            if (Find(child, id) is { } found) return found;
        return null;
    }

    private static string[] Told(HeadlessAccessibilityBridge bridge, Element editor) =>
        [.. bridge.Diffs.SelectMany(d => d.PropertyChanged).Where(c => c.Node.ElementId == editor.Id).Select(c => c.PropertyName)];

    [Fact]
    public void AKeystroke_NobodyReading_MakesLessThanOneCopyOfTheText()
    {
        var blocks = Enumerable.Range(0, 5_000).Select(i => P($"paragraph {i} of a long document, with a few words in it")).ToList();
        var (session, editor, bridge) = Mount(new RichDocument(blocks));
        using var _ = session;
        editor.State.Select(RichSelection.Caret(2_500, 3));
        session.PumpFrame();
        for (int i = 0; i < 3; i++) { session.Window.TypeText("w"); session.PumpFrame(); }

        long textBytes = editor.Document.Length * 2L;
        long bytes = Allocations.Least(() =>
        {
            session.Window.TypeText("x");
            session.PumpFrame();
        }, goal: textBytes / 4);
        // The tree was rebuilt and told of the keystroke, and the text a reader would read is one
        // ask away; but it was not made: a keystroke allocated a copy of the text, its tables and
        // a second copy for the value (~1.6 MB here).
        Assert.True(bytes < textBytes / 2, $"a keystroke allocated {bytes:N0} B; the text is {textBytes:N0} B");
        Assert.Contains(AccessibilityProperties.Text, Told(bridge, editor));
        var node = EditorNode(bridge, editor);
        Assert.Equal(editor.Document.ToPlainText(), node.Value);
        Assert.Equal(editor.Document.ToPlainText(), node.Text!.Text);
    }

    [Fact]
    public void TheDiffer_TellsTextAndCaret_NotAFormattingChange_AndACaretMovedByAnEditAbove()
    {
        AccessibilityClients.Force = true;
        try
        {
            var (session, editor, bridge) = Mount(new RichDocument([P("hello world"), P("second line")]));
            using var _ = session;
            bridge.ClearDiffs();

            session.Window.TypeText("x");
            session.PumpFrame();
            Assert.Equal([AccessibilityProperties.Text, AccessibilityProperties.TextSelection], Told(bridge, editor));

            // Bold over a word: a new document with the same text, the selection where it was.
            editor.State.Select(new RichPosition(0, 1), new RichPosition(0, 6));
            session.PumpFrame();
            bridge.ClearDiffs();
            var before = editor.Document;
            session.Window.PressKey(KeyCode.B, KeyModifiers.Ctrl);
            session.PumpFrame();
            Assert.NotSame(before, editor.Document);
            Assert.Empty(Told(bridge, editor));

            // An edit above the caret: the caret's place is the same, its offset in the text is not.
            editor.State.Select(RichSelection.Caret(1, 2));
            session.PumpFrame();
            bridge.ClearDiffs();
            editor.State.Select(RichSelection.Caret(0, 0));
            session.Window.TypeText("Q");
            editor.State.Select(RichSelection.Caret(1, 2));
            session.PumpFrame();
            Assert.Equal([AccessibilityProperties.Text, AccessibilityProperties.TextSelection], Told(bridge, editor));
            Assert.Equal("Qxhello world\n".Length + 2, EditorNode(bridge, editor).Text!.Caret);
        }
        finally { AccessibilityClients.Force = null; }
    }

    [Fact]
    public void ABridgeThread_GetsTheTextFromTheDocument_ThenFromTheLayout_AsAReaderKeepsReading()
    {
        const string longText = "one two three four five six seven eight nine ten eleven twelve thirteen";
        var (session, editor, bridge) = Mount(new RichDocument([P(longText), P("short")]), width: 160);
        using var _ = session;
        var source = editor.AccessibleTextSource!;

        // Nobody had read: a bridge's thread gets the text from the document alone, its paragraphs
        // as its lines and no boxes.
        var first = Task.Run(() => source.AccessibleText).Result!;
        Assert.Equal(longText + "\nshort", first.Text);
        Assert.Equal(0, first.Caret);
        Assert.Null(first.Geometry);
        Assert.Equal(2, first.LineCount);

        // The UI thread is asked to make it from the layout, and does at its next turn: the lines
        // as drawn, which the UI thread's own read agrees with.
        session.PumpFrame();
        var second = Task.Run(() => source.AccessibleText).Result!;
        Assert.NotNull(second.Geometry);
        Assert.True(second.LineCount > second.ParagraphCount, $"{second.LineCount} lines");
        var fromLayout = source.AccessibleText!;
        Assert.Equal(fromLayout.LineCount, second.LineCount);
        Assert.Equal(fromLayout.Text, second.Text);

        // A reader is reading: the next version is made from the layout as the tree takes it.
        session.Window.TypeText("z");
        session.PumpFrame();
        var third = Task.Run(() => source.AccessibleText).Result!;
        Assert.StartsWith("z", third.Text);
        Assert.NotNull(third.Geometry);
        Assert.True(third.LineCount > third.ParagraphCount);
        Assert.Same(third, EditorNode(bridge, editor).Text);
    }
}
