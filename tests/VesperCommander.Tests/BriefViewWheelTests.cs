using HultUI.Core.Elements;
using HultUI.Core.Events;
using HultUI.Core.Reactivity;
using Xunit;

namespace VesperCommander.Tests;

/// <summary>
/// The brief view lays a folder out in columns and scrolls sideways through them. A mouse
/// wheel did nothing there: the view had no vertical range to use it on, and before that a
/// sideways scroll also never reached the handler that realizes the next columns. Now the
/// vertical wheel moves through the columns, the way Windows' list view does, and the
/// columns it reaches are built.
/// </summary>
public class BriefViewWheelTests
{
    [Fact]
    public void TheWheelMovesThroughTheColumnsAndBuildsTheOnesItReaches()
    {
        using var t = new DiffViewTests.Fixture();
        for (int i = 0; i < 400; i++)
            t.WriteLeft($"file{i:D3}.txt", "x");
        t.Open();
        t.Press(KeyCode.F2, KeyModifiers.Ctrl);     // view: Brief
        t.Session.PumpFrame();

        var brief = t.Field<Ref<ScrollViewElement>>("_briefScrollL").Current!;
        Assert.True(brief.ContentWidth > brief.Layout.AbsoluteContentBox.Width + 1,
            "the fixture's folder fits without scrolling");
        Assert.Null(Label(brief, "file399.txt"));   // the last column is not built yet

        var box = brief.ScreenBorderBox;
        for (int i = 0; i < 200; i++)
            t.Session.Window.Wheel(box.X + box.Width / 2, box.Y + box.Height / 2, 0, -1);
        t.Session.PumpFrames(2);

        brief = t.Field<Ref<ScrollViewElement>>("_briefScrollL").Current!;
        Assert.True(brief.ScrollOffsetX > 0, "the wheel did not scroll the brief view");
        Assert.NotNull(Label(brief, "file399.txt"));
    }

    private static TextElement? Label(Element root, string content)
    {
        if (root is TextElement t && t.Content == content) return t;
        foreach (var child in root.Children)
            if (Label(child, content) is { } found) return found;
        return null;
    }
}
