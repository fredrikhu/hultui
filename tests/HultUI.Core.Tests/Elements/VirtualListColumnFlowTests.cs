using HultUI.Core.Builders;
using HultUI.Core.Elements;
using HultUI.Core.Events;
using HultUI.Core.Layout;
using HultUI.Core.Primitives;
using HultUI.Core.Styling;
using Xunit;

namespace HultUI.Core.Tests.Elements;

/// <summary>
/// A virtual list in <see cref="ListFlow.Columns"/>: items fill a column top to bottom, then the
/// next to the right, and the list scrolls sideways only — a file manager's Brief view. The
/// geometry is the list's own: rows per column from the height, the columns the viewport shows
/// realized, cells placed at their column and row.
/// </summary>
public class VirtualListColumnFlowTests
{
    private readonly LayoutEngine _layout = new(new StubMeasureContext());

    private static List<string> Items(int count) => Enumerable.Range(0, count).Select(i => $"Item {i}").ToList();

    private VirtualListElement<string> Columns(int count, float height = 200f, float width = 300f, bool bar = false,
        Func<string, Element>? template = null)
    {
        var vl = VirtualListElement<string>.New()
            .ItemHeight(40)
            .Items(Items(count))
            .RenderWith(template ?? (item => TextElement.New(item)))
            .Flow(ListFlow.Columns, columnWidth: 100f);
        vl.ShowScrollbarX = bar;
        vl.Box.Width = width;
        vl.Box.Height = height;
        Layout(vl, width, height);
        return vl;
    }

    private void Layout(VirtualListElement<string> vl, float width, float height)
    {
        _layout.PerformLayout(vl, width, height);
        StyleResolver.Resolve(vl);
        vl.UpdateContentSize();
        _layout.PerformLayout(vl, width, height);
    }

    /// <summary>The realized cells by the index of their item, from what each one shows.</summary>
    private static Dictionary<int, Element> Cells(VirtualListElement<string> vl)
    {
        var cells = new Dictionary<int, Element>();
        foreach (var child in vl.Children)
            if (child is TextElement { Content: var text } && text.StartsWith("Item ", StringComparison.Ordinal))
                cells[int.Parse(text[5..])] = child;
        return cells;
    }

    [Fact]
    public void RowsPerColumn_FollowTheHeight()
    {
        var vl = Columns(100, height: 200);   // 200 / 40
        Assert.Equal(5, vl.RowsPerColumn);
    }

    [Fact]
    public void TheHorizontalBar_TakesItsRoom_WhenTheColumnsOverflow()
    {
        var overflowing = Columns(100, height: 200, bar: true);
        Assert.Equal(4, overflowing.RowsPerColumn);   // (200 - bar) / 40

        var fitting = Columns(10, height: 200, bar: true);   // two columns of five fit in 300
        Assert.Equal(5, fitting.RowsPerColumn);
    }

    [Fact]
    public void ContentWidth_IsColumnsTimesTheColumnWidth()
    {
        var vl = Columns(23, height: 200);   // 5 rows: 5 columns
        Assert.Equal(500, vl.ContentWidth);
        Assert.Equal(200, vl.ContentHeight);
    }

    [Fact]
    public void OnlyTheVisibleColumnsAreRealized()
    {
        var vl = Columns(1_000_000, height: 200, width: 300);
        int visibleColumns = 3;
        Assert.True(vl.Children.Count <= 1 + vl.RowsPerColumn * (visibleColumns + 1),
            $"{vl.Children.Count} children for {visibleColumns} visible columns of {vl.RowsPerColumn}");
    }

    [Fact]
    public void CellsSitAtTheirColumnAndRow()
    {
        var vl = Columns(100, height: 200);
        var content = vl.Layout.AbsoluteContentBox;

        var cell = Cells(vl)[7];   // column 1, row 2

        Assert.Equal(content.X + 100, cell.Layout.AbsoluteX);
        Assert.Equal(content.Y + 80, cell.Layout.AbsoluteY);
        Assert.Equal(100, cell.Layout.AbsoluteBorderBox.Width);
        Assert.Equal(40, cell.Layout.AbsoluteBorderBox.Height);
    }

    [Fact]
    public void ACellsMargin_IsInsideItsPlace_ThroughEveryLayoutPass()
    {
        var vl = Columns(100, height: 200, template: item => TextElement.New(item).Margin(4));
        var content = vl.Layout.AbsoluteContentBox;

        // The layout pass alone, as a window resize that keeps the same columns runs it: the list
        // places no cell then, so the cells stand where the pass puts them.
        _layout.PerformLayout(vl, 300, 200);

        var cell = Cells(vl)[7];   // column 1, row 2
        Assert.Equal(content.X + 100 + 4, cell.Layout.AbsoluteX);
        Assert.Equal(content.Y + 80 + 4, cell.Layout.AbsoluteY);
        Assert.Equal(92, cell.Layout.AbsoluteBorderBox.Width);
        Assert.Equal(32, cell.Layout.AbsoluteBorderBox.Height);
    }

    [Fact]
    public void ScrollOffsetX_MovesTheWindow_AndScrollOffsetYStaysZero()
    {
        var vl = Columns(100, height: 200);

        vl.ScrollOffsetX = 250;
        vl.ScrollOffsetY = 100;
        Layout(vl, 300, 200);

        Assert.Equal(0, vl.ScrollOffsetY);
        Assert.Equal(10, Cells(vl).Keys.Min());   // column 2 is the first shown
        Assert.DoesNotContain(9, Cells(vl).Keys);
    }

    [Fact]
    public void AHeightChange_RePlacesKeptCells_WithoutMakingThemAgain()
    {
        int made = 0;
        var items = Items(100);
        var vl = VirtualListElement<string>.New()
            .ItemHeight(40)
            .Items(items)
            .RenderWith((item, selected) => { made++; return TextElement.New(item); })
            .Flow(ListFlow.Columns, columnWidth: 100f);
        vl.ShowScrollbarX = false;
        vl.Box.Width = 300;
        vl.Box.Height = 200;
        Layout(vl, 300, 200);
        var before = Cells(vl);
        int madeBefore = made;

        vl.Box.Height = 160;   // 4 rows a column
        vl.MarkLayoutDirty();
        Layout(vl, 300, 160);

        Assert.Equal(4, vl.RowsPerColumn);
        var after = Cells(vl);
        Assert.Equal(madeBefore, made);
        Assert.Same(before[7], after[7]);
        var content = vl.Layout.AbsoluteContentBox;
        Assert.Equal(content.X + 100, after[7].Layout.AbsoluteX);   // column 1, row 3
        Assert.Equal(content.Y + 120, after[7].Layout.AbsoluteY);
    }

    [Fact]
    public void ItemHeightFor_IsRefusedInColumns_InEitherOrder()
    {
        var columns = VirtualListElement<string>.New().Flow(ListFlow.Columns);
        Assert.Throws<InvalidOperationException>(() => columns.RowHeights((_, _) => 30f));

        var varied = VirtualListElement<string>.New().RowHeights((_, _) => 30f);
        Assert.Throws<InvalidOperationException>(() => varied.Flow = ListFlow.Columns);
    }

    [Fact]
    public void PatchFrom_CopiesTheFlowAndWidth_AndAFlowChangeMakesEveryRowAgain()
    {
        int made = 0;
        var items = Items(100);
        VirtualListElement<string> Build(ListFlow flow) => VirtualListElement<string>.New()
            .ItemHeight(40).Items(items)
            .RenderWith((item, selected) => { made++; return TextElement.New(item); })
            .Flow(flow, columnWidth: 120f);
        var vl = Build(ListFlow.Rows);
        vl.Box.Width = 300;
        vl.Box.Height = 200;
        Layout(vl, 300, 200);

        vl.PatchFrom(Build(ListFlow.Columns));
        Assert.Equal(ListFlow.Columns, vl.Flow);
        Assert.Equal(120, vl.ColumnWidth);
        int madeBefore = made;
        Layout(vl, 300, 200);
        Assert.Equal(vl.Children.Count - 1, made - madeBefore);
    }

    [Fact]
    public void KeyboardScroll_InColumns_MovesSideways_AndLetsUpAndDownGo()
    {
        var vl = Columns(100, height: 200);
        vl.KeyboardScroll();

        var right = new KeyDownEvent { Key = KeyCode.Right };
        EventDispatcher.Dispatch(right, vl);
        Assert.Equal(100, vl.ScrollOffsetX);
        Assert.True(right.StopPropagation);

        var down = new KeyDownEvent { Key = KeyCode.Down };
        EventDispatcher.Dispatch(down, vl);
        Assert.False(down.StopPropagation);
        Assert.Equal(100, vl.ScrollOffsetX);

        EventDispatcher.Dispatch(new KeyDownEvent { Key = KeyCode.End }, vl);
        Assert.Equal(vl.ContentWidth - 300, vl.ScrollOffsetX);
        EventDispatcher.Dispatch(new KeyDownEvent { Key = KeyCode.Home }, vl);
        Assert.Equal(0, vl.ScrollOffsetX);
    }

    [Fact]
    public void KeyboardScroll_InColumns_PagesByAViewportLessAColumn()
    {
        var vl = Columns(100, height: 200);   // 5 rows, 20 columns of 100 in a 300 px viewport
        vl.KeyboardScroll();

        EventDispatcher.Dispatch(new KeyDownEvent { Key = KeyCode.PageDown }, vl);
        Assert.Equal(200, vl.ScrollOffsetX);
        EventDispatcher.Dispatch(new KeyDownEvent { Key = KeyCode.PageDown }, vl);
        Assert.Equal(400, vl.ScrollOffsetX);
        EventDispatcher.Dispatch(new KeyDownEvent { Key = KeyCode.PageUp }, vl);
        Assert.Equal(200, vl.ScrollOffsetX);
    }

    [Fact]
    public void EndInColumns_ReachesItemsAddedSinceTheLastLayout()
    {
        var vl = Columns(100, height: 200);   // 5 rows: 20 columns
        vl.KeyboardScroll();
        vl.Items = Items(200);                // 40 columns, not laid out yet

        EventDispatcher.Dispatch(new KeyDownEvent { Key = KeyCode.End }, vl);

        Assert.Equal((40 * 100) - 300, vl.ScrollOffsetX);
    }

    [Fact]
    public void ItemRectAndIndexAfter_CountTheRowsTheNewItemsMake_BeforeLayout()
    {
        // 200 px holds 5 rows of 40; 10 items fit in 2 columns, 100 overflow and the 6 px bar leaves 4.
        var vl = Columns(10, height: 200, bar: true);
        Assert.Equal(5, vl.RowsPerColumn);

        vl.Items = Items(100);

        Assert.Equal(4, vl.RowsPerColumn);
        Assert.Equal(new Rect(200, 40, 100, 40), vl.ItemRect(9));
        Assert.Equal(13, vl.IndexAfter(9, ListMove.Right));
    }

    [Fact]
    public void StickyRowsAreRefusedInColumns()
    {
        var columns = VirtualListElement<string>.New().Flow(ListFlow.Columns);
        Assert.Throws<InvalidOperationException>(() => columns.StickyRows((_, i) => i == 0));

        var sticky = VirtualListElement<string>.New().StickyRows((_, i) => i == 0);
        Assert.Throws<InvalidOperationException>(() => sticky.Flow = ListFlow.Columns);
    }

    /// <summary>A list in columns of 100 items, 300 by 200, not laid out yet.</summary>
    private static VirtualListElement<string> Unlaid()
    {
        var vl = VirtualListElement<string>.New()
            .ItemHeight(40)
            .Items(Items(100))
            .RenderWith(item => TextElement.New(item))
            .Flow(ListFlow.Columns, columnWidth: 100f);
        vl.ShowScrollbarX = false;
        vl.Box.Width = 300;
        vl.Box.Height = 200;
        return vl;
    }

    [Fact]
    public void HorizontalRangeRevealBeforeLayoutIsNotHeld()
    {
        // The view's sideways reveal has no width to work from before layout and is dropped, as
        // ScrollViewElement does for any view; the list holds a reveal by index until it has one.
        var range = Unlaid();
        range.ScrollRangeIntoViewHorizontal(1500, 100);
        Layout(range, 300, 200);
        Assert.Equal(0, range.ScrollOffsetX);

        var index = Unlaid();
        index.ScrollIndexIntoView(77);   // 5 rows a column: column 15, 1500 to 1600
        Layout(index, 300, 200);
        Assert.Equal(1600 - 300, index.ScrollOffsetX);
    }
}
