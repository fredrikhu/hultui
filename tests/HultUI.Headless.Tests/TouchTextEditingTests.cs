using HultUI.Core.Builders;
using HultUI.Core.Elements;
using HultUI.Core.Events;
using HultUI.Core.Primitives;
using Xunit;
using TextB = HultUI.Core.Builders.Text;

namespace HultUI.Headless.Tests;

/// <summary>
/// Touch text editing (docs/design/SOFT-KEYBOARD.md §4): a long press or a double tap selects a
/// word and shows two handles and the edit bar; a handle's drag moves its end; a tap shows the
/// insertion handle for a while; a tap elsewhere, typing or the mouse put them away; a pan still
/// scrolls and keeps the selection. For the text input, the text area and the rich text editor.
/// </summary>
public sealed class TouchTextEditingTests
{
    private const string Words = "alpha bravo charlie";   // bravo is 6..11, charlie 12..19

    private static (HeadlessSession Session, TextInputElement Field, ButtonElement Other) Started()
    {
        var session = HeadlessSession.Create(width: 400, height: 300);
        var field = TextInput.New(Words).Width(320);
        var other = Button.New("Elsewhere");
        // The button well below the field: the edit bar opens under a field this near the top,
        // and a tap on the button must not land on it.
        session.Window.SetRoot(Flex.Column(gap: 120).Padding(20).Children(field, other));
        session.PumpFrame();
        return (session, field, other);
    }

    private static Window W(HeadlessSession s) => s.Window.Window;

    /// <summary>Where character <paramref name="index"/> of the field's text is, in the window: the
    /// middle of the caret before it, as the field lays it out. Read with the field's selection
    /// set and then put back.</summary>
    private static Point AtIndex(HeadlessSession session, Element field, int index)
    {
        var text = (ITouchTextEditing)field;
        switch (field)
        {
            case TextInputElement input:
            {
                // TextInput has no selection setter: the keyboard moves its caret.
                input.Focus();
                session.Window.PressKey(KeyCode.Home);
                for (int i = 0; i < index; i++) session.Window.PressKey(KeyCode.Right);
                break;
            }
            case TextAreaElement area: area.Select(index, index); break;
        }
        session.PumpFrame();
        var (_, caret) = text.TouchCaretBoxes()!.Value;
        var box = field.WindowBorderBox;
        var laid = field.Layout.AbsoluteBorderBox;
        return new Point(caret.X + box.X - laid.X, caret.Y + caret.Height / 2 + box.Y - laid.Y);
    }

    private static void LongPressAt(HeadlessSession session, Point at)
    {
        session.Window.TouchDown(1, at.X, at.Y);
        session.PumpFrame(TimeSpan.FromSeconds(HultUI.Window.LongPressSeconds + 0.1));
        session.Window.TouchUp(1);
        session.PumpFrame();
    }

    private static Point Centre(Rect r) => new(r.X + r.Width / 2, r.Y + r.Height / 2);

    private static List<string> BarLabels(HeadlessSession session) =>
        W(session).TouchEditBar is { } bar
            ? bar.Children.Select(b => b.Children.OfType<TextElement>().Single().Content).ToList()
            : [];

    private static void TapBarItem(HeadlessSession session, string label)
    {
        var bar = W(session).TouchEditBar ?? throw new InvalidOperationException("no edit bar");
        var item = bar.Children.Single(b => b.Children.OfType<TextElement>().Single().Content == label);
        var c = Centre(item.WindowBorderBox);
        session.Window.Tap(c.X, c.Y);
        session.PumpFrame();
    }

    /// <summary>A long press on "bravo", with the frame after it pumped.</summary>
    private static void SelectBravo(HeadlessSession session, TextInputElement field)
    {
        var at = AtIndex(session, field, 8);
        LongPressAt(session, at);
    }

    // ──── Selecting ────

    [Fact]
    public void ALongPressOnAWord_SelectsIt_WithTwoHandlesAndTheEditBar_AndNoContextMenu()
    {
        var (session, field, _) = Started();
        using var _s = session;

        SelectBravo(session, field);

        Assert.Equal("bravo", field.SelectedText);
        Assert.Same(field, W(session).FocusedElement);
        Assert.Equal(2, W(session).TouchHandleCount);
        Assert.Equal(["Cut", "Copy", "Select All"], BarLabels(session));   // nothing on the clipboard: no Paste
        Assert.Empty(session.Window.OverlayContents.OfType<ContextMenuElement>());
    }

    [Fact]
    public void TheHandles_SitUnderTheSelectionsEnds_AndAreDrawnInThePrimaryColour()
    {
        var (session, field, _) = Started();
        using var _s = session;
        var start = AtIndex(session, field, 6);
        var end = AtIndex(session, field, 11);
        SelectBravo(session, field);

        var boxes = W(session).TouchHandleBoxes();

        Assert.Equal(2, boxes.Count);
        Assert.Equal(start.X, boxes[0].Right, 1f);    // the start's square corner on its caret
        Assert.Equal(end.X, boxes[1].X, 2f);          // the end's, past the caret's width
        Assert.True(boxes[0].Y > start.Y && boxes[1].Y > end.Y, "the handles hang below the line");
        using var image = session.Window.CapturePixels();
        using var bitmap = SkiaSharp.SKBitmap.FromImage(image);
        var c = Centre(boxes[1]);
        var pixel = bitmap.GetPixel((int)c.X, (int)c.Y);
        var primary = session.Application.Theme.Primary;
        Assert.Equal((primary.R, primary.G, primary.B), (pixel.Red, pixel.Green, pixel.Blue));
    }

    [Fact]
    public void ADoubleTap_SelectsTheWord_WithHandlesAndTheBar()
    {
        var (session, field, _) = Started();
        using var _s = session;
        var at = AtIndex(session, field, 8);

        session.Window.Tap(at.X, at.Y);
        session.PumpFrame(TimeSpan.FromMilliseconds(100));
        session.Window.Tap(at.X, at.Y);
        session.PumpFrame();

        Assert.Equal("bravo", field.SelectedText);
        Assert.Equal(2, W(session).TouchHandleCount);
        Assert.NotNull(W(session).TouchEditBar);
    }

    // ──── The bar ────

    [Fact]
    public void TheBarsCopy_CopiesTheWord_KeepsTheFocusAndSelection_AndPutsTheHandlesAway()
    {
        var (session, field, _) = Started();
        using var _s = session;
        SelectBravo(session, field);

        TapBarItem(session, "Copy");

        Assert.Equal("bravo", session.Window.Clipboard.Text);
        Assert.Same(field, W(session).FocusedElement);
        Assert.Equal("bravo", field.SelectedText);
        Assert.Equal(0, W(session).TouchHandleCount);
        Assert.Null(W(session).TouchEditBar);
    }

    [Fact]
    public void TheBarsSelectAll_SelectsEverything_AndShowsTheHandlesAgain()
    {
        var (session, field, _) = Started();
        using var _s = session;
        SelectBravo(session, field);

        TapBarItem(session, "Select All");

        Assert.Equal(Words, field.SelectedText);
        Assert.Equal(2, W(session).TouchHandleCount);
        Assert.NotNull(W(session).TouchEditBar);
    }

    [Fact]
    public void TheBarsCut_TakesTheWordOut()
    {
        var (session, field, _) = Started();
        using var _s = session;
        SelectBravo(session, field);

        TapBarItem(session, "Cut");

        Assert.Equal("alpha  charlie", field.Text);
        Assert.Equal("bravo", session.Window.Clipboard.Text);
        Assert.Equal(0, W(session).TouchHandleCount);
    }

    [Fact]
    public void TheBar_SitsAboveTheSelection()
    {
        using var session = HeadlessSession.Create(width: 400, height: 300);
        var field = TextInput.New(Words).Width(320);
        session.Window.SetRoot(Flex.Column().Padding(new Thickness(150, 20, 20, 20)).Children(field));
        session.PumpFrame();
        SelectBravo(session, field);

        var bar = W(session).TouchEditBar!;
        session.PumpFrame();

        Assert.True(bar.WindowBorderBox.Bottom <= field.WindowContentBox.Y + 1,
            $"the bar ends at {bar.WindowBorderBox.Bottom}, over the field's text at {field.WindowContentBox.Y}");
    }

    [Fact]
    public void TheBar_GoesBelowTheHandles_WhereThereIsNoRoomAbove()
    {
        var (session, field, _) = Started();   // the field 20 from the window's top
        using var _s = session;
        SelectBravo(session, field);

        var bar = W(session).TouchEditBar!;
        session.PumpFrame();

        var handle = W(session).TouchHandleBoxes()[1];
        Assert.True(bar.WindowBorderBox.Y >= handle.Bottom, $"the bar starts at {bar.WindowBorderBox.Y}, over the handle to {handle.Bottom}");
    }

    // ──── The handles ────

    [Fact]
    public void DraggingTheEndHandle_ExtendsTheSelection_AndTheBarComesBack()
    {
        var (session, field, _) = Started();
        using var _s = session;
        var to = AtIndex(session, field, Words.Length);
        SelectBravo(session, field);
        var handle = Centre(W(session).TouchHandleBoxes()[1]);
        var endCaret = AtIndexOfSelectionEnd(session, field);

        session.Window.TouchDown(1, handle.X, handle.Y);
        Assert.Null(W(session).TouchEditBar);   // put away while the handle moves
        for (int i = 1; i <= 5; i++)
        {
            session.PumpFrame(TimeSpan.FromMilliseconds(16));
            session.Window.TouchMove(1, handle.X + (to.X - endCaret.X + 4) * i / 5f, handle.Y);
        }
        session.Window.TouchUp(1);
        session.PumpFrame();

        Assert.Equal("bravo charlie", field.SelectedText);
        Assert.NotNull(W(session).TouchEditBar);
        Assert.Empty(session.Window.OverlayContents.OfType<ContextMenuElement>());
    }

    /// <summary>The selection's end caret, in the window, without moving the selection.</summary>
    private static Point AtIndexOfSelectionEnd(HeadlessSession session, Element field)
    {
        var (_, caret) = ((ITouchTextEditing)field).TouchCaretBoxes()!.Value;
        var box = field.WindowBorderBox;
        var laid = field.Layout.AbsoluteBorderBox;
        return new Point(caret.X + box.X - laid.X, caret.Y + caret.Height / 2 + box.Y - laid.Y);
    }

    [Fact]
    public void DraggingTheStartHandle_MovesTheStart()
    {
        var (session, field, _) = Started();
        using var _s = session;
        var to = AtIndex(session, field, 0);
        SelectBravo(session, field);
        var handle = Centre(W(session).TouchHandleBoxes()[0]);
        var startCaret = AtIndex(session, field, 6);
        SelectBravo(session, field);

        session.Window.TouchDown(1, handle.X, handle.Y);
        session.Window.TouchMove(1, handle.X - 20, handle.Y);
        session.Window.TouchMove(1, handle.X + (to.X - startCaret.X) - 2, handle.Y);
        session.Window.TouchUp(1);
        session.PumpFrame();

        Assert.Equal("alpha bravo", field.SelectedText);
    }

    [Fact]
    public void AHandle_NeverCollapsesTheSelection()
    {
        var (session, field, _) = Started();
        using var _s = session;
        var start = AtIndex(session, field, 6);
        SelectBravo(session, field);
        var handle = Centre(W(session).TouchHandleBoxes()[1]);
        var endCaret = AtIndexOfSelectionEnd(session, field);

        session.Window.TouchDown(1, handle.X, handle.Y);
        session.Window.TouchMove(1, handle.X - 20, handle.Y);
        session.Window.TouchMove(1, handle.X - (endCaret.X - start.X), handle.Y);   // onto the start
        session.PumpFrame();

        Assert.True(field.HasSelection, "a handle dragged onto the other end keeps a selection");
        Assert.Equal(2, W(session).TouchHandleCount);
        session.Window.TouchUp(1);
    }

    [Fact]
    public void ATap_PlacesTheCaret_AndShowsTheInsertionHandle_ForAWhile()
    {
        var (session, field, _) = Started();
        using var _s = session;
        var at = AtIndex(session, field, 8);
        session.Window.PressKey(KeyCode.End);

        session.Window.Tap(at.X, at.Y);
        session.PumpFrame();

        Assert.False(field.HasSelection);
        Assert.Equal(8, field.SelectionStart);
        Assert.Equal(1, W(session).TouchHandleCount);
        Assert.Null(W(session).TouchEditBar);

        session.PumpFrame(TimeSpan.FromSeconds(4.5));

        Assert.Equal(0, W(session).TouchHandleCount);
    }

    [Fact]
    public void ATapOnTheInsertionHandle_OffersPasteAndSelectAll()
    {
        var (session, field, _) = Started();
        using var _s = session;
        session.Window.Clipboard.Text = "pasted";
        var at = AtIndex(session, field, 8);
        session.Window.Tap(at.X, at.Y);
        session.PumpFrame();
        var handle = Centre(Assert.Single(W(session).TouchHandleBoxes()));

        session.Window.Tap(handle.X, handle.Y);
        session.PumpFrame();

        Assert.Equal(["Paste", "Select All"], BarLabels(session));
        TapBarItem(session, "Paste");
        Assert.Equal("alpha brpastedavo charlie", field.Text);
    }

    [Fact]
    public void DraggingTheInsertionHandle_MovesTheCaret()
    {
        var (session, field, _) = Started();
        using var _s = session;
        var to = AtIndex(session, field, 2);
        var at = AtIndex(session, field, 8);
        session.Window.Tap(at.X, at.Y);
        session.PumpFrame();
        var handle = Centre(Assert.Single(W(session).TouchHandleBoxes()));

        session.Window.TouchDown(1, handle.X, handle.Y);
        session.Window.TouchMove(1, handle.X - 20, handle.Y);
        session.Window.TouchMove(1, handle.X - (at.X - to.X), handle.Y);
        session.Window.TouchUp(1);
        session.PumpFrame();

        Assert.False(field.HasSelection);
        Assert.Equal(2, field.SelectionStart);
        Assert.Equal(1, W(session).TouchHandleCount);   // shown again for a while
    }

    // ──── Putting them away ────

    [Fact]
    public void ATapElsewhere_PutsTheHandlesAndTheBarAway()
    {
        var (session, field, other) = Started();
        using var _s = session;
        SelectBravo(session, field);

        // The tap has to miss the bar, which opens below a field this near the top. With the
        // button just under it, a font a little wider (Linux's) put the bar's Select all where the
        // tap went, and the handles came back for the whole text.
        var bar = W(session).TouchEditBar!.WindowBorderBox;
        var tap = Centre(other.WindowBorderBox);
        Assert.False(bar.Contains(tap), $"the tap at {tap} is on the bar {bar}");
        session.Window.Tap(other);
        session.PumpFrame();

        Assert.Equal(0, W(session).TouchHandleCount);
        Assert.Null(W(session).TouchEditBar);
    }

    [Fact]
    public void Typing_PutsThemAway()
    {
        var (session, field, _) = Started();
        using var _s = session;
        SelectBravo(session, field);

        session.Window.TypeText("x");
        session.PumpFrame();

        Assert.Equal("alpha x charlie", field.Text);
        Assert.Equal(0, W(session).TouchHandleCount);
        Assert.Null(W(session).TouchEditBar);
    }

    [Fact]
    public void Typing_PutsTheInsertionHandleAway()
    {
        var (session, field, _) = Started();
        using var _s = session;
        var at = AtIndex(session, field, 8);
        session.Window.Tap(at.X, at.Y);
        session.PumpFrame();
        Assert.Equal(1, W(session).TouchHandleCount);

        session.Window.TypeText("x");
        session.PumpFrame();

        Assert.Equal(0, W(session).TouchHandleCount);
    }

    [Fact]
    public void TheMouse_PutsThemAway_AndARightClickStillOpensTheContextMenu()
    {
        var (session, field, _) = Started();
        using var _s = session;
        SelectBravo(session, field);

        session.Window.Click(field, MouseButton.Right);
        session.PumpFrame();

        Assert.Equal(0, W(session).TouchHandleCount);
        Assert.Single(session.Window.OverlayContents.OfType<ContextMenuElement>());
    }

    [Fact]
    public void APanOnThePage_ScrollsIt_KeepsTheSelectionAndItsHandles_AndTheBarComesBackAfter()
    {
        using var session = HeadlessSession.Create(width: 400, height: 300);
        var field = TextInput.New(Words).Width(320);
        // Rows above the field, so the scroll leaves it in view.
        var rows = new List<Element> { TextB.New("Top 0").Height(30f).FlexShrink(0), TextB.New("Top 1").Height(30f).FlexShrink(0), TextB.New("Top 2").Height(30f).FlexShrink(0), field };
        for (int i = 0; i < 20; i++) rows.Add(TextB.New($"Row {i}").Height(30f).FlexShrink(0));
        var scroll = ScrollView.Vertical().FlexGrow(1);
        scroll.Children(Flex.Column(gap: 4).Padding(10).Children(rows.ToArray()));
        session.Window.SetRoot(Flex.Column().Height(Dimension.Percent(100)).Children(scroll));
        session.PumpFrame();
        SelectBravo(session, field);
        var handleBefore = W(session).TouchHandleBoxes()[1];

        session.Window.Swipe(new Point(200, 250), new Point(200, 210), TimeSpan.FromMilliseconds(800));
        Assert.True(scroll.ScrollOffsetY > 20, $"the page scrolled {scroll.ScrollOffsetY}");
        session.PumpFrame();

        Assert.Equal("bravo", field.SelectedText);
        Assert.Equal(2, W(session).TouchHandleCount);
        var handleAfter = W(session).TouchHandleBoxes()[1];
        Assert.Equal(handleBefore.Y - scroll.ScrollOffsetY, handleAfter.Y, 1f);   // they went with the text
        Assert.NotNull(W(session).TouchEditBar);
    }

    // ──── The focus, under a finger on empty space ────

    [Fact]
    public void ATapOnEmptySpace_StillClearsTheFocus_AndStopsTheKeyboard()
    {
        var (session, field, _) = Started();
        using var _s = session;
        session.Window.Tap(field);
        Assert.Same(field, W(session).FocusedElement);

        session.Window.Tap(380, 280);   // the root's empty corner
        session.PumpFrame();

        Assert.Null(W(session).FocusedElement);
        Assert.False(session.Window.TextInputHost.IsTextInputActive);
    }

    [Fact]
    public void AScrollStartingOnEmptySpace_KeepsTheFocusAndTheKeyboard()
    {
        // A finger on the page under a field is a scroll as often as a tap: the focus, and a
        // phone's keyboard with it, waits for the release to know which.
        using var session = HeadlessSession.Create(width: 400, height: 300);
        var field = TextInput.New(Words).Width(320);
        var rows = new List<Element> { field };
        for (int i = 0; i < 20; i++) rows.Add(TextB.New($"Row {i}").Height(30f).FlexShrink(0));
        var scroll = ScrollView.Vertical().FlexGrow(1);
        scroll.Children(Flex.Column(gap: 4).Padding(10).Children(rows.ToArray()));
        session.Window.SetRoot(Flex.Column().Height(Dimension.Percent(100)).Children(scroll));
        session.PumpFrame();
        session.Window.Tap(field);

        session.Window.Swipe(new Point(200, 250), new Point(200, 200), TimeSpan.FromMilliseconds(800));
        session.PumpFrame();

        Assert.True(scroll.ScrollOffsetY > 20);
        Assert.Same(field, W(session).FocusedElement);
        Assert.True(session.Window.TextInputHost.IsTextInputActive);
    }

    // ──── The other controls ────

    [Fact]
    public void InATextArea_ALongPressSelectsTheWord_AndAHandleExtendsIt()
    {
        using var session = HeadlessSession.Create(width: 400, height: 300);
        var area = TextArea.New("first line\nalpha bravo charlie").Width(320).Height(120);
        session.Window.SetRoot(Flex.Column().Padding(20).Children(area));
        session.PumpFrame();
        area.Focus();
        var to = AtIndex(session, area, 11 + Words.Length);
        var at = AtIndex(session, area, 11 + 8);

        LongPressAt(session, at);

        Assert.Equal("bravo", area.SelectedText);
        Assert.Equal(2, W(session).TouchHandleCount);
        var handle = Centre(W(session).TouchHandleBoxes()[1]);
        var endCaret = AtIndexOfSelectionEnd(session, area);
        session.Window.TouchDown(1, handle.X, handle.Y);
        session.Window.TouchMove(1, handle.X + 20, handle.Y);
        session.Window.TouchMove(1, handle.X + (to.X - endCaret.X) + 4, handle.Y);
        session.Window.TouchUp(1);
        session.PumpFrame();
        Assert.Equal("bravo charlie", area.SelectedText);
    }

    [Fact]
    public void InTheRichTextEditor_ALongPressSelectsTheWord_WithHandlesAndTheBar()
    {
        using var session = HeadlessSession.Create(width: 400, height: 300);
        var editor = RichTextEditor.New().Width(320).Height(120);
        session.Window.SetRoot(Flex.Column().Padding(20).Children(editor));
        session.PumpFrame();
        editor.Focus();
        session.Window.TypeText(Words);
        session.PumpFrame();
        session.Window.PressKey(KeyCode.Home);
        for (int i = 0; i < 8; i++) session.Window.PressKey(KeyCode.Right);
        session.PumpFrame();
        var at = AtIndexOfSelectionEnd(session, editor);

        LongPressAt(session, at);

        Assert.Equal("bravo", editor.State.SelectedText);
        Assert.Equal(2, W(session).TouchHandleCount);
        Assert.Contains("Copy", BarLabels(session));
    }

    [Fact]
    public void AFieldInAScrolledPage_SelectsTheWordUnderTheFinger()
    {
        // The finger is mapped through the area's drawn place: a page scrolled 100 down puts the
        // area's layout 100 below where it is drawn, and which line is under the finger is the
        // height's to say.
        using var session = HeadlessSession.Create(width: 400, height: 300);
        var rows = new List<Element>();
        for (int i = 0; i < 6; i++) rows.Add(TextB.New($"Row {i}").Height(30f).FlexShrink(0));
        var field = TextArea.New("first line\n" + Words).Width(320).Height(80).FlexShrink(0);
        rows.Add(field);
        for (int i = 0; i < 10; i++) rows.Add(TextB.New($"After {i}").Height(30f).FlexShrink(0));
        var scroll = ScrollView.Vertical().FlexGrow(1);
        scroll.Children(Flex.Column(gap: 4).Padding(10).Children(rows.ToArray()));
        session.Window.SetRoot(Flex.Column().Height(Dimension.Percent(100)).Children(scroll));
        session.PumpFrame();
        scroll.ScrollOffsetY = 100;
        session.PumpFrame();
        field.Focus();
        var at = AtIndex(session, field, 11 + 14);   // in "charlie", on the second line

        LongPressAt(session, at);

        Assert.Equal("charlie", field.SelectedText);
        Assert.Equal(100, scroll.ScrollOffsetY, 0.5f);
    }

    [Fact]
    public void AnOnLongPressHandlerAroundAField_StillWins()
    {
        using var session = HeadlessSession.Create(width: 400, height: 300);
        int held = 0;
        var field = TextInput.New(Words).Width(320);
        var row = Box.New().Padding(10).Children(field);
        row.OnLongPress(_ => held++);
        session.Window.SetRoot(Flex.Column().Children(row));
        session.PumpFrame();

        session.Window.LongPress(field);
        session.PumpFrame();

        Assert.Equal(1, held);
        Assert.False(field.HasSelection);
        Assert.Equal(0, W(session).TouchHandleCount);
    }
}
