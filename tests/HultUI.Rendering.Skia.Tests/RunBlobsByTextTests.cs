using HultUI.Core.Primitives;
using HultUI.Core.Rendering;
using HultUI.Core.Styling;
using HultUI.Text;
using Xunit;

namespace HultUI.Rendering.Skia.Tests;

/// <summary>
/// The context keeps a text blob for each run it draws, keyed by what the run draws, and a run of
/// several words puts its glyph array together the first time its glyphs are read. The key was the
/// glyphs: every run new to the map (each line of a paragraph laid out again at a width a resize
/// passes through, each row a scroll reveals) put its array together only to find the blob an
/// equal run made before (plan 17, round 6: 3,885 of the 4,599 glyphs a step of text/wrap put
/// together). Runs whose glyphs follow from their text (<see cref="ShapedRun.GlyphsFollowText"/>)
/// are matched by their text, and a new blob is built from a run's words in a buffer the context
/// keeps; any other two runs of the same text still need the same glyphs.
/// </summary>
public class RunBlobsByTextTests
{
    private const string Paragraph =
        "Pack my box with five dozen liquor jugs. How vexingly quick daft zebras jump! Item 4711 of 815 "
        + "units, 3.5% of 100 and 12345 67890. The five boxing wizards jump quickly over the lazy dog.";

    private static readonly ResolvedInheritedStyle Style = new(
        FontFamily: "sans-serif", FontSize: 14f, FontWeight: FontWeight.Regular,
        TextColor: new Color(20, 20, 20, 255), TextDirection: TextDirection.Ltr, Cursor: CursorType.Default);

    private static SkiaRasterRenderSurface Surface(SkiaTextServices text)
    {
        var surface = new SkiaRasterRenderSurface(text);
        surface.Resize(700, 400, 1f);
        return surface;
    }

    private static IEnumerable<ShapedRun> Runs(TextParagraph paragraph) =>
        ((PipelineTextParagraph)paragraph).TextLines.SelectMany(line => line.Runs);

    private static void Frame(IRenderContext ctx, params TextParagraph[] paragraphs)
    {
        ctx.BeginFrame(700, 400, 1f, Color.White);
        for (int i = 0; i < paragraphs.Length; i++)
            ctx.DrawParagraph(paragraphs[i], new Point(10, 10 + 50 * i));
        ctx.EndFrame();
    }

    /// <summary>A paragraph laid out again at each width a resize passes through makes new runs
    /// for every line, the ones whose text did not change too. Drawn, each line's run finds the
    /// blob of the first run of its text, and no run of several words puts its glyph array
    /// together: the lines the resize did not move only to find their blobs, the new lines only to
    /// copy their glyphs into one. (A one-word line shares its word's array from the start.)</summary>
    [Fact]
    public void AParagraphLaidOutAgainAtEachWidth_MakesABlobPerLineText_AndPutsNoGlyphsTogether()
    {
        using var text = new SkiaTextServices();
        using var surface = Surface(text);
        var ctx = (SkiaRenderContext)surface.RenderContext;

        var runs = new List<ShapedRun>();
        var pending = new List<ShapedRun>();
        for (float width = 420f; width >= 300f; width -= 4f)
        {
            var paragraph = ctx.LayoutParagraph(Paragraph, Style, width);
            runs.AddRange(Runs(paragraph));
            pending.AddRange(Runs(paragraph).Where(run => !run.GlyphsComposed));
            Frame(ctx, paragraph);
        }

        int texts = runs.Select(run => run.Text).Distinct().Count();
        Assert.True(runs.Distinct().Count() > 2 * texts, $"{runs.Count} runs of {texts} texts: the resize moved too few lines");
        Assert.True(pending.Count > runs.Count / 2, $"{pending.Count} of {runs.Count} runs had no glyph array");
        Assert.All(runs, run => Assert.True(run.GlyphsFollowText, $"\"{run.Text}\" is not marked"));
        Assert.Equal((long)texts, ctx.BlobsMade);
        Assert.Empty(pending.Where(run => run.GlyphsComposed).Select(run => run.Text));
    }

    /// <summary>A run of words drawn after another of the same text, face and size draws the
    /// other's blob, and neither puts its glyphs together for it: the first made its blob from its
    /// words, the second was matched by its text.</summary>
    [Fact]
    public void ARunOfWords_DrawnAfterAnEqualOne_DrawsItsBlob_AndNeitherPutsItsGlyphsTogether()
    {
        using var text = new SkiaTextServices();
        using var surface = Surface(text);
        var ctx = (SkiaRenderContext)surface.RenderContext;
        var font = text.Fonts.Resolve("sans-serif", FontWeight.Regular, FontSlant.Normal);
        const string Words = "How vexingly quick daft zebras jump";

        var first = text.Pipeline.ShapeByWords(Words, font, 14f);
        var second = text.Pipeline.ShapeByWords(Words, font, 14f);
        Assert.NotSame(first, second);

        nint blob = ctx.BlobOf(first);
        Assert.NotEqual(nint.Zero, blob);
        Assert.Equal(1L, ctx.BlobsMade);
        Assert.False(first.GlyphsComposed, "a new blob put its run's glyph array together");

        Assert.Equal(blob, ctx.BlobOf(second));
        Assert.Equal(1L, ctx.BlobsMade);
        Assert.False(second.GlyphsComposed, "finding a blob put the run's glyph array together");
        Assert.False(first.GlyphsComposed, "finding a blob put the kept run's glyph array together");
    }

    /// <summary>A blob built from a run's words, in the kept buffer, is the blob its glyph array
    /// builds: paragraphs of runs of 30 to 170 glyphs, digits among them (put together from the
    /// face's digit table), drawn on a fresh surface before any glyph is read, draw the pixels the
    /// same paragraphs draw from their arrays. Each blob holds only its own run's glyphs, after
    /// longer runs filled the buffer.</summary>
    [Fact]
    public void ABlobFromARunsWords_DrawsAsTheRunsGlyphArrayDoes()
    {
        string[] texts = ["12345 and 67890", Paragraph, Paragraph[..60], "Short words, then 4711", Paragraph[40..]];

        using var fonts = new SkiaTextServices();
        using var fromWords = Surface(fonts);
        var words = texts.Select(t => fromWords.RenderContext.LayoutParagraph(t, Style, 680)).ToArray();
        Frame(fromWords.RenderContext, words);
        fromWords.Present();
        Assert.Contains(words.SelectMany(Runs), run => run.GlyphCount > 64);
        Assert.Empty(words.SelectMany(Runs).Where(run => run.GlyphsComposed).Select(run => run.Text));

        using var arrays = new SkiaTextServices(fonts.Fonts);
        using var fromArrays = Surface(arrays);
        var composed = texts.Select(t => TextDrawAllocationTests.WithGlyphs(fromArrays.RenderContext.LayoutParagraph(t, Style, 680))).ToArray();
        Frame(fromArrays.RenderContext, composed);
        fromArrays.Present();

        Assert.Equal(fromArrays.ReadPixels(out _, out _, out _), fromWords.ReadPixels(out _, out _, out _));
    }

    /// <summary>Runs of the same text whose glyphs do not follow from it keep blobs of their own:
    /// letter spacing moves every glyph, and a right-to-left run is shaped whole, in the other
    /// order. Two such runs share a blob only when their glyphs are the same, as do a run whose
    /// glyphs follow from its text and one, of the same text, whose glyphs are the same.</summary>
    [Fact]
    public void RunsOfTheSameText_AndOtherGlyphs_KeepBlobsOfTheirOwn()
    {
        using var text = new SkiaTextServices();
        using var again = new SkiaTextServices(text.Fonts);
        using var surface = Surface(text);
        var ctx = (SkiaRenderContext)surface.RenderContext;
        var font = text.Fonts.Resolve("sans-serif", FontWeight.Regular, FontSlant.Normal);
        const string Words = "The five boxing wizards jump quickly";
        var spacedStyle = Style with { LetterSpacing = 0.6f };

        ShapedRun Single(TextPipeline pipeline, ResolvedInheritedStyle style) =>
            Assert.Single(Assert.Single(pipeline.Layout(Words, style, float.PositiveInfinity, null).Lines).Runs);

        var plain = Single(text.Pipeline, Style);
        var spaced = Single(text.Pipeline, spacedStyle);
        var spacedAgain = Single(again.Pipeline, spacedStyle);
        var rtl = text.Shaper.Shape(Words, font, 14f, rtl: true);
        Assert.Same(plain.Font, spaced.Font);
        Assert.Same(plain.Font, rtl.Font);
        Assert.NotSame(spaced, spacedAgain);
        Assert.True(plain.GlyphsFollowText);
        Assert.False(spaced.GlyphsFollowText);
        Assert.False(rtl.GlyphsFollowText);

        nint plainBlob = ctx.BlobOf(plain), spacedBlob = ctx.BlobOf(spaced), rtlBlob = ctx.BlobOf(rtl);
        Assert.Equal(3, new[] { plainBlob, spacedBlob, rtlBlob }.Distinct().Count());
        Assert.Equal(3L, ctx.BlobsMade);

        // The same text and the same glyphs: the blob made before.
        Assert.Equal(spacedBlob, ctx.BlobOf(spacedAgain));
        var plainCopy = new ShapedRun(Words, font, 14f, plain.Width, again.Pipeline.ShapeByWords(Words, font, 14f).Glyphs);
        Assert.False(plainCopy.GlyphsFollowText);
        Assert.Equal(plainBlob, ctx.BlobOf(plainCopy));
        Assert.Equal(3L, ctx.BlobsMade);
    }
}
