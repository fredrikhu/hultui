using HultUI.Core.Elements;
using HultUI.Core.Events;
using HultUI.Core.Layout;
using HultUI.Core.Platform;
using HultUI.Core.Primitives;
using HultUI.Core.Styling;
using Xunit;

namespace HultUI.Core.Tests.Elements;

public class TextInputElementTests
{
    private readonly LayoutEngine _layout = new(new StubMeasureContext());

    private TextInputElement CreateInput(string text = "Hello")
    {
        var input = TextInputElement.New(text);
        input.Box.Width = 200;
        input.Box.Height = 32;
        var container = BoxElement.New();
        container.Box.Width = 400;
        container.Box.Height = 400;
        container.AddChild(input);
        // Resolve styles so ResolvedInherited has valid FontSize (needed for hit-testing)
        StyleResolver.Resolve(container, Theme.Light.ToResolvedStyle());
        _layout.PerformLayout(container, 400, 400);
        // Simulate focus
        EventDispatcher.DispatchDirect(new FocusEvent(), input);
        return input;
    }

    [Fact]
    public void New_SetsTextAndCursor()
    {
        var input = TextInputElement.New("Hello");
        Assert.Equal("Hello", input.Text);
        Assert.Equal(5, input.CursorPosition);
    }

    [Fact]
    public void TextInput_InsertsText()
    {
        var input = CreateInput("Hello");
        EventDispatcher.Dispatch(new TextInputEvent { Text = " World" }, input);
        Assert.Equal("Hello World", input.Text);
    }

    [Fact]
    public void Backspace_DeletesCharacter()
    {
        var input = CreateInput("Hello");
        EventDispatcher.Dispatch(new KeyDownEvent { Key = KeyCode.Backspace }, input);
        Assert.Equal("Hell", input.Text);
        Assert.Equal(4, input.CursorPosition);
    }

    [Fact]
    public void Delete_DeletesForward()
    {
        var input = CreateInput("Hello");
        input.CursorPosition = 0;
        EventDispatcher.Dispatch(new KeyDownEvent { Key = KeyCode.Delete }, input);
        Assert.Equal("ello", input.Text);
        Assert.Equal(0, input.CursorPosition);
    }

    [Fact]
    public void ArrowLeft_MovesCursorBack()
    {
        var input = CreateInput("Hello");
        Assert.Equal(5, input.CursorPosition);
        EventDispatcher.Dispatch(new KeyDownEvent { Key = KeyCode.Left }, input);
        Assert.Equal(4, input.CursorPosition);
    }

    [Fact]
    public void ArrowRight_MovesCursorForward()
    {
        var input = CreateInput("Hello");
        input.CursorPosition = 2;
        EventDispatcher.Dispatch(new KeyDownEvent { Key = KeyCode.Right }, input);
        Assert.Equal(3, input.CursorPosition);
    }

    [Fact]
    public void Home_MovesCursorToStart()
    {
        var input = CreateInput("Hello");
        EventDispatcher.Dispatch(new KeyDownEvent { Key = KeyCode.Home }, input);
        Assert.Equal(0, input.CursorPosition);
    }

    [Fact]
    public void End_MovesCursorToEnd()
    {
        var input = CreateInput("Hello");
        input.CursorPosition = 0;
        EventDispatcher.Dispatch(new KeyDownEvent { Key = KeyCode.End }, input);
        Assert.Equal(5, input.CursorPosition);
    }

    [Fact]
    public void CtrlA_SelectsAll()
    {
        var input = CreateInput("Hello");
        input.CursorPosition = 2;
        EventDispatcher.Dispatch(new KeyDownEvent { Key = KeyCode.A, Modifiers = KeyModifiers.Ctrl }, input);
        Assert.Equal(0, input.SelectionStart);
        Assert.Equal(5, input.SelectionEnd);
        Assert.True(input.HasSelection);
    }

    [Fact]
    public void Selection_DeletedOnTextInput()
    {
        var input = CreateInput("Hello");
        // Select all
        EventDispatcher.Dispatch(new KeyDownEvent { Key = KeyCode.A, Modifiers = KeyModifiers.Ctrl }, input);
        // Type replacement
        EventDispatcher.Dispatch(new TextInputEvent { Text = "Hi" }, input);
        Assert.Equal("Hi", input.Text);
    }

    [Fact]
    public void OnTextChanged_CalledOnInsert()
    {
        string? changed = null;
        var input = CreateInput("Hello");
        input.TextChanged = t => changed = t;
        EventDispatcher.Dispatch(new TextInputEvent { Text = "!" }, input);
        Assert.Equal("Hello!", changed);
    }

    [Fact]
    public void PatchFrom_CopiesTextFromSource()
    {
        var input = CreateInput("Hello World");
        input.CursorPosition = 5;

        var incoming = TextInputElement.New("Updated");
        incoming.Placeholder = "Type here...";

        input.PatchFrom(incoming);

        Assert.Equal("Updated", input.Text);
        Assert.Equal("Type here...", input.Placeholder);
    }

    [Fact]
    public void PatchFrom_ClearsTextWhenSourceIsEmpty()
    {
        var input = CreateInput("Hello World");

        var incoming = TextInputElement.New();
        input.PatchFrom(incoming);

        Assert.Equal("", input.Text);
    }

    [Fact]
    public void PatchFrom_PreservesTextWhenSourceHasSameText()
    {
        var input = CreateInput("Hello World");
        input.CursorPosition = 5;

        var incoming = TextInputElement.New("Hello World");
        input.PatchFrom(incoming);

        Assert.Equal("Hello World", input.Text);
        Assert.Equal(5, input.CursorPosition);
    }

    [Fact]
    public void CursorPosition_ClampedToTextLength()
    {
        var input = TextInputElement.New("Hi");
        input.CursorPosition = 100;
        Assert.Equal(2, input.CursorPosition);
    }

    [Fact]
    public void Backspace_WithSelection_DeletesSelection()
    {
        var input = CreateInput("Hello");
        // Select "ell" (positions 1-4)
        EventDispatcher.Dispatch(new KeyDownEvent { Key = KeyCode.Home }, input);
        EventDispatcher.Dispatch(new KeyDownEvent { Key = KeyCode.Right }, input);
        EventDispatcher.Dispatch(new KeyDownEvent { Key = KeyCode.Right, Modifiers = KeyModifiers.Shift }, input);
        EventDispatcher.Dispatch(new KeyDownEvent { Key = KeyCode.Right, Modifiers = KeyModifiers.Shift }, input);
        EventDispatcher.Dispatch(new KeyDownEvent { Key = KeyCode.Right, Modifiers = KeyModifiers.Shift }, input);
        Assert.True(input.HasSelection);
        Assert.Equal("ell", input.SelectedText);

        EventDispatcher.Dispatch(new KeyDownEvent { Key = KeyCode.Backspace }, input);
        Assert.Equal("Ho", input.Text);
    }

    [Fact]
    public void DoubleClick_SelectsWord()
    {
        var input = CreateInput("Hello World");
        // Double-click somewhere in "Hello" — approximate position doesn't matter,
        // the click handler uses PositionFromPoint which falls back to char approximation
        EventDispatcher.Dispatch(new ClickEvent { Position = new Point(10, 5), ClickCount = 2 }, input);
        Assert.True(input.HasSelection);
        Assert.Equal("Hello", input.SelectedText);
    }

    [Fact]
    public void TripleClick_SelectsAll()
    {
        var input = CreateInput("Hello World");
        EventDispatcher.Dispatch(new ClickEvent { Position = new Point(10, 5), ClickCount = 3 }, input);
        Assert.True(input.HasSelection);
        Assert.Equal("Hello World", input.SelectedText);
    }

    [Fact]
    public void PointerDown_StartsDrag()
    {
        var input = CreateInput("Hello");
        EventDispatcher.Dispatch(new PointerDownEvent { Position = new Point(8, 5), Button = 0 }, input);
        Assert.True(input.IsDragging);

        EventDispatcher.Dispatch(new PointerUpEvent { Position = new Point(8, 5), Button = 0 }, input);
        Assert.False(input.IsDragging);
    }

    // ──── IME composition (preedit) ────

    [Fact]
    public void TextEditing_SetsCompositionWithoutMutatingText()
    {
        var input = CreateInput("abc");
        input.CursorPosition = 2;

        EventDispatcher.Dispatch(new TextEditingEvent { Text = "ni", SegmentStart = 0, SegmentLength = 2 }, input);

        Assert.True(input.IsComposing);
        Assert.Equal("ni", input.CompositionText);
        Assert.Equal("abc", input.Text);
        Assert.Equal(2, input.CursorPosition);
    }

    [Fact]
    public void TextEditing_EmptyText_ClearsComposition()
    {
        var input = CreateInput("abc");
        EventDispatcher.Dispatch(new TextEditingEvent { Text = "ni" }, input);
        Assert.True(input.IsComposing);

        EventDispatcher.Dispatch(new TextEditingEvent { Text = "" }, input);

        Assert.False(input.IsComposing);
        Assert.Equal("", input.CompositionText);
    }

    [Fact]
    public void TextInput_AfterComposition_ClearsPreeditAndInsertsCommit()
    {
        var input = CreateInput("abc");
        input.CursorPosition = 3;
        EventDispatcher.Dispatch(new TextEditingEvent { Text = "ni" }, input);
        Assert.True(input.IsComposing);

        EventDispatcher.Dispatch(new TextInputEvent { Text = "你" }, input);

        Assert.False(input.IsComposing);
        Assert.Equal("abc你", input.Text);
        Assert.Equal(4, input.CursorPosition);
    }

    [Fact]
    public void TextEditing_WithSelection_DeletesSelectionOnFirstEvent()
    {
        var input = CreateInput("hello world");
        // Select " world" (positions 5..11)
        input.CursorPosition = 5;
        EventDispatcher.Dispatch(new KeyDownEvent { Key = KeyCode.End, Modifiers = KeyModifiers.Shift }, input);
        Assert.True(input.HasSelection);

        EventDispatcher.Dispatch(new TextEditingEvent { Text = "ni" }, input);

        Assert.Equal("hello", input.Text);
        Assert.False(input.HasSelection);
        Assert.True(input.IsComposing);

        // Subsequent TextEditing events should NOT re-delete (nothing selected).
        EventDispatcher.Dispatch(new TextEditingEvent { Text = "nih", SegmentStart = 0, SegmentLength = 3 }, input);
        Assert.Equal("hello", input.Text);
        Assert.Equal("nih", input.CompositionText);
    }

    [Fact]
    public void Blur_WhileComposing_ClearsCompositionAndCallsHost()
    {
        var input = CreateInput("abc");
        var host = new FakeTextInputHost();
        input.TextInputHost = host;
        EventDispatcher.Dispatch(new TextEditingEvent { Text = "ni" }, input);
        Assert.True(input.IsComposing);

        EventDispatcher.DispatchDirect(new BlurEvent(), input);

        Assert.False(input.IsComposing);
        Assert.Equal(1, host.ClearCompositionCalls);
    }

    [Fact]
    public void Blur_WithoutComposition_DoesNotCallHost()
    {
        var input = CreateInput("abc");
        var host = new FakeTextInputHost();
        input.TextInputHost = host;

        EventDispatcher.DispatchDirect(new BlurEvent(), input);

        Assert.Equal(0, host.ClearCompositionCalls);
    }

    private sealed class FakeTextInputHost : ITextInputHost
    {
        public int SetInputAreaCalls;
        public int ClearCompositionCalls;
        public void SetInputArea(Rect caretRect, int cursorOffset) => SetInputAreaCalls++;
        public void ClearComposition() => ClearCompositionCalls++;
    }

    // ──── UTF-16 surrogate pairs (emojis) ────

    // U+1F600 GRINNING FACE (😀) = surrogate pair [D83D, DE00]
    // Using concatenation so no string escapes are needed.
    private const string Grin = "\uD83D\uDE00";
    private const string Heart = "\uD83D\uDC96";

    [Fact]
    public void Backspace_AcrossEmoji_RemovesWholePair()
    {
        var input = CreateInput($"a{Grin}");
        Assert.Equal(3, input.Text.Length); // 'a' + 2 surrogate halves
        input.CursorPosition = input.Text.Length;

        EventDispatcher.Dispatch(new KeyDownEvent { Key = KeyCode.Backspace }, input);

        Assert.Equal("a", input.Text);
        Assert.Equal(1, input.CursorPosition);
    }

    [Fact]
    public void Delete_AcrossEmoji_RemovesWholePair()
    {
        var input = CreateInput($"{Grin}b");
        input.CursorPosition = 0;

        EventDispatcher.Dispatch(new KeyDownEvent { Key = KeyCode.Delete }, input);

        Assert.Equal("b", input.Text);
        Assert.Equal(0, input.CursorPosition);
    }

    [Fact]
    public void ArrowLeft_OverEmoji_JumpsByPair()
    {
        var input = CreateInput($"a{Grin}"); // length 3
        input.CursorPosition = 3;

        EventDispatcher.Dispatch(new KeyDownEvent { Key = KeyCode.Left }, input);

        Assert.Equal(1, input.CursorPosition); // skipped both halves
    }

    [Fact]
    public void ArrowRight_OverEmoji_JumpsByPair()
    {
        var input = CreateInput($"a{Grin}b"); // length 4
        input.CursorPosition = 1;

        EventDispatcher.Dispatch(new KeyDownEvent { Key = KeyCode.Right }, input);

        Assert.Equal(3, input.CursorPosition); // past both halves, before 'b'
    }

    [Fact]
    public void CursorPosition_MidSurrogatePair_SnapsBack()
    {
        var input = CreateInput($"a{Grin}b");

        input.CursorPosition = 2; // between high and low surrogate

        Assert.Equal(1, input.CursorPosition); // snapped to start of pair
    }

    [Fact]
    public void BackspaceSecondLastEmoji_DoesNotCrashAndRemovesIt()
    {
        // Repro for the reported crash: paste several emojis, backspace second-last.
        var input = CreateInput($"{Grin}{Heart}{Grin}");
        Assert.Equal(6, input.Text.Length);
        input.CursorPosition = 4; // after the middle emoji

        EventDispatcher.Dispatch(new KeyDownEvent { Key = KeyCode.Backspace }, input);

        Assert.Equal($"{Grin}{Grin}", input.Text);
        Assert.Equal(2, input.CursorPosition);
    }
}
