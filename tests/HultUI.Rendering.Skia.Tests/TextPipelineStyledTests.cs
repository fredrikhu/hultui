using HultUI.Core.Layout;
using HultUI.Core.Primitives;
using HultUI.Core.Rendering;
using HultUI.Core.Styling;
using HultUI.Text;
using Xunit;

namespace HultUI.Rendering.Skia.Tests;

/// <summary>
/// Metric-affecting spans (stage 2 of docs/plans/12-rich-text-spans.md): a bold or
/// resized range shapes differently and must measure, wrap and stack accordingly —
/// while paint-only span lists stay byte-identical to the plain path, cache
/// entries included.
/// </summary>
public class TextPipelineStyledTests
{
    private static readonly ResolvedInheritedStyle Style = new(
        FontFamily: "sans-serif", FontSize: 16f, FontWeight: FontWeight.Regular,
        TextColor: new(0, 0, 0, 255), TextDirection: TextDirection.Ltr, Cursor: CursorType.Default);

    private static TextPipeline CreatePipeline() =>
        new(new SkiaFontCollection(), new SkiaTextShaper());

    private const string Text = "hello wonderful world";

    [Fact]
    public void ABoldSpan_MakesItsRangeWider()
    {
        var pipeline = CreatePipeline();
        var plain = pipeline.Layout(Text, Style, 10_000, null);
        var bold = pipeline.Layout(Text, Style, 10_000, null, TextOverflow.Clip,
            [new TextSpan(6, 9, default, Weight: FontWeight.Bold)]);   // "wonderful"

        Assert.True(bold.Size.Width > plain.Size.Width + 0.5f,
            $"bold {bold.Size.Width} should exceed plain {plain.Size.Width}");
    }

    [Fact]
    public void ABoldSpan_WrapsWhereTheBolderTextActuallyEnds()
    {
        var pipeline = CreatePipeline();
        float plainWidth = pipeline.Layout(Text, Style, 10_000, null).Size.Width;

        // Wide enough for the plain text, too narrow for the bold version.
        var spans = new[] { new TextSpan(6, 9, default, Weight: FontWeight.Bold) };
        float boldWidth = pipeline.Layout(Text, Style, 10_000, null, TextOverflow.Clip, spans).Size.Width;
        Assert.True(boldWidth > plainWidth);   // precondition, same as the test above

        float maxWidth = (plainWidth + boldWidth) / 2f;
        var plain = pipeline.Layout(Text, Style, maxWidth, null);
        var bold = pipeline.Layout(Text, Style, maxWidth, null, TextOverflow.Clip, spans);

        Assert.Single(plain.Lines);
        Assert.True(bold.Lines.Count > 1,
            "the bold range no longer fits the width the plain text fits, so it must wrap");
    }

    [Fact]
    public void ALargerSizeSpan_RaisesItsLineHeight()
    {
        var pipeline = CreatePipeline();
        var plain = pipeline.Layout(Text, Style, 10_000, null);
        var sized = pipeline.Layout(Text, Style, 10_000, null, TextOverflow.Clip,
            [new TextSpan(6, 9, default, FontSize: 32f)]);

        Assert.True(sized.Size.Height > plain.Size.Height + 1f,
            $"a 32px run inside 16px text must raise the line ({sized.Size.Height} vs {plain.Size.Height})");
    }

    [Fact]
    public void ColorOnlySpans_TakeThePlainPath_CacheEntryIncluded()
    {
        var pipeline = CreatePipeline();
        var plain = pipeline.Layout(Text, Style, 120f, null);
        var colored = pipeline.Layout(Text, Style, 120f, null, TextOverflow.Clip,
            [new TextSpan(0, 5, new Color(200, 0, 0, 255))]);

        // Not merely equal — the same cached instance: paint-only spans must not
        // fragment the layout caches or pay any styled-path cost.
        Assert.Same(plain, colored);
    }

    [Fact]
    public void AStyledLayout_MapsLineStartsBackToTheSourceText()
    {
        var pipeline = CreatePipeline();
        var spans = new[] { new TextSpan(6, 9, default, Weight: FontWeight.Bold) };
        var layout = pipeline.Layout("aaa bbb\nccc ddd", Style, 10_000, null, TextOverflow.Clip, spans);

        Assert.Equal(2, layout.Lines.Count);
        Assert.Equal(0, layout.Lines[0].Start);
        Assert.Equal(8, layout.Lines[1].Start);
    }
}
