using HultUI.Core.Builders;
using HultUI.Core.Components;
using HultUI.Core.Elements;
using HultUI.Core.Layout;
using HultUI.Core.Primitives;
using HultUI.Core.Rendering;
using HultUI.Core.Reactivity;
using HultUI.Core.Styling;
using HultUI.Icons;

namespace PerfSuite;

static partial class Scenarios
{
    private static IEnumerable<Scenario> MovingRows()
    {
        yield return new Scenario("virtual/filter 1,000 item rows, rows follow their item",
            "Hide one author's commits in a 1,000-row list on item rows and show them again, as typing into a filter does, the list at its top: the rows that stay on screen stand at other indices after each change",
            ctx => MovingRowsWalk(ctx, insert: false, carryPlace: false));

        yield return new Scenario("virtual/filter 1,000 item rows, rows carry their place",
            "The same filter when each item carries its place in the list, as an app that keeps the index in its item does: a row that moved never comes back equal",
            ctx => MovingRowsWalk(ctx, insert: false, carryPlace: true));

        yield return new Scenario("virtual/insert above 1,000 item rows, rows follow their item",
            "A commit arrives at the top of a 1,000-row list on item rows scrolled 200 rows down, and goes again, as a fetch brings one: every row on screen stands one index further after each change",
            ctx => MovingRowsWalk(ctx, insert: true, carryPlace: false));

        yield return new Scenario("virtual/insert above 1,000 item rows, rows carry their place",
            "The same arrival when each item carries its place in the list: a row that moved never comes back equal",
            ctx => MovingRowsWalk(ctx, insert: true, carryPlace: true));
    }

    /// <summary>A walk of <see cref="MovingRowsPane"/>: each step makes the change or takes it back.
    /// The row reports the rows the template made per step; the check reads every row on screen.</summary>
    private static Op MovingRowsWalk(Ctx ctx, bool insert, bool carryPlace)
    {
        var pane = new MovingRowsPane(WalkItems(ctx.Rng, 1_000), insert, carryPlace);
        ctx.Mount(pane.Mount());
        pane.ScrollToStart();
        ctx.Pump(2);
        int madeBefore = 0;
        return new Op
        {
            Run = _ =>
            {
                madeBefore = pane.RowsMade;
                pane.Toggle();
            },
            Figure = () => pane.RowsMade - madeBefore,
            FigureUnit = "rows made",
            Check = _ => pane.Verify(),
        };
    }
}

/// <summary>
/// A list of commits on item rows (<c>.RenderWith((item, selected) => …)</c>), no row selected,
/// whose items move: with <paramref name="insert"/> a commit arrives at the top while the list is
/// scrolled <see cref="ScrolledTo"/> rows down, and goes again; without, a filter hides one
/// author's commits and shows them again while the list is at its top. Both lists of items are
/// made once, so a step allocates what the list and its rows do. With
/// <paramref name="carryPlace"/> each item carries its index, as an app that keeps the index in
/// its item does; without, an item is its commit alone.
/// </summary>
sealed class MovingRowsPane(List<WalkItem> items, bool insert, bool carryPlace) : Component
{
    public const float RowHeight = WalkPane.RowHeight;

    /// <summary>The first row on screen in the insert walk.</summary>
    public const int ScrolledTo = 200;

    /// <summary>An item as the list holds it: the commit, and its place when the walk carries it
    /// (−1 when it does not).</summary>
    public readonly record struct Placed(WalkItem Item, int Place);

    private sealed class PlacedItems(List<WalkItem> shown, bool carryPlace) : IReadOnlyList<Placed>
    {
        public int Count => shown.Count;

        public Placed this[int index] => new(shown[index], carryPlace ? index : -1);

        public IEnumerator<Placed> GetEnumerator()
        {
            for (int i = 0; i < shown.Count; i++) yield return this[i];
        }

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }

    private readonly List<WalkItem> _all = items;
    private readonly List<WalkItem> _changed = insert
        ? [new WalkItem(-1, "git-commit", "A commit that arrived with the last fetch", "0000000", "Ada Lovelace",
            "0000000 · Ada Lovelace · 2026-10-06 09:00 · +1 −0"), .. items]
        : items.Where(i => i.Author != items[1].Author).ToList();   // from the second row down

    private readonly Signal<bool> _isChanged = new(false);
    private readonly Ref<VirtualListElement<Placed>> _list = new();
    private Func<Placed, string?>? _label;

    /// <summary>Rows the template has made.</summary>
    public int RowsMade { get; private set; }

    private List<WalkItem> Shown => _isChanged.Value ? _changed : _all;

    public void Toggle() => _isChanged.Value = !_isChanged.Value;

    /// <summary>Scrolls the insert walk's list to <see cref="ScrolledTo"/>; the filter's stays at its top.</summary>
    public void ScrollToStart()
    {
        if (insert && _list.Current is { } list) list.ScrollOffsetY = ScrolledTo * RowHeight;
    }

    public override Element Build() =>
        VirtualListElement<Placed>.New()
            .ItemHeight(RowHeight)
            .Items(new PlacedItems(Shown, carryPlace))
            .AccessibleItemLabel(_label ??= p => p.Item.Subject)
            .RenderWith((p, selected) => Row(p.Item, selected))
            .FlexGrow(1)
            .WithRef(_list);

    private BoxElement Row(WalkItem item, bool selected)
    {
        RowsMade++;
        return Flex.Row(gap: 10).AlignItems(AlignItems.Center).Padding(new Thickness(0, 16))
            .Background(selected ? Ui.Selected : Color.Transparent)
            .BorderColor(Ui.Line).BorderWidth(new Thickness(0, 0, 1, 0))
            .Children(
                Phosphor.Draw(item.Icon, 18f, selected ? Ui.Accent : Ui.Muted),
                Flex.Column(gap: 3).FlexGrow(1).FlexShrink(1).Children(
                    Text.New(item.Subject).FontSize(13f).MaxLines(1).TextOverflow(TextOverflow.Ellipsis),
                    Text.New(item.Meta).FontSize(11f).TextColor(Ui.Muted).MaxLines(1).TextOverflow(TextOverflow.Ellipsis)));
    }

    /// <summary>
    /// Null when every row on screen shows the commit that stands at its place in what the last
    /// change left, the rows fill the view, and the insert walk's list is still scrolled where it
    /// was. Reads the rows by where they are drawn, not by how the list made them.
    /// </summary>
    public string? Verify()
    {
        if (_list.Current is not { } list) return "the list is not mounted";
        var shown = Shown;
        if (insert && Math.Abs(list.ScrollOffsetY - ScrolledTo * RowHeight) > 0.5f)
            return $"the list is scrolled to {list.ScrollOffsetY:0.#}, not {ScrolledTo * RowHeight:0.#}";
        var view = list.WindowContentBox;
        int onScreen = 0;
        for (int i = 0; i < list.Children.Count; i++)
        {
            if (list.Children[i] is not BoxElement { Children.Count: 2 } row) continue;   // not the spacer
            var box = row.WindowBorderBox;
            if (box.Bottom <= view.Y || box.Y >= view.Bottom) continue;
            int index = (int)MathF.Round((box.Y - view.Y + list.ScrollOffsetY) / RowHeight);
            if ((uint)index >= (uint)shown.Count) return $"a row stands at {index}, past the {shown.Count} items";
            string subject = ((TextElement)row.Children[1].Children[0]).Content;
            if (subject != shown[index].Subject)
                return $"row {index} shows \"{subject}\", not \"{shown[index].Subject}\"";
            onScreen++;
        }
        int expected = (int)(view.Height / RowHeight);
        return onScreen < expected ? $"{onScreen} rows on screen, not at least {expected}" : null;
    }
}
