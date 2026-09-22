using HultUI.Core.Accessibility;
using HultUI.Core.Builders;
using HultUI.Core.Elements;
using HultUI.Core.Primitives;
using HultUI.Headless;
using Xunit;

namespace HultUI.Headless.Tests;

/// <summary>
/// What is open over a window is part of that window: a context menu, a dropdown list or a
/// dialog is usually the thing that just took the keyboard, and a snapshot without it hands
/// assistive tech a window whose contents do not match what the user is being asked about.
/// </summary>
public class OverlayAccessibilityTests
{
    private static IEnumerable<AccessibilityNode> Walk(AccessibilityNode node)
    {
        yield return node;
        foreach (var child in node.Children)
            foreach (var descendant in Walk(child))
                yield return descendant;
    }

    [Fact]
    public void AnOpenMenuIsInTheTreeWithItsItems()
    {
        using var session = HeadlessSession.Create(600, 400);
        var target = Box.New().Width(120).Height(40)
            .AccessibleName("Target").AutomationId("target")
            .ContextMenu(() => [
                new ContextMenuItem { Label = "Play" },
                ContextMenuItem.Separator(),
                new ContextMenuItem { Label = "Mark watched" },
            ]);
        session.Window.SetRoot(Box.New().FlexGrow(1).Children(target));
        session.PumpFrame();

        Assert.DoesNotContain(Walk(session.Window.AccessibilityTree), n => n.Name == "Play");

        session.Window.Window.OpenContextMenu(target, new Point(60, 20));
        session.PumpFrame();

        var names = Walk(session.Window.AccessibilityTree).Select(n => n.Name).ToList();
        Assert.Contains("Play", names);
        Assert.Contains("Mark watched", names);
    }

    [Fact]
    public void MenuItemsReportWhereTheyAreOnScreen()
    {
        using var session = HeadlessSession.Create(600, 400);
        var target = Box.New().Width(120).Height(40).AccessibleName("Target")
            .ContextMenu(() => [new ContextMenuItem { Label = "Play" }]);
        session.Window.SetRoot(Box.New().FlexGrow(1).Children(target));
        session.PumpFrame();

        // Open it well away from the window's corner: overlay content is positioned at
        // render time, so without the origin the node would report bounds near (0,0).
        session.Window.Window.OpenContextMenu(target, new Point(200, 150));
        session.PumpFrame();

        var item = Walk(session.Window.AccessibilityTree).First(n => n.Name == "Play");
        Assert.True(item.BoundingRect.X >= 190, $"item is at x={item.BoundingRect.X}, not near the menu");
        Assert.True(item.BoundingRect.Y >= 140, $"item is at y={item.BoundingRect.Y}, not near the menu");
    }

    [Fact]
    public void ClickingAMenuItemWhereItSaysItIsRunsIt()
    {
        using var session = HeadlessSession.Create(600, 400);
        bool ran = false;
        var target = Box.New().Width(120).Height(40).AccessibleName("Target")
            .ContextMenu(() => [new ContextMenuItem { Label = "Play", Action = () => ran = true }]);
        session.Window.SetRoot(Box.New().FlexGrow(1).Children(target));
        session.PumpFrame();
        session.Window.Window.OpenContextMenu(target, new Point(200, 150));
        session.PumpFrame();

        var rect = Walk(session.Window.AccessibilityTree).First(n => n.Name == "Play").BoundingRect;
        session.Window.Click(rect.X + rect.Width / 2f, rect.Y + rect.Height / 2f);
        session.PumpFrame();

        Assert.True(ran, "the point the snapshot reported did not hit the item");
    }

    [Fact]
    public void ATooltipStaysOutOfTheTree()
    {
        using var session = HeadlessSession.Create(600, 400);
        var target = Box.New().Width(120).Height(40).AccessibleName("Target").Tooltip("Explains the target");
        session.Window.SetRoot(Box.New().FlexGrow(1).Children(target));
        session.PumpFrame();

        session.Window.MoveMouse(60, 20);
        session.PumpFrame(TimeSpan.FromSeconds(2));

        // The tooltip belongs to the element it describes; as a sibling node it would be
        // read twice.
        Assert.DoesNotContain(Walk(session.Window.AccessibilityTree), n => n.Name == "Explains the target");
    }
}
