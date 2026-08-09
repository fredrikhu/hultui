using System.Collections.Generic;
using HultUI.Core.Builders;
using HultUI.Core.Elements;
using HultUI.Core.Layout;
using HultUI.Core.Primitives;
using HultUI.Core.Rendering;
using HultUI.Core.Styling;
using Xunit;

namespace HultUI.Core.Tests.Elements;

/// <summary>
/// A label has to be measured under the overflow mode it will be painted with. Clip and
/// Ellipsis truncate overlong text differently, so measuring under one and drawing under
/// the other reports a size the element never occupies — and, worse, couples every
/// ellipsized label to changes in the clipping path. That coupling is not hypothetical: a
/// fix to how a single clipped line is cut once resized Junction's repository cards, which
/// ellipsize and never clip.
/// </summary>
public class TextMeasureOverflowTests
{
    [Theory]
    [InlineData(TextOverflow.Clip)]
    [InlineData(TextOverflow.Ellipsis)]
    public void ALabelIsMeasuredWithItsOwnOverflowMode(TextOverflow overflow)
    {
        var ctx = new RecordingMeasureContext();
        var engine = new LayoutEngine(ctx);

        var label = Text.New("a string wider than the box it is given").TextOverflow(overflow).MaxLines(1);
        var root = Flex.Column(gap: 0).Children(label);
        Frame(engine, root);

        Assert.Contains(ctx.Calls, c => c.Text.StartsWith("a string") && c.Overflow == overflow);
        Assert.DoesNotContain(ctx.Calls, c => c.Text.StartsWith("a string") && c.Overflow != overflow);
    }

    [Fact]
    public void TheDefaultIsClipBecauseThatIsWhatDrawingDefaultsTo()
    {
        var ctx = new RecordingMeasureContext();
        var engine = new LayoutEngine(ctx);

        var label = Text.New("plain");   // no explicit overflow
        Frame(engine, Flex.Column(gap: 0).Children(label));

        Assert.Equal(TextOverflow.Clip, label.Overflow);
        Assert.Contains(ctx.Calls, c => c.Text == "plain" && c.Overflow == TextOverflow.Clip);
    }

    private static void Frame(LayoutEngine engine, Element root)
    {
        StyleResolver.Resolve(root, new Theme());
        engine.PerformLayout(root, 200, 200);
    }

    private sealed class RecordingMeasureContext : IMeasureContext
    {
        private readonly StubMeasureContext _inner = new();
        public readonly List<(string Text, TextOverflow Overflow)> Calls = new();

        public Size MeasureText(string text, ResolvedInheritedStyle style, float maxWidth, int? maxLines,
            TextOverflow overflow = TextOverflow.Clip)
        {
            Calls.Add((text, overflow));
            return _inner.MeasureText(text, style, maxWidth, maxLines, overflow);
        }
    }
}
