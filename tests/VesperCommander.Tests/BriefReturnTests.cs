using HultUI.Core.Events;
using Xunit;

namespace VesperCommander.Tests;

/// <summary>
/// Back to Brief from another view. Brief's scroll view leaves the window with the view, and the
/// build that brings Brief back makes a new one, which opens at the start. The columns used to be
/// made for the offset the old one had, so a pane scrolled away from the start came back with every
/// column in view blank (4 of 4 at 400 files), until a key built again; with the cursor on the last
/// row no key did. The columns are now made for the start, where the new view opens. Each test holds
/// the window to what a full build makes (CursorNoBuildTests.AssertIsAFullBuild).
/// </summary>
public class BriefReturnTests
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

    private static int[] Blank(DiffViewTests.Fixture t) => GridCursorNoBuildTests.BlankColumns(t);

    /// <summary>The cursor on the last row (the view scrolled to the end), then the other view and
    /// back: no blank column on the frame Brief comes back on.</summary>
    [Theory]
    [InlineData(KeyCode.F1)]   // Detail
    [InlineData(KeyCode.F3)]   // Thumbnails
    public void BackFromAnotherView_WithTheViewScrolledToTheEnd_ShowsNoBlankColumn(KeyCode view)
    {
        using var t = OpenBrief();
        t.Press(KeyCode.End);
        t.Session.PumpFrame();
        Assert.True(GridCursorNoBuildTests.BriefScroll(t).ScrollOffsetX > 0, "End did not scroll");

        t.Press(view, KeyModifiers.Ctrl);
        t.Session.PumpFrames(2);
        t.Press(KeyCode.F2, KeyModifiers.Ctrl);

        Assert.True(Blank(t) is [], $"columns {string.Join(", ", Blank(t))} in view and not made");
        CursorNoBuildTests.AssertIsAFullBuild(t, "back in Brief");
    }

    /// <summary>The viewer replaces the panes. Closing it put a Brief pane's scroll back
    /// (RestorePaneScroll) after a build had made the columns for the start, where the new scroll
    /// view opened, and at whatever build came next (the first build back cannot set it: the view has
    /// no size yet): every column in view was blank on the frame back and after that build. Brief
    /// now comes back at the start, as it does from Detail, made for the start, and stays there
    /// through the frames and builds after it.</summary>
    [Fact]
    public void BackFromTheViewer_ComesBackAtTheStart_WithNoBlankColumn()
    {
        using var t = OpenBrief();
        t.Press(KeyCode.End);
        t.Session.PumpFrame();
        Assert.True(GridCursorNoBuildTests.BriefScroll(t).ScrollOffsetX > 0, "End did not scroll");

        t.Press(KeyCode.F3, KeyModifiers.Shift);   // the viewer, on the last file
        t.Session.PumpUntil(() => t.Field<bool>("_viewerOpen"), System.TimeSpan.FromSeconds(10));
        t.Session.PumpFrames(2);
        t.Press(KeyCode.Escape);
        Assert.False(t.Field<bool>("_viewerOpen"));
        Assert.True(Blank(t) is [], $"on the frame back: columns {string.Join(", ", Blank(t))} in view and not made");

        CursorNoBuildTests.AssertIsAFullBuild(t, "back from the viewer");
        Assert.Equal(0f, GridCursorNoBuildTests.BriefScroll(t).ScrollOffsetX);
        Assert.True(Blank(t) is [], $"after a build: columns {string.Join(", ", Blank(t))} in view and not made");
    }

    /// <summary>The bar scrolled the view away from the cursor, then Detail and back.</summary>
    [Fact]
    public void BackFromDetail_AfterTheBarScrolledAwayFromTheCursor_ShowsNoBlankColumn()
    {
        using var t = OpenBrief();
        var brief = GridCursorNoBuildTests.BriefScroll(t);
        var box = brief.Layout.AbsoluteContentBox;
        t.Session.Window.MouseDown(box.Right - 30f, box.Bottom - 3f);
        t.Session.PumpFrame();
        t.Session.Window.MouseUp(box.Right - 30f, box.Bottom - 3f);
        t.Session.PumpFrame();
        Assert.True(brief.ScrollOffsetX > 0, "the track click did not scroll");

        t.Press(KeyCode.F1, KeyModifiers.Ctrl);
        t.Session.PumpFrames(2);
        t.Press(KeyCode.F2, KeyModifiers.Ctrl);

        Assert.True(Blank(t) is [], $"columns {string.Join(", ", Blank(t))} in view and not made");
        CursorNoBuildTests.AssertIsAFullBuild(t, "back in Brief");
    }
}
