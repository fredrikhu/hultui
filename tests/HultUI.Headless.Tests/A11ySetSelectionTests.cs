using HultUI.Core.Builders;
using HultUI.Core.Elements;
using HultUI.Core.Primitives;
using HultUI.Core.RichText;
using Xunit;

namespace HultUI.Headless.Tests;

/// <summary>
/// A reader's selection in a field (TalkBack's set-selection and its moves by character and word,
/// through the window's router): the offsets are the text the field offers a reader, made the
/// field's own, with no edit. The rich text editor's reader text has a <c>'\n'</c> between blocks
/// and a picture as its words, so its offsets are not the document's.
/// </summary>
public class A11ySetSelectionTests
{
    private static (HeadlessSession Session, AccessibilityActionRouter Router) Mount(Element field)
    {
        var session = HeadlessSession.Create(600, 400);
        session.Window.SetRoot(Box.New().Width(600).Height(400).Children(field));
        session.PumpFrame();
        return (session, new AccessibilityActionRouter(session.Window.Window));
    }

    [Fact]
    public void ATextInputsSelection_IsSet_AndNothingIsEdited()
    {
        var input = TextInput.New("hello world").Width(300);
        var (session, router) = Mount(input);
        using var _ = session;
        router.SetSelection(input.Id, 6, 11);
        router.DrainPendingActions();
        Assert.Equal((6, 11), (input.SelectionStart, input.SelectionEnd));
        Assert.Equal(11, input.CursorPosition);
        Assert.Equal("hello world", input.Text);
        Assert.False(input.CanUndo);

        router.SetSelection(input.Id, 99, 99);   // clamped
        router.DrainPendingActions();
        Assert.Equal(11, input.CursorPosition);
        Assert.False(input.HasSelection);
    }

    [Fact]
    public void ATextAreasCaret_IsPlaced_WithItsAnchor()
    {
        var area = TextArea.New("one\ntwo").Width(300).Height(100);
        var (session, router) = Mount(area);
        using var _ = session;
        router.SetSelection(area.Id, 6, 4);   // anchor after "tw", caret before it
        router.DrainPendingActions();
        Assert.Equal((4, 6), (area.SelectionStart, area.SelectionEnd));
        Assert.Equal(4, area.CursorPosition);
    }

    [Fact]
    public void TheRichTextEditorsSelection_IsAtTheReadersOffsets_PastAPicturesWords()
    {
        // Reader's text: "ab\nPicture\ncd" — a picture with no words of its own reads as "Picture".
        var editor = RichTextEditor.New(new RichDocument([
            RichBlock.Paragraph("ab"), RichBlock.Picture(new RichImage("x.png")), RichBlock.Paragraph("cd")])).Width(300).Height(200);
        var (session, router) = Mount(editor);
        using var _ = session;
        Assert.Equal("ab\nPicture\ncd", editor.AccessibleTextSource!.AccessibleText!.Text);

        router.SetSelection(editor.Id, 1, 12);   // from after "a" to after "c"
        router.DrainPendingActions();
        var selection = editor.State.Selection;
        Assert.Equal(new RichPosition(0, 1), selection.Anchor);
        Assert.Equal(new RichPosition(2, 1), selection.Focus);

        router.SetSelection(editor.Id, 5, 5);    // inside the picture's words: the picture
        router.DrainPendingActions();
        Assert.Equal(new RichPosition(1, 0), editor.State.Selection.Focus);
    }

    [Fact]
    public void ADisabledField_IsLeftAsItIs()
    {
        var input = TextInput.New("hello").Width(300);
        input.AccessibleEnabled = false;
        var (session, router) = Mount(input);
        using var _ = session;
        int before = input.CursorPosition;
        router.SetSelection(input.Id, 0, 2);
        router.DrainPendingActions();
        Assert.Equal(before, input.CursorPosition);
    }
}
