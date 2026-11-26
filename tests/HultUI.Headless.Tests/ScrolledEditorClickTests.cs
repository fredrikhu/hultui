using System.Reflection;
using HultUI.Core.Builders;
using HultUI.Core.Elements;
using HultUI.Core.Primitives;
using HultUI.Core.Rendering;
using HultUI.Core.RichText;
using Xunit;

namespace HultUI.Headless.Tests;

/// <summary>
/// A click in an editor on a page that has scrolled: a settings form, a dialog's long body.
/// Layout is unscrolled and the page's scroll is a translate at paint, so the pointer has to be
/// taken back through every scroll view above the editor before it is a place in its text. An
/// editor that read the window's point as its layout's put the caret as far below the click as
/// the page had scrolled (or, sideways, as far to the right), and a scrollbar inside the page
/// (an editor's own, a nested scroll view's) took its press as that far off: the top of the bar
/// missed. <see cref="ScrolledLabelSelectionTests"/> is the same for a selectable label.
/// </summary>
public class ScrolledEditorClickTests
{
    // A page of 200 that scrolls by 60: a spacer, the editor, then room to scroll.
    private static void Page(HeadlessSession session, Element editor, float scroll = 60)
    {
        var page = ScrollView.Vertical().Width(300).Height(200).Children(
            Box.New().Height(150).FlexShrink(0),
            editor,
            Box.New().Height(300).FlexShrink(0));
        session.Window.SetRoot(page);
        session.PumpFrame();
        page.ScrollOffsetY = scroll;
        session.PumpFrame();
    }

    private static TextAreaElement Area(int lines) =>
        TextArea.New(string.Join("\n", Enumerable.Range(0, lines).Select(i => $"line {i}")))
            .Width(280).Height(120).FlexShrink(0);

    private static IRenderContext RenderContextOf(Element editor) =>
        (IRenderContext)editor.GetType()
            .GetField("_renderCtx", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(editor)!;

    // One line's height as the area lays its lines out (TextAreaElement.GetLineHeight).
    private static float LineHeight(TextAreaElement area) =>
        RenderContextOf(area).MeasureText("X", area.ResolvedInherited).Height;

    // Where "line N" starts in the area's text.
    private static int LineStart(int line) => Enumerable.Range(0, line).Sum(i => $"line {i}\n".Length);

    // ── TextArea ──

    [Fact]
    public void AClickInATextAreaOnAScrolledPage_PutsTheCaretOnTheLineUnderThePointer()
    {
        using var session = HeadlessSession.Create(width: 300, height: 200);
        var area = Area(lines: 5);
        Page(session, area);

        float lh = LineHeight(area);
        var drawn = area.ScreenContentBox;
        Assert.True(drawn.Y < area.Layout.AbsoluteContentBox.Y, "the page should have scrolled the area up");
        session.Window.Click(drawn.X + 1, drawn.Y + lh * 1.5f);   // the start of the second line on screen

        Assert.Equal(LineStart(1), area.CursorPosition);
    }

    [Fact]
    public void ADragInATextAreaOnAScrolledPage_SelectsTheLinesUnderThePointer()
    {
        using var session = HeadlessSession.Create(width: 300, height: 200);
        var area = Area(lines: 5);
        Page(session, area);

        float lh = LineHeight(area);
        var drawn = area.ScreenContentBox;
        // From the start of line 1 to past the end of line 2.
        session.Window.Drag(new Point(drawn.X + 1, drawn.Y + lh * 1.5f), new Point(drawn.X + 200, drawn.Y + lh * 2.5f));

        Assert.Equal("line 1\nline 2", area.SelectedText);
    }

    // ── A scrollbar inside the page: the area's own, a nested scroll view's ──

    // A press 40 into the bar's track as drawn, below the thumb at its top: the track jumps to
    // centre the thumb there. Where it lands is the same however far the page has scrolled.
    private static (float Scrolled, bool CaretMoved) PressTheTrack<T>(Func<T> make, float pageScroll, Func<T, float> offset)
        where T : Element
    {
        using var session = HeadlessSession.Create(width: 300, height: 200);
        var scroller = make();
        Page(session, scroller, pageScroll);
        int caret = (scroller as TextAreaElement)?.CursorPosition ?? -1;
        var drawn = scroller.ScreenContentBox;
        session.Window.Click(drawn.Right - 2, drawn.Y + 40);
        session.PumpFrame();
        return (offset(scroller), ((scroller as TextAreaElement)?.CursorPosition ?? -1) != caret);
    }

    [Fact]
    public void APressOnATextAreasScrollbar_OnAScrolledPage_ScrollsItAsOnAPageThatHasNot()
    {
        var still = PressTheTrack(() => Area(lines: 30), 0, a => a.ScrollOffsetY);
        var scrolled = PressTheTrack(() => Area(lines: 30), 60, a => a.ScrollOffsetY);

        Assert.True(still.Scrolled > 0, "the control: the press should scroll the area");
        Assert.Equal(still.Scrolled, scrolled.Scrolled);
        Assert.False(scrolled.CaretMoved, "the press was the bar's, not the text's");
    }

    [Fact]
    public void APressOnANestedScrollViewsBar_OnAScrolledPage_ScrollsItAsOnAPageThatHasNot()
    {
        static ScrollViewElement Inner() => ScrollView.Vertical().Width(280).Height(120).FlexShrink(0)
            .Children(Box.New().Height(600).FlexShrink(0));
        var still = PressTheTrack(Inner, 0, v => v.ScrollOffsetY);
        var scrolled = PressTheTrack(Inner, 60, v => v.ScrollOffsetY);

        Assert.True(still.Scrolled > 0, "the control: the press should scroll the inner view");
        Assert.Equal(still.Scrolled, scrolled.Scrolled);
    }

    [Fact]
    public void APressOnARichTextEditorsScrollbar_OnAScrolledPage_ScrollsItAsOnAPageThatHasNot()
    {
        static RichTextEditorElement Editor() => RichTextEditor.New(new RichDocument(
                [.. Enumerable.Range(0, 40).Select(i => RichBlock.Paragraph($"block {i}"))]))
            .Width(280).Height(120).FlexShrink(0);
        var still = PressTheTrack(Editor, 0, e => e.ScrollOffsetY);
        var scrolled = PressTheTrack(Editor, 60, e => e.ScrollOffsetY);

        Assert.True(still.Scrolled > 0, "the control: the press should scroll the editor");
        Assert.Equal(still.Scrolled, scrolled.Scrolled);
    }

    // ── TextInput, on a page that scrolls sideways ──

    [Fact]
    public void AClickInATextInputOnAPageScrolledSideways_PutsTheCaretUnderThePointer()
    {
        using var session = HeadlessSession.Create(width: 300, height: 100);
        var input = TextInput.New("abcdefghij").Width(200).FlexShrink(0);
        var page = ScrollView.Horizontal().Width(300).Height(100).Children(
            Box.New().Width(150).FlexShrink(0),
            input,
            Box.New().Width(300).FlexShrink(0));
        session.Window.SetRoot(page);
        session.PumpFrame();
        page.ScrollOffsetX = 100;
        session.PumpFrame();

        // Just before the middle of "f", the sixth letter: the caret goes before it.
        var drawn = input.ScreenContentBox;
        Assert.True(drawn.X < input.Layout.AbsoluteContentBox.X, "the page should have scrolled the field left");
        var ctx = RenderContextOf(input);
        float before = ctx.TextCaretX("abcdefghij", input.ResolvedInherited, 5);
        float after = ctx.TextCaretX("abcdefghij", input.ResolvedInherited, 6);
        session.Window.Click(drawn.X + (before + after) / 2 - 1, drawn.Y + drawn.Height / 2);

        Assert.Equal(5, input.CursorPosition);
    }

    // ── RichTextEditor ──

    [Fact]
    public void AClickInARichTextEditorOnAScrolledPage_PutsTheCaretInTheBlockUnderThePointer()
    {
        using var session = HeadlessSession.Create(width: 300, height: 200);
        var editor = RichTextEditor.New(new RichDocument(
                [.. Enumerable.Range(0, 5).Select(i => RichBlock.Paragraph($"block {i}"))]))
            .Width(280).Height(120).FlexShrink(0);
        Page(session, editor);

        // CaretBounds is where the editor draws, scrolled: block 2, before its fourth letter.
        var caret = editor.CaretBounds(new RichPosition(2, 3));
        Assert.True(caret.Height > 0, "the editor has not been drawn");
        session.Window.Click(caret.X + 1, caret.Y + caret.Height / 2);

        Assert.Equal(RichSelection.Caret(2, 3), editor.State.Selection);
    }

    [Fact]
    public void ADragInARichTextEditorOnAScrolledPage_SelectsUnderThePointer()
    {
        using var session = HeadlessSession.Create(width: 300, height: 200);
        var editor = RichTextEditor.New(new RichDocument(
                [.. Enumerable.Range(0, 5).Select(i => RichBlock.Paragraph($"block {i}"))]))
            .Width(280).Height(120).FlexShrink(0);
        Page(session, editor);

        var from = editor.CaretBounds(new RichPosition(1, 2));
        var to = editor.CaretBounds(new RichPosition(2, 3));
        session.Window.Drag(new Point(from.X + 1, from.Y + from.Height / 2), new Point(to.X + 1, to.Y + to.Height / 2));

        Assert.Equal(new RichSelection(new(1, 2), new(2, 3)), editor.State.Selection);
    }
}
