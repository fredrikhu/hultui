using HultUI.Core.Builders;
using HultUI.Core.Elements;
using HultUI.Core.Events;
using HultUI.Core.Primitives;
using HultUI.Core.Rendering;
using HultUI.Core.RichText;
using Xunit;

namespace HultUI.Headless.Tests;

/// <summary>
/// The rich text editor in a window (docs/plans/16-rich-text-editor.md, stage 3): the keyboard,
/// the pointer and the clipboard reaching the editing engine, and the document coming back out.
/// The engine's own rules are <c>RichEditStateTests</c>; these check the wiring and the geometry.
/// </summary>
public class RichTextEditorTests
{
    private static RichBlock P(string text, params RichSpan[] spans) => RichBlock.Paragraph(text, spans);
    private static readonly RichMarks Bold = new(InlineStyle.Bold);

    private sealed class Rig : IDisposable
    {
        public required HeadlessSession Session;
        public required RichTextEditorElement Editor;
        public readonly List<RichDocument> Changes = [];
        public HeadlessWindow Window => Session.Window;
        public RichEditState State => Editor.State;
        public void Dispose() => Session.Dispose();

        public Rect Content => Editor.Layout.AbsoluteContentBox;

        /// <summary>The window point of the caret before character <paramref name="offset"/> of
        /// <paramref name="block"/>, half way down its line.</summary>
        public Point PointAt(int offset, int block = 0)
        {
            var caret = Editor.CaretBounds(new RichPosition(block, offset));
            Assert.True(caret.Height > 0, "the editor has not been drawn");
            return new Point(caret.X, caret.Y + caret.Height / 2);
        }
    }

    private static Rig Build(RichDocument? doc = null, float width = 400, float height = 240, bool focus = true,
        Action<RichTextEditorElement>? configure = null)
    {
        var session = HeadlessSession.Create(width: 600, height: 400);
        var editor = RichTextEditor.New(doc ?? RichDocument.Empty).Width(width).Height(height);
        configure?.Invoke(editor);
        var rig = new Rig { Session = session, Editor = editor };
        editor.DocumentChanged = rig.Changes.Add;
        // Something else to take focus, so Tab out of the editor has somewhere to go.
        var next = Button.New("Next");
        session.Window.SetRoot(Box.New().Width(600).Height(400).Children(editor, next));
        session.PumpFrame();
        if (focus)
        {
            session.Window.Click(editor);
            session.PumpFrame();
        }
        return rig;
    }

    private static string Show(RichEditState s) => string.Join(" | ", s.Document.Blocks);

    // ── Typing ──

    [Fact]
    public void Typing_EditsTheDocument_AndReportsEachNewOne()
    {
        using var rig = Build();
        rig.Window.TypeText("Hello");
        rig.Session.PumpFrame();

        Assert.Equal("Hello", rig.Editor.Document.ToPlainText());
        Assert.Equal(5, rig.Changes.Count);
        Assert.Same(rig.Editor.Document, rig.Changes[^1]);
    }

    [Fact]
    public void Enter_CtrlB_AndShortcuts_ComeFromTheKeyboard()
    {
        using var rig = Build();
        rig.Window.TypeText("# Title");
        rig.Window.PressKey(KeyCode.Enter);
        rig.Window.PressKey(KeyCode.B, KeyModifiers.Ctrl);
        rig.Window.TypeText("bold");
        rig.Window.PressKey(KeyCode.B, KeyModifiers.Ctrl);
        rig.Window.TypeText(" plain");
        rig.Session.PumpFrame();

        Assert.Equal("h1 \"Title\" | Paragraph \"bold plain\" {0+4:Bold}", Show(rig.State));
    }

    [Fact]
    public void CtrlZ_UndoesAndCtrlY_Redoes()
    {
        using var rig = Build();
        rig.Window.TypeText("one two");
        rig.Window.PressKey(KeyCode.Z, KeyModifiers.Ctrl);
        Assert.Equal("one ", rig.Editor.Document.ToPlainText());
        rig.Window.PressKey(KeyCode.Y, KeyModifiers.Ctrl);
        Assert.Equal("one two", rig.Editor.Document.ToPlainText());
    }

    [Fact]
    public void CtrlEnter_IsLeftToTheForm()
    {
        int submitted = 0;
        using var rig = Build(configure: e => e.OnKeyDown(k =>
        {
            if (k.Key == KeyCode.Enter && k.Modifiers.HasFlag(KeyModifiers.Ctrl)) submitted++;
        }));
        rig.Window.TypeText("text");
        rig.Window.PressKey(KeyCode.Enter, KeyModifiers.Ctrl);
        Assert.Equal(1, rig.Editor.Document.BlockCount);   // no new paragraph
        Assert.Equal(1, submitted);                       // and the key went on to the form
    }

    [Fact]
    public void AReadOnlyEditor_TakesNoTyping()
    {
        using var rig = Build(new RichDocument([P("fixed")]), configure: e => e.ReadOnly = true);
        rig.Window.TypeText("x");
        rig.Window.PressKey(KeyCode.Backspace);
        Assert.Equal("fixed", rig.Editor.Document.ToPlainText());
        Assert.Empty(rig.Changes);
    }

    // ── The pointer ──

    [Fact]
    public void AClick_PutsTheCaretBeforeTheCharacterClicked()
    {
        using var rig = Build(new RichDocument([P("hello world")]));
        var at = rig.PointAt(6);
        rig.Window.Click(at.X + 1, at.Y);
        Assert.Equal(RichSelection.Caret(0, 6), rig.State.Selection);
    }

    [Fact]
    public void AClick_AfterABoldWord_LandsWhereTheBoldWordEnds()
    {
        // The paragraph is laid out with its spans, so a click maps through the bold run's own
        // widths: measuring the plain text instead would land a few characters further on.
        using var rig = Build(new RichDocument([P("WWWWWWWWWW plain text", new RichSpan(0, 10, Bold))]));
        var style = rig.Editor.ResolvedInherited;
        float x = rig.Content.X + TextMeasure.MeasureWidth("WWWWWWWWWW", style with { FontWeight = Core.Styling.FontWeight.Bold })
            + TextMeasure.MeasureWidth(" pl", style);
        rig.Window.Click(x + 1, rig.Content.Y + 4);
        Assert.Equal(RichSelection.Caret(0, 13), rig.State.Selection);
    }

    [Fact]
    public void AClickBelowTheText_PutsTheCaretInTheLastBlock()
    {
        using var rig = Build(new RichDocument([P("first"), P("second")]));
        rig.Window.Click(rig.Content.X + 2, rig.Content.Bottom - 4);
        Assert.Equal(1, rig.State.Selection.Focus.Block);
    }

    [Fact]
    public void ADrag_SelectsAcrossBlocks()
    {
        using var rig = Build(new RichDocument([P("alpha"), P("beta")]));
        var from = rig.PointAt(2, 0);
        var to = rig.PointAt(3, 1);
        rig.Window.Drag(new Point(from.X + 1, from.Y), new Point(to.X + 1, to.Y));

        Assert.Equal(new RichSelection(new(0, 2), new(1, 3)), rig.State.Selection);
        Assert.Equal("pha\nbet", rig.State.SelectedText);
    }

    [Fact]
    public void ADoubleClick_SelectsAWord()
    {
        using var rig = Build(new RichDocument([P("one two three")]));
        var at = rig.PointAt(5);
        rig.Window.DoubleClick(at.X + 1, at.Y);
        Assert.Equal("two", rig.State.SelectedText);
    }

    [Fact]
    public void ClickingATasksBox_TicksIt()
    {
        using var rig = Build(new RichDocument([RichBlock.Of(BlockKind.TaskItem, "do it")]));
        var theme = Core.Styling.Theme.Light.RichText;
        // The box sits in the marker column, centred on the first line.
        rig.Window.Click(rig.Content.X + theme.IndentWidth / 2 - 2, rig.Content.Y + rig.Editor.ResolvedInherited.FontSize * 0.6f);
        Assert.True(rig.Editor.Document[0].Checked);
    }

    // ── Moving the caret ──

    [Fact]
    public void UpAndDown_MoveBetweenBlocks_HoldingTheColumn()
    {
        using var rig = Build(new RichDocument([P("abcdef"), P("abcdef")]));
        rig.State.Select(RichSelection.Caret(0, 3));
        rig.Window.PressKey(KeyCode.Down);
        Assert.Equal(RichSelection.Caret(1, 3), rig.State.Selection);
        rig.Window.PressKey(KeyCode.Up);
        Assert.Equal(RichSelection.Caret(0, 3), rig.State.Selection);
    }

    [Fact]
    public void ShiftArrows_ExtendTheSelection_AndArrowsCollapseIt()
    {
        using var rig = Build(new RichDocument([P("abcdef")]));
        rig.State.Select(RichSelection.Caret(0, 1));
        rig.Window.PressKey(KeyCode.Right, KeyModifiers.Shift);
        rig.Window.PressKey(KeyCode.Right, KeyModifiers.Shift);
        Assert.Equal("bc", rig.State.SelectedText);
        rig.Window.PressKey(KeyCode.Left);
        Assert.Equal(RichSelection.Caret(0, 1), rig.State.Selection);
    }

    [Fact]
    public void EndOnAWrappedLine_StaysOnThatLine()
    {
        const string text = "the quick brown fox jumps over the lazy dog and keeps on running far away";
        using var rig = Build(new RichDocument([P(text)]), width: 160);
        rig.State.Select(RichSelection.Caret(0, 0));
        rig.Window.PressKey(KeyCode.End);
        int offset = rig.State.Selection.Focus.Offset;
        Assert.InRange(offset, 1, text.Length - 1);   // the end of the first line, not of the text
        rig.Window.PressKey(KeyCode.Home);
        Assert.Equal(0, rig.State.Selection.Focus.Offset);
    }

    [Fact]
    public void CtrlEnd_ScrollsALongDocumentToItsEnd()
    {
        var doc = new RichDocument(Enumerable.Range(0, 60).Select(i => P($"line {i}")));
        using var rig = Build(doc, height: 120);
        rig.Window.PressKey(KeyCode.End, KeyModifiers.Ctrl);
        rig.Session.PumpFrame();
        Assert.Equal(new RichPosition(59, 7), rig.State.Selection.Focus);
        Assert.True(rig.Editor.ScrollOffsetY > 100, $"scrolled to {rig.Editor.ScrollOffsetY}");
    }

    // ── Tab ──

    [Fact]
    public void Tab_NestsAListItem_AndElsewhereMovesFocusOn()
    {
        var doc = new RichDocument([RichBlock.Of(BlockKind.BulletItem, "a"), RichBlock.Of(BlockKind.BulletItem, "b")]);
        using var rig = Build(doc);
        rig.State.Select(RichSelection.Caret(1, 1));
        rig.Window.PressKey(KeyCode.Tab);
        Assert.Equal(1, rig.Editor.Document[1].Indent);
        Assert.True(rig.Editor.IsFocused);

        rig.State.Select(RichSelection.Caret(0, 0));
        rig.State.SetBlockKind(BlockKind.Paragraph);
        rig.Window.PressKey(KeyCode.Tab);
        Assert.False(rig.Editor.IsFocused);
    }

    // ── The clipboard ──

    [Fact]
    public void CopyAndPaste_BetweenEditors_KeepsTheMarks()
    {
        using var rig = Build(new RichDocument([P("bold plain", new RichSpan(0, 4, Bold))]));
        rig.State.Select(new RichPosition(0, 0), new RichPosition(0, 4));
        rig.Window.PressKey(KeyCode.C, KeyModifiers.Ctrl);
        Assert.True(rig.Window.Clipboard.HasFormat(RichClipboard.MimeType));
        Assert.Equal("bold", rig.Window.Clipboard.GetText());

        rig.State.Select(RichSelection.Caret(0, 10));
        rig.Window.PressKey(KeyCode.V, KeyModifiers.Ctrl);
        Assert.Equal("Paragraph \"bold plainbold\" {0+4:Bold 10+4:Bold}", Show(rig.State));
    }

    [Fact]
    public void PastingPlainText_MakesAParagraphALine()
    {
        using var rig = Build();
        rig.Window.Clipboard.SetText("one\ntwo");
        rig.Window.PressKey(KeyCode.V, KeyModifiers.Ctrl);
        Assert.Equal("Paragraph \"one\" | Paragraph \"two\"", Show(rig.State));
    }

    [Fact]
    public void Copy_OffersHtml()
    {
        using var rig = Build(new RichDocument([RichBlock.Heading(2, "T"), P("x", new RichSpan(0, 1, Bold))]));
        rig.State.SelectAll();
        rig.Window.PressKey(KeyCode.C, KeyModifiers.Ctrl);
        var html = System.Text.Encoding.UTF8.GetString(rig.Window.Clipboard.GetData("text/html")!);
        Assert.Equal("<h2>T</h2><p><strong>x</strong></p>", html);
    }

    // ── Links ──

    [Fact]
    public void Links_OpenOnAPlainClickInAViewer_AndOnCtrlClickInAnEditor()
    {
        var doc = new RichDocument([P("see docs here", new RichSpan(4, 4, new RichMarks(Link: "u")))]);
        var opened = new List<string>();

        using (var rig = Build(doc, configure: e => { e.ReadOnly = true; e.LinkActivated = opened.Add; }))
        {
            var at = rig.PointAt(5);
            rig.Window.Click(at.X + 1, at.Y);
        }
        Assert.Equal(["u"], opened);

        using (var rig = Build(doc, configure: e => e.LinkActivated = opened.Add))
        {
            var at = rig.PointAt(5);
            rig.Window.Click(at.X + 1, at.Y);
            Assert.Single(opened);                                  // a plain click edits
            var away = rig.PointAt(0);
            rig.Window.Click(away.X, away.Y);                       // so the next is not a double-click
            rig.Window.Click(at.X + 1, at.Y, modifiers: KeyModifiers.Ctrl);
        }
        Assert.Equal(["u", "u"], opened);
    }

    // ── The value ──

    [Fact]
    public void ARebuild_HandingBackTheDocument_KeepsTheHistory()
    {
        using var rig = Build();
        rig.Window.TypeText("abc");
        var current = rig.Editor.Document;

        var rebuilt = RichTextEditor.New(current);
        Patch(rig.Editor, rebuilt);
        Assert.Same(current, rig.Editor.Document);
        Assert.True(rig.State.CanUndo);

        var other = new RichDocument([P("replaced")]);
        Patch(rig.Editor, RichTextEditor.New(other));
        Assert.Same(other, rig.Editor.Document);
        Assert.False(rig.State.CanUndo);
    }

    [Fact]
    public void AnUncontrolledEditor_KeepsItsEdits_AcrossRebuilds()
    {
        var initial = new RichDocument([P("start")]);
        using var rig = Build(initial);
        rig.State.Select(RichSelection.Caret(0, 5));
        rig.Window.TypeText("ed");
        Patch(rig.Editor, RichTextEditor.New(initial));   // the app never took the new document
        Assert.Equal("started", rig.Editor.Document.ToPlainText());
    }

    private static void Patch(Element target, Element source) =>
        typeof(Element).GetMethod("PatchFrom", System.Reflection.BindingFlags.NonPublic
            | System.Reflection.BindingFlags.Instance)!.Invoke(target, [source]);

    // ── Height ──

    [Fact]
    public void AutoHeight_GrowsWithTheDocument_UpToItsMaximum()
    {
        var session = HeadlessSession.Create(width: 600, height: 600);
        using var _ = session;
        var editor = RichTextEditor.New().Width(300).AutoHeight(20, 160);
        session.Window.SetRoot(Box.New().Width(600).Height(600).Children(editor));
        session.PumpFrame();
        float empty = editor.Layout.AbsoluteBorderBox.Height;

        session.Window.Click(editor);
        session.Window.TypeText("one");
        session.Window.PressKey(KeyCode.Enter);
        session.Window.TypeText("two");
        session.PumpFrame();
        float two = editor.Layout.AbsoluteBorderBox.Height;
        Assert.True(two > empty + 10, $"{two} should exceed {empty}");

        for (int i = 0; i < 20; i++) { session.Window.PressKey(KeyCode.Enter); session.Window.TypeText("more"); }
        session.PumpFrame();
        Assert.Equal(160f, editor.Layout.AbsoluteBorderBox.Height, 1);
    }
}
