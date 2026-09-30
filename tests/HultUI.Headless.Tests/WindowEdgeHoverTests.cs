using HultUI.Core.Builders;
using HultUI.Core.Layout;
using HultUI.Core.Styling;
using HultUI.Headless;
using Xunit;

namespace HultUI.Headless.Tests;

/// <summary>
/// The pointer leaving the window, and a cursor that changes under a still pointer: a
/// button at the window's edge must not stay lit once the pointer is outside, and a cursor
/// set by a style change (a web page reports the one it wants just after the move that
/// caused it) must show without waiting for the pointer to move again.
/// </summary>
public class WindowEdgeHoverTests
{
    [Fact]
    public void LeavingTheWindow_LeavesTheHoverChain()
    {
        using var session = HeadlessSession.Create(400, 300);
        int leaves = 0;
        var edge = Box.New().Width(100f).Height(40f).OnPointerLeave(_ => leaves++);
        session.Application.SetRoot(Flex.Column().AlignItems(AlignItems.FlexStart).Children(edge));
        session.PumpFrame();

        session.Window.MoveMouse(50, 20);
        Assert.True(edge.IsHovered);

        session.Window.MouseLeaveWindow();

        Assert.Equal(1, leaves);
        Assert.False(edge.IsHovered);
    }

    [Fact]
    public void LeavingTheWindow_DuringAPress_KeepsTheCapture()
    {
        using var session = HeadlessSession.Create(400, 300);
        int leaves = 0;
        var pad = Box.New().Width(100f).Height(40f)
            .OnPointerDown(e => e.CapturePointer())
            .OnPointerLeave(_ => leaves++);
        session.Application.SetRoot(Flex.Column().AlignItems(AlignItems.FlexStart).Children(pad));
        session.PumpFrame();

        session.Window.MouseDown(50, 20);
        session.Window.MouseLeaveWindow();

        Assert.Equal(0, leaves);                  // the press owns the pointer until release
    }

    [Fact]
    public void ACursorChangedUnderAStillPointer_ShowsAtTheNextFrame()
    {
        using var session = HeadlessSession.Create(400, 300);
        var target = Box.New().Width(100f).Height(40f);
        session.Application.SetRoot(Flex.Column().AlignItems(AlignItems.FlexStart).Children(target));
        session.PumpFrame();
        session.Window.MoveMouse(50, 20);
        Assert.Equal(CursorType.Default, session.Window.LastCursor);

        target.Cursor(CursorType.Pointer);
        target.MarkStyleDirty();
        session.PumpFrame();

        Assert.Equal(CursorType.Pointer, session.Window.LastCursor);
    }

    [Fact]
    public void ACursorChangeElsewhere_LeavesTheCursorAlone()
    {
        using var session = HeadlessSession.Create(400, 300);
        var hovered = Box.New().Width(100f).Height(40f).Cursor(CursorType.Text);
        var other = Box.New().Width(100f).Height(40f);
        session.Application.SetRoot(Flex.Column().AlignItems(AlignItems.FlexStart).Children(hovered, other));
        session.PumpFrame();
        session.Window.MoveMouse(50, 20);
        Assert.Equal(CursorType.Text, session.Window.LastCursor);

        other.Cursor(CursorType.Wait);
        other.MarkStyleDirty();
        session.PumpFrame();

        Assert.Equal(CursorType.Text, session.Window.LastCursor);
    }
}
