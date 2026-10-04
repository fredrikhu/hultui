using HultUI.Core.Accessibility;
using HultUI.Core.Builders;
using HultUI.Core.Dialogs;
using HultUI.Core.Elements;
using HultUI.Core.Events;
using Xunit;

namespace HultUI.Headless.Tests;

/// <summary>
/// A modal dialog and the keyboard: it takes the keyboard when it opens, Escape closes it,
/// Tab cannot leave it, and nothing behind it hears a key while it is up. Before, a dialog
/// could only be closed with the pointer, Tab walked straight out to the page behind, and with
/// nothing focused the page's own shortcuts fired underneath the dialog.
/// </summary>
public class DialogKeyboardTests
{
    /// <summary>A page with two fields and a key handler at its root that counts what reaches it.</summary>
    private sealed class Page : System.IDisposable
    {
        public readonly HeadlessSession Session = HeadlessSession.Create(500, 400);
        public readonly TextInputElement First = TextInput.New();
        public readonly TextInputElement Second = TextInput.New();
        public readonly Element Root;
        public int KeysAtRoot;

        public Page()
        {
            Root = Flex.Column(gap: 8).Children(First, Second);
            Root.On<KeyDownEvent>(_ => KeysAtRoot++);
            Session.Window.SetRoot(Root);
            Session.PumpFrame();
        }

        public void Dispose() => Session.Dispose();

        public IOverlayHost Host => Root.OverlayHost!;
        public Element? Focused => Session.Window.Window.FocusedElement;

        public void Press(KeyCode key, KeyModifiers mods = KeyModifiers.None)
        {
            Session.Window.PressKey(key, mods);
            Session.PumpFrame();
        }
    }

    [Fact]
    public async System.Threading.Tasks.Task EscapeClosesTheDialogWithTheDefaultResult()
    {
        using var page = new Page();
        var answer = Dialog.Confirm(page.Host, "Delete?", "Gone for good.");
        page.Session.PumpFrame();

        page.Press(KeyCode.Escape);

        Assert.True(answer.IsCompleted);
        Assert.False(await answer);
        Assert.Null(page.Session.Window.FindByRole(AccessibilityRole.Dialog));
    }

    [Fact]
    public void EscapeCanBeTurnedOff()
    {
        using var page = new Page();
        var answer = Dialog.Confirm(page.Host, "Delete?", "Gone for good.",
            options: new DialogOptions { DismissOnEscape = false });
        page.Session.PumpFrame();

        page.Press(KeyCode.Escape);

        Assert.False(answer.IsCompleted);
    }

    /// <summary>Escape means the innermost thing first: an open dropdown list closes, and only
    /// the next Escape closes the dialog.</summary>
    [Fact]
    public void EscapeClosesAnOpenDropdownBeforeTheDialog()
    {
        using var page = new Page();
        DropdownElement? dropdown = null;
        var shown = Dialog.Show<string>(page.Host, _ =>
        {
            dropdown = Dropdown.New(["One", "Two", "Three"], selectedIndex: 0);
            return Box.New().Width(240).Children(dropdown);
        });
        page.Session.PumpFrame();
        Assert.Same(dropdown, page.Focused);
        page.Press(KeyCode.Down, KeyModifiers.Alt);
        Assert.True(dropdown!.IsOpen);

        page.Press(KeyCode.Escape);
        Assert.False(dropdown.IsOpen);
        Assert.False(shown.IsCompleted);

        page.Press(KeyCode.Escape);
        Assert.True(shown.IsCompleted);
    }

    /// <summary>It opens holding the keyboard — its first control — and gives it back to what
    /// had it when it closes.</summary>
    [Fact]
    public void TheDialogTakesTheKeyboardAndGivesItBack()
    {
        using var page = new Page();
        page.First.Focus();
        page.Session.PumpFrame();

        _ = Dialog.Confirm(page.Host, "Delete?", "Gone for good.");
        page.Session.PumpFrame();
        var cancel = Assert.IsType<ButtonElement>(page.Focused);
        Assert.Equal("Cancel", cancel.Label);

        page.Session.Window.TypeText("x");
        Assert.Equal("", page.First.Text);

        page.Press(KeyCode.Escape);
        Assert.Same(page.First, page.Focused);
    }

    [Fact]
    public void TabAndShiftTabStayInsideTheDialog()
    {
        using var page = new Page();
        TextInputElement? a = null, b = null;
        _ = Dialog.Show<string>(page.Host, _ =>
        {
            a = TextInput.New();
            b = TextInput.New();
            return Box.New().Width(240).Children(Flex.Column(gap: 8).Children(a, b));
        });
        page.Session.PumpFrame();
        Assert.Same(a, page.Focused);

        page.Press(KeyCode.Tab);
        Assert.Same(b, page.Focused);
        page.Press(KeyCode.Tab);
        Assert.Same(a, page.Focused);              // round again, not out to the page
        page.Press(KeyCode.Tab, KeyModifiers.Shift);
        Assert.Same(b, page.Focused);
    }

    /// <summary>A dialog with nothing to focus — a message and no buttons — still takes the
    /// keyboard, so Escape still closes it and the page still hears nothing.</summary>
    [Fact]
    public void ADialogWithNothingToFocusStillTakesTheKeyboard()
    {
        using var page = new Page();
        page.First.Focus();
        var shown = Dialog.Show<string>(page.Host, _ => Box.New().Width(240).Children(TextElement.New("Working…")));
        page.Session.PumpFrame();

        page.Press(KeyCode.K, KeyModifiers.Ctrl);
        Assert.Equal(0, page.KeysAtRoot);

        page.Press(KeyCode.Escape);
        Assert.True(shown.IsCompleted);
    }

    /// <summary>Focus moved behind the dialog from code — a background load finishing and
    /// focusing its result — must not hand the page the keyboard while the dialog is up.</summary>
    [Fact]
    public void FocusMovedBehindTheDialogDoesNotGiveThePageTheKeys()
    {
        using var page = new Page();
        var shown = Dialog.Confirm(page.Host, "Delete?", "Gone for good.");
        page.Session.PumpFrame();

        page.Second.Focus();
        page.Session.PumpFrame();
        page.Session.Window.TypeText("x");
        page.Press(KeyCode.K, KeyModifiers.Ctrl);

        Assert.Equal("", page.Second.Text);
        Assert.Equal(0, page.KeysAtRoot);

        page.Press(KeyCode.Escape);                   // and the dialog still has its Escape
        Assert.True(shown.IsCompleted);
    }

    [Fact]
    public void TheDialogIsAnnouncedAsADialogNamedByItsTitle()
    {
        using var page = new Page();
        _ = Dialog.Confirm(page.Host, "Delete branch?", "Gone for good.");
        page.Session.PumpFrame();

        Assert.NotNull(page.Session.Window.FindByRole(AccessibilityRole.Dialog, "Delete branch?"));
    }

    /// <summary>Not only dialogs: Tab from a field in any overlay cycles in that overlay. It used
    /// to jump to the first control of the page underneath.</summary>
    [Fact]
    public void TabCyclesInsideANonModalOverlayHoldingTheFocus()
    {
        using var page = new Page();
        var a = TextInput.New();
        var b = TextInput.New();
        page.Host.ShowOverlay(Box.New().Width(200).Children(Flex.Column(gap: 8).Children(a, b)), 250, 10);
        page.Session.PumpFrame();
        a.Focus();

        page.Press(KeyCode.Tab);
        Assert.Same(b, page.Focused);
        page.Press(KeyCode.Tab);
        Assert.Same(a, page.Focused);
    }
}
