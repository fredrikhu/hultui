using HultUI.Core.Builders;
using HultUI.Core.Elements;
using HultUI.Core.Layout;
using HultUI.Core.Styling;
using Xunit;

namespace HultUI.Core.Tests.Elements;

/// <summary>
/// <see cref="VirtualListElement{T}.IndexAfter"/>: where a cursor move lands, for the list's flow
/// and size, which only the list knows. The keys stay the app's.
/// </summary>
public class VirtualListIndexAfterTests
{
    private readonly LayoutEngine _layout = new(new StubMeasureContext());

    private VirtualListElement<string> List(int count, ListFlow flow, Func<string, int, float>? heights = null)
    {
        var vl = VirtualListElement<string>.New()
            .ItemHeight(40)
            .Items(Enumerable.Range(0, count).Select(i => $"Item {i}").ToList())
            .RenderWith(item => TextElement.New(item));
        if (heights is not null) vl.RowHeights(heights);
        if (flow == ListFlow.Columns)
        {
            vl.Flow(ListFlow.Columns, columnWidth: 100f);
            vl.ShowScrollbarX = false;
        }
        vl.Box.Width = 300;   // three whole columns
        vl.Box.Height = 200;  // five rows
        _layout.PerformLayout(vl, 300, 200);
        StyleResolver.Resolve(vl);
        vl.UpdateContentSize();
        _layout.PerformLayout(vl, 300, 200);
        return vl;
    }

    [Theory]
    [InlineData(12, ListMove.Up, 11)]
    [InlineData(12, ListMove.Down, 13)]
    [InlineData(12, ListMove.Left, 7)]
    [InlineData(12, ListMove.Right, 17)]
    [InlineData(3, ListMove.Left, 0)]       // the first column: to the first item
    [InlineData(47, ListMove.Right, 49)]    // the last column: to the last item
    [InlineData(12, ListMove.PageDown, 27)] // five rows × three whole columns
    [InlineData(20, ListMove.PageUp, 5)]
    [InlineData(12, ListMove.Home, 0)]
    [InlineData(12, ListMove.End, 49)]
    [InlineData(0, ListMove.Up, 0)]
    public void InColumns(int from, ListMove move, int to)
    {
        Assert.Equal(to, List(50, ListFlow.Columns).IndexAfter(from, move));
    }

    [Theory]
    [InlineData(12, ListMove.Up, 11)]
    [InlineData(12, ListMove.Down, 13)]
    [InlineData(12, ListMove.Left, 12)]
    [InlineData(12, ListMove.Right, 12)]
    [InlineData(12, ListMove.PageDown, 16)] // the rows a viewport shows less one: 5 - 1
    [InlineData(12, ListMove.PageUp, 8)]
    [InlineData(2, ListMove.PageUp, 0)]
    [InlineData(12, ListMove.Home, 0)]
    [InlineData(12, ListMove.End, 49)]
    [InlineData(49, ListMove.Down, 49)]
    public void InRows(int from, ListMove move, int to)
    {
        Assert.Equal(to, List(50, ListFlow.Rows).IndexAfter(from, move));
    }

    [Fact]
    public void AnEmptyList_HasNowhereToGo()
    {
        Assert.Equal(-1, List(0, ListFlow.Rows).IndexAfter(0, ListMove.Down));
        Assert.Equal(-1, List(0, ListFlow.Columns).IndexAfter(0, ListMove.Right));
    }

    [Fact]
    public void APage_OfUnequalRows_IsAViewportLessTheRowItLeaves()
    {
        // Rows 20, 40, 60, 20, 40, 60, …: from row 3 (at 120, 20 tall) a page down reaches
        // 120 + (200 - 20) = 300, which is row 8 (300–360); a page up from row 8 (60 tall) reaches
        // 300 - (200 - 60) = 160, row 4 (140–180).
        var vl = List(30, ListFlow.Rows, (_, i) => ((i % 3) + 1) * 20f);
        Assert.Equal(8, vl.IndexAfter(3, ListMove.PageDown));
        Assert.Equal(4, vl.IndexAfter(8, ListMove.PageUp));
    }
}
