using HultUI.Core.Builders;
using HultUI.Core.Events;
using Xunit;

namespace HultUI.Core.Tests.Builders;

/// <summary>
/// A table the app sorts (<see cref="TableSortMode.External"/>): it never sorts, a press asks the
/// app with the keys the table would have used, and the header shows only the keys the app sets
/// once its rows are in that order. A table that sorts its own rows is as it was.
/// </summary>
public class TableViewExternalSortTests
{
    private sealed record File(string Name, long Size, int Order);

    private static readonly File[] Files =
    [
        new("b.txt", 300, 0), new("a.txt", 100, 1), new("c.txt", 100, 2), new("d.txt", 200, 3),
    ];

    private static readonly TableSortKey NameAsc = new("name", SortDirection.Ascending);
    private static readonly TableSortKey NameDesc = new("name", SortDirection.Descending);
    private static readonly TableSortKey SizeAsc = new("size", SortDirection.Ascending);
    private static readonly TableSortKey SizeDesc = new("size", SortDirection.Descending);

    /// <summary>"name" sorts only where the app sorts (CanSort, no Compare); "size" has a Compare
    /// that throws, so a table that compares fails; "kind" cannot sort.</summary>
    private static (TableView<File> Table, List<string> Log, List<TableSortRequest> Requests) Make(
        TableSortMode mode = TableSortMode.External)
    {
        var log = new List<string>();
        var requests = new List<TableSortRequest>();
        var table = new TableView<File>(
        [
            new TableColumn<File> { Id = "name", Header = "Name", Text = f => f.Name, Fill = 1, CanHide = false, CanSort = true },
            new TableColumn<File> { Id = "size", Header = "Size", Text = f => f.Size.ToString(), Width = 90,
                Align = ColumnAlign.End, FirstSort = SortDirection.Descending,
                Compare = (_, _) => throw new InvalidOperationException("the table compared") },
            new TableColumn<File> { Id = "kind", Header = "Kind", Text = _ => "File", Width = 70 },
        ], () => log.Add("changed"))
        {
            SortMode = mode,
            SortRequested = requests.Add,
        };
        table.StateChanged = () => log.Add("state");
        log.Clear();
        return (table, log, requests);
    }

    [Fact]
    public void InExternalModeSortAndBodyReturnTheListAsGiven_AndNeverCompare()
    {
        var (table, _, _) = Make();
        table.SetSortKeys([SizeDesc]);

        Assert.Same(Files, table.Sort(Files));
        Assert.Same(Files, table.Body(Files).Items);
        Assert.Same(Files, table.Body(Files, sort: true).Items);
    }

    [Fact]
    public void APressRequests_AndChangesNothing()
    {
        var (table, log, requests) = Make();

        table.SortBy("size");

        var request = Assert.Single(requests);
        Assert.Equal("size", request.ColumnId);
        Assert.Equal(KeyModifiers.None, request.Modifiers);
        Assert.Equal([SizeDesc], request.Suggested);
        Assert.Empty(table.SortKeys);
        Assert.Null(table.SortColumn);
        Assert.Empty(log);
    }

    public static TheoryData<TableSortKey[], string, KeyModifiers, SortDirection?, TableSortKey[]> Clicks => new()
    {
        { [], "name", KeyModifiers.None, null, [NameAsc] },
        { [NameAsc], "name", KeyModifiers.None, null, [NameDesc] },
        { [NameAsc], "size", KeyModifiers.None, null, [SizeDesc] },
        { [NameAsc], "size", KeyModifiers.Shift, null, [NameAsc, SizeDesc] },
        { [NameAsc, SizeDesc], "size", KeyModifiers.Shift, null, [NameAsc, SizeAsc] },
        { [NameAsc, SizeDesc], "size", KeyModifiers.None, null, [SizeDesc] },
        { [], "size", KeyModifiers.Shift, null, [SizeDesc] },
        { [NameAsc, SizeDesc], "name", KeyModifiers.Ctrl | KeyModifiers.Alt, null, [NameDesc] },
        { [NameAsc], "name", KeyModifiers.None, SortDirection.Ascending, [NameAsc] },
    };

    [Theory]
    [MemberData(nameof(Clicks))]
    public void SuggestedFollowsTheClickRule(TableSortKey[] keys, string column, KeyModifiers modifiers,
        SortDirection? direction, TableSortKey[] suggested)
    {
        var (table, _, requests) = Make();
        table.SetSortKeys(keys);

        // A direction is the app's SortBy; a press is the header's, with its modifiers.
        if (direction is { } given) table.SortBy(column, given);
        else Press(table, column, modifiers);

        var request = Assert.Single(requests);
        Assert.Equal(suggested, request.Suggested);
        Assert.Equal(direction is null ? modifiers : KeyModifiers.None, request.Modifiers);
    }

    /// <summary>A header press with modifiers, as the header cell makes it.</summary>
    private static void Press(TableView<File> table, string column, KeyModifiers modifiers)
    {
        var header = table.Header();
        var cell = header.Children.First(c => c.AutomationId == "column-" + column);
        EventDispatcher.DispatchDirect(new ClickEvent { Modifiers = modifiers }, cell);
    }

    [Fact]
    public void SetSortKeysSkipsUnknownUnsortableAndRepeated()
    {
        var (table, log, _) = Make();

        table.SetSortKeys([new("x", SortDirection.Ascending), new("kind", SortDirection.Ascending), NameDesc, NameAsc, SizeAsc]);

        Assert.Equal([NameDesc, SizeAsc], table.SortKeys);
        Assert.Equal("name", table.SortColumn);
        Assert.Equal(SortDirection.Descending, table.SortDirection);
        Assert.Equal(["changed"], log);

        log.Clear();
        table.SetSortKeys([NameDesc, SizeAsc]);
        Assert.Empty(log);
    }

    [Fact]
    public void CanSortCountsOnlyInExternalMode()
    {
        var (table, log, requests) = Make(TableSortMode.Internal);
        var name = table.Column("name")!;

        Assert.False(table.CanSortBy(name));
        table.SortBy("name");
        Assert.Null(table.SortColumn);
        Assert.Empty(log);
        Assert.Empty(requests);

        table.SortMode = TableSortMode.External;
        Assert.True(table.CanSortBy(name));
        Assert.True(table.CanSortBy(table.Column("size")!));
        Assert.False(table.CanSortBy(table.Column("kind")!));
    }

    [Fact]
    public void SeveralKeysInInternalModeThrow_AndOneIsSetSort()
    {
        var (table, log, _) = Make(TableSortMode.Internal);

        var both = new TableView<File>(
        [
            new TableColumn<File> { Id = "a", Header = "A", Compare = (x, y) => 0 },
            new TableColumn<File> { Id = "b", Header = "B", Compare = (x, y) => 0 },
        ], () => { });
        Assert.Throws<InvalidOperationException>(() => both.SetSortKeys([new("a", SortDirection.Ascending), new("b", SortDirection.Ascending)]));

        table.SetSortKeys([SizeAsc]);
        Assert.Equal([SizeAsc], table.SortKeys);
        Assert.Equal("size", table.SortColumn);
        Assert.Equal(["changed"], log);
    }

    [Fact]
    public void InternalModeKeepsSortKeysInStepWithSortColumn()
    {
        var (table, _, _) = Make(TableSortMode.Internal);
        void InStep()
        {
            if (table.SortColumn is null) Assert.Empty(table.SortKeys);
            else Assert.Equal([new TableSortKey(table.SortColumn, table.SortDirection)], table.SortKeys);
        }

        table.SortBy("size");
        InStep();
        table.SortBy("size");
        InStep();
        table.SetSort("size", SortDirection.Ascending);
        InStep();
        table.ClearSort();
        InStep();
        table.Restore(new TableState { SortColumn = "size", SortDirection = SortDirection.Descending });
        InStep();
        Assert.Equal("size", table.SortColumn);
        table.SetSort(null, SortDirection.Ascending);
        InStep();
    }

    [Fact]
    public void SwitchingToInternalKeepsTheFirstKeyWithACompare_AndDropsTheMemo()
    {
        int compared = 0;
        var table = new TableView<File>(
        [
            new TableColumn<File> { Id = "name", Header = "Name", CanSort = true },
            new TableColumn<File> { Id = "size", Header = "Size", Compare = (a, b) => { compared++; return a.Size.CompareTo(b.Size); } },
        ], () => { });

        table.SetSort("size", SortDirection.Ascending);
        table.Sort(Files);
        Assert.True(compared > 0);

        // Back to the same key it remembered a sort for: sorted again, not handed the old copy (the
        // app may have changed its list in place while it sorted the rows itself).
        table.SortMode = TableSortMode.External;
        table.SetSortKeys([SizeAsc, NameAsc]);
        table.SortMode = TableSortMode.Internal;
        Assert.Equal([SizeAsc], table.SortKeys);

        compared = 0;
        Assert.Equal([100L, 100, 200, 300], table.Sort(Files).Select(f => f.Size));
        Assert.True(compared > 0);

        table.SortMode = TableSortMode.External;
        table.SetSortKeys([NameAsc, SizeDesc]);
        table.SortMode = TableSortMode.Internal;
        Assert.Empty(table.SortKeys);   // "name" has no Compare
        Assert.Null(table.SortColumn);
    }

    [Fact]
    public void RestoreLeavesAnExternalSortAlone()
    {
        var (table, _, _) = Make();
        table.SetSortKeys([NameAsc, SizeDesc]);

        table.Restore(new TableState
        {
            SortColumn = "size",
            SortDirection = SortDirection.Ascending,
            Widths = new() { ["size"] = 150 },
        });

        Assert.Equal([NameAsc, SizeDesc], table.SortKeys);
        Assert.Equal(150, table.WidthOf(table.Column("size")!));
    }
}
