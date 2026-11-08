using System.Numerics;
using HultUI.Core.Layout;
using HultUI.Core.Primitives;
using HultUI.Core.Rendering;
using HultUI.Core.Styling;
using HultUI.Text;
using Xunit;
using Xunit.Abstractions;

namespace HultUI.Rendering.Skia.Tests;

/// <summary>
/// A blob whose glyphs all sit on the baseline is drawn with its baseline on a whole device pixel
/// while the canvas only scales and moves. Skia's GPU text-blob cache keeps an entry per blob and
/// fraction of a device pixel the blob was drawn at, and scans a blob's entries on every draw of it;
/// one blob shared by every cell or line of the same text gathered an entry per height it was ever
/// drawn at (plan 17, round 7: 819 → 5,815 entries scanned a frame over a 200-notch walk of a
/// 300×10 table). Skia puts such glyphs on the row <c>floor(device y + ½)</c> anyway, so no pixel
/// moves; raised glyphs, text Skia draws as a distance field or paths, and a canvas with skew keep
/// the baseline layout gave them.
/// </summary>
public class BaselineOnPixelTests(ITestOutputHelper output)
{
    private static ResolvedInheritedStyle Style(float size) => new(
        FontFamily: "sans-serif", FontSize: size, FontWeight: FontWeight.Regular,
        TextColor: new Color(20, 20, 20, 255), TextDirection: TextDirection.Ltr, Cursor: CursorType.Default);

    private const string Words = "Pack my box with five dozen liquor jugs";

    private static SkiaRasterRenderSurface Surface(float scale, int w = 700, int h = 500)
    {
        var surface = new SkiaRasterRenderSurface();
        surface.Resize((int)(w * scale), (int)(h * scale), scale);
        return surface;
    }

    /// <summary>One text drawn at layout height <paramref name="y"/> under a scroll and a
    /// transform: the baseline layout gave its last blob and the one it was drawn at, both as
    /// Skia maps them to the device (t + s·y).</summary>
    private static (float Layout, float Drawn, float DeviceLayout, float DeviceDrawn) DrawOne(
        SkiaRenderContext ctx, string text, float size, float y, float scrollY = 0f,
        Matrix3x2? transform = null, TextSpan[]? spans = null)
    {
        // The scrolls put most texts far off the surface, and a text the size of 40 px wraps: every
        // line drawn, so the last blob is this text's wherever its clip is.
        ctx.CullsTextLines = false;
        float[] at = new float[2];
        for (int pass = 0; pass < 2; pass++)
        {
            ctx.SnapsBaselines = pass == 1;
            ctx.PushTranslate(0f, -scrollY);
            if (transform is { } m) ctx.PushTransform(m);
            ctx.DrawText(text, new Rect(10.3f, y, 600f, 400f), Style(size), TextOverflow.Clip, null, spans);
            at[pass] = ctx.LastBlobBaseline;
            if (transform is not null) ctx.Pop();
            ctx.Pop();
        }
        ctx.PushTranslate(0f, -scrollY);
        if (transform is { } t) ctx.PushTransform(t);
        var matrix = ctx.Canvas.TotalMatrix;
        if (transform is not null) ctx.Pop();
        ctx.Pop();
        return (at[0], at[1], matrix.TransY + matrix.ScaleY * at[0], matrix.TransY + matrix.ScaleY * at[1]);
    }

    private static float Fraction(float device) => MathF.Abs(device - MathF.Round(device));

    [Theory]
    [InlineData(1f)]
    [InlineData(1.25f)]
    [InlineData(1.5f)]
    [InlineData(1.75f)]
    [InlineData(2f)]
    public void TextAtAFractionalHeight_IsDrawnOnTheWholePixel_ItsGlyphsLandOn(float scale)
    {
        using var surface = Surface(scale);
        var ctx = (SkiaRenderContext)surface.RenderContext;
        ctx.BeginFrame(700, 500, scale, Color.White);
        var rng = new Random(11);
        float[] sizes = [9f, 12.5f, 13f, 14f, 17.3f, 24f, 40f];
        int draws = 0, fractionalLayouts = 0, exact = 0;
        foreach (var transform in new Matrix3x2?[] { null, Matrix3x2.CreateScale(1.3f), Matrix3x2.CreateScale(0.8f, 1.1f) * Matrix3x2.CreateTranslation(3.7f, 0.45f) })
            for (int i = 0; i < 120; i++)
            {
                float y = rng.Next(0, 300) + rng.NextSingle();
                float scroll = rng.Next(0, 4) == 0 ? rng.Next(0, 4000) : rng.Next(0, 4000) + rng.NextSingle();
                var (layout, drawn, deviceLayout, deviceDrawn) = DrawOne(ctx, Words, sizes[i % sizes.Length], y, scroll, transform);
                draws++;
                if (Fraction(deviceLayout) > 1e-3f) fractionalLayouts++;
                if (deviceDrawn == MathF.Round(deviceDrawn)) exact++;
                // The row Skia puts the glyphs on from the layout's baseline, now the baseline itself.
                float row = MathF.Floor(deviceLayout + 0.5f);
                Assert.True(MathF.Abs(deviceDrawn - row) < 1e-3f,
                    $"at {scale}x, y {y} scrolled {scroll}, transform {transform}: layout baseline {layout} (device {deviceLayout}) drawn at {drawn} (device {deviceDrawn}), not on row {row}");
            }
        ctx.EndFrame();
        Assert.True(fractionalLayouts > draws / 2, $"only {fractionalLayouts} of {draws} layout baselines were between pixels");
        output.WriteLine($"at {scale}x: {draws} draws, {fractionalLayouts} between pixels as laid out, {exact} exactly whole as drawn");
    }

    [Theory]
    [InlineData(1f)]
    [InlineData(1.5f)]
    public void AColourStretch_DrawsItsSubBlobOnTheWholePixel(float scale)
    {
        using var surface = Surface(scale);
        var ctx = (SkiaRenderContext)surface.RenderContext;
        ctx.BeginFrame(700, 500, scale, Color.White);
        // The last stretch covers part of the line's one run: drawn from a sub-blob of its glyphs.
        TextSpan[] spans = [new(4, 3, new Color(200, 0, 0, 255)), new(20, 6, new Color(0, 0, 200, 255))];
        var (layout, drawn, deviceLayout, deviceDrawn) = DrawOne(ctx, Words, 14f, 33.37f, 101.6f, spans: spans);
        ctx.EndFrame();
        Assert.True(ctx.CachedSubBlobCount > 0, "the stretches drew whole runs");
        Assert.True(Fraction(deviceLayout) > 0.01f, $"the layout baseline {deviceLayout} is already whole");
        Assert.True(MathF.Abs(deviceDrawn - MathF.Floor(deviceLayout + 0.5f)) < 1e-3f,
            $"sub-blob drawn at {drawn} (device {deviceDrawn}), layout {layout} (device {deviceLayout})");
    }

    [Fact]
    public void UnderRotationOrSkew_TextKeepsItsLayoutBaseline()
    {
        using var surface = Surface(1.5f);
        var ctx = (SkiaRenderContext)surface.RenderContext;
        ctx.BeginFrame(700, 500, 1.5f, Color.White);
        foreach (var transform in new[] { Matrix3x2.CreateRotation(0.12f), Matrix3x2.CreateSkew(0.2f, 0f), Matrix3x2.CreateSkew(0f, 0.05f) })
        {
            var (layout, drawn, _, _) = DrawOne(ctx, Words, 14f, 40.37f, 11.3f, transform);
            Assert.Equal(layout, drawn);
        }
        // And a plain scale and move after them is snapped again.
        var after = DrawOne(ctx, Words, 14f, 40.37f, 11.3f);
        Assert.NotEqual(after.Layout, after.Drawn);
        ctx.EndFrame();
    }

    /// <summary>Skia draws text of 162 device pixels and more as a distance field on the GPU and
    /// from 256 as paths, both at the exact baseline: such text is not moved, and nothing near it.</summary>
    [Theory]
    [InlineData(1f, 127f, true)]
    [InlineData(1f, 128f, false)]
    [InlineData(1.5f, 80f, true)]
    [InlineData(1.5f, 86f, false)]
    [InlineData(2f, 63.9f, true)]
    [InlineData(2f, 120f, false)]
    public void TextSkiaMayNotDrawFromItsAtlas_KeepsItsLayoutBaseline(float scale, float size, bool snapped)
    {
        using var surface = Surface(scale, 900, 600);
        var ctx = (SkiaRenderContext)surface.RenderContext;
        ctx.BeginFrame(900, 600, scale, Color.White);
        var (layout, drawn, deviceLayout, _) = DrawOne(ctx, "Big", size, 20.37f, 3.3f);
        ctx.EndFrame();
        Assert.True(Fraction(deviceLayout) > 0.01f, $"the layout baseline {deviceLayout} is already whole");
        Assert.Equal(snapped, layout != drawn);
    }

    /// <summary>A glyph the shaper raised or lowered off the baseline (a stacked or attached mark)
    /// lands on <c>floor(device y + offset + ½)</c>, which a moved baseline could change: its run
    /// keeps the baseline layout gave it.</summary>
    [Fact]
    public void ARunWithGlyphsOffTheBaseline_KeepsItsLayoutBaseline()
    {
        using var surface = Surface(1.5f);
        var ctx = (SkiaRenderContext)surface.RenderContext;
        ctx.BeginFrame(700, 500, 1.5f, Color.White);
        string[] candidates = ["X́", "Ậ", "q́̂", "שָׁ", "مَرْ", "Z͓͑͒"];
        int raised = 0;
        foreach (var text in candidates)
        {
            var runs = ((PipelineTextParagraph)ctx.LayoutParagraph(text, Style(16f), 600f)).TextLines.SelectMany(l => l.Runs).ToList();
            if (runs.Count != 1 || runs[0].Glyphs.All(g => g.Y == 0f)) continue;
            raised++;
            var (layout, drawn, _, _) = DrawOne(ctx, text, 16f, 40.37f, 11.3f);
            Assert.Equal(layout, drawn);
        }
        ctx.EndFrame();
        Assert.True(raised > 0, "no candidate shaped to a single run with a glyph off the baseline here");
        output.WriteLine($"{raised} of {candidates.Length} candidates had glyphs off the baseline");
    }

    /// <summary>The canvas matrix is read once for all the text drawn under it, not once a blob:
    /// again after a translate or a transform is pushed or popped, not after a clip.</summary>
    [Fact]
    public void TheMatrix_IsReadOncePerMove_NotPerBlob()
    {
        using var surface = Surface(1.5f);
        var ctx = (SkiaRenderContext)surface.RenderContext;
        ctx.BeginFrame(700, 500, 1.5f, Color.White);
        long reads = ctx.DeviceMatrixReads;
        ctx.PushTranslate(0f, -17.3f);
        for (int i = 0; i < 40; i++)
        {
            ctx.PushClip(new Rect(0, i * 11.3f, 600, 20));
            ctx.DrawText($"Row {i % 7} of the table", new Rect(8, i * 11.3f + 0.4f, 500, 20), Style(13f), TextOverflow.Clip, null);
            ctx.Pop();
        }
        Assert.Equal(reads + 1, ctx.DeviceMatrixReads);
        ctx.Pop();
        ctx.DrawText(Words, new Rect(8, 3.3f, 500, 20), Style(13f), TextOverflow.Clip, null);
        Assert.Equal(reads + 2, ctx.DeviceMatrixReads);
        ctx.PushTransform(Matrix3x2.CreateScale(1.1f));
        ctx.DrawText(Words, new Rect(8, 3.3f, 500, 20), Style(13f), TextOverflow.Clip, null);
        ctx.DrawText(Words, new Rect(8, 23.3f, 500, 20), Style(13f), TextOverflow.Clip, null);
        Assert.Equal(reads + 3, ctx.DeviceMatrixReads);
        ctx.Pop();
        ctx.EndFrame();
        ctx.BeginFrame(700, 500, 1.5f, Color.White);
        ctx.DrawText(Words, new Rect(8, 3.3f, 500, 20), Style(13f), TextOverflow.Clip, null);
        Assert.Equal(reads + 4, ctx.DeviceMatrixReads);
        ctx.EndFrame();
    }

    // ── Pixels ──

    private static readonly string[] Corpus =
    [
        "Plain ASCII: The quick brown fox jumps over the lazy dog, 0123456789 {}[]();",
        "Hebrew שָׁלוֹם עולם, Arabic مَرْحَبًا بالعالم, numbers 12345 between them.",
        "Marks: é ä ô ñ ạ̀ X́ q́̂ Z̶a̶l̶g̶o̶ x̣̂̄ and Devanagari नमस्ते क्षत्रिय",
        "Emoji \U0001F600\U0001F389 family \U0001F468‍\U0001F469‍\U0001F467 flag \U0001F1F8\U0001F1EA heart ❤️ end",
        "CJK 東京都 漢字かなカナ 한국어, Thai ภาษาไทย ปู่ ผู้ใหญ่ น้ำ",
    ];

    private static TextSpan[] EveryFew(string text, int every)
    {
        var colours = new[] { new Color(192, 0, 0, 255), new Color(0, 128, 0, 255), new Color(0, 0, 192, 255) };
        var spans = new List<TextSpan>();
        for (int at = 0, c = 0; at < text.Length; at += every, c++)
            spans.Add(new TextSpan(at, Math.Min(every, text.Length - at), colours[c % colours.Length]));
        return spans.ToArray();
    }

    private static byte[] DrawCorpus(float scale, bool snap)
    {
        const int W = 900, H = 900;
        using var surface = Surface(scale, W, H);
        var ctx = (SkiaRenderContext)surface.RenderContext;
        ctx.SnapsBaselines = snap;
        ctx.BeginFrame(W, H, scale, Color.White);
        var transforms = new Matrix3x2?[] { null, Matrix3x2.CreateScale(1.37f), Matrix3x2.CreateRotation(0.07f), Matrix3x2.CreateScale(1.2f, 0.9f) };
        float y = 3.3f;
        int k = 0;
        foreach (var transform in transforms)
        {
            ctx.PushTranslate(0.25f, -0.37f * (k + 1));
            if (transform is { } m) ctx.PushTransform(m);
            foreach (float size in new[] { 9f, 13f, 14.5f, 17.3f, 24f })
                foreach (var text in Corpus)
                {
                    var spans = k % 3 == 0 ? null : EveryFew(text, 1 + k % 4);
                    if (k++ % 2 == 0)
                        ctx.DrawText(text, new Rect(6.1f, y, W - 12, 200), Style(size), TextOverflow.Clip, null, spans);
                    else
                        ctx.DrawParagraph(ctx.LayoutParagraph(text, Style(size), W - 12, spans), new Point(6.1f, y));
                    y += size * 1.07f + 0.13f;
                    if (y > H / (transform is null ? 1f : 1.4f)) y -= H / 1.5f;
                }
            if (transform is not null) ctx.Pop();
            ctx.Pop();
        }
        // Either side of the atlas limits: 128 device pixels here, 162 (a distance field on the GPU), 256 (paths).
        foreach (float device in new[] { 100f, 127f, 140f, 170f, 300f })
        {
            ctx.DrawText("Hg", new Rect(20.3f, 30.37f + device / 2f, 800, 400), Style(device / scale), TextOverflow.Clip, null);
        }
        ctx.EndFrame();
        surface.Present();
        return surface.ReadPixels(out _, out _, out _);
    }

    /// <summary>The same pixels with the baselines moved as without, at whole and fractional
    /// scales, through every text path: marks, BiDi, emoji, colour stretches (sub-blobs), scaled,
    /// rotated and stretched canvases, and sizes either side of the limits. DirectWrite can draw
    /// a glyph again a little differently after Skia's glyph cache let it go (see
    /// SpannedLineSubBlobTests.AssertSamePixels), so a difference is drawn again before it fails.</summary>
    [Theory]
    [InlineData(1f)]
    [InlineData(1.25f)]
    [InlineData(1.5f)]
    [InlineData(2f)]
    public void MovingBaselinesToWholePixels_ChangesNoPixel(float scale)
    {
        var before = DrawCorpus(scale, snap: false);
        var after = DrawCorpus(scale, snap: true);
        for (int attempt = 1; !before.AsSpan().SequenceEqual(after); attempt++)
        {
            if (attempt == 3)
            {
                int differ = 0, first = -1;
                for (int i = 0; i < before.Length; i++)
                    if (before[i] != after[i]) { differ++; if (first < 0) first = i; }
                Assert.Fail($"at {scale}x: {differ} bytes differ, the first at byte {first}");
            }
            before = DrawCorpus(scale, snap: false);
            after = DrawCorpus(scale, snap: true);
        }
    }
}
