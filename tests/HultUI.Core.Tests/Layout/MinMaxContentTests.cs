using HultUI.Core.Builders;
using HultUI.Core.Elements;
using HultUI.Core.Layout;
using HultUI.Core.Primitives;
using Xunit;

namespace HultUI.Core.Tests.Layout;

/// <summary>
/// A box's min and max bind it <i>before</i> its content is laid out, so what is inside
/// is laid out in the size the box ends up with.
///
/// <para>They used to be applied to the box alone, afterwards. A column with
/// <c>MaxWidth(720)</c> stretched across a 1400-wide parent came out 720 wide with its
/// children laid out 1400 wide: drawn past its edge, and — since hit testing stops at a
/// parent's bounds — out of reach of the pointer. That is how a Connect button at the
/// right-hand end of a settings card came to look enabled and ignore every click. Text
/// with a max width was the same fault from the other side: measured on one long line,
/// painted wrapped, and so shorter in the layout than on the screen.</para>
/// </summary>
public class MinMaxContentTests
{
    private readonly LayoutEngine _engine = new(new StubMeasureContext());

    private void Layout(Element root, float width = 1000, float height = 600) =>
        _engine.PerformLayout(root, width, height);

    [Fact]
    public void AStretchedColumnChildWithAMaxWidth_LaysItsChildrenOutInsideIt()
    {
        // The season picker's card: a row whose button is pushed to the far end by a
        // growing spacer. The far end has to be the card's, not the page's.
        var button = Box.New().Width(80).Height(30);
        var row = Flex.Row().Children(Box.New().FlexGrow(1), button);
        var card = Flex.Column().MaxWidth(560).Children(row);
        var page = Flex.Column().Width(1000).Children(card);
        Layout(page);

        Assert.Equal(560, card.Layout.Width, 0.5f);
        Assert.Equal(560, row.Layout.Width, 0.5f);
        Assert.True(button.Layout.AbsoluteBorderBox.Right <= card.Layout.AbsoluteBorderBox.Right + 0.5f,
            $"the button ends at {button.Layout.AbsoluteBorderBox.Right}, the card at {card.Layout.AbsoluteBorderBox.Right}");
    }

    [Fact]
    public void ScrollContentWithAMaxWidth_KeepsItsCardsInsideIt()
    {
        // The settings page: a scroll view's content, capped, with padding of its own.
        var card = Flex.Column().Children(Box.New().Height(40));
        var content = Flex.Column().Padding(new Thickness(24)).MaxWidth(720).Children(card);
        var scroll = ScrollView.Vertical().Width(1376).Height(600);
        scroll.AddChild(content);
        Layout(scroll, 1376, 600);

        Assert.Equal(720, content.Layout.Width, 0.5f);
        Assert.Equal(720 - 48, card.Layout.Width, 0.5f);
    }

    [Fact]
    public void TextWithAMaxWidth_IsMeasuredAtTheWidthItIsDrawnAt()
    {
        // 200 characters at the stub's 7px: 1400px on one line, which a 1000px column would
        // break in two. Capped at 350px it is 50 characters to a line — four lines — and
        // the box has to be four lines tall, or whatever follows it is drawn over the last.
        var text = Text.New(new string('x', 200)).MaxWidth(350);
        var after = Box.New().Height(10);
        var column = Flex.Column().Width(1000).Children(text, after);
        Layout(column);

        Assert.Equal(350, text.Layout.Width, 0.5f);
        Assert.Equal(4 * 14, text.Layout.Height, 0.5f);
        Assert.Equal(4 * 14, after.Layout.Y, 0.5f);
    }

    [Fact]
    public void AStretchedRowChildWithAMaxHeight_LaysItsChildrenOutInsideIt()
    {
        // The same rule on the other axis: a column stretched to a row's height, capped.
        var filler = Box.New().FlexGrow(1);
        var capped = Flex.Column().Width(100).MaxHeight(120).Children(filler);
        var row = Flex.Row().Width(400).Height(400).Children(capped);
        Layout(row, 400, 400);

        Assert.Equal(120, capped.Layout.Height, 0.5f);
        Assert.Equal(120, filler.Layout.Height, 0.5f);
    }

    [Fact]
    public void AMinWidthWiderThanTheSpace_IsTheWidthTheContentGets()
    {
        // The mirror case: a box that may not be narrower than 300 in a 200 column is 300
        // wide, and its content is laid out across all of it.
        var inner = Box.New().Height(20);
        var box = Flex.Column().MinWidth(300).Children(inner);
        var column = Flex.Column().Width(200).Children(box);
        Layout(column, 200, 600);

        Assert.Equal(300, box.Layout.Width, 0.5f);
        Assert.Equal(300, inner.Layout.Width, 0.5f);
    }

    [Fact]
    public void AMaxWidthThatDoesNotBind_ChangesNothing()
    {
        var inner = Box.New().Height(20);
        var box = Flex.Column().MaxWidth(2000).Children(inner);
        var column = Flex.Column().Width(640).Children(box);
        Layout(column, 640, 600);

        Assert.Equal(640, box.Layout.Width, 0.5f);
        Assert.Equal(640, inner.Layout.Width, 0.5f);
    }
}
