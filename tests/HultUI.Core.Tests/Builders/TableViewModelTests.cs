using HultUI.Core.Builders;
using HultUI.Core.Layout;
using Xunit;

namespace HultUI.Core.Tests.Builders;

/// <summary>The state half of <see cref="TableView{T}"/>: sorting, widths, hidden columns,
/// and the state a settings file keeps.</summary>
public class TableViewModelTests
{
    private sealed record File(string Name, long Size, int Order);

    private static readonly File[] Files =
    [
        new("b.txt", 300, 0), new("a.txt", 100, 1), new("c.txt", 100, 2), new("d.txt", 200, 3),
    ];

    private static (TableView<File> Table, List<string> Log) Make()
    {
        var log = new List<string>();
        var table = new TableView<File>(
        [
            new TableColumn<File> { Id = "name", Header = "Name", Text = f => f.Name, Fill = 1, CanHide = false,
                Compare = (a, b) => string.CompareOrdinal(a.Name, b.Name) },
            new TableColumn<File> { Id = "size", Header = "Size", Text = f => f.Size.ToString(), Width = 90,
                MinWidth = 50, MaxWidth = 300, Align = ColumnAlign.End,
                Compare = (a, b) => a.Size.CompareTo(b.Size), FirstSort = SortDirection.Descending },
            new TableColumn<File> { Id = "kind", Header = "Kind", Text = _ => "File", Width = 70 },
            new TableColumn<File> { Id = "owner", Header = "Owner", Text = _ => "me", HiddenByDefault = true },
        ], () => log.Add("changed")) { };
        table.StateChanged = () => log.Add("state");
        return (table, log);
    }

    private static string[] Names(IReadOnlyList<File> files) => files.Select(f => f.Name).ToArray();

    [Fact]
    public void UnsortedIsTheListAsGiven()
    {
        var (table, _) = Make();
        Assert.Same(Files, table.Sort(Files));
    }

    [Fact]
    public void AHeaderClickSortsTheColumnsFirstWay_AndASecondClickTheOther()
    {
        var (table, log) = Make();
        table.SortBy("name");
        Assert.Equal(["a.txt", "b.txt", "c.txt", "d.txt"], Names(table.Sort(Files)));
        table.SortBy("name");
        Assert.Equal(SortDirection.Descending, table.SortDirection);
        Assert.Equal(["d.txt", "c.txt", "b.txt", "a.txt"], Names(table.Sort(Files)));

        // Sizes start largest first; equal sizes keep the order they came in, either way.
        table.SortBy("size");
        Assert.Equal(["b.txt", "d.txt", "a.txt", "c.txt"], Names(table.Sort(Files)));
        table.SortBy("size");
        Assert.Equal(["a.txt", "c.txt", "d.txt", "b.txt"], Names(table.Sort(Files)));
        Assert.Equal(4, log.Count(l => l == "state"));
    }

    /// <summary>Rows that compare equal keep the order they came in. A short list cannot show
    /// it: <c>Array.Sort</c> insertion-sorts up to sixteen items, which is stable by itself.</summary>
    [Fact]
    public void EqualRowsKeepTheirOrder_InALongList()
    {
        var (table, _) = Make();
        var many = Enumerable.Range(0, 200).Select(i => new File($"f{i:000}", (i * 7) % 3, i)).ToArray();
        table.SortBy("size");
        var sorted = table.Sort(many);
        for (int i = 1; i < sorted.Count; i++)
            if (sorted[i].Size == sorted[i - 1].Size)
                Assert.True(sorted[i].Order > sorted[i - 1].Order, $"{sorted[i - 1].Name} and {sorted[i].Name} swapped");
    }

    [Fact]
    public void TheSortIsRememberedUntilTheListOrTheSortChanges()
    {
        var (table, _) = Make();
        table.SortBy("name");
        var first = table.Sort(Files);
        Assert.Same(first, table.Sort(Files));
        Assert.NotSame(first, table.Sort([.. Files]));
        table.SortBy("name");
        Assert.NotSame(first, table.Sort(Files));
    }

    [Fact]
    public void AnUnsortableColumnIsIgnored()
    {
        var (table, log) = Make();
        table.SortBy("kind");
        Assert.Null(table.SortColumn);
        Assert.Empty(log);
    }

    [Fact]
    public void WidthsClamp_AFillColumnHasNone_AndTheTracksFollow()
    {
        var (table, log) = Make();
        Assert.Equal([GridTrack.Fr(1), GridTrack.Fixed(90), GridTrack.Fixed(70)], table.Tracks());

        table.SetWidth("size", 20, settled: false);
        Assert.Equal(50, table.WidthOf(table.Column("size")!));
        table.SetWidth("size", 500);
        Assert.Equal(300, table.WidthOf(table.Column("size")!));
        table.SetWidth("name", 400);
        Assert.Equal(GridTrack.Fr(1), table.Tracks()[0]);
        Assert.Equal(1, log.Count(l => l == "state"));   // only the settled one
    }

    [Fact]
    public void HidingAndShowingColumns_ButNotTheOneThatNamesTheRow()
    {
        var (table, _) = Make();
        Assert.Equal(["name", "size", "kind"], table.VisibleColumns.Select(c => c.Id));
        table.SetVisible("kind", false);
        table.SetVisible("owner", true);
        table.SetVisible("name", false);
        Assert.Equal(["name", "size", "owner"], table.VisibleColumns.Select(c => c.Id));
        Assert.Equal(3, table.Tracks().Length);
    }

    [Fact]
    public void TheStateRoundTrips_AndSkipsColumnsThatAreGone()
    {
        var (table, _) = Make();
        table.SortBy("size");
        table.SetWidth("size", 150);
        table.SetVisible("kind", false);
        table.SetVisible("owner", true);
        var state = table.State;

        var (restored, log) = Make();
        restored.Restore(state with
        {
            Widths = new(state.Widths) { ["gone"] = 10 },
            Hidden = [.. state.Hidden, "gone"],
        });
        Assert.Equal("size", restored.SortColumn);
        Assert.Equal(SortDirection.Descending, restored.SortDirection);
        Assert.Equal(150, restored.WidthOf(restored.Column("size")!));
        Assert.Equal(["name", "size", "owner"], restored.VisibleColumns.Select(c => c.Id));
        Assert.DoesNotContain("state", log);
        Assert.DoesNotContain("gone", restored.State.Widths.Keys);
    }

    [Fact]
    public void TwoColumnsWithOneIdAreRefused()
    {
        Assert.Throws<ArgumentException>(() => new TableView<File>(
        [
            new TableColumn<File> { Id = "x", Header = "A" },
            new TableColumn<File> { Id = "x", Header = "B" },
        ], () => { }));
    }
}
