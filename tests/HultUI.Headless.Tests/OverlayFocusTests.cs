using HultUI.Core.Builders;
using HultUI.Core.Dialogs;
using HultUI.Core.Elements;
using Xunit;

namespace HultUI.Headless.Tests;

/// <summary>
/// The keyboard inside overlays: a dialog's field takes the focus when asked, what is typed goes
/// to it, and closing the dialog gives the focus back to what had it.
/// </summary>
public class OverlayFocusTests
{
    [Fact]
    public void AFieldInADialog_TakesTheKeyboard_AndClosingGivesItBack()
    {
        using var session = HeadlessSession.Create(400, 300);
        var behind = TextInput.New();
        var root = Flex.Column().Children(behind);
        session.Window.SetRoot(root);
        session.PumpFrame();
        behind.Focus();
        session.PumpFrame();

        TextInputElement? field = null;
        IDialogController<string>? dialog = null;
        _ = Dialog.Show<string>(root.OverlayHost!, controller =>
        {
            dialog = controller;
            field = TextInput.New();
            return Box.New().Width(200).Children(field);
        });
        field!.Focus(); // did nothing: the overlay had no focus manager
        session.PumpFrame();
        Assert.Same(field, session.Window.Window.FocusedElement);

        session.Window.TypeText("secret");
        Assert.Equal("secret", field.Text);
        Assert.Equal("", behind.Text);

        dialog!.Close("done");
        session.PumpFrame();
        Assert.Same(behind, session.Window.Window.FocusedElement);
        session.Window.TypeText("x");
        Assert.Equal("x", behind.Text);
    }

    [Fact]
    public void ClosingADialogHoldingTheFocus_WithNothingBefore_LeavesNoneOnWhatHasGone()
    {
        using var session = HeadlessSession.Create(400, 300);
        var root = Flex.Column().Children(TextInput.New());
        session.Window.SetRoot(root);
        session.PumpFrame();

        TextInputElement? field = null;
        IDialogController<string>? dialog = null;
        _ = Dialog.Show<string>(root.OverlayHost!, controller =>
        {
            dialog = controller;
            field = TextInput.New();
            return Box.New().Width(200).Children(field);
        });
        field!.Focus();
        session.PumpFrame();
        dialog!.Close("done");
        session.PumpFrame();

        Assert.Null(session.Window.Window.FocusedElement);
    }
}
