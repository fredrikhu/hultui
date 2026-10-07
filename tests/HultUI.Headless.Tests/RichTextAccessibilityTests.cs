using HultUI.Core.Accessibility;
using HultUI.Core.Builders;
using HultUI.Core.Elements;
using HultUI.Core.Events;
using HultUI.Core.Primitives;
using HultUI.Core.RichText;
using Xunit;

namespace HultUI.Headless.Tests;

/// <summary>
/// The rich text editor for a screen reader (docs/plans/16-rich-text-editor.md, stage 5): the
/// document as text to move through by character, word, line and paragraph, the caret and the
/// selection in it, where each character is, and what changes tell the reader.
/// </summary>
public class RichTextAccessibilityTests
{
    private static RichBlock P(string text) => RichBlock.Paragraph(text);

    private static (HeadlessSession Session, RichTextEditorElement Editor) Mount(RichDocument doc, float width = 300)
    {
        var session = HeadlessSession.Create(width: 600, height: 400);
        var editor = RichTextEditor.New(doc).Width(width).Height(300);
        session.Window.SetRoot(Box.New().Width(600).Height(400).Padding(new Thickness(20)).Children(editor));
        session.PumpFrame();
        return (session, editor);
    }

    private static AccessibilityNode? WithText(AccessibilityNode node) =>
        node.Text is not null ? node : node.Children.Select(WithText).FirstOrDefault(n => n is not null);

    private static AccessibleText TextOf(RichTextEditorElement editor) =>
        editor.AccessibleTextSource!.AccessibleText!;

    [Fact]
    public void TheText_IsTheBlocks_APictureReadsAsItsWords()
    {
        var (session, editor) = Mount(new RichDocument([
            RichBlock.Heading(1, "Title"), RichBlock.Picture(new RichImage("cat.png", "A cat asleep")),
            RichBlock.Picture(new RichImage("x.png")), RichBlock.Cell(0, 0, "a"), RichBlock.Cell(0, 1, "b"), P("end"),
        ]));
        using var _ = session;
        var text = TextOf(editor);
        Assert.Equal("Title\nA cat asleep\nPicture\na\nb\nend", text.Text);
        Assert.False(text.IsReadOnly);

        // The caret after a picture's block maps past its words.
        editor.State.Select(RichSelection.Caret(5, 2));
        Assert.Equal(text.Text.Length - 1, TextOf(editor).Caret);
        editor.State.Select(new RichPosition(0, 1), new RichPosition(1, 0));
        var selected = TextOf(editor);
        Assert.Equal((1, 6), (selected.SelectionStart, selected.SelectionEnd));
    }

    [Fact]
    public void Lines_AreTheLinesDrawn_ParagraphsTheBlocks()
    {
        const string longText = "one two three four five six seven eight nine ten eleven twelve thirteen";
        var (session, editor) = Mount(new RichDocument([P(longText), P("short")]), width: 160);
        using var _ = session;
        var text = TextOf(editor);
        Assert.True(text.LineCount > 3, $"the long paragraph wraps: {text.LineCount} lines");
        Assert.Equal((0, longText.Length + 1), text.ParagraphRange(10));
        Assert.Equal((longText.Length + 1, text.Length), text.ParagraphRange(text.Length - 1));

        // The lines cover the paragraph with no gaps, a wrapped one keeping its trailing space.
        var (s1, e1) = text.LineRange(0);
        var (s2, _) = text.LineRange(1);
        Assert.Equal(0, s1);
        Assert.Equal(e1, s2);
        Assert.Equal(' ', text.Text[e1 - 1]);
    }

    [Fact]
    public void ACharactersBox_IsWhereTheEditorDrawsIt()
    {
        var (session, editor) = Mount(new RichDocument([P("first line"), P("hello world")]));
        using var _ = session;
        var text = TextOf(editor);
        int offset = "first line\n".Length + 6;   // the w of world
        var box = text.Geometry!.CharacterBounds(text, offset);
        var caret = editor.CaretBounds(new RichPosition(1, 6));
        var next = editor.CaretBounds(new RichPosition(1, 7));
        Assert.Equal(caret.X, box.X, 2);
        Assert.Equal(caret.Y, box.Y, 2);
        Assert.Equal(next.X - caret.X, box.Width, 2);

        Assert.Equal(offset, text.OffsetAt(new Point(box.X + 1, box.Y + box.Height / 2)));
        Assert.Equal(-1, text.OffsetAt(new Point(box.X, box.Y + 500)));
    }

    [Fact]
    public void ScrollingMovesTheBoxes_WithoutANewSnapshot()
    {
        var blocks = Enumerable.Range(0, 60).Select(i => P($"line {i}")).ToList();
        var (session, editor) = Mount(new RichDocument(blocks));
        using var _ = session;
        var text = TextOf(editor);
        var before = text.Geometry!.CharacterBounds(text, 0);
        editor.ScrollOffsetY = 100;
        session.PumpFrame();
        var after = text.Geometry!.CharacterBounds(text, 0);
        Assert.Equal(before.Y - 100, after.Y, 1);
    }

    [Fact]
    public void ACaretMove_IsToldToTheReader_AndAFormattingShortcutSaid()
    {
        AccessibilityClients.Force = true;
        try
        {
            var (session, editor) = Mount(new RichDocument([P("hello world")]));
            using var _ = session;
            var bridge = session.Window.InstallAccessibilityBridge();
            session.Window.Click(editor);
            session.PumpFrame();
            bridge.ClearDiffs();

            editor.State.Select(RichSelection.Caret(0, 5));
            session.PumpFrame();
            Assert.Contains(bridge.Diffs, d => d.PropertyChanged.Any(c => c.PropertyName == AccessibilityProperties.TextSelection));
            Assert.Equal(5, WithText(bridge.CurrentTree!)!.Text!.Caret);

            session.Window.PressKey(KeyCode.B, KeyModifiers.Ctrl);
            session.PumpFrame();
            Assert.Contains(bridge.Announcements, a => a.Text == "Bold on");
            session.Window.PressKey(KeyCode.B, KeyModifiers.Ctrl);
            session.PumpFrame();
            Assert.Contains(bridge.Announcements, a => a.Text == "Bold off");

            session.Window.PressKey(KeyCode.Digit2, KeyModifiers.Ctrl | KeyModifiers.Alt);
            session.PumpFrame();
            Assert.Contains(bridge.Announcements, a => a.Text == "Heading 2");
        }
        finally { AccessibilityClients.Force = null; }
    }

    [Fact]
    public void AnAssistiveTool_SettingTheValue_ReplacesTheDocument_AsOneEdit()
    {
        var (session, editor) = Mount(new RichDocument([RichBlock.Heading(1, "Old"), P("text")]));
        using var _ = session;
        var changes = new List<RichDocument>();
        editor.DocumentChanged = changes.Add;
        editor.SetValueFromAssistiveTool("new\ntext");
        Assert.Equal("Paragraph \"new\" | Paragraph \"text\"", string.Join(" | ", editor.Document.Blocks));
        Assert.Single(changes);
        editor.State.Undo();
        Assert.Equal("h1 \"Old\" | Paragraph \"text\"", string.Join(" | ", editor.Document.Blocks));

        editor.ReadOnly = true;
        editor.SetValueFromAssistiveTool("refused");
        Assert.Equal("Old\ntext", editor.Document.ToPlainText());
    }
}
