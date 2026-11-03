using HultUI.Core.Layout;
using HultUI.Core.Primitives;
using HultUI.Core.Rendering;
using HultUI.Core.Styling;
using HultUI.Testing;
using Xunit;
using Xunit.Abstractions;

namespace HultUI.Text.Tests;

/// <summary>
/// The line breakers — plain, styled and direction-aware — fill one list the pipeline keeps, and
/// each layout gets an exact array of its lines; a truncated layout has its last line replaced in
/// that list, with no layout made of the lines before the ellipsis. These hold every layout to
/// lines of its own that no later layout changes, however the pipeline is used in between, and a
/// truncated label to what its own line and layout cost.
/// </summary>
public class LayoutLinesTests(ITestOutputHelper output)
{
    private static readonly ResolvedInheritedStyle Base = new(
        FontFamily: "TestFont", FontSize: 16f, FontWeight: FontWeight.Regular,
        TextColor: new(0, 0, 0, 255), TextDirection: TextDirection.Ltr, Cursor: CursorType.Default);

    private static readonly string[] Words =
        ["pretty", "red", "table", "chair", "office", "AV", "To", "fifty", "4711", "keyboard", "house", "Wa"];

    private static string Paragraph(int words, int seed = 0) =>
        string.Join(' ', Enumerable.Range(0, words).Select(i => Words[(i * 7 + seed) % Words.Length]));

    private static TextPipeline Pipeline() =>
        new(new WidthChangeEquivalenceTests.TwoFonts(negativeDescent: false), new WidthChangeEquivalenceTests.ContextShaper());

    // The four ways a label is truncated: the plain path (one face), a measured line (a Greek word
    // the fallback face has), the styled path (a bold span) and the direction-aware path (Hebrew).
    public static TheoryData<string> Paths => new() { "plain", "multi-font", "styled", "bidi" };

    private static string Text(string path, int words, int seed) => path switch
    {
        "multi-font" => $"{Paragraph(words / 2, seed)} αβγδ {Paragraph(words / 2, seed + 1)} {seed}",
        "bidi" => $"{Paragraph(words / 2, seed)} שלום עולם {Paragraph(words / 2, seed + 1)} {seed}",
        _ => $"{Paragraph(words, seed)} {seed}",
    };

    private static TextSpan[]? Spans(string path, string text) =>
        path == "styled" ? [new TextSpan(3, Math.Min(12, text.Length - 3), Color.Black, Weight: FontWeight.Bold)] : null;

    private static TextLayout Lay(TextPipeline pipeline, string path, string text, float width, int? maxLines,
        TextOverflow overflow) =>
        pipeline.Layout(text, Base, width, maxLines, overflow, Spans(path, text));

    [Theory]
    [MemberData(nameof(Paths))]
    public void EachLayout_HasAnExactArrayOfItsLines_ThatLaterLayoutsLeaveAlone(string path)
    {
        var pipeline = Pipeline();
        var kept = new List<(TextLayout Layout, TextLine[] Lines)>();
        for (int i = 0; i < 40; i++)
        {
            string text = Text(path, 10 + i * 3, i);
            foreach (var (maxLines, overflow) in new (int?, TextOverflow)[]
                     { (null, TextOverflow.Clip), (2, TextOverflow.Ellipsis), (3, TextOverflow.Clip), (null, TextOverflow.Ellipsis) })
            {
                var layout = Lay(pipeline, path, text, 150f + i, maxLines, overflow);
                Assert.True(layout.Lines.Count >= 2, $"{layout.Lines.Count} lines of \"{text}\"");
                var lines = Assert.IsType<TextLine[]>(layout.Lines);
                Assert.Equal(layout.Lines.Count, lines.Length);
                if (maxLines is { } most) Assert.True(lines.Length <= most);
                kept.Add((layout, lines.ToArray()));
            }
        }

        // Every layout still has the lines it was made with, after all the layouts made since.
        foreach (var (layout, lines) in kept)
        {
            Assert.Equal(lines.Length, layout.Lines.Count);
            for (int i = 0; i < lines.Length; i++) Assert.Same(lines[i], layout.Lines[i]);
        }
    }

    /// <summary>A truncated paragraph's last line is the ellipsized one, in the place of the line
    /// it replaces, and the layout's width is its widest line's.</summary>
    [Theory]
    [MemberData(nameof(Paths))]
    public void ATruncatedParagraph_HasItsLastLineReplacedInPlace(string path)
    {
        var pipeline = Pipeline();
        string text = Text(path, 60, 3);
        var clipped = Lay(pipeline, path, text, 180f, 3, TextOverflow.Clip);
        var truncated = Lay(pipeline, path, text, 180f, 3, TextOverflow.Ellipsis);
        Assert.Equal(3, truncated.Lines.Count);
        for (int i = 0; i < 2; i++)
        {
            Assert.Equal(clipped.Lines[i].Start, truncated.Lines[i].Start);
            Assert.Equal(clipped.Lines[i].Width, truncated.Lines[i].Width);
        }
        Assert.Equal(clipped.Lines[2].OffsetY, truncated.Lines[2].OffsetY);
        Assert.Equal(clipped.Lines[2].Start, truncated.Lines[2].Start);
        Assert.Contains("…", string.Concat(truncated.Lines[2].Runs.Select(r => r.Text)));
        Assert.DoesNotContain("…", string.Concat(clipped.Lines[2].Runs.Select(r => r.Text)));
        Assert.Equal(clipped.Size.Height, truncated.Size.Height);
        Assert.Equal(truncated.Lines.Max(l => l.Width), truncated.Size.Width);
    }

    /// <summary>
    /// What a truncated label costs when it is laid out at a new width, as a resize drag does: its
    /// line and its layout, with an exact array of the one line. A list for each layout, kept by
    /// it at the default capacity, and a second list and layout to replace the last line were up
    /// to 150 B a label (plan 17, round 7). Bounds 24 B over what each path allocates now (160,
    /// 224, 248 and 184 B); the plain path's one-line shortcut never made either.
    /// </summary>
    [Theory]
    [InlineData("plain", 184)]
    [InlineData("multi-font", 248)]
    [InlineData("styled", 272)]
    [InlineData("bidi", 208)]
    public void ATruncatedLabel_CostsItsLineAndOneLayout(string path, int most)
    {
        var pipeline = Pipeline();
        var labels = Enumerable.Range(0, 300).Select(i => Text(path, 12, i)).ToArray();
        foreach (var label in labels) Lay(pipeline, path, label, 120f, 1, TextOverflow.Ellipsis);   // their words, their entries
        float width = 120f;
        long bytes = Allocations.Least(() =>
        {
            width += 0.001f;
            foreach (var label in labels) Lay(pipeline, path, label, width, 1, TextOverflow.Ellipsis);
        });
        output.WriteLine($"{path}: {bytes / labels.Length} B a label");
        Assert.True(bytes / labels.Length <= most, $"{path}: {bytes / labels.Length} B a label");
    }
}
