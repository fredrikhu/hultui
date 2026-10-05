using HultUI.Core.Builders;
using HultUI.Core.Primitives;
using Xunit;

namespace HultUI.Core.Tests.Builders;

/// <summary><see cref="Popover.Position"/>: the side it takes, where along the anchor, and
/// keeping inside the window.</summary>
public class PopoverPositionTests
{
    private static readonly Size Window = new(800, 600);
    private const float Gap = 6f, Margin = 8f;

    private static (Point At, PopoverPlacement Side) Place(Rect anchor, Size size,
        PopoverPlacement side = PopoverPlacement.Below, PopoverAlignment align = PopoverAlignment.Start) =>
        Popover.Position(anchor, size, Window, side, align, Gap, Margin);

    [Fact]
    public void BelowAndLinedUp_WhereItFits()
    {
        var anchor = new Rect(100, 50, 80, 30);
        Assert.Equal((new Point(100, 86), PopoverPlacement.Below), Place(anchor, new Size(200, 100)));
        Assert.Equal(new Point(40, 86), Place(anchor, new Size(200, 100), align: PopoverAlignment.Center).At);
        Assert.Equal(new Point(-20 + 8 + 20, 86), Place(anchor, new Size(200, 100), align: PopoverAlignment.End).At);
    }

    [Fact]
    public void ItFlipsWhenItDoesNotFit_AndTheOtherSideHasMoreRoom()
    {
        var low = new Rect(100, 520, 80, 30);
        var (at, side) = Place(low, new Size(200, 100));
        Assert.Equal(PopoverPlacement.Above, side);
        Assert.Equal(520 - 6 - 100, at.Y);

        var right = new Rect(700, 100, 60, 30);
        (at, side) = Place(right, new Size(200, 100), PopoverPlacement.Right);
        Assert.Equal(PopoverPlacement.Left, side);
        Assert.Equal(700 - 6 - 200, at.X);
    }

    [Fact]
    public void ItKeepsItsSide_WhenTheOtherHasNoMoreRoom()
    {
        // Taller than either side: below has more room than above, so below it stays, slid up
        // to stay in the window and over the anchor.
        var anchor = new Rect(100, 200, 80, 30);
        var (at, side) = Place(anchor, new Size(200, 500));
        Assert.Equal(PopoverPlacement.Below, side);
        Assert.Equal(600 - 8 - 500, at.Y);
    }

    [Fact]
    public void ItSlidesAlongTheAnchorToStayInTheWindow()
    {
        var nearRight = new Rect(740, 50, 50, 30);
        Assert.Equal(800 - 8 - 200, Place(nearRight, new Size(200, 100)).At.X);

        var nearLeft = new Rect(10, 50, 50, 30);
        Assert.Equal(8, Place(nearLeft, new Size(200, 100), align: PopoverAlignment.End).At.X);

        // Wider than the window: from the near margin.
        Assert.Equal(8, Place(nearLeft, new Size(900, 100)).At.X);
    }

    [Fact]
    public void ItLandsOnWholePixels()
    {
        var anchor = new Rect(100.4f, 50.3f, 81, 30);
        var at = Place(anchor, new Size(200, 100), align: PopoverAlignment.Center).At;
        Assert.Equal(MathF.Round(at.X), at.X);
        Assert.Equal(MathF.Round(at.Y), at.Y);
    }
}
