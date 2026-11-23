using Aperture.Core;
using Aperture.Core.Requests;
using Aperture.Theme;
using HultUI.Core.Builders;
using HultUI.Core.Components;
using HultUI.Core.Elements;
using HultUI.Core.Layout;
using HultUI.Core.Primitives;
using HultUI.Core.Reactivity;
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
    internal const string FavouritesRowKey = "favourites";
    internal static string LatestRowKey(string libraryId) => "latest-" + libraryId;

    /// <summary>
    /// A shelf's card, handed back as it is when what it shows is what it was made with in the build
    /// before (REACTIVITY.md, "Hand back the elements a key does not change"): its item, its place in
    /// the walk, the cursor on it, the picture it draws, the caption setting and the scale its glyphs
    /// are cut at. A cursor key makes the two cards it moved between instead of every card on Home.
    /// The picture is asked for either way, which keeps one on screen recent in the cache and finds a
    /// card whose picture the cache let go of; a picture that lands goes into its card in place.
    /// </summary>
    private Element HomeCard(KeptElements<HomeCardState> cards, MediaItem item, bool still, int index,
        SeasonNotice? notice = null, OutSeasonCard? outCard = null)
    {
        var picture = still
            ? ShownPicture(item, ImageKind.Backdrop, Halide.StillWidth, Halide.StillHeight)
            : ShownPicture(item, ImageKind.Primary, Halide.PosterWidth, MathF.Round(Halide.PosterWidth * 1.5f));
        // A new season's card says when its season arrived ("added 2 days ago"), which moves on
        // with the clock: the line is part of what the card was made with.
        var note = notice is not null ? notice.SeriesName + "|" + NewSeasonLine(notice) + "|" + NewSeasonAdded(notice)
            : outCard is not null ? "out|" + OutSeasonLine(outCard) + "|" + OutSeasonAired(outCard)
            : null;
        var state = new HomeCardState(item, index, MarkOf(index), picture, _settings.Data.ShowCardTitles, Ui.DisplayScale, note);
        if (cards.TryKeep(item.Id, state, out var kept)) return kept;
        return cards.Made(item.Id, state,
            notice is not null ? NewSeasonCard(item, notice, index, picture)
            : outCard is not null ? OutSeasonCardElement(item, outCard, index, picture)
            : still ? StillCard(item, index, picture) : PosterCard(item, Halide.PosterWidth, index, picture));
    }

    /// <summary>What a card on a shelf shows; see <see cref="HomeCard"/>.</summary>
    private readonly record struct HomeCardState(MediaItem Item, int Index, CardMark Mark,
        CardArt Picture, bool Titles, float Scale, string? Note = null);

    /// <summary>A shelf as Home's latest build drew it: where it starts in the walk, how many it
    /// holds, its items and its cards (null for the watchlist's titles), and its row, how many a page
    /// holds and the page it shows (moved by a turn the shelf builds alone), and its part (null for the
    /// watchlist, which the app's build makes).</summary>
    private readonly record struct HomeShelf(int Start, int Count, IReadOnlyList<MediaItem> Items,
        KeptElements<HomeCardState>? Cards, string RowId, int PerPage, int Page, HomeShelfPart? Part);

    private readonly List<HomeShelf> _homeShelves = [];

    /// <summary>
    /// What a shelf of library cards shows besides its cards: its heading (the title, the page of how
    /// many, "See all"), the menu in it (where the row stands among every row Home could draw, which
    /// "Move up" and "Move down" are offered by) and the scale its icons are cut at. The heading's
    /// handlers capture the row and the list of rows of the build that made them, so what they read is
    /// here too; the menu's other entries read the settings when it opens.
    /// </summary>
    private readonly record struct HomeShelfState(string Title, int Count, int Page, int PerPage, bool SeeAll,
        int Place, int Rows, float Scale);

    /// <summary>
    /// A shelf of library cards on Home, a part (HultUI.Core.Components.Part) of its own, made once for
    /// its row and kept by the row's key: an item can stand on two shelves, so a part only ever shows its
    /// own row's cards. A turn of its page (a cursor key onto a card on another page, its Previous and
    /// Next) writes <see cref="Turned"/>, and the shelf builds alone: it used to build all of Home, every
    /// shelf's cards asked for again around the seven the turn makes. The app's build asks for the cards
    /// on each shelf's page as before and places the part stale when one of them was made or the heading
    /// moved, so a shelf whose items changed still shows them; the forced <c>MarkDirty</c> builds every
    /// shelf. See ApertureApp.Parts.cs.
    /// </summary>
    private sealed class HomeShelfPart
    {
        /// <summary>The part's own revision: written by a turn of the shelf's page.</summary>
        public readonly Signal<int> Turned = new(0);
        public readonly Part Part;

        /// <summary>The row's cards, with what each was made from (<see cref="HomeCard"/>), numbered by
        /// <see cref="CardBuilds"/>: the app's builds ask for them and so does a turn.</summary>
        public readonly KeptElements<HomeCardState> Cards = new();
        public int CardBuilds;

        /// <summary>The heading's menu and "See all", numbered by <see cref="ExtraBuilds"/>.</summary>
        public readonly KeptElements<HomeShelfExtraState> Extra = new();
        public int ExtraBuilds;

        /// <summary>What the app's latest build placed the shelf with: its row, every row Home could draw
        /// (its menu's), where it starts in the walk and its place in <see cref="_homeShelves"/>.</summary>
        public HomeRow Row = null!;
        public List<HomeRow> All = null!;
        public int Start, Slot;

        /// <summary>The app build that last placed the shelf.</summary>
        public int Placed;

        /// <summary>The heading the shelf's latest build made.</summary>
        public HomeShelfState Shown;

        /// <summary>While the app's build places the shelf: the cards on its page it made or handed back,
        /// for the shelf's build to put in its row. Null otherwise: a turn asks for its own.</summary>
        public List<Element>? Asked;

        /// <summary>Set by a build of the shelf from <see cref="Asked"/>: the placing built it.</summary>
        public bool BuiltFromAsked;

        public HomeShelfPart(PartGroup group, Func<HomeShelfPart, Element> build, Action afterBuild) =>
            Part = new Part(group, () => build(this), own: Turned, afterBuild: afterBuild);

        public KeptElements<HomeCardState> BeginCards()
        {
            Cards.Begin(++CardBuilds);
            return Cards;
        }
    }

    /// <summary>Home's shelves of library cards, by the row's key; a row Home no longer draws lets its
    /// part go.</summary>
    private readonly Dictionary<string, HomeShelfPart> _homeShelfParts = new(StringComparer.Ordinal);

    /// <summary>The shelves' parts: the forced <c>MarkDirty</c> builds them all.</summary>
    private readonly PartGroup _homeShelfGroup = new();

    /// <summary>While the app's build places the shelves: a shelf built then leaves the keyboard to the
    /// app's own after-build.</summary>
    private bool _placingHomeShelves;

    /// <summary>The cards on the page of the shelf being built, reused from shelf to shelf and emptied
    /// once Home's shelves are made: left full, it held the last shelf's cards, and through them the
    /// whole Home its build made, behind every other view.</summary>
    private readonly List<Element> _homeShelfCards = [];

    /// <summary>Builds of Home's shelves of library cards (each made again), for tests.</summary>
    internal int HomeShelvesMade { get; private set; }

    /// <summary>The app build that last built Home, or -1.</summary>
    private int _homeBuild = -1;

    /// <summary>
    /// Whether the place <paramref name="index"/> in Home's walk (or -1, nowhere) is a library card
    /// that Home's latest build drew, and its live element: null when it is on another page of its
    /// shelf, so not on screen. False for a watchlist title, whose card is built.
    /// </summary>
    private bool HomeCardAt(int index, out Element? card)
    {
        card = null;
        if (index < 0) return true;
        foreach (var shelf in _homeShelves)
        {
            if (index < shelf.Start || index >= shelf.Start + shelf.Count) continue;
            if (shelf.Cards is null) return false;
            card = shelf.Cards.Live(shelf.Items[index - shelf.Start].Id);
            return true;
        }
        return false;
    }

    /// <summary>Turns the shelf the cursor is on to the cursor's page when it shows another
    /// (<see cref="TurnShelfTo"/>): the shelf, when it turned.</summary>
    private HomeShelf? TurnHomeShelfToCursor()
    {
        foreach (var shelf in _homeShelves)
            if (TurnShelfTo(_cursorIndex, shelf.Start, shelf.Count, shelf.RowId, shelf.PerPage, shelf.Page)) return shelf;
        return null;
    }

    /// <summary>The part of the Home shelf <paramref name="rowId"/>, while Home is on screen as its latest
    /// build left it; null for the watchlist, which the app's build makes.</summary>
    private HomeShelfPart? HomeShelfShowing(string rowId)
    {
        if (Current.View != ApertureView.Home || _homeBuild != Builds) return null;
        foreach (var shelf in _homeShelves)
            if (shelf.RowId == rowId) return shelf.Part;
        return null;
    }

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
        // A card dismissed from under the cursor: the cursor stays at its place, on the card that
        // moved up into it, rather than starting over with the shorter list.
        if (_homeCursorAfterRemoval is { } place)
        {
            _homeCursorAfterRemoval = null;
            if (walk.Count > 0)
            {
                _cursorIndex = Math.Min(place, walk.Count - 1);
                _pendingCursorFocus = true;
            }
        }
        // The new-season toast's "Show": the cursor on the first card of the row it names.
        if (_homeCursorToRow is { } toRow)
        {
            _homeCursorToRow = null;
            int start = 0;
            foreach (var row in shown)
            {
                if (row.Key == toRow && row.Count > 0)
                {
                    _cursorIndex = start;
                    _pendingCursorFocus = true;
                    break;
                }
                start += row.Count;
            }
        }

        _homeBuild = Builds;
        _homeShelves.Clear();
        int cursorBase = 0, parts = 0;
        _placingHomeShelves = true;
        try
        {
            for (int index = 0; index < shown.Count; index++)
            {
                var row = shown[index];
                int start = cursorBase;
                var titles = row.Titles;
                string rowId = "row-" + row.Key;
                int shownPage = ShelfPage(rowId, row.Count, row.CardWidth, out int perPage);
                AddCursorShelf(start, row.Count, row.CardWidth, rowId);
                // Every child of the page is keyed: a part's host is keyed by its part, and comes back
                // through the keyed reconcile with nothing marked.
                if (titles is not null)
                {
                    _homeShelves.Add(new HomeShelf(start, row.Count, row.Items, null, rowId, perPage, shownPage, null));
                    var shelf = CardRow(row.Title, row.Count,
                        i => DiscoverCard(titles[i], row.CardWidth, cursorIndex: start + i),
                        row.CardWidth, rowId, headingExtra: HomeRowHeadingExtra(row, all));
                    shelf.Key = row.Key;
                    page.AddChild(shelf);
                }
                else
                {
                    var part = HomeShelfPartOf(row.Key);
                    _homeShelves.Add(new HomeShelf(start, row.Count, row.Items, part.Cards, rowId, perPage, shownPage, part));
                    page.AddChild(PlaceHomeShelf(part, row, all, start, shownPage, perPage).FlexShrink(0));
                    parts++;
                }
                cursorBase += row.Count;
            }
        }
        finally
        {
            _placingHomeShelves = false;
        }
        _homeShelfCards.Clear();
        // A row Home no longer draws (hidden, or gone with its library) lets its part go.
        if (_homeShelfParts.Count > parts)
        {
            foreach (var (key, part) in _homeShelfParts)
                if (part.Placed != Builds) _homeShelfParts.Remove(key);
        }

        if (shown.Count < all.Count)
        {
            var footer = HiddenRowsFooter(all, hidden);
            footer.Key = "home-hidden";
            page.AddChild(footer);
        }

        scroll.AddChild(page);
        return scroll;
    }

    private HomeShelfPart HomeShelfPartOf(string rowKey)
    {
        if (!_homeShelfParts.TryGetValue(rowKey, out var part))
            _homeShelfParts[rowKey] = part = new HomeShelfPart(_homeShelfGroup, BuildHomeShelf, AfterHomeShelfBuild);
        return part;
    }

    /// <summary>
    /// A shelf of library cards for the app's build: its part's host, the part built first when a card on
    /// its page was made or its heading would say something else (REACTIVITY.md, "Hand back the elements a
    /// key does not change"). A Home build runs mostly when data lands, and made every shelf's heading and
    /// row around the kept cards: 39 of the 46 elements of a build with every card kept. The cards are
    /// asked for first, whatever comes of the shelf: that asks for each picture, and keeps each card's
    /// entry for the next build and for a ring moved with no build.
    /// </summary>
    private ComponentHost PlaceHomeShelf(HomeShelfPart part, HomeRow row, List<HomeRow> all, int start, int page, int perPage)
    {
        part.Row = row;
        part.All = all;
        part.Start = start;
        part.Slot = _homeShelves.Count - 1;
        part.Placed = Builds;
        var asked = _homeShelfCards;
        bool cardsKept = AskForShelfCards(part, page, perPage, asked);
        part.Asked = asked;
        part.BuiltFromAsked = false;
        try
        {
            // A shelf with no host yet (Home opened) builds as its host is made, from the cards asked for.
            var host = part.Part.Mount();
            part.Part.Place(stale: !part.BuiltFromAsked && (!cardsKept || HomeShelfStateOf(row, all, page, perPage) != part.Shown));
            return host;
        }
        finally
        {
            part.Asked = null;
            asked.Clear();
        }
    }

    /// <summary>Asks for the cards on the shelf's page <paramref name="page"/> into <paramref name="cards"/>,
    /// each handed back when what it shows is what it was made with: true when every one was.</summary>
    private bool AskForShelfCards(HomeShelfPart part, int page, int perPage, List<Element> cards)
    {
        var row = part.Row;
        var kept = part.BeginCards();
        int first = page * perPage, end = Math.Min(row.Count, first + perPage);
        cards.Clear();
        bool cardsKept = true;
        bool newSeasons = row.Key == NewSeasonsRowKey;
        for (int i = first; i < end; i++)
        {
            var card = HomeCard(kept, row.Items[i], row.Stills, part.Start + i,
                newSeasons ? NoticeFor(row.Items[i]) : null, newSeasons ? OutCardFor(row.Items[i]) : null);
            cardsKept &= card.IsHosted;   // a card handed back is the live one
            cards.Add(card);
        }
        return cardsKept;
    }

    private HomeShelfState HomeShelfStateOf(HomeRow row, List<HomeRow> all, int page, int perPage) =>
        new(row.Title, row.Count, page, perPage, row.SeeAll is not null, all.IndexOf(row), all.Count, Ui.DisplayScale);

    /// <summary>
    /// A shelf part's build: the heading and the row around the cards on its page. Placed by the app's
    /// build, it puts in the cards that build asked for; on a turn it asks for its own, with nothing read
    /// tracked: its own effect hears only <see cref="HomeShelfPart.Turned"/>, as a loader that lands builds
    /// the app, which places the part.
    /// </summary>
    private Element BuildHomeShelf(HomeShelfPart part) => ReactiveScope.Untracked(() =>
    {
        HomeShelvesMade++;
        var row = part.Row;
        string rowId = "row-" + row.Key;
        int page = ShelfPage(rowId, row.Count, row.CardWidth, out int perPage);
        var cards = part.Asked;
        if (cards is null)
        {
            cards = _homeShelfCards;
            AskForShelfCards(part, page, perPage, cards);
            _homeShelves[part.Slot] = _homeShelves[part.Slot] with { Page = page, PerPage = perPage };
        }
        else
        {
            part.BuiltFromAsked = true;
        }
        try
        {
            var shown = part.Shown = HomeShelfStateOf(row, part.All, page, perPage);
            // The heading's menu and "See all" show nothing of the page: a turn hands them back.
            var extraState = new HomeShelfExtraState(shown.Title, shown.SeeAll, shown.Place, shown.Rows, shown.Scale);
            part.Extra.Begin(++part.ExtraBuilds);
            var extra = part.Extra.TryKeep(row.Key, extraState, out var kept) ? kept
                : part.Extra.Made(row.Key, extraState, HomeRowHeadingExtra(row, part.All));
            int first = page * perPage;
            return CardRow(row.Title, row.Count, i => cards[i - first], row.CardWidth, rowId, headingExtra: extra);
        }
        finally
        {
            if (part.Asked is null) cards.Clear();
        }
    });

    /// <summary>What a shelf's heading menu and its "See all" show and do (<see cref="HomeShelfState"/>
    /// without the page).</summary>
    private readonly record struct HomeShelfExtraState(string Title, bool SeeAll, int Place, int Rows, float Scale);

    /// <summary>After a shelf built alone (a turn): the cursor's card, made by that build, takes the
    /// keyboard, and a cursor card the turn took off screen is let go of. A shelf the app's build placed
    /// leaves that to the app's own after-build.</summary>
    private void AfterHomeShelfBuild()
    {
        if (_placingHomeShelves) return;
        FocusCursorCard();
        LetGoOfLeftCursorCard();
    }

    /// <summary>Turns the row <paramref name="rowId"/> to <paramref name="page"/>: a Home shelf builds alone,
    /// any other row with the view.</summary>
    private void TurnRowTo(string rowId, int page)
    {
        _rowPages[rowId] = Math.Max(0, page);
        if (HomeShelfShowing(rowId) is { } shelf) shelf.Turned.Value++;
        else Rerender();
    }

    /// <summary>
    /// Every row home could draw, in the order it will draw them: the built-in order,
    /// rearranged by whatever has been moved. A row with nothing in it is not a row —
    /// an empty heading is worse than a missing one — so it never reaches the order.
    /// </summary>
    private List<HomeRow> AvailableHomeRows()
    {
        var rows = new List<HomeRow>();

        // Series watched through that have had a season arrive since (ApertureApp.NewSeasons.cs). The
        // server's Next up names the same first episode, so while this row is shown Next up leaves it
        // out: one episode under two neighbouring headings reads as a bug, as in the combined row.
        var newSeasons = NewSeasonItems;
        var nextUp = NextUpItems;
        if (newSeasons.Count > 0 && nextUp.Count > 0 && !NewSeasonsRowHidden)
            nextUp = nextUp.Where(i => !newSeasons.Any(n => n.Id == i.Id)).ToList();

        if (_settings.Data.CombineUpNext)
        {
            var merged = MergeUpNext(ResumeItems, nextUp);
            if (merged.Count > 0)
                rows.Add(new HomeRow(CombinedRowKey, "Up next", merged, Halide.StillWidth, Stills: true));
        }
        else
        {
            if (ResumeItems.Count > 0)
                rows.Add(new HomeRow(ResumeRowKey, "Continue watching", ResumeItems, Halide.StillWidth, Stills: true,
                    SeeAll: ResumeItems.Count >= ResumeRowSize
                        ? () => Navigate(new Route(ApertureView.ContinueWatching))
                        : null));
            if (nextUp.Count > 0)
                rows.Add(new HomeRow(NextUpRowKey, "Next up", nextUp, Halide.PosterWidth, Stills: false,
                    SeeAll: NextUpItems.Count >= NextUpRowSize
                        ? () => Navigate(new Route(ApertureView.NextUp))
                        : null));
        }

        // Right after what you are watching, as it answers the same question: what do I put on now.
        if (newSeasons.Count > 0)
            rows.Add(new HomeRow(NewSeasonsRowKey, "New seasons", newSeasons, Halide.PosterWidth, Stills: false));

        // What you meant to watch, from the request server — after what you are already
        // watching, before what has merely arrived. Only with a request server connected
        // and something on the list: like every row here, an empty one is not drawn.
        if (_requestServer is not null && HomeWatchlist.Count > 0)
            rows.Add(new HomeRow(WatchlistRowKey, "Your watchlist", [], Halide.PosterWidth, Stills: false,
                Titles: HomeWatchlist,
                SeeAll: HomeWatchlistHasMore
                    ? () => Navigate(new Route(ApertureView.DiscoverAll, WatchlistRouteId, "Your watchlist"))
                    : null));

        // What you have hearted, from every library, after what you are watching and mean
        // to — the same list as the Favourites page, whose first twelve this is.
        if (Favourites.Count > 0)
            rows.Add(new HomeRow(FavouritesRowKey, "Your favourites", [.. Favourites.Take(FavouritesRowSize)],
                Halide.PosterWidth, Stills: false,
                SeeAll: Favourites.Count > FavouritesRowSize ? () => Navigate(new Route(ApertureView.Favourites)) : null));

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

        if (row.Key == NewSeasonsRowKey)
        {
            items.Add(ContextMenuItem.Separator());
            items.Add(new ContextMenuItem { Label = "Clear all", Action = DismissAllNewSeasons });
        }

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
