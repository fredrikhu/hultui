using HultUI.Core.Layout;
using HultUI.Core.Styling;
using HultUI.Text;
using Xunit;

namespace HultUI.Rendering.Skia.Tests;

/// <summary>
/// <see cref="TextPipeline.ShaperCalls"/> with HarfBuzz behind it counts the calls a shaper that
/// can cut runs takes too: a prefix table shapes its long words with their cuts, through the
/// shaper's other door (docs/plans/19-run-shaping.md, item 4).
/// </summary>
public class ShaperCallsTests
{
    private static readonly ResolvedInheritedStyle Sans = new(
        FontFamily: "sans-serif", FontSize: 13f, FontWeight: FontWeight.Regular,
        TextColor: new(0, 0, 0, 255), TextDirection: TextDirection.Ltr, Cursor: CursorType.Default);

    [Fact]
    public void ShaperCalls_CountsTheCallsWithCuts()
    {
        using var inner = new SkiaTextShaper();
        var shaper = new CountingShaper(inner);
        var pipeline = new TextPipeline(new SkiaFontCollection(), shaper);

        pipeline.Layout("Unbreakable internationalization, שלום עולם, 12:30", Sans, 160f, null);
        pipeline.MeasurePrefixes("characteristically incomprehensibilities", 0, 40, Sans);
        pipeline.Layout("characteristically", Sans, 60f, 1, TextOverflow.Ellipsis);

        Assert.True(shaper.WithCuts > 0, "no word was shaped with its cuts");
        Assert.Equal(shaper.Calls + shaper.WithCuts, pipeline.ShaperCalls);
    }

    private sealed class CountingShaper(SkiaTextShaper inner) : ITextShaper, IShapesWithCuts
    {
        public long Calls, WithCuts;

        public ShapedRun Shape(string text, IResolvedFont font, float fontSize, bool rtl = false)
        {
            Calls++;
            return inner.Shape(text, font, fontSize, rtl);
        }

        ShapedRun IShapesWithCuts.Shape(string text, IResolvedFont font, float fontSize, out RunCuts? cuts)
        {
            WithCuts++;
            return ((IShapesWithCuts)inner).Shape(text, font, fontSize, out cuts);
        }
    }
}
