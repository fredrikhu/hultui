using HultUI.Core.Builders;
using HultUI.Core.Elements;
using Xunit;

namespace HultUI.Headless.Tests;

/// <summary>
/// Tab traversal wraps in a window, because there is nowhere else for focus to go, and
/// stops at the ends when a host asks it to — that is how an embedded tree hands focus
/// back to whatever comes after it in the host's own tab order rather than trapping it.
/// </summary>
public class FocusBoundaryTests
{
    private static HeadlessSession WithThreeButtons(out Element[] buttons)
    {
        var session = HeadlessSession.Create();
        var made = new Element[]
        {
            Button.New("one"),
            Button.New("two"),
            Button.New("three"),
        };
        buttons = made;
        session.Window.SetRoot(Flex.Column().Children(made));
        session.PumpFrame();
        return session;
    }

    [Fact]
    public void Wrapping_IsTheWindowBehaviour()
    {
        using var session = WithThreeButtons(out var buttons);
        var window = session.Window.Window;

        Assert.True(window.MoveFocus(forward: true));
        Assert.Same(buttons[0], window.FocusedElement);
        Assert.True(window.MoveFocus(forward: true));
        Assert.True(window.MoveFocus(forward: true));
        Assert.Same(buttons[2], window.FocusedElement);

        // Past the last: back to the first, and it reports that it moved.
        Assert.True(window.MoveFocus(forward: true));
        Assert.Same(buttons[0], window.FocusedElement);
    }

    [Fact]
    public void WithoutWrapping_TheEndReportsThatFocusDidNotMove()
    {
        using var session = WithThreeButtons(out var buttons);
        var window = session.Window.Window;

        window.MoveFocus(forward: true);
        window.MoveFocus(forward: true);
        window.MoveFocus(forward: true);
        Assert.Same(buttons[2], window.FocusedElement);

        // The host's cue to let Tab leave the control.
        Assert.False(window.MoveFocus(forward: true, wrap: false));

        // And focus stays put rather than silently jumping to the start.
        Assert.Same(buttons[2], window.FocusedElement);
    }

    [Fact]
    public void WithoutWrapping_TheStartStopsShiftTabToo()
    {
        using var session = WithThreeButtons(out var buttons);
        var window = session.Window.Window;

        window.MoveFocus(forward: true);
        Assert.Same(buttons[0], window.FocusedElement);

        Assert.False(window.MoveFocus(forward: false, wrap: false));
        Assert.Same(buttons[0], window.FocusedElement);
    }

    [Fact]
    public void FocusEdge_LandsOnEitherEnd_ForAHostHandingFocusIn()
    {
        using var session = WithThreeButtons(out var buttons);
        var window = session.Window.Window;

        Assert.True(window.FocusEdge(first: true));
        Assert.Same(buttons[0], window.FocusedElement);

        // Shift+Tab into the control should arrive at the last element, not the first.
        Assert.True(window.FocusEdge(first: false));
        Assert.Same(buttons[2], window.FocusedElement);
    }

    [Fact]
    public void ClearFocus_LeavesNothingFocused()
    {
        using var session = WithThreeButtons(out _);
        var window = session.Window.Window;

        window.FocusEdge(first: true);
        Assert.NotNull(window.FocusedElement);

        window.ClearFocus();
        Assert.Null(window.FocusedElement);
    }

    [Fact]
    public void NothingFocusable_ReportsNoMovement()
    {
        using var session = HeadlessSession.Create();
        session.Window.SetRoot(Box.New().Children(HultUI.Core.Builders.Text.New("just a label")));
        session.PumpFrame();
        var window = session.Window.Window;

        Assert.False(window.MoveFocus(forward: true));
        Assert.False(window.FocusEdge(first: true));
    }
}
