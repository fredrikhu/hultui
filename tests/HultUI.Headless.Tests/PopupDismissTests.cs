using HultUI.Core.Builders;
using HultUI.Core.Elements;
using HultUI.Core.Primitives;
using Xunit;

namespace HultUI.Headless.Tests;

/// <summary>
/// Light-dismiss popups (a context menu, a dropdown's list, a popover) close on the presses the
/// window never sees, as native menus do, and on a press elsewhere in a dialog. Dismissal ran
/// only on a press in the window's own client area: a menu stayed open over a window the user had
/// left for another application, or whose title bar they pressed; and a context menu or a
/// dropdown's list opened from a dialog stayed open while the user pressed elsewhere in it.
/// </summary>
public class PopupDismissTests
{
    private static readonly ContextMenuItem[] Items = [new() { Label = "Copy", Action = () => { } }];

    private static HeadlessSession Mount()
    {
        var session = HeadlessSession.Create(500, 400);
        session.Window.SetRoot(Flex.Column().Children(Box.New().Width(50f).Height(20f)));
        session.PumpFrame();
        return session;
    }

    [Fact]
    public void LosingTheForeground_ClosesTheWindowsPopups_ButNotItsDialogs()
    {
        using var session = Mount();
        var window = session.Window.Window;
        int deactivated = 0;
        window.Deactivated += () => deactivated++;
        window.ShowOverlay(Box.New().Width(40f).Height(40f), 300, 300, dismissOnOutsideClick: false);
        window.OpenContextMenu(Items, new Point(100, 100));
        session.PumpFrame();
        Assert.Equal(2, session.Window.OverlayContents.Count);

        session.Window.Deactivate();
        session.PumpFrame();

        Assert.Single(session.Window.OverlayContents);   // the one that does not light-dismiss
        Assert.Equal(1, deactivated);
    }

    [Fact]
    public void APressOnTheFrame_ClosesThePopups()
    {
        using var session = Mount();
        var window = session.Window.Window;
        var dropdown = Dropdown.New(["a", "b"], 0);
        session.Window.SetRoot(Box.New().Padding(new Thickness(40)).Children(dropdown));
        session.PumpFrame();
        session.Window.Click(dropdown);
        session.PumpFrame();
        Assert.True(dropdown.IsOpen);

        session.Window.PressFrame();
        session.PumpFrame();

        Assert.False(dropdown.IsOpen);
    }

    /// <summary>Into the DevTools inspector the window's popups stay, so a menu can be inspected
    /// while it is open.</summary>
    [Fact]
    public void LosingTheForegroundToTheInspector_KeepsThePopups()
    {
        using var session = Mount();
        var window = session.Window.Window;
        session.Application.OpenInspector(window);
        window.OpenContextMenu(Items, new Point(100, 100));
        session.PumpFrame();

        session.Window.Deactivate();
        session.PumpFrame();

        Assert.Single(session.Window.OverlayContents);
        session.Application.CloseInspector();
    }

    /// <summary>A dialog is a page of its own: a press in it outside a context menu opened from it
    /// closes the menu, as a press in the page does, and the dialog stays.</summary>
    [Fact]
    public void APressInADialog_ClosesAMenuOpenedFromIt()
    {
        using var session = Mount();
        var window = session.Window.Window;
        var spot = Box.New().Width(60f).Height(30f);
        var dialog = Box.New().Width(240f).Height(160f).Padding(new Thickness(20)).Children(spot);
        window.ShowOverlay(dialog, 100, 100, dismissOnOutsideClick: false, modal: true);
        session.PumpFrame();
        window.OpenContextMenu(Items, new Point(250, 220));
        session.PumpFrame();
        Assert.Equal(2, session.Window.OverlayContents.Count);

        session.Window.Click(spot);
        session.PumpFrame();

        Assert.Single(session.Window.OverlayContents);
        Assert.Same(dialog, session.Window.OverlayContents[0]);
    }

    /// <summary>A press in a menu (no dialog) still leaves the overlays above it that are not
    /// anchored: a context menu's flyout stays while its own rows are pressed.</summary>
    [Fact]
    public void APressInAPopover_StillLeavesAnUnanchoredOverlayAboveIt()
    {
        using var session = Mount();
        var window = session.Window.Window;
        var spot = Box.New().Width(60f).Height(30f);
        var popover = Box.New().Width(200f).Height(120f).Padding(new Thickness(20)).Children(spot);
        window.ShowOverlay(popover, 100, 100);
        session.PumpFrame();
        window.ShowOverlay(Box.New().Width(40f).Height(40f), 350, 300);
        session.PumpFrame();

        session.Window.Click(spot);
        session.PumpFrame();

        Assert.Equal(2, session.Window.OverlayContents.Count);
    }

    /// <summary>A popover closes when its window loses the foreground (bug B06).</summary>
    [Fact]
    public void AnOpenPopover_Closes_WhenTheWindowLosesFocus()
    {
        using var session = HeadlessSession.Create(width: 400, height: 300);
        var filters = new Popover(() => TextElement.New("Filters"));
        var anchor = Button.New("Filters").OnClick(e => filters.Toggle(e.CurrentTarget));
        session.Window.SetRoot(Flex.Column(gap: 8).Padding(20f).Children(anchor));
        session.PumpFrame();
        session.Window.GetByText("Filters").Click();
        session.PumpFrame();
        Assert.True(filters.IsOpen);

        session.Window.Deactivate();   // SDL_EVENT_WINDOW_FOCUS_LOST
        session.PumpFrame();

        Assert.False(filters.IsOpen);
    }

    /// <summary>A dropdown's list closed by the window losing the foreground had no press to
    /// swallow, so the next click on the dropdown opens it again.</summary>
    [Fact]
    public void ADropdownClosedByLosingTheForeground_OpensOnTheNextClick()
    {
        using var session = Mount();
        var dropdown = Dropdown.New(["a", "b"], 0);
        session.Window.SetRoot(Box.New().Padding(new Thickness(40)).Children(dropdown));
        session.PumpFrame();
        session.Window.Click(dropdown);
        session.PumpFrame();
        Assert.True(dropdown.IsOpen);

        session.Window.Deactivate();
        session.PumpFrame();
        Assert.False(dropdown.IsOpen);

        session.Window.Click(dropdown);
        session.PumpFrame();
        Assert.True(dropdown.IsOpen);
    }

    /// <summary>The same for a date picker's calendar.</summary>
    [Fact]
    public void ADatePickerClosedByLosingTheForeground_OpensOnTheNextClick()
    {
        using var session = Mount();
        var picker = DatePickerElement.New();
        session.Window.SetRoot(Box.New().Padding(new Thickness(40)).Children(picker));
        session.PumpFrame();
        session.Window.Click(picker);
        session.PumpFrame();
        Assert.True(picker.IsOpen);

        session.Window.Deactivate();
        session.PumpFrame();
        Assert.False(picker.IsOpen);

        session.Window.Click(picker);
        session.PumpFrame();
        Assert.True(picker.IsOpen);
    }
}
