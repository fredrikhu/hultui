using Aperture.Core;
using HultUI;

namespace Aperture;

/// <summary>
/// Everything the views read, and the loads behind it. One rule holds the file together:
/// a view's <c>Build()</c> may <i>ask</i> for data but never wait for it. Each loader is
/// keyed on what it depends on — the data generation, plus whatever identifies the thing
/// — so a rebuild that changes nothing is a cache hit, a rebuild that changes the key
/// starts one load, and <see cref="Refresh"/> bumps the generation to invalidate the lot.
/// </summary>
public sealed partial class ApertureApp
{
    /// <summary>Bumped by <see cref="Refresh"/> and by a sign-in: every loader's key
    /// carries it, so one increment re-asks the server for everything.</summary>
    private int _dataGeneration;

    private readonly AsyncLoader<IReadOnlyList<LibraryView>> _libraries = new([]);
    private readonly AsyncLoader<IReadOnlyList<MediaItem>> _resume = new([]);
    private readonly AsyncLoader<IReadOnlyList<MediaItem>> _nextUp = new([]);
    private readonly AsyncLoader<IReadOnlyDictionary<string, IReadOnlyList<MediaItem>>> _latest =
        new(new Dictionary<string, IReadOnlyList<MediaItem>>());
    private readonly AsyncLoader<MediaItem?> _detail = new(null);
    private readonly AsyncLoader<IReadOnlyList<MediaItem>> _children = new([]);
    private readonly AsyncLoader<IReadOnlyList<MediaItem>> _search = new([]);
    private readonly AsyncLoader<ItemPage> _gridPage = new(ItemPage.Empty);

    /// <summary>The libraries on the rail and the home screen.</summary>
    internal IReadOnlyList<LibraryView> Libraries => _libraries.Value;

    /// <summary>Asks for what the shell itself needs. Called from the browsing build, so
    /// the rail has its libraries on the second frame at the latest.</summary>
    private void EnsureLibraries()
    {
        if (_library is not { } lib) return;
        _libraries.Load(("libraries", _dataGeneration),
            () => Fetch(() => lib.GetLibrariesAsync(), (IReadOnlyList<LibraryView>)[], "The libraries"));
    }

    private void EnsureHomeRows()
    {
        if (_library is not { } lib) return;
        EnsureHomeWatchlist();
        _resume.Load(("resume", _dataGeneration),
            () => Fetch(() => lib.GetResumeAsync(ResumeRowSize), (IReadOnlyList<MediaItem>)[], "Continue watching"));
        _nextUp.Load(("nextup", _dataGeneration),
            () => Fetch(() => lib.GetNextUpAsync(NextUpRowSize), (IReadOnlyList<MediaItem>)[], "Next up"));

        // One load for every library's latest row: two or three small calls, and a single
        // key means the home screen settles in one step rather than flickering in per row.
        var libraries = Libraries;
        if (libraries.Count == 0) return;
        _latest.Load(("latest", _dataGeneration, libraries.Count), () =>
        {
            var map = new Dictionary<string, IReadOnlyList<MediaItem>>(StringComparer.Ordinal);
            foreach (var view in libraries)
            {
                if (view.Kind is LibraryKind.Music or LibraryKind.Books) continue;
                map[view.Id] = Fetch(() => lib.GetLatestAsync(view.Id, LatestRowSize),
                    (IReadOnlyList<MediaItem>)[], $"Latest in {view.Name}");
            }
            return (IReadOnlyDictionary<string, IReadOnlyList<MediaItem>>)map;
        });
    }

    internal IReadOnlyList<MediaItem> ResumeItems => _resume.Value;
    internal IReadOnlyList<MediaItem> NextUpItems => _nextUp.Value;
    /// <summary>How many Home's Continue watching row asks for; a full row has "See all".</summary>
    internal const int ResumeRowSize = 12;

    /// <summary>How many Home's Next up row asks for; a full row has "See all".</summary>
    internal const int NextUpRowSize = 12;

    /// <summary>How many a "Latest in" row asks for. A row that comes back full may have
    /// more behind it — which is when it offers "See all".</summary>
    internal const int LatestRowSize = 12;

    internal IReadOnlyList<MediaItem> LatestIn(string libraryId) =>
        _latest.Value.TryGetValue(libraryId, out var items) ? items : [];

    internal bool HomeIsLoading => _libraries.IsLoading || _resume.IsLoading || _nextUp.IsLoading || _latest.IsLoading;

    // ── The library grid, with its paging ────────────────────────────────

    /// <summary>What the grid is currently showing, accumulated page by page. Kept beside
    /// the loader rather than in it because paging <i>appends</i>: the loader's value is
    /// the page that just arrived, this is every page so far.</summary>
    private readonly List<MediaItem> _gridItems = [];

    /// <summary>Identifies the list being accumulated — library, sort, direction, filter.
    /// A change clears the accumulation; the same key just extends it.</summary>
    private string? _gridKey;
    private int _gridTotal;

    internal ItemSort GridSort { get; private set; } = ItemSort.Name;
    internal bool GridDescending { get; private set; }
    internal bool? GridPlayedFilter { get; private set; }

    /// <summary>The genre the grid is filtered to, or null for all of them.</summary>
    internal MediaGenre? GridGenre { get; private set; }

    /// <summary>True when the grid is showing only what has been hearted.</summary>
    internal bool GridFavouritesOnly { get; private set; }

    private readonly AsyncLoader<IReadOnlyList<MediaGenre>> _genres = new([]);

    /// <summary>The genres of the library on screen, for the filter menu.</summary>
    internal IReadOnlyList<MediaGenre> Genres => _genres.Value;

    private void EnsureGenres(string libraryId)
    {
        if (_library is not { } lib) return;
        _genres.Load(("genres", _dataGeneration, libraryId), () => Fetch(
            () => lib.GetGenresAsync(libraryId), (IReadOnlyList<MediaGenre>)[], "The genres"));
    }

    /// <summary>How many items a page asks for. Large enough that scrolling a film
    /// library rarely waits, small enough that the first screenful arrives at once.</summary>
    internal const int GridPageSize = 100;

    internal IReadOnlyList<MediaItem> GridItems => _gridItems;
    internal int GridTotal => _gridTotal;
    internal bool GridIsLoading => _gridPage.IsLoading;
    internal bool GridHasMore => _gridItems.Count < _gridTotal;

    /// <summary>Asks for the page that would extend the grid, if one is needed. Called
    /// from the grid's build with the number of items scrolled into view, so paging
    /// follows the viewport rather than a button.</summary>
    private void EnsureGridPage(string libraryId, int neededCount)
    {
        if (_library is not { } lib) return;

        var key = $"{_dataGeneration}|{libraryId}|{GridSort}|{(GridDescending ? "desc" : "asc")}" +
                  $"|{GridPlayedFilter}|{GridGenre?.Id}|{GridFavouritesOnly}";
        if (_gridKey != key)
        {
            // A different list: start it over. (Not a rebuild — the key only changes when
            // the library, the sort or the filter does.)
            _gridKey = key;
            _gridItems.Clear();
            _gridTotal = 0;
        }
        else if (_gridItems.Count >= neededCount && _gridItems.Count > 0)
        {
            return;   // enough loaded for what is on screen
        }
        else if (_gridItems.Count > 0 && !GridHasMore)
        {
            return;   // that is the whole library
        }

        int start = _gridItems.Count;
        var sort = GridSort;
        bool desc = GridDescending;
        var played = GridPlayedFilter;
        var genreId = GridGenre?.Id;
        bool favourites = GridFavouritesOnly;

        _gridPage.Load((key, start), () => Fetch(() => lib.GetItemsAsync(new ItemQuery
        {
            ParentId = libraryId,
            StartIndex = start,
            Limit = GridPageSize,
            Sort = sort,
            Descending = desc,
            Played = played,
            GenreId = genreId,
            Favorite = favourites ? true : null,
            // A film library holds films; a series library holds series, not every episode
            // in it. Both are "what the shelf shows", which is what Recursive would ruin.
            Recursive = false,
        }), ItemPage.Empty, "The library"), page =>
        {
            // Applied on the UI thread, before the rebuild the load triggers.
            if (_gridKey != key) return;              // the user moved on while it was in flight
            if (page.StartIndex != _gridItems.Count) return;   // a stale page for this list
            _gridItems.AddRange(page.Items);
            _gridTotal = page.TotalCount;
        });
    }

    internal void SetGridSort(ItemSort sort, bool descending)
    {
        if (GridSort == sort && GridDescending == descending) return;
        GridSort = sort;
        GridDescending = descending;
        Rerender();
    }

    internal void SetGridPlayedFilter(bool? played)
    {
        if (GridPlayedFilter == played) return;
        GridPlayedFilter = played;
        Rerender();
    }

    internal void SetGridGenre(MediaGenre? genre)
    {
        if (GridGenre?.Id == genre?.Id) return;
        GridGenre = genre;
        Rerender();
    }

    internal void SetGridFavouritesOnly(bool only)
    {
        if (GridFavouritesOnly == only) return;
        GridFavouritesOnly = only;
        Rerender();
    }

    /// <summary>Puts every filter back — what the "clear" chip does, and what opening a
    /// different library should feel like.</summary>
    internal void ClearGridFilters()
    {
        if (GridGenre is null && !GridFavouritesOnly && GridPlayedFilter is null) return;
        GridGenre = null;
        GridFavouritesOnly = false;
        GridPlayedFilter = null;
        Rerender();
    }

    internal bool GridHasFilters => GridGenre is not null || GridFavouritesOnly || GridPlayedFilter is not null;

    /// <summary>
    /// A "Latest in" row's "See all": the library itself, newest first and unfiltered — the
    /// very query the row is the first twelve of, so the grid carries on where it stops. The
    /// grid's sort and filters are shared by every library, so this is what the next library
    /// opens with too; that is the grid remembering how it was last left, as it always does.
    /// </summary>
    internal void OpenLatest(string libraryId, string libraryName)
    {
        ClearGridFilters();
        SetGridSort(ItemSort.DateAdded, descending: true);
        Navigate(new Route(ApertureView.Library, libraryId, libraryName));
    }

    // ── Detail and its children ──────────────────────────────────────────

    private void EnsureDetail(string itemId)
    {
        if (_library is not { } lib) return;
        _detail.Load(("detail", _dataGeneration, itemId),
            () => Fetch<MediaItem?>(async () =>
            {
                var result = await lib.GetItemAsync(itemId).ConfigureAwait(false);
                return result.Ok
                    ? ApertureResult<MediaItem?>.Success(result.Value)
                    : result.As<MediaItem?>();
            }, null, "That item"));
    }

    /// <summary>The item the detail view is about — null while it loads.</summary>
    internal MediaItem? DetailItem => _detail.Value;
    internal bool DetailIsLoading => _detail.IsLoading;

    /// <summary>Seasons under a series, episodes under a season. One loader: the detail
    /// view shows exactly one level at a time.</summary>
    private void EnsureChildren(string parentId, MediaKind kind)
    {
        if (_library is not { } lib) return;
        _children.Load(("children", _dataGeneration, parentId, kind), () => Fetch(
            () => lib.GetItemsAsync(new ItemQuery
            {
                ParentId = parentId,
                Limit = 500,
                Recursive = false,
                // Seasons and episodes are numbered, and the number is the order.
                Sort = ItemSort.Name,
            }),
            ItemPage.Empty, "Episodes").Items);
    }

    internal IReadOnlyList<MediaItem> Children => _children.Value;
    internal bool ChildrenAreLoading => _children.IsLoading;

    private readonly AsyncLoader<IReadOnlyList<MediaItem>> _seriesNext = new([]);

    /// <summary>What a series page's Play button plays: the server's own next-up answer for
    /// that series, which knows about part-watched episodes and season boundaries in a way
    /// "the first child without a tick" does not.</summary>
    private void EnsureNextInSeries(string seriesId)
    {
        if (_library is not { } lib) return;
        _seriesNext.Load(("next-in", _dataGeneration, seriesId), () => Fetch(
            () => lib.GetNextUpAsync(1, seriesId), (IReadOnlyList<MediaItem>)[], "The next episode"));
    }

    /// <summary>The next episode of the series being shown, once it has loaded.</summary>
    internal MediaItem? NextInSeries(string seriesId) =>
        _seriesNext.Value.FirstOrDefault(e => e.SeriesId == seriesId);

    // ── One person's work, and what has been hearted ─────────────────────

    private readonly AsyncLoader<ItemPage> _personItems = new(ItemPage.Empty);
    private readonly AsyncLoader<ItemPage> _favourites = new(ItemPage.Empty);

    private void EnsurePersonItems(string personId)
    {
        if (_library is not { } lib) return;
        _personItems.Load(("person", _dataGeneration, personId), () => Fetch(
            () => lib.GetItemsAsync(new ItemQuery
            {
                PersonId = personId,
                Limit = 120,
                Recursive = true,
                Sort = ItemSort.PremiereDate,
                Descending = true,
                // A person's page is things you can watch, not the seasons and series
                // rows that would otherwise repeat every episode they appeared in.
                Kinds = [MediaKind.Movie, MediaKind.Series],
            }),
            ItemPage.Empty, "That person's work"));
    }

    internal IReadOnlyList<MediaItem> PersonItems => _personItems.Value.Items;
    internal bool PersonItemsLoading => _personItems.IsLoading;

    private readonly AsyncLoader<IReadOnlyList<MediaItem>> _resumeAll = new([]);

    /// <summary>Everything part-watched, for the Continue watching page. In one call, as the
    /// favourites are: a list of things someone is halfway through is tens long, not
    /// thousands, and the server has no cheaper way to count it.</summary>
    private void EnsureResumeAll()
    {
        if (_library is not { } lib) return;
        _resumeAll.Load(("resume-all", _dataGeneration), () => Fetch(
            () => lib.GetResumeAsync(200), (IReadOnlyList<MediaItem>)[], "Continue watching"));
    }

    internal IReadOnlyList<MediaItem> ResumeAll => _resumeAll.Value;
    internal bool ResumeAllLoading => _resumeAll.IsLoading;

    private readonly AsyncLoader<IReadOnlyList<MediaItem>> _nextUpAll = new([]);

    /// <summary>Every series' next episode, for the Next up page — one per series being
    /// watched, so as short a list as Continue watching, and in one call for the same reason.</summary>
    private void EnsureNextUpAll()
    {
        if (_library is not { } lib) return;
        _nextUpAll.Load(("nextup-all", _dataGeneration), () => Fetch(
            () => lib.GetNextUpAsync(200), (IReadOnlyList<MediaItem>)[], "Next up"));
    }

    internal IReadOnlyList<MediaItem> NextUpAll => _nextUpAll.Value;
    internal bool NextUpAllLoading => _nextUpAll.IsLoading;

    private void EnsureFavourites()
    {
        if (_library is not { } lib) return;
        _favourites.Load(("favourites", _dataGeneration), () => Fetch(
            () => lib.GetItemsAsync(new ItemQuery
            {
                Favorite = true,
                Limit = 200,
                Recursive = true,
                Sort = ItemSort.Name,
            }),
            ItemPage.Empty, "Your favourites"));
    }

    internal IReadOnlyList<MediaItem> Favourites => _favourites.Value.Items;
    internal bool FavouritesLoading => _favourites.IsLoading;

    // ── Search ───────────────────────────────────────────────────────────

    private string _searchTerm = "";
    internal string SearchTerm => _searchTerm;

    internal void SetSearchTerm(string term)
    {
        if (_searchTerm == term) return;
        _searchTerm = term;
        Rerender();
    }

    private void EnsureSearch()
    {
        if (_library is not { } lib) return;
        var term = _searchTerm.Trim();
        if (term.Length < 2)
        {
            return;   // one letter matches everything; the view says so instead
        }
        _search.Load(("search", _dataGeneration, term), () => Fetch(
            () => lib.GetItemsAsync(new ItemQuery
            {
                SearchTerm = term,
                Limit = 60,
                Recursive = true,
                Kinds = [MediaKind.Movie, MediaKind.Series, MediaKind.Episode],
            }),
            ItemPage.Empty, "The search").Items);
    }

    internal IReadOnlyList<MediaItem> SearchResults => _search.Value;
    internal bool SearchIsLoading => _search.IsLoading;

    // ── Running a call off the UI thread ─────────────────────────────────

    /// <summary>
    /// Runs one library call from an <see cref="AsyncLoader{T}"/> producer and hands back
    /// its value, or the fallback with the failure shown as a toast. Blocking here is
    /// correct: the producer is already off the UI thread, and the loader exists precisely
    /// to keep that wait away from a frame.
    /// </summary>
    private T Fetch<T>(Func<Task<ApertureResult<T>>> call, T fallback, string what)
    {
        var result = call().GetAwaiter().GetResult();
        if (result.Ok) return result.Value;

        var failure = result.Failure!;
        Application.InvokeOnMainThread(() => FlashFailure(failure, what));
        return fallback;
    }
}
