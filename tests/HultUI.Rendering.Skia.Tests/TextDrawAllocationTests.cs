using HultUI.Core.Layout;
using HultUI.Core.Primitives;
using HultUI.Core.Rendering;
using HultUI.Core.Styling;
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
/// <para>The bound is under one byte per frame on average, not zero, so a one-off allocation
/// somewhere in the runtime does not fail the test; anything drawn per frame costs tens of bytes
/// a frame.</para>
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
    /// caches are warm.</summary>
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
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < Frames; i++) Frame();
        return (GC.GetAllocatedBytesForCurrentThread() - before) / (double)Frames;
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

    private static readonly string[] Labels =
        ["Name", "Size", "Modified", "Author", "Branch main", "3 files changed", "Commit", "Settings"];
}
