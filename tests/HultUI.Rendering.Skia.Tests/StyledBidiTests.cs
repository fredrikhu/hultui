using HultUI.Core.Layout;
using HultUI.Core.Primitives;
using HultUI.Core.Rendering;
using HultUI.Core.Styling;
using HultUI.Text;
using Xunit;

namespace HultUI.Rendering.Skia.Tests;

/// <summary>
/// Right-to-left text with metric spans — a bold word, an italic one, a larger size — keeps
/// its direction: direction runs first, then style runs, then font runs (docs/plans/14-bidi.md).
/// It used to take the left-to-right styled path, and one bold word laid a Hebrew paragraph
/// out left to right, every run at level 0.
/// </summary>
public class StyledBidiTests
{
    private static ResolvedInheritedStyle Style(TextDirection direction = TextDirection.Ltr) => new(
        FontFamily: "sans-serif", FontSize: 16f, FontWeight: FontWeight.Regular,
        TextColor: new(0, 0, 0, 255), TextDirection: direction, Cursor: CursorType.Default);

    private static TextPipeline CreatePipeline() => new(new SkiaFontCollection(), new SkiaTextShaper());

    private static TextSpan Bold(string text, string word) =>
        new(text.IndexOf(word, StringComparison.Ordinal), word.Length, Color.Black, Weight: FontWeight.Bold);

    private static ShapedRun RunWith(TextLine line, string fragment) =>
        line.Runs.FirstOrDefault(r => r.Text.Contains(fragment, StringComparison.Ordinal))
        ?? throw new Xunit.Sdk.XunitException(
            $"no run contains '{fragment}'; runs: [{string.Join(" | ", line.Runs.Select(r => r.Text))}]");

    private const string Hebrew = "שלום עולם יפה";

    [Fact]
    public void ABoldWordInARightToLeftParagraph_KeepsItRightToLeft()
    {
        var layout = CreatePipeline().Layout(Hebrew, Style(TextDirection.Rtl), 10_000, null,
            TextOverflow.Clip, [Bold(Hebrew, "עולם")]);

        Assert.True(layout.IsRtlParagraph);
        var line = Assert.Single(layout.Lines);
        Assert.All(line.Runs, r => Assert.True(r.IsRtl, $"'{r.Text}' at level {r.Level}"));
        // Logical order right to left: the first word rightmost, the last leftmost.
        Assert.True(RunWith(line, "שלום").OffsetX > RunWith(line, "עולם").OffsetX);
        Assert.True(RunWith(line, "עולם").OffsetX > RunWith(line, "יפה").OffsetX);
    }

    [Fact]
    public void TheBoldWord_IsShapedInTheBoldFace()
    {
        var pipeline = CreatePipeline();
        var plain = pipeline.Layout(Hebrew, Style(TextDirection.Rtl), 10_000, null);
        var styled = pipeline.Layout(Hebrew, Style(TextDirection.Rtl), 10_000, null,
            TextOverflow.Clip, [Bold(Hebrew, "עולם")]);

        var line = styled.Lines[0];
        var bold = RunWith(line, "עולם");
        Assert.Equal("עולם", bold.Text);
        Assert.NotSame(RunWith(line, "שלום").Font, bold.Font);
        // The rest is shaped as the plain line shapes it: the same font, the same direction.
        Assert.Same(plain.Lines[0].Runs[0].Font, RunWith(line, "שלום").Font);
    }

    [Fact]
    public void AutoDirection_ResolvesHebrewWithABoldSpanRightToLeft()
    {
        var layout = CreatePipeline().Layout(Hebrew, Style(TextDirection.Auto), 10_000, null,
            TextOverflow.Clip, [Bold(Hebrew, "עולם")]);

        Assert.True(layout.IsRtlParagraph);
        Assert.True(RunWith(layout.Lines[0], "שלום").OffsetX > RunWith(layout.Lines[0], "יפה").OffsetX);
    }

    [Fact]
    public void ABoldHebrewWordInsideLatinText_ReadsRightToLeftWithinItsRun()
    {
        const string text = "commit שלום עולם merged";
        var layout = CreatePipeline().Layout(text, Style(), 10_000, null,
            TextOverflow.Clip, [Bold(text, "עולם")]);

        Assert.False(layout.IsRtlParagraph);
        var line = layout.Lines[0];
        float commit = RunWith(line, "commit").OffsetX, merged = RunWith(line, "merged").OffsetX;
        float first = RunWith(line, "שלום").OffsetX, second = RunWith(line, "עולם").OffsetX;
        // The Hebrew stays between the Latin words, and within it the second word is on the left.
        Assert.True(commit < second && second < first && first < merged,
            $"commit {commit}, עולם {second}, שלום {first}, merged {merged}");
        Assert.True(RunWith(line, "עולם").IsRtl);
        Assert.False(RunWith(line, "commit").IsRtl);
    }

    [Fact]
    public void TheCaretAtTheStartOfAStyledRightToLeftLine_IsAtItsRightEdge()
    {
        var line = CreatePipeline().Layout(Hebrew, Style(TextDirection.Rtl), 10_000, null,
            TextOverflow.Clip, [Bold(Hebrew, "עולם")]).Lines[0];

        Assert.Equal(line.Width, line.CaretXAt(0), 1);
        Assert.Equal(0f, line.CaretXAt(line.DrawnLength), 1);
    }

    [Fact]
    public void WrappedStyledRightToLeftText_IsRightToLeftOnEveryLine()
    {
        const string text = "שלום עולם יפה מאוד היום ספר בית מחשב חלון קובץ";
        var layout = CreatePipeline().Layout(text, Style(TextDirection.Rtl), 120f, null,
            TextOverflow.Clip, [Bold(text, "מאוד"), Bold(text, "חלון")]);

        Assert.True(layout.Lines.Count >= 3, $"{layout.Lines.Count} lines");
        int expectedStart = 0;
        foreach (var line in layout.Lines)
        {
            Assert.Equal(expectedStart, line.Start);
            expectedStart = line.Start + line.Length;
            Assert.All(line.Runs, r => Assert.True(r.IsRtl, $"'{r.Text}' at level {r.Level}"));
            // Reading right to left: each run starts left of the one before it.
            for (int i = 1; i < line.Runs.Count; i++)
                Assert.True(line.Runs[i].OffsetX < line.Runs[i - 1].OffsetX,
                    $"'{line.Runs[i].Text}' at {line.Runs[i].OffsetX} is not left of '{line.Runs[i - 1].Text}' at {line.Runs[i - 1].OffsetX}");
        }
    }

    [Fact]
    public void AnEllipsizedStyledRightToLeftLine_EndsWhereReadingEnds_InTheElementsOwnWeight()
    {
        const string text = "שלום עולם יפה מאוד היום ספר בית מחשב";
        var pipeline = CreatePipeline();
        var layout = pipeline.Layout(text, Style(TextDirection.Rtl), 110f, maxLines: 1,
            TextOverflow.Ellipsis, [Bold(text, "עולם")]);

        var line = Assert.Single(layout.Lines);
        var mark = line.Runs[^1];
        Assert.Equal("…", mark.Text);
        // Reading ends on the left in a right-to-left paragraph.
        Assert.Equal(0f, mark.OffsetX, 1);
        Assert.True(line.Width <= 110f + 0.5f, $"{line.Width}px in a 110px box");
        var regular = pipeline.Layout("…", Style(), 10_000, null).Lines[0].Runs[0];
        Assert.Same(regular.Font, mark.Font);
    }

    [Fact]
    public void AnRtlAndAnAutoLayoutOfTheSameStyledText_AreCachedApart()
    {
        // Latin text in an Rtl style resolves right to left; under Auto it is left to right.
        // Same text, spans and width: only the direction tells the two layouts apart.
        const string text = "plain words here";
        var spans = new[] { Bold(text, "words") };
        var pipeline = CreatePipeline();

        var rtl = pipeline.Layout(text, Style(TextDirection.Rtl), 10_000, null, TextOverflow.Clip, spans);
        var auto = pipeline.Layout(text, Style(TextDirection.Auto), 10_000, null, TextOverflow.Clip, spans);

        Assert.True(rtl.IsRtlParagraph);
        Assert.False(auto.IsRtlParagraph);
    }
}
