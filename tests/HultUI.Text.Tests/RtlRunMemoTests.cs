using HultUI.Core.Layout;
using HultUI.Core.Rendering;
using HultUI.Core.Styling;
using Xunit;

namespace HultUI.Text.Tests;

/// <summary>
/// Right-to-left runs bypass the word cache and are shaped whole; the pipeline keeps them whole,
/// by their characters, face and size (docs/plans/19-run-shaping.md, item 3). A run is shaped
/// once for each face and size it is laid out in, and the layouts are the ones shaping every run
/// again gives (<see cref="TextPipeline.MemoizesRtlRuns"/> off).
/// </summary>
public class RtlRunMemoTests
{
    private static readonly ResolvedInheritedStyle Style = new(
        FontFamily: "TestFont", FontSize: 16f, FontWeight: FontWeight.Regular,
        TextColor: new(0, 0, 0, 255), TextDirection: TextDirection.Ltr, Cursor: CursorType.Default);

    private static readonly string[] HebrewWords =
        ["שלום", "עולם", "ספר", "בית", "מחשב", "חלון", "קובץ", "תיקייה", "שורה", "טקסט", "מסך", "עכבר"];

    // Hebrew alone, and Hebrew with a Latin word in every third place, as the perf suite's rows.
    private static string Paragraph(Random rng, int words, bool latin) =>
        string.Join(' ', Enumerable.Range(0, words).Select(i =>
            latin && i % 3 == 2 ? $"word{rng.Next(20)}" : HebrewWords[rng.Next(HebrewWords.Length)]));

    private static IEnumerable<string> Texts()
    {
        var rng = new Random(19);
        for (int i = 0; i < 40; i++)
            yield return Paragraph(rng, 30, latin: i % 2 == 0);
    }

    private static readonly float[] Widths = [900f, 640f, 500f, 380f, 260f, 640f, 900f];

    [Fact]
    public void ARightToLeftRun_IsShapedOnceForEachFaceAndSize()
    {
        var memo = new RecordingShaper();
        var again = new RecordingShaper();
        var pipeline = new TextPipeline(new StubFontCollection(), memo);
        var reference = new TextPipeline(new StubFontCollection(), again) { MemoizesRtlRuns = false };
        foreach (float width in Widths)
            foreach (string text in Texts())
            {
                pipeline.Layout(text, Style, width, null);
                reference.Layout(text, Style, width, null);
                pipeline.Layout(text, Style, width, 1, TextOverflow.Ellipsis);
                reference.Layout(text, Style, width, 1, TextOverflow.Ellipsis);
            }

        var shapedAgain = memo.Rtl.GroupBy(t => t).Where(g => g.Count() > 1).Select(g => g.Key.Text).ToList();
        Assert.True(shapedAgain.Count == 0, $"{shapedAgain.Count} runs shaped more than once, as \"{shapedAgain.FirstOrDefault()}\"");
        Assert.True(memo.Rtl.Count > 100, $"only {memo.Rtl.Count} runs shaped");
        // Without the memo the same runs are shaped many times over: what the test would catch.
        Assert.True(again.Rtl.Count > 3 * memo.Rtl.Count, $"{again.Rtl.Count} shaped again, {memo.Rtl.Count} kept");
        Assert.True(memo.Rtl.Select(t => t.Text).ToHashSet().SetEquals(again.Rtl.Select(t => t.Text)));
        Assert.Equal(again.Ltr.Count, memo.Ltr.Count);   // left-to-right text goes as it went
    }

    [Fact]
    public void ALayoutFromTheMemo_IsTheLayoutShapingEveryRunGives()
    {
        var pipeline = new TextPipeline(new StubFontCollection(), new StubTextShaper());
        var reference = new TextPipeline(new StubFontCollection(), new StubTextShaper()) { MemoizesRtlRuns = false };
        var spans = new[] { new TextSpan(6, 14, HultUI.Core.Primitives.Color.Black, Weight: FontWeight.Bold, FontSize: 20f) };
        foreach (var direction in new[] { TextDirection.Ltr, TextDirection.Rtl })
            foreach (float width in Widths)
                foreach (string text in Texts())
                {
                    var style = Style with { TextDirection = direction };
                    Assert.Equal(Of(reference.Layout(text, style, width, null)), Of(pipeline.Layout(text, style, width, null)));
                    Assert.Equal(Of(reference.Layout(text, style, width, 1, TextOverflow.Ellipsis)),
                        Of(pipeline.Layout(text, style, width, 1, TextOverflow.Ellipsis)));
                    Assert.Equal(Of(reference.Layout(text, style, width, null, TextOverflow.Clip, spans)),
                        Of(pipeline.Layout(text, style, width, null, TextOverflow.Clip, spans)));
                }

        static string Of(TextLayout layout) => $"{layout.Size}|{layout.IsRtlParagraph}|" + string.Join('/', layout.Lines.Select(line =>
            $"{line.Start}+{line.Length} {line.Width}:" + string.Join(',', line.Runs.Select(r =>
                $"{r.Text}@{r.OffsetX}~{r.Level}={r.Width}[{string.Join(' ', r.Glyphs.Select(g => $"{g.GlyphId}:{g.X}:{g.Advance}"))}]"))));
    }

    [Fact]
    public void ARunFromTheMemo_IsARunOfItsOwn_OverTheGlyphsKept()
    {
        // Layout sets a run's offset and level: two lines holding the same run must not share
        // the object. The glyphs, which nothing writes to, are shared.
        var pipeline = new TextPipeline(new StubFontCollection(), new StubTextShaper());
        string hebrew = "שלום עולם";
        var a = pipeline.Layout("abc " + hebrew + " def", Style, 10_000f, null).Lines[0].Runs.Single(r => r.IsRtl);
        var b = pipeline.Layout("uvwxyz " + hebrew, Style, 10_000f, null).Lines[0].Runs.Single(r => r.IsRtl);

        Assert.Equal(hebrew, a.Text);
        Assert.Equal(hebrew, b.Text);
        Assert.NotSame(a, b);
        Assert.Same(a.Glyphs, b.Glyphs);
        Assert.NotEqual(a.OffsetX, b.OffsetX);
    }

    [Fact]
    public void TheMemo_KeepsARunForEachFaceAndSize()
    {
        var shaper = new RecordingShaper();
        var pipeline = new TextPipeline(new TwoFaces(), shaper);
        string text = "abc שלום עולם def";
        pipeline.Layout(text, Style, 10_000f, null);
        pipeline.Layout(text, Style with { FontSize = 20f }, 10_000f, null);
        pipeline.Layout(text, Style with { FontWeight = FontWeight.Bold }, 10_000f, null);
        pipeline.Layout(text, Style with { FontSize = 20f }, 640f, null);   // found again: no fourth
        Assert.Equal(3, shaper.Rtl.Count(t => t.Text == "שלום עולם"));
        Assert.Equal(shaper.Rtl.Count, shaper.Rtl.Distinct().Count());   // nothing shaped twice
    }

    private sealed class RecordingShaper : ITextShaper
    {
        private readonly StubTextShaper _inner = new();
        public readonly List<(string Text, IResolvedFont Font, float Size)> Rtl = new();
        public readonly List<(string Text, IResolvedFont Font, float Size)> Ltr = new();

        public ShapedRun Shape(string text, IResolvedFont font, float fontSize, bool rtl = false)
        {
            (rtl ? Rtl : Ltr).Add((text, font, fontSize));
            return _inner.Shape(text, font, fontSize, rtl);
        }
    }

    // A regular face and a bold one, told apart by their widths.
    private sealed class TwoFaces : IFontCollection
    {
        private readonly StubResolvedFont _regular = new(10f), _bold = new(11f);

        public IResolvedFont Resolve(string familyName, FontWeight weight, FontSlant slant) =>
            weight >= FontWeight.Bold ? _bold : _regular;

        public IResolvedFont? MatchCharacter(int codepoint, FontWeight weight, FontSlant slant) =>
            Resolve("", weight, slant);
    }
}
