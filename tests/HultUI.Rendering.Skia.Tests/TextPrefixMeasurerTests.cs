using HultUI.Core.Layout;
using HultUI.Core.Rendering;
using HultUI.Core.Styling;
using HultUI.Testing;
using HultUI.Text;
using Xunit;

namespace HultUI.Rendering.Skia.Tests;

/// <summary>
/// Inside a run whose shaping merged clusters — emoji, a family joined by ZWJ, a flag, a skin tone,
/// combining marks, Thai, Devanagari, Hebrew points, a lam-alef — a line's caret, hit-test and
/// selection ask for the widths of prefixes of the run's text (<see cref="TextPrefixMeasurer"/>),
/// for every line drawn with a span or a selection on it, every frame. They are measured where they
/// stand in the run's text: the number the prefix cut out and laid out on a line of its own gives,
/// to the bit, and found again without a string.
/// </summary>
public class TextPrefixMeasurerTests
{
    private static readonly ResolvedInheritedStyle Base = new(
        FontFamily: "sans-serif", FontSize: 14f, FontWeight: FontWeight.Regular,
        TextColor: new(0, 0, 0, 255), TextDirection: TextDirection.Ltr, Cursor: CursorType.Default);

    private static readonly ResolvedInheritedStyle[] Styles =
    [
        Base,
        Base with { TextDirection = TextDirection.Rtl },
        Base with { TextDirection = TextDirection.Auto },
        Base with { LetterSpacing = 0.6f },
        Base with { FontSize = 12f, FontWeight = FontWeight.Bold },
        Base with { FontFamily = FontFamilies.Monospace, FontSize = 12f },
    ];

    // Numeric code points: the editing tools turn written-out escapes into the characters.
    private static string C(params int[] codePoints) => string.Concat(codePoints.Select(char.ConvertFromUtf32));

    private static readonly string[] Lines =
    [
        "  Emoji:     " + C(0x1F600) + " " + C(0x1F389) + " " + C(0x1F44D, 0x1F3FD) + " "
            + C(0x1F468, 0x200D, 0x1F469, 0x200D, 0x1F467) + " " + C(0x1F1F8, 0x1F1EA),
        "  Combining: e" + C(0x0301) + " a" + C(0x0308) + " o" + C(0x0302) + " n" + C(0x0303) + " a" + C(0x0323, 0x0302),
        "Thai " + C(0x0E01, 0x0E33, 0x0E25, 0x0E31, 0x0E07) + " and Devanagari " + C(0x0915, 0x094D, 0x0937, 0x0924, 0x094D, 0x0930, 0x093F),
        "Hebrew " + C(0x05E9, 0x05C1, 0x05B8, 0x05DC, 0x05D5, 0x05B9, 0x05DD) + " and " + C(0x0644, 0x0627, 0x0020, 0x0645, 0x0631, 0x062D, 0x0628, 0x0627),
        "office affluent fjord " + C(0x1F600) + " keycap #" + C(0xFE0F, 0x20E3),
    ];

    [Fact]
    public void EveryPrefixOfAMergedRun_MeasuresWhatItsOwnLineDoes_ToTheBit()
    {
        var services = new SkiaTextServices();
        using var ctx = new SkiaRenderContext();
        ctx.SetPipeline(services.Pipeline);
        IRenderContext context = ctx;
        var reference = new TextPipeline(services.Fonts, services.Shaper);
        int merged = 0, compared = 0;
        foreach (var style in Styles)
            foreach (var text in Lines)
            {
                var paragraph = services.Pipeline.LayoutParagraph(text, style, float.PositiveInfinity, spans: null);
                var line = paragraph.TextLines[0];
                var measure = paragraph.PrefixMeasurer(line);
                foreach (var run in line.Runs)
                {
                    if (run.Glyphs.Length == run.Text.Length) continue;
                    merged++;
                    Assert.NotNull(measure);
                    for (int n = 1; n < run.Text.Length; n++)
                    {
                        float expected = reference.Layout(run.Text[..n], style, float.PositiveInfinity, maxLines: 1).Size.Width;
                        // Asked three times, as frames do: measured, kept, found.
                        for (int time = 0; time < 3; time++)
                        {
                            AssertBits(expected, context.MeasureTextWidth(run.Text.AsSpan(0, n), style), run.Text, n, style, time);
                            AssertBits(expected, measure!(run.Text, n), run.Text, n, style, time);
                            compared += 2;
                        }
                    }
                }

                // The context's own caret and selection answers (on the line it lays out) are the
                // ones the cut-out prefixes give, as it measured them before.
                var single = services.Pipeline.Layout(text, style, float.PositiveInfinity, maxLines: 1).Lines[0];
                TextPrefixMeasurer? cut = MergesClusters(single) ? (t, n) => context.MeasureText(t[..n], style).Width : null;
                for (int offset = 0; offset <= single.DrawnLength; offset++)
                {
                    float expected = single.CaretXAt(offset, cut);
                    Assert.Equal(BitConverter.SingleToInt32Bits(expected),
                        BitConverter.SingleToInt32Bits(context.TextCaretX(text, style, offset)));
                    var ranges = context.TextSelectionRanges(text, style, 0, offset);
                    var expectedRanges = single.VisualRanges(0, offset, cut);
                    Assert.Equal(expectedRanges.Count, ranges.Count);
                    for (int r = 0; r < ranges.Count; r++)
                        Assert.Equal((expectedRanges[r].X0, expectedRanges[r].X1), (ranges[r].X0, ranges[r].X1));
                }
            }
        // The emoji line and the last merge clusters with any faces (a surrogate pair is one glyph
        // even as .notdef); the others need faces a Linux runner may lack (64 runs on Windows).
        Assert.True(merged >= Styles.Length * 2, $"only {merged} runs merged clusters");
        Assert.True(compared > 300, $"only {compared} prefixes");   // 2,322 on Windows
    }

    /// <summary>A <see cref="TextLine"/> asked with a measurer of the prefix as a string of its own
    /// answers as it does with one that measures it where it stands.</summary>
    [Fact]
    public void AMeasurerOfCutOutPrefixes_AnswersAsOneOfPrefixesInPlace()
    {
        var services = new SkiaTextServices();
        var pipeline = services.Pipeline;
        var buffer = new List<VisualRange>();
        int lines = 0;
        foreach (var style in Styles)
            foreach (var text in Lines)
            {
                var line = pipeline.Layout(text, style, float.PositiveInfinity, maxLines: 1).Lines[0];
                if (!MergesClusters(line)) continue;
                lines++;
                Func<string, float> byString = s => pipeline.Layout(s, style, float.PositiveInfinity, maxLines: 1).Size.Width;
                TextPrefixMeasurer inPlace = (t, n) => pipeline.MeasureWidth(t.AsSpan(0, n), style);
                for (int offset = 0; offset <= line.DrawnLength; offset++)
                {
                    Assert.Equal(line.CaretXAt(offset, inPlace), line.CaretXAt(offset, byString));
                    Assert.Equal(line.OffsetAtX(line.CaretXAt(offset, inPlace) + 0.3f, inPlace),
                        line.OffsetAtX(line.CaretXAt(offset, inPlace) + 0.3f, byString));
                    Assert.Equal(line.VisualRanges(0, offset, inPlace), line.VisualRanges(0, offset, byString));
                    line.VisualRanges(offset / 2, offset, buffer, byString);
                    Assert.Equal(line.VisualRanges(offset / 2, offset, inPlace), buffer);
                    Assert.Equal(line.TryMoveCaretVisually(offset, true, out int a, inPlace),
                        line.TryMoveCaretVisually(offset, true, out int b, byString));
                    Assert.Equal(a, b);
                }
            }
        Assert.True(lines >= Styles.Length * 2, $"only {lines} lines merged clusters");
    }

    /// <summary>
    /// Once a line's prefixes have been measured, mapping its carets and selections again — what
    /// every frame that draws it with a span or a selection does — makes no string and allocates
    /// nothing. With the prefixes cut out, a line of emoji cost a string per boundary per frame.
    /// </summary>
    [Fact]
    public void MappingALineOfMergedClustersAgain_AllocatesNothing()
    {
        var services = new SkiaTextServices();
        var pipeline = services.Pipeline;
        var buffer = new List<VisualRange>();
        var work = new List<(TextLine Line, TextPrefixMeasurer Measure)>();
        foreach (var style in Styles)
            foreach (var text in Lines)
            {
                var paragraph = pipeline.LayoutParagraph(text, style, float.PositiveInfinity, spans: null);
                var line = paragraph.TextLines[0];
                if (paragraph.PrefixMeasurer(line) is { } measure) work.Add((line, measure));
            }
        Assert.True(work.Count >= Styles.Length * 2, $"only {work.Count} lines merged clusters");

        void Round()
        {
            foreach (var (line, measure) in work)
                for (int offset = 0; offset <= line.DrawnLength; offset++)
                {
                    line.CaretXAt(offset, measure);
                    line.VisualRanges(0, offset, buffer, measure);
                    line.OffsetAtX(offset * 3.7f, measure);
                }
        }
        Round();
        Assert.Equal(0, Allocations.Least(Round));
    }

    private static bool MergesClusters(TextLine line) => line.Runs.Any(r => r.Glyphs.Length != r.Text.Length);

    private static void AssertBits(float expected, float actual, string runText, int n, ResolvedInheritedStyle style, int time) =>
        Assert.True(BitConverter.SingleToInt32Bits(expected) == BitConverter.SingleToInt32Bits(actual),
            $"\"{runText[..n]}\" ({n} of \"{runText}\") {style.TextDirection} {style.FontFamily} {style.FontSize} "
            + $"spaced {style.LetterSpacing}, time {time + 1}: laid out {expected}, measured {actual}");
}
