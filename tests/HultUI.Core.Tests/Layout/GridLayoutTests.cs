using HultUI.Core.Builders;
using HultUI.Core.Elements;
using HultUI.Core.Layout;
using HultUI.Core.Primitives;
using HultUI.Core.Rendering;
using HultUI.Core.Styling;
using Xunit;

namespace HultUI.Core.Tests.Layout;

public class GridLayoutTests
{
    private readonly LayoutEngine _engine = new(new StubMeasureContext());

    private void Layout(Element root, float width = 800, float height = 600) =>
        _engine.PerformLayout(root, width, height);

    private static BoxElement Cell(int row, int col) =>
        Box.New().GridCell(row, col);

    [Fact]
    public void FixedColumns_AssignWidthsAndPositions()
    {
        var root = Grid.Of("100px 200px 50px").Children(
            Cell(0, 0),
            Cell(0, 1),
            Cell(0, 2));
        Layout(root, 800, 600);

        Assert.Equal(100, root.Children[0].Layout.Width);
        Assert.Equal(0, root.Children[0].Layout.AbsoluteX);

        Assert.Equal(200, root.Children[1].Layout.Width);
        Assert.Equal(100, root.Children[1].Layout.AbsoluteX);

        Assert.Equal(50, root.Children[2].Layout.Width);
        Assert.Equal(300, root.Children[2].Layout.AbsoluteX);
    }

    [Fact]
    public void PercentColumns_ResolveAgainstContainerContent()
    {
        var root = Grid.Of("25% 75%").Width(400).Children(
            Cell(0, 0),
            Cell(0, 1));
        Layout(root, 800, 600);

        Assert.Equal(100, root.Children[0].Layout.Width);
        Assert.Equal(300, root.Children[1].Layout.Width);
    }

    [Fact]
    public void FrTracks_DistributeRemainingSpace()
    {
        // Container 600px wide. 200px fixed + 1fr + 2fr.
        // Remaining = 400px → 1fr=133.33, 2fr=266.67
        var root = Grid.Of("200px 1fr 2fr").Width(600).Children(
            Cell(0, 0),
            Cell(0, 1),
            Cell(0, 2));
        Layout(root, 800, 600);

        Assert.Equal(200, root.Children[0].Layout.Width);
        Assert.InRange(root.Children[1].Layout.Width, 133.0f, 134.0f);
        Assert.InRange(root.Children[2].Layout.Width, 266.0f, 267.0f);
    }

    [Fact]
    public void FrTracks_CollapseWhenNoRemainingSpace()
    {
        var root = Grid.Of("400px 1fr").Width(400).Children(
            Cell(0, 0),
            Cell(0, 1));
        Layout(root, 800, 600);

        Assert.Equal(400, root.Children[0].Layout.Width);
        Assert.Equal(0, root.Children[1].Layout.Width);
    }

    [Fact]
    public void AutoColumn_SizesToContent()
    {
        var labelChild = Text.New("Hello"); // 5 chars * 7px = 35px in StubMeasureContext
        labelChild.GridItem.Row = 0;
        labelChild.GridItem.Column = 0;

        var root = Grid.Of("auto 1fr").Width(400).Children(
            labelChild,
            Cell(0, 1));
        Layout(root, 800, 600);

        Assert.InRange(root.Children[0].Layout.Width, 34f, 36f);
        // The 1fr column gets the remaining ~365px
        Assert.InRange(root.Children[1].Layout.Width, 364f, 366f);
    }

    [Fact]
    public void AutoColumn_TakesMaxOfAllCellsInColumn()
    {
        var short_ = Text.New("ab");      // 14px
        var long_ = Text.New("abcdefgh"); // 56px
        short_.GridItem.Row = 0; short_.GridItem.Column = 0;
        long_.GridItem.Row = 1; long_.GridItem.Column = 0;

        var root = Grid.Of("auto", "auto auto").Children(short_, long_);
        Layout(root, 800, 600);

        // Both cells in column 0 should occupy the column's max-content width
        Assert.InRange(root.Children[0].Layout.Width, 55f, 57f);
        Assert.InRange(root.Children[1].Layout.Width, 55f, 57f);
    }

    [Fact]
    public void Rows_DefaultAutoSize_FromCellHeights()
    {
        var a = Box.New().Height(40).GridCell(0, 0);
        var b = Box.New().Height(80).GridCell(1, 0);
        var root = Grid.Of("100px", "auto auto").Children(a, b);
        Layout(root, 800, 600);

        Assert.Equal(0, a.Layout.AbsoluteY);
        Assert.Equal(40, b.Layout.AbsoluteY);
        Assert.Equal(80, b.Layout.Height);
    }

    [Fact]
    public void RowGap_AppliedBetweenRowsNotAtEdges()
    {
        var a = Box.New().Height(40).GridCell(0, 0);
        var b = Box.New().Height(40).GridCell(1, 0);
        var root = Grid.Of("100px", "auto auto").Children(a, b);
        root.Grid.RowGap = 12;
        Layout(root, 800, 600);

        Assert.Equal(0, a.Layout.AbsoluteY);
        Assert.Equal(52, b.Layout.AbsoluteY); // 40 + 12 gap
    }

    [Fact]
    public void ColumnGap_AppliedBetweenColumnsNotAtEdges()
    {
        var a = Box.New().Width(50).Height(20).GridCell(0, 0);
        var b = Box.New().Width(50).Height(20).GridCell(0, 1);
        var c = Box.New().Width(50).Height(20).GridCell(0, 2);
        var root = Grid.Of("50px 50px 50px").Children(a, b, c);
        root.Grid.ColumnGap = 8;
        Layout(root, 800, 600);

        Assert.Equal(0, a.Layout.AbsoluteX);
        Assert.Equal(58, b.Layout.AbsoluteX);
        Assert.Equal(116, c.Layout.AbsoluteX);
    }

    [Fact]
    public void Gap_ConstructorParamSetsBothAxes()
    {
        var root = Grid.Of("50px 50px", "20px 20px", gap: 10).Children(
            Cell(0, 0), Cell(0, 1),
            Cell(1, 0), Cell(1, 1));
        Layout(root, 800, 600);

        Assert.Equal(60, root.Children[1].Layout.AbsoluteX);  // col gap
        Assert.Equal(30, root.Children[2].Layout.AbsoluteY);  // row gap
    }

    [Fact]
    public void Stretch_IsDefault_ChildFillsCell()
    {
        var root = Grid.Of("200px", "100px").Children(
            Box.New().GridCell(0, 0));  // no explicit width/height
        Layout(root, 800, 600);

        Assert.Equal(200, root.Children[0].Layout.Width);
        Assert.Equal(100, root.Children[0].Layout.Height);
    }

    [Fact]
    public void JustifySelfStart_PlacesChildAtCellStart()
    {
        var child = Box.New().Width(50).Height(20).GridCell(0, 0).JustifySelf(JustifySelf.Start);
        var root = Grid.Of("200px", "100px").Children(child);
        Layout(root, 800, 600);

        Assert.Equal(0, child.Layout.AbsoluteX);
        Assert.Equal(50, child.Layout.Width);
    }

    [Fact]
    public void JustifySelfEnd_PlacesChildAtCellEnd()
    {
        var child = Box.New().Width(50).Height(20).GridCell(0, 0).JustifySelf(JustifySelf.End);
        var root = Grid.Of("200px", "100px").Children(child);
        Layout(root, 800, 600);

        Assert.Equal(150, child.Layout.AbsoluteX);
    }

    [Fact]
    public void JustifySelfCenter_CentersChildHorizontally()
    {
        var child = Box.New().Width(50).Height(20).GridCell(0, 0).JustifySelf(JustifySelf.Center);
        var root = Grid.Of("200px", "100px").Children(child);
        Layout(root, 800, 600);

        Assert.Equal(75, child.Layout.AbsoluteX);
    }

    [Fact]
    public void AlignSelfCenter_CentersChildVertically()
    {
        var child = Box.New().Width(50).Height(20).GridCell(0, 0).AlignSelf(AlignSelf.Center);
        var root = Grid.Of("200px", "100px").Children(child);
        Layout(root, 800, 600);

        Assert.Equal(40, child.Layout.AbsoluteY);
    }

    [Fact]
    public void ImplicitRow_CreatedWhenCellExceedsTemplate()
    {
        // Template has 1 row; place cell in row 2 → implicit auto rows fill rows 1 and 2
        var a = Box.New().Width(50).Height(20).GridCell(0, 0);
        var b = Box.New().Width(50).Height(30).GridCell(2, 0);
        var root = Grid.Of("50px", "auto").Children(a, b);
        Layout(root, 800, 600);

        Assert.Equal(0, a.Layout.AbsoluteY);
        Assert.Equal(20, b.Layout.AbsoluteY); // row 1 has no children → height 0
        Assert.Equal(30, b.Layout.Height);
    }

    [Fact]
    public void ContainerContentSize_UsedByAncestorMeasurement()
    {
        // Parent uses AlignItems.FlexStart so the grid is NOT stretched horizontally;
        // its content size should then drive its outer dimensions.
        var root = Flex.Column().AlignItems(AlignItems.FlexStart).Children(
            Grid.Of("60px 60px", "30px 30px", gap: 4).Children(
                Cell(0, 0), Cell(0, 1),
                Cell(1, 0), Cell(1, 1)));
        Layout(root, 800, 600);

        var grid = root.Children[0];
        Assert.Equal(124, grid.Layout.Width);  // 60 + 4 + 60
        Assert.Equal(64, grid.Layout.Height);  // 30 + 4 + 30
    }

    [Fact]
    public void NoGrid_FlexLayoutUnchanged()
    {
        // Sanity: BoxElement without grid template uses flex (regression guard)
        var root = Flex.Row().Children(
            Box.New().Width(100).Height(50),
            Box.New().Width(200).Height(50));
        Layout(root);

        Assert.Equal(0, root.Children[0].Layout.AbsoluteX);
        Assert.Equal(100, root.Children[1].Layout.AbsoluteX);
    }

    [Fact]
    public void AutoTrack_HitsMeasureCacheOnSecondLayoutPass()
    {
        var counter = new CountingMeasureContext();
        var engine = new LayoutEngine(counter);

        var root = Grid.Of("auto auto", "auto auto").Children(
            Text.New("aa").GridCell(0, 0),
            Text.New("bb").GridCell(0, 1),
            Text.New("cc").GridCell(1, 0),
            Text.New("dd").GridCell(1, 1));

        engine.PerformLayout(root, 400, 200);
        int first = counter.Count;
        engine.PerformLayout(root, 400, 200);
        int second = counter.Count - first;

        // Second pass should not re-measure anything (clean tree, identical constraints)
        Assert.Equal(0, second);
    }

    private sealed class CountingMeasureContext : IMeasureContext
    {
        private readonly StubMeasureContext _inner = new();
        public int Count;
        public Size MeasureText(string text, ResolvedInheritedStyle style, float maxWidth, int? maxLines,
            TextOverflow overflow = TextOverflow.Clip)
        {
            Count++;
            return _inner.MeasureText(text, style, maxWidth, maxLines, overflow);
        }
    }
}
