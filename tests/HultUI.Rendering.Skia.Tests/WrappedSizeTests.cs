using HultUI.Core.Primitives;
using HultUI.Core.Rendering;
using HultUI.Text;
using HultUI.Text.Tests;
using Xunit;

namespace HultUI.Rendering.Skia.Tests;

/// <summary>
/// A height ask (<see cref="TextMeasure.MeasureWrapped"/>) breaks the text as a layout does and adds its lines up,
/// making no layout (TextPipeline.WrappedSize): a list of wrapped rows asks every row's height and draws a few. Its
/// size must be the reference layout's to the bit at every width of a drag, the range of widths it keeps must hold
/// to its ends, a layout asked of the same pipeline between the sizes must be the reference's, and an ask that breaks
/// the text again must allocate nothing. Over <see cref="WrapRangeTests"/>' corpus and styles.
/// </summary>
public class WrappedSizeTests
{
    [Fact]
    public void ASizeAsk_IsTheReferenceLayoutsSize_AtEveryWidthOfADrag()
    {
        var fonts = new SkiaFontCollection();
        var shaper = new SkiaTextShaper();
        int compared = 0, layouts = 0, kept = 0, wrapped = 0, row = 0;
        foreach (var style in WrapRangeTests.Styles)
        {
            var sizes = new TextPipeline(fonts, shaper);
            var reference = new TextPipeline(fonts, shaper) { KeepsWrapRanges = false };
            foreach (float width in WrapRangeTests.Drag(640f))
            {
                int before = sizes.WrapRangeHits;
                foreach (var text in WrapRangeTests.Texts)
                {
                    var expected = reference.Layout(text, style, width, null);
                    SameSize(expected.Size, sizes.WrappedSize(text, style, width, null), $"\"{text[..20]}\" w={width:R} {style}");
                    compared++;
                    if (expected.Lines.Count > 1) wrapped++;
                    // A row drawn: its layout, asked after its size at the same width.
                    if (++row % 3 == 0)
                    {
                        LayoutAssert.Same(expected, sizes.Layout(text, style, width, null), $"\"{text[..20]}\" drawn w={width:R} {style}");
                        layouts++;
                    }
                }
                kept += sizes.WrapRangeHits - before;
            }
        }
        Assert.True(compared > 1_000 && layouts > 300, $"compared only {compared} sizes and {layouts} layouts");
        // Most steps of the drag keep a paragraph's lines: with nothing kept the sizes above would all be broken again.
        Assert.True(kept * 2 > wrapped, $"kept {kept} of {wrapped} wrapped sizes");
    }

    [Fact]
    public void ASizeAsksRange_HoldsToItsEnds_AndNoFurther()
    {
        var fonts = new SkiaFontCollection();
        var shaper = new SkiaTextShaper();
        int ranges = 0, probes = 0;
        foreach (var style in WrapRangeTests.Styles)
        foreach (float start in new[] { 640f, 517.33f })
        {
            var reference = new TextPipeline(fonts, shaper) { KeepsWrapRanges = false };
            foreach (var text in WrapRangeTests.Texts)
            {
                var sizes = new TextPipeline(fonts, shaper);
                sizes.WrappedSize(text, style, start - 150f, null);   // seen once, laid out and not drawn
                var (layouts, _) = sizes.LayoutCacheUse;
                sizes.WrappedSize(text, style, start, null);
                Assert.Equal(layouts, sizes.LayoutCacheUse.Entries);   // the range at start is a size's
                if (!sizes.KeptWrapRange(text, style, null, out float from, out float below)) continue;
                ranges++;
                Assert.True(from <= start && start < below, $"[{from:R}, {below:R}) leaves out {start:R}");
                string at = $"\"{text[..20]}\" [{from:R}, {below:R}) {style}";
                float mid = float.IsPositiveInfinity(below) ? from + 100f : from + (below - from) / 2f;
                if (mid >= below) mid = from;
                foreach (float t in new[] { from, MathF.BitDecrement(below), mid })
                {
                    int hits = sizes.WrapRangeHits;
                    SameSize(reference.Layout(text, style, t, null).Size, sizes.WrappedSize(text, style, t, null), $"{at} at {t:R}");
                    Assert.Equal(hits + 1, sizes.WrapRangeHits);
                    probes++;
                }
                foreach (float t in new[] { MathF.BitDecrement(from), below })
                {
                    if (float.IsInfinity(t)) continue;
                    SameSize(reference.Layout(text, style, t, null).Size, sizes.WrappedSize(text, style, t, null), $"{at} past an end at {t:R}");
                    probes++;
                }
            }
        }
        Assert.True(ranges > 50 && probes > 5 * 50, $"only {ranges} ranges and {probes} probes");
    }

    [Fact]
    public void ASizeAsk_BreaksAgainWithoutALayout_AndAllocatesNothing()
    {
        var pipeline = new TextPipeline(new SkiaFontCollection(), new SkiaTextShaper());
        var reference = new TextPipeline(new SkiaFontCollection(), new SkiaTextShaper()) { KeepsWrapRanges = false };
        var style = WrapRangeTests.Styles[0];
        string paragraph = WrapRangeTests.Latin[3];
        pipeline.WrappedSize(paragraph, style, 720f, null);   // its first ask, which lays it out (and does not draw it)
        var (layouts, _) = pipeline.LayoutCacheUse;
        // The drag once: shaped, its words and the prefixes its lines' ends are cut at cached, the lists grown.
        for (float w = 690f; w > 310f; w -= 37f) pipeline.WrappedSize(paragraph, style, w, null);

        // And again: each width outside the range the last one kept, so the text is broken again each time.
        int asks = 0, hits = pipeline.WrapRangeHits;
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (float w = 690f; w > 310f; w -= 37f, asks++) pipeline.WrappedSize(paragraph, style, w, null);
        long bytes = GC.GetAllocatedBytesForCurrentThread() - before;
        int broken = asks - (pipeline.WrapRangeHits - hits);

        Assert.True(broken >= 5, $"only {broken} of {asks} asks broke the text again");
        Assert.Equal(0, bytes);
        Assert.Equal(layouts, pipeline.LayoutCacheUse.Entries);   // no layout made since, both times
        for (float w = 690f; w > 310f; w -= 37f)
            SameSize(reference.Layout(paragraph, style, w, null).Size, pipeline.WrappedSize(paragraph, style, w, null), $"w={w:R}");
    }

    [Fact]
    public void ASizeAsk_LaysOutWhatIsLikelyDrawnNext()
    {
        var pipeline = new TextPipeline(new SkiaFontCollection(), new SkiaTextShaper());
        var style = WrapRangeTests.Styles[0];
        string drawn = WrapRangeTests.Latin[0], notDrawn = WrapRangeTests.Latin[1], neverSeen = WrapRangeTests.Latin[2];
        _ = pipeline.Layout(drawn, style, 640f, null).Lines[0].Runs;   // drawn: its first line's runs read
        _ = pipeline.Layout(notDrawn, style, 640f, null);              // laid out, not drawn
        foreach (var text in new[] { drawn, notDrawn, neverSeen })
        {
            pipeline.WrappedSize(text, style, 400f, null);
            int hits = pipeline.WrapRangeHits;
            pipeline.Layout(text, style, 400f, null);
            // The drawn row's layout and the one never seen were made by their size asks; the other's is made now.
            Assert.Equal(text == notDrawn ? hits : hits + 1, pipeline.WrapRangeHits);
        }
        // Not drawn at 400: its next size ask makes no layout.
        pipeline.WrappedSize(drawn, style, 300f, null);
        int before = pipeline.WrapRangeHits;
        pipeline.Layout(drawn, style, 300f, null);
        Assert.Equal(before, pipeline.WrapRangeHits);
    }

    [Fact]
    public void TextMeasure_AsksTheContextForTheSizeAlone()
    {
        var pipeline = new TextPipeline(new SkiaFontCollection(), new SkiaTextShaper());
        var reference = new TextPipeline(new SkiaFontCollection(), new SkiaTextShaper()) { KeepsWrapRanges = false };
        var style = WrapRangeTests.Styles[1];
        string paragraph = WrapRangeTests.Latin[0];
        var saved = TextMeasure.Context;
        TextMeasure.Context = new SkiaMeasureContext(pipeline);
        try
        {
            TextMeasure.MeasureWrapped(paragraph, style, 660f);
            var (layouts, _) = pipeline.LayoutCacheUse;
            for (float w = 630f; w > 250f; w -= 29f) TextMeasure.MeasureWrapped(paragraph, style, w);
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (float w = 630f; w > 250f; w -= 29f) TextMeasure.MeasureWrapped(paragraph, style, w);
            Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
            Assert.Equal(layouts, pipeline.LayoutCacheUse.Entries);   // no layout made since, both times
            for (float w = 630f; w > 250f; w -= 29f)
                SameSize(reference.Layout(paragraph, style, w, null).Size, TextMeasure.MeasureWrapped(paragraph, style, w), $"w={w:R}");
        }
        finally { TextMeasure.Context = saved; }
    }

    private static void SameSize(Size expected, Size actual, string where)
    {
        Assert.True(BitConverter.SingleToInt32Bits(expected.Width) == BitConverter.SingleToInt32Bits(actual.Width)
            && BitConverter.SingleToInt32Bits(expected.Height) == BitConverter.SingleToInt32Bits(actual.Height),
            $"{where}: {actual} is not the layout's {expected}");
    }
}
