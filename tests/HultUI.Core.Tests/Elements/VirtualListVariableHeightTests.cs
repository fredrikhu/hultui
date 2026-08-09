using HultUI.Core.Builders;
using HultUI.Core.Elements;
using HultUI.Core.Events;
using HultUI.Core.Layout;
using HultUI.Core.Styling;
using Xunit;

namespace HultUI.Core.Tests.Elements;

/// <summary>
/// Rows of unequal height. Everything the uniform list got from arithmetic — total height,
/// which rows are visible, where each one sits, what the spacer holds up — now comes from a
/// prefix-sum index, and has to agree with the uniform answer whenever the rows happen to be
/// uniform.
/// </summary>
public class VirtualListVariableHeightTests
{
    private readonly LayoutEngine _layout = new(new StubMeasureContext());

    // Row i is (i % 3 + 1) * 20 tall: 20, 40, 60, 20, 40, 60, … — 120 per group of three.
    private static float Staircase(string item, int index) => ((index % 3) + 1) * 20f;

    private VirtualListElement<string> CreateList(
        int itemCount, float viewportHeight = 200f, Func<string, int, float>? heights = null)
    {
        var items = Enumerable.Range(0, itemCount).Select(i => $"Item {i}").ToList();
        var vl = VirtualListElement<string>.New()
            .ItemHeight(40f)
            .RowHeights(heights ?? Staircase)
            .Items(items)
            .RenderWith(item => TextElement.New(item));

        vl.Box.Width = 300;
        vl.Box.Height = viewportHeight;

        _layout.PerformLayout(vl, 300, viewportHeight);
        StyleResolver.Resolve(vl);
        vl.UpdateContentSize();
        _layout.PerformLayout(vl, 300, viewportHeight);
        return vl;
    }

    private static void Scroll(VirtualListElement<string> vl, float y, LayoutEngine layout)
    {
        vl.ScrollOffsetY = y;
        vl.UpdateContentSize();
        layout.PerformLayout(vl, 300, 200f);
    }

    [Fact]
    public void TheContentHeightIsTheSumOfTheRowHeights()
    {
        var vl = CreateList(30);

        // 10 groups of three rows, 120 per group.
        Assert.Equal(1200, vl.ContentHeight);
    }

    [Fact]
    public void AUniformHeightFunctionAgreesWithTheUniformList()
    {
        var variable = CreateList(100, heights: (_, _) => 40f);

        Assert.Equal(4000, variable.ContentHeight);
    }

    [Fact]
    public void EachRowIsGivenItsOwnHeight()
    {
        var vl = CreateList(30);
        var realized = vl.Children.Where(c => c is TextElement).Cast<TextElement>().ToList();

        Assert.NotEmpty(realized);
        // The first three realized rows follow the staircase.
        Assert.Equal(20f, realized[0].Box.Height);
        Assert.Equal(40f, realized[1].Box.Height);
        Assert.Equal(60f, realized[2].Box.Height);
    }

    [Fact]
    public void RowsStackWithoutOverlappingOrLeavingGaps()
    {
        var vl = CreateList(30);
        var rows = vl.Children.Where(c => c is TextElement).ToList();

        for (int i = 1; i < rows.Count; i++)
        {
            float prevBottom = rows[i - 1].Layout.AbsoluteBorderBox.Y + rows[i - 1].Layout.AbsoluteBorderBox.Height;
            float top = rows[i].Layout.AbsoluteBorderBox.Y;
            Assert.Equal(prevBottom, top, precision: 2);
        }
    }

    // The point of the prefix sum: scrolling to an arbitrary offset has to land on the row
    // that actually spans it, which no multiplication can work out when rows differ.
    [Fact]
    public void ScrollingLandsOnTheRowThatSpansTheOffset()
    {
        var vl = CreateList(30);

        // Rows 0,1,2 occupy 0–20, 20–60, 60–120. Offset 130 is inside row 3 (120–140).
        Scroll(vl, 130f, _layout);

        var first = vl.Children.OfType<TextElement>().First();
        // One row of overscan above, so the first realized row is row 2.
        Assert.Equal("Item 2", first.Content);
    }

    [Fact]
    public void TheSpacerHoldsUpExactlyTheRowsAboveTheViewport()
    {
        var vl = CreateList(30);
        Scroll(vl, 360f, _layout);   // three whole groups of 120

        var spacer = vl.Children[0];
        var firstRow = vl.Children.OfType<TextElement>().First();
        int firstIndex = int.Parse(firstRow.Content.Split(' ')[1]);

        // The spacer must equal the summed height of every row before the first realized one.
        float expected = Enumerable.Range(0, firstIndex).Sum(i => Staircase("", i));
        Assert.Equal(expected, spacer.Box.Height);
    }

    [Fact]
    public void OnlyTheRowsNearTheViewportAreRealized()
    {
        var vl = CreateList(300);   // 12000 tall

        int realized = vl.Children.Count(c => c is TextElement);

        // A 200-tall viewport over rows averaging 40 needs a handful, not 300.
        Assert.InRange(realized, 1, 20);
    }

    [Fact]
    public void ATallRowIsStillFoundWhenItAloneFillsTheViewport()
    {
        // One row taller than the viewport, surrounded by short ones.
        var vl = CreateList(10, heights: (_, i) => i == 5 ? 500f : 20f);

        Scroll(vl, 200f, _layout);   // rows 0..4 are 100 tall total, so this is inside row 5

        var contents = vl.Children.OfType<TextElement>().Select(t => t.Content).ToList();
        Assert.Contains("Item 5", contents);
    }

    [Fact]
    public void AZeroHeightRowStillOccupiesAPixel()
    {
        var vl = CreateList(5, heights: (_, _) => 0f);

        // Five rows that each claim nothing still have to be distinguishable by offset.
        Assert.Equal(5, vl.ContentHeight);
    }

    [Fact]
    public void ArrowKeysStepByTheHeightOfTheRowAtTheTop()
    {
        var vl = CreateList(30);
        vl.KeyboardScroll();

        // Row 0 is 20 tall, so one Down is 20.
        EventDispatcher.Dispatch(new KeyDownEvent { Key = KeyCode.Down }, vl);
        Assert.Equal(20f, vl.ScrollOffsetY);

        // Now row 1 (40 tall) is at the top.
        vl.UpdateContentSize();
        EventDispatcher.Dispatch(new KeyDownEvent { Key = KeyCode.Down }, vl);
        Assert.Equal(60f, vl.ScrollOffsetY);
    }

    [Fact]
    public void EndScrollsToTheRealBottom()
    {
        var vl = CreateList(30);
        vl.KeyboardScroll();

        EventDispatcher.Dispatch(new KeyDownEvent { Key = KeyCode.End }, vl);

        Assert.Equal(vl.ContentHeight - 200f, vl.ScrollOffsetY, precision: 2);
    }

    // A resize re-wraps every row, so the heights change while the items do not.
    [Fact]
    public void InvalidatingHeightsRecomputesThemWithoutTouchingTheItems()
    {
        float scale = 1f;
        var items = Enumerable.Range(0, 10).Select(i => $"Item {i}").ToList();
        var vl = VirtualListElement<string>.New()
            .ItemHeight(40f)
            .RowHeights((_, _) => 20f * scale)
            .Items(items)
            .RenderWith(item => TextElement.New(item));
        vl.Box.Width = 300; vl.Box.Height = 200;
        _layout.PerformLayout(vl, 300, 200);
        StyleResolver.Resolve(vl);
        vl.UpdateContentSize();
        Assert.Equal(200, vl.ContentHeight);

        scale = 2f;
        vl.InvalidateHeights();
        vl.UpdateContentSize();

        Assert.Equal(400, vl.ContentHeight);
    }
}
