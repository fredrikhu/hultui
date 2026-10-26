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
using Xunit.Abstractions;

namespace HultUI.Rendering.Skia.Tests;

/// <summary>
/// A spanned line draws each colour stretch under a clip of its own. It drew every run that
/// overlaps the stretch, all of its glyphs, once per stretch: a line of syntax-coloured code in
/// six stretches handed Skia six times its glyphs (Junction's diff, 24,527 glyphs a hover frame).
/// Now a stretch draws a kept sub-blob of only the glyphs whose ink can reach its clip — the
/// face's bounding box around each glyph, with a margin — so the pixels are the same and the
/// glyphs about one pass of the line (plan 17, round 3). A face whose box Skia marks as a guess
/// (a variable font: Cascadia Mono, Segoe UI Variable, Fedora's Noto Sans) drew its runs whole
/// until each run took its extent from its own glyphs' bounds (round 4).
/// </summary>
public class SpannedLineSubBlobTests(ITestOutputHelper output)
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
    /// What the sub-blobs stand on: no glyph inks outside its run's extent. Every glyph of a
    /// corpus in every script and style used here, its bounds as Skia rasterizes it (synthetic
    /// bold and oblique included, where the machine's faces need them), sits inside the extent
    /// the context gives its run — its face's box, or its own glyphs' bounds for a face whose box
    /// is a guess — scaled to the size.
    /// </summary>
    [Fact]
    public void EveryGlyphOfTheCorpus_InksInsideItsRunsExtent()
    {
        using var surface = new SkiaRasterRenderSurface();
        surface.Resize(800, 200, 1f);
        var ctx = (SkiaRenderContext)surface.RenderContext;
        using var font = new SKFont { Subpixel = true };
        int checkedGlyphs = 0, fromRuns = 0;
        foreach (var style in Styles())
            foreach (var text in Corpus)
            {
                var paragraph = (PipelineTextParagraph)ctx.LayoutParagraph(text, style, float.PositiveInfinity);
                foreach (var run in paragraph.TextLines.SelectMany(l => l.Runs))
                {
                    if (ctx.RunInkExtentOf(run) is not { } ink) continue;
                    bool ownGlyphs = ctx.InkExtentOf(run.Font) is null;
                    font.Typeface = (SKTypeface)run.Font.NativeHandle;
                    font.Size = run.FontSize;
                    var ids = run.Glyphs.Select(g => (ushort)g.GlyphId).ToArray();
                    var bounds = new SKRect[ids.Length];
                    font.GetGlyphWidths(ids, null, bounds);
                    for (int i = 0; i < ids.Length; i++)
                    {
                        if (bounds[i].IsEmpty) continue;
                        checkedGlyphs++;
                        if (ownGlyphs) fromRuns++;
                        // Within a pixel (the mask's rounding) of the extent; the drawing margin is two.
                        Assert.True(bounds[i].Left >= ink.Left * run.FontSize - 1f && bounds[i].Right <= ink.Right * run.FontSize + 1f,
                            $"glyph {ids[i]} of '{run.Text}' ({font.Typeface.FamilyName} {style.FontWeight} {style.FontSlant}, {run.FontSize}px) " +
                            $"inks {bounds[i].Left:F2}..{bounds[i].Right:F2}, outside {ink.Left * run.FontSize:F2}..{ink.Right * run.FontSize:F2}");
                    }
                }
            }
        Assert.True(checkedGlyphs > 500, $"only {checkedGlyphs} glyphs checked");
        if (VariableFonts.Family is not null)
            Assert.True(fromRuns > 200, $"only {fromRuns} glyphs checked against their run's own extent");
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
        // A variable face, whose runs give their own extents, wherever one is installed.
        if (VariableFonts.Family is { } variable)
        {
            yield return Style(12f, family: variable);
            yield return Style(21f, FontWeight.Bold, FontSlant.Italic, variable);
        }
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

    private static (byte[] Pixels, long Glyphs, int SubBlobs, int RunExtents) Draw(float scale, bool subBlobs, Matrix3x2? transform = null)
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
        return (surface.ReadPixels(out _, out _, out _), ctx.GlyphsDrawn, ctx.CachedSubBlobCount, (int)ctx.RunExtentsFromGlyphs);
    }

    [Theory]
    [InlineData(1f)]
    [InlineData(1.5f)]
    [InlineData(2f)]
    public void SpannedLines_DrawTheSamePixelsFromSubBlobs_WithFewerGlyphs(float scale)
    {
        var whole = Draw(scale, subBlobs: false);
        var parts = Draw(scale, subBlobs: true);

        Assert.Equal(0, whole.SubBlobs);
        Assert.True(parts.SubBlobs > 100, $"{parts.SubBlobs} sub-blobs");
        // The corpus reaches runs whose extent is their own wherever a variable face is installed.
        if (VariableFonts.Family is not null)
            Assert.True(parts.RunExtents > 10, $"{parts.RunExtents} runs took their own extent");
        AssertSamePixels(whole.Pixels, parts.Pixels, () => Draw(scale, subBlobs: false).Pixels,
            () => Draw(scale, subBlobs: true).Pixels, $"at {scale}x", scale);
        // How many fewer depends on the machine's faces: 70,424 -> ~12,100 on Windows, 71,133 ->
        // ~9,700 on Fedora (whose Noto Sans is variable). The win on a line of code is held below.
        Assert.True(parts.Glyphs < whole.Glyphs, $"{whole.Glyphs} -> {parts.Glyphs} glyphs handed to Skia");
        output.WriteLine($"at {scale}x: {whole.Glyphs:N0} -> {parts.Glyphs:N0} glyphs handed to Skia, {parts.RunExtents} runs with their own extent (variable face: {VariableFonts.Family ?? "none"})");
    }

    /// <summary>
    /// Holds the sub-blobs' pixels to whole runs', telling the drawing's differences from the
    /// glyph cache's. DirectWrite draws some glyph masks a little differently when Skia makes them
    /// again after its glyph cache (2 MB by default, shared by every test drawing text at the same
    /// time) let them go: two draws of the same text can differ by a few pixels (plan 17, round 4:
    /// 4–5 of 120 frames, up to 51 levels). So a difference sends the reference to be drawn again,
    /// and the sub-blobs may match either draw of it; a difference that is the drawing's is there
    /// every time, so it fails after three tries.
    /// </summary>
    private static void AssertSamePixels(byte[] whole, byte[] parts, Func<byte[]> drawWhole, Func<byte[]> drawParts,
        string what, float scale)
    {
        for (int attempt = 1; ; attempt++)
        {
            if (whole.AsSpan().SequenceEqual(parts)) return;
            var again = drawWhole();
            if (again.AsSpan().SequenceEqual(parts)) return;
            if (attempt == 3)
                Assert.Fail($"{what}: {DescribeDifference(whole, parts, scale)}"
                    + (again.AsSpan().SequenceEqual(whole) ? "" : $"; the whole runs drawn again: {DescribeDifference(whole, again, scale)}"));
            whole = again;
            parts = drawParts();
        }
    }

    /// <summary>
    /// A generic family whose face here has a box Skia trusts. A variable font's box is the
    /// default instance's, so Skia marks it as a guess and its runs give their own extents:
    /// Cascadia Mono (Windows' monospace) and Noto Sans (Fedora's sans-serif) are variable;
    /// Segoe UI, Consolas and the DejaVu faces are not.
    /// </summary>
    private static string FamilyWithAnExtent(SkiaRenderContext ctx)
    {
        foreach (var family in new[] { "monospace", "sans-serif", "serif" })
        {
            var paragraph = (PipelineTextParagraph)ctx.LayoutParagraph("var x = 1;", Style(12f, family: family), 400);
            if (paragraph.TextLines.SelectMany(l => l.Runs).All(r => ctx.InkExtentOf(r.Font) is not null))
                return family;
        }
        throw new InvalidOperationException("no generic family here has a face with a box Skia trusts");
    }

    private const string CodeLine = "        var result = Compute(items[index], \"value\", 42); // keep the totals";

    private static readonly TextSpan[] CodeLineSpans =
    [
        new(8, 3, new Color(86, 156, 214, 255)),
        new(19, 7, new Color(220, 220, 170, 255)),
        new(27, 5, new Color(156, 220, 254, 255)),
        new(41, 7, new Color(206, 145, 120, 255)),
        new(50, 2, new Color(181, 206, 168, 255)),
        new(55, CodeLine.Length - 55, new Color(106, 153, 85, 255)),
    ];

    // Glyphs handed to Skia for the line of code in six stretches, in the family given.
    private static long CodeLineGlyphs(string family, bool subBlobs)
    {
        using var surface = new SkiaRasterRenderSurface();
        surface.Resize(800, 60, 1f);
        var ctx = (SkiaRenderContext)surface.RenderContext;
        ctx.DrawsStretchesFromSubBlobs = subBlobs;
        ctx.BeginFrame(800, 60, 1f, Color.White);
        ctx.DrawText(CodeLine, new Rect(4, 4, 790, 30), Style(13f, family: family), TextOverflow.Clip, null, CodeLineSpans);
        ctx.EndFrame();
        return ctx.GlyphsDrawn;
    }

    /// <summary>
    /// The win: a line of code coloured in six stretches handed Skia its glyphs once a stretch;
    /// now each stretch's own and its neighbours' within the face's box.
    /// </summary>
    [Fact]
    public void ALineOfCodeInSixStretches_HandsSkiaAFractionOfTheGlyphs()
    {
        string family;
        using (var probe = new SkiaRasterRenderSurface()) family = FamilyWithAnExtent((SkiaRenderContext)probe.RenderContext);
        long whole = CodeLineGlyphs(family, false), parts = CodeLineGlyphs(family, true);
        Assert.True(parts * 2 < whole, $"{whole} -> {parts} glyphs handed to Skia");
    }

    /// <summary>
    /// The same in a variable face, whose box is a guess: its runs drew whole in every stretch
    /// until each took its extent from its own glyphs (Windows' monospace, Cascadia Mono, 900 -> 97
    /// glyphs for this line).
    /// </summary>
    [VariableFontFact]
    public void ALineOfCodeInAVariableFace_HandsSkiaAFractionOfTheGlyphs()
    {
        long whole = CodeLineGlyphs(VariableFonts.Family!, false), parts = CodeLineGlyphs(VariableFonts.Family!, true);
        Assert.True(parts * 2 < whole, $"{whole} -> {parts} glyphs handed to Skia in {VariableFonts.Family}");
    }

    /// <summary>A variable face has no extent of its own; each of its runs has one, from its
    /// glyphs: at least the slack either side of the origin. Each glyph's bounds are read from
    /// Skia once, for the face: asked again, or for another run of the same glyphs, the extent is
    /// the same and nothing more is read.</summary>
    [VariableFontFact]
    public void AVariableFace_HasNoBox_SoEachRunGivesItsOwnExtent()
    {
        using var surface = new SkiaRasterRenderSurface();
        surface.Resize(800, 100, 1f);
        var ctx = (SkiaRenderContext)surface.RenderContext;
        var paragraph = (PipelineTextParagraph)ctx.LayoutParagraph(CodeLine, Style(13f, family: VariableFonts.Family!), 780);
        var run = paragraph.TextLines[0].Runs[0];
        Assert.Null(ctx.InkExtentOf(run.Font));
        var extent = ctx.RunInkExtentOf(run);
        Assert.NotNull(extent);
        Assert.InRange(extent!.Value.Left, -1.5f, -0.125f);
        Assert.InRange(extent.Value.Right, 0.125f, 2.5f);
        Assert.Equal(run.Glyphs.Select(g => (ushort)g.GlyphId).Distinct().Count(), ctx.GlyphInksRead);
        long read = ctx.GlyphInksRead;
        Assert.Equal(extent, ctx.RunInkExtentOf(run));
        var again = ((PipelineTextParagraph)ctx.LayoutParagraph(CodeLine + " ", Style(13f, family: VariableFonts.Family!), 780)).TextLines[0].Runs[0];
        Assert.NotSame(run, again);
        Assert.Equal(extent, ctx.RunInkExtentOf(again));
        Assert.Equal(read, ctx.GlyphInksRead);
    }

    /// <summary>
    /// A run's extent is the union of its glyphs' bounds, read from the face's table now rather
    /// than kept per run: the same floats, to the bit, as reading the run's glyphs from Skia at
    /// once (the extents were kept per run until plan 17 round 5). Runs are asked in an order that
    /// fills the table from other runs first, some twice, longer than a piece of the reads, and
    /// with glyphs repeated.
    /// </summary>
    [VariableFontFact]
    public void ARunsExtent_IsItsGlyphsBoundsAsSkiaGivesThemForTheRun_ToTheBit()
    {
        using var surface = new SkiaRasterRenderSurface();
        surface.Resize(800, 100, 1f);
        var ctx = (SkiaRenderContext)surface.RenderContext;
        using var reference = new SKFont { LinearMetrics = true, Size = 2048f };
        var rng = new Random(23);
        string alphabet = string.Concat(Enumerable.Range(0x21, 0x5E).Select(c => (char)c))
            + "áéíóúñçøåäöüßÀÉÎÕÜŁŒŠŽƒ ΑΒΓΔαβγδλπωЖЗИЙжзийя ─│┌┐└┘├┤┬┴┼═║╔╗ ←↑→↓ ‰€™ ﬁﬂ";
        var texts = new List<string>(Corpus) { CodeLine, new string('.', 300) + "W" + new string('i', 40) };
        for (int t = 0; t < 400; t++)
        {
            var chars = new char[1 + rng.Next(120)];
            for (int i = 0; i < chars.Length; i++) chars[i] = alphabet[rng.Next(alphabet.Length)];
            texts.Add(new string(chars));
        }
        var runs = new List<ShapedRun>();
        foreach (var style in new[] { Style(12f, family: VariableFonts.Family!), Style(21f, FontWeight.Bold, FontSlant.Italic, VariableFonts.Family!) })
            foreach (var text in texts)
                runs.AddRange(((PipelineTextParagraph)ctx.LayoutParagraph(text, style, float.PositiveInfinity)).TextLines.SelectMany(l => l.Runs));
        // Shuffled, and half of them asked twice.
        var order = runs.Concat(runs.Where((_, i) => i % 2 == 0)).OrderBy(_ => rng.Next()).ToList();
        int fromGlyphs = 0;
        foreach (var run in order)
        {
            if (ctx.RunInkExtentOf(run) is not { } extent || ctx.InkExtentOf(run.Font) is not null) continue;
            fromGlyphs++;
            var expected = Reference(run);
            Assert.True(Bits(expected.Left) == Bits(extent.Left) && Bits(expected.Right) == Bits(extent.Right),
                $"'{run.Text}' in {((SKTypeface)run.Font.NativeHandle).FamilyName}: {extent.Left:R}..{extent.Right:R}, read whole {expected.Left:R}..{expected.Right:R}");
        }
        Assert.True(fromGlyphs > 500, $"only {fromGlyphs} runs took their extent from their glyphs");
        output.WriteLine($"{fromGlyphs} runs, {ctx.GlyphInksRead} glyph bounds read");

        // The old reading: all the run's glyphs from Skia at once, joined in order.
        SkiaRenderContext.InkExtent Reference(ShapedRun run)
        {
            var typeface = (SKTypeface)run.Font.NativeHandle;
            reference.Typeface = typeface;
            var ids = run.Glyphs.Select(g => (ushort)g.GlyphId).ToArray();
            var bounds = new SKRect[ids.Length];
            reference.GetGlyphWidths(ids, null, bounds);
            float left = 0f, right = 0f;
            foreach (var b in bounds)
            {
                if (b.IsEmpty) continue;
                left = MathF.Min(left, b.Left);
                right = MathF.Max(right, b.Right);
            }
            float slack = typeface.FontSlant == SKFontStyleSlant.Upright ? 0.125f : 0.375f;
            return float.IsFinite(left) && float.IsFinite(right)
                ? new(left / 2048f - slack, right / 2048f + slack)
                : new(float.NegativeInfinity, float.PositiveInfinity);
        }

        static int Bits(float f) => BitConverter.SingleToInt32Bits(f);
    }

    /// <summary>Whether a run's glyphs are in X order is found in the pass that takes its extent
    /// from its glyphs, and must agree with <see cref="GlyphRange.XOrdered"/>: a run whose X goes
    /// back searched as if in order would miss glyphs that reach a stretch.</summary>
    [VariableFontFact]
    public void ARunsGlyphOrder_FoundWithItsExtent_IsGlyphRangesOrder()
    {
        using var surface = new SkiaRasterRenderSurface();
        surface.Resize(800, 100, 1f);
        var ctx = (SkiaRenderContext)surface.RenderContext;
        var run = ((PipelineTextParagraph)ctx.LayoutParagraph(CodeLine, Style(13f, family: VariableFonts.Family!), 780)).TextLines[0].Runs[0];
        Assert.Null(ctx.InkExtentOf(run.Font));
        var glyphs = run.Glyphs.ToArray();
        // A mark pulled back over the glyph before it, at the start, the middle and the end.
        foreach (int at in new[] { 1, glyphs.Length / 2, glyphs.Length - 1 })
        {
            var pulled = (GlyphPosition[])glyphs.Clone();
            pulled[at] = pulled[at] with { X = pulled[at - 1].X - 0.5f };
            Check(new ShapedRun(run.Text, run.Font, run.FontSize, run.Width, pulled), false);
        }
        Check(new ShapedRun(run.Text, run.Font, run.FontSize, run.Width, glyphs), true);

        void Check(ShapedRun r, bool expected)
        {
            Assert.Equal(expected, GlyphRange.XOrdered(r.Glyphs));
            Assert.NotNull(ctx.RunInkExtentAndOrderOf(r, out bool ordered));
            Assert.Equal(expected, ordered);
        }
    }

    /// <summary>A run's extent keeps nothing of the run: once the text caches let a run go, it
    /// is gone, however many lines the context drew from it (the extents kept per run held up to
    /// 2,048 runs, 2.29 MB after a 20,000-line Cascadia document closed).</summary>
    [VariableFontFact]
    public void ARunsExtent_KeepsTheRunAlive_NoLonger()
    {
        using var surface = new SkiaRasterRenderSurface();
        surface.Resize(800, 100, 1f);
        var ctx = (SkiaRenderContext)surface.RenderContext;
        var gone = ExtentOfARunNobodyKeeps(ctx);
        for (int i = 0; i < 3 && gone.IsAlive; i++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
        }
        Assert.False(gone.IsAlive, "the context kept a run alive after taking its extent");
        GC.KeepAlive(ctx);
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static WeakReference ExtentOfARunNobodyKeeps(SkiaRenderContext ctx)
    {
        // A pipeline of its own, so no text cache keeps the run either.
        var pipeline = new TextPipeline(new SkiaFontCollection(), new SkiaTextShaper());
        var layout = pipeline.Layout(CodeLine, Style(13f, family: VariableFonts.Family!), 780, null, TextOverflow.Clip, null);
        var run = layout.Lines[0].Runs[0];
        Assert.NotNull(ctx.RunInkExtentOf(run));
        Assert.Null(ctx.InkExtentOf(run.Font));
        return new WeakReference(run);
    }

    /// <summary>A run longer than the pieces its bounds are read in takes every glyph's: a wide
    /// one at the end of 300 narrow ones reaches as far right as it does alone.</summary>
    [VariableFontFact]
    public void ALongRunsExtent_TakesEveryGlyph()
    {
        using var surface = new SkiaRasterRenderSurface();
        surface.Resize(800, 100, 1f);
        var ctx = (SkiaRenderContext)surface.RenderContext;
        var style = Style(13f, family: VariableFonts.Family!);
        ShapedRun Only(string text)
        {
            var runs = ((PipelineTextParagraph)ctx.LayoutParagraph(text, style, float.PositiveInfinity)).TextLines[0].Runs;
            Assert.Single(runs);
            return runs[0];
        }
        var wide = ctx.RunInkExtentOf(Only("W"))!.Value;
        var run = Only(new string('.', 300) + "W");
        Assert.True(run.Glyphs.Length > 256, $"{run.Glyphs.Length} glyphs");
        var extent = ctx.RunInkExtentOf(run)!.Value;
        Assert.True(extent.Right >= wide.Right && extent.Left <= wide.Left,
            $"the long run inks {extent.Left:F3}..{extent.Right:F3} em, its W alone {wide.Left:F3}..{wide.Right:F3}");
    }

    /// <summary>The pixel corpus takes both ways to a stretch's glyphs: runs in X order (the
    /// binary search) and runs whose X goes back (the pass over the run). Runs go back only where
    /// a font shapes Devanagari's pre-base vowel sign, so the test needs one.</summary>
    [DevanagariFontsFact]
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
        AssertSamePixels(Draw(scale, subBlobs: false, m).Pixels, Draw(scale, subBlobs: true, m).Pixels,
            () => Draw(scale, subBlobs: false, m).Pixels, () => Draw(scale, subBlobs: true, m).Pixels, name, scale);
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

    // Draws count items, perFrame a frame: as a list scrolled through, whose blobs the maps let
    // go once two frames have passed without them.
    private static void InFrames(SkiaRenderContext ctx, int count, int perFrame, Action<int> draw)
    {
        for (int i = 0; i < count; i++)
        {
            if (i % perFrame == 0) ctx.BeginFrame(800, 100, 1f, Color.White);
            draw(i);
            if (i % perFrame == perFrame - 1 || i == count - 1) ctx.EndFrame();
        }
    }

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
        var style = Style(12f, family: FamilyWithAnExtent(ctx));
        const int Round = 200;
        var rows = new TextParagraph[SkiaRenderContext.RunBlobCacheMaxSize + Allocations.MaxRounds * Round];
        for (int i = 0; i < rows.Length; i++)
        {
            string text = $"    var item{i} = Compute({i % 997}, \"value {i}\"); // note {i}";
            rows[i] = TextDrawAllocationTests.WithGlyphs(ctx.LayoutParagraph(text, style, 780, Thirds(text)));
        }
        // A few rows first, so the measured ones fill a map that has not grown yet: a map made
        // small would grow under them (and the run blobs' cache, grown by then, would not). Rows
        // are drawn fifty a frame, as a list scrolled through, so the maps let the old ones go.
        int next = 0;
        InFrames(ctx, 20, 20, _ => ctx.DrawParagraph(rows[next++], new Point(4, 4)));
        InFrames(ctx, SkiaRenderContext.RunBlobCacheMaxSize + 100, 100,
            i => ctx.DrawText($"Row {i} of the list", new Rect(4, 40, 780, 30), style, TextOverflow.Clip, null));
        Assert.True(ctx.CachedSubBlobCount > 0);

        long subBlobsBefore = ctx.BlobsMade;
        // One round on its own: hundreds of sub-blobs into the map the first ones made. Grown
        // from a default capacity it was ~300 B a row; a one-off retired allocation context
        // (see Allocations) is about 8 KB, ~40 B a row over 200 rows — and the Linux runner
        // read 40.4 once (the Meridian installer run of 2026-10-13), so the bound sits well
        // between the two rather than on the retired context's edge.
        long first = GC.GetAllocatedBytesForCurrentThread();
        InFrames(ctx, Round, 50, _ => ctx.DrawParagraph(rows[next++], new Point(4, 4)));
        double firstPerRow = (GC.GetAllocatedBytesForCurrentThread() - first) / (double)Round;
        Assert.True(firstPerRow < 120, $"the sub-blob map grew: {firstPerRow:0.#} B a row");

        long bytes = Allocations.Least(() => InFrames(ctx, Round, 50, _ => ctx.DrawParagraph(rows[next++], new Point(4, 4))));
        Assert.True(ctx.BlobsMade - subBlobsBefore >= 2 * Round, $"{ctx.BlobsMade - subBlobsBefore} blobs made");
        Assert.True(bytes / (double)Round < 1, $"a new row of three stretches allocated {bytes / (double)Round:0.#} B");
    }

    [Fact]
    public void SubBlobs_AreReleasedOnce_ThroughCacheChurnAndDispose()
    {
        var surface = new SkiaRasterRenderSurface();
        surface.Resize(800, 100, 1f);
        var ctx = (SkiaRenderContext)surface.RenderContext;
        var style = Style(12f, family: FamilyWithAnExtent(ctx));
        bool sawSubBlobs = false;
        InFrames(ctx, 3 * SkiaRenderContext.RunBlobCacheMaxSize, 50, i =>
        {
            string text = $"    var item{i} = Compute({i % 997}, \"value {i}\"); // note {i}";
            ctx.DrawText(text, new Rect(4, 4, 780, 30), style, TextOverflow.Clip, null, Thirds(text));
            Assert.Equal(ctx.CachedBlobCount + ctx.CachedSubBlobCount, ctx.BlobsMade - ctx.BlobsReleased);
            sawSubBlobs |= ctx.CachedSubBlobCount > 0;
        });
        Assert.True(sawSubBlobs, "no sub-blob was made");
        Assert.True(ctx.BlobsReleased > SkiaRenderContext.RunBlobCacheMaxSize, $"{ctx.BlobsReleased} released");

        surface.Dispose();
        Assert.Equal(0, ctx.CachedSubBlobCount);
        Assert.Equal(ctx.BlobsMade, ctx.BlobsReleased);
    }

    /// <summary>A new row in a variable face takes its run's extent from the face's glyph table,
    /// whose page for the row's glyphs the first rows made: the row allocates nothing.</summary>
    [VariableFontFact]
    public void ANewRowInAVariableFace_TakesItsRunsExtentWithoutAllocating()
    {
        using var surface = new SkiaRasterRenderSurface();
        surface.Resize(800, 100, 1f);
        var ctx = (SkiaRenderContext)surface.RenderContext;
        var style = Style(12f, family: VariableFonts.Family!);
        const int Warm = 20, First = 1_000, Round = 200;
        var rows = new TextParagraph[Warm + First + Allocations.MaxRounds * Round];
        for (int i = 0; i < rows.Length; i++)
        {
            string text = $"    var item{i} = Compute({i % 997}, \"value {i}\"); // note {i}";
            rows[i] = TextDrawAllocationTests.WithGlyphs(ctx.LayoutParagraph(text, style, 780, Thirds(text)));
        }
        int next = 0;
        InFrames(ctx, Warm, Warm, _ => ctx.DrawParagraph(rows[next++], new Point(4, 4)));
        // The run blobs' cache, which is not sized ahead, grown to its bound first.
        InFrames(ctx, SkiaRenderContext.RunBlobCacheMaxSize + 100, 100,
            i => ctx.DrawText($"Row {i} of the list", new Rect(4, 40, 780, 30), style, TextOverflow.Clip, null));
        Assert.True(ctx.RunExtentsFromGlyphs > 0, "no run took its own extent");

        // A thousand new runs. Kept per run in a map grown from a default capacity, their extents
        // were ~70 B a row; a one-off retired allocation context (see Allocations) is about 8 KB,
        // 8 B a row.
        long first = GC.GetAllocatedBytesForCurrentThread();
        InFrames(ctx, First, 50, _ => ctx.DrawParagraph(rows[next++], new Point(4, 4)));
        double firstPerRow = (GC.GetAllocatedBytesForCurrentThread() - first) / (double)First;
        Assert.True(firstPerRow < 30, $"the run extents grew something: {firstPerRow:0.#} B a row");

        long bytes = Allocations.Least(() => InFrames(ctx, Round, 50, _ => ctx.DrawParagraph(rows[next++], new Point(4, 4))));
        Assert.True(bytes / (double)Round < 1, $"a new row of three stretches in {VariableFonts.Family} allocated {bytes / (double)Round:0.#} B");
    }

    /// <summary>What the extents keep grows with the face's glyphs, not with the runs drawn: six
    /// thousand new rows read the bounds of the few dozen glyphs they hold, once each.</summary>
    [VariableFontFact]
    public void RunExtents_ReadEachGlyphOnce_HoweverManyRunsHoldIt()
    {
        using var surface = new SkiaRasterRenderSurface();
        surface.Resize(800, 100, 1f);
        var ctx = (SkiaRenderContext)surface.RenderContext;
        var style = Style(12f, family: VariableFonts.Family!);
        var glyphs = new HashSet<(IResolvedFont, ushort)>();
        ctx.BeginFrame(800, 100, 1f, Color.White);
        for (int i = 0; i < 3 * SkiaRenderContext.RunBlobCacheMaxSize; i++)
        {
            string text = $"    var item{i} = Compute({i % 997}, \"value {i}\"); // note {i}";
            var paragraph = (PipelineTextParagraph)ctx.LayoutParagraph(text, style, 780, Thirds(text));
            foreach (var run in paragraph.TextLines.SelectMany(l => l.Runs))
                foreach (var g in run.Glyphs)
                    glyphs.Add((run.Font, (ushort)g.GlyphId));
            ctx.DrawParagraph(paragraph, new Point(4, 4));
        }
        ctx.EndFrame();
        Assert.True(ctx.RunExtentsFromGlyphs >= 3 * SkiaRenderContext.RunBlobCacheMaxSize, $"{ctx.RunExtentsFromGlyphs} runs took their own extent");
        Assert.InRange(ctx.GlyphInksRead, 1, glyphs.Count);
        output.WriteLine($"{ctx.RunExtentsFromGlyphs} run extents from {ctx.GlyphInksRead} glyph bounds read ({glyphs.Count} glyphs in the rows)");
    }
}
