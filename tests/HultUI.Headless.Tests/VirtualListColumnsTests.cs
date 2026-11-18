// UiaHostBridge is marked for Windows, but what is asked of it here reads only the element tree,
// so it runs anywhere.
#pragma warning disable CA1416
using HultUI.Core.Accessibility;
using HultUI.Core.Builders;
using HultUI.Core.Components;
using HultUI.Core.Elements;
using HultUI.Core.Events;
using HultUI.Core.Primitives;
using HultUI.Core.Reactivity;
using SkiaSharp;
using Xunit;
using TextB = HultUI.Core.Builders.Text;

namespace HultUI.Headless.Tests;

/// <summary>
/// A virtual list in columns in a real window: the wheel moves it a column a notch, a page around
/// it still gets the wheel first, the pointer finds the cell it is over, a reveal lands, and
/// assistive technology sees every item where it is drawn.
/// </summary>
public class VirtualListColumnsTests
{
    private const float ItemHeight = 24f;
    private const float ColumnWidth = 120f;

    private static List<string> Names(int count) => Enumerable.Range(0, count).Select(i => $"item {i}").ToList();

    private static VirtualListElement<string> Columns(int count, Action<string>? clicked = null)
    {
        return VirtualListElement<string>.New()
            .ItemHeight(ItemHeight)
            .Items(Names(count))
            .AccessibleItemLabel(t => t)
            .RenderWith(t =>
            {
                var cell = Flex.Row().Children(TextB.New(t).FontSize(12f));
                if (clicked is not null) cell.OnClick(_ => clicked(t));
                return cell;
            })
            .Flow(ListFlow.Columns, ColumnWidth);
    }

    private static VirtualListElement<string> Mount(HeadlessSession session, VirtualListElement<string> list)
    {
        session.Window.SetRoot(Flex.Column().FlexGrow(1).Children(list.FlexGrow(1)));
        session.PumpFrames(2);
        return list;
    }

    [Fact]
    public void OneVerticalWheelNotch_MovesExactlyOneColumn()
    {
        using var session = HeadlessSession.Create(600, 300);
        var list = Mount(session, Columns(1_000));

        session.Window.Wheel(300, 150, 0, -1);
        session.PumpFrame();

        Assert.Equal(ColumnWidth, list.ScrollOffsetX);
        Assert.Equal(0, list.ScrollOffsetY);
    }

    [Fact]
    public void InsideAPageThatCanStillScrollDown_ThePageTakesTheWheel()
    {
        using var session = HeadlessSession.Create(600, 300);
        var list = Columns(1_000).Height(200f);
        var page = ScrollView.New().FlexGrow(1).Children(
            Flex.Column().FlexShrink(0).Children(list, Box.New().Height(1_000f).FlexShrink(0)));
        session.Window.SetRoot(Flex.Column().FlexGrow(1).Children(page));
        session.PumpFrames(2);

        session.Window.Wheel(300, 100, 0, -1);
        session.PumpFrame();

        Assert.True(page.ScrollOffsetY > 0, "the page did not scroll");
        Assert.Equal(0, list.ScrollOffsetX);
    }

    [Fact]
    public void MiddleClickAutoscroll_MovesSideways()
    {
        using var session = HeadlessSession.Create(600, 300);
        var list = Columns(1_000);
        list.MiddleClickAutoScroll = true;
        Mount(session, list);

        session.Window.MouseDown(200, 150, MouseButton.Middle);
        session.Window.MoveMouse(240, 150);
        for (int i = 0; i < 30; i++) session.PumpFrame();
        session.Window.MouseUp(240, 150, MouseButton.Middle);

        Assert.True(list.ScrollOffsetX > 0, "autoscroll did not move the columns");
        Assert.Equal(0, list.ScrollOffsetY);
    }

    [Fact]
    public void AClickOnACellInTheThirdColumn_HitsThatItem()
    {
        using var session = HeadlessSession.Create(600, 300);
        string? clicked = null;
        var list = Mount(session, Columns(1_000, t => clicked = t));
        var view = list.WindowContentBox;
        int rows = list.RowsPerColumn;

        session.Window.Click(view.X + 2 * ColumnWidth + 10, view.Y + 3 * ItemHeight + 5);
        session.PumpFrame();

        Assert.Equal($"item {2 * rows + 3}", clicked);
    }

    /// <summary>A pane that lists what it is given and reveals its cursor after each build.</summary>
    private sealed class Pane : Component
    {
        public List<string> Items = Names(10);
        public int Cursor;
        public readonly Ref<VirtualListElement<string>> List = new();

        public override Element Build() =>
            Flex.Column().FlexGrow(1).Children(Columns(0).Items(Items).FlexGrow(1).WithRef(List));

        public void Grow(int count, int cursor)
        {
            Items = Names(count);
            Cursor = cursor;
            MarkDirty();
        }

        protected internal override void OnAfterBuild() => List.Current?.ScrollIndexIntoView(Cursor);
    }

    [Fact]
    public void ARevealFromOnAfterBuild_InTheFrameTheItemsGrew_Lands()
    {
        using var session = HeadlessSession.Create(600, 300);
        var pane = new Pane();
        session.Application.SetRoot(pane.Mount());
        session.PumpFrames(3);

        pane.Grow(1_000, 500);
        session.PumpFrames(2);

        var list = pane.List.Current!;
        var cell = list.ItemRect(500);
        float left = cell.X - list.ScrollOffsetX;
        Assert.True(left >= 0 && left + cell.Width <= list.WindowContentBox.Width + 0.5f,
            $"item 500's column is at {left}, outside the {list.WindowContentBox.Width} px viewport");
    }

    [Fact]
    public void ARevealFromOnAfterBuild_Lands_WhenTheBarTakesARowInTheSameFrame()
    {
        // 288 px holds 12 rows of 24 exactly; once the columns overflow, the bar's room leaves 11,
        // so the reveal must count the rows the items make now, not the rows the last pass left.
        using var session = HeadlessSession.Create(600, 288);
        var pane = new Pane();
        session.Application.SetRoot(pane.Mount());
        session.PumpFrames(3);
        var list = pane.List.Current!;
        int rowsBefore = list.RowsPerColumn;

        pane.Grow(1_000, 500);
        session.PumpFrames(2);

        Assert.Equal(rowsBefore - 1, list.RowsPerColumn);
        var cell = list.ItemRect(500);
        Assert.Equal(500 / list.RowsPerColumn * ColumnWidth, cell.X);
        float left = cell.X - list.ScrollOffsetX;
        Assert.True(left >= 0 && left + cell.Width <= list.WindowContentBox.Width + 0.5f,
            $"item 500's column is at {left}, outside the {list.WindowContentBox.Width} px viewport");
    }

    [Fact]
    public void FifteenHundredItems_PublishFifteenHundredListItems_WithColumnBounds()
    {
        using var session = HeadlessSession.Create(600, 300);
        var bridge = session.Window.InstallAccessibilityBridge();
        var list = Mount(session, Columns(1_500));
        session.PumpFrame(TimeSpan.FromMilliseconds(120));

        var node = Find(bridge.CurrentTree!, list.Id)!;
        Assert.Equal(1_500, node.Children.Count);
        Assert.All(node.Children, c => Assert.Equal(AccessibilityRole.ListItem, c.Role));
        var view = list.ScreenContentBox;
        int rows = list.RowsPerColumn;
        int index = rows + 2;   // column 1, row 2
        var bounds = node.Children[index].BoundingRect;
        Assert.Equal(view.X + ColumnWidth, bounds.X, 0.5f);
        Assert.Equal(view.Y + 2 * ItemHeight, bounds.Y, 0.5f);
        Assert.Equal(ColumnWidth, bounds.Width, 0.5f);
    }

    [Fact]
    public void TheUiaHost_AgreesWithTheDrawnCells()
    {
        using var session = HeadlessSession.Create(600, 300);
        var window = session.Window.Window;
        var host = new UiaHostBridge(window, new AccessibilityActionRouter(window));
        var drawn = new Dictionary<string, Element>();
        var list = VirtualListElement<string>.New()
            .ItemHeight(ItemHeight)
            .Items(Names(1_000))
            .RenderWith(t => drawn[t] = Flex.Row().Children(TextB.New(t).FontSize(12f)))
            .Flow(ListFlow.Columns, ColumnWidth);
        Mount(session, list);
        list.ScrollOffsetX = 2 * ColumnWidth;
        session.PumpFrame();
        int rows = list.RowsPerColumn;

        foreach (int index in new[] { 2 * rows, 2 * rows + 3, 4 * rows + 1 })
        {
            Assert.True(host.TryGetItemLayout(list.Id, index, out var rect));
            var cell = drawn[$"item {index}"].ScreenBorderBox;
            Assert.Equal(cell.X, rect.X, 0.5f);
            Assert.Equal(cell.Y, rect.Y, 0.5f);
            Assert.False(host.GetItemOffscreen(list.Id, index));
        }
        Assert.True(host.GetItemOffscreen(list.Id, 0));               // scrolled out to the left
        Assert.True(host.GetItemOffscreen(list.Id, 20 * rows));       // far to the right
    }

    /// <summary>Item <paramref name="index"/>'s cell on screen, from <see cref="VirtualListElement{T}.ItemRect"/>.</summary>
    private static Rect OnScreen(VirtualListElement<string> list, int index)
    {
        var view = list.ScreenContentBox;
        var r = list.ItemRect(index);
        return new Rect(view.X + r.X - list.ScrollOffsetX, view.Y + r.Y - list.ScrollOffsetY, r.Width, r.Height);
    }

    private static void AssertBox(Rect expected, Rect actual)
    {
        Assert.Equal(expected.X, actual.X, 0.5f);
        Assert.Equal(expected.Y, actual.Y, 0.5f);
        Assert.Equal(expected.Width, actual.Width, 0.5f);
        Assert.Equal(expected.Height, actual.Height, 0.5f);
    }

    [Fact]
    public void ItemBoundsInColumnsHoldInEveryReader()
    {
        using var session = HeadlessSession.Create(600, 300);
        var bridge = session.Window.InstallAccessibilityBridge();
        var actions = session.Window.InstallActionRouter();
        var list = Mount(session, Columns(100));
        session.PumpFrame(TimeSpan.FromMilliseconds(120));
        int rows = list.RowsPerColumn;
        int index = rows + 2;   // the second column, row 2

        // The tree as built, read from the node's own bounds.
        AssertBox(OnScreen(list, index), Find(bridge.CurrentTree!, list.Id)!.Children[index].BoundingRect);

        // Refreshed in place after the list moved sideways.
        list.ScrollOffsetX = ColumnWidth / 2;
        session.PumpFrame(TimeSpan.FromMilliseconds(120));
        AssertBox(OnScreen(list, index), Find(bridge.CurrentTree!, list.Id)!.Children[index].BoundingRect);

        // A reader's scroll-into-view of an item three columns right of the viewport shows its column.
        int columnsShown = (int)(list.WindowContentBox.Width / ColumnWidth);
        int far = (columnsShown + 3) * rows;
        actions.ScrollElementIntoView(VirtualItemId.For(list.Id, far));
        session.PumpFrame(TimeSpan.FromMilliseconds(120));
        var cell = list.ItemRect(far);
        float left = cell.X - list.ScrollOffsetX;
        Assert.True(left >= 0 && left + cell.Width <= list.WindowContentBox.Width + 0.5f,
            $"item {far}'s column is at {left}, outside the {list.WindowContentBox.Width} px viewport");
    }

    [Fact]
    public void ItemBoundsInColumns_HoldForAListServedOnDemand()
    {
        using var session = HeadlessSession.Create(600, 300);
        var bridge = session.Window.InstallAccessibilityBridge();
        var list = Mount(session, Columns(3_000));   // over 2,000 items: served on demand
        list.ScrollOffsetX = 3 * ColumnWidth;
        session.PumpFrame(TimeSpan.FromMilliseconds(120));
        var node = Find(bridge.CurrentTree!, list.Id)!;
        Assert.Empty(node.Children);
        int rows = list.RowsPerColumn;

        int shown = 4 * rows + 1;
        Assert.True(node.TryGetServedItemBox(shown, out var box, out bool offscreen));
        AssertBox(OnScreen(list, shown), box);
        Assert.False(offscreen);
        Assert.True(node.TryGetServedItemBox(1, out _, out bool left));
        Assert.True(left);   // scrolled out to the left: its row is on screen, its column is not
    }

    [Fact]
    public void RowsWiderThanTheList_AreOnScreenForReaders_WhenScrolledSideways()
    {
        using var session = HeadlessSession.Create(400, 300);
        var bridge = session.Window.InstallAccessibilityBridge();
        var actions = session.Window.InstallActionRouter();
        var list = Mount(session, VirtualListElement<string>.New()
            .ItemHeight(ItemHeight).Items(Names(200)).AccessibleItemLabel(t => t)
            .RenderWith(t => TextB.New(t).FontSize(12f)).RowWidth(1200));
        list.ScrollOffsetX = 800;
        session.PumpFrame(TimeSpan.FromMilliseconds(120));

        // Built after the sideways scroll: a row on screen spans the content, not the viewport.
        var node = Find(bridge.CurrentTree!, list.Id)!;
        Assert.False(node.Children[3].IsOffscreen);
        AssertBox(OnScreen(list, 3), node.Children[3].BoundingRect);
        Assert.Equal(1200, node.Children[3].BoundingRect.Width, 0.5f);

        // A reader's scroll-into-view of a row below the viewport scrolls down to it.
        actions.ScrollElementIntoView(VirtualItemId.For(list.Id, 150));
        session.PumpFrame(TimeSpan.FromMilliseconds(120));
        float top = list.ItemRect(150).Y - list.ScrollOffsetY;
        Assert.True(top >= 0 && top + ItemHeight <= list.WindowContentBox.Height + 0.5f,
            $"row 150 is at {top}, outside the {list.WindowContentBox.Height} px viewport");
    }

    [Fact]
    public void TheSelectionOverlay_SpansARowWiderThanTheList()
    {
        using var session = HeadlessSession.Create(400, 300);
        var mark = Flex.Row().Width(16).Height(16);
        var list = Mount(session, VirtualListElement<string>.New()
            .ItemHeight(ItemHeight).Items(Names(200)).RenderWith(t => TextB.New(t).FontSize(12f))
            .RowWidth(1200).SelectedIndex(3).SelectionOverlay(mark));
        list.ScrollOffsetX = 800;
        session.PumpFrame(TimeSpan.FromMilliseconds(120));

        // The overlay's content stands against the row's end, on screen at the viewport's right.
        var box = mark.WindowBorderBox;
        var view = list.WindowContentBox;
        Assert.True(box.X >= view.X && box.Right <= view.Right + 0.5f,
            $"the overlay's content is at {box.X}–{box.Right}, outside the viewport {view.X}–{view.Right}");
    }

    private static readonly Color Selected = new(40, 90, 200);

    [Fact]
    public void SelectionHighlightIsDrawnInItsColumn()
    {
        using var session = HeadlessSession.Create(600, 300);
        var list = Mount(session, Columns(1_000).SelectionHighlight(Selected));
        int rows = list.RowsPerColumn;
        list.SelectedIndex = rows + 3;   // the second column, row 3
        session.PumpFrame();

        using var image = session.Window.CapturePixels();
        using var shot = SKBitmap.FromImage(image);
        var view = list.WindowContentBox;
        // Clear of the text, at the right of each cell.
        SKColor At(int column, int row) =>
            shot.GetPixel((int)(view.X + (column + 1) * ColumnWidth - 6), (int)(view.Y + row * ItemHeight + ItemHeight / 2));
        var fill = new SKColor(Selected.R, Selected.G, Selected.B, Selected.A);
        Assert.Equal(fill, At(1, 3));
        Assert.NotEqual(fill, At(0, 3));   // the same row in the first column
        Assert.NotEqual(fill, At(1, 2));   // the cell above it
    }

    [Fact]
    public void InvalidateRowRemakesOneRowInColumns()
    {
        using var session = HeadlessSession.Create(600, 300);
        int made = 0;
        var cells = new Dictionary<string, Element>();
        var list = VirtualListElement<string>.New()
            .ItemHeight(ItemHeight)
            .Items(Names(1_000))
            .RenderWith((t, _) =>
            {
                made++;
                return cells[t] = Flex.Row().Children(TextB.New(t).FontSize(12f));
            })
            .Flow(ListFlow.Columns, ColumnWidth);
        Mount(session, list);
        int rows = list.RowsPerColumn;
        int index = rows + 2;
        made = 0;

        list.InvalidateRow(index);
        session.PumpFrame();

        Assert.Equal(1, made);
        var drawn = cells[$"item {index}"].ScreenBorderBox;
        AssertBox(OnScreen(list, index), drawn);
    }

    private static AccessibilityNode? Find(AccessibilityNode node, ElementId id)
    {
        if (node.ElementId == id) return node;
        foreach (var child in node.Children)
            if (Find(child, id) is { } found) return found;
        return null;
    }
}
