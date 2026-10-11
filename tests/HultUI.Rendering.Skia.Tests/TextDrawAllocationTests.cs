using HultUI.Core.Layout;
using HultUI.Core.Primitives;
using HultUI.Core.Rendering;
using HultUI.Core.Styling;
using HultUI.Testing;
using Xunit;

namespace HultUI.Rendering.Skia.Tests;

/// <summary>
/// Drawing text the frame before drew allocates nothing. The layouts and the glyph blobs are
/// cached, so what is left per frame is looping over lines and runs — and a <c>foreach</c> over
/// <c>TextLayout.Lines</c> or <c>TextLine.Runs</c>, which are interfaces, boxes an enumerator
/// every time: 72 B per label per frame, 0.6 KB per spanned line, 17 KB a frame on the perf
/// suite's dense text screen (plan 17, round 1). A rounded border with sides of their own built
/// four <c>SKPath</c>s per frame for its corner clips.
///
/// <para>The bound is under one byte per frame on average, over the least of a few rounds of
/// frames (<see cref="Allocations"/>), so a one-off allocation does not fail the test; anything
/// drawn per frame costs tens of bytes a frame, in every round.</para>
/// </summary>
public class TextDrawAllocationTests
{
    private const int Frames = 100;

    private static ResolvedInheritedStyle Style(TextDecoration decoration = TextDecoration.None,
        TextAlign align = TextAlign.Start) => new(
        FontFamily: "sans-serif", FontSize: 14f, FontWeight: FontWeight.Regular,
        TextColor: new Color(20, 20, 20, 255), TextDirection: TextDirection.Ltr, Cursor: CursorType.Default,
        TextAlign: align, Decoration: decoration);

    private const string Paragraph =
        "The quick brown fox jumps over the lazy dog, and then it does it again on the next line.";

    // Colour stretches, a background, an underline and a bold word: every pass of the spanned
    // line, and a line of several runs.
    private static readonly TextSpan[] Spans =
    [
        new(4, 5, new Color(200, 0, 0, 255)),
        new(10, 5, default, Background: new Color(255, 255, 0, 255)),
        new(20, 5, new Color(0, 0, 200, 255), Decoration: TextDecoration.Underline),
        new(26, 4, default, Weight: FontWeight.Bold),
        new(40, 12, new Color(0, 128, 0, 255), new Color(220, 220, 255, 255)),
    ];

    private static SkiaRasterRenderSurface Surface()
    {
        var surface = new SkiaRasterRenderSurface();
        surface.Resize(600, 400, 1f);
        return surface;
    }

    /// <summary>Bytes this thread allocated per frame of <paramref name="draw"/>, once the
    /// caches are warm: the least of a few rounds.</summary>
    private static double AllocatedPerFrame(SkiaRasterRenderSurface surface, Action<IRenderContext> draw)
    {
        var ctx = surface.RenderContext;

        void Frame()
        {
            ctx.BeginFrame(600, 400, 1f, Color.White);
            draw(ctx);
            ctx.EndFrame();
        }

        Frame();
        Frame();
        return Allocations.PerCall(Frames, Frame);
    }

    [Fact]
    public void Labels_AllocateNothingPerFrame()
    {
        var plain = Style();
        var underlined = Style(TextDecoration.Underline);
        var centred = Style(align: TextAlign.Center);

        using var surface = Surface();
        double perFrame = AllocatedPerFrame(surface, ctx =>
        {
            for (int i = 0; i < Labels.Length; i++)
                ctx.DrawText(Labels[i], new Rect(10, 10 + i * 20, 200, 20), plain, TextOverflow.Clip, null);
            ctx.DrawText(Paragraph, new Rect(220, 10, 150, 200), plain, TextOverflow.Clip, null);
            ctx.DrawText("Underlined", new Rect(220, 220, 150, 20), underlined, TextOverflow.Clip, null);
            ctx.DrawText("Centred", new Rect(220, 250, 150, 20), centred, TextOverflow.Clip, null);
            ctx.DrawText(Paragraph, new Rect(400, 10, 120, 20), plain, TextOverflow.Ellipsis, 1);
        });

        Assert.True(perFrame < 1, $"drawing 12 labels allocated {perFrame:0.#} B per frame");
    }

    [Fact]
    public void SpannedText_AllocatesNothingPerFrame()
    {
        var plain = Style();
        var underlined = Style(TextDecoration.Underline);

        using var surface = Surface();
        double perFrame = AllocatedPerFrame(surface, ctx =>
        {
            ctx.DrawText(Paragraph, new Rect(10, 10, 180, 200), plain, TextOverflow.Clip, null, Spans);
            ctx.DrawText(Paragraph, new Rect(200, 10, 600, 20), underlined, TextOverflow.Clip, null, Spans);
        });

        Assert.True(perFrame < 1, $"drawing spanned text allocated {perFrame:0.#} B per frame");
    }

    [Fact]
    public void Paragraphs_AndTheirSelection_AllocateNothingPerFrame()
    {
        using var surface = Surface();
        var plain = surface.RenderContext.LayoutParagraph(Paragraph, Style(), 180);
        var spanned = surface.RenderContext.LayoutParagraph(Paragraph, Style(), 180, Spans);
        var boxes = new List<Rect>();

        double perFrame = AllocatedPerFrame(surface, ctx =>
        {
            ctx.DrawParagraph(plain, new Point(10, 10));
            ctx.DrawParagraph(spanned, new Point(200, 10));
            boxes.Clear();
            spanned.SelectionBoxes(6, 60, boxes, lineEndWidth: 4);
        });

        Assert.NotEmpty(boxes);
        Assert.True(perFrame < 1, $"drawing two paragraphs and a selection allocated {perFrame:0.#} B per frame");
    }

    // A line whose shaping merged clusters: a combining mark composed with its letter into one
    // glyph, a character outside the BMP (two chars, one glyph).
    private const string MergedClusters =
        "Cafe\u0301 and nai\u0308ve \U0001F600 with re\u0301sume\u0301s, and more words to wrap the line";

    /// <summary>
    /// A line whose shaping merged clusters (marks, emoji, ligatures) maps offsets to x through a
    /// prefix measurer, not its glyphs: a spanned line asks for one for its backgrounds and
    /// decorations and again for each colour stretch, a selection for each line it covers, a
    /// caret or a click for each. A new closure each time was ~1 KB a frame over two such lines
    /// in a viewer (plan 17, round 3); the context keeps one measurer per style, the paragraph
    /// one of its own.
    /// </summary>
    [Fact]
    public void LinesThatMergedClusters_SpannedSelectedAndCareted_AllocateNothingPerFrame()
    {
        var style = Style();
        using var surface = Surface();
        var spanned = (HultUI.Text.PipelineTextParagraph)surface.RenderContext.LayoutParagraph(MergedClusters, style, 180, Spans);
        Assert.Contains(spanned.TextLines, line => line.Runs.Any(run => run.Glyphs.Length != run.Text.Length));
        var boxes = new List<Rect>();
        float caret = 0;

        double perFrame = AllocatedPerFrame(surface, ctx =>
        {
            ctx.DrawText(MergedClusters, new Rect(10, 10, 180, 200), style, TextOverflow.Clip, null, Spans);
            ctx.DrawParagraph(spanned, new Point(200, 10));
            boxes.Clear();
            spanned.SelectionBoxes(2, 50, boxes, lineEndWidth: 4);
            caret += ctx.TextCaretX("Cafe\u0301 \U0001F600", style, 5) + ctx.TextOffsetAtX("Cafe\u0301 \U0001F600", style, 30f);
        });

        Assert.NotEmpty(boxes);
        Assert.True(caret > 0);
        Assert.True(perFrame < 1, $"drawing, selecting and placing a caret in lines that merged clusters allocated {perFrame:0.#} B per frame");
    }

    [Fact]
    public void ARoundedBorderWithSidesOfTheirOwn_AllocatesNothingPerFrame()
    {
        using var surface = Surface();
        double perFrame = AllocatedPerFrame(surface, ctx =>
        {
            ctx.DrawBorder(new Rect(10, 10, 120, 40), new CornerRadius(6), Color.Black,
                new Thickness(1, 2, 3, 4), BorderStyle.Solid);
            ctx.DrawBorder(new Rect(10, 60, 120, 40), new CornerRadius(4), Color.Black,
                new Thickness(0, 0, 2, 0), BorderStyle.Solid);
        });

        Assert.True(perFrame < 1, $"drawing two rounded per-side borders allocated {perFrame:0.#} B per frame");
    }

    /// <summary>
    /// A run drawn for the first time becomes a text blob, built in one builder the context
    /// keeps: <c>Build</c> hands the blob over and leaves the builder empty for the next. A new
    /// <c>SKTextBlobBuilder</c> per blob was 80 B more per new blob (216 → 136 B; plan 17, round
    /// 2), ~4 KB a step when a scrolled list draws ~50 new rows. The glyphs go in through the raw
    /// run buffer, a struct: <c>AllocatePositionedRun</c>'s buffer is a class, 56 B more per new
    /// blob (136 → 80 B; plan 17, round 3) wherever the compiler does not keep it on the stack —
    /// here, and in the NativeAOT build of the perf suite (scroll/jump 9.1 → 6.3 KB a step).
    /// </summary>
    [Fact]
    public void ANewBlob_IsBuiltInTheKeptBuilder()
    {
        using var surface = Surface();
        var ctx = (SkiaRenderContext)surface.RenderContext;
        var style = Style();
        var paragraphs = new TextParagraph[SkiaRenderContext.RunBlobCacheMaxSize + 1_000];
        for (int i = 0; i < paragraphs.Length; i++)
            paragraphs[i] = ctx.LayoutParagraph($"Row {i:D5} of the list", style, 400);

        ctx.BeginFrame(600, 400, 1f, Color.White);
        // Draw until the blob cache reaches its bound and empties: an emptied dictionary keeps
        // its capacity, so the misses measured after it grow nothing and cost only the blob.
        int next = 0, lastCount = 0;
        while (next < paragraphs.Length && ctx.CachedBlobCount >= lastCount)
        {
            lastCount = ctx.CachedBlobCount;
            ctx.DrawParagraph(paragraphs[next++], new Point(10, 10));
        }
        Assert.True(next < paragraphs.Length - 500, "the blob cache never reached its bound");

        int countBefore = ctx.CachedBlobCount;
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = next; i < next + 500; i++)
            ctx.DrawParagraph(paragraphs[i], new Point(10, 10));
        long bytes = GC.GetAllocatedBytesForCurrentThread() - before;
        int misses = ctx.CachedBlobCount - countBefore;
        ctx.EndFrame();

        Assert.True(misses >= 500, $"only {misses} new blobs over 500 new rows");
        double perBlob = bytes / (double)misses;
        // 80 B is the blob's own wrapper and its handle registration; a run buffer object adds
        // 56, a builder 80.
        Assert.True(perBlob < 112, $"a new blob allocated {perBlob:0.#} B");
    }

    /// <summary>The kept builder starts each blob empty: text drawn after other new text looks
    /// exactly as it does on a fresh surface, with none of the earlier glyphs in it.</summary>
    [Fact]
    public void ABlobBuiltAfterOthers_HoldsOnlyItsOwnGlyphs()
    {
        var style = Style();

        using var fresh = Surface();
        fresh.RenderContext.BeginFrame(600, 400, 1f, Color.White);
        fresh.RenderContext.DrawText(Paragraph, new Rect(10, 10, 300, 200), style, TextOverflow.Clip, null);
        fresh.RenderContext.EndFrame();
        fresh.Present();
        var expected = fresh.ReadPixels(out _, out _, out _);

        using var used = Surface();
        var ctx = used.RenderContext;
        ctx.BeginFrame(600, 400, 1f, Color.White);
        for (int i = 0; i < Labels.Length; i++)
            ctx.DrawText(Labels[i], new Rect(10, 10 + i * 20, 200, 20), style, TextOverflow.Clip, null);
        ctx.EndFrame();
        ctx.BeginFrame(600, 400, 1f, Color.White);
        ctx.DrawText(Paragraph, new Rect(10, 10, 300, 200), style, TextOverflow.Clip, null);
        ctx.EndFrame();
        used.Present();

        Assert.Equal(expected, used.ReadPixels(out _, out _, out _));
    }

    private static readonly string[] Labels =
        ["Name", "Size", "Modified", "Author", "Branch main", "3 files changed", "Commit", "Settings"];
}
