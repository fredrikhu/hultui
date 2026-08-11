using HultUI.Core.Builders;
using HultUI.Core.Elements;
using HultUI.Core.Layout;
using HultUI.Core.Primitives;
using Xunit;

namespace HultUI.Core.Tests.Layout;

/// <summary>
/// A flex item's cross size depends on the main size it ends up with, so it can only be
/// measured once grow/shrink has run. Measuring at the flex base size instead is invisible
/// for a fixed-size box and catastrophic for text: an item with <c>flex-basis: 0</c> is
/// measured at zero width, wraps to one line per character, and hands that height to its
/// line — so a table header of <c>FlexBasis(0)</c> cells came out as tall as its longest
/// label had characters.
/// <para>The stub measure context wraps at one character per line when the width is zero,
/// which is what the real text pipeline does too.</para>
/// </summary>
public class FlexCrossSizeTests
{
    private readonly LayoutEngine _layout = new(new StubMeasureContext());   // 7px chars, 14px lines

    /// <summary>A cell shaped like a table-header column: takes its width from the row,
    /// not from its content.</summary>
    private static Element Cell(string label) =>
        Flex.Row(gap: 0).FlexGrow(1).FlexBasis(Dimension.Px(0))
            .Children(Text.New(label));

    [Fact]
    public void ARowOfZeroBasisCellsIsOneLineTall()
    {
        var row = Flex.Row(gap: 0).Children(Cell("BRANCH"), Cell("LAST COMMIT"));
        var root = Flex.Column().Width(800).Height(600).Children(row);

        _layout.PerformLayout(root, 800, 600);

        // Not 11 lines, which is what "LAST COMMIT" measures to at zero width.
        Assert.Equal(14, row.Layout.Height, 1f);
    }

    [Fact]
    public void TheCellsThemselvesGetTheRowsHeight()
    {
        var a = Cell("BRANCH");
        var b = Cell("LAST COMMIT");
        var row = Flex.Row(gap: 0).Children(a, b);
        var root = Flex.Column().Width(800).Height(600).Children(row);

        _layout.PerformLayout(root, 800, 600);

        Assert.Equal(14, a.Layout.Height, 1f);
        Assert.Equal(14, b.Layout.Height, 1f);
        // And they really did grow — a cell still at its zero basis would prove nothing.
        Assert.Equal(400, a.Layout.Width, 1f);
    }

    /// <summary>The height has to come from the label that is longest once laid out, not
    /// from the one with the most characters. Both are one line here, and a row that still
    /// measured at the basis would be 11 lines tall — one per character of the longer.</summary>
    [Fact]
    public void PaddingIsAddedToOneLineNotToElevenOfThem()
    {
        var row = Flex.Row(gap: 0).Padding(new Thickness(6, 26, 6, 26))
            .Children(Cell("BRANCH"), Cell("LAST COMMIT"));
        var root = Flex.Column().Width(800).Height(600).Children(row);

        _layout.PerformLayout(root, 800, 600);

        Assert.Equal(14 + 12, row.Layout.Height, 1f);
    }

    /// <summary>A cell narrow enough that its text genuinely does wrap still reports the
    /// wrapped height — the fix is about measuring at the right width, not about refusing
    /// to wrap.</summary>
    [Fact]
    public void ACellTooNarrowForItsLabelStillWraps()
    {
        // 100px row, two cells at 50px each: "ABCDEFGHIJKLMN" is 14 chars × 7px = 98px,
        // so it needs two lines in a 50px cell.
        var row = Flex.Row(gap: 0).Children(Cell("ABCDEFGHIJKLMN"), Cell("X"));
        var root = Flex.Column().Width(100).Height(600).Children(row);

        _layout.PerformLayout(root, 100, 600);

        Assert.Equal(28, row.Layout.Height, 1f);
    }

    /// <summary>Shrinking moves an item's main size just as growing does, and the same
    /// re-measure has to follow it.</summary>
    [Fact]
    public void AShrunkCellIsMeasuredAtTheWidthItShrankTo()
    {
        // Two 200px-wide cells in a 100px row: each shrinks to 50px, and the 14-character
        // label then needs two lines. Measured at its 200px basis it would claim one.
        var wide = Flex.Row(gap: 0).Width(200).Children(Text.New("ABCDEFGHIJKLMN"));
        var other = Flex.Row(gap: 0).Width(200).Children(Text.New("X"));
        var row = Flex.Row(gap: 0).Children(wide, other);
        var root = Flex.Column().Width(100).Height(600).Children(row);

        _layout.PerformLayout(root, 100, 600);

        Assert.Equal(50, wide.Layout.Width, 1f);
        Assert.Equal(28, row.Layout.Height, 1f);
    }

    /// <summary>The other half of the engine. A container's height normally comes from its
    /// parent's measure pass, but a row that its parent centres rather than stretches is
    /// left to size itself — and that goes through the layout pass, which needs the same
    /// post-flex measurement.</summary>
    [Fact]
    public void ARowTheParentCentresRatherThanStretchesIsAlsoOneLineTall()
    {
        var inner = Flex.Row(gap: 0).FlexGrow(1).FlexBasis(Dimension.Px(0))
            .Children(Cell("BRANCH"), Cell("LAST COMMIT"));
        var outer = Flex.Row(gap: 0).AlignItems(AlignItems.Center).Children(inner);
        var root = Flex.Column().Width(800).Height(600).Children(outer);

        _layout.PerformLayout(root, 800, 600);

        Assert.Equal(14, inner.Layout.Height, 1f);
    }

    /// <summary>An item with a height of its own is not re-measured — nothing about its
    /// main size can change it — and the row takes that height.</summary>
    [Fact]
    public void AFixedHeightCellKeepsItsHeight()
    {
        var cell = Flex.Row(gap: 0).Height(40).FlexGrow(1).FlexBasis(Dimension.Px(0))
            .Children(Text.New("LAST COMMIT"));
        var row = Flex.Row(gap: 0).Children(cell);
        var root = Flex.Column().Width(800).Height(600).Children(row);

        _layout.PerformLayout(root, 800, 600);

        Assert.Equal(40, row.Layout.Height, 1f);
    }
}
