using HultUI.Core.Accessibility;
using HultUI.Core.Builders;
using HultUI.Core.Elements;
using HultUI.Core.Events;
using HultUI.Core.Primitives;
using Xunit;

namespace HultUI.Headless.Tests;

/// <summary>
/// <see cref="Window.OpenContextMenu(IReadOnlyList{ContextMenuItem}, Point, Action?)"/>: a menu
/// whose owner decides its entries after the click — a web page's engine does — and must know
/// when it closed, because a close with nothing chosen is an answer too.
/// </summary>
public class ContextMenuFromListTests
{
    private static IEnumerable<AccessibilityNode> Walk(AccessibilityNode? node)
    {
        if (node is null) yield break;
        yield return node;
        foreach (var child in node.Children)
            foreach (var d in Walk(child))
                yield return d;
    }

    private static HeadlessSession Session()
    {
        var session = HeadlessSession.Create(600, 400);
        session.Window.SetRoot(Box.New().FlexGrow(1));
        session.PumpFrame();
        return session;
    }

    private static void ClickItem(HeadlessSession session, string label)
    {
        var r = Walk(session.Window.AccessibilityTree).First(n => n.Name == label).BoundingRect;
        session.Window.Click(r.X + r.Width / 2f, r.Y + r.Height / 2f);
        session.PumpFrames(2);
    }

    [Fact]
    public void ChoosingAnItem_RunsItsAction_BeforeTheOwnerHearsTheMenuClosed()
    {
        using var session = Session();
        var order = new List<string>();

        session.Window.Window.OpenContextMenu([new ContextMenuItem { Label = "Copy", Action = () => order.Add("action") }],
            new Point(100, 100), onClosed: () => order.Add("closed"));
        session.PumpFrame();
        ClickItem(session, "Copy");

        // The menu closes itself before it runs the action; an owner told at that moment would
        // take every choice for a dismissal.
        Assert.Equal(["action", "closed"], order);
    }

    [Fact]
    public void AClickOutside_Escape_OrAnotherMenu_EachCloseItOnce()
    {
        using var session = Session();
        int closed = 0;
        ContextMenuItem[] items = [new() { Label = "Copy" }];

        session.Window.Window.OpenContextMenu(items, new Point(100, 100), onClosed: () => closed++);
        session.PumpFrame();
        session.Window.Click(500, 350);
        session.PumpFrames(2);
        Assert.Equal(1, closed);

        session.Window.Window.OpenContextMenu(items, new Point(100, 100), onClosed: () => closed++);
        session.PumpFrame();
        session.Window.PressKey(KeyCode.Escape);
        session.PumpFrames(2);
        Assert.Equal(2, closed);

        session.Window.Window.OpenContextMenu(items, new Point(100, 100), onClosed: () => closed++);
        session.PumpFrame();
        session.Window.Window.OpenContextMenu(items, new Point(300, 100));
        session.PumpFrames(2);
        Assert.Equal(3, closed);
        Assert.Contains("Copy", Walk(session.Window.AccessibilityTree).Select(n => n.Name)); // the second is open
    }

    [Fact]
    public void AnEmptyList_OpensNothing_ButStillSaysClosed()
    {
        using var session = Session();
        bool closed = false;

        session.Window.Window.OpenContextMenu([], new Point(100, 100), onClosed: () => closed = true);
        session.PumpFrame();

        Assert.True(closed);
    }
}
