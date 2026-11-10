using System.IO;
using System.Reflection;
using HultUI.Core.Elements;
using HultUI.Core.Events;
using HultUI.Core.Reactivity;
using Xunit;

namespace VesperCommander.Tests;

/// <summary>
/// Back to Brief from another view. Brief's scroll view leaves the window with the view, and the
/// build that brings Brief back makes a new one. Until plan 17's round 10 the columns were made for
/// the offset the old one had while the new one opened at the start: every column in view was blank
/// (4 of 4 at 400 files). Step 15 made them for the start, and Brief came back at the start. Since
/// step 15b the build gives the new scroll view the offset Brief was left at, which a scroll view
/// keeps when it is set before its first layout, and makes the columns for it: Brief comes back
/// showing the columns and the cursor it showed. A folder change while away starts at the top; a
/// cursor moved while away is shown. Each test holds the window to what a full build makes
/// (CursorNoBuildTests.AssertIsAFullBuild).
/// </summary>
public class BriefReturnTests
{
    public enum Away { Detail, Thumbnails, Viewer, Diff, Sync }

    private static DiffViewTests.Fixture OpenBrief(int files = 400, string? folder = null)
    {
        var t = new DiffViewTests.Fixture();
        for (int i = 0; i < files; i++) t.WriteLeft($"f{i:000}.txt", $"left {i}\n");
        if (folder is not null) Directory.CreateDirectory(Path.Combine(t.Left, folder));
        t.WriteRight("other.txt", "right\n");
        t.Open();
        t.Session.PumpFrames(2);
        t.Press(KeyCode.F2, KeyModifiers.Ctrl);
        t.Session.PumpFrames(2);
        return t;
    }

    private static int[] Blank(DiffViewTests.Fixture t) => GridCursorNoBuildTests.BlankColumns(t);
    private static ScrollViewElement Brief(DiffViewTests.Fixture t) => GridCursorNoBuildTests.BriefScroll(t);

    private static bool FullShellOpen(DiffViewTests.Fixture t) =>
        (bool)typeof(CommanderApp).GetProperty("FullShellOpen", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(t.App)!;

    /// <summary>Opens the other view; the viewer and the diff open on the file under the cursor.</summary>
    private static void GoTo(DiffViewTests.Fixture t, Away view)
    {
        switch (view)
        {
            case Away.Detail: t.Press(KeyCode.F1, KeyModifiers.Ctrl); break;
            case Away.Thumbnails: t.Press(KeyCode.F3, KeyModifiers.Ctrl); break;
            case Away.Viewer: t.Press(KeyCode.F3, KeyModifiers.Shift); break;
            case Away.Diff: t.Press(KeyCode.F3); break;
            case Away.Sync: t.Press(KeyCode.F9); break;
        }
        if (view is Away.Viewer or Away.Diff or Away.Sync)
        {
            t.Session.PumpUntil(() => FullShellOpen(t), System.TimeSpan.FromSeconds(10));
            if (view == Away.Sync) t.Session.PumpUntil(() => !t.Field<bool>("_syncComparing"), System.TimeSpan.FromSeconds(30));
        }
        t.Session.PumpFrames(2);
        Assert.False(t.Field<Ref<ScrollViewElement>>("_briefScrollL").Current is { IsHosted: true }, $"{view} did not replace Brief");
    }

    /// <summary>Back to Brief, the one frame a key gives.</summary>
    private static void Back(DiffViewTests.Fixture t, Away view)
    {
        if (view is Away.Detail or Away.Thumbnails) t.Press(KeyCode.F2, KeyModifiers.Ctrl);
        else t.Press(KeyCode.Escape);
        Assert.False(FullShellOpen(t));
    }

    /// <summary>Brief scrolled by the cursor, to the end or to the middle, then another view and back:
    /// the same offset and cursor on the frame back, the cursor's cell made and no blank column, and a
    /// full build keeps it all.</summary>
    [Theory]
    [InlineData(Away.Detail, true)]
    [InlineData(Away.Detail, false)]
    [InlineData(Away.Thumbnails, true)]
    [InlineData(Away.Thumbnails, false)]
    [InlineData(Away.Viewer, true)]
    [InlineData(Away.Viewer, false)]
    [InlineData(Away.Diff, true)]
    [InlineData(Away.Diff, false)]
    [InlineData(Away.Sync, true)]
    [InlineData(Away.Sync, false)]
    public void BackFromAnotherView_ShowsTheColumnsAndTheCursorItWasLeftAt(Away view, bool atTheEnd)
    {
        using var t = OpenBrief();
        if (atTheEnd) t.Press(KeyCode.End);
        else
        {
            t.Press(KeyCode.Home);
            for (int i = 0; i < 200; i++) t.Session.Window.PressKey(KeyCode.Down);   // to f197, mid-folder
        }
        t.Session.PumpFrames(2);
        float leftAt = Brief(t).ScrollOffsetX;
        int cursor = t.Cursor('L');
        Assert.True(leftAt > 0, "the cursor did not scroll Brief");

        GoTo(t, view);
        Back(t, view);

        Assert.Equal(leftAt, Brief(t).ScrollOffsetX);
        Assert.Equal(cursor, t.Cursor('L'));
        Assert.True(Blank(t) is [], $"columns {string.Join(", ", Blank(t))} in view and not made");
        Assert.True(t.Field<Ref<BoxElement>>("_cursorRowL").Current is { IsHosted: true }, "the cursor's cell is not made");
        CursorNoBuildTests.AssertIsAFullBuild(t, $"back from {view}");
        Assert.Equal(leftAt, Brief(t).ScrollOffsetX);
    }

    /// <summary>The bar scrolled the view away from the cursor, then another view and back: the
    /// columns the bar left in view, the cursor still out of view (a key away, as before Brief
    /// left).</summary>
    [Theory]
    [InlineData(Away.Detail)]
    [InlineData(Away.Viewer)]
    public void BackFromAnotherView_AfterTheBarScrolledAwayFromTheCursor_ShowsWhatTheBarLeft(Away view)
    {
        using var t = OpenBrief();
        t.PutCursorOn('L', "f000.txt");
        var brief = Brief(t);
        var box = brief.Layout.AbsoluteContentBox;
        t.Session.Window.MouseDown(box.Right - 30f, box.Bottom - 3f);
        t.Session.PumpFrame();
        t.Session.Window.MouseUp(box.Right - 30f, box.Bottom - 3f);
        t.Session.PumpFrame();
        float leftAt = brief.ScrollOffsetX;
        Assert.True(leftAt > 0, "the track click did not scroll");

        GoTo(t, view);
        Back(t, view);

        Assert.Equal(leftAt, Brief(t).ScrollOffsetX);
        Assert.True(Blank(t) is [], $"columns {string.Join(", ", Blank(t))} in view and not made");
        CursorNoBuildTests.AssertIsAFullBuild(t, $"back from {view}");
        Assert.Equal(leftAt, Brief(t).ScrollOffsetX);
    }

    /// <summary>Another folder while in Detail, and back to the first: Brief starts at the top, as
    /// a folder change always has.</summary>
    [Fact]
    public void BackFromDetail_AfterAFolderChange_StartsAtTheTop()
    {
        using var t = OpenBrief(folder: "sub");
        t.Press(KeyCode.End);
        t.Session.PumpFrames(2);
        Assert.True(Brief(t).ScrollOffsetX > 0, "End did not scroll");
        int leftOn = t.Cursor('L');

        t.Press(KeyCode.F1, KeyModifiers.Ctrl);
        t.Session.PumpFrames(2);
        var navigate = typeof(CommanderApp).GetMethod("NavigateTo", BindingFlags.NonPublic | BindingFlags.Instance)!;
        navigate.Invoke(t.App, ['L', Path.Combine(t.Left, "sub"), null, true]);
        t.Session.PumpUntil(() => t.Rows('L').Count < 5, System.TimeSpan.FromSeconds(10));
        // Back on the last file, the row Brief was left on: only the folder change says to start over.
        navigate.Invoke(t.App, ['L', t.Left, "f399.txt", true]);
        t.Session.PumpUntil(() => t.Rows('L').Count > 400 && t.Cursor('L') == leftOn, System.TimeSpan.FromSeconds(10));
        t.Session.PumpFrames(2);
        t.Press(KeyCode.F2, KeyModifiers.Ctrl);

        Assert.Equal(0f, Brief(t).ScrollOffsetX);
        Assert.True(Blank(t) is [], $"columns {string.Join(", ", Blank(t))} in view and not made");
        CursorNoBuildTests.AssertIsAFullBuild(t, "back in Brief");
    }

    /// <summary>The cursor moved in Detail to a file out of the columns Brief was left at: Brief
    /// comes back moved the least distance that shows it, its cell made, no blank column.</summary>
    [Fact]
    public void BackFromDetail_TheCursorMovedThere_ShowsTheCursor()
    {
        using var t = OpenBrief();
        t.Press(KeyCode.End);
        t.Session.PumpFrames(2);
        float leftAt = Brief(t).ScrollOffsetX;

        t.Press(KeyCode.F1, KeyModifiers.Ctrl);
        t.Session.PumpFrames(2);
        t.PutCursorOn('L', "f200.txt");
        t.Press(KeyCode.F2, KeyModifiers.Ctrl);

        float shown = Brief(t).ScrollOffsetX;
        Assert.True(shown > 0 && shown < leftAt, $"Brief came back at {shown}, left at {leftAt}");
        Assert.True(t.Field<Ref<BoxElement>>("_cursorRowL").Current is { IsHosted: true } cell
            && cell.Layout.AbsoluteBorderBox.X >= Brief(t).Layout.AbsoluteContentBox.X - 0.5f
            && cell.Layout.AbsoluteBorderBox.Right - shown <= Brief(t).Layout.AbsoluteContentBox.Right + 0.5f,
            "the cursor's cell is not in view");
        Assert.True(Blank(t) is [], $"columns {string.Join(", ", Blank(t))} in view and not made");
        CursorNoBuildTests.AssertIsAFullBuild(t, "back in Brief");
    }
}
