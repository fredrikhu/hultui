using HultUI.Core.Layout;
using HultUI.Core.Primitives;
using HultUI.Core.Rendering;
using HultUI.Core.Styling;
using HultUI.Text;
using Xunit;
using Xunit.Abstractions;

namespace HultUI.Rendering.Skia.Tests;

/// <summary>
/// The context keeps a native text blob for each run it draws, and for each part of a run a
/// colour stretch draws (a sub-blob), in two maps of a bound of 2,048 each. A full map emptied
/// whole, so a screen that needed more than 2,048 made every blob again on every frame: a
/// dense-colour terminal at 1200×900 (2,338 a frame), at 1600×1000 5,877, a Chinese one at
/// 1080p 4,096 run blobs (plan 17, round 5). Now a full map lets go only of what neither this
/// frame nor the last drew, and the next sweep waits until the map has grown again; it empties
/// whole as before only when what the two frames drew holds more than
/// <see cref="SkiaRenderContext.MaxGlyphsKept"/> glyphs.
/// </summary>
public class BlobWorkingSetTests(ITestOutputHelper output)
{
    private const int W = 1600, H = 1000;

    private static ResolvedInheritedStyle Style(float size = 13f, string family = "monospace") => new(
        FontFamily: family, FontSize: size, FontWeight: FontWeight.Regular,
        TextColor: new Color(204, 204, 204, 255), TextDirection: TextDirection.Ltr, Cursor: CursorType.Default);

    private static readonly Color[] Colours =
    [
        new(205, 49, 49, 255), new(13, 188, 121, 255), new(229, 229, 16, 255), new(36, 114, 200, 255),
        new(188, 63, 188, 255), new(17, 168, 205, 255), new(229, 229, 229, 255), new(241, 76, 76, 255),
    ];

    private static readonly string[] Fields = ["PID", "42", "usr", "S", "0.3", "1.2", "R", "bash", "py", "x", "[]", "{}"];

    /// <summary>An htop- or diff-like row: sixty short fields, each in a colour of its own, the
    /// row numbered so no two rows draw the same glyphs.</summary>
    private static TextParagraph DenseRow(SkiaRenderContext ctx, int row)
    {
        var sb = new System.Text.StringBuilder($"{row,5} ");
        var spans = new List<TextSpan>();
        for (int k = 0; k < 60; k++)
        {
            int at = sb.Length;
            sb.Append(Fields[(row * 5 + k) % Fields.Length]);
            spans.Add(new TextSpan(at, sb.Length - at, Colours[(row + k) % Colours.Length]));
            sb.Append(' ');
        }
        return TextDrawAllocationTests.WithGlyphs(ctx.LayoutParagraph(sb.ToString(), Style(), 4000, spans.ToArray()));
    }

    private static void Frame(SkiaRenderContext ctx, IReadOnlyList<TextParagraph> rows, int first = 0, int count = int.MaxValue)
    {
        ctx.BeginFrame(W, H, 1f, Color.Black);
        for (int i = first; i < Math.Min(rows.Count, first + (long)count); i++)
            ctx.DrawParagraph(rows[i], new Point(4, 4 + (i - first) % 60 * 16));
        ctx.EndFrame();
    }

    /// <summary>A screen of 60 dense rows needs ~7,000 sub-blobs, more than the map's bound: it
    /// made every one of them again on every frame; now its second frame makes none, nor its
    /// third. A sweep while the screen is drawn lets go of rows it no longer shows and keeps the
    /// screen; and every blob made is given back once the surface goes.</summary>
    [Fact]
    public void ADenseScreen_OfMoreSubBlobsThanTheBound_MakesNoneOnItsSecondFrame()
    {
        var surface = new SkiaRasterRenderSurface();
        surface.Resize(W, H, 1f);
        var ctx = (SkiaRenderContext)surface.RenderContext;
        var rows = Enumerable.Range(1, 60).Select(r => DenseRow(ctx, r)).ToArray();
        var gone = Enumerable.Range(500, 10).Select(r => DenseRow(ctx, r)).ToArray();
        var added = Enumerable.Range(1_000, 80).Select(r => DenseRow(ctx, r)).ToArray();

        // The screen and ten rows it then scrolls away.
        Frame(ctx, [.. rows, .. gone]);
        long first = ctx.BlobsMade;
        Assert.True(ctx.CachedSubBlobCount > 3_000, $"the screen needs {ctx.CachedSubBlobCount} sub-blobs");
        Frame(ctx, rows);
        Frame(ctx, rows);
        Assert.Equal(first, ctx.BlobsMade);
        Assert.Equal(0, ctx.BlobsReleased);

        // The screen, and so many new rows beside it that the map sweeps: the ten rows go, the
        // screen stays.
        Frame(ctx, [.. rows, .. added]);
        Assert.True(ctx.BlobsReleased > 0, "the map never swept");
        long made = ctx.BlobsMade;
        Frame(ctx, rows);
        Assert.Equal(made, ctx.BlobsMade);
        Assert.Equal(ctx.CachedBlobCount + ctx.CachedSubBlobCount, ctx.BlobsMade - ctx.BlobsReleased);
        output.WriteLine($"first frame {first} blobs, then none; {ctx.BlobsReleased} let go; {ctx.CachedSubBlobCount} sub-blobs kept");

        surface.Dispose();
        Assert.Equal(ctx.BlobsMade, ctx.BlobsReleased);
    }

    /// <summary>A Chinese terminal draws each wide character on its own: a screen of more than
    /// 2,048 different ideographs needs a run blob for each. Its second frame makes none.</summary>
    [HanFontsFact]
    public void AChineseScreen_OfMoreRunsThanTheBound_MakesNoRunBlobsOnItsSecondFrame()
    {
        var surface = new SkiaRasterRenderSurface();
        surface.Resize(W, H, 1f);
        var ctx = (SkiaRenderContext)surface.RenderContext;
        var style = Style(15f, "sans-serif");
        var cells = Enumerable.Range(0, 3_000).Select(i => ctx.LayoutParagraph(((char)(0x4E00 + i)).ToString(), style, 100)).ToArray();
        void Draw()
        {
            ctx.BeginFrame(W, H, 1f, Color.Black);
            for (int i = 0; i < cells.Length; i++)
                ctx.DrawParagraph(cells[i], new Point(i % 80 * 20, i / 80 * 24));
            ctx.EndFrame();
        }

        Draw();
        long first = ctx.BlobsMade;
        Assert.True(ctx.CachedBlobCount > SkiaRenderContext.RunBlobCacheMaxSize, $"the screen needs {ctx.CachedBlobCount} run blobs");
        Draw();
        Assert.Equal(first, ctx.BlobsMade);
        output.WriteLine($"first frame {first} run blobs, then none");

        surface.Dispose();
        Assert.Equal(ctx.BlobsMade, ctx.BlobsReleased);
    }

    /// <summary>The first frame of a big screen sweeps nothing it drew: the map grows by doubling
    /// past its bound, and a sweep with nothing old to let go of looks at no entry. A sweep on
    /// every new blob past the bound was O(n²) while such a screen first filled.</summary>
    [Fact]
    public void ABigScreensFirstFrame_LooksAtNoEntryToSweep()
    {
        using var surface = new SkiaRasterRenderSurface();
        surface.Resize(W, H, 1f);
        var ctx = (SkiaRenderContext)surface.RenderContext;
        var rows = Enumerable.Range(1, 300).Select(r => DenseRow(ctx, r)).ToArray();

        Frame(ctx, rows);
        Assert.True(ctx.CachedSubBlobCount > 15_000, $"{ctx.CachedSubBlobCount} sub-blobs");
        Assert.Equal(0, ctx.SweptEntries);
        Assert.Equal(0, ctx.BlobsReleased);
    }

    /// <summary>A dense screen that stays, with one row that changes every frame (a status line, a
    /// clock): each sweep looks at every entry the screen keeps, so sweeps wait until the map has
    /// doubled. Sweeping as soon as the map grew past what the last sweep kept looked at the whole
    /// screen for every changed row.</summary>
    [Fact]
    public void ADenseScreenWithOneChangingRow_SweepsRarely()
    {
        using var surface = new SkiaRasterRenderSurface();
        surface.Resize(W, H, 1f);
        var ctx = (SkiaRenderContext)surface.RenderContext;
        var screen = Enumerable.Range(1, 60).Select(r => DenseRow(ctx, r)).ToArray();
        var changing = Enumerable.Range(1_000, 200).Select(r => DenseRow(ctx, r)).ToArray();
        long made = 0;
        for (int f = 0; f < changing.Length; f++)
        {
            ctx.BeginFrame(W, H, 1f, Color.Black);
            for (int i = 0; i < screen.Length; i++) ctx.DrawParagraph(screen[i], new Point(4, 4 + i * 16));
            if (f == 1) made = ctx.BlobsMade;
            ctx.DrawParagraph(changing[f], new Point(4, 980));
            ctx.EndFrame();
        }
        made = ctx.BlobsMade - made;
        Assert.True(ctx.BlobsReleased > 0, "nothing was let go");
        Assert.InRange(ctx.SweptEntries, 0, 4 * made);
        output.WriteLine($"{made} blobs made for the changing row, {ctx.SweptEntries} entries swept, {ctx.CachedSubBlobCount} held");
    }

    /// <summary>Paging down draws a screen of new rows a frame: the maps let the old ones go and
    /// stay within their bound, and every blob made is given back once.</summary>
    [Fact]
    public void PagingDown_KeepsTheMapsWithinTheirBound()
    {
        var surface = new SkiaRasterRenderSurface();
        surface.Resize(W, H, 1f);
        var ctx = (SkiaRenderContext)surface.RenderContext;
        const int Screen = 4;   // rows a screen: ~480 sub-blobs (one a field, one a gap), two screens under half the bound
        var rows = Enumerable.Range(1, 240 * Screen).Select(r => DenseRow(ctx, r)).ToArray();
        int most = 0, mostRuns = 0;
        for (int page = 0; page < 240; page++)
        {
            Frame(ctx, rows, page * Screen, Screen);
            most = Math.Max(most, ctx.CachedSubBlobCount);
            mostRuns = Math.Max(mostRuns, ctx.CachedBlobCount);
            Assert.InRange(ctx.CachedSubBlobCount, 0, SkiaRenderContext.RunBlobCacheMaxSize);
            Assert.InRange(ctx.CachedBlobCount, 0, SkiaRenderContext.RunBlobCacheMaxSize);
            Assert.Equal(ctx.CachedBlobCount + ctx.CachedSubBlobCount, ctx.BlobsMade - ctx.BlobsReleased);
        }
        Assert.True(ctx.BlobsReleased > 10_000, $"{ctx.BlobsReleased} released");
        // A sweep looks at the entries in the map, once per bound's worth of new ones.
        Assert.InRange(ctx.SweptEntries, 1, 3 * ctx.BlobsMade);
        output.WriteLine($"most sub-blobs held {most}, run blobs {mostRuns}; made {ctx.BlobsMade}, released {ctx.BlobsReleased}, swept {ctx.SweptEntries}");

        surface.Dispose();
        Assert.Equal(ctx.BlobsMade, ctx.BlobsReleased);
    }

    /// <summary>What two frames drew is kept only up to <see cref="SkiaRenderContext.MaxGlyphsKept"/>
    /// glyphs: past it a full map empties whole, as every full map did, and the next frame makes
    /// its blobs again.</summary>
    [Fact]
    public void AScreenOverTheGlyphCap_EmptiesTheMapWhole()
    {
        var surface = new SkiaRasterRenderSurface();
        surface.Resize(W, H, 1f);
        var ctx = (SkiaRenderContext)surface.RenderContext;
        int glyphsPerRow = SkiaRenderContext.MaxGlyphsKept / SkiaRenderContext.RunBlobCacheMaxSize + 8;
        var rows = Enumerable.Range(0, SkiaRenderContext.RunBlobCacheMaxSize + 100)
            .Select(i => TextDrawAllocationTests.WithGlyphs(ctx.LayoutParagraph($"{i,6} " + new string((char)('a' + i % 26), glyphsPerRow), Style(), 100_000)))
            .ToArray();

        Frame(ctx, rows);
        long first = ctx.BlobsMade;
        Assert.True(ctx.BlobsReleased >= SkiaRenderContext.RunBlobCacheMaxSize, $"{ctx.BlobsReleased} released");
        Assert.True(ctx.CachedBlobCount < SkiaRenderContext.RunBlobCacheMaxSize, $"{ctx.CachedBlobCount} held");
        Frame(ctx, rows);
        Assert.True(ctx.BlobsMade - first >= SkiaRenderContext.RunBlobCacheMaxSize, $"{ctx.BlobsMade - first} made again");
        Assert.Equal(ctx.CachedBlobCount + ctx.CachedSubBlobCount, ctx.BlobsMade - ctx.BlobsReleased);

        surface.Dispose();
        Assert.Equal(ctx.BlobsMade, ctx.BlobsReleased);
    }
}
