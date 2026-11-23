using HultUI.Core.Accessibility;
using HultUI.Core.Builders;
using HultUI.Core.Components;
using HultUI.Core.Elements;
using HultUI.Core.Events;
using Xunit;

namespace HultUI.Headless.Tests;

/// <summary>
/// A <see cref="TableView{T}"/> the app sorts (<see cref="TableSortMode.External"/>), on screen: a
/// press on a header — pointer, Enter, Shift+Enter, an assistive invoke — asks the app and changes
/// nothing; the header shows the app's answer when it comes, and a reader hears it once, from the
/// pressed column's header.
/// </summary>
public sealed class TableViewExternalSortTests
{
    private sealed record Entry(string Name, long Size);

    private sealed class Page : Component
    {
        public readonly TableView<Entry> Table;
        public readonly List<TableSortRequest> Requests = [];
        public int Saved;
        public IReadOnlyList<Entry> Rows =
        [
            new("b.txt", 300), new("a.txt", 100), new("c.txt", 200), new("d.txt", 50),
        ];
        /// <summary>Calls SetSort with the sort it keeps at the top of every build, as an app that
        /// keeps the sort itself may (docs/ELEMENTS.md, TableView, Sorting).</summary>
        public bool Reassert;
        public TableSortKey? Kept;

        public Page()
        {
            Table = new TableView<Entry>(
            [
                new TableColumn<Entry> { Id = "name", Header = "Name", Fill = 1, Text = e => e.Name, CanSort = true },
                new TableColumn<Entry> { Id = "size", Header = "Size", Width = 90, MinWidth = 40, Align = ColumnAlign.End,
                    Text = e => $"{e.Size} B", CanSort = true, FirstSort = SortDirection.Descending },
                new TableColumn<Entry> { Id = "kind", Header = "Kind", Width = 70, Text = _ => "File" },
            ], MarkDirty)
            {
                SortMode = TableSortMode.External,
                SortRequested = Requests.Add,
                StateChanged = () => Saved++,
            };
        }

        /// <summary>The app's answer: its rows in the order of <paramref name="keys"/>.</summary>
        public void Answer(IReadOnlyList<TableSortKey> keys)
        {
            IEnumerable<Entry> rows = Rows;
            IOrderedEnumerable<Entry>? ordered = null;
            foreach (var key in keys)
            {
                Func<Entry, IComparable> by = key.ColumnId == "size" ? e => e.Size : e => e.Name;
                ordered = ordered is null
                    ? key.Direction == SortDirection.Ascending ? rows.OrderBy(by) : rows.OrderByDescending(by)
                    : key.Direction == SortDirection.Ascending ? ordered.ThenBy(by) : ordered.ThenByDescending(by);
            }
            Rows = ordered?.ToArray() ?? Rows;
            Kept = keys.Count > 0 ? keys[0] : null;
            Table.SetSortKeys(keys);
            MarkDirty();
        }

        public override Element Build()
        {
            if (Reassert) Table.SetSort(Kept?.ColumnId, Kept?.Direction ?? SortDirection.Ascending);
            return Flex.Column(gap: 0).Children(Table.Header(), Table.Body(Rows).FlexGrow(1));
        }
    }

    private sealed class Fixture : IDisposable
    {
        public readonly HeadlessSession Session = HeadlessSession.Create(600, 300);
        public readonly Page Page = new();
        public readonly ComponentHost Host;

        public Fixture(bool reassert = false)
        {
            Page.Reassert = reassert;
            Host = Page.Mount();
            Session.Window.SetRoot(Host);
            Session.PumpFrames(2);
        }

        public HeadlessElement Column(string id) => Session.Window.FindByAutomationId("column-" + id)!;

        public string Name(string id) => Column(id).Element.AccessibleName!;

        public VirtualListElement<Entry> List => LayoutProbe.Descendants<VirtualListElement<Entry>>(Host).Single();

        public string[] Order => List.Items.Select(e => e.Name).ToArray();

        public void Dispose() => Session.Dispose();
    }

    private static readonly TableSortKey NameAsc = new("name", SortDirection.Ascending);
    private static readonly TableSortKey SizeDesc = new("size", SortDirection.Descending);

    [Fact]
    public void AHeaderClickRequests_AndTheHeaderWaitsForTheApp()
    {
        using var f = new Fixture();

        f.Session.Window.Click(f.Column("size").Element);
        f.Session.PumpFrames(2);

        var request = Assert.Single(f.Page.Requests);
        Assert.Equal([SizeDesc], request.Suggested);
        Assert.Equal("Size", f.Name("size"));
        Assert.Equal(["b.txt", "a.txt", "c.txt", "d.txt"], f.Order);
        Assert.Equal(0, f.Page.Saved);

        f.Page.Answer(request.Suggested);
        f.Session.PumpFrames(2);
        Assert.Equal("Size, sorted descending", f.Name("size"));
        Assert.Equal(["b.txt", "c.txt", "a.txt", "d.txt"], f.Order);
        Assert.Equal(0, f.Page.Saved);
    }

    [Fact]
    public void TheAnswerIsAnnouncedOnce()
    {
        using var f = new Fixture();
        var bridge = f.Session.Window.InstallAccessibilityBridge();
        f.Session.PumpFrame();

        f.Session.Window.Click(f.Column("size").Element);
        f.Session.PumpFrame();
        f.Page.Answer(f.Page.Requests[0].Suggested);
        f.Session.PumpFrames(2);

        var said = Assert.Single(bridge.Announcements);
        Assert.Equal("Sorted by Size, descending", said.Text);
        Assert.Equal(AnnouncementPoliteness.Polite, said.Politeness);
        Assert.Equal(f.Column("size").Element.Id, said.Source?.ElementId);

        // The app changing its sort with no press asked: nothing said.
        f.Page.Answer([NameAsc]);
        f.Session.PumpFrames(2);
        Assert.Single(bridge.Announcements);
    }

    [Fact]
    public void AnAppThatReassertsItsSortEveryBuildStillHearsTheAnswer()
    {
        using var f = new Fixture(reassert: true);
        var bridge = f.Session.Window.InstallAccessibilityBridge();
        f.Session.PumpFrame();

        f.Session.Window.Click(f.Column("size").Element);
        f.Session.PumpFrames(2);
        f.Page.Answer(f.Page.Requests[0].Suggested);
        f.Session.PumpFrames(2);

        // From the cell that shows the answer: the builds that re-assert the old sort neither said it
        // early, from the unsorted cell, nor used up the press.
        var said = Assert.Single(bridge.Announcements);
        Assert.Equal("Sorted by Size, descending", said.Text);
        Assert.Equal("Size, sorted descending", said.Source?.Name);
    }

    [Fact]
    public void TheAnnouncementFollowsTheColumnNotThePlace()
    {
        using var f = new Fixture();
        var bridge = f.Session.Window.InstallAccessibilityBridge();
        f.Session.PumpFrame();

        f.Session.Window.Click(f.Column("size").Element);
        f.Session.PumpFrame();
        // Name hidden moves Size one place back: the header's cells are patched by place.
        f.Page.Table.SetVisible("name", false);
        f.Session.PumpFrames(2);
        f.Page.Answer(f.Page.Requests[0].Suggested);
        f.Session.PumpFrames(2);

        var said = Assert.Single(bridge.Announcements);
        Assert.Equal(f.Column("size").Element.Id, said.Source?.ElementId);
        Assert.Equal("Size, sorted descending", said.Source?.Name);
    }

    [Fact]
    public void ShiftClickSuggestsASecondKey_AndBothHeadersShowTheirPlace()
    {
        using var f = new Fixture();
        var bridge = f.Session.Window.InstallAccessibilityBridge();
        f.Page.Answer([NameAsc]);
        f.Session.PumpFrames(2);

        f.Session.Window.Click(f.Column("size").Element, modifiers: KeyModifiers.Shift);
        f.Session.PumpFrame();
        var request = Assert.Single(f.Page.Requests);
        Assert.Equal(KeyModifiers.Shift, request.Modifiers & KeyModifiers.Shift);
        Assert.Equal([NameAsc, SizeDesc], request.Suggested);

        f.Page.Answer(request.Suggested);
        f.Session.PumpFrames(2);
        Assert.Equal("Name, sorted ascending, sort key 1 of 2", f.Name("name"));
        Assert.Equal("Size, sorted descending, sort key 2 of 2", f.Name("size"));
        var marks = LayoutProbe.Descendants<TableView<Entry>.SortMarkElement>(f.Host).Where(m => m.Direction is not null)
            .OrderBy(m => m.Priority).ToArray();
        Assert.Equal([1, 2], marks.Select(m => m.Priority));
        Assert.All(marks, m => Assert.True(m.Layout.Width > 8f, $"mark {m.Priority} is {m.Layout.Width} wide"));
        // The place is drawn, not text: no "1" or "2" a reader would hear after the name.
        Assert.DoesNotContain(Walk(f.Session.Window.AccessibilityTree), n => n.Name is "1" or "2");
        Assert.Equal("Sorted by Name, ascending, then Size, descending", Assert.Single(bridge.Announcements).Text);
    }

    [Fact]
    public void EnterAndShiftEnterOnAFocusedHeaderRequest()
    {
        using var f = new Fixture();
        f.Column("name").Element.Focus();
        f.Session.PumpFrame();

        f.Session.Window.PressKey(KeyCode.Enter);
        f.Session.Window.PressKey(KeyCode.Enter, KeyModifiers.Shift);
        f.Session.Window.PressKey(KeyCode.Space, KeyModifiers.Shift, text: " ");
        f.Session.Window.PressKey(KeyCode.Enter, KeyModifiers.Ctrl);

        Assert.Equal([KeyModifiers.None, KeyModifiers.Shift, KeyModifiers.Shift],
            f.Page.Requests.Select(r => r.Modifiers & (KeyModifiers.Shift | KeyModifiers.Ctrl)));
        Assert.All(f.Page.Requests, r => Assert.Equal("name", r.ColumnId));
    }

    [Fact]
    public void AnAssistiveInvokeRequestsLikeAClick()
    {
        using var f = new Fixture();
        var router = f.Session.Window.InstallActionRouter();

        router.InvokeElement(f.Column("size").Element.Id);
        f.Session.PumpFrame();

        var request = Assert.Single(f.Page.Requests);
        Assert.Equal(KeyModifiers.None, request.Modifiers);
        Assert.Equal([SizeDesc], request.Suggested);
    }

    [Fact]
    public void TheGripAndTheColumnMenuRequestNothing()
    {
        using var f = new Fixture();
        var size = f.Column("size").Element.WindowBorderBox;
        float x = size.X + 2, y = size.Y + size.Height / 2;
        f.Session.Window.MouseDown(x, y);
        f.Session.Window.MoveMouse(x - 40, y);
        f.Session.Window.MouseUp(x - 40, y);
        f.Session.PumpFrames(2);

        f.Session.Window.RightClick(f.Column("kind").Element);
        f.Session.PumpFrames(2);
        f.Session.Window.PressKey(KeyCode.Escape);

        Assert.Empty(f.Page.Requests);
        Assert.Equal(130f, f.Column("size").Element.WindowBorderBox.Width, 0.5f);
    }

    /// <summary>An unchanged rebuild of a table whose sort the app set lays none of its header out:
    /// the cells, names and marks (two keys, with their places) are patched as they were. The body's
    /// list is left out: Body hands it new delegates each build, in either mode, which is not the sort's.</summary>
    [Fact]
    public void AnUnchangedExternalSortRebuildLaysNothingOut()
    {
        using var f = new Fixture();
        f.Page.Answer([NameAsc, SizeDesc]);
        f.Session.PumpFrames(3);
        Assert.Equal(2, f.Page.Table.SortKeys.Count);
        var header = LayoutProbe.Descendants<TableView<Entry>.HeaderElement>(f.Host).Single();

        f.Host.ForceRebuild();

        Assert.Same(header, LayoutProbe.Descendants<TableView<Entry>.HeaderElement>(f.Host).Single());
        Assert.Empty(LayoutDirty(header));
        f.Session.PumpFrames(2);
    }

    private readonly record struct Row(int Id, long Size);

    private sealed class Million : Component
    {
        public readonly TableView<Row> Table;
        public IReadOnlyList<Row> Rows;
        public Task? Sorting;
        public TableSortRequest? Asked;
        public int Compared;
        public int UiCount, OtherCount;
        private readonly int _uiThread = Environment.CurrentManagedThreadId;

        public Million()
        {
            var rows = new Row[1_000_000];
            var rng = new Random(38);
            for (int i = 0; i < rows.Length; i++) rows[i] = new Row(i, rng.NextInt64(1, 1L << 40));
            Rows = rows;
            Table = new TableView<Row>(
            [
                new TableColumn<Row> { Id = "id", Header = "Id", Width = 90, Text = r => r.Id.ToString(System.Globalization.CultureInfo.InvariantCulture) },
                new TableColumn<Row> { Id = "size", Header = "Size", Fill = 1, CanSort = true, FirstSort = SortDirection.Descending,
                    Text = r => r.Size.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    Compare = (a, b) => { Compared++; return a.Size.CompareTo(b.Size); } },
            ], MarkDirty)
            {
                SortMode = TableSortMode.External,
                SortRequested = request =>
                {
                    Asked = request;
                    var from = Rows;
                    Sorting = Task.Run(() =>
                    {
                        var copy = from.ToArray();
                        Array.Sort(copy, Comparer<Row>.Create((a, b) =>
                        {
                            if (Environment.CurrentManagedThreadId == _uiThread) Interlocked.Increment(ref UiCount);
                            else Interlocked.Increment(ref OtherCount);
                            return b.Size.CompareTo(a.Size);
                        }));
                        Rows = copy;
                    });
                },
            };
        }

        public void Answer()
        {
            Table.SetSortKeys(Asked!.Value.Suggested);
            MarkDirty();
        }

        public override Element Build() =>
            Flex.Column(gap: 0).Children(Table.Header(), Table.Body(Rows).FlexGrow(1));
    }

    [Fact]
    public void AMillionRowsSortedByTheAppCostTheUiThreadNoComparison()
    {
        using var session = HeadlessSession.Create(800, 600);
        var page = new Million();
        var host = page.Mount();
        session.Window.SetRoot(host);
        session.PumpFrames(2);
        var size = session.Window.FindByAutomationId("column-size")!.Element;

        long before = GC.GetAllocatedBytesForCurrentThread();
        session.Window.Click(size);
        session.PumpFrames(2);
        long clicked = GC.GetAllocatedBytesForCurrentThread() - before;

        session.PumpUntilCompleted(page.Sorting!, TimeSpan.FromSeconds(30));

        before = GC.GetAllocatedBytesForCurrentThread();
        page.Answer();
        session.PumpFrames(2);
        long answered = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Equal([new TableSortKey("size", SortDirection.Descending)], page.Asked!.Value.Suggested);
        Assert.Equal(0, page.UiCount);
        Assert.True(page.OtherCount > 0);
        Assert.Equal(0, page.Compared);
        var list = LayoutProbe.Descendants<VirtualListElement<Row>>(host).Single();
        Assert.Equal(page.Rows.Max(r => r.Size), list.Items[0].Size);
        Assert.Equal("Size, sorted descending", size.AccessibleName);
        Assert.True(clicked < 1 << 20, $"the press allocated {clicked:N0} B on the UI thread");
        Assert.True(answered < 1 << 20, $"the answer allocated {answered:N0} B on the UI thread");
    }

    private static List<string> LayoutDirty(Element e, List<string>? into = null)
    {
        into ??= [];
        if ((e.DirtyFlags & DirtyFlags.Layout) != 0) into.Add(e.GetType().Name);
        foreach (var c in e.Children) LayoutDirty(c, into);
        return into;
    }

    private static IEnumerable<AccessibilityNode> Walk(AccessibilityNode node)
    {
        yield return node;
        foreach (var child in node.Children)
            foreach (var descendant in Walk(child))
                yield return descendant;
    }
}
