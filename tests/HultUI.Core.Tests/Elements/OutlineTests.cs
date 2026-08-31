using HultUI.Core.Builders;
using HultUI.Core.Elements;
using HultUI.Core.Layout;
using HultUI.Core.Primitives;
using Xunit;

namespace HultUI.Core.Tests.Elements;

/// <summary>
/// An outline is a ring that takes up no room.
///
/// <para>The whole point is the negative: a border on an auto-sized element makes it wider and
/// taller, so a row of buttons where only the selected one is bordered shifts every time the
/// selection moves. Junction's diff toolbar did exactly that — measured, the Split button slid
/// 2px left the moment Wrap was turned on, and Unified moved too without changing state at all.
/// <c>box-sizing</c> is no help: an element with no declared width is <c>content + padding +
/// border</c> under either box model, so there is nothing for a box model to reinterpret.</para>
/// </summary>
public class OutlineTests
{
    // Content-sized, which is the whole point: these have no declared width. A declared width
    // IS the border box here, so a border would not change the outer size and this test would
    // pass against an implementation that feeds the outline straight into layout. The first
    // version of this file made exactly that mistake and the fault injection caught it.
    private static BoxElement Cell() => Box.New().Padding(new Thickness(4)).Children(Text.New("ab"));

    private static (BoxElement First, BoxElement Neighbour) Row(Action<BoxElement> decorate)
    {
        var first = Cell();
        decorate(first);
        var neighbour = Cell();
        var row = Flex.Row(gap: 6).Children(first, neighbour);
        new LayoutEngine(new StubMeasureContext()).PerformLayout(row, 400, 100);
        return (first, neighbour);
    }

    /// <summary>
    /// The element measures the same outlined as bare, and its neighbour stays put.
    ///
    /// <para>Both halves matter and they fail separately: an outline that fed into the element's
    /// own size would fail the first, and one that fed only into the parent's content size would
    /// fail only the second.</para>
    /// </summary>
    [Fact]
    public void AnOutlineChangesNothingAboutLayout()
    {
        var (outlined, neighbour) = Row(b => b.Outline = new Outline(3f, Color.Red));
        var (bare, bareNeighbour) = Row(_ => { });

        Assert.Equal(bare.Layout.Width, outlined.Layout.Width);
        Assert.Equal(bare.Layout.Height, outlined.Layout.Height);
        Assert.Equal(bareNeighbour.Layout.AbsoluteX, neighbour.Layout.AbsoluteX);
    }

    /// <summary>
    /// A border, on the same box, does both of those things — which is the bug being fixed.
    ///
    /// <para>Without this the test above passes against an outline that does nothing at all,
    /// including one that never draws. It is the control: it says the geometry being asserted is
    /// genuinely reachable, and names a border as what reaches it. The numbers are Junction's
    /// symptom exactly — 3px of border on each side, 6px of neighbour movement.</para>
    /// </summary>
    [Fact]
    public void ABorderIsWhatAnOutlineIsInsteadOf()
    {
        var (bordered, shoved) = Row(b => b.BorderWidth(3f).BorderColor(Color.Red));
        var (bare, stayed) = Row(_ => { });

        Assert.Equal(bare.Layout.Width + 6f, bordered.Layout.Width);
        Assert.Equal(stayed.Layout.AbsoluteX + 6f, shoved.Layout.AbsoluteX);
    }

    /// <summary>An outline survives a rebuild. Asserting that an outlined element is still
    /// outlined would pass with the <c>PatchFrom</c> line deleted — the live element simply keeps
    /// what it already had — so this changes the value and watches it arrive.</summary>
    [Fact]
    public void AnOutlineSurvivesReconciliation()
    {
        var live = Box.New();
        live.Outline = new Outline(1f, Color.Red);

        var rebuilt = Box.New();
        rebuilt.Outline = new Outline(2f, Color.Blue, Offset: 3f);
        live.PatchFrom(rebuilt);

        Assert.Equal(new Outline(2f, Color.Blue, Offset: 3f), live.Outline);
    }

    /// <summary>Zero width and a transparent colour both mean "draw nothing", so a caller can
    /// switch the ring off by either without a branch at the call site.</summary>
    [Theory]
    [InlineData(0f, 255, false)]
    [InlineData(2f, 0, false)]
    [InlineData(2f, 255, true)]
    public void NothingIsDrawnWithoutBothAWidthAndAColour(float width, byte alpha, bool visible)
        => Assert.Equal(visible, new Outline(width, new Color(255, 0, 0, alpha)).IsVisible);

    /// <summary>A square corner stays square. Growing a zero radius would round a corner that
    /// was never round, so the ring would bulge away from the flat box it is meant to trace.</summary>
    [Fact]
    public void TheRingKeepsASquareCornerSquare()
    {
        Assert.Equal(new CornerRadius(0f), new CornerRadius(0f).Inflate(4f));
        Assert.Equal(new CornerRadius(9f), new CornerRadius(5f).Inflate(4f));
        Assert.Equal(new CornerRadius(1f, 0f, 0f, 0f), new CornerRadius(5f, 0f, 0f, 0f).Inflate(-4f));
    }
}
