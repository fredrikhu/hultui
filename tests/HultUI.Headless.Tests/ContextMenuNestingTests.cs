using HultUI.Core.Builders;
using HultUI.Core.Elements;
using HultUI.Core.Events;
using HultUI.Core.Primitives;
using Xunit;

namespace HultUI.Headless.Tests;

/// <summary>
/// A context menu opens submenus at any depth, by hover, click, → and Enter, and ← and Escape close
/// one level. The window held one flyout: a third level never opened, its parent item did nothing
/// when clicked, and in a menu bar → on it jumped to the next menu. A flyout with no room on the
/// right opens on its parent's left.
/// </summary>
public class ContextMenuNestingTests
{
    private static (HeadlessSession Session, List<string> Chosen) Mount(int width = 600)
    {
        var session = HeadlessSession.Create(width, 400);
        session.Window.SetRoot(Box.New().Width(50f).Height(20f));
        session.PumpFrame();
        return (session, []);
    }

    private static ContextMenuItem[] Items(List<string> chosen) =>
    [
        new()
        {
            Label = "File",
            Children =
            [
                new() { Label = "New", Action = () => chosen.Add("New") },
                new()
                {
                    Label = "Recent",
                    Children =
                    [
                        new() { Label = "a.txt", Action = () => chosen.Add("a.txt") },
                        new() { Label = "b.txt", Action = () => chosen.Add("b.txt") },
                    ],
                },
            ],
        },
        new() { Label = "Close", Action = () => chosen.Add("Close") },
    ];

    private static List<ContextMenuElement> Menus(HeadlessSession session) =>
        session.Window.OverlayContents.OfType<ContextMenuElement>().ToList();

    /// <summary>Where row <paramref name="index"/> of <paramref name="menu"/> is in the window.</summary>
    private static (float X, float Y) Row(ContextMenuElement menu, int index)
    {
        var box = menu.WindowBorderBox;
        return (box.X + box.Width / 2, box.Y + menu.ItemTop(index) + 6);
    }

    [Fact]
    public void TheKeyboard_OpensEveryLevel_AndEnterChoosesInTheDeepest()
    {
        var (session, chosen) = Mount();
        using var _ = session;
        session.Window.Window.OpenContextMenu(Items(chosen), new Point(40, 40));
        session.PumpFrame();

        session.Window.PressKey(KeyCode.Down);    // File
        session.Window.PressKey(KeyCode.Right);   // its flyout, on New
        session.Window.PressKey(KeyCode.Down);    // Recent
        session.Window.PressKey(KeyCode.Right);   // its flyout, on a.txt
        session.PumpFrame();
        Assert.Equal(3, Menus(session).Count);

        session.Window.PressKey(KeyCode.Down);    // b.txt
        session.Window.PressKey(KeyCode.Enter);
        session.PumpFrame();

        Assert.Equal(["b.txt"], chosen);
        Assert.Empty(Menus(session));
    }

    [Fact]
    public void LeftAndEscape_CloseOneLevelEach()
    {
        var (session, chosen) = Mount();
        using var _ = session;
        session.Window.Window.OpenContextMenu(Items(chosen), new Point(40, 40));
        session.PumpFrame();
        foreach (var key in new[] { KeyCode.Down, KeyCode.Right, KeyCode.Down, KeyCode.Right })
            session.Window.PressKey(key);
        session.PumpFrame();
        Assert.Equal(3, Menus(session).Count);

        session.Window.PressKey(KeyCode.Left);
        Assert.Equal(2, Menus(session).Count);
        // ← left the keyboard in the flyout it came back to: → opens Recent's again.
        session.Window.PressKey(KeyCode.Right);
        Assert.Equal(3, Menus(session).Count);
        session.Window.PressKey(KeyCode.Escape);
        session.Window.PressKey(KeyCode.Escape);
        Assert.Single(Menus(session));
        session.Window.PressKey(KeyCode.Escape);
        Assert.Empty(Menus(session));
        Assert.Empty(chosen);
    }

    [Fact]
    public void ThePointer_OpensEveryLevelByHover_AndAClickChoosesInTheDeepest()
    {
        var (session, chosen) = Mount();
        using var _ = session;
        session.Window.Window.OpenContextMenu(Items(chosen), new Point(40, 40));
        session.PumpFrame();

        var (fx, fy) = Row(Menus(session)[0], 0);
        session.Window.MoveMouse(fx, fy);           // File
        session.PumpFrame();
        var (rx, ry) = Row(Menus(session)[1], 1);
        session.Window.MoveMouse(rx, ry);           // Recent
        session.PumpFrame();
        Assert.Equal(3, Menus(session).Count);

        var (bx, by) = Row(Menus(session)[2], 1);
        session.Window.MoveMouse(bx, by);
        session.Window.Click(bx, by);               // b.txt
        session.PumpFrame();

        Assert.Equal(["b.txt"], chosen);
        Assert.Empty(Menus(session));
    }

    /// <summary>A click on a parent item at the second level opens its flyout, as at the first.</summary>
    [Fact]
    public void AClickOnAParentItemInAFlyout_OpensItsFlyout()
    {
        var (session, chosen) = Mount();
        using var _ = session;
        session.Window.Window.OpenContextMenu(Items(chosen), new Point(40, 40));
        session.PumpFrame();
        session.Window.PressKey(KeyCode.Down);
        session.Window.PressKey(KeyCode.Right);
        session.PumpFrame();

        var (rx, ry) = Row(Menus(session)[1], 1);
        session.Window.Click(rx, ry);               // Recent
        session.PumpFrame();

        Assert.Equal(3, Menus(session).Count);
        Assert.Empty(chosen);
    }

    /// <summary>Hovering back over the menu's own items closes the flyouts deeper than it.</summary>
    [Fact]
    public void HoveringAnItemOfAnOuterMenu_ClosesTheFlyoutsInsideIt()
    {
        var (session, chosen) = Mount();
        using var _ = session;
        session.Window.Window.OpenContextMenu(Items(chosen), new Point(40, 40));
        session.PumpFrame();
        foreach (var key in new[] { KeyCode.Down, KeyCode.Right, KeyCode.Down, KeyCode.Right })
            session.Window.PressKey(key);
        session.PumpFrame();
        Assert.Equal(3, Menus(session).Count);

        var (cx, cy) = Row(Menus(session)[0], 1);
        session.Window.MoveMouse(cx, cy);           // Close: no children
        session.PumpFrame();

        Assert.Single(Menus(session));
    }

    /// <summary>With no room on the right, a flyout opens on its parent's left rather than being
    /// pushed back over the parent.</summary>
    [Fact]
    public void AFlyoutWithNoRoomOnTheRight_OpensOnItsParentsLeft()
    {
        var (session, chosen) = Mount(width: 400);
        using var _ = session;
        session.Window.Window.OpenContextMenu(Items(chosen), new Point(250, 40));
        session.PumpFrame();
        session.Window.PressKey(KeyCode.Down);
        session.Window.PressKey(KeyCode.Right);
        session.PumpFrame();

        var menus = Menus(session);
        var parent = menus[0].WindowBorderBox;
        var flyout = menus[1].WindowBorderBox;
        Assert.True(flyout.Right <= parent.X + 4.5f, $"flyout {flyout} should sit left of its parent {parent}");
    }
}
