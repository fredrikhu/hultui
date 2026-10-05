using HultUI.Core.Builders;
using HultUI.Core.Elements;
using HultUI.Core.Layout;
using HultUI.Core.Primitives;
using HultUI.Core.Rendering;
using Xunit;

namespace HultUI.Core.Tests.Layout;

/// <summary>
/// <see cref="BoxStyle.AspectRatio"/>: the side left auto follows the one that is set,
/// stretched, flexed or given by a grid cell — width first — and the box keeps its shape when a
/// column runs short.
/// </summary>
public class AspectRatioTests
{
    private readonly LayoutEngine _engine = new(new StubMeasureContext());

    private void Layout(Element root, float width = 1000, float height = 800) =>
        _engine.PerformLayout(root, width, height);

    private static void AssertSize(Element e, float width, float height)
    {
        Assert.Equal(width, e.Layout.Width, 0.5f);
        Assert.Equal(height, e.Layout.Height, 0.5f);
    }

    [Fact]
    public void AGivenWidthMakesTheHeight_AndAGivenHeightTheWidth()
    {
        var byWidth = Box.New().Width(320).AspectRatio(16, 9);
        var byHeight = Box.New().Height(120).AspectRatio(16, 9);
        var both = Box.New().Width(100).Height(100).AspectRatio(16, 9);
        Layout(Flex.Row(gap: 0).AlignItems(AlignItems.FlexStart).Children(byWidth, byHeight, both));

        AssertSize(byWidth, 320, 180);
        AssertSize(byHeight, 213.33f, 120);
        AssertSize(both, 100, 100);   // both given: the ratio has nothing to decide
    }

    [Fact]
    public void InAColumn_ARatioBoxTakesTheWidth_AndWhatFollowsSitsUnderIt()
    {
        var poster = Box.New().AspectRatio(2, 3);
        var caption = Box.New().Height(20);
        var column = Flex.Column(gap: 0).Width(300).Children(poster, caption);
        Layout(Flex.Column(gap: 0).AlignItems(AlignItems.FlexStart).Children(column));

        AssertSize(poster, 300, 450);
        Assert.Equal(450, caption.Layout.Y, 0.5f);
        Assert.Equal(470, column.Layout.Height, 0.5f);
    }

    /// <summary>The column in a column of no set width: its room is what the outer one offers,
    /// and the ratio box measures against that in both passes, not against its own content.</summary>
    [Fact]
    public void NestedInColumnsOfNoWidth_ItStillTakesTheRoom()
    {
        var picture = Box.New().AspectRatio(2);
        var inner = Flex.Column(gap: 0).Children(picture);
        var outer = Flex.Column(gap: 0).Children(inner, Box.New().Height(10));
        Layout(Flex.Row(gap: 0).Children(Flex.Column(gap: 0).Width(600).Children(outer)));

        AssertSize(picture, 600, 300);
        Assert.Equal(310, outer.Layout.Height, 0.5f);
    }

    [Fact]
    public void InARow_ItIsNotStretchedDown()
    {
        var square = Box.New().Width(100).AspectRatio(1);
        var plain = Box.New().Width(100);
        Layout(Flex.Row(gap: 0).Height(300).Children(square, plain));

        AssertSize(square, 100, 100);
        AssertSize(plain, 100, 300);
    }

    [Fact]
    public void GrowingInARow_ItKeepsItsShape()
    {
        var a = Box.New().FlexGrow(1).FlexBasis(0).AspectRatio(1);
        var b = Box.New().FlexGrow(1).FlexBasis(0).AspectRatio(2);
        var row = Flex.Row(gap: 0).Width(600).Children(a, b);
        Layout(Flex.Column(gap: 0).AlignItems(AlignItems.FlexStart).Children(row));

        AssertSize(a, 300, 300);
        AssertSize(b, 300, 150);
        Assert.Equal(300, row.Layout.Height, 0.5f);
    }

    [Fact]
    public void InAGridCell_ItTakesTheCellsWidth_AndItsOwnHeightAtTheTop()
    {
        var a = Box.New().AspectRatio(2, 3).GridCell(0, 0);
        var b = Box.New().AspectRatio(1).GridCell(0, 1);
        var auto = Grid.Of("1fr 1fr").Width(600).Children(a, b);
        var fixedRow = Box.New().AspectRatio(2).GridCell(0, 0);
        var tall = Grid.Of("1fr", rows: "500px").Width(400).Children(fixedRow);
        // Room for both grids, so neither is squeezed by the column.
        Layout(Flex.Column(gap: 0).AlignItems(AlignItems.FlexStart).Children(auto, tall), height: 2000);

        AssertSize(a, 300, 450);
        AssertSize(b, 300, 300);
        Assert.Equal(450, auto.Layout.Height, 0.5f);   // the auto row is the tallest ratio
        AssertSize(fixedRow, 400, 200);                 // not stretched down the 500px row
        Assert.Equal(0, fixedRow.Layout.Y, 0.5f);           // at the top of its cell
    }

    [Fact]
    public void InAColumnThatRunsShort_ItKeepsItsShapeAndOverflows()
    {
        var picture = Box.New().AspectRatio(2);
        var column = Flex.Column(gap: 0).Width(400).Height(100).Children(picture);
        Layout(Flex.Column(gap: 0).AlignItems(AlignItems.FlexStart).Children(column));

        AssertSize(picture, 400, 200);
    }

    [Fact]
    public void MinAndMaxStillBind_ThePercentWidthResolves_AndNoWidthTakesTheContents()
    {
        var capped = Box.New().Width(400).AspectRatio(1).MaxHeight(300);
        var half = Box.New().Width(Dimension.Percent(50)).AspectRatio(4);
        var fromContent = Flex.Row(gap: 0).AspectRatio(2).Children(Box.New().Width(100).Height(10));
        Layout(Flex.Column(gap: 0).Width(800).AlignItems(AlignItems.FlexStart).Children(capped, half, fromContent));

        AssertSize(capped, 400, 300);
        AssertSize(half, 400, 100);
        AssertSize(fromContent, 100, 50);
    }

    [Fact]
    public void GivenAHeight_InAColumn_TheHeightDecidesTheWidth()
    {
        var thumb = Box.New().Height(90).AspectRatio(16, 9);
        Layout(Flex.Column(gap: 0).Width(600).Children(thumb));
        AssertSize(thumb, 160, 90);
    }

    /// <summary>A picture keeps its own shape when it is given one side — as an HTML image does
    /// — and only then: given both, or neither and stretched across a column, it lays out as it
    /// always has and its fit draws it.</summary>
    [Fact]
    public void APictureGivenOneSide_KeepsItsOwnShape()
    {
        var source = new Picture(new Size(400, 300));
        var byWidth = Image.New(source).Width(200);
        var byHeight = Image.New(source).Height(60);
        var both = Image.New(source).Width(50).Height(50);
        var stretched = Image.New(source);
        Layout(Flex.Column(gap: 0).Width(600).Children(byWidth, byHeight, both, stretched));

        AssertSize(byWidth, 200, 150);
        AssertSize(byHeight, 80, 60);
        AssertSize(both, 50, 50);
        AssertSize(stretched, 600, 300);
    }

    private sealed class Picture(Size size) : IImageSource
    {
        public Size IntrinsicSize => size;
        public bool IsLoaded => true;
        public string? AlternateText => null;
    }

    [Fact]
    public void AnAbsoluteBoxWithAWidth_TakesItsHeightFromIt()
    {
        var badge = Box.New().Width(90).AspectRatio(3);
        badge.Position.Type = PositionType.Absolute;
        Layout(Box.New().Width(500).Height(500).Children(badge));
        AssertSize(badge, 90, 30);
    }
}

