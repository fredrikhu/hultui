using HultUI.Core.Layout;
using HultUI.Core.Primitives;
using HultUI.Core.Rendering;
using HultUI.Core.Styling;
using Xunit;

namespace HultUI.Text.Tests;

/// <summary>
/// A box too narrow for even one character wraps one character per line — one code point,
/// so a surrogate pair (an emoji) is its own line rather than being split or skipped. The
/// floor used to step back off a pair, give an empty line and stay where it was: with no line
/// limit, wrapping never finished. These use a limit, so the old behavior fails here (one line
/// then empty ones) instead of hanging the run.
/// </summary>
public class NarrowWrapTests
{
    private static readonly ResolvedInheritedStyle Style = new(
        FontFamily: "TestFont", FontSize: 16f, FontWeight: FontWeight.Regular,
        TextColor: new(0, 0, 0, 255), TextDirection: TextDirection.Ltr, Cursor: CursorType.Default);

    private const string Text = "a\U0001F600b";   // the emoji is above U+0590: the BiDi path

    private static string[] Drawn(TextLayout layout) =>
        layout.Lines.Select(l => string.Concat(l.Runs.Select(r => r.Text))).ToArray();

    [Fact]
    public void BidiPath_ASurrogatePairWiderThanTheBox_IsALineOfItsOwn()
    {
        var pipeline = new TextPipeline(new StubFontCollection(10f), new StubTextShaper());

        var layout = pipeline.Layout(Text, Style, 5f, maxLines: 50);

        Assert.Equal(["a", "\U0001F600", "b"], Drawn(layout));
        Assert.Equal([0, 1, 3], layout.Lines.Select(l => l.Start));
    }

    [Fact]
    public void StyledPath_ASurrogatePairWiderThanTheBox_IsALineOfItsOwn()
    {
        var pipeline = new TextPipeline(new StubFontCollection(10f), new StubTextShaper());
        var bold = new[] { new TextSpan(0, 1, Color.Black, Weight: FontWeight.Bold) };

        var layout = pipeline.Layout(Text, Style, 5f, maxLines: 50, TextOverflow.Clip, bold);

        Assert.Equal(["a", "\U0001F600", "b"], Drawn(layout));
        Assert.Equal([0, 1, 3], layout.Lines.Select(l => l.Start));
    }
}
