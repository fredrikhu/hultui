using HultUI.Core.Builders;
using HultUI.Core.Elements;
using HultUI.Core.Events;
using Xunit;
using TextB = HultUI.Core.Builders.Text;

namespace HultUI.Headless.Tests;

/// <summary>
/// A selectable label copies through the window's clipboard however it took the focus (plan 17
/// round 19). The window handed the clipboard to a label only on Tab and on a press, so one focused
/// by <see cref="Element.Focus"/> or by a reader's focus request copied nothing with Ctrl+C or the
/// menu's Copy: a reader user who moved to Junction's diff text or Meridian's response could not
/// copy it.
/// </summary>
public class LabelFocusClipboardTests
{
    private static (HeadlessSession Session, TextElement Label) Rig()
    {
        var session = HeadlessSession.Create(400, 200);
        session.Application.SetRoot(Flex.Column().Children(
            TextInput.New("a field").AutomationId("field"),
            TextB.New("selectable words").Selectable().AutomationId("label")));
        session.PumpFrames(2);
        var label = (TextElement)session.Window.FindByAutomationId("label")!.Element;
        Assert.Null(label.Clipboard);
        return (session, label);
    }

    private static void SelectAllAndCopy(HeadlessSession session, TextElement label)
    {
        Assert.Same(label, session.Window.Window.FocusedElement);
        session.Window.PressKey(KeyCode.A, KeyModifiers.Ctrl);
        session.PumpFrames(1);
        Assert.Equal("selectable words", label.SelectedText);
        session.Window.PressKey(KeyCode.C, KeyModifiers.Ctrl);
        session.PumpFrames(1);
    }

    [Fact]
    public void ALabelFocusedFromCode_CopiesWithCtrlC()
    {
        var (session, label) = Rig();
        using var _s = session;
        label.Focus();
        session.PumpFrames(1);

        SelectAllAndCopy(session, label);
        Assert.Equal("selectable words", session.Window.Clipboard.Text);
    }

    [Fact]
    public void ALabelFocusedByAReader_CopiesWithCtrlC()
    {
        var (session, label) = Rig();
        using var _s = session;
        session.Window.InstallActionRouter().FocusElement(label.Id);
        session.PumpFrames(2);

        SelectAllAndCopy(session, label);
        Assert.Equal("selectable words", session.Window.Clipboard.Text);
    }

    /// <summary>Tab still hands it over: the move goes through the same focus change.</summary>
    [Fact]
    public void ALabelReachedWithTab_CopiesWithCtrlC()
    {
        var (session, label) = Rig();
        using var _s = session;
        session.Window.FindByAutomationId("field")!.Element.Focus();
        session.PumpFrames(1);
        session.Window.PressKey(KeyCode.Tab);
        session.PumpFrames(1);

        SelectAllAndCopy(session, label);
        Assert.Equal("selectable words", session.Window.Clipboard.Text);
    }
}
