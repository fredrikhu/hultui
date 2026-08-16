using HultUI.Core.Layout;
using HultUI.Core.Primitives;
using HultUI.Core.Rendering;
using HultUI.Core.Styling;
using HultUI.Text;
using Xunit;

namespace HultUI.Rendering.Skia.Tests;

/// <summary>
/// The two refinements the rich-text and BiDi plans left open: text that carries
/// metric-affecting spans, or right-to-left script, used to CLIP under
/// <see cref="TextOverflow.Ellipsis"/> rather than ellipsize — the text simply
/// stopped, with no mark to say it had been cut. And min-content width answered
/// with the widest *character* rather than the widest word.
/// </summary>
public class TextTruncationTests
{
    private static ResolvedInheritedStyle Style(TextDirection direction = TextDirection.Ltr) => new(
        FontFamily: "sans-serif", FontSize: 16f, FontWeight: FontWeight.Regular,
        TextColor: Color.Black, TextDirection: direction, Cursor: CursorType.Default);

    private static TextPipeline Pipeline() => new(new SkiaFontCollection(), new SkiaTextShaper());

    private static string DrawnText(TextLine line)
        => string.Concat(line.Runs.Select(r => r.Text));

    // ── Ellipsis on styled text ──

    [Fact]
    public void StyledTextTooLongToFit_EndsWithAnEllipsis()
    {
        var pipeline = Pipeline();
        const string text = "the quick brown fox jumps over the lazy dog";
        var spans = new[] { new TextSpan(4, 5, Color.Black, Weight: FontWeight.Bold) };

        var layout = pipeline.Layout(text, Style(), 120f, maxLines: 1, TextOverflow.Ellipsis, spans);

        var line = Assert.Single(layout.Lines);
        Assert.EndsWith("…", DrawnText(line));
        Assert.True(line.Width <= 120f + 0.5f, $"ellipsized line is {line.Width}px wide in a 120px box");
    }

    [Fact]
    public void AStyledEllipsizedLine_KeepsTheBoldRunItStillShows()
    {
        var pipeline = Pipeline();
        const string text = "the quick brown fox jumps over the lazy dog";
        var spans = new[] { new TextSpan(4, 5, Color.Black, Weight: FontWeight.Bold) };

        var layout = pipeline.Layout(text, Style(), 120f, maxLines: 1, TextOverflow.Ellipsis, spans);
        var line = layout.Lines[0];

        // "quick" is inside the visible prefix, so the line is still split at the
        // span's boundaries — truncation must not flatten the styling.
        Assert.True(line.Runs.Count >= 3,
            $"expected the bold span to keep its own run, got [{string.Join(" | ", line.Runs.Select(r => r.Text))}]");
    }

    [Fact]
    public void StyledTextThatFits_IsLeftAlone()
    {
        var pipeline = Pipeline();
        const string text = "short";
        var spans = new[] { new TextSpan(0, 5, Color.Black, Weight: FontWeight.Bold) };

        var layout = pipeline.Layout(text, Style(), 1000f, maxLines: 1, TextOverflow.Ellipsis, spans);
        Assert.DoesNotContain("…", DrawnText(layout.Lines[0]));
    }

    // ── Ellipsis on bidi text ──

    [Fact]
    public void RightToLeftTextTooLongToFit_EndsWithAnEllipsis()
    {
        var pipeline = Pipeline();
        const string text = "שלום עולם אבג דהו זחט וכל השאר";

        var layout = pipeline.Layout(text, Style(TextDirection.Rtl), 100f, maxLines: 1, TextOverflow.Ellipsis);

        var line = Assert.Single(layout.Lines);
        Assert.Contains("…", DrawnText(line));
    }

    [Fact]
    public void InARightToLeftParagraph_TheEllipsisSitsAtTheLeftEdge()
    {
        // Reading ends on the left, so that is where the truncation mark belongs.
        var pipeline = Pipeline();
        const string text = "שלום עולם אבג דהו זחט וכל השאר";

        var layout = pipeline.Layout(text, Style(TextDirection.Rtl), 100f, maxLines: 1, TextOverflow.Ellipsis);
        var line = layout.Lines[0];

        var ellipsis = line.Runs.Single(r => r.Text == "…");
        Assert.Equal(0f, ellipsis.OffsetX, 1);
        foreach (var run in line.Runs.Where(r => r.Text != "…"))
            Assert.True(run.OffsetX >= ellipsis.Width - 0.5f,
                $"'{run.Text}' at {run.OffsetX} should sit right of the ellipsis");
    }

    [Fact]
    public void InALeftToRightParagraphWithHebrewInIt_TheEllipsisSitsAtTheRightEdge()
    {
        var pipeline = Pipeline();
        const string text = "commit שלום merged by אבג into main branch today";

        var layout = pipeline.Layout(text, Style(), 120f, maxLines: 1, TextOverflow.Ellipsis);
        var line = layout.Lines[0];

        var ellipsis = line.Runs.Single(r => r.Text == "…");
        foreach (var run in line.Runs.Where(r => r.Text != "…"))
            Assert.True(run.OffsetX <= ellipsis.OffsetX + 0.5f,
                $"'{run.Text}' at {run.OffsetX} should sit left of the ellipsis at {ellipsis.OffsetX}");
    }

    // ── Min-content width ──

    [Fact]
    public void MinContentWidth_IsTheWidestWord_NotTheWidestCharacter()
    {
        var pipeline = Pipeline();
        const string text = "a wonderfully long word";
        var style = Style();

        float minContent = pipeline.MinContentWidth(text, style);
        float longestWord = pipeline.Layout("wonderfully", style, 10_000, null).Size.Width;
        float widestChar = pipeline.Layout("w", style, 10_000, null).Size.Width;

        Assert.Equal(longestWord, minContent, 1);
        Assert.True(minContent > widestChar * 3, "the old answer was one character wide");
    }

    [Fact]
    public void MinContentWidth_IgnoresTheSpaceAtABreak()
    {
        // "wonderfully " is a segment including its trailing space; the line ends
        // at the break, so the space must not widen the floor.
        var pipeline = Pipeline();
        var style = Style();
        Assert.Equal(
            pipeline.MinContentWidth("wonderfully more", style),
            pipeline.MinContentWidth("wonderfully", style), 1);
    }

    [Fact]
    public void MinContentWidth_OfAnUnbreakableToken_IsTheWholeToken()
    {
        var pipeline = Pipeline();
        var style = Style();
        const string token = "supercalifragilisticexpialidocious";

        // Nowhere to break, so nothing narrower than the whole thing holds it.
        Assert.Equal(pipeline.Layout(token, style, 10_000, null).Size.Width,
            pipeline.MinContentWidth(token, style), 1);
    }

    [Fact]
    public void MinContentWidth_OfAPath_IsItsWidestSegment_NotTheWholePath()
    {
        // UAX #14 allows a break after a solidus, so a long path is not one
        // unbreakable token — it wraps at its separators, and the width below which
        // it genuinely breaks is that of its widest component. Worth pinning
        // because "a path has no break opportunities" is the natural assumption.
        var pipeline = Pipeline();
        var style = Style();
        const string path = "C:/very/long/path/without/spaces/in/it.txt";

        float full = pipeline.Layout(path, style, 10_000, null).Size.Width;
        float minContent = pipeline.MinContentWidth(path, style);

        Assert.True(minContent < full / 2, $"min-content {minContent} vs full {full}");
        Assert.Equal(pipeline.MinContentWidth("without/", style), minContent, 1);
    }

    [Fact]
    public void MinContentWidth_TakesTheWidestLineOfMultilineText()
    {
        var pipeline = Pipeline();
        var style = Style();
        Assert.Equal(
            pipeline.MinContentWidth("wonderfully", style),
            pipeline.MinContentWidth("ab\nwonderfully\ncd", style), 1);
    }

    [Fact]
    public void MinContentWidth_AccountsForABoldSpanWideningItsWord()
    {
        var pipeline = Pipeline();
        var style = Style();
        const string text = "a wonderfully long word";
        var spans = new[] { new TextSpan(2, 11, Color.Black, Weight: FontWeight.Bold) };

        Assert.True(pipeline.MinContentWidth(text, style, spans) > pipeline.MinContentWidth(text, style),
            "a bold word is wider than the same word in regular weight");
    }

    [Fact]
    public void MinContentWidth_OfEmptyText_IsZero()
        => Assert.Equal(0f, Pipeline().MinContentWidth("", Style()));
}
