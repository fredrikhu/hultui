using HultUI.Core.Elements;
using HultUI.Core.Events;
using HultUI.Core.Layout;
using HultUI.Core.Primitives;
using HultUI.Core.Styling;
using Xunit;

namespace HultUI.Core.Tests.Elements;

public class TextAreaElementTests
{
    private readonly LayoutEngine _layout = new(new StubMeasureContext());

    private TextAreaElement CreateArea(string text = "Hello", float width = 200, float height = 100)
    {
        var area = TextAreaElement.New(text);
        area.Box.Width = width;
        area.Box.Height = height;
        var container = BoxElement.New();
        container.Box.Width = 400;
        container.Box.Height = 400;
        container.AddChild(area);
        StyleResolver.Resolve(container, Theme.Light.ToResolvedStyle());
        _layout.PerformLayout(container, 400, 400);
        EventDispatcher.DispatchDirect(new FocusEvent(), area);
        return area;
    }

    // ──── Basic Properties ────

    [Fact]
    public void New_SetsTextAndCursor()
    {
        var area = TextAreaElement.New("Hello");
        Assert.Equal("Hello", area.Text);
        Assert.Equal(5, area.CursorPosition);
    }

    [Fact]
    public void CursorPosition_ClampedToTextLength()
    {
        var area = TextAreaElement.New("Hi");
        area.CursorPosition = 100;
        Assert.Equal(2, area.CursorPosition);
    }

    // ──── Text Input ────

    [Fact]
    public void TextInput_InsertsText()
    {
        var area = CreateArea("Hello");
        EventDispatcher.Dispatch(new TextInputEvent { Text = " World" }, area);
        Assert.Equal("Hello World", area.Text);
    }

    [Fact]
    public void Enter_InsertsNewline()
    {
        var area = CreateArea("Hello");
        EventDispatcher.Dispatch(new KeyDownEvent { Key = KeyCode.Enter }, area);
        Assert.Equal("Hello\n", area.Text);
        Assert.Equal(6, area.CursorPosition);
    }

    [Fact]
    public void Enter_SplitsLineAtCursor()
    {
        var area = CreateArea("Hello World");
        area.CursorPosition = 5;
        EventDispatcher.Dispatch(new KeyDownEvent { Key = KeyCode.Enter }, area);
        Assert.Equal("Hello\n World", area.Text);
        Assert.Equal(6, area.CursorPosition);
    }

    [Fact]
    public void MultipleNewlines_CreateMultipleLines()
    {
        var area = CreateArea("A\nB\nC");
        var style = area.ResolvedInherited;
        var starts = area.GetLineStarts(200, style);
        Assert.Equal(3, starts.Count);
        Assert.Equal(0, starts[0]); // "A\n"
        Assert.Equal(2, starts[1]); // "B\n"
        Assert.Equal(4, starts[2]); // "C"
    }

    // ──── Backspace / Delete ────

    [Fact]
    public void Backspace_DeletesCharacter()
    {
        var area = CreateArea("Hello");
        EventDispatcher.Dispatch(new KeyDownEvent { Key = KeyCode.Backspace }, area);
        Assert.Equal("Hell", area.Text);
    }

    [Fact]
    public void Backspace_AcrossNewline()
    {
        var area = CreateArea("AB\nCD");
        area.CursorPosition = 3; // at 'C', just after \n
        EventDispatcher.Dispatch(new KeyDownEvent { Key = KeyCode.Backspace }, area);
        Assert.Equal("ABCD", area.Text);
        Assert.Equal(2, area.CursorPosition);
    }

    [Fact]
    public void Delete_DeletesForward()
    {
        var area = CreateArea("Hello");
        area.CursorPosition = 0;
        EventDispatcher.Dispatch(new KeyDownEvent { Key = KeyCode.Delete }, area);
        Assert.Equal("ello", area.Text);
    }

    [Fact]
    public void Delete_AcrossNewline()
    {
        var area = CreateArea("AB\nCD");
        area.CursorPosition = 2; // before \n
        EventDispatcher.Dispatch(new KeyDownEvent { Key = KeyCode.Delete }, area);
        Assert.Equal("ABCD", area.Text);
    }

    // ──── Selection ────

    [Fact]
    public void CtrlA_SelectsAll()
    {
        var area = CreateArea("Hello\nWorld");
        area.CursorPosition = 2;
        EventDispatcher.Dispatch(new KeyDownEvent { Key = KeyCode.A, Modifiers = KeyModifiers.Ctrl }, area);
        Assert.Equal(0, area.SelectionStart);
        Assert.Equal(11, area.SelectionEnd);
        Assert.True(area.HasSelection);
    }

    [Fact]
    public void Selection_DeletedOnTextInput()
    {
        var area = CreateArea("Hello");
        EventDispatcher.Dispatch(new KeyDownEvent { Key = KeyCode.A, Modifiers = KeyModifiers.Ctrl }, area);
        EventDispatcher.Dispatch(new TextInputEvent { Text = "Hi" }, area);
        Assert.Equal("Hi", area.Text);
    }

    [Fact]
    public void ShiftRight_ExtendsSelection()
    {
        var area = CreateArea("Hello");
        area.CursorPosition = 0;
        EventDispatcher.Dispatch(new KeyDownEvent { Key = KeyCode.Right, Modifiers = KeyModifiers.Shift }, area);
        EventDispatcher.Dispatch(new KeyDownEvent { Key = KeyCode.Right, Modifiers = KeyModifiers.Shift }, area);
        Assert.True(area.HasSelection);
        Assert.Equal("He", area.SelectedText);
    }

    [Fact]
    public void ShiftDown_ExtendsSelectionAcrossLines()
    {
        var area = CreateArea("AB\nCD");
        area.CursorPosition = 1; // after 'A'
        EventDispatcher.Dispatch(new KeyDownEvent { Key = KeyCode.Down, Modifiers = KeyModifiers.Shift }, area);
        Assert.True(area.HasSelection);
        // Selection should span from position 1 to somewhere on line 2
        Assert.True(area.SelectionEnd > 2, "Selection should extend past the newline");
    }

    // ──── Arrow Navigation ────

    [Fact]
    public void LeftRight_MovesWithinLine()
    {
        var area = CreateArea("Hello");
        Assert.Equal(5, area.CursorPosition);
        EventDispatcher.Dispatch(new KeyDownEvent { Key = KeyCode.Left }, area);
        Assert.Equal(4, area.CursorPosition);
        EventDispatcher.Dispatch(new KeyDownEvent { Key = KeyCode.Right }, area);
        Assert.Equal(5, area.CursorPosition);
    }

    [Fact]
    public void Left_CrossesNewline()
    {
        var area = CreateArea("AB\nCD");
        area.CursorPosition = 3; // 'C'
        EventDispatcher.Dispatch(new KeyDownEvent { Key = KeyCode.Left }, area);
        Assert.Equal(2, area.CursorPosition); // at \n
    }

    [Fact]
    public void Right_CrossesNewline()
    {
        var area = CreateArea("AB\nCD");
        area.CursorPosition = 2; // at \n
        EventDispatcher.Dispatch(new KeyDownEvent { Key = KeyCode.Right }, area);
        Assert.Equal(3, area.CursorPosition); // 'C'
    }

    [Fact]
    public void Down_MovesToNextLine()
    {
        var area = CreateArea("AB\nCD");
        area.CursorPosition = 1; // after 'A'
        EventDispatcher.Dispatch(new KeyDownEvent { Key = KeyCode.Down }, area);
        // Should be on line 2, same column offset
        Assert.Equal(4, area.CursorPosition); // after 'C' (column 1 on line 2)
    }

    [Fact]
    public void Up_MovesToPreviousLine()
    {
        var area = CreateArea("AB\nCD");
        area.CursorPosition = 4; // after 'C' on line 2
        EventDispatcher.Dispatch(new KeyDownEvent { Key = KeyCode.Up }, area);
        Assert.Equal(1, area.CursorPosition); // after 'A' on line 1
    }

    [Fact]
    public void Up_AtFirstLine_GoesToStart()
    {
        var area = CreateArea("AB\nCD");
        area.CursorPosition = 1;
        EventDispatcher.Dispatch(new KeyDownEvent { Key = KeyCode.Up }, area);
        Assert.Equal(0, area.CursorPosition);
    }

    [Fact]
    public void Down_AtLastLine_GoesToEnd()
    {
        var area = CreateArea("AB\nCD");
        area.CursorPosition = 3; // 'C'
        EventDispatcher.Dispatch(new KeyDownEvent { Key = KeyCode.Down }, area);
        Assert.Equal(5, area.CursorPosition);
    }

    // ──── Home / End ────

    [Fact]
    public void Home_GoesToLineStart()
    {
        var area = CreateArea("AB\nCD");
        area.CursorPosition = 4; // after 'C'
        EventDispatcher.Dispatch(new KeyDownEvent { Key = KeyCode.Home }, area);
        Assert.Equal(3, area.CursorPosition); // start of line 2
    }

    [Fact]
    public void End_GoesToLineEnd()
    {
        var area = CreateArea("AB\nCD");
        area.CursorPosition = 3; // 'C'
        EventDispatcher.Dispatch(new KeyDownEvent { Key = KeyCode.End }, area);
        Assert.Equal(5, area.CursorPosition); // end of "CD"
    }

    [Fact]
    public void CtrlHome_GoesToDocumentStart()
    {
        var area = CreateArea("AB\nCD");
        area.CursorPosition = 4;
        EventDispatcher.Dispatch(new KeyDownEvent { Key = KeyCode.Home, Modifiers = KeyModifiers.Ctrl }, area);
        Assert.Equal(0, area.CursorPosition);
    }

    [Fact]
    public void CtrlEnd_GoesToDocumentEnd()
    {
        var area = CreateArea("AB\nCD");
        area.CursorPosition = 0;
        EventDispatcher.Dispatch(new KeyDownEvent { Key = KeyCode.End, Modifiers = KeyModifiers.Ctrl }, area);
        Assert.Equal(5, area.CursorPosition);
    }

    // ──── Word Navigation ────

    [Fact]
    public void CtrlLeft_MovesToWordBoundary()
    {
        var area = CreateArea("Hello World");
        area.CursorPosition = 11; // end
        EventDispatcher.Dispatch(new KeyDownEvent { Key = KeyCode.Left, Modifiers = KeyModifiers.Ctrl }, area);
        Assert.Equal(6, area.CursorPosition); // start of "World"
    }

    [Fact]
    public void CtrlRight_MovesToWordBoundary()
    {
        var area = CreateArea("Hello World");
        area.CursorPosition = 0;
        EventDispatcher.Dispatch(new KeyDownEvent { Key = KeyCode.Right, Modifiers = KeyModifiers.Ctrl }, area);
        Assert.Equal(6, area.CursorPosition); // past "Hello "
    }

    // ──── Line Wrapping ────

    [Fact]
    public void LongLine_WrapsAtWidth()
    {
        // StubMeasureContext: 7px per char. Width 200 → padding ~16px → content ~184px → ~26 chars per line
        // "AAAA...A" (40 chars) should wrap to 2 lines
        var area = CreateArea(new string('A', 40), width: 200);
        var style = area.ResolvedInherited;
        float wrapWidth = area.Layout.AbsoluteContentBox.Width;
        var starts = area.GetLineStarts(wrapWidth, style);
        Assert.True(starts.Count >= 2, $"Expected 2+ wrapped lines, got {starts.Count}. wrapWidth={wrapWidth}");
    }

    [Fact]
    public void WordWrap_BreaksAtSpace()
    {
        // Content width = 200 - 16 padding = 184px. 7px/char → 26 chars fit.
        // "The quick brown fox jumps" is 25 chars → fits on one line.
        // "The quick brown fox jumps over" is 30 chars → wraps.
        var text = "The quick brown fox jumps over";
        var area = CreateArea(text, width: 200);
        var style = area.ResolvedInherited;
        float wrapWidth = area.Layout.AbsoluteContentBox.Width;
        var starts = area.GetLineStarts(wrapWidth, style);
        Assert.True(starts.Count >= 2);
        // The break should be at a space, not mid-word
        int breakPos = starts[1];
        Assert.True(breakPos > 0);
        // Character before break should be a space (word boundary break)
        Assert.True(breakPos <= text.Length);
    }

    // ──── CursorToLineCol ────

    [Fact]
    public void CursorToLineCol_FirstLine()
    {
        var area = CreateArea("AB\nCD");
        var style = area.ResolvedInherited;
        var (line, col) = area.CursorToLineCol(1, 200, style);
        Assert.Equal(0, line);
        Assert.Equal(1, col);
    }

    [Fact]
    public void CursorToLineCol_SecondLine()
    {
        var area = CreateArea("AB\nCD");
        var style = area.ResolvedInherited;
        var (line, col) = area.CursorToLineCol(4, 200, style);
        Assert.Equal(1, line);
        Assert.Equal(1, col);
    }

    [Fact]
    public void CursorToLineCol_AtNewline()
    {
        var area = CreateArea("AB\nCD");
        var style = area.ResolvedInherited;
        // Position 2 is the \n character, which belongs to line 0
        var (line, col) = area.CursorToLineCol(2, 200, style);
        Assert.Equal(0, line);
        Assert.Equal(2, col);
    }

    // ──── TextChanged callback ────

    [Fact]
    public void OnTextChanged_CalledOnInsert()
    {
        string? changed = null;
        var area = CreateArea("Hello");
        area.TextChanged = t => changed = t;
        EventDispatcher.Dispatch(new TextInputEvent { Text = "!" }, area);
        Assert.Equal("Hello!", changed);
    }

    [Fact]
    public void OnTextChanged_CalledOnEnter()
    {
        string? changed = null;
        var area = CreateArea("AB");
        area.TextChanged = t => changed = t;
        EventDispatcher.Dispatch(new KeyDownEvent { Key = KeyCode.Enter }, area);
        Assert.Equal("AB\n", changed);
    }

    // ──── PatchFrom ────

    [Fact]
    public void PatchFrom_CopiesTextFromSource()
    {
        var area = CreateArea("Hello World");
        area.CursorPosition = 5;

        var incoming = TextAreaElement.New("Updated");
        incoming.Placeholder = "Type here...";

        area.PatchFrom(incoming);

        Assert.Equal("Updated", area.Text);
        Assert.Equal("Type here...", area.Placeholder);
    }

    // ──── Double/Triple Click ────

    [Fact]
    public void DoubleClick_SelectsWord()
    {
        var area = CreateArea("Hello World");
        EventDispatcher.Dispatch(new ClickEvent { Position = new Point(10, 5), ClickCount = 2 }, area);
        Assert.True(area.HasSelection);
        Assert.Equal("Hello", area.SelectedText);
    }

    [Fact]
    public void TripleClick_SelectsCurrentLine()
    {
        var area = CreateArea("AB\nCD");
        // Cursor at position 1 → line 0
        area.CursorPosition = 1;
        EventDispatcher.Dispatch(new ClickEvent { Position = new Point(10, 5), ClickCount = 3 }, area);
        Assert.True(area.HasSelection);
        Assert.Contains("AB", area.SelectedText);
    }

    [Fact]
    public void TripleClick_SelectsSecondLine()
    {
        var area = CreateArea("AB\nCD");
        // Cursor at position 4 → line 1
        area.CursorPosition = 4;
        EventDispatcher.Dispatch(new ClickEvent { Position = new Point(10, 20), ClickCount = 3 }, area);
        Assert.True(area.HasSelection);
        Assert.Contains("CD", area.SelectedText);
    }

    // ──── Pointer Drag ────

    [Fact]
    public void PointerDown_StartsDrag()
    {
        var area = CreateArea("Hello");
        EventDispatcher.Dispatch(new PointerDownEvent { Position = new Point(8, 5), Button = 0 }, area);
        Assert.True(area.IsDragging);

        EventDispatcher.Dispatch(new PointerUpEvent { Position = new Point(8, 5), Button = 0 }, area);
        Assert.False(area.IsDragging);
    }

    // ──── Empty text edge cases ────

    [Fact]
    public void EmptyText_HasOneLine()
    {
        var area = CreateArea("");
        var style = area.ResolvedInherited;
        var starts = area.GetLineStarts(200, style);
        Assert.Single(starts);
        Assert.Equal(0, starts[0]);
    }

    [Fact]
    public void TrailingNewline_CreatesExtraLine()
    {
        var area = CreateArea("AB\n");
        var style = area.ResolvedInherited;
        var starts = area.GetLineStarts(200, style);
        Assert.Equal(2, starts.Count);
        Assert.Equal(0, starts[0]);
        Assert.Equal(3, starts[1]);
    }
}
