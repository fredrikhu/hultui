using HultUI.Core.Elements;
using HultUI.Core.Events;
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

        var list = t.Field<Ref<VirtualListElement<int>>>("_thumbVListL");
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
