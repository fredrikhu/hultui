using HultUI.Core.Builders;
using HultUI.Core.Components;
using HultUI.Core.Elements;
using HultUI.Core.Layout;
using HultUI.Core.Primitives;
using HultUI.Core.Styling;

namespace PerfSuite;

static partial class Scenarios
{
    private static IEnumerable<Scenario> Virtual()
    {
        yield return new Scenario("virtual/list 100k wheel",
            "Wheel 3 notches through a 100,000-item VirtualList: rows realized as they come into view",
            ctx =>
            {
                var list = VirtualRows(ctx.Rng, 100_000);
                ctx.Mount(list.FlexGrow(1));
                return new Op
                {
                    Run = step => ctx.Window.Wheel(500, 380, 0, WheelDirection(step) * 3),
                    Check = ScrolledTo(() => list.ScrollOffsetY),
                };
            });

        yield return new Scenario("virtual/list 100k jump",
            "Jump a 100,000-item VirtualList to a far offset: a whole viewport of new rows",
            ctx =>
            {
                var list = VirtualRows(ctx.Rng, 100_000);
                ctx.Mount(list.FlexGrow(1));
                return new Op { Run = step => list.ScrollOffsetY = (step * 7_919 % 99_000) * 30f };
            });

        yield return new Scenario("virtual/list 100k replace items",
            "Assign a new 100,000-item list (a refresh from the model): visible rows re-realized",
            ctx =>
            {
                var a = Models(ctx.Rng, 100_000);
                var b = Models(ctx.Rng, 100_000);
                var list = VirtualListElement<RowItem>.New().ItemHeight(30).RenderWith(RenderVirtualRow).Items(a);
                ctx.Mount(list.FlexGrow(1));
                return new Op { Run = step => list.Items = step % 2 == 0 ? b : a };
            });

        yield return new Scenario("table/TableView 10k sort",
            "Re-sort a 10,000-row TableView (4 columns) by its text column, flipping direction each step",
            ctx =>
            {
                var table = new TableBench(Models(ctx.Rng, 10_000));
                ctx.Mount(table.Mount());
                return new Op
                {
                    Run = step => table.Table.SortBy("label", step % 2 == 0 ? SortDirection.Ascending : SortDirection.Descending),
                    Check = step => table.Table.SortColumn == "label"
                        && table.Table.SortDirection == (step % 2 == 0 ? SortDirection.Ascending : SortDirection.Descending)
                        ? null : "the table did not re-sort",
                };
            });

        yield return new Scenario("table/TableView 10k wheel",
            "Wheel through a 10,000-row TableView",
            ctx =>
            {
                var table = new TableBench(Models(ctx.Rng, 10_000));
                ctx.Mount(table.Mount());
                return new Op { Run = step => ctx.Window.Wheel(500, 400, 0, WheelDirection(step) * 3) };
            });

        yield return new Scenario("table/TableView 10k resize column",
            "Drag-resize a TableView column: every visible row re-laid out on the new tracks",
            ctx =>
            {
                var table = new TableBench(Models(ctx.Rng, 10_000));
                ctx.Mount(table.Mount());
                return new Op { Run = step => table.Table.SetWidth("label", 220 + 4 * (step % 40)) };
            });
    }

    private static List<RowItem> Models(Random rng, int count)
    {
        var items = new List<RowItem>(count);
        for (int i = 0; i < count; i++) items.Add(new RowItem(i + 1, Ui.Label(rng)));
        return items;
    }

    private static VirtualListElement<RowItem> VirtualRows(Random rng, int count)
        => VirtualListElement<RowItem>.New().ItemHeight(30).RenderWith(RenderVirtualRow).Items(Models(rng, count));

    private static Element RenderVirtualRow(RowItem m) => Ui.Row(m.Id, m.Label);
}

sealed class TableBench : Component
{
    private readonly List<RowItem> _items;

    public TableView<RowItem> Table { get; }

    public TableBench(List<RowItem> items)
    {
        _items = items;
        Table = new TableView<RowItem>(
        [
            new TableColumn<RowItem> { Id = "id", Header = "Id", Width = 80, Text = r => r.Id.ToString(),
                Compare = (a, b) => a.Id.CompareTo(b.Id), Align = ColumnAlign.End },
            new TableColumn<RowItem> { Id = "label", Header = "Label", Width = 260, Text = r => r.Label,
                Compare = (a, b) => string.CompareOrdinal(a.Label, b.Label) },
            new TableColumn<RowItem> { Id = "length", Header = "Length", Width = 90, Text = r => r.Label.Length.ToString(),
                Compare = (a, b) => a.Label.Length.CompareTo(b.Label.Length), Align = ColumnAlign.End },
            new TableColumn<RowItem> { Id = "notes", Header = "Notes", Fill = 1, Text = r => r.Label.ToUpperInvariant() },
        ], MarkDirty);
    }

    public override Element Build()
        => Flex.Column(gap: 0).FlexGrow(1).Children(Table.Header(), Table.Body(_items).FlexGrow(1));
}
