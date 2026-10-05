using HultUI.Core.Builders;
using HultUI.Core.Elements;
using HultUI.Core.Layout;
using HultUI.Core.Primitives;
using HultUI.Core.Rendering;
using HultUI.Core.Styling;
using Xunit;

namespace HultUI.Core.Tests.Layout;

/// <summary>
/// <see cref="AlignItems.Baseline"/>, which existed in the enum and did nothing — it laid out
/// as FlexStart. Measured with a font whose numbers are easy to check by hand: a line is
/// 1.25 × the font size tall and its baseline sits at 1 × the size, so 12px text is 15px
/// tall with its baseline 12px down, and 32px text is 40px tall with its baseline at 32.
/// </summary>
public class BaselineAlignTests
{
    private sealed class SizedFont : IMeasureContext
    {
        public Size MeasureText(string text, ResolvedInheritedStyle style, float maxWidth, int? maxLines,
            TextOverflow overflow = TextOverflow.Clip) =>
            string.IsNullOrEmpty(text) ? Size.Zero : new Size(text.Length * style.FontSize * 0.5f, style.FontSize * 1.25f);

        public FontMetrics MeasureFont(ResolvedInheritedStyle style) =>
            new("Test", Ascent: style.FontSize, Descent: style.FontSize * 0.25f, LineGap: 0,
                CharacterWidth: style.FontSize * 0.5f, IsFixedPitch: false,
                UnderlinePosition: 1, UnderlineThickness: 1, StrikeoutPosition: -4, StrikeoutThickness: 1);
    }

    private static readonly IMeasureContext Font = new SizedFont();

    private static TextElement Label(string text, float size)
    {
        var label = TextElement.New(text);
        label.Inherited.FontSize = size;
        return label;
    }

    private static void Lay(Element root, float width = 600, float height = 400)
    {
        StyleResolver.Resolve(root, Theme.Light.ToResolvedStyle());
        new LayoutEngine(Font).PerformLayout(root, width, height, forceRootFill: false);
    }

    private static float BaselineOf(Element e) =>
        e.Layout.AbsoluteY + e.MeasureBaseline(new Size(e.Layout.Width, e.Layout.Height), Font)!.Value;

    [Fact]
    public void TwoSizesOfTextShareABaseline()
    {
        var big = Label("Total", 32);
        var small = Label("items", 12);
        var row = Flex.Row().AlignItems(AlignItems.Baseline).Children(big, small);

        Lay(row);

        Assert.Equal(0, big.Layout.Y);
        Assert.Equal(20, small.Layout.Y);            // 32 − 12: lowered to the big one's baseline
        Assert.Equal(BaselineOf(big), BaselineOf(small));
        Assert.Equal(40, row.Layout.Height);         // nothing hangs below the big text
    }

    /// <summary>The control: the same row at FlexStart puts both at the top, which is what
    /// Baseline used to do.</summary>
    [Fact]
    public void FlexStartIsNotBaseline()
    {
        var small = Label("items", 12);
        var row = Flex.Row().AlignItems(AlignItems.FlexStart).Children(Label("Total", 32), small);

        Lay(row);

        Assert.Equal(0, small.Layout.Y);
    }

    /// <summary>
    /// An item with no text gets a baseline at its bottom edge, so text beside an icon sits on
    /// the icon's bottom and its descenders hang below it — and the row grows to hold them.
    /// The measure pass has to agree, or a parent sizing this row reserves too little.
    /// </summary>
    [Fact]
    public void TheRowGrowsForTextThatHangsBelowAnIcon()
    {
        var icon = Box.New().Width(40f).Height(40f);
        var text = Label("Downloads", 12);
        var row = Flex.Row().AlignItems(AlignItems.Baseline).Children(icon, text);
        var parent = Flex.Row().AlignItems(AlignItems.FlexStart).Children(row, Box.New().Width(10f).Height(10f));

        Lay(parent);

        Assert.Equal(0, icon.Layout.Y);
        Assert.Equal(28, text.Layout.Y);             // its baseline (12 down) on the icon's bottom (40)
        Assert.Equal(43, row.Layout.Height);         // 40 above the baseline + 3 of descent below
        Assert.Equal(43, parent.Layout.Height);      // measured as tall as it lays out
    }

    /// <summary>A container's baseline is its first item's: a stack of two labels lines up by
    /// its top label.</summary>
    [Fact]
    public void AContainerAlignsByItsFirstChild()
    {
        var top = Label("Name", 12);
        var stack = Flex.Column().Children(top, Label("Subtitle", 12));
        var big = Label("Hi", 24);
        var row = Flex.Row().AlignItems(AlignItems.Baseline).Children(stack, big);

        Lay(row);

        Assert.Equal(12, stack.Layout.Y);            // 24 − 12
        Assert.Equal(BaselineOf(big), BaselineOf(top));
    }

    /// <summary>Padding and border above the text count: the baseline is measured from the
    /// border box's top, through them.</summary>
    [Fact]
    public void PaddingAboveTheTextMovesTheBaseline()
    {
        var padded = Box.New().Padding(new Thickness(10, 0, 0, 0)).BorderWidth(new Thickness(2, 0, 0, 0))
            .Children(Label("in a box", 12));
        var plain = Label("plain", 12);
        var row = Flex.Row().AlignItems(AlignItems.Baseline).Children(padded, plain);

        Lay(row);

        Assert.Equal(0, padded.Layout.Y);
        Assert.Equal(12, plain.Layout.Y);            // 10 padding + 2 border
    }

    [Fact]
    public void AlignSelfBaselineJoinsTheGroupInAnotherwiseCentredRow()
    {
        var big = Label("Total", 32);
        big.Flex.AlignSelf = AlignSelf.Baseline;
        var small = Label("items", 12);
        small.Flex.AlignSelf = AlignSelf.Baseline;
        var centred = Box.New().Width(10f).Height(10f);
        var row = Flex.Row().AlignItems(AlignItems.Center).Children(big, small, centred);

        Lay(row);

        Assert.Equal(20, small.Layout.Y);
        Assert.Equal(15, centred.Layout.Y);          // (40 − 10) / 2, still centred
    }

    /// <summary>A text field's baseline is its text line's, centred in the box — so a label
    /// beside a 32px field reads on the same line as what is typed in it.</summary>
    [Fact]
    public void ALabelLinesUpWithTheTextInAField()
    {
        var label = Label("Name", 12);
        var field = TextInputElement.New("Ada");
        field.Inherited.FontSize = 12;
        field.Box.Height = 32;
        field.Box.Width = 120;
        var row = Flex.Row().AlignItems(AlignItems.Baseline).Children(label, field);

        Lay(row);

        Assert.Equal(BaselineOf(field), BaselineOf(label));
        Assert.True(label.Layout.Y > 0, "the label should come down to the field's text line");
    }

    /// <summary>A column has no baseline across its line, as in CSS: Baseline there is FlexStart
    /// — at the start, and not stretched.</summary>
    [Fact]
    public void InAColumnBaselineIsFlexStart()
    {
        var label = Label("abc", 12);
        var column = Flex.Column().Width(300f).AlignItems(AlignItems.Baseline).Children(label);

        Lay(column);

        Assert.Equal(0, label.Layout.X);
        Assert.Equal(18, label.Layout.Width);        // its own width, not the column's 300
    }

    /// <summary>A centred column sized by its content has no free space, so its text sits at
    /// its top — and the baseline the row aligns by has to be measured the same way, or the
    /// row lowers the other text to a baseline nothing is drawn on.</summary>
    [Fact]
    public void AnAutoHeightCentredColumnOffersTheBaselineItLaysOut()
    {
        var label = Label("Total", 12);
        var inner = Label("items", 12);
        var column = Flex.Column().JustifyContent(JustifyContent.Center).Children(inner);
        var row = Flex.Row().Height(40f).AlignItems(AlignItems.Baseline).Children(label, column);

        Lay(row);

        Assert.Equal(15, column.Layout.Height);
        Assert.Equal(0, label.Layout.Y);
        Assert.Equal(0, inner.Layout.AbsoluteY);
        Assert.Equal(BaselineOf(label), BaselineOf(inner));
    }

    /// <summary>A row with a min height centres its text in that height wherever it is placed,
    /// and offers the baseline of the text where it is drawn.</summary>
    [Fact]
    public void AMinHeightRowOffersTheBaselineOfItsCentredText()
    {
        var label = Label("Total", 12);
        var inner = Label("items", 12);
        var minRow = Flex.Row().MinHeight(40f).AlignItems(AlignItems.Center).Children(inner);
        var row = Flex.Row().Height(80f).AlignItems(AlignItems.Baseline).Children(label, minRow);

        Lay(row);

        Assert.Equal(40, minRow.Layout.Height);
        Assert.Equal(12.5f, inner.Layout.AbsoluteY - minRow.Layout.AbsoluteY);   // (40 − 15) / 2
        Assert.Equal(BaselineOf(label), BaselineOf(inner));
    }

    /// <summary>The cross-axis twin: a wrapping row sized by its lines has no space to align
    /// them in, and its baseline must be measured that way too.</summary>
    [Fact]
    public void AnAutoHeightWrappingRowOffersTheBaselineItLaysOut()
    {
        var label = Label("Total", 12);
        var inner = Label("items", 12);
        var wrapped = Flex.Row().Width(200f).FlexWrap(FlexWrap.Wrap).AlignContent(AlignContent.Center).Children(inner);
        var row = Flex.Row().Height(40f).AlignItems(AlignItems.Baseline).Children(label, wrapped);

        Lay(row);

        Assert.Equal(15, wrapped.Layout.Height);
        Assert.Equal(0, label.Layout.Y);
        Assert.Equal(0, inner.Layout.AbsoluteY);
        Assert.Equal(BaselineOf(label), BaselineOf(inner));
    }
}
