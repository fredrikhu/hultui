using HultUI.Core.Accessibility;
using HultUI.Core.Builders;
using HultUI.Core.Components;
using HultUI.Core.Elements;
using HultUI.Core.Events;
using HultUI.Core.Layout;
using Xunit;

namespace HultUI.Headless.Tests;

/// <summary>
/// <see cref="TableView{T}"/> on screen: the header and the rows on one grid, sorting from the
/// header by pointer and keyboard with pinned groups, resizing by the grip, hiding columns from
/// the header's menu, and what a screen reader is told.
/// </summary>
public sealed class TableViewTests
{
    private sealed record Entry(string Name, long Size, bool Folder);

    private sealed class Page : Component
    {
        public readonly TableView<Entry> Table;
        public int Saved;
        public readonly Entry[] Entries =
        [
            new("..", 0, true), new("src", 0, true),
            new("b.txt", 300, false), new("a.txt", 100, false), new("c.txt", 200, false),
        ];

        public Page()
        {
            Table = new TableView<Entry>(
            [
                new TableColumn<Entry> { Id = "name", Header = "Name", Fill = 1, CanHide = false, Text = e => e.Name,
                    Compare = (a, b) => string.CompareOrdinal(a.Name, b.Name) },
                new TableColumn<Entry> { Id = "size", Header = "Size", Width = 90, MinWidth = 40, Align = ColumnAlign.End,
                    Text = e => e.Folder ? "" : $"{e.Size} B", Compare = (a, b) => a.Size.CompareTo(b.Size),
                    FirstSort = SortDirection.Descending },
                new TableColumn<Entry> { Id = "kind", Header = "Kind", Width = 70, Text = e => e.Folder ? "Folder" : "File" },
            ], MarkDirty)
            {
                Group = e => e.Name == ".." ? 0 : e.Folder ? 1 : 2,
                StateChanged = () => Saved++,
            };
        }

        public void Refresh() => MarkDirty();

        public override Element Build() =>
            Flex.Column(gap: 0).Children(Table.Header(), Table.Body(Entries).FlexGrow(1));
    }

    private sealed class Fixture : IDisposable
    {
        public readonly HeadlessSession Session = HeadlessSession.Create(600, 300);
        public readonly Page Page = new();
        public readonly ComponentHost Host;

        public Fixture()
        {
            Host = Page.Mount();
            Session.Window.SetRoot(Host);
            Session.PumpFrames(2);
        }

        public HeadlessElement Column(string id) => Session.Window.FindByAutomationId("column-" + id)!;

        public VirtualListElement<Entry> List => LayoutProbe.Descendants<VirtualListElement<Entry>>(Host).Single();

        public string[] Order => List.Items.Select(e => e.Name).ToArray();

        public void Dispose() => Session.Dispose();
    }

    [Fact]
    public void TheRowsCellsSitUnderTheirHeaders()
    {
        using var f = new Fixture();
        var header = LayoutProbe.Descendants<TableView<Entry>.HeaderCellElement>(f.Host).ToArray();
        var row = f.List.Children.OfType<TableView<Entry>.RowElement>().First();
        var cells = row.Children.ToArray();
        Assert.Equal(3, header.Length);
        Assert.Equal(3, cells.Length);
        for (int i = 0; i < 3; i++)
        {
            Assert.Equal(header[i].WindowBorderBox.X, cells[i].WindowBorderBox.X, 0.5f);
            Assert.Equal(header[i].WindowBorderBox.Width, cells[i].WindowBorderBox.Width, 0.5f);
        }
        Assert.Equal(90f, header[1].WindowBorderBox.Width, 0.5f);
    }

    [Fact]
    public void AHeaderPressSortsAndASecondReverses_WithTheGroupsKeptInPlace()
    {
        using var f = new Fixture();
        f.Column("size").Click();
        f.Session.PumpFrames(2);
        Assert.Equal(["..", "src", "b.txt", "c.txt", "a.txt"], f.Order);
        Assert.Equal("Size, sorted descending", f.Column("size").Name);
        Assert.Equal(AccessibilityRole.ColumnHeader, f.Column("size").Role);

        f.Column("size").Click();
        f.Session.PumpFrames(2);
        Assert.Equal(["..", "src", "a.txt", "c.txt", "b.txt"], f.Order);

        // An unsortable header is not a sort.
        f.Column("kind").Click();
        f.Session.PumpFrames(2);
        Assert.Equal("size", f.Page.Table.SortColumn);
        Assert.Equal(2, f.Page.Saved);
    }

    [Fact]
    public void TabReachesTheSortableHeaders_AndEnterSorts()
    {
        using var f = new Fixture();
        f.Session.Window.PressKey(KeyCode.Tab);
        Assert.True(f.Column("name").IsFocused);
        f.Session.Window.PressKey(KeyCode.Enter);
        f.Session.PumpFrames(2);
        Assert.Equal("name", f.Page.Table.SortColumn);
        f.Session.Window.PressKey(KeyCode.Tab);
        Assert.True(f.Column("size").IsFocused);   // "kind" cannot sort, so it is no stop

        f.Page.Table.HeadersTakeFocus = false;
        f.Page.Refresh();
        f.Session.PumpFrames(2);
        Assert.False(f.Column("name").Element.IsFocusable);
    }

    /// <summary>
    /// Size comes after the fill column, so the fixed columns after it pin its end edge: it
    /// resizes from its start edge, the divider it shares with Name, and a drag to the left
    /// makes it wider.
    /// </summary>
    [Fact]
    public void DraggingTheGripResizes_ItSettlesOnce_AndADoubleClickPutsItBack()
    {
        using var f = new Fixture();
        var size = f.Column("size").Element.WindowBorderBox;
        float x = size.X + 2, y = size.Y + size.Height / 2;
        f.Session.Window.MouseDown(x, y);
        f.Session.Window.MoveMouse(x + 20, y);
        f.Session.Window.MoveMouse(x - 40, y);
        f.Session.PumpFrames(2);
        Assert.Equal(0, f.Page.Saved);
        f.Session.Window.MouseUp(x - 40, y);
        f.Session.PumpFrames(2);

        var after = f.Column("size").Element.WindowBorderBox;
        Assert.Equal(130f, after.Width, 0.5f);
        Assert.Equal(size.Right, after.Right, 0.5f);   // the end stays; the divider moved
        Assert.Equal(1, f.Page.Saved);
        Assert.Null(f.Page.Table.SortColumn);           // the grip is not the header's press

        // Past its minimum, it stops.
        f.Session.Window.MouseDown(after.X + 2, y);
        f.Session.Window.MoveMouse(after.X + 300, y);
        f.Session.Window.MouseUp(after.X + 300, y);
        f.Session.PumpFrames(2);
        Assert.Equal(40f, f.Column("size").Element.WindowBorderBox.Width, 0.5f);

        var grip = LayoutProbe.Descendants<TableView<Entry>.GripElement>(f.Column("size").Element).Single();
        f.Session.Window.DoubleClick(grip);
        f.Session.PumpFrames(2);
        Assert.Equal(90f, f.Column("size").Element.WindowBorderBox.Width, 0.5f);
    }

    [Fact]
    public void TheHeadersMenuHidesAndShowsColumns_ButNotTheNameColumn()
    {
        using var f = new Fixture();
        f.Session.Window.RightClick(f.Column("kind").Element);
        f.Session.PumpFrames(2);
        var tree = Walk(f.Session.Window.AccessibilityTree).ToList();
        Assert.Contains(tree, n => n.Name == "Kind" && n.IsToggled == true);
        Assert.DoesNotContain(tree, n => n.Name == "Name" && n.IsToggled is not null);

        // The menu's rows are virtual: press where the tree says the row is.
        var item = tree.First(n => n.Name == "Kind" && n.IsToggled is not null).BoundingRect;
        f.Session.Window.MouseDown(item.X + item.Width / 2, item.Y + item.Height / 2);
        f.Session.Window.MouseUp(item.X + item.Width / 2, item.Y + item.Height / 2);
        f.Session.PumpFrames(2);
        Assert.Null(f.Session.Window.FindByAutomationId("column-kind"));
        var row = f.List.Children.OfType<TableView<Entry>.RowElement>().First();
        Assert.Equal(2, row.Children.Count);
        Assert.Contains("kind", f.Page.Table.State.Hidden);
    }

    [Fact]
    public void EachRowIsNamedByItsColumns()
    {
        using var f = new Fixture();
        var items = Walk(f.Session.Window.AccessibilityTree).Where(n => n.Role == AccessibilityRole.ListItem).ToList();
        Assert.Contains(items, n => n.Name == "b.txt, Size 300 B, Kind File");
        Assert.Contains(items, n => n.Name == "src, Kind Folder");
    }

    private static IEnumerable<AccessibilityNode> Walk(AccessibilityNode node) =>
        node.Children.SelectMany(Walk).Prepend(node);
}
