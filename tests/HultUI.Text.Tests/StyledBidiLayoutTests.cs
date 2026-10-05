using HultUI.Core.Layout;
using HultUI.Core.Primitives;
using HultUI.Core.Rendering;
using HultUI.Core.Styling;
using Xunit;

namespace HultUI.Text.Tests;

/// <summary>
/// The geometry of a right-to-left line with a metric span, to the pixel, with a stub font
/// whose every character is 10 px wide: a direction run is cut at the span's edges and the
/// pieces placed right to left, the first logical piece rightmost.
/// </summary>
public class StyledBidiLayoutTests
{
    private static ResolvedInheritedStyle Style(TextDirection direction) => new(
        FontFamily: "TestFont", FontSize: 16f, FontWeight: FontWeight.Regular,
        TextColor: new(0, 0, 0, 255), TextDirection: direction, Cursor: CursorType.Default);

    private static TextPipeline CreatePipeline() => new(new StubFontCollection(10f), new StubTextShaper());

    [Theory]
    [InlineData(TextDirection.Rtl)]
    [InlineData(TextDirection.Auto)]
    public void ARightToLeftRunCutByASpan_PlacesItsPiecesRightToLeft(TextDirection direction)
    {
        const string text = "אב גד";   // one right-to-left run: the space between is level 1 too
        var bold = new TextSpan(3, 2, Color.Black, Weight: FontWeight.Bold);

        var layout = CreatePipeline().Layout(text, Style(direction), 1_000f, null, TextOverflow.Clip, [bold]);

        Assert.True(layout.IsRtlParagraph);
        var line = Assert.Single(layout.Lines);
        Assert.Equal((0, 5), (line.Start, line.Length));
        Assert.Equal(["אב ", "גד"], line.Runs.Select(r => r.Text));      // logical order
        Assert.All(line.Runs, r => Assert.Equal(1, r.Level));
        Assert.Equal(20f, line.Runs[0].OffsetX);   // "אב " on the right…
        Assert.Equal(0f, line.Runs[1].OffsetX);    // …"גד" read after it, on the left
        Assert.Equal(50f, line.Width);
        Assert.Equal(new Size(50f, line.Height), layout.Size);
    }

    [Fact]
    public void ALeftToRightParagraph_KeepsTheRightToLeftPiecesInsideIt()
    {
        const string text = "ab אב גד cd";
        var bold = new TextSpan(6, 2, Color.Black, Weight: FontWeight.Bold);   // "גד"

        var line = CreatePipeline().Layout(text, Style(TextDirection.Ltr), 1_000f, null,
            TextOverflow.Clip, [bold]).Lines[0];

        Assert.Equal(["ab ", "אב ", "גד", " cd"], line.Runs.Select(r => r.Text));
        Assert.Equal([0, 1, 1, 0], line.Runs.Select(r => (int)r.Level));
        // ab | גד אב | cd: the Hebrew between the Latin, read right to left within it.
        Assert.Equal([0f, 50f, 30f, 80f], line.Runs.Select(r => r.OffsetX));
    }
}
