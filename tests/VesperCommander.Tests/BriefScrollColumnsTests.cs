using HultUI.Core.Elements;
using HultUI.Core.Events;
using HultUI.Headless;
using Xunit;

namespace VesperCommander.Tests;

/// <summary>
/// Brief makes only the columns in view, picked from its scroll offset. The wheel's ScrollEvent
/// built them again; the scrollbar (a track click, a thumb drag) and the middle-button autoscroll
/// raise none, so until plan 17's round 10 the columns they scrolled to stayed blank until the next
/// key (a track click: 4 of 4 columns in view blank). Brief now builds on the scroll view's
/// UserScrolled, which all three call. Each test holds the window to what a full build makes
/// (CursorNoBuildTests.AssertIsAFullBuild).
/// </summary>
public class BriefScrollColumnsTests
{
    private static DiffViewTests.Fixture OpenBrief(int files = 400)
    {
        var t = new DiffViewTests.Fixture();
        for (int i = 0; i < files; i++) t.WriteLeft($"f{i:000}.txt", $"left {i}\n");
        t.WriteRight("other.txt", "right\n");
        t.Open();
        t.Session.PumpFrames(2);
        t.Press(KeyCode.F2, KeyModifiers.Ctrl);
        t.Session.PumpFrames(2);
        return t;
    }

    private static int Builds(DiffViewTests.Fixture t) => t.Field<int>("_builds");
    private static ScrollViewElement Brief(DiffViewTests.Fixture t) => GridCursorNoBuildTests.BriefScroll(t);
    private static int[] Blank(DiffViewTests.Fixture t) => GridCursorNoBuildTests.BlankColumns(t);

    /// <summary>The horizontal bar's line, a few pixels above the bottom of the view.</summary>
    private static float BarY(ScrollViewElement brief) => brief.Layout.AbsoluteContentBox.Bottom - 3f;

    [Fact]
    public void ATrackClick_MakesTheColumnsItScrollsTo_InOneBuild()
    {
        using var t = OpenBrief();
        var box = Brief(t).Layout.AbsoluteContentBox;
        int builds = Builds(t);

        t.Session.Window.MouseDown(box.Right - 30f, BarY(Brief(t)));
        t.Session.PumpFrame();
        t.Session.Window.MouseUp(box.Right - 30f, BarY(Brief(t)));
        t.Session.PumpFrame();

        Assert.True(Brief(t).ScrollOffsetX > 4 * CommanderApp.BriefColWidth, "the track click did not scroll past the columns made");
        Assert.True(Blank(t) is [], $"columns {string.Join(", ", Blank(t))} in view and not made");
        Assert.Equal(1, Builds(t) - builds);
        CursorNoBuildTests.AssertIsAFullBuild(t, "after a track click");
    }

    /// <summary>Every move of a thumb drag, right and back, shows no blank column after its own
    /// frame; a move inside the columns made builds nothing.</summary>
    [Fact]
    public void AThumbDrag_MakesTheColumnsEachMoveBringsIntoView()
    {
        using var t = OpenBrief();
        var brief = Brief(t);
        var box = brief.Layout.AbsoluteContentBox;
        float thumb = MathF.Max(20f, box.Width * box.Width / brief.ContentWidth);
        float x = box.X + thumb / 2, y = BarY(brief);
        t.Session.Window.MouseDown(x, y);   // on the thumb, at the start
        t.Session.PumpFrame();

        int scrolled = 0;
        foreach (float dx in new[] { 40f, 40f, 40f, 40f, 40f, 40f, -37f, -37f, -37f, -37f, -37f, -37f })
        {
            x += dx;
            float before = Brief(t).ScrollOffsetX;
            t.Session.Window.MoveMouse(x, y);
            t.Session.PumpFrame();
            if (Brief(t).ScrollOffsetX != before) scrolled++;
            Assert.True(Blank(t) is [], $"after a move to {x}: columns {string.Join(", ", Blank(t))} in view and not made");
        }
        Assert.True(scrolled >= 10, $"only {scrolled} moves scrolled");

        int builds = Builds(t);
        t.Session.Window.MoveMouse(x + 1f, y);   // a pixel: the same columns
        t.Session.PumpFrame();
        Assert.Equal(0, Builds(t) - builds);

        t.Session.Window.MouseUp(x + 1f, y);
        t.Session.PumpFrame();
        CursorNoBuildTests.AssertIsAFullBuild(t, "after a thumb drag");
    }

    /// <summary>A folder whose columns fit the pane has nothing to scroll: a track click where the
    /// bar would be, the wheel and the middle button leave it at the start. Its content used to
    /// grow to fill the pane, and a horizontal scroll view gives its content all the width it asks
    /// for, so it was 1.7e38 wide: each of those scrolled the columns out of view.</summary>
    [Fact]
    public void AFolderThatFitsThePane_HasNothingToScroll()
    {
        using var t = OpenBrief(files: 40);
        var brief = Brief(t);
        var box = brief.Layout.AbsoluteContentBox;
        Assert.True(brief.ContentWidth <= box.Width, $"content {brief.ContentWidth} wide in a {box.Width} view");

        t.Session.Window.MouseDown(box.Right - 30f, BarY(brief));
        t.Session.PumpFrame();
        t.Session.Window.MouseUp(box.Right - 30f, BarY(brief));
        t.Session.PumpFrame();
        for (int i = 0; i < 5; i++) t.Session.Window.Wheel(box.X + box.Width / 2, box.Y + box.Height / 2, 0, -1);
        t.Session.PumpFrame();
        t.Session.Window.MouseDown(box.X + box.Width / 2, box.Y + box.Height / 2, MouseButton.Middle);
        t.Session.Window.MoveMouse(box.X + box.Width / 2 + 120f, box.Y + box.Height / 2);
        t.Session.PumpFrames(30);
        t.Session.Window.MouseUp(box.X + box.Width / 2 + 120f, box.Y + box.Height / 2, MouseButton.Middle);
        t.Session.PumpFrame();

        Assert.Equal(0f, Brief(t).ScrollOffsetX);
        Assert.True(Blank(t) is [], $"columns {string.Join(", ", Blank(t))} in view and not made");
        CursorNoBuildTests.AssertIsAFullBuild(t, "after the bar, the wheel and the middle button");
    }

    /// <summary>The middle button held right of where it was pressed: each frame the view moves
    /// shows no blank column.</summary>
    [Fact]
    public void TheAutoscroll_MakesTheColumnsItReaches()
    {
        using var t = OpenBrief();
        var box = Brief(t).Layout.AbsoluteContentBox;
        float cx = box.X + box.Width / 2, cy = box.Y + box.Height / 2;

        t.Session.Window.MouseDown(cx, cy, MouseButton.Middle);
        t.Session.Window.MoveMouse(cx + 120f, cy);
        for (int f = 0; f < 60; f++)
        {
            t.Session.PumpFrame();
            Assert.True(Blank(t) is [], $"frame {f}: columns {string.Join(", ", Blank(t))} in view and not made");
        }
        Assert.True(Brief(t).ScrollOffsetX > 4 * CommanderApp.BriefColWidth, "the autoscroll did not scroll past the columns made");
        t.Session.Window.MouseUp(cx + 120f, cy, MouseButton.Middle);
        t.Session.PumpFrame();
        CursorNoBuildTests.AssertIsAFullBuild(t, "after an autoscroll");
    }
}
