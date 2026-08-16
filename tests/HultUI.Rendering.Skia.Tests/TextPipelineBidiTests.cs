using HultUI.Core.Styling;
using HultUI.Text;
using Xunit;

namespace HultUI.Rendering.Skia.Tests;

/// <summary>
/// BiDi stage 1 (docs/plans/14-bidi.md): mixed-direction lines shape per
/// directional run and place runs in UAX #9 visual order, while TextLine.Runs and
/// Start/Length stay logical. Hebrew is the test script (unambiguously RTL, no
/// joining, present in every platform's font fallback).
/// </summary>
public class TextPipelineBidiTests
{
    private static ResolvedInheritedStyle Style(TextDirection direction = TextDirection.Ltr) => new(
        FontFamily: "sans-serif", FontSize: 16f, FontWeight: FontWeight.Regular,
        TextColor: new(0, 0, 0, 255), TextDirection: direction, Cursor: CursorType.Default);

    private static TextPipeline CreatePipeline() =>
        new(new SkiaFontCollection(), new SkiaTextShaper());

    private static float OffsetOf(TextLayout layout, string fragment)
    {
        foreach (var run in layout.Lines[0].Runs)
            if (run.Text.Contains(fragment))
                return run.OffsetX;
        throw new Xunit.Sdk.XunitException(
            $"no run contains '{fragment}'; runs: [{string.Join(" | ", layout.Lines[0].Runs.Select(r => r.Text))}]");
    }

    [Fact]
    public void AnRtlRunInsideLtrText_KeepsItsPlaceBetweenItsNeighbours()
    {
        var pipeline = CreatePipeline();
        var layout = pipeline.Layout("abc אבג def", Style(), 10_000, null);

        var line = layout.Lines[0];
        Assert.True(line.Runs.Count >= 3, $"expected direction-split runs, got {line.Runs.Count}");
        Assert.True(OffsetOf(layout, "abc") < OffsetOf(layout, "אבג"));
        Assert.True(OffsetOf(layout, "אבג") < OffsetOf(layout, "def"));
        Assert.True(line.Width > 0);
    }

    [Fact]
    public void AnRtlParagraph_ReversesRunOrderVisually()
    {
        // Logical: אבג abc דהו. In an RTL paragraph, UAX #9 L2 renders the LAST
        // logical run leftmost: דהו, then abc, then אבג rightmost — while
        // TextLine.Runs stays logical. Only the offsets carry the reordering.
        var pipeline = CreatePipeline();
        var layout = pipeline.Layout("אבג abc דהו", Style(TextDirection.Rtl), 10_000, null);

        float first = OffsetOf(layout, "אבג");
        float middle = OffsetOf(layout, "abc");
        float last = OffsetOf(layout, "דהו");

        Assert.True(last < middle, $"דהו at {last} should be left of abc at {middle}");
        Assert.True(middle < first, $"abc at {middle} should be left of אבג at {first}");
    }

    [Fact]
    public void NumbersInsideRtlText_StayLeftToRight_AndSitLeftOfTheHebrew()
    {
        // Digits are level 2 inside an RTL paragraph: their own run, placed per L2
        // — visually to the LEFT of the Hebrew that logically precedes them.
        var pipeline = CreatePipeline();
        var layout = pipeline.Layout("אבג 123", Style(TextDirection.Rtl), 10_000, null);

        Assert.True(OffsetOf(layout, "123") < OffsetOf(layout, "אבג"));
    }

    [Fact]
    public void LatinTextInAnRtlParagraph_MeasuresTheSameAsInLtr()
    {
        // Pure-Latin content resolves to LTR runs regardless of paragraph
        // direction; the bidi path must not change its width.
        var pipeline = CreatePipeline();
        float ltr = pipeline.Layout("hello wonderful world", Style(), 10_000, null).Size.Width;
        float rtl = pipeline.Layout("hello wonderful world", Style(TextDirection.Rtl), 10_000, null).Size.Width;

        Assert.True(Math.Abs(ltr - rtl) < 1f, $"ltr {ltr} vs rtl-paragraph {rtl}");
    }

    [Fact]
    public void MixedText_StillWraps_WithLogicalLineStarts()
    {
        var pipeline = CreatePipeline();
        var narrow = pipeline.Layout("abc אבג def אבג abc", Style(), 60f, null);

        Assert.True(narrow.Lines.Count > 1, "narrow mixed text must wrap");
        Assert.Equal(0, narrow.Lines[0].Start);
        for (int i = 1; i < narrow.Lines.Count; i++)
            Assert.True(narrow.Lines[i].Start > narrow.Lines[i - 1].Start,
                "line starts must remain logical and increasing");
    }
}
