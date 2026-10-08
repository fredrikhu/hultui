using HultUI.Core.Builders;
using HultUI.Core.Elements;
using HultUI.Core.Layout;
using HultUI.Core.Primitives;
using HultUI.Core.Rendering;
using HultUI.Core.Styling;
using Xunit;

namespace HultUI.Core.Tests.Layout;

/// <summary>
/// A one-line label that truncates (MaxLines(1), with an ellipsis or clipped), directly in a row.
/// Its flex base size is its untruncated width capped at the room the row offers — not the label
/// truncated at that room — so a resize truncates it once a step, at the width it ends up with,
/// rather than once for the base and again at that width.
/// </summary>
public partial class TruncatingLabelTests
{
    /// <summary>
    /// Truncates as the text pipeline does: whole characters and then the ellipsis, which is drawn
    /// even when there is no room for it, so the truncated text ends short of the room by less
    /// than a character (the ellipsis's slack). 7 px a character, 14 px a line; a ▲ on the line
    /// drawn makes it 21 px, as a fallback font with a taller ascent does. Counts every
    /// measurement, and the ones that truncate.
    /// </summary>
    internal sealed class EllipsisMeasureContext : IMeasureContext
    {
        public const float CharWidth = 7f, LineHeight = 14f, TallLineHeight = 21f;
        public int Calls, Truncations;

        public Size MeasureText(string text, ResolvedInheritedStyle style, float maxWidth, int? maxLines,
            TextOverflow overflow, IReadOnlyList<TextSpan>? spans, out float sameFromWidth)
        {
            var size = MeasureText(text, style, maxWidth, maxLines, overflow);
            float total = text.Length * CharWidth;
            sameFromWidth = total <= maxWidth && !text.Contains('\n') ? total : float.NaN;
            return size;
        }

        public Size MeasureText(string text, ResolvedInheritedStyle style, float maxWidth, int? maxLines,
            TextOverflow overflow = TextOverflow.Clip)
        {
            Calls++;
            if (string.IsNullOrEmpty(text)) return Size.Zero;
            float total = text.Length * CharWidth;
            if (total <= maxWidth) return new Size(total, Height(text));

            if (maxLines == 1)
            {
                Truncations++;
                if (overflow == TextOverflow.Ellipsis)
                {
                    int kept = Math.Max(0, (int)((maxWidth - CharWidth) / CharWidth));
                    return new Size((kept + 1) * CharWidth, Height(text[..kept]));
                }
                int fit = Math.Max(0, (int)(maxWidth / CharWidth));
                return new Size(fit * CharWidth, Height(text[..fit]));
            }

            int perLine = Math.Max(1, (int)(maxWidth / CharWidth));
            int lines = (int)Math.Ceiling((double)text.Length / perLine);
            if (maxLines is { } max) lines = Math.Min(lines, max);
            return new Size(Math.Min(total, maxWidth), lines * LineHeight);
        }

        private static float Height(string drawn) => drawn.Contains('▲') ? TallLineHeight : LineHeight;

        public float MinContentTextWidth(string text, ResolvedInheritedStyle style, IReadOnlyList<TextSpan>? spans = null) =>
            text.Split(' ', '\n').Max(word => word.Length) * CharWidth;
    }

    // 60 characters in words of four: 420 px untruncated, 28 px its widest word.
    private static readonly string Long = string.Join(' ', Enumerable.Repeat("abcd", 12));

    private static TextElement Truncating(string text, TextOverflow overflow = TextOverflow.Ellipsis) =>
        Text.New(text).MaxLines(1).TextOverflow(overflow);

    [Fact]
    public void AnEllipsisLabelInAShrinkingRowIsTruncatedOnceARelayout()
    {
        // The js-framework-benchmark row's shape: a label that takes the slack, beside a count
        // that keeps its width. Each new width truncates the label once, at the width it shrinks
        // to; its untruncated width is measured once and kept, however many widths go by.
        var context = new EllipsisMeasureContext();
        var engine = new LayoutEngine(context);
        var label = Truncating(Long).FlexGrow(1);
        var root = Flex.Column().Children(Flex.Row(gap: 8).Children(label, Text.New("12").FlexShrink(0)));
        engine.PerformLayout(root, 300, 200);

        for (int width = 290; width >= 220; width -= 10)
        {
            context.Calls = context.Truncations = 0;
            engine.PerformLayout(root, width, 200);

            Assert.Equal(1, context.Calls);
            Assert.Equal(1, context.Truncations);
            Assert.Equal(width - 8 - 14, label.Layout.Width);
        }
    }

    [Fact]
    public void AClippedLabelInAShrinkingRowIsTruncatedOnceARelayout()
    {
        var context = new EllipsisMeasureContext();
        var engine = new LayoutEngine(context);
        var root = Flex.Column().Children(
            Flex.Row(gap: 8).Children(Truncating(Long, TextOverflow.Clip).FlexGrow(1), Text.New("12").FlexShrink(0)));
        engine.PerformLayout(root, 300, 200);

        for (int width = 290; width >= 220; width -= 10)
        {
            context.Calls = 0;
            engine.PerformLayout(root, width, 200);
            Assert.Equal(1, context.Calls);
        }
    }

    [Fact]
    public void ATruncatingLabelsBaseIsTheRoomNotTheTruncatedText()
    {
        // Alone in a row of 200 that it does not fit, it takes the row: truncated at 200 the text
        // is 27 characters and the ellipsis, 196 px, which used to be its width.
        var label = Truncating(Long);
        var root = Flex.Column().AlignItems(AlignItems.FlexStart).Children(Flex.Row().Width(Dimension.Px(200)).Children(label));
        new LayoutEngine(new EllipsisMeasureContext()).PerformLayout(root, 300, 200);

        Assert.Equal(200, label.Layout.Width);
    }

    [Fact]
    public void ATruncatingLabelSharesTheShrinkByTheRoomItWasOffered()
    {
        // Bases 200 (capped) and 70 (fits) in a row of 200: the 70 px they overflow by is taken
        // from each in proportion to its base. With the truncated base, 196, the shares were
        // 147.37 and 52.63.
        var first = Truncating(Long);
        var second = Truncating("ab ab ab a");
        var root = Flex.Column().AlignItems(AlignItems.FlexStart).Children(
            Flex.Row().Width(Dimension.Px(200)).Children(first, second));
        new LayoutEngine(new EllipsisMeasureContext()).PerformLayout(root, 300, 200);

        Assert.Equal(200f - 70f * 200f / 270f, first.Layout.Width, 3);
        Assert.Equal(70f - 70f * 70f / 270f, second.Layout.Width, 3);
    }

    [Fact]
    public void ATruncatingLabelThatFitsIsMeasuredOnceAsBefore()
    {
        // 70 px in rows of 300 down to 230: its own width, measured once and held at every width
        // it fits in.
        var context = new EllipsisMeasureContext();
        var engine = new LayoutEngine(context);
        var label = Truncating("abcd abcd ");
        var root = Flex.Column().Children(Flex.Row().Children(label));
        engine.PerformLayout(root, 300, 200);
        Assert.Equal(1, context.Calls);
        Assert.Equal(70, label.Layout.Width);

        context.Calls = 0;
        for (int width = 290; width >= 230; width -= 10)
            engine.PerformLayout(root, width, 200);
        Assert.Equal(0, context.Calls);
        Assert.Equal(70, label.Layout.Width);
    }

    [Fact]
    public void ATruncatedLabelIsAsTallAsTheLineItDraws()
    {
        // The untruncated line holds a taller glyph that the truncated one cuts off: the row is as
        // tall as the line drawn, measured at the width the label is laid out at, not as the
        // untruncated one.
        var label = Truncating(Long + "▲▲");
        var root = Flex.Column().AlignItems(AlignItems.FlexStart).Children(Flex.Row().Width(Dimension.Px(100)).Children(label));
        new LayoutEngine(new EllipsisMeasureContext()).PerformLayout(root, 300, 200);

        Assert.Equal(100, label.Layout.Width);
        Assert.Equal(EllipsisMeasureContext.LineHeight, label.Layout.Height);
        Assert.Equal(EllipsisMeasureContext.LineHeight, label.Parent!.Layout.Height);
    }

    [Fact]
    public void ATruncatingLabelWithNoRoomHasABaseOfItsPadding()
    {
        // Junction's branch-name cell beside a chip that takes all of it: the column holding the
        // title gets nothing, and so does the row in it. The label's base is 0 — the ellipsis
        // alone used to measure 7 px there, overflow the row and set it shrinking.
        var label = Truncating(Long);
        var titleRow = Flex.Row().Children(label);
        var titleColumn = Flex.Column().FlexBasis(Dimension.Px(0)).FlexGrow(1).Children(titleRow);
        var cell = Flex.Row().Width(Dimension.Px(100)).Children(
            Box.New().Width(Dimension.Px(100)).FlexShrink(0), titleColumn);
        var root = Flex.Column().AlignItems(AlignItems.FlexStart).Children(cell);
        new LayoutEngine(new EllipsisMeasureContext()).PerformLayout(root, 300, 200);

        Assert.Equal(0, titleRow.Layout.Width);
        Assert.Equal(0, label.Layout.Width);
    }

    [Fact]
    public void ALabelThatTruncatesOnlyAtItsSetWidthKeepsItsMeasurement()
    {
        // Not a label sized from its text: a set width is its base, as for any item.
        var label = Truncating(Long).Width(Dimension.Px(150));
        var root = Flex.Column().AlignItems(AlignItems.FlexStart).Children(Flex.Row().Width(Dimension.Px(200)).Children(label));
        new LayoutEngine(new EllipsisMeasureContext()).PerformLayout(root, 300, 200);

        Assert.Equal(150, label.Layout.Width);
    }
}
