using System.IO;
using HultUI.Core.Elements;
using HultUI.Core.Events;
using HultUI.Core.Primitives;
using HultUI.Core.Reactivity;
using VesperCommander.Theme;
using Xunit;

namespace VesperCommander.Tests;

/// <summary>
/// In Brief and Thumbnails a key that moves only the cursor takes the ring off the live cell or
/// tile it leaves and puts it on the one it reaches, with no build (CommanderApp.MoveCursorLive),
/// and the next build of the pane puts them back as they were made first (SettleRing). Each test
/// holds the window to what a full build makes (CursorNoBuildTests.AssertIsAFullBuild).
/// </summary>
public class GridCursorNoBuildTests
{
    private static string Name(int i) => $"f{i:000}.txt";

    private static DiffViewTests.Fixture Open(int files = 40)
    {
        var t = new DiffViewTests.Fixture();
        Directory.CreateDirectory(Path.Combine(t.Left, "docs"));
        for (int i = 0; i < files; i++)
        {
            t.WriteLeft(Name(i), $"left {i}\n");
            t.WriteRight($"other{i:000}.txt", $"right {i}\n");
        }
        t.Open();
        t.Session.PumpFrames(2);
        return t;
    }

    private static int Builds(DiffViewTests.Fixture t) => t.Field<int>("_builds");

    private static void Press(DiffViewTests.Fixture t, params KeyCode[] keys)
    {
        foreach (var key in keys) t.Press(key);
        t.Session.PumpFrames(1);
    }

    private static readonly Color Fill = Nocturne.Alpha(Nocturne.Accent500, 0.13);

    private static string NameOf(Element cell) => cell.Children.OfType<TextElement>().Last().Content;

    /// <summary>The left pane's Brief cells on screen, by name.</summary>
    private static Dictionary<string, BoxElement> BriefCells(DiffViewTests.Fixture t)
    {
        var scroll = t.Field<Ref<ScrollViewElement>>("_briefScrollL").Current!;
        var cells = new Dictionary<string, BoxElement>();
        foreach (var column in scroll.Children[0].Children)
            foreach (var cell in column.Children)
                if (cell.Children.OfType<TextElement>().Any()) cells[NameOf(cell)] = (BoxElement)cell;
        return cells;
    }

    /// <summary>The left pane's thumbnail tiles realized, by name.</summary>
    private static Dictionary<string, BoxElement> Tiles(DiffViewTests.Fixture t, char side = 'L')
    {
        var list = t.Field<Ref<VirtualListElement<CommanderApp.ThumbRowItem>>>(side == 'L' ? "_thumbVListL" : "_thumbVListR").Current!;
        var tiles = new Dictionary<string, BoxElement>();
        foreach (var row in list.Children.Skip(1))
            foreach (var tile in row.Children)
                tiles[NameOf(tile)] = (BoxElement)tile;
        return tiles;
    }

    private static string[] Ringed(Dictionary<string, BoxElement> cells) =>
        [.. cells.Where(c => c.Value.BorderColor == Nocturne.Accent600).Select(c => c.Key).Order()];

    private static BoxElement? CursorRef(DiffViewTests.Fixture t) => t.Field<Ref<BoxElement>>("_cursorRowL").Current;

    // ── Brief ────────────────────────────────────────────────────────────

    /// <summary>A key builds nothing and touches no cell but the two the ring moved between, which
    /// it restyles in place; the window is what a full build makes, and that build makes only those
    /// two cells again.</summary>
    [Fact]
    public void InBrief_ACursorKey_BuildsNothing_AndMovesTheRingOnTheLiveCells()
    {
        using var t = Open();
        t.Press(KeyCode.F2, KeyModifiers.Ctrl);
        t.Session.PumpFrames(2);
        t.PutCursorOn('L', Name(3));
        var before = BriefCells(t);
        foreach (var cell in before.Values) cell.AutomationId = "untouched";
        int builds = Builds(t), parts = t.App.PartBuilds;

        Press(t, KeyCode.Down, KeyCode.Down, KeyCode.Up);

        Assert.Equal(0, Builds(t) - builds);
        Assert.Equal(0, t.App.PartBuilds - parts);
        var after = BriefCells(t);
        Assert.All(after, c => Assert.Same(before[c.Key], c.Value));
        Assert.All(after.Values, c => Assert.Equal("untouched", c.AutomationId));
        Assert.Equal([Name(4)], Ringed(after));
        Assert.Equal(Fill, after[Name(4)].BackgroundColor);
        Assert.Equal(Color.Transparent, after[Name(3)].BackgroundColor);
        Assert.Same(after[Name(4)], CursorRef(t));

        CursorNoBuildTests.AssertIsAFullBuild(t, "after Down, Down, Up");
        Assert.Equal([Name(3), Name(4)], [.. BriefCells(t).Where(c => c.Value.AutomationId != "untouched").Select(c => c.Key).Order()]);
    }

    /// <summary>Tab after keys that moved the ring with no build: the pane left shows no cursor.
    /// The cell the ring reached was made without it, so a build that hands back what it was made
    /// as kept the ring until each build first put the cells back as they were made.</summary>
    [Fact]
    public void InBrief_TabAfterKeysThatBuiltNothing_LeavesNoRingBehind()
    {
        using var t = Open();
        t.Press(KeyCode.F2, KeyModifiers.Ctrl);
        t.Session.PumpFrames(2);
        t.PutCursorOn('L', Name(3));
        Press(t, KeyCode.Down, KeyCode.Down);
        Assert.Equal([Name(5)], Ringed(BriefCells(t)));

        Press(t, KeyCode.Tab);
        Assert.Empty(Ringed(BriefCells(t)));
        CursorNoBuildTests.AssertIsAFullBuild(t, "after Tab");

        Press(t, KeyCode.Tab);
        Assert.Equal([Name(5)], Ringed(BriefCells(t)));
        Assert.Same(BriefCells(t)[Name(5)], CursorRef(t));
        CursorNoBuildTests.AssertIsAFullBuild(t, "after Tab back");
    }

    /// <summary>A click after keys that built nothing rings the cell clicked, and only that one.</summary>
    [Fact]
    public void InBrief_AClickAfterKeysThatBuiltNothing_RingsOnlyTheCellClicked()
    {
        using var t = Open();
        t.Press(KeyCode.F2, KeyModifiers.Ctrl);
        t.Session.PumpFrames(2);
        t.PutCursorOn('L', Name(3));
        Press(t, KeyCode.Down, KeyCode.Down);
        var target = BriefCells(t)[Name(9)];
        var b = target.Layout.AbsoluteBorderBox;
        t.Session.Window.Click(b.X + b.Width / 2, b.Y + b.Height / 2);
        t.Session.PumpFrames(2);
        Assert.Equal([Name(9)], Ringed(BriefCells(t)));
        CursorNoBuildTests.AssertIsAFullBuild(t, "after the click");
    }

    /// <summary>A click back on the cell the last build made with the ring, after a key moved it
    /// away with no build: that cell is handed back as made, and the cursor's ref (where the
    /// context-menu key opens) is on it again, not on the cell the key reached.</summary>
    [Fact]
    public void InBrief_AClickBackOnTheCellABuildRinged_TakesTheCursorsRef()
    {
        using var t = Open();
        t.Press(KeyCode.F2, KeyModifiers.Ctrl);
        t.Session.PumpFrames(2);
        t.PutCursorOn('L', Name(3));
        Press(t, KeyCode.Down);
        Assert.Same(BriefCells(t)[Name(4)], CursorRef(t));
        var target = BriefCells(t)[Name(3)];
        var b = target.Layout.AbsoluteBorderBox;
        t.Session.Window.Click(b.X + b.Width / 2, b.Y + b.Height / 2);
        t.Session.PumpFrames(2);
        Assert.Equal([Name(3)], Ringed(BriefCells(t)));
        Assert.Same(BriefCells(t)[Name(3)], CursorRef(t));
        Assert.Same(target, CursorRef(t));
    }

    /// <summary>A key whose cell is out of view, or not made at all, builds, and scrolls to it before
    /// it builds: the columns made are the ones in view after the scroll, so the next key builds
    /// nothing.</summary>
    [Fact]
    public void InBrief_AKeyToACellOutOfView_Builds_AndTheNextKeyNothing()
    {
        using var t = Open(files: 400);
        t.Press(KeyCode.F2, KeyModifiers.Ctrl);
        t.Session.PumpFrames(2);
        Press(t, KeyCode.Home, KeyCode.Down);
        int builds = Builds(t);
        Press(t, KeyCode.End);
        Assert.Equal(1, Builds(t) - builds);
        Assert.True(BriefScroll(t).ScrollOffsetX > 0, "End did not scroll");
        Press(t, KeyCode.Up);
        Assert.Equal(1, Builds(t) - builds);
        Assert.Equal([Name(398)], Ringed(BriefCells(t)));
        Press(t, KeyCode.Up);
        Assert.Equal(1, Builds(t) - builds);
        Assert.Equal([Name(397)], Ringed(BriefCells(t)));
        CursorNoBuildTests.AssertIsAFullBuild(t, "after End, Up, Up");
    }

    /// <summary>A page jump scrolls past the columns made before it: its build makes the columns it
    /// scrolls to, so the window after its own frame is what a full build makes, and a key back onto
    /// a cell in view builds nothing.</summary>
    [Fact]
    public void InBrief_APageJump_MakesTheColumnsItScrollsTo()
    {
        using var t = Open(files: 400);
        t.Press(KeyCode.F2, KeyModifiers.Ctrl);
        t.Session.PumpFrames(2);
        Press(t, KeyCode.Home, KeyCode.Right, KeyCode.Right);   // the last column in full view
        int builds = Builds(t);
        t.Press(KeyCode.PageDown);
        Assert.Equal(1, Builds(t) - builds);
        Assert.Empty(BlankColumns(t));
        CursorNoBuildTests.AssertIsAFullBuild(t, "after PageDown");
        builds = Builds(t);
        Press(t, KeyCode.Left);
        Assert.Equal(0, Builds(t) - builds);
        CursorNoBuildTests.AssertIsAFullBuild(t, "after PageDown, Left");
    }

    /// <summary>Every key that scrolls the grid — to the end and back, a page each way, a column
    /// left, a filter typed and cleared — shows no blank column (in view and not made) after its own
    /// frame. The columns used to be made for the scroll before the key, and a scroll set from code
    /// fires no ScrollEvent to make them, so they stayed blank until the next key.</summary>
    [Fact]
    public void InBrief_AKeyThatScrolls_LeavesNoBlankColumn()
    {
        using var t = Open(files: 400);
        t.Press(KeyCode.F2, KeyModifiers.Ctrl);
        t.Session.PumpFrames(2);
        Press(t, KeyCode.Home);
        var keys = new[] { KeyCode.End, KeyCode.Home, KeyCode.PageDown, KeyCode.PageDown, KeyCode.PageUp, KeyCode.End,
            KeyCode.Left, KeyCode.Left, KeyCode.Left, KeyCode.Left, KeyCode.Digit9, KeyCode.Escape };
        int scrolled = 0;
        foreach (var key in keys)
        {
            float before = BriefScroll(t).ScrollOffsetX;
            t.Press(key);
            if (BriefScroll(t).ScrollOffsetX != before) scrolled++;
            Assert.True(BlankColumns(t) is [], $"after {key}: columns {string.Join(", ", BlankColumns(t))} in view and not made");
        }
        Assert.True(scrolled >= 9, $"only {scrolled} of the keys scrolled");
        CursorNoBuildTests.AssertIsAFullBuild(t, "after the keys");
    }

    /// <summary>A Down walk from the first row to the last and back builds once for each key that
    /// scrolls, and never for a key that does not.</summary>
    [Fact]
    public void InBrief_ADownWalk_BuildsOncePerScroll()
    {
        using var t = Open(files: 400);
        t.Press(KeyCode.F2, KeyModifiers.Ctrl);
        t.Session.PumpFrames(2);
        Press(t, KeyCode.Home);
        int n = t.Rows('L').Count;
        foreach (var key in new[] { KeyCode.Down, KeyCode.Up })
        {
            int builds = 0, scrolls = 0;
            for (int i = 0; i < n - 1; i++)
            {
                float before = BriefScroll(t).ScrollOffsetX;
                int b = Builds(t);
                t.Press(key);
                if (BriefScroll(t).ScrollOffsetX != before) scrolls++;
                builds += Builds(t) - b;
            }
            Assert.True(scrolls >= 10, $"{key}: the walk scrolled {scrolls} times");
            Assert.Equal(scrolls, builds);
        }
        CursorNoBuildTests.AssertIsAFullBuild(t, "after the walk");
    }

    internal static ScrollViewElement BriefScroll(DiffViewTests.Fixture t) => t.Field<Ref<ScrollViewElement>>("_briefScrollL").Current!;

    /// <summary>The left pane's Brief columns at least partly in view that the latest build did not make.</summary>
    internal static int[] BlankColumns(DiffViewTests.Fixture t)
    {
        var scroll = BriefScroll(t);
        int rows = t.Field<int[]>("_briefRows")[0];
        int total = (t.Rows('L').Count + rows - 1) / rows;
        var made = scroll.Children[0].Children.Select(c => c.Key).ToHashSet();
        const float colW = CommanderApp.BriefColWidth;
        int first = (int)(scroll.ScrollOffsetX / colW);
        int last = Math.Min(total - 1, (int)Math.Ceiling((scroll.ScrollOffsetX + scroll.Layout.AbsoluteContentBox.Width) / colW - 0.001) - 1);
        return [.. Enumerable.Range(first, Math.Max(0, last - first + 1)).Where(c => !made.Contains("col" + c))];
    }

    // ── Thumbnails ───────────────────────────────────────────────────────

    /// <summary>The cursor's row scrolled away and back with the wheel (no build) is made from the
    /// list's last build's items, which had the cursor in another row: it rings the cursor as it
    /// is, and a click on a tile in a third row after it leaves one ring, on the tile clicked (the
    /// list would keep the cursor's row, its item unchanged since the last build, ring and all).</summary>
    [Fact]
    public void InThumbnails_ARowMadeAfterKeysThatBuiltNothing_RingsTheCursor()
    {
        using var t = Open(files: 300);
        t.Press(KeyCode.F3, KeyModifiers.Ctrl);
        t.Session.PumpFrames(2);
        t.PutCursorOn('L', Name(1));
        int perRow = t.Field<int[]>("_thumbCols")[0];
        Press(t, KeyCode.Down);
        string cursor = t.Rows('L')[t.Cursor('L')].Name;
        var list = t.Field<Ref<VirtualListElement<CommanderApp.ThumbRowItem>>>("_thumbVListL").Current!;
        var b = list.Layout.AbsoluteBorderBox;
        int builds = Builds(t);
        for (int i = 0; i < 6; i++) { t.Session.Window.Wheel(b.X + b.Width / 2, b.Y + b.Height / 2, 0, -10); t.Session.PumpFrames(1); }
        Assert.True(list.ScrollOffsetY > 3 * b.Height, "the wheel did not scroll the grid far");
        Assert.DoesNotContain(cursor, Tiles(t).Keys);
        for (int i = 0; i < 6; i++) { t.Session.Window.Wheel(b.X + b.Width / 2, b.Y + b.Height / 2, 0, 10); t.Session.PumpFrames(1); }
        Assert.Equal(0f, list.ScrollOffsetY);
        Assert.Equal(0, Builds(t) - builds);
        Assert.Equal([cursor], Ringed(Tiles(t)));

        string clicked = t.Rows('L')[t.Cursor('L') + perRow].Name;
        var target = Tiles(t)[clicked].Layout.AbsoluteBorderBox;
        t.Session.Window.Click(target.X + target.Width / 2, target.Y + 10);
        t.Session.PumpFrames(2);
        Assert.Equal([clicked], Ringed(Tiles(t)));
        CursorNoBuildTests.AssertIsAFullBuild(t, "after the click");
    }

    /// <summary>As in Brief: no build, no tile made, the ring on the cursor's tile, and the window
    /// what a full build makes.</summary>
    [Fact]
    public void InThumbnails_ACursorKey_BuildsNothing_AndMovesTheRingOnTheLiveTiles()
    {
        using var t = Open();
        t.Press(KeyCode.F3, KeyModifiers.Ctrl);
        t.Session.PumpFrames(2);
        t.PutCursorOn('L', Name(1));
        var before = Tiles(t);
        foreach (var tile in before.Values) tile.AutomationId = "untouched";
        int builds = Builds(t);

        Press(t, KeyCode.Right, KeyCode.Right, KeyCode.Left, KeyCode.Down);

        Assert.Equal(0, Builds(t) - builds);
        var after = Tiles(t);
        Assert.All(after, c => Assert.Same(before[c.Key], c.Value));
        Assert.All(after.Values, c => Assert.Equal("untouched", c.AutomationId));
        int perRow = t.Field<int[]>("_thumbCols")[0];
        string expected = t.Rows('L')[t.Cursor('L')].Name;
        Assert.Equal(Name(2 + perRow), expected);
        Assert.Equal([expected], Ringed(after));
        Assert.Same(after[expected], CursorRef(t));
        CursorNoBuildTests.AssertIsAFullBuild(t, "after the keys");
    }

    /// <summary>A walk down a long grid that scrolls builds nothing while the tile reached is in a
    /// realized row. The rows that scroll in are made from the list's last build's items, which
    /// know nothing of the keys since: they read the cursor as it is, so only its tile has the
    /// ring, there and back.</summary>
    [Fact]
    public void InThumbnails_AWalkThatScrolls_RingsOnlyTheCursorsTile()
    {
        using var t = Open(files: 300);
        t.Press(KeyCode.F3, KeyModifiers.Ctrl);
        t.Session.PumpFrames(2);
        Press(t, KeyCode.Home, KeyCode.Right);
        int builds = Builds(t);
        var list = t.Field<Ref<VirtualListElement<CommanderApp.ThumbRowItem>>>("_thumbVListL").Current!;

        for (int i = 0; i < 20; i++) Press(t, KeyCode.Down);
        Assert.True(list.ScrollOffsetY > 0, "the walk did not scroll the grid");
        Assert.Equal([t.Rows('L')[t.Cursor('L')].Name], Ringed(Tiles(t)));
        for (int i = 0; i < 17; i++) Press(t, KeyCode.Up);
        Assert.Equal([t.Rows('L')[t.Cursor('L')].Name], Ringed(Tiles(t)));
        Assert.True(Builds(t) - builds < 37, $"{Builds(t) - builds} builds for 37 keys");
        CursorNoBuildTests.AssertIsAFullBuild(t, "after the walk");
    }

    /// <summary>The pointer on a tile the ring reaches with no build: no hover tint over the ring
    /// while it is there, and the ring kept when the pointer leaves — the hover lays itself over the
    /// look the key gave it, not over the one the tile had when it was first hovered. On the tile
    /// the ring left, the tint comes back.</summary>
    [Fact]
    public void InThumbnails_TheRingAKeyMovedOntoAHoveredTile_StaysWhenThePointerLeaves()
    {
        using var t = Open();
        t.Press(KeyCode.F3, KeyModifiers.Ctrl);
        t.Session.PumpFrames(2);
        t.PutCursorOn('L', Name(0));
        void Hover(string name)
        {
            var b = Tiles(t)[name].Layout.AbsoluteBorderBox;
            t.Session.Window.MoveMouse(b.X + b.Width / 2, b.Y + 8);
            t.Session.PumpFrames(1);
        }
        var tint = Nocturne.Alpha(Nocturne.Accent500, 0.06);
        Hover(Name(1));
        Assert.Equal(tint, Tiles(t)[Name(1)].BackgroundColor);
        Hover(Name(2));
        int builds = Builds(t);

        Press(t, KeyCode.Right);                       // onto f001, hovered before
        Hover(Name(1));
        Assert.Equal(Fill, Tiles(t)[Name(1)].BackgroundColor);
        Hover(Name(2));
        Assert.Equal(Nocturne.Accent600, Tiles(t)[Name(1)].BorderColor);
        Assert.Equal(Fill, Tiles(t)[Name(1)].BackgroundColor);

        Press(t, KeyCode.Right);                       // onto f002, under the pointer
        Assert.Equal(Fill, Tiles(t)[Name(2)].BackgroundColor);
        Hover(Name(1));
        Assert.Equal(tint, Tiles(t)[Name(1)].BackgroundColor);
        Assert.Equal(Color.Transparent, Tiles(t)[Name(1)].BorderColor);
        Assert.Equal(0, Builds(t) - builds);
        Assert.Equal([Name(2)], Ringed(Tiles(t)));
        CursorNoBuildTests.AssertIsAFullBuild(t, "with the pointer on a tile");
    }

    /// <summary>A key to a tile no row holds builds (End), and the walk's ring is not left behind.</summary>
    [Fact]
    public void InThumbnails_AKeyToATileNotMade_Builds_AndLeavesOneRing()
    {
        using var t = Open(files: 300);
        t.Press(KeyCode.F3, KeyModifiers.Ctrl);
        t.Session.PumpFrames(2);
        Press(t, KeyCode.Home, KeyCode.Right, KeyCode.Right, KeyCode.Down);
        int builds = Builds(t);
        Press(t, KeyCode.End);
        Assert.Equal(1, Builds(t) - builds);
        Assert.Equal([Name(299)], Ringed(Tiles(t)));
        Press(t, KeyCode.Home);
        Assert.Equal([".."], Ringed(Tiles(t)));
        CursorNoBuildTests.AssertIsAFullBuild(t, "after End, Home");
    }

    /// <summary>A Shift sweep still builds (its marks), and leaves the ring on the cursor only.</summary>
    [Fact]
    public void InAGrid_AShiftSweep_Builds()
    {
        using var t = Open();
        t.Press(KeyCode.F3, KeyModifiers.Ctrl);
        t.Session.PumpFrames(2);
        t.PutCursorOn('L', Name(1));
        Press(t, KeyCode.Right);
        int builds = Builds(t);
        t.Press(KeyCode.Right, KeyModifiers.Shift);
        t.Session.PumpFrames(1);
        Assert.Equal(1, Builds(t) - builds);
        Assert.Equal([Name(3)], Ringed(Tiles(t)));
        Assert.Contains(Name(2), t.MarkNames('L'));
        CursorNoBuildTests.AssertIsAFullBuild(t, "after a sweep");
    }
}
