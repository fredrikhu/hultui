using HultUI.Core.Elements;
using HultUI.Core.Events;
using HultUI.Core.Layout;
using HultUI.Core.Platform;
using HultUI.Core.Styling;
using Xunit;

namespace HultUI.Core.Tests.Elements;

/// <summary>
/// Ctrl+Z and Ctrl+Y in <see cref="TextInputElement"/> and <see cref="TextAreaElement"/>: that
/// every edit path records, that the keys and the context menu reach it, and that a value set
/// from code forgets a history describing other text. The grouping rules themselves are in
/// <see cref="TextUndoHistoryTests"/>.
/// </summary>
public class TextUndoTests
{
    private readonly LayoutEngine _layout = new(new StubMeasureContext());

    private T Mount<T>(T field) where T : BoxElement
    {
        field.Box.Width = 200;
        field.Box.Height = 100;
        var container = BoxElement.New();
        container.Box.Width = 400;
        container.Box.Height = 400;
        container.AddChild(field);
        StyleResolver.Resolve(container, Theme.Light.ToResolvedStyle());
        _layout.PerformLayout(container, 400, 400);
        EventDispatcher.DispatchDirect(new FocusEvent(), field);
        return field;
    }

    private TextInputElement Input(string text = "") => Mount(TextInputElement.New(text));
    private TextAreaElement Area(string text = "") => Mount(TextAreaElement.New(text));

    private static void Type(Element field, string text)
    {
        foreach (char c in text)
            EventDispatcher.Dispatch(new TextInputEvent { Text = c.ToString() }, field);
    }

    private static void Key(Element field, KeyCode key, KeyModifiers mods = KeyModifiers.None, int layoutKey = 0) =>
        EventDispatcher.Dispatch(new KeyDownEvent { Key = key, Modifiers = mods, LayoutKey = layoutKey }, field);

    private static void CtrlZ(Element field) => Key(field, KeyCode.Z, KeyModifiers.Ctrl);

    // ──── TextInputElement ────

    [Fact]
    public void CtrlZTakesTypingBackAWordAtATime()
    {
        var input = Input();
        Type(input, "hello world");

        CtrlZ(input);
        Assert.Equal("hello ", input.Text);
        CtrlZ(input);
        Assert.Equal("", input.Text);
        Assert.False(input.CanUndo);
    }

    [Fact]
    public void CtrlYAndCtrlShiftZBothRedo()
    {
        var input = Input();
        Type(input, "one two");
        CtrlZ(input);
        CtrlZ(input);

        Key(input, KeyCode.Y, KeyModifiers.Ctrl);
        Assert.Equal("one ", input.Text);
        Key(input, KeyCode.Z, KeyModifiers.Ctrl | KeyModifiers.Shift);
        Assert.Equal("one two", input.Text);
        Assert.Equal(7, input.CursorPosition);
    }

    [Fact]
    public void UndoBringsBackTheSelectionThatWasTypedOver()
    {
        var input = Input("Hello World");
        input.CursorPosition = 6;
        for (int i = 0; i < 5; i++)
            Key(input, KeyCode.Right, KeyModifiers.Shift);
        Type(input, "X");
        Assert.Equal("Hello X", input.Text);

        CtrlZ(input);

        Assert.Equal("Hello World", input.Text);
        Assert.Equal("World", input.SelectedText);
    }

    [Fact]
    public void ARunOfBackspacesUndoesInOneStep()
    {
        var input = Input("Hello");
        for (int i = 0; i < 3; i++)
            Key(input, KeyCode.Backspace);
        Assert.Equal("He", input.Text);

        CtrlZ(input);

        Assert.Equal("Hello", input.Text);
        Assert.Equal(5, input.CursorPosition);
        Assert.False(input.CanUndo);
    }

    [Fact]
    public void ForwardDeleteAndCtrlBackspaceRecordToo()
    {
        var input = Input("alpha beta");
        Key(input, KeyCode.Backspace, KeyModifiers.Ctrl);
        Assert.Equal("alpha ", input.Text);
        input.CursorPosition = 0;
        Key(input, KeyCode.Delete);
        Assert.Equal("lpha ", input.Text);

        CtrlZ(input);
        Assert.Equal("alpha ", input.Text);
        CtrlZ(input);
        Assert.Equal("alpha beta", input.Text);
    }

    [Fact]
    public void PasteAndCutAreStepsOfTheirOwn()
    {
        var clipboard = new StubClipboard { Text = "XY" };
        var input = Input();
        input.Clipboard = clipboard;
        Type(input, "ab");
        Key(input, KeyCode.V, KeyModifiers.Ctrl);
        Type(input, "c");
        Assert.Equal("abXYc", input.Text);

        CtrlZ(input);
        Assert.Equal("abXY", input.Text);
        CtrlZ(input);
        Assert.Equal("ab", input.Text);

        Key(input, KeyCode.A, KeyModifiers.Ctrl);
        Key(input, KeyCode.X, KeyModifiers.Ctrl);
        Assert.Equal("", input.Text);
        CtrlZ(input);
        Assert.Equal("ab", input.Text);
    }

    [Fact]
    public void MovingTheCaretEndsATypingRun()
    {
        var input = Input();
        Type(input, "ab");
        Key(input, KeyCode.Left);
        Key(input, KeyCode.Right);
        Type(input, "cd");

        CtrlZ(input);

        Assert.Equal("ab", input.Text);
    }

    [Fact]
    public void ANewEditDiscardsWhatCouldHaveBeenRedone()
    {
        var input = Input();
        Type(input, "abc");
        CtrlZ(input);
        Type(input, "x");

        Assert.False(input.CanRedo);
        Key(input, KeyCode.Y, KeyModifiers.Ctrl);
        Assert.Equal("x", input.Text);
    }

    [Fact]
    public void UndoAndRedoReportTheValue()
    {
        var input = Input();
        var reported = new List<string>();
        input.TextChanged = reported.Add;
        Type(input, "ab");
        reported.Clear();

        CtrlZ(input);
        Key(input, KeyCode.Y, KeyModifiers.Ctrl);

        Assert.Equal(new[] { "", "ab" }, reported);
    }

    /// <summary>A value set from code — a field cleared after its form was submitted — has
    /// nothing to do with the offsets the history recorded.</summary>
    [Fact]
    public void AValueSetFromCodeForgetsTheHistory()
    {
        var input = Input();
        Type(input, "abc");

        input.Text = "zzz";
        CtrlZ(input);

        Assert.Equal("zzz", input.Text);
        Assert.False(input.CanUndo);
    }

    /// <summary>The rebuild every keystroke causes in a controlled field hands the element the
    /// value it already holds. That must not count as a value set from code, or the history
    /// would be forgotten one keystroke after it was written.</summary>
    [Fact]
    public void APatchWithTheSameValueKeepsTheHistory()
    {
        var input = Input();
        Type(input, "abc");

        input.PatchFrom(TextInputElement.New("abc"));
        CtrlZ(input);

        Assert.Equal("", input.Text);
    }

    /// <summary>On a German keyboard the key <see cref="KeyCode"/> calls Y is printed Z. Matched
    /// by position, Ctrl+Z there would redo and Ctrl+Y would undo.</summary>
    [Fact]
    public void TheShortcutsFollowTheLettersOnTheKeys()
    {
        var input = Input();
        Type(input, "one two");

        Key(input, KeyCode.Y, KeyModifiers.Ctrl, layoutKey: 'z');
        Assert.Equal("one ", input.Text);
        Key(input, KeyCode.Z, KeyModifiers.Ctrl, layoutKey: 'y');
        Assert.Equal("one two", input.Text);

        // French: the key at A's position is printed Q, and selects nothing.
        Key(input, KeyCode.A, KeyModifiers.Ctrl, layoutKey: 'q');
        Assert.False(input.HasSelection);
        Key(input, KeyCode.Q, KeyModifiers.Ctrl, layoutKey: 'a');
        Assert.Equal("one two", input.SelectedText);
    }

    [Fact]
    public void UndoWaitsWhileAnInputMethodIsComposing()
    {
        var input = Input();
        Type(input, "ab");
        EventDispatcher.Dispatch(new TextEditingEvent { Text = "に" }, input);

        CtrlZ(input);

        Assert.Equal("ab", input.Text);
        Assert.True(input.IsComposing);
    }

    [Fact]
    public void TheContextMenuOffersUndoAndRedo()
    {
        var input = Input();
        Assert.True(Item(input, "Undo").IsDisabled);

        Type(input, "abc");
        var undo = Item(input, "Undo");
        Assert.False(undo.IsDisabled);
        undo.Action!();
        Assert.Equal("", input.Text);

        var redo = Item(input, "Redo");
        Assert.False(redo.IsDisabled);
        redo.Action!();
        Assert.Equal("abc", input.Text);
    }

    // ──── TextAreaElement ────

    [Fact]
    public void AnAreaUndoesTypingAcrossLinesAWordAtATime()
    {
        var area = Area();
        Type(area, "one");
        Key(area, KeyCode.Enter);
        Type(area, "two");

        CtrlZ(area);
        Assert.Equal("one\n", area.Text);
        CtrlZ(area);
        Assert.Equal("", area.Text);

        Key(area, KeyCode.Y, KeyModifiers.Ctrl);
        Key(area, KeyCode.Y, KeyModifiers.Ctrl);
        Assert.Equal("one\ntwo", area.Text);
    }

    [Fact]
    public void AnAreaRecordsPasteAndDeletes()
    {
        var area = Area("abc");
        area.Clipboard = new StubClipboard { Text = "\nmore" };
        Key(area, KeyCode.V, KeyModifiers.Ctrl);
        Key(area, KeyCode.Backspace);
        Key(area, KeyCode.Backspace);

        CtrlZ(area);
        Assert.Equal("abc\nmore", area.Text);
        CtrlZ(area);
        Assert.Equal("abc", area.Text);
    }

    /// <summary>The wrapped rows are cached against the text; an undo that changed the text
    /// without dropping them would lay out the old rows.</summary>
    [Fact]
    public void AnUndoReWrapsTheArea()
    {
        var area = Area();
        area.Clipboard = new StubClipboard { Text = string.Join(' ', Enumerable.Repeat("word", 40)) };
        Key(area, KeyCode.V, KeyModifiers.Ctrl);
        float width = area.Layout.AbsoluteContentBox.Width;
        Assert.True(area.GetLineStarts(width, area.ResolvedInherited).Count > 1);

        CtrlZ(area);

        Assert.Single(area.GetLineStarts(width, area.ResolvedInherited));
    }

    [Fact]
    public void AReadOnlyAreaDoesNotUndo()
    {
        var area = Area();
        Type(area, "abc");
        area.ReadOnly = true;

        CtrlZ(area);

        Assert.Equal("abc", area.Text);
        Assert.False(area.CanUndo);
        Assert.DoesNotContain(area.ContextMenuItems!(), i => i.Label == "Undo");

        area.ReadOnly = false;
        CtrlZ(area);
        Assert.Equal("", area.Text);
    }

    [Fact]
    public void AnAreaValueSetFromCodeForgetsTheHistory()
    {
        var area = Area();
        Type(area, "abc");

        area.Text = "zzz";
        CtrlZ(area);

        Assert.Equal("zzz", area.Text);
    }

    [Fact]
    public void TheAreaContextMenuOffersUndo()
    {
        var area = Area();
        Type(area, "abc");

        Item(area, "Undo").Action!();

        Assert.Equal("", area.Text);
        Assert.False(Item(area, "Redo").IsDisabled);
    }

    private static ContextMenuItem Item(Element field, string label) =>
        field.ContextMenuItems!().Single(i => i.Label == label);

    private sealed class StubClipboard : IClipboard
    {
        public string? Text { get; set; }
        public string? GetText() => Text;
        public void SetText(string text) => Text = text;
        public bool HasFormat(string mimeType) => false;
        public byte[]? GetData(string mimeType) => null;
        public void SetData(IReadOnlyDictionary<string, byte[]> data) => throw new NotSupportedException();
        public void Clear() => Text = null;
    }
}
