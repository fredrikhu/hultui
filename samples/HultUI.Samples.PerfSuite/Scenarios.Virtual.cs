using HultUI.Core.Builders;
using HultUI.Core.Components;
using HultUI.Core.Elements;
using HultUI.Core.Events;
using HultUI.Core.Layout;
using HultUI.Core.Primitives;
using HultUI.Core.Reactivity;
using HultUI.Core.Rendering;
using HultUI.Core.Styling;
using HultUI.Icons;

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

        yield return new Scenario("virtual/selection walk 1,000 rows",
            "Press Down in a 1,000-row list of two-line rows whose selection the owning component's Build reads: the owner rebuilds, the rows are realized again in the render pass",
            ctx => SelectionWalk(ctx, null));

        yield return new Scenario("virtual/selection walk 1,000 item rows",
            "The same walk with .RenderWith((item, selected) => …): the list keeps the rows whose item and selection did not change and makes only the old and the new selected row (and the one scrolled in)",
            ctx => SelectionWalk(ctx, null, itemRows: true));

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

    /// <summary>
    /// A key walk down a History-style pane (<see cref="WalkPane"/>), as Junction's History,
    /// Commit and Files views and Vesper's panes are walked: Down at the window, the selection
    /// a signal the pane's Build reads, so every press rebuilds the pane, patches its list and
    /// has the list realize its rows again while it paints. The walk starts on the last row the
    /// list shows whole, so every press also scrolls a row, as it does once a walk is under
    /// way. Each frame comes 120 ms after the last — a key pressed and released, not held — so
    /// with an accessibility client listening every step rebuilds its tree: a scroll's refresh
    /// waits 100 ms. <paramref name="check"/> adds what the bridge must have seen. With
    /// <paramref name="itemRows"/> the rows come from <c>.RenderWith((item, selected) => …)</c>,
    /// which keeps the ones that did not change. The row reports the rows made per step.
    /// </summary>
    internal static Op SelectionWalk(Ctx ctx, Func<int, string?>? check, Action? reset = null, bool itemRows = false)
    {
        var pane = new WalkPane(WalkItems(ctx.Rng, 1_000), itemRows);
        var host = pane.Mount();
        host.On<KeyDownEvent>(e =>
        {
            if (e.Key == KeyCode.Down) { pane.Move(1); e.StopPropagation = true; }
            else if (e.Key == KeyCode.Up) { pane.Move(-1); e.StopPropagation = true; }
        });
        ctx.Mount(host);
        pane.Select(pane.LastWholeRow());
        ctx.Session.PumpFrame(WalkFrame);
        int first = pane.Selected;
        int madeBefore = 0;
        return new Op
        {
            Reset = reset is null ? null : _ => reset(),
            Run = _ =>
            {
                madeBefore = pane.RowsMade;
                ctx.Window.PressKey(KeyCode.Down);
            },
            FrameDelta = WalkFrame,
            Figure = () => pane.RowsMade - madeBefore,
            FigureUnit = "rows made",
            Check = step => pane.Verify(first + step + 1) ?? check?.Invoke(step),
        };
    }

    private static readonly TimeSpan WalkFrame = TimeSpan.FromMilliseconds(120);

    private static readonly string[] WalkIcons = ["git-commit", "git-commit", "git-commit", "git-merge", "git-commit", "tag"];
    private static readonly string[] WalkAuthors = ["Fredrik Hult", "Ada Lovelace", "Grace Hopper", "Linus Torvalds", "Barbara Liskov"];

    private static List<WalkItem> WalkItems(Random rng, int count)
    {
        var items = new List<WalkItem>(count);
        var when = new DateTime(2026, 10, 5, 18, 0, 0);
        for (int i = 0; i < count; i++)
        {
            string hash = rng.Next(0x1000000, int.MaxValue).ToString("x7")[..7];
            string author = WalkAuthors[rng.Next(WalkAuthors.Length)];
            when = when.AddMinutes(-rng.Next(5, 600));
            items.Add(new WalkItem(i, WalkIcons[rng.Next(WalkIcons.Length)],
                Ui.Paragraph(rng, 5 + rng.Next(6)), hash, author,
                $"{hash} · {author} · {when:yyyy-MM-dd HH:mm} · +{rng.Next(1, 400)} −{rng.Next(0, 120)}"));
        }
        return items;
    }
}

/// <summary>One commit of <see cref="WalkPane"/>: an icon, a subject, a line of detail.</summary>
sealed record WalkItem(int Index, string Icon, string Subject, string Hash, string Author, string Meta);

/// <summary>
/// The shape of Junction's History view: a header, then a <see cref="VirtualListElement{T}"/>
/// of two-line rows (an icon, the subject, a line of detail), no keys on the rows, the
/// selection drawn by the template from the index the pane's Build read, the list told the
/// selected index for assistive technology, and its rows named for it by a label made from
/// the item alone. A selection move writes a signal; the rebuild scrolls the row into view after
/// the build, as Junction's <c>OnAfterBuild</c> does. With <paramref name="itemRows"/> the
/// template is the two-argument one, whose rows the list keeps while they are unchanged.
/// With <paramref name="details"/> the same Build makes a details pane beside the list, for
/// the commit the walk started on (<see cref="DetailsPane"/>): what an app's whole-window
/// Build makes again, unchanged, on every move.
/// </summary>
sealed class WalkPane(List<WalkItem> items, bool itemRows = false, bool details = false) : Component
{
    public const float RowHeight = 46f;

    private readonly Signal<int> _selected = new(0);
    private readonly Ref<VirtualListElement<WalkItem>> _list = new();
    private int _built;
    private bool _reveal;
    private int _opened = -1;

    /// <summary>Rows the template has made.</summary>
    public int RowsMade { get; private set; }

    // Made once, as an app keeps a label that reads only its item: one delegate every build.
    private Func<WalkItem, string?>? _label;

    private static string Label(WalkItem item) => $"{item.Subject}, {item.Author}, {item.Hash}";

    public int Selected => _selected.Value;

    /// <summary>Shows <paramref name="index"/> in the details pane, as opening a commit does.</summary>
    public void Open(int index)
    {
        _opened = index;
        MarkDirty();
    }

    public void Select(int index)
    {
        _reveal = true;
        _selected.Value = Math.Clamp(index, 0, items.Count - 1);
    }

    public void Move(int delta) => Select(_selected.Value + delta);

    /// <summary>The last row the list shows whole, scrolled to the top.</summary>
    public int LastWholeRow() =>
        Math.Max(0, (int)((_list.Current?.Layout.AbsoluteContentBox.Height ?? 0) / RowHeight) - 1);

    public override Element Build()
    {
        int selected = _built = _selected.Value;
        var list = VirtualListElement<WalkItem>.New()
            .ItemHeight(RowHeight)
            .Items(items)
            .AccessibleItemLabel(_label ??= Label)
            .SelectedIndex(selected)
            .FlexGrow(1);
        if (itemRows) list.RenderWith((item, isSelected) => Row(item, isSelected));
        else list.RenderWith(item => Row(item, item.Index == selected));
        var pane = Flex.Column(gap: 0).FlexGrow(1).Children(
            Flex.Row(gap: 8).FlexShrink(0).AlignItems(AlignItems.Center).Padding(new Thickness(9, 16))
                .BorderColor(Ui.Line).BorderWidth(new Thickness(0, 0, 1, 0))
                .Children(
                    Phosphor.Draw("git-branch", 14f, Ui.Muted),
                    Text.New("History").FontSize(13f).FontWeight(FontWeight.SemiBold),
                    Text.New($"{items.Count:N0} commits").FontSize(12f).TextColor(Ui.Muted)),
            list.WithRef(_list));
        if (!details) return pane;
        return Flex.Row(gap: 0).FlexGrow(1).Children(pane, DetailsPane(items[Math.Max(0, _opened)]));
    }

    /// <summary>
    /// A commit's details, as an app shows the one that was opened: its subject, who and when,
    /// a message, the files it changed and two actions. About sixty nodes for assistive
    /// technology, none of which a selection move changes.
    /// </summary>
    private static Element DetailsPane(WalkItem item)
    {
        var files = new Element[24];
        for (int i = 0; i < files.Length; i++)
            files[i] = Flex.Row(gap: 8).FlexShrink(0).AlignItems(AlignItems.Center).Padding(new Thickness(3, 0))
                .Children(
                    Phosphor.Draw(i % 5 == 0 ? "file-image" : "file-code", 14f, Ui.Muted),
                    Text.New($"src/{item.Author.Split(' ')[0].ToLowerInvariant()}/part{i}/File{item.Index + i}.cs")
                        .FontSize(12f).FlexGrow(1).MaxLines(1).TextOverflow(TextOverflow.Ellipsis),
                    Text.New($"+{(item.Index * 7 + i * 13) % 90} −{(item.Index * 3 + i * 5) % 40}").FontSize(11f).TextColor(Ui.Muted));
        return Flex.Column(gap: 8).Width(Dimension.Px(380)).FlexShrink(0).Padding(16)
            .BorderColor(Ui.Line).BorderWidth(new Thickness(0, 0, 0, 1))
            .Children(
                Text.New("Commit").FontSize(12f).TextColor(Ui.Muted),
                Text.New(item.Subject).FontSize(15f).FontWeight(FontWeight.SemiBold),
                Text.New(item.Meta).FontSize(12f).TextColor(Ui.Muted),
                Text.New($"{item.Subject}. {item.Subject}.").FontSize(13f),
                Flex.Row(gap: 8).Children(Button.New("Checkout"), Button.New("Copy hash", ButtonVariant.Secondary)),
                Text.New($"{files.Length} files changed").FontSize(12f).FontWeight(FontWeight.SemiBold),
                ScrollView.New().FlexGrow(1).Children(Flex.Column().FlexShrink(0).Children(files)));
    }

    protected override void OnAfterBuild()
    {
        if (!_reveal) return;
        _reveal = false;
        _list.Current?.ScrollRangeIntoView(_built * RowHeight, RowHeight);
    }

    private BoxElement Row(WalkItem item, bool selected)
    {
        RowsMade++;
        var row = Flex.Row(gap: 10).AlignItems(AlignItems.Center).Padding(new Thickness(0, 16))
            .Background(selected ? Ui.Selected : Color.Transparent)
            .BorderColor(Ui.Line).BorderWidth(new Thickness(0, 0, 1, 0))
            .Cursor(CursorType.Pointer)
            .Children(
                Phosphor.Draw(item.Icon, 18f, selected ? Ui.Accent : Ui.Muted),
                Flex.Column(gap: 3).FlexGrow(1).FlexShrink(1).Children(
                    Text.New(item.Subject).FontSize(13f).MaxLines(1).TextOverflow(TextOverflow.Ellipsis),
                    Text.New(item.Meta).FontSize(11f).TextColor(Ui.Muted).MaxLines(1).TextOverflow(TextOverflow.Ellipsis)));
        row.OnClick(_ => Select(item.Index));
        return row;
    }

    /// <summary>
    /// Null when row <paramref name="expected"/> is selected, told to the list, on screen
    /// whole and drawn selected, and the row above it no longer is. Reads the rows the list
    /// shows by where they are, not by how it made them.
    /// </summary>
    public string? Verify(int expected)
    {
        if (_selected.Value != expected) return $"the selection is on row {_selected.Value}, not {expected}";
        if (_list.Current is not { } list) return "the list is not mounted";
        if (list.SelectedIndex != expected) return $"the list says row {list.SelectedIndex} is selected";
        var view = list.WindowContentBox;
        float top = view.Y + expected * RowHeight - list.ScrollOffsetY;
        if (top < view.Y - 0.5f || top + RowHeight > view.Bottom + 0.5f)
            return $"row {expected} is not on screen whole (top {top:0.#}, view {view.Y:0.#}–{view.Bottom:0.#})";
        if (RowAt(list, top + RowHeight / 2) is not { } row) return $"no row is drawn where row {expected} is";
        if (row.BackgroundColor != Ui.Selected) return $"row {expected} is not drawn selected";
        if (RowAt(list, top - RowHeight / 2) is { } above && above.BackgroundColor == Ui.Selected)
            return $"row {expected - 1} is still drawn selected";
        return null;
    }

    private static BoxElement? RowAt(Element list, float y)
    {
        var children = list.Children;
        for (int i = 0; i < children.Count; i++)
        {
            if (children[i] is not BoxElement { Children.Count: > 0 } row) continue;   // not the spacer
            var box = row.WindowBorderBox;
            if (y >= box.Y && y < box.Bottom) return row;
        }
        return null;
    }
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
