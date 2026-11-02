using HultUI.Core.Layout;
using HultUI.Core.Primitives;
using HultUI.Core.Rendering;
using HultUI.Core.Styling;
using HultUI.Text;
using HultUI.Text.Tests;
using Xunit;

namespace HultUI.Rendering.Skia.Tests;

/// <summary>
/// The real-font twin of HultUI.Text.Tests' RunsOnFirstReadTests: a line the pipeline breaks makes
/// its runs the first time they are read, and they are the runs the reference path
/// (<c>ComposeRunsWhenRead = false</c>) made with the line, to the bit — through HarfBuzz and the
/// system's fonts, with styled spans, letter spacing, fallback faces, ellipsis and line limits, read
/// straight away or after the word cache let every word go. Drawing a paragraph reads the lines the
/// clip shows and no others, and a line drawn is the pixels of a line made with its runs.
/// </summary>
public class RunsOnFirstReadTests
{
    private static readonly ResolvedInheritedStyle Base = new(
        FontFamily: "sans-serif", FontSize: 16f, FontWeight: FontWeight.Regular,
        TextColor: new(0, 0, 0, 255), TextDirection: TextDirection.Ltr, Cursor: CursorType.Default);

    private static readonly ResolvedInheritedStyle[] Styles =
    [
        Base,
        Base with { LetterSpacing = 0.6f },
        Base with { FontSize = 13f, FontWeight = FontWeight.Bold, LineHeight = 20f },
        Base with { FontFamily = FontFamilies.Monospace, FontSize = 12f },
        Base with { FontFamily = "serif", FontSize = 17.5f },
        Base with { TextDirection = TextDirection.Rtl },
    ];

    private const string Pangrams =
        "Pack my box with five dozen liquor jugs. How vexingly quick daft zebras jump! The five boxing wizards jump quickly.";

    private static readonly string[] Texts =
    [
        Pangrams,
        "AVA To Wa Ty Yo office affine flow fjord and the final office",
        "Item 4711 - 815 units, 3.5% of 100 and 12345 67890",
        "double  spaced   words  here   and there  ",
        "ʃɪp ðə ŋ ipa words beside Ελληνικό κείμενο εδώ and Привет, мир",
        "Mixed English — 東京 → ∞ and 100 € £ ¥ then more words",
        "中文文本测试，标点符号。还有更多的文字在这里。 and some latin words",
        "東京に行きます and then 大阪 for some more words",
        "first line of words\nsecond line is longer than the first one\n",
    ];

    private static readonly (int? MaxLines, TextOverflow Overflow)[] Modes =
    [
        (null, TextOverflow.Clip),
        (1, TextOverflow.Ellipsis),
        (2, TextOverflow.Ellipsis),
        (3, TextOverflow.Clip),
    ];

    [Fact]
    public void RunsReadLater_AreTheRunsMadeWithTheLine_ToTheBit()
    {
        var fonts = new SkiaFontCollection();
        int compared = 0, readLater = 0;
        foreach (bool flushFirst in new[] { false, true })
        {
            var reference = new TextPipeline(fonts, new SkiaTextShaper()) { ComposeRunsWhenRead = false };
            var lazy = new TextPipeline(fonts, new SkiaTextShaper());
            var laidOut = new List<(TextLayout Expected, TextLayout Actual, string Context)>();

            foreach (var style in Styles)
            foreach (var text in Texts)
            foreach (var spans in TestSpans.Variants(text).Prepend(null))
            foreach (var (maxLines, overflow) in Modes)
            for (float w = 30f; w < 900f; w += 61.5f)
                laidOut.Add((reference.Layout(text, style, w, maxLines, overflow, spans),
                    lazy.Layout(text, style, w, maxLines, overflow, spans),
                    $"\"{text}\" spans {spans?.Length ?? 0} w={w} lines={maxLines} {overflow} {style}"));

            // Every word of the corpus out of the word cache (8,192 entries) before a line is read.
            if (flushFirst)
                for (int i = 0; i < 2 * 8192; i++)
                    lazy.Layout($"w{i:x5}", Base, float.PositiveInfinity, null);

            foreach (var (expected, actual, context) in laidOut)
            {
                readLater += actual.Lines.Count(line => !line.RunsMade);
                LayoutAssert.Same(expected, actual, (flushFirst ? "after a flush: " : "") + context);
                compared++;
            }
        }
        Assert.True(compared > 5_000, $"compared only {compared} layouts");
        Assert.True(readLater > 5_000, $"only {readLater} lines made their runs when read");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DrawingAParagraph_ReadsTheLinesTheClipShows_AndNoOthers(bool styled)
    {
        using var text = new SkiaTextServices();
        using var surface = new SkiaRasterRenderSurface(text);
        surface.Resize(300, 200, 1f);
        var ctx = (SkiaRenderContext)surface.RenderContext;

        string words = string.Concat(Enumerable.Repeat(Pangrams + " ", 16)).TrimEnd();
        TextSpan[]? spans = styled ? [new TextSpan(5, 12, Color.Black, Weight: FontWeight.Bold)] : null;
        var paragraph = (PipelineTextParagraph)ctx.LayoutParagraph(words, Base, 280f, spans);
        var lines = paragraph.TextLines;
        Assert.True(lines.Count > 30, $"{lines.Count} lines");
        Assert.All(lines, line => Assert.False(line.RunsMade, "a line made its runs when it was laid out"));

        float lineHeight = lines[1].OffsetY;
        ctx.BeginFrame(300, 200, 1f, Color.White);
        ctx.PushClip(new Rect(0, 0, 300, 3 * lineHeight));
        ctx.DrawParagraph(paragraph, new Point(0, -10 * lineHeight));
        ctx.EndFrame();

        // Lines 10 to 12 are in the clip; a line's height of slack each way covers what hangs out
        // of its box. The rest of the paragraph has made nothing.
        int read = lines.Count(line => line.RunsMade);
        Assert.InRange(read, 3, 7);
        Assert.True(lines[10].RunsMade && lines[12].RunsMade, "a line in the clip was not read");
    }

    /// <summary>A paragraph drawn with runs made when read and with runs made with the line: the
    /// same pixels, the same frame.</summary>
    [Theory]
    [InlineData(TextOverflow.Clip, null)]
    [InlineData(TextOverflow.Ellipsis, 2)]
    [InlineData(TextOverflow.Ellipsis, 1)]
    public void ALineDrawn_IsThePixelsOfALineMadeWithItsRuns(TextOverflow overflow, int? maxLines)
    {
        using var text = new SkiaTextServices();
        using var surface = new SkiaRasterRenderSurface(text);
        surface.Resize(360, 200, 1f);
        var ctx = (SkiaRenderContext)surface.RenderContext;
        var reference = new TextPipeline(text.Fonts, text.Shaper) { ComposeRunsWhenRead = false };
        TextSpan[] spans = [new TextSpan(5, 12, Color.Black, Weight: FontWeight.Bold), new TextSpan(40, 8, Color.Black, FontSize: 22f)];

        byte[] Draw(TextParagraph paragraph)
        {
            ctx.BeginFrame(360, 200, 1f, Color.White);
            ctx.DrawParagraph(paragraph, new Point(8, 8));
            ctx.EndFrame();
            return surface.ReadPixels(out _, out _, out _);
        }

        foreach (var used in new[] { null, spans })
        {
            byte[] made = Draw(reference.LayoutParagraph(Pangrams, Base, 340f, used, maxLines, overflow));
            byte[] read = Draw(ctx.LayoutParagraph(Pangrams, Base, 340f, used, maxLines, overflow));
            Assert.Equal(made, read);
        }
    }
}
