using HultUI.Core.Layout;
using HultUI.Core.Primitives;
using HultUI.Core.Rendering;
using HultUI.Core.Styling;
using HultUI.Text;
using Xunit;

namespace HultUI.Rendering.Skia.Tests;

/// <summary>
/// Stage 1 of docs/plans/16-rich-text-editor.md: a span can change the font family, and a
/// laid-out <see cref="TextParagraph"/> answers caret, hit-test and selection questions from the
/// styled runs it is drawn from — not from prefixes measured in the element's style.
/// </summary>
public class TextParagraphTests
{
    private static readonly ResolvedInheritedStyle Style = new(
        FontFamily: "sans-serif", FontSize: 16f, FontWeight: FontWeight.Regular,
        TextColor: new(0, 0, 0, 255), TextDirection: TextDirection.Ltr, Cursor: CursorType.Default);

    private static TextPipeline CreatePipeline() =>
        new(new SkiaFontCollection(), new SkiaTextShaper());

    // ── Font family on a span ──

    [Fact]
    public void AFamilySpan_ShapesItsRangeInThatFace()
    {
        // A monospace 'i' is as wide as an 'm'; a proportional one is a sliver.
        var pipeline = CreatePipeline();
        const string text = "say iiiiiiii now";
        var plain = pipeline.Layout(text, Style, 10_000, null);
        var mono = pipeline.Layout(text, Style, 10_000, null, TextOverflow.Clip,
            [new TextSpan(4, 8, default, FontFamily: "monospace")]);

        Assert.True(mono.Size.Width > plain.Size.Width + 8f,
            $"monospace i's ({mono.Size.Width}) should be much wider than sans ones ({plain.Size.Width})");
    }

    [Fact]
    public void AFamilySpan_IsMetricAffecting_AndKeysTheCache()
    {
        Assert.True(new TextSpan(0, 1, default, FontFamily: "monospace").IsMetricAffecting);

        var pipeline = CreatePipeline();
        const string text = "iiii iiii";
        var mono = pipeline.Layout(text, Style, 10_000, null, TextOverflow.Clip,
            [new TextSpan(0, 4, default, FontFamily: "monospace")]);
        var serif = pipeline.Layout(text, Style, 10_000, null, TextOverflow.Clip,
            [new TextSpan(0, 4, default, FontFamily: "serif")]);

        Assert.NotSame(mono, serif);
    }

    // ── The paragraph handle ──

    [Fact]
    public void Caret_AfterABoldWord_SitsWhereTheBoldWordEnds()
    {
        var pipeline = CreatePipeline();
        const string text = "plain BOLDWORD tail";
        var spans = new[] { new TextSpan(6, 8, default, Weight: FontWeight.Bold) };
        var p = pipeline.LayoutParagraph(text, Style, 10_000, spans);

        float styled = p.CaretX(0, 14);
        float elementStyle = pipeline.Layout(text[..14], Style, float.PositiveInfinity, 1).Size.Width;
        float bold = pipeline.Layout(text[..14], Style, float.PositiveInfinity, 1, TextOverflow.Clip, spans).Size.Width;

        Assert.True(styled > elementStyle + 0.5f,
            $"caret {styled} must clear the bold word, not the regular-weight measure {elementStyle}");
        Assert.Equal(bold, styled, 2);
    }

    [Fact]
    public void OffsetAt_IsTheInverseOfCaretX_AcrossStyledRuns()
    {
        var pipeline = CreatePipeline();
        const string text = "small LARGE mono end";
        var spans = new[]
        {
            new TextSpan(6, 5, default, FontSize: 28f),
            new TextSpan(12, 4, default, FontFamily: "monospace"),
        };
        var p = pipeline.LayoutParagraph(text, Style, 10_000, spans);

        for (int offset = 0; offset <= text.Length; offset++)
        {
            float x = p.CaretX(0, offset);
            Assert.Equal(offset, p.OffsetAt(x + 0.1f, p.Lines[0].Height / 2));
        }
    }

    [Fact]
    public void EmptyText_HasOneLineAsTallAsTheFont()
    {
        var pipeline = CreatePipeline();
        var p = pipeline.LayoutParagraph("", Style, 200, null);
        var one = pipeline.LayoutParagraph("x", Style, 200, null);

        var line = Assert.Single(p.Lines);
        Assert.Equal(0, line.Start);
        Assert.Equal(one.Lines[0].Height, line.Height, 3);
        Assert.Equal(one.Size.Height, p.Size.Height, 3);
        Assert.Equal(0f, p.CaretBox(0).X);
    }

    [Fact]
    public void AFinalNewline_IsFollowedByAnEmptyLine_ThatTheEndOffsetSitsOn()
    {
        var pipeline = CreatePipeline();
        var p = pipeline.LayoutParagraph("one\n", Style, 200, null);

        Assert.Equal(2, p.Lines.Count);
        Assert.Equal(4, p.Lines[1].Start);
        Assert.Equal(1, p.LineOf(4));
        Assert.Equal(0, p.LineOf(3));          // before the newline: end of the first line
        Assert.Equal(p.Lines[0].Height, p.Lines[1].OffsetY, 3);
        Assert.Equal(p.Lines[1].OffsetY + p.Lines[1].Height, p.Size.Height, 3);
    }

    [Fact]
    public void AtAWrap_TheCaretGoesDownstream_UnlessAskedForTheEndOfTheLine()
    {
        var pipeline = CreatePipeline();
        const string text = "alpha beta gamma delta";
        float width = pipeline.Layout("alpha beta ", Style, float.PositiveInfinity, 1).Size.Width + 2f;
        var p = pipeline.LayoutParagraph(text, Style, width, null);
        Assert.True(p.Lines.Count >= 2);

        int wrap = p.Lines[1].Start;
        Assert.Equal(1, p.LineOf(wrap));
        Assert.Equal(0, p.LineOf(wrap, upstream: true));
        Assert.Equal(p.Lines[1].OffsetY, p.CaretBox(wrap).Y);
        Assert.Equal(0f, p.CaretBox(wrap).X);
    }

    [Fact]
    public void Centered_LinesStartHalfTheirSpareWidthIn()
    {
        var pipeline = CreatePipeline();
        var centered = Style with { TextAlign = TextAlign.Center };
        var p = pipeline.LayoutParagraph("mid", centered, 300, null);

        float expected = (300 - p.Lines[0].Width) / 2;
        Assert.Equal(expected, p.LineLeft(0), 2);
        Assert.Equal(expected, p.CaretX(0, 0), 2);
    }

    [Fact]
    public void ASelectionOverAWrap_CoversBothLines_AndTheBreakBetweenThem()
    {
        var pipeline = CreatePipeline();
        const string text = "alpha beta gamma delta";
        float width = pipeline.Layout("alpha beta ", Style, float.PositiveInfinity, 1).Size.Width + 2f;
        var p = pipeline.LayoutParagraph(text, Style, width, null);

        var boxes = new List<Rect>();
        p.SelectionBoxes(2, p.Lines[1].Start + 3, boxes, lineEndWidth: 5f);

        // The first line's text runs on over its break as one box; then the second line's text.
        Assert.Equal(2, boxes.Count);
        Assert.Equal(p.CaretX(0, 2), boxes[0].X, 2);
        Assert.Equal(p.Lines[0].Width + 5f, boxes[0].Right, 2);
        Assert.Equal(p.Lines[1].OffsetY, boxes[1].Y, 2);
        Assert.Equal(0f, boxes[1].X, 2);
    }

    [Fact]
    public void ASelectionOverAnEmptyLine_PaintsItsBreak()
    {
        var pipeline = CreatePipeline();
        var p = pipeline.LayoutParagraph("one\n\ntwo", Style, 200, null);
        Assert.Equal(3, p.Lines.Count);

        var boxes = new List<Rect>();
        p.SelectionBoxes(0, 8, boxes, lineEndWidth: 5f);

        Assert.Contains(boxes, b => b.Y == p.Lines[1].OffsetY && b.X == 0f && b.Width == 5f);
    }

    [Fact]
    public void TheDefault_FromLines_AnswersWithoutAPipeline()
    {
        // The fallback a recorder or a test double gets: lines from LayoutTextLines, carets from
        // the one-line methods. Here those are the real Skia ones, so the answers must agree.
        var services = new SkiaTextServices();
        using var ctx = new SkiaRenderContext();
        ctx.SetPipeline(services.Pipeline);

        const string text = "one two three\nfour";
        var real = ctx.LayoutParagraph(text, Style, 60, null);
        var fallback = TextParagraph.FromLines(ctx, text, Style, 60, null);

        Assert.Equal(real.Lines.Count, fallback.Lines.Count);
        for (int i = 0; i < real.Lines.Count; i++)
        {
            Assert.Equal(real.Lines[i].Start, fallback.Lines[i].Start);
            Assert.Equal(real.Lines[i].Length, fallback.Lines[i].Length);
        }
        for (int offset = 0; offset <= text.Length; offset++)
            Assert.Equal(real.CaretBox(offset).X, fallback.CaretBox(offset).X, 2);
    }
}
