using HultUI.Core.Elements;
using HultUI.Core.Events;
using HultUI.Core.Primitives;
using HultUI.Core.Reactivity;
using VesperCommander;
using Xunit;

namespace VesperCommander.Tests;

public class ViewModesTests
{
    // ── Brief grid math ──────────────────────────────────────────────────
    [Fact]
    public void BriefGrid_FixedColumnsThatFitDoNotScroll()
    {
        // 41 items, 28 rows fit the height → 2 columns; both fit 600px (3 fit) → no scroll.
        var (rows, cols, colW, overflow) = CommanderApp.BriefGrid(41, availW: 600, availH: 680, rowH: 24);
        Assert.Equal(28, rows);
        Assert.Equal(2, cols);
        Assert.False(overflow);
        Assert.Equal(186f, colW);   // fixed width, left-packed
    }

    [Fact]
    public void BriefGrid_ScrollsWhenColumnsOverflow()
    {
        // 100 items, 10 rows per column → 10 columns; only ~2 fit 400px → overflow, fixed 186.
        var (rows, cols, colW, overflow) = CommanderApp.BriefGrid(100, availW: 400, availH: 240, rowH: 24);
        Assert.Equal(10, rows);
        Assert.Equal(10, cols);
        Assert.True(overflow);
        Assert.Equal(186f, colW);
    }

    [Fact]
    public void BriefGrid_AlwaysAtLeastOneRowAndColumn()
    {
        var (rows, cols, _, _) = CommanderApp.BriefGrid(0, availW: 10, availH: 5, rowH: 24);
        Assert.Equal(1, rows);
        Assert.Equal(1, cols);
    }

    // ── Thumbnail per-row math (design: 5/row comfortable, 6/row compact) ──
    [Theory]
    [InlineData(610, 100, 8, 5)]
    [InlineData(610, 84, 7, 6)]
    [InlineData(50, 100, 8, 1)]   // never zero
    public void ThumbPerRow_MatchesDesign(float availW, float tileW, float gap, int expected) =>
        Assert.Equal(expected, CommanderApp.ThumbPerRow(availW, tileW, gap));

    // ── Resizing the window re-lays the grids ────────────────────────────
    // Both grids are sized in the build from the window, and a resize alone did not rebuild,
    // so the tiles kept the old window's width until a key press happened to rebuild.

    [Fact]
    public void ThumbnailGridFollowsTheWindowWidth()
    {
        using var t = new DiffViewTests.Fixture();
        for (int i = 0; i < 40; i++) t.WriteLeft($"f{i:00}.txt", "x");
        t.Open();
        t.Press(KeyCode.F3, KeyModifiers.Ctrl);   // view: Thumbnails
        t.Session.PumpFrames(2);

        var list = t.Field<Ref<VirtualListElement<CommanderApp.ThumbRowItem>>>("_thumbVListL");
        float before = list.Current!.Layout.AbsoluteBorderBox.Width;
        int perRowBefore = t.Field<int[]>("_thumbCols")[0];

        t.Session.Window.SetClientSize(1800, 820);
        t.Session.PumpFrames(2);

        float after = list.Current!.Layout.AbsoluteBorderBox.Width;
        Assert.True(after > before + 200, $"grid {before:0} → {after:0} px after widening by 560 px");
        Assert.True(t.Field<int[]>("_thumbCols")[0] > perRowBefore, "no more tiles per row");
    }

    [Fact]
    public void BriefColumnsFollowTheWindowHeight()
    {
        using var t = new DiffViewTests.Fixture();
        for (int i = 0; i < 80; i++) t.WriteLeft($"f{i:00}.txt", "x");
        t.Open();
        t.Press(KeyCode.F2, KeyModifiers.Ctrl);   // view: Brief
        t.Session.PumpFrames(2);
        int rowsBefore = t.Field<int[]>("_briefRows")[0];

        t.Session.Window.SetClientSize(1240, 500);
        t.Session.PumpFrames(2);

        Assert.True(t.Field<int[]>("_briefRows")[0] < rowsBefore, "rows per column kept the taller window's count");
    }

    // ── …and only a resize that changes the grid builds ──────────────────
    // A build of the whole window on every resize step made every cell of a grid again (Brief
    // 750 KB a step). Most steps change only the grid's size, which the live grid takes; a
    // build comes when the tiles per row, the rows per column or the columns realized change.

    [Fact]
    public void AResizeThatKeepsTheTilesPerRow_DoesNotRebuild_AndTheGridStillFollowsTheWindow()
    {
        using var t = new DiffViewTests.Fixture();
        for (int i = 0; i < 40; i++) t.WriteLeft($"f{i:00}.txt", "x");
        t.Open();
        t.Press(KeyCode.F3, KeyModifiers.Ctrl);   // view: Thumbnails
        t.Session.PumpFrames(2);
        var list = t.Field<Ref<VirtualListElement<CommanderApp.ThumbRowItem>>>("_thumbVListL");
        int perRow = t.Field<int[]>("_thumbCols")[0];
        float width = list.Current!.Layout.AbsoluteBorderBox.Width;
        var rows = list.Current!.Children.ToList();
        int builds = t.Field<int>("_builds");

        t.Session.Window.SetClientSize(1248, 830);   // each pane 4 px wider: the same tiles per row
        t.Session.PumpFrames(2);

        Assert.Equal(builds, t.Field<int>("_builds"));
        Assert.Equal(perRow, t.Field<int[]>("_thumbCols")[0]);
        Assert.Equal(width + 4, list.Current!.Layout.AbsoluteBorderBox.Width, 0.01f);
        Assert.Equal(rows, list.Current!.Children);

        t.Session.Window.SetClientSize(1800, 830);   // more tiles per row: a build
        t.Session.PumpFrames(2);

        Assert.True(t.Field<int>("_builds") > builds, "a resize that changes the tiles per row did not build");
        Assert.True(t.Field<int[]>("_thumbCols")[0] > perRow, "no more tiles per row");
    }

    [Fact]
    public void AResizeThatKeepsBriefsColumns_DoesNotRebuild_AndTheGridStillFollowsTheWindow()
    {
        using var t = new DiffViewTests.Fixture();
        for (int i = 0; i < 80; i++) t.WriteLeft($"f{i:00}.txt", "x");
        t.Open();
        t.Press(KeyCode.F2, KeyModifiers.Ctrl);   // view: Brief
        t.Session.PumpFrames(2);
        var scroll = t.Field<Ref<ScrollViewElement>>("_briefScrollL");
        int rowsPerColumn = t.Field<int[]>("_briefRows")[0];
        var box = scroll.Current!.Layout.AbsoluteBorderBox;
        int builds = t.Field<int>("_builds");

        t.Session.Window.SetClientSize(1248, 830);   // 4 px wider panes, 10 px taller: the same rows
        t.Session.PumpFrames(2);

        Assert.Equal(builds, t.Field<int>("_builds"));
        Assert.Equal(rowsPerColumn, t.Field<int[]>("_briefRows")[0]);
        var after = scroll.Current!.Layout.AbsoluteBorderBox;
        Assert.Equal(box.Width + 4, after.Width, 0.01f);
        Assert.Equal(box.Height + 10, after.Height, 0.01f);

        t.Session.Window.SetClientSize(1248, 500);   // fewer rows per column: a build
        t.Session.PumpFrames(2);

        Assert.True(t.Field<int>("_builds") > builds, "a resize that changes the rows per column did not build");
        Assert.True(t.Field<int[]>("_briefRows")[0] < rowsPerColumn);
    }

    /// <summary>A resize step that does not build must leave the window as a build would have
    /// made it: the path trails (which a grid view has always followed) and the grid, cell by
    /// cell. Each step is compared with a forced build at the same size; the folder is deep
    /// enough that the trail drops a segment somewhere in the sweep, on any platform.</summary>
    [Theory]
    [InlineData(KeyCode.F2)]   // Brief
    [InlineData(KeyCode.F3)]   // Thumbnails
    public void EveryResizeStep_ShowsWhatABuildWould(KeyCode view)
    {
        using var t = new DiffViewTests.Fixture();
        var deep = Path.Combine(t.Left, "first-long-name", "second-long-name", "third-long-name",
            "fourth-long-name", "fifth-long-name", "sixth-long-name");
        Directory.CreateDirectory(deep);
        for (int i = 0; i < 80; i++) File.WriteAllText(Path.Combine(deep, $"f{i:00}.txt"), "x");
        t.Open();
        t.Invoke("NavigateTo", 'L', deep);
        t.Session.PumpUntil(() => t.Rows('L').Count > 80);
        t.Press(view, KeyModifiers.Ctrl);
        t.Session.PumpFrames(2);

        int builds = t.Field<int>("_builds"), trailChanges = 0;
        string? trail = null;
        for (int width = 1240; width <= 1560; width += 6)
        {
            t.Session.Window.SetClientSize(width, 820);
            t.Session.PumpFrames(2);
            string shown = Snapshot(t, view);

            t.MarkDirty();
            t.Session.PumpFrames(2);
            Assert.Equal(Snapshot(t, view), shown);

            string now = Trail(t.Root, "crumb-strip-L");
            if (trail is not null && now != trail) trailChanges++;
            trail = now;
        }
        Assert.True(trailChanges > 0, "no step of the sweep changed the path trail");
        // The forced builds are 54; the steps themselves built only where something changed.
        Assert.True(t.Field<int>("_builds") - builds < 54 + 20, "most resize steps still built");
    }

    /// <summary>The texts and boxes of both path trails and of the left pane's grid.</summary>
    private static string Snapshot(DiffViewTests.Fixture t, KeyCode view)
    {
        var sb = new System.Text.StringBuilder();
        foreach (var id in new[] { "crumb-strip-L", "crumb-strip-R" })
            Dump(FindById(t.Root, id) ?? throw new Xunit.Sdk.XunitException($"no {id}"), sb);
        Element grid = view == KeyCode.F2
            ? t.Field<Ref<ScrollViewElement>>("_briefScrollL").Current!
            : t.Field<Ref<VirtualListElement<CommanderApp.ThumbRowItem>>>("_thumbVListL").Current!;
        Dump(grid, sb);
        return sb.ToString();

        static void Dump(Element e, System.Text.StringBuilder sb)
        {
            var b = e.Layout.AbsoluteBorderBox;
            sb.Append(e.GetType().Name).Append(' ').Append((e as TextElement)?.Content)
              .Append($" {b.X} {b.Y} {b.Width} {b.Height}\n");
            foreach (var c in e.Children) Dump(c, sb);
        }
    }

    private static string Trail(Element root, string id)
    {
        var sb = new System.Text.StringBuilder();
        Walk(FindById(root, id)!);
        return sb.ToString();

        void Walk(Element e)
        {
            if (e is TextElement text) sb.Append(text.Content).Append('|');
            foreach (var c in e.Children) Walk(c);
        }
    }

    private static Element? FindById(Element e, string id)
    {
        if (e.AutomationId == id) return e;
        foreach (var c in e.Children)
            if (FindById(c, id) is { } found) return found;
        return null;
    }

    /// <summary>The grid keeps its rows of tiles across builds, and a tile is made with a
    /// placeholder while its picture decodes: the decode landing has to reach it all the same.</summary>
    [Fact]
    public void ADecodeThatLands_ReachesItsTileInAKeptRow()
    {
        using var t = new DiffViewTests.Fixture();
        WritePng(Path.Combine(t.Left, "a-picture.png"), 200, 150);
        for (int i = 0; i < 20; i++) t.WriteLeft($"f{i:00}.txt", "x");
        t.Open();
        t.Press(KeyCode.F3, KeyModifiers.Ctrl);   // view: Thumbnails
        var list = t.Field<Ref<VirtualListElement<CommanderApp.ThumbRowItem>>>("_thumbVListL");

        t.Session.PumpUntil(() => list.Current is { } l && HasThumbnail(l), System.TimeSpan.FromSeconds(10));

        Assert.True(HasThumbnail(list.Current!), "the decoded picture never reached its tile");

        // The thumbnail fills its image area; the kind icons and the placeholder are drawn at a size.
        static bool HasThumbnail(Element e) =>
            (e is ImageElement && e.Box.Width == Dimension.Percent(100)) || e.Children.Any(HasThumbnail);
    }

    // ── ThumbnailCache ───────────────────────────────────────────────────
    [Fact]
    public void ThumbnailCache_MissIsPendingThenClassifies()
    {
        using var mre = new System.Threading.ManualResetEventSlim();
        int decodes = 0;
        using var cache = new ThumbnailCache(() =>
        {
            System.Threading.Interlocked.Increment(ref decodes);
            mre.Set();
        });

        // A non-existent file: first Get enqueues a decode and reports Pending.
        var (img, status) = cache.Get(@"C:\does\not\exist.png", size: 123, mtime: default, targetPx: 128);
        Assert.Null(img);
        Assert.Equal(ThumbnailCache.Status.Pending, status);

        // That a decode was enqueued is counted, not sampled. PendingCount is in-flight work,
        // and a file that does not exist fails as fast as the decode can be dequeued — so
        // reading 1 there is a race the test thread only usually wins. It lost on the build
        // runner (expected 1, actual 0) and turned main red on an unrelated commit.
        Assert.True(mre.Wait(System.TimeSpan.FromSeconds(5)), "decode did not complete");
        Assert.Equal(1, System.Threading.Volatile.Read(ref decodes));
        // Deterministic in the other direction: the count is dropped before the callback runs.
        Assert.Equal(0, cache.PendingCount);

        // Now cached as Failed (undecodable), and repeat Gets don't re-enqueue.
        var (img2, status2) = cache.Get(@"C:\does\not\exist.png", size: 123, mtime: default, targetPx: 128);
        Assert.Null(img2);
        Assert.Equal(ThumbnailCache.Status.Failed, status2);
        Assert.Equal(0, cache.PendingCount);
        Assert.Equal(1, System.Threading.Volatile.Read(ref decodes));   // the hit decoded nothing
    }

    [Fact]
    public void ThumbnailCache_DecodesAndDownscalesARealImage()
    {
        var tmp = Path.Combine(Path.GetTempPath(), $"vesper-thumb-{Guid.NewGuid():N}.png");
        WritePng(tmp, 400, 300);
        try
        {
            using var mre = new System.Threading.ManualResetEventSlim();
            using var cache = new ThumbnailCache(mre.Set);
            var info = new FileInfo(tmp);

            var (_, status) = cache.Get(tmp, info.Length, info.LastWriteTime, targetPx: 64);
            Assert.Equal(ThumbnailCache.Status.Pending, status);
            Assert.True(mre.Wait(System.TimeSpan.FromSeconds(5)), "decode did not complete");

            var (img, ready) = cache.Get(tmp, info.Length, info.LastWriteTime, targetPx: 64);
            Assert.Equal(ThumbnailCache.Status.Ready, ready);
            Assert.NotNull(img);
            // Downscaled to fit the 64px box (longest side), aspect preserved (400x300 → 64x48).
            Assert.True(img!.IntrinsicSize.Width <= 64 && img.IntrinsicSize.Height <= 64);
        }
        finally { File.Delete(tmp); }
    }

    private static void WritePng(string path, int w, int h)
    {
        using var bmp = new SkiaSharp.SKBitmap(w, h);
        using (var canvas = new SkiaSharp.SKCanvas(bmp)) canvas.Clear(SkiaSharp.SKColors.SlateBlue);
        using var img = SkiaSharp.SKImage.FromBitmap(bmp);
        using var data = img.Encode(SkiaSharp.SKEncodedImageFormat.Png, 90);
        using var fs = File.Create(path);
        data.SaveTo(fs);
    }
}
