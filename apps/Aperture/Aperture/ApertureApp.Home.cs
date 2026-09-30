using Aperture.Core;
using Aperture.Core.Requests;
using Aperture.Theme;
using HultUI.Core.Builders;
using HultUI.Core.Elements;
using HultUI.Core.Layout;
using HultUI.Core.Primitives;
using HultUI.Core.Styling;

namespace Aperture;

/// <summary>
/// Home: what you were watching, what comes next, and what has just arrived — in that
/// order, because that is the order of how likely you are to want it. A server with a
/// cold history shows only the latest rows, and an empty one says so rather than
/// presenting three blank headings.
///
/// <para>That order is a default, not a rule. Every row carries a menu that moves it,
/// hides it, or folds the two "what now" rows into one; the choices are keyed by row and
/// kept in the settings file, so home opens the way it was left.</para>
/// </summary>
public sealed partial class ApertureApp
{
    /// <summary>One shelf on home, as the current data and the current preferences make
    /// it. Built fresh each frame — the settings store keys and an order, never items.</summary>
    /// <param name="Titles">When set, a row of the request server's titles rather than
    /// library items — the watchlist — drawn as discovery cards.</param>
    /// <param name="SeeAll">Where "See all" goes, for a row with more behind it than it shows.</param>
    private sealed record HomeRow(
        string Key, string Title, IReadOnlyList<MediaItem> Items,
        float CardWidth, bool Stills, IReadOnlyList<DiscoverItem>? Titles = null, Action? SeeAll = null)
    {
        public int Count => Titles?.Count ?? Items.Count;
    }

    internal const string ResumeRowKey = "resume";
    internal const string NextUpRowKey = "nextup";
    internal const string CombinedRowKey = "upnext";
    internal const string WatchlistRowKey = "watchlist";
    internal static string LatestRowKey(string libraryId) => "latest-" + libraryId;

    private Element BuildHomeView()
    {
        EnsureLibraries();
        EnsureHomeRows();

        var scroll = ScrollView.Vertical().FlexGrow(1);
        // No FlexGrow inside a scroll view — see the note in the detail view.
        var page = Flex.Column(gap: Halide.S6)
            .Padding(new Thickness(Halide.S5));

        var all = AvailableHomeRows();
        if (all.Count == 0)
        {
            page.AddChild(HomeEmptyState());
            scroll.AddChild(page);
            return scroll;
        }

        var hidden = _settings.Data.HiddenHomeRows;
        var shown = all.Where(r => !hidden.Contains(r.Key, StringComparer.Ordinal)).ToList();

        // Home is several rows but one walk: the arrows run along a row and then into the
        // next, which is how every shelf on a television behaves. The flat list is built
        // in the order the rows are drawn — so reordering the shelves reorders the walk —
        // and each card is handed its place in it.
        var walk = new List<object>();
        foreach (var row in shown)
        {
            if (row.Titles is { } titles) walk.AddRange(titles);
            else walk.AddRange(row.Items);
        }
        SetCursorList($"home:{string.Join(',', shown.Select(r => r.Key))}:{walk.Count}",
            walk, VisibleCards(Halide.PosterWidth));

        int cursorBase = 0;
        for (int index = 0; index < shown.Count; index++)
        {
            var row = shown[index];
            int start = cursorBase;
            var items = row.Items;
            var titles = row.Titles;
            page.AddChild(CardRow(row.Title, row.Count,
                i => titles is not null ? DiscoverCard(titles[i], row.CardWidth, cursorIndex: start + i)
                    : row.Stills ? StillCard(items[i], cursorIndex: start + i)
                    : PosterCard(items[i], cursorIndex: start + i),
                row.CardWidth, "row-" + row.Key,
                headingExtra: HomeRowHeadingExtra(row, all)));
            cursorBase += row.Count;
        }

        if (shown.Count < all.Count) page.AddChild(HiddenRowsFooter(all, hidden));

        scroll.AddChild(page);
        return scroll;
    }

    /// <summary>
    /// Every row home could draw, in the order it will draw them: the built-in order,
    /// rearranged by whatever has been moved. A row with nothing in it is not a row —
    /// an empty heading is worse than a missing one — so it never reaches the order.
    /// </summary>
    private List<HomeRow> AvailableHomeRows()
    {
        var rows = new List<HomeRow>();

        if (_settings.Data.CombineUpNext)
        {
            var merged = MergeUpNext(ResumeItems, NextUpItems);
            if (merged.Count > 0)
                rows.Add(new HomeRow(CombinedRowKey, "Up next", merged, Halide.StillWidth, Stills: true));
        }
        else
        {
            if (ResumeItems.Count > 0)
                rows.Add(new HomeRow(ResumeRowKey, "Continue watching", ResumeItems, Halide.StillWidth, Stills: true));
            if (NextUpItems.Count > 0)
                rows.Add(new HomeRow(NextUpRowKey, "Next up", NextUpItems, Halide.PosterWidth, Stills: false));
        }

        // What you meant to watch, from the request server — after what you are already
        // watching, before what has merely arrived. Only with a request server connected
        // and something on the list: like every row here, an empty one is not drawn.
        if (_requestServer is not null && HomeWatchlist.Count > 0)
            rows.Add(new HomeRow(WatchlistRowKey, "Your watchlist", [], Halide.PosterWidth, Stills: false,
                Titles: HomeWatchlist,
                SeeAll: HomeWatchlistHasMore
                    ? () => Navigate(new Route(ApertureView.DiscoverAll, WatchlistRouteId, "Your watchlist"))
                    : null));

        foreach (var library in Libraries)
        {
            var latest = LatestIn(library.Id);
            if (latest.Count == 0) continue;
            var (id, name) = (library.Id, library.Name);
            rows.Add(new HomeRow(LatestRowKey(id), $"Latest in {name}", latest, Halide.PosterWidth, Stills: false,
                SeeAll: latest.Count >= LatestRowSize ? () => OpenLatest(id, name) : null));
        }

        var order = _settings.Data.HomeRowOrder;
        if (order.Count == 0) return rows;

        // A stable sort on the stored position: rows nobody has moved keep their built-in
        // order at the end, which is where a library shared with you this morning belongs.
        return rows.OrderBy(r =>
        {
            int at = order.IndexOf(r.Key);
            return at < 0 ? int.MaxValue : at;
        }).ToList();
    }

    /// <summary>
    /// The two "what now" rows as one. What you were part-way through comes first, then
    /// what the server says is next, minus anything already in the first half — the same
    /// episode listed twice under one heading looks like a bug.
    /// </summary>
    internal static List<MediaItem> MergeUpNext(IReadOnlyList<MediaItem> resume, IReadOnlyList<MediaItem> next)
    {
        var merged = new List<MediaItem>(resume);
        var seen = new HashSet<string>(resume.Select(i => i.Id), StringComparer.Ordinal);
        foreach (var item in next)
            if (seen.Add(item.Id)) merged.Add(item);
        return merged;
    }

    /// <summary>The row's own menu, in its heading. Kept to the things that are about
    /// <em>this</em> row, so it reads as "this shelf" rather than "home settings".</summary>
    /// <summary>What sits at the end of a row's heading: its menu — and, for a row with
    /// more behind it (the watchlist past one page, a library's latest), "See all" before it.</summary>
    private Element HomeRowHeadingExtra(HomeRow row, List<HomeRow> all)
    {
        var menu = RowMenuButton(row, all);
        if (row.SeeAll is not { } seeAll) return menu;
        var both = Flex.Row(gap: Halide.S1).AlignItems(AlignItems.Center);
        both.AddChild(SeeAllButton(row.Title, "row-" + row.Key + "-see-all", seeAll));
        both.AddChild(menu);
        return both;
    }

    private Element RowMenuButton(HomeRow row, List<HomeRow> all)
    {
        var button = Ui.IconButton(Icons.More, $"Customise “{row.Title}”", () => { }, 14f,
            Halide.TextMuted, "row-" + row.Key + "-menu");
        button.ContextMenu(() => RowMenu(row, all));
        button.OnClick(e => _window.OpenContextMenu(e.CurrentTarget, e.Position));
        return button;
    }

    private IReadOnlyList<ContextMenuItem> RowMenu(HomeRow row, List<HomeRow> all)
    {
        int at = all.FindIndex(r => r.Key == row.Key);
        var items = new List<ContextMenuItem>
        {
            new() { Label = "Move up", IsDisabled = at <= 0, Action = () => MoveHomeRow(row.Key, -1) },
            new() { Label = "Move down", IsDisabled = at < 0 || at >= all.Count - 1, Action = () => MoveHomeRow(row.Key, 1) },
            ContextMenuItem.Separator(),
            new() { Label = "Hide this row", Action = () => SetHomeRowHidden(row.Key, true) },
        };

        bool upNextRow = row.Key is ResumeRowKey or NextUpRowKey or CombinedRowKey;
        if (upNextRow)
        {
            items.Add(ContextMenuItem.Separator());
            items.Add(new ContextMenuItem
            {
                Label = _settings.Data.CombineUpNext
                    ? "Split back into two rows"
                    : "Combine these two rows",
                Action = () => SetCombineUpNext(!_settings.Data.CombineUpNext),
            });
        }

        if (HomeIsCustomised)
        {
            items.Add(ContextMenuItem.Separator());
            items.Add(new ContextMenuItem { Label = "Reset home layout", Action = ResetHomeLayout });
        }
        return items;
    }

    /// <summary>What home is not showing, and one click each to bring it back. A hidden
    /// row that leaves no trace is a feature people turn on once and then report as data
    /// loss.</summary>
    private Element HiddenRowsFooter(List<HomeRow> all, List<string> hidden)
    {
        var column = Flex.Column(gap: Halide.S2).FlexShrink(0).AutomationId("home-hidden");
        column.AddChild(Text.New("Hidden rows").FontSize(12f).TextColor(Halide.TextFaint));

        var row = Flex.Row(gap: Halide.S2).AlignItems(AlignItems.Center);
        row.Flex.Wrap = FlexWrap.Wrap;
        row.Gap(Halide.S2);
        foreach (var candidate in all.Where(r => hidden.Contains(r.Key, StringComparer.Ordinal)))
        {
            var key = candidate.Key;
            row.AddChild(Ui.Button(ButtonVariant.Secondary, "Show " + candidate.Title,
                () => SetHomeRowHidden(key, false), "home-show-" + key));
        }
        column.AddChild(row);
        return column;
    }

    /// <summary>Whether anything has been moved, hidden or combined — what "reset" is
    /// offered for, and what makes it worth offering.</summary>
    internal bool HomeIsCustomised =>
        _settings.Data.HomeRowOrder.Count > 0 ||
        _settings.Data.HiddenHomeRows.Count > 0 ||
        _settings.Data.CombineUpNext;

    /// <summary>
    /// Moves a row one place. The stored order starts as the order home is drawing now,
    /// so the first move of a row that has never been moved does not also shuffle the
    /// rows around it.
    /// </summary>
    internal void MoveHomeRow(string key, int delta)
    {
        var all = AvailableHomeRows();
        int at = all.FindIndex(r => r.Key == key);
        int to = at + delta;
        if (at < 0 || to < 0 || to >= all.Count) return;

        var order = all.Select(r => r.Key).ToList();
        (order[at], order[to]) = (order[to], order[at]);

        // Keys for rows that are not on offer today — an empty library, the rows the
        // combine switch is currently folding away — keep their stored places.
        var kept = _settings.Data.HomeRowOrder.Where(k => !order.Contains(k, StringComparer.Ordinal));
        _settings.Data.HomeRowOrder = order.Concat(kept).ToList();
        _settings.Save();
        Rerender();
    }

    internal void SetHomeRowHidden(string key, bool hidden)
    {
        var list = _settings.Data.HiddenHomeRows;
        if (hidden)
        {
            if (list.Contains(key, StringComparer.Ordinal)) return;
            list.Add(key);
        }
        else if (!list.Remove(key)) return;

        _settings.Save();
        Rerender();
    }

    internal void SetCombineUpNext(bool combine)
    {
        if (_settings.Data.CombineUpNext == combine) return;
        _settings.Data.CombineUpNext = combine;
        _settings.Save();
        Rerender();
    }

    internal void ResetHomeLayout()
    {
        _settings.Data.HomeRowOrder = [];
        _settings.Data.HiddenHomeRows = [];
        _settings.Data.CombineUpNext = false;
        _settings.Save();
        Rerender();
    }

    /// <summary>
    /// Nothing to show. Three reasons look identical from here — still loading, an empty
    /// server, a user with no libraries shared to them — so the state says which one it is
    /// rather than showing the same shrug for all three.
    /// </summary>
    private Element HomeEmptyState()
    {
        var column = Flex.Column(gap: Halide.S3)
            .Height(Dimension.Percent(100))
            .AlignItems(AlignItems.Center).JustifyContent(JustifyContent.Center)
            .AutomationId("home-empty");

        if (HomeIsLoading)
        {
            column.AddChild(Ui.Icon(Icons.Loading, 22f, Halide.TextFaint));
            column.AddChild(Text.New("Asking the server…").FontSize(13f).TextColor(Halide.TextMuted));
            return column;
        }

        if (Libraries.Count == 0)
        {
            column.AddChild(Ui.Icon(Icons.OtherLibrary, 26f, Halide.TextFaint));
            column.AddChild(Text.New("No libraries here").FontSize(15f).FontWeight(FontWeight.SemiBold)
                .TextColor(Halide.TextDim));
            column.AddChild(Text.New($"{_library?.User.Name} has no libraries on {_library?.Server.Name}.")
                .FontSize(12f).TextColor(Halide.TextMuted));
            column.AddChild(Text.New("An administrator grants access under Dashboard → Users.")
                .FontSize(11.5f).TextColor(Halide.TextFaint));
            return column;
        }

        column.AddChild(Ui.Icon(Icons.Movies, 26f, Halide.TextFaint));
        column.AddChild(Text.New("Nothing watched yet").FontSize(15f).FontWeight(FontWeight.SemiBold)
            .TextColor(Halide.TextDim));
        column.AddChild(Text.New("Open a library from the rail and start something.")
            .FontSize(12f).TextColor(Halide.TextMuted));

        var row = Flex.Row(gap: Halide.S2).AlignItems(AlignItems.Center);
        foreach (var library in Libraries.Take(4))
        {
            var id = library.Id;
            var name = library.Name;
            row.AddChild(Ui.Button(ButtonVariant.Secondary, name,
                () => Navigate(new Route(ApertureView.Library, id, name)),
                "empty-open-" + id, Icons.ForLibrary(library.Kind)));
        }
        column.AddChild(row);
        return column;
    }
}
