using System.Numerics;
using System.Runtime.InteropServices;
using HultUI.Core.Layout;
using HultUI.Core.Primitives;
using HultUI.Core.Rendering;
using HultUI.Core.Styling;
using HultUI.Testing;
using HultUI.Text;
using SkiaSharp;
using Xunit;

namespace HultUI.Rendering.Skia.Tests;

/// <summary>
/// A spanned line draws each colour stretch under a clip of its own. It drew every run that
/// overlaps the stretch, all of its glyphs, once per stretch: a line of syntax-coloured code in
/// six stretches handed Skia six times its glyphs (Junction's diff, 24,527 glyphs a hover frame).
/// Now a stretch draws a kept sub-blob of only the glyphs whose ink can reach its clip — the
/// face's bounding box around each glyph, with a margin — so the pixels are the same and the
/// glyphs about one pass of the line (plan 17, round 3).
/// </summary>
public class SpannedLineSubBlobTests
{
    private static ResolvedInheritedStyle Style(float size = 14f, FontWeight weight = FontWeight.Regular,
        FontSlant slant = FontSlant.Normal, string family = "sans-serif") => new(
        FontFamily: family, FontSize: size, FontWeight: weight,
        TextColor: new Color(32, 32, 32, 255), TextDirection: TextDirection.Ltr, Cursor: CursorType.Default,
        FontSlant: slant);

    // ── Which glyphs reach a stretch ──

    private static GlyphPosition[] Run(params float[] xs) =>
        xs.Select((x, i) => new GlyphPosition((uint)(i + 3), x, 0f, 7f)).ToArray();

    /// <summary>Every glyph whose ink [X + left, X + right] meets [x0, x1), by brute force.</summary>
    private static (int First, int Last) Brute(GlyphPosition[] glyphs, float left, float right, float x0, float x1)
    {
        int first = -1, last = -1;
        for (int i = 0; i < glyphs.Length; i++)
            if (glyphs[i].X + right > x0 && glyphs[i].X + left < x1)
            {
                if (first < 0) first = i;
                last = i;
            }
        return (first, last);
    }

    [Fact]
    public void GlyphsInXOrder_AreFoundExactly_ByTheBinarySearch()
    {
        var rng = new Random(17);
        for (int trial = 0; trial < 2_000; trial++)
        {
            // Advances of 0 (marks on their base, zero-width joiners) to 14.
            int n = rng.Next(1, 120);
            var xs = new float[n];
            for (int i = 1; i < n; i++) xs[i] = xs[i - 1] + (rng.Next(4) == 0 ? 0f : rng.NextSingle() * 14f);
            var glyphs = Run(xs);
            Assert.True(GlyphRange.XOrdered(glyphs));
            float left = -rng.NextSingle() * 6f, right = rng.NextSingle() * 16f;
            float x0 = rng.NextSingle() * (xs[^1] + 20f) - 10f, x1 = x0 + rng.NextSingle() * 60f;

            var (start, end) = GlyphRange.Reaching(glyphs, xOrdered: true, left, right, x0, x1);
            var (first, last) = Brute(glyphs, left, right, x0, x1);
            if (first < 0) Assert.Equal(start, end);
            else Assert.Equal((first, last + 1), (start, end));
        }
    }

    [Fact]
    public void GlyphsThatGoBackInX_AreAllCovered_ByOnePass()
    {
        // A mark pulled back over a wide base; a right-to-left cluster put back in order; a
        // glyph far to the left of where the run had got to.
        var marks = Run(0, 9, 4, 18, 27, 21, 36);
        var reordered = Run(0, 8, 30, 22, 14, 40, 48);
        Assert.False(GlyphRange.XOrdered(marks));
        Assert.False(GlyphRange.XOrdered(reordered));

        var rng = new Random(5);
        foreach (var glyphs in new[] { marks, reordered })
            for (int trial = 0; trial < 500; trial++)
            {
                float left = -rng.NextSingle() * 3f, right = rng.NextSingle() * 10f;
                float x0 = rng.NextSingle() * 60f - 5f, x1 = x0 + rng.NextSingle() * 20f;
                var (start, end) = GlyphRange.Reaching(glyphs, xOrdered: false, left, right, x0, x1);
                var (first, last) = Brute(glyphs, left, right, x0, x1);
                if (first < 0) Assert.Equal(start, end);
                else Assert.Equal((first, last + 1), (start, end));
            }

        // Searched as if in order, the mark at 4 would be missed: the search must not be used.
        var (s, e) = GlyphRange.Reaching(marks, xOrdered: false, 0f, 3f, 4.5f, 5f);
        Assert.Equal((2, 3), (s, e));
    }

    [Fact]
    public void NoGlyphReaching_GivesAnEmptyRange()
    {
        var glyphs = Run(0, 10, 20, 30);
        Assert.Equal(0, Width(GlyphRange.Reaching(glyphs, true, -1f, 8f, 100f, 120f)));
        Assert.Equal(0, Width(GlyphRange.Reaching(glyphs, false, -1f, 8f, 100f, 120f)));
        Assert.Equal(0, Width(GlyphRange.Reaching(glyphs, true, -1f, 8f, -40f, -2f)));
        Assert.Equal(0, Width(GlyphRange.Reaching([], true, -1f, 8f, 0f, 10f)));

        static int Width((int Start, int End) r) => r.End - r.Start;
    }

    // ── Faces ──

    /// <summary>A colour emoji face (bitmaps or colour layers, whose bounds Skia itself does not
    /// trust) has no ink extent: its runs are drawn whole in every stretch, as before.</summary>
    [Fact]
    public void AColourEmojiFace_HasNoInkExtent_SoItsRunsAreDrawnWhole()
    {
        using var surface = new SkiaRasterRenderSurface();
        surface.Resize(600, 100, 1f);
        var ctx = (SkiaRenderContext)surface.RenderContext;
        string emoji = "\U0001F600\U0001F389\U0001F44D\U0001F3FD\U0001F468‍\U0001F469‍\U0001F467\U0001F1F8\U0001F1EA\U0001F600\U0001F389";
        var paragraph = (PipelineTextParagraph)ctx.LayoutParagraph(emoji, Style(20f), 580, EveryFew(emoji, 2));
        var faces = paragraph.TextLines.SelectMany(l => l.Runs).Select(r => r.Font).Distinct().ToList();
        Assert.NotEmpty(faces);
        if (faces.All(f => ((SKTypeface)f.NativeHandle).GetTableSize(Tag("COLR")) == 0
                           && ((SKTypeface)f.NativeHandle).GetTableSize(Tag("CBDT")) == 0
                           && ((SKTypeface)f.NativeHandle).GetTableSize(Tag("sbix")) == 0))
            return;   // no colour emoji face on this machine: nothing to hold
        foreach (var face in faces.Where(IsColour))
            Assert.Null(ctx.InkExtentOf(face));

        ctx.BeginFrame(600, 100, 1f, Color.White);
        ctx.DrawParagraph(paragraph, new Point(10, 10));
        ctx.EndFrame();
        Assert.Equal(0, ctx.CachedSubBlobCount);

        static bool IsColour(IResolvedFont f) =>
            ((SKTypeface)f.NativeHandle).GetTableSize(Tag("COLR")) > 0
            || ((SKTypeface)f.NativeHandle).GetTableSize(Tag("CBDT")) > 0
            || ((SKTypeface)f.NativeHandle).GetTableSize(Tag("sbix")) > 0;
    }

    private static uint Tag(string t) => (uint)(t[0] << 24 | t[1] << 16 | t[2] << 8 | t[3]);

    /// <summary>The context reads Skia's "bounds invalid" flag from the first four bytes of
    /// <see cref="SKFontMetrics"/>, SkiaSharp's copy of sk_fontmetrics_t: that field must stay
    /// first.</summary>
    [Fact]
    public void SKFontMetrics_StartsWithSkiasFlags()
    {
        var field = typeof(SKFontMetrics).GetField("fFlags", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        Assert.NotNull(field);
        Assert.Equal(typeof(uint), field.FieldType);
        Assert.Equal(0, Marshal.OffsetOf<SKFontMetrics>("fFlags").ToInt32());
        Assert.True(typeof(SKFontMetrics).IsLayoutSequential);
    }

    /// <summary>
    /// What the sub-blobs stand on: no glyph inks outside its face's extent. Every glyph of a
    /// corpus in every script and style used here, its bounds as Skia rasterizes it (synthetic
    /// bold and oblique included, where the machine's faces need them), sits inside the extent
    /// the context gives its face, scaled to the size.
    /// </summary>
    [Fact]
    public void EveryGlyphOfTheCorpus_InksInsideItsFacesExtent()
    {
        using var surface = new SkiaRasterRenderSurface();
        surface.Resize(800, 200, 1f);
        var ctx = (SkiaRenderContext)surface.RenderContext;
        using var font = new SKFont { Subpixel = true };
        int checkedGlyphs = 0;
        foreach (var style in Styles())
            foreach (var text in Corpus)
            {
                var paragraph = (PipelineTextParagraph)ctx.LayoutParagraph(text, style, float.PositiveInfinity);
                foreach (var run in paragraph.TextLines.SelectMany(l => l.Runs))
                {
                    if (ctx.InkExtentOf(run.Font) is not { } ink) continue;
                    font.Typeface = (SKTypeface)run.Font.NativeHandle;
                    font.Size = run.FontSize;
                    var ids = run.Glyphs.Select(g => (ushort)g.GlyphId).ToArray();
                    var bounds = new SKRect[ids.Length];
                    font.GetGlyphWidths(ids, null, bounds);
                    for (int i = 0; i < ids.Length; i++)
                    {
                        if (bounds[i].IsEmpty) continue;
                        checkedGlyphs++;
                        // Within a pixel (the mask's rounding) of the extent; the drawing margin is two.
                        Assert.True(bounds[i].Left >= ink.Left * run.FontSize - 1f && bounds[i].Right <= ink.Right * run.FontSize + 1f,
                            $"glyph {ids[i]} of '{run.Text}' ({font.Typeface.FamilyName} {style.FontWeight} {style.FontSlant}, {run.FontSize}px) " +
                            $"inks {bounds[i].Left:F2}..{bounds[i].Right:F2}, outside {ink.Left * run.FontSize:F2}..{ink.Right * run.FontSize:F2}");
                    }
                }
            }
        Assert.True(checkedGlyphs > 500, $"only {checkedGlyphs} glyphs checked");
    }

    // ── Pixels ──

    private static readonly string[] Corpus =
    [
        "Plain ASCII: The quick brown fox jumps over the lazy dog, 0123456789 {}[]();",
        "Hebrew שלום עולם, Arabic مرحبا بالعالم, numbers 12345 between them.",
        "Marks: é ä ô ñ ạ̀ Z̶a̶l̶g̶o̶ x̣̂̄ and Devanagari नमस्ते क्षत्रिय",
        "Emoji \U0001F600\U0001F389 family \U0001F468‍\U0001F469‍\U0001F467 flag \U0001F1F8\U0001F1EA keycap #️⃣ heart ❤️ end",
        "CJK 東京都 漢字かなカナ 한국어 mixed with Latin text",
        "Thai ภาษาไทยสวัสดี and ligatures ffi fl ff office waffle fjord",
        "Overhangs: f j y g / \\ W V A T Y ƒ ʃ italics fly far",
    ];

    private static IEnumerable<ResolvedInheritedStyle> Styles()
    {
        yield return Style(9f);
        yield return Style(13f);
        yield return Style(17.5f, slant: FontSlant.Italic);
        yield return Style(24f, FontWeight.Bold);
        yield return Style(40f, FontWeight.Bold, FontSlant.Italic);
        yield return Style(15f, family: "monospace");
    }

    // A colour change every `every` characters, so stretch edges cut through clusters and glyphs.
    private static TextSpan[] EveryFew(string text, int every)
    {
        var colours = new[] { new Color(192, 0, 0, 255), new Color(0, 128, 0, 255), new Color(0, 0, 192, 255), new Color(160, 160, 0, 255) };
        var spans = new List<TextSpan>();
        for (int at = 0, c = 0; at < text.Length; at += every, c++)
            spans.Add(new TextSpan(at, Math.Min(every, text.Length - at), colours[c % colours.Length]));
        return spans.ToArray();
    }

    private static (byte[] Pixels, long Glyphs, int SubBlobs) Draw(float scale, bool subBlobs, Matrix3x2? transform = null)
    {
        const int W = 900, H = 1_000;
        using var surface = new SkiaRasterRenderSurface();
        surface.Resize((int)(W * scale), (int)(H * scale), scale);
        var ctx = (SkiaRenderContext)surface.RenderContext;
        ctx.DrawsStretchesFromSubBlobs = subBlobs;
        ctx.BeginFrame(W, H, scale, Color.White);
        if (transform is { } m) ctx.PushTransform(m);
        float y = 4;
        int every = 1;
        foreach (var style in Styles())
            foreach (var text in Corpus)
            {
                var spans = EveryFew(text, every);
                every = every % 3 + 1;
                // Through both drawing paths: DrawText lays out, DrawParagraph draws laid-out lines.
                if ((int)y % 2 == 0)
                    ctx.DrawText(text, new Rect(6, y, W - 12, 200), style, TextOverflow.Clip, null, spans);
                else
                    ctx.DrawParagraph(ctx.LayoutParagraph(text, style, W - 12, spans), new Point(6, y));
                y += style.FontSize * 1.1f;
                if (y > H) y -= H - 3;   // wrap round: overlapping lines draw over each other, also the same
            }
        if (transform is not null) ctx.Pop();
        ctx.EndFrame();
        surface.Present();
        return (surface.ReadPixels(out _, out _, out _), ctx.GlyphsDrawn, ctx.CachedSubBlobCount);
    }

    [Theory]
    [InlineData(1f)]
    [InlineData(1.5f)]
    [InlineData(2f)]
    public void SpannedLines_DrawTheSamePixelsFromSubBlobs_WithFarFewerGlyphs(float scale)
    {
        var whole = Draw(scale, subBlobs: false);
        var parts = Draw(scale, subBlobs: true);

        Assert.Equal(0, whole.SubBlobs);
        Assert.True(parts.SubBlobs > 100, $"{parts.SubBlobs} sub-blobs");
        Assert.True(whole.Pixels.AsSpan().SequenceEqual(parts.Pixels), DescribeDifference(whole.Pixels, parts.Pixels, scale));
        // A stretch every one to three characters: whole runs went to Skia once a stretch, the
        // parts about a glyph's width either side of each (52,306 -> ~15,500 at every scale).
        Assert.True(parts.Glyphs * 3 < whole.Glyphs, $"{whole.Glyphs} -> {parts.Glyphs} glyphs handed to Skia");
    }

    /// <summary>The pixel corpus takes both ways to a stretch's glyphs: runs in X order (the
    /// binary search) and runs whose X goes back (the pass over the run).</summary>
    [Fact]
    public void TheCorpus_HasRunsInXOrder_AndRunsWhoseXGoesBack()
    {
        using var surface = new SkiaRasterRenderSurface();
        surface.Resize(900, 200, 1f);
        var ctx = surface.RenderContext;
        int ordered = 0, back = 0;
        foreach (var style in Styles())
            foreach (var text in Corpus)
                foreach (var run in ((PipelineTextParagraph)ctx.LayoutParagraph(text, style, 888)).TextLines.SelectMany(l => l.Runs))
                    if (GlyphRange.XOrdered(run.Glyphs)) ordered++;
                    else back++;
        Assert.True(ordered > 0 && back > 0, $"{ordered} runs in X order, {back} going back");
    }

    public static TheoryData<string, float, float, float, float> Transforms() => new()
    {
        // name, scale x, scale y, rotation (degrees), device scale
        { "scaled down", 0.3f, 0.3f, 0f, 1f },
        { "scaled down, 1.5", 0.45f, 0.45f, 0f, 1.5f },
        { "stretched", 2.5f, 0.6f, 0f, 1f },
        { "rotated", 1f, 1f, 7f, 1f },
        { "rotated and scaled", 0.7f, 0.7f, -33f, 1.5f },
    };

    /// <summary>Under a transform a device pixel is more than one layout unit (scaled down), or
    /// x is no longer horizontal (rotated): the margin follows the canvas matrix.</summary>
    [Theory]
    [MemberData(nameof(Transforms))]
    public void UnderATransform_TheSamePixels(string name, float sx, float sy, float degrees, float scale)
    {
        var m = Matrix3x2.CreateScale(sx, sy) * Matrix3x2.CreateRotation(degrees * MathF.PI / 180f) * Matrix3x2.CreateTranslation(40, 30);
        var whole = Draw(scale, subBlobs: false, m);
        var parts = Draw(scale, subBlobs: true, m);
        Assert.True(whole.Pixels.AsSpan().SequenceEqual(parts.Pixels), name + ": " + DescribeDifference(whole.Pixels, parts.Pixels, scale));
    }

    private static string DescribeDifference(byte[] a, byte[] b, float scale)
    {
        if (a.Length != b.Length) return $"lengths {a.Length} and {b.Length}";
        int width = (int)(900 * scale), count = 0, first = -1;
        for (int i = 0; i < a.Length; i += 4)
            if (a[i] != b[i] || a[i + 1] != b[i + 1] || a[i + 2] != b[i + 2] || a[i + 3] != b[i + 3])
            {
                if (first < 0) first = i / 4;
                count++;
            }
        return count == 0 ? "identical" : $"{count} pixels differ, the first at ({first % width}, {first / width})";
    }

    // ── Allocation and lifetimes ──

    private static TextSpan[] Thirds(string text) =>
    [
        new(0, text.Length / 3, new Color(86, 156, 214, 255)),
        new(text.Length / 3, text.Length / 3, new Color(156, 220, 254, 255)),
        new(2 * (text.Length / 3), text.Length - 2 * (text.Length / 3), new Color(206, 145, 120, 255)),
    ];

    /// <summary>A sub-blob is a native handle like a run's blob, kept in a map the context sizes
    /// to its bound when it makes the first: a new one allocates nothing.</summary>
    [Fact]
    public void ANewSubBlob_AllocatesNothing()
    {
        using var surface = new SkiaRasterRenderSurface();
        surface.Resize(800, 100, 1f);
        var ctx = (SkiaRenderContext)surface.RenderContext;
        var style = Style(12f);
        const int Round = 200;
        var rows = new TextParagraph[SkiaRenderContext.RunBlobCacheMaxSize + Allocations.MaxRounds * Round];
        for (int i = 0; i < rows.Length; i++)
        {
            string text = $"    var item{i} = Compute({i % 997}, \"value {i}\"); // note {i}";
            rows[i] = ctx.LayoutParagraph(text, style, 780, Thirds(text));
        }
        ctx.BeginFrame(800, 100, 1f, Color.White);
        // A few rows first, so the measured ones fill a map that has not grown yet: a map made
        // small would grow under them (and the run blobs' cache, grown by then, would not).
        int next = 0;
        for (; next < 20; next++) ctx.DrawParagraph(rows[next], new Point(4, 4));
        for (int i = 0; i <= SkiaRenderContext.RunBlobCacheMaxSize; i++)
            ctx.DrawText($"Row {i} of the list", new Rect(4, 40, 780, 30), style, TextOverflow.Clip, null);
        Assert.True(ctx.CachedSubBlobCount > 0);

        long subBlobsBefore = ctx.BlobsMade;
        // One round on its own: hundreds of sub-blobs into the map the first ones made. Grown
        // from a default capacity it was ~300 B a row; a one-off retired allocation context
        // (see Allocations) is at most ~40.
        long first = GC.GetAllocatedBytesForCurrentThread();
        for (int i = next; i < next + Round; i++) ctx.DrawParagraph(rows[i], new Point(4, 4));
        next += Round;
        double firstPerRow = (GC.GetAllocatedBytesForCurrentThread() - first) / (double)Round;
        Assert.True(firstPerRow < 40, $"the sub-blob map grew: {firstPerRow:0.#} B a row");

        long bytes = Allocations.Least(() =>
        {
            for (int i = next; i < next + Round; i++) ctx.DrawParagraph(rows[i], new Point(4, 4));
            next += Round;
        });
        ctx.EndFrame();
        Assert.True(ctx.BlobsMade - subBlobsBefore >= 2 * Round, $"{ctx.BlobsMade - subBlobsBefore} blobs made");
        Assert.True(bytes / (double)Round < 1, $"a new row of three stretches allocated {bytes / (double)Round:0.#} B");
    }

    [Fact]
    public void SubBlobs_AreReleasedOnce_ThroughCacheChurnAndDispose()
    {
        var surface = new SkiaRasterRenderSurface();
        surface.Resize(800, 100, 1f);
        var ctx = (SkiaRenderContext)surface.RenderContext;
        var style = Style(12f);
        ctx.BeginFrame(800, 100, 1f, Color.White);
        for (int i = 0; i < 3 * SkiaRenderContext.RunBlobCacheMaxSize; i++)
        {
            string text = $"    var item{i} = Compute({i % 997}, \"value {i}\"); // note {i}";
            ctx.DrawText(text, new Rect(4, 4, 780, 30), style, TextOverflow.Clip, null, Thirds(text));
            Assert.Equal(ctx.CachedBlobCount + ctx.CachedSubBlobCount, ctx.BlobsMade - ctx.BlobsReleased);
        }
        ctx.EndFrame();
        Assert.True(ctx.BlobsReleased > SkiaRenderContext.RunBlobCacheMaxSize, $"{ctx.BlobsReleased} released");

        surface.Dispose();
        Assert.Equal(0, ctx.CachedSubBlobCount);
        Assert.Equal(ctx.BlobsMade, ctx.BlobsReleased);
    }
}
