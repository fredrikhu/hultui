using HultUI.Core.Builders;
using HultUI.Core.Elements;
using HultUI.Core.Layout;
using HultUI.Core.Primitives;
using HultUI.Core.Rendering;
using HultUI.Core.Styling;
using Xunit;

namespace HultUI.Core.Tests.Layout;

/// <summary>
/// How often layout measures a label. Text is measured at a width, so the measure pass (which
/// offers it unbounded height) and the layout pass (which offers, or forces, its line's height)
/// should share one measurement per width — they used to make one each, and a leaf laid out in
/// Phase 6 measured itself a third time: 11 text measurements per row of a resizing list where 4
/// are needed. The results must not change: these pin both the count and what the cache may not
/// share.
/// </summary>
public class LeafMeasureCacheTests
{
    /// <summary>Measures like the stub, scaled by font size, and counts the calls. With
    /// <paramref name="saysWhereItHolds"/> it says, as the real pipeline does, that one line that
    /// fits is the same at any width it fits in.</summary>
    private sealed class CountingMeasureContext(bool saysWhereItHolds = false) : IMeasureContext
    {
        public int Calls;

        public Size MeasureText(string text, ResolvedInheritedStyle style, float maxWidth, int? maxLines,
            TextOverflow overflow, IReadOnlyList<TextSpan>? spans, out float sameFromWidth)
        {
            var size = MeasureText(text, style, maxWidth, maxLines, overflow);
            sameFromWidth = saysWhereItHolds && size.Width <= maxWidth && size.Height <= LineHeightOf(style)
                ? size.Width
                : float.NaN;
            return size;
        }

        private static float LineHeightOf(ResolvedInheritedStyle style) =>
            (style.FontSize > 0 ? style.FontSize : 14f) * 1.2f;

        public Size MeasureText(string text, ResolvedInheritedStyle style, float maxWidth, int? maxLines,
            TextOverflow overflow = TextOverflow.Clip)
        {
            Calls++;
            if (string.IsNullOrEmpty(text)) return Size.Zero;
            float size = style.FontSize > 0 ? style.FontSize : 14f;
            float charWidth = size / 2, lineHeight = size * 1.2f;
            float total = text.Length * charWidth;
            if (total <= maxWidth) return new Size(total, lineHeight);
            int perLine = Math.Max(1, (int)(maxWidth / charWidth));
            int lines = (int)Math.Ceiling((double)text.Length / perLine);
            if (maxLines is { } max) lines = Math.Min(lines, max);
            return new Size(Math.Min(total, maxWidth), lines * lineHeight);
        }
    }

    /// <summary>The js-framework-benchmark row: a fixed-width id, a growing label, a fixed glyph.</summary>
    private static Element Row(int i) =>
        Flex.Row(gap: 12).FlexShrink(0).AlignItems(AlignItems.Center).Padding(new Thickness(6, 16)).Children(
            Text.New($"{i}").Width(Dimension.Px(64)),
            Box.New().FlexGrow(1).Children(Text.New($"label number {i}")),
            Box.New().Width(Dimension.Px(24)).Children(Text.New("x")));

    [Fact]
    public void AResizedListMeasuresEachLabelOncePerWidthItTakes()
    {
        var context = new CountingMeasureContext();
        var engine = new LayoutEngine(context);
        var rows = Enumerable.Range(0, 50).Select(Row).ToArray();
        var root = ScrollView.New().Children(Flex.Column().FlexShrink(0).Children(rows));
        engine.PerformLayout(root, 800, 600);

        context.Calls = 0;
        engine.PerformLayout(root, 780, 600);

        // Per row: the growing label three times — at the row's width while its flex base size is
        // found, then at the width it grew to, once offered and once forced. The id and the glyph
        // are in boxes of a set width, which hold at any width the row offers.
        Assert.Equal(3 * rows.Length, context.Calls);
    }

    [Fact]
    public void AResizedListOfLabelsThatFitMeasuresOnlyTheOneForcedToANewWidth()
    {
        // A label on one line that fits is the same at any width it fits in, so the row's new
        // width and the width the label grew to find its measurement already taken. Only the
        // layout pass forcing it to the width it grew to, a new one, measures it.
        var context = new CountingMeasureContext(saysWhereItHolds: true);
        var engine = new LayoutEngine(context);
        var rows = Enumerable.Range(0, 50).Select(Row).ToArray();
        var root = ScrollView.New().Children(Flex.Column().FlexShrink(0).Children(rows));
        engine.PerformLayout(root, 800, 600);

        context.Calls = 0;
        engine.PerformLayout(root, 780, 600);

        Assert.Equal(rows.Length, context.Calls);
    }

    [Fact]
    public void AResizeLaysOutWhatItDidBeforeTheCacheWasShared()
    {
        // The same row laid out fresh at the new width, with nothing cached, is the reference.
        var resized = Flex.Column().FlexShrink(0).Children(Enumerable.Range(0, 5).Select(Row).ToArray());
        var engine = new LayoutEngine(new CountingMeasureContext());
        engine.PerformLayout(resized, 800, 600);
        engine.PerformLayout(resized, 500, 600);

        var fresh = Flex.Column().FlexShrink(0).Children(Enumerable.Range(0, 5).Select(Row).ToArray());
        new LayoutEngine(new CountingMeasureContext()).PerformLayout(fresh, 500, 600);

        foreach (var (a, b) in Flatten(resized).Zip(Flatten(fresh)))
            Assert.Equal(b.Layout, a.Layout);
    }

    [Fact]
    public void APercentageHeightLabelIsStillMeasuredAgainstTheHeightItIsOffered()
    {
        // Text measures at a width only, but a percentage height resolves against the height on
        // offer — the one case the cache must keep the height in its key for. In a row, where the
        // height is the cross axis and nothing forces it (in a column it would be the flex basis,
        // and arrive forced).
        var label = Text.New("half").Height(Dimension.Percent(50));
        var root = Flex.Row().Children(label);
        var engine = new LayoutEngine(new CountingMeasureContext());

        engine.PerformLayout(root, 800, 600);
        Assert.Equal(300, label.Layout.Height);

        engine.PerformLayout(root, 800, 400);
        Assert.Equal(200, label.Layout.Height);
    }

    [Fact]
    public void APercentageMinHeightLabelIsStillMeasuredAgainstTheHeightItIsOffered()
    {
        var label = Text.New("half").MinHeight(Dimension.Percent(50));
        var root = Flex.Column().Children(label);
        var engine = new LayoutEngine(new CountingMeasureContext());

        engine.PerformLayout(root, 800, 600);
        Assert.Equal(300, label.Layout.Height);

        engine.PerformLayout(root, 800, 400);
        Assert.Equal(200, label.Layout.Height);
    }

    [Fact]
    public void AStretchedLabelIsAsTallAsItsLineAndAsWideAsItMeasures()
    {
        // A row stretches its label to the line's height: the forced height is taken from the
        // line, the width from the label's own measurement — which the cache now shares between
        // the measure pass (height not forced) and the layout pass (forced).
        var label = Text.New("abc");
        var tall = Box.New().Width(Dimension.Px(10)).Height(Dimension.Px(40));
        // In a column, so the row is as tall as its tallest item rather than the window.
        var root = Flex.Column().Children(Flex.Row().Children(label, tall));
        new LayoutEngine(new CountingMeasureContext()).PerformLayout(root, 800, 600);

        Assert.Equal(40, label.Layout.Height);
        Assert.Equal(3 * 7f, label.Layout.Width);
    }

    [Fact]
    public void AFontSizeChangeLaysTheLabelOutAgain()
    {
        // The style pass is the only place that sees a resolved font size change, so it is where
        // the label is marked for layout. Before, the layout kept the old size until something
        // else touched the label — and with leaves sized from the measure cache, for good.
        var label = Text.New("hello").FontSize(10f);
        var root = Flex.Column().Children(label);
        var engine = new LayoutEngine(new CountingMeasureContext());
        StyleResolver.Resolve(root);
        engine.PerformLayout(root, 800, 600);
        Assert.Equal(12f, label.Layout.Height, 3);

        label.FontSize(20f);
        label.MarkStyleDirty();
        StyleResolver.Resolve(root);
        engine.PerformLayout(root, 800, 600);

        Assert.Equal(24f, label.Layout.Height, 3);
    }

    [Fact]
    public void AColourChangeDoesNotLayAnythingOut()
    {
        var label = Text.New("hello").FontSize(10f);
        var root = Flex.Column().Children(label);
        var context = new CountingMeasureContext();
        var engine = new LayoutEngine(context);
        StyleResolver.Resolve(root);
        engine.PerformLayout(root, 800, 600);

        label.TextColor(Color.FromHex("#FF0000"));
        label.MarkStyleDirty();
        StyleResolver.Resolve(root);

        Assert.Equal(DirtyFlags.None, root.DirtyFlags & DirtyFlags.Layout);
        context.Calls = 0;
        engine.PerformLayout(root, 800, 600);
        Assert.Equal(0, context.Calls);
    }

    private static IEnumerable<Element> Flatten(Element e)
    {
        yield return e;
        foreach (var c in e.Children)
            foreach (var d in Flatten(c))
                yield return d;
    }
}
