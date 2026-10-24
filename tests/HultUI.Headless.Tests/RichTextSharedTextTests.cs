// The AT-SPI answers are Linux's, but they are plain functions of the text: they run anywhere.
#pragma warning disable CA1416
using System.Reflection;
using HultUI.Core.Accessibility;
using HultUI.Core.Builders;
using HultUI.Core.Elements;
using HultUI.Core.Events;
using HultUI.Core.Primitives;
using HultUI.Core.RichText;
using HultUI.Platform.Accessibility.Linux;
using HultUI.Testing;
using Xunit;

namespace HultUI.Headless.Tests;

/// <summary>
/// With a reader reading, a caret move in the rich text editor makes the text of the next version
/// from the layout as the version is taken (RichTextTextOnRequestTests): the whole document's text
/// with the tables of its lines and paragraphs and where each line is, 474 KB a move at 5,000
/// blocks, for a caret that moved. When only the caret or the selection moved — the same document,
/// width, layout, scroll, read-only and part on screen — the new text shares the last one's text,
/// tables and geometry and has its own caret and selection (plan 17, round 4). It answers every
/// question a reader asks as a text made afresh does.
/// </summary>
public sealed class RichTextSharedTextTests : IDisposable
{
    public RichTextSharedTextTests() => AccessibilityClients.Force = true;

    public void Dispose() => AccessibilityClients.Force = null;

    private static RichBlock P(string text) => RichBlock.Paragraph(text);

    private const string Long = "one two three four five six seven eight nine ten eleven twelve thirteen fourteen fifteen";

    private static RichDocument Wrapping() => new([
        P(Long), P("short"), P(Long + " and more words at its end"), RichBlock.Heading(2, "a heading"), P(Long),
    ]);

    private static (HeadlessSession Session, RichTextEditorElement Editor, HeadlessAccessibilityBridge Bridge) Mount(
        RichDocument doc, float width = 180, float height = 300)
    {
        var session = HeadlessSession.Create(width: 600, height: 500);
        var editor = RichTextEditor.New(doc).Width(width).Height(height);
        session.Window.SetRoot(Box.New().Width(600).Height(500).Padding(new Thickness(20)).Children(editor));
        var bridge = session.Window.InstallAccessibilityBridge();
        session.PumpFrames(2);
        session.Window.Click(editor);
        session.PumpFrame();
        editor.State.Select(RichSelection.Caret(0, 0));
        session.PumpFrame();
        return (session, editor, bridge);
    }

    /// <summary>What a reader on its own thread reads: the text of the version the last tree has.</summary>
    private static AccessibleText Read(Element editor) =>
        Task.Run(() => editor.AccessibleTextSource!.AccessibleText!).Result;

    /// <summary>The text made afresh from the editor's layout as it is now.</summary>
    private static AccessibleText Fresh(RichTextEditorElement editor) =>
        (AccessibleText)typeof(RichTextEditorElement)
            .GetMethod("BuildAccessibleText", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(editor, [editor.Document, true])!;

    /// <summary>A reader reads, the key moves the caret, the frame tells it, and the reader reads
    /// again: the text of the version the move made.</summary>
    private static AccessibleText MoveAndRead(HeadlessSession session, Element editor, KeyCode key,
        KeyModifiers modifiers = KeyModifiers.None)
    {
        // Twice: the UI thread makes the version just read from the layout, or rebuilds for a
        // change the frame before left untold (a width change, which the render finds); the
        // second read is of what that made, so the reader has read the version the key replaces.
        for (int i = 0; i < 2; i++)
        {
            Read(editor);
            session.PumpFrame();
        }
        session.Window.PressKey(key, modifiers);
        session.PumpFrame();
        return Read(editor);
    }

    [Fact]
    public void ACaretMove_WithAReaderReading_SharesTheTables_AndAnswersAsATextMadeAfresh()
    {
        var (session, editor, _) = Mount(Wrapping());
        using var _ = session;
        var first = MoveAndRead(session, editor, KeyCode.Right);
        Assert.NotNull(first.Geometry);
        Assert.True(first.LineCount > first.ParagraphCount, $"{first.LineCount} lines");

        var steps = new (KeyCode Key, KeyModifiers Modifiers)[]
        {
            (KeyCode.Right, KeyModifiers.None), (KeyCode.Right, KeyModifiers.Shift), (KeyCode.Right, KeyModifiers.Shift),
            (KeyCode.Down, KeyModifiers.None), (KeyCode.End, KeyModifiers.None), (KeyCode.Left, KeyModifiers.Ctrl),
            (KeyCode.Down, KeyModifiers.Shift), (KeyCode.Home, KeyModifiers.None),
        };
        var previous = first;
        foreach (var (key, modifiers) in steps)
        {
            var text = MoveAndRead(session, editor, key, modifiers);
            Assert.NotSame(previous, text);
            Assert.Same(first.Geometry, text.Geometry);
            Assert.Same(first.Text, text.Text);
            AssertSameAnswers(Fresh(editor), text);
            previous = text;
        }
    }

    [Fact]
    public void TheTablesAreMadeAgain_AfterAnEdit_AWidthChange_AReadOnlySwitch_AHeightChange_AndAScroll()
    {
        var (session, editor, _) = Mount(Wrapping(), height: 90);
        using var _ = session;
        var shared = MoveAndRead(session, editor, KeyCode.Right);
        var text = MoveAndRead(session, editor, KeyCode.Right);
        Assert.Same(shared.Geometry, text.Geometry);

        // A text made afresh now answers as the one read, but after a scroll: the key's frame then
        // scrolls the caret back into view after the text was made, so the two show different parts.
        void NotShared(string what, Action change, bool compare = true)
        {
            Read(editor);
            session.PumpFrame();
            change();
            session.PumpFrame();
            var after = MoveAndRead(session, editor, KeyCode.Right);
            Assert.True(!ReferenceEquals(text.Geometry, after.Geometry), $"{what}: the tables were shared");
            if (compare) AssertSameAnswers(Fresh(editor), after);
            text = after;
        }

        NotShared("an edit", () => session.Window.TypeText("x"));
        NotShared("a width change", () => { editor.Width(240); editor.MarkLayoutDirty(); });
        NotShared("a read-only switch", () => editor.ReadOnly = true);
        // Fewer blocks on screen, the rest the same: the part on screen and the boxes measured differ.
        int visibleEnd = text.VisibleEnd;
        NotShared("a height change", () => { editor.Height(40); editor.MarkLayoutDirty(); });
        Assert.NotEqual(visibleEnd, text.VisibleEnd);
        NotShared("a scroll", () => editor.ScrollOffsetY = 60, compare: false);
        // And back to sharing once only the caret moves again (the move after the scroll brought
        // the caret back into view, which is a scroll of its own).
        var settled = MoveAndRead(session, editor, KeyCode.Left);
        var again = MoveAndRead(session, editor, KeyCode.Right);
        Assert.Same(settled.Geometry, again.Geometry);
        AssertSameAnswers(Fresh(editor), again);
    }

    [Fact]
    public void ACaretMoveInALongDocument_WithAReaderReading_DoesNotMakeTheTextAgain()
    {
        var blocks = Enumerable.Range(0, 5_000).Select(i => P($"paragraph {i} of a long document, with a few words in it")).ToList();
        var (session, editor, _) = Mount(new RichDocument(blocks), width: 300);
        using var _ = session;
        editor.State.Select(RichSelection.Caret(2_500, 3));
        session.PumpFrame();
        int step = 0;
        void Move()
        {
            Read(editor);
            session.PumpFrame();
            session.Window.PressKey(step++ % 2 == 0 ? KeyCode.Right : KeyCode.Left);
            session.PumpFrame();
        }
        for (int i = 0; i < 4; i++) Move();

        long textBytes = editor.Document.Length * 2L;
        long bytes = Allocations.Least(Move, goal: textBytes / 16);
        // The reader's reads allocate on its own thread; this counts the UI thread's.
        Assert.True(bytes < textBytes / 8, $"a caret move allocated {bytes:N0} B; the text is {textBytes:N0} B");
        Assert.Equal(editor.Document.ToOffset(editor.State.Selection.Focus), Read(editor).Caret);
    }

    /// <summary>Every answer UIA's text ranges and AT-SPI's Text interface make from a text.</summary>
    private static void AssertSameAnswers(AccessibleText expected, AccessibleText actual)
    {
        Assert.Equal(expected.Text, actual.Text);
        Assert.Equal((expected.Caret, expected.SelectionStart, expected.SelectionEnd, expected.HasSelection),
            (actual.Caret, actual.SelectionStart, actual.SelectionEnd, actual.HasSelection));
        Assert.Equal((expected.VisibleStart, expected.VisibleEnd, expected.IsReadOnly),
            (actual.VisibleStart, actual.VisibleEnd, actual.IsReadOnly));
        Assert.Equal((expected.LineCount, expected.ParagraphCount), (actual.LineCount, actual.ParagraphCount));
        Assert.Equal(expected.Geometry is null, actual.Geometry is null);
        for (int line = 0; line < expected.LineCount; line++)
        {
            Assert.Equal(expected.LineStart(line), actual.LineStart(line));
            Assert.Equal(expected.LineEnd(line), actual.LineEnd(line));
            Assert.Equal(expected.LineRange(line), actual.LineRange(line));
            Assert.Equal(expected.RangeBounds(expected.LineStart(line), expected.LineEnd(line)),
                actual.RangeBounds(actual.LineStart(line), actual.LineEnd(line)));
        }
        for (int at = 0; at <= expected.Length; at++)
        {
            Assert.Equal(expected.LineAt(at), actual.LineAt(at));
            Assert.Equal(expected.ParagraphRange(at), actual.ParagraphRange(at));
            Assert.Equal(expected.WordRange(at), actual.WordRange(at));
            Assert.Equal(expected.NextWordStart(at), actual.NextWordStart(at));
            Assert.Equal(expected.PreviousWordStart(at), actual.PreviousWordStart(at));
            Assert.Equal(expected.NextCharacter(at), actual.NextCharacter(at));
            for (uint g = 0; g <= 4; g++)
                Assert.Equal(AtSpiText.GetStringAtOffset(expected, at, g), AtSpiText.GetStringAtOffset(actual, at, g));
            if (expected.Geometry is not null)
            {
                var box = expected.Geometry.CharacterBounds(expected, at);
                Assert.Equal(box, actual.Geometry!.CharacterBounds(actual, at));
                var point = new Point(box.X + box.Width / 2, box.Y + box.Height / 2);
                Assert.Equal(expected.OffsetAt(point), actual.OffsetAt(point));
                Assert.Equal(AtSpiText.GetCharacterExtents(expected, at, 1.5f), AtSpiText.GetCharacterExtents(actual, at, 1.5f));
            }
        }
        Assert.Equal(AtSpiText.GetRangeExtents(expected, 0, -1, 1.25f), AtSpiText.GetRangeExtents(actual, 0, -1, 1.25f));
        Assert.Equal(AtSpiText.GetSelection(expected, 0), AtSpiText.GetSelection(actual, 0));
    }
}
