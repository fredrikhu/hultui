using Aperture.Core;
using Aperture.Theme;
using HultUI.Core.Accessibility;
using HultUI.Core.Builders;
using HultUI.Core.Components;
using HultUI.Core.Elements;
using HultUI.Core.Layout;
using HultUI.Core.Primitives;
using HultUI.Core.Reactivity;
using HultUI.Core.Styling;

namespace Aperture;

/// <summary>
/// A library: a poster grid with a sort bar over it. The grid is rows of tiles driven
/// through a <c>VirtualListElement</c> — one entry per row of posters — so a library of
/// thousands realizes only what is on screen, and only those tiles ask the poster cache
/// for artwork. Paging follows the viewport and the cursor: a row that comes into view within
/// a page of the loaded end, or a cursor that gets there, asks for the next page, and End asks
/// for the rest.
/// </summary>
public sealed partial class ApertureApp
{
    private readonly Ref<VirtualListElement<GridRowItem>> _gridList = new();

    /// <summary>The grid's viewport height as its last layout left it, kept when a build that shows
    /// another view lets go of the grid (<see cref="LetGoOfGrid"/>): the next library build sizes what it
    /// asks for from it, as it did from the old grid.</summary>
    private float? _gridViewportHeight;

    /// <summary>For a build that shows no library grid: the ref to the last one held the whole library
    /// view through its parents, behind Settings, a film's page or any other view. The one thing a later
    /// build read from it, its viewport's height, is kept.</summary>
    private void LetGoOfGrid()
    {
        if (_gridList.Current is not { } grid) return;
        _gridViewportHeight = grid.Layout.AbsoluteContentBox.Height;
        _gridList.Current = null;
    }

    private Element BuildLibraryView(Route route)
    {
        EnsureLibraries();
        var libraryId = route.Id!;
        var library = Libraries.FirstOrDefault(l => l.Id == libraryId);
        var title = library?.Name ?? route.Title ?? "Library";

        var column = Flex.Column(gap: 0).FlexGrow(1);
        column.AddChild(BuildLibraryBar(title, libraryId));

        // How far down the list the viewport reaches decides what to ask for next. Before
        // the first frame there is no list yet, so one screenful is the estimate.
        // A list is a grid one across, with a short row: everything below — paging,
        // the cursor, the virtual list — is the same either way, which is the point of
        // treating layout as two numbers rather than two views.
        bool asList = _settings.Data.Layout == LibraryLayout.List;
        int perRow = asList ? 1 : VisibleCards(Halide.PosterWidth);
        float rowStride = asList
            ? ListRowHeight + Halide.S2
            : Halide.PosterHeight + (_settings.Data.ShowCardTitles ? Halide.PosterCaption : 0f) + Halide.PosterGap;
        float viewportHeight = _gridList.Current?.Layout.AbsoluteContentBox.Height ?? _gridViewportHeight
            ?? Math.Max(200f, _window.LayoutHeight - Halide.TitleBarHeight - 110f);
        int visibleRows = Math.Max(1, (int)Math.Ceiling(viewportHeight / rowStride));
        _fitGridStride = rowStride;
        var cursorKey = $"library:{libraryId}:{GridSort}:{GridDescending}:{GridPlayedFilter}:{GridGenre?.Id}:{GridFavouritesOnly}";
        // Where the page was left is a place in the list it showed then (the key the page arrived
        // with, until SetCursorList below takes this one): another sort or filter since is another
        // list, which opens at the top, as the cursor starts over in it.
        if (_cursorKey != cursorKey) _gridArrivalScrollY = -1;

        // What to have loaded: a page beyond the furthest the grid has been taken — the last
        // row made as it came into view (see GridRowFor), or the cursor — so the next page is
        // asked for while a page is still to go; and all of it while End is on its way to the
        // last item. Before any row is made, a screenful beyond the first is the estimate. This
        // used to be that estimate alone, from the window's height: at 1440×900 a 250-film
        // library loaded 100, and End, the arrows and the wheel stopped at the 100th.
        int needed = Math.Max((visibleRows * 2 + 1) * perRow, _gridReached + GridPageSize);
        if (CursorIndexIn(cursorKey) is >= 0 and var cursor) needed = Math.Max(needed, cursor + 1 + GridPageSize);
        // The rows down to where the page was left, as for the cursor.
        if (_gridArrivalScrollY >= 0)
            needed = Math.Max(needed, ((int)((_gridArrivalScrollY + viewportHeight) / rowStride) + 1) * perRow + GridPageSize);
        if (CursorGoingToEndIn(cursorKey)) needed = int.MaxValue;
        EnsureGridPage(libraryId, needed);
        // A page that lands rebuilds the view: this read is tracked. Nothing else here read the
        // loader once the total was known, so a landed page showed only at the next build.
        _ = _gridPage.Value;

        var items = GridItems;
        // A grid opening where its visit left it shows the rows there; its first card on screen is the
        // first of the first whole one. A grid kept for Back (ApertureApp.KeptPage.cs) is the list on
        // screen, where it was left.
        bool shownAgain = _gridShownAgain && _gridList.Current is { IsHosted: true };
        _gridShownAgain = false;
        float openAt = _gridList.Current is not { IsHosted: true } ? _gridArrivalScrollY
            : shownAgain ? _gridList.Current.ScrollOffsetY : -1f;
        int firstOnScreen = openAt > 0 ? (int)MathF.Ceiling(openAt / rowStride - 0.01f) * perRow : 0;
        // What the arrows walk in this view, and how many fit across; Back or Forward with no cursor
        // starts it on that card.
        SetCursorList(cursorKey, items, perRow, GridTotal, firstOnScreen);
        // A cursor its visit left out of sight (the wheel took the grid away from it before a card was
        // clicked) comes to that card too: Back hands the keyboard to a card the page shows, and a key
        // walks on from where the grid is, not from where it was.
        if (openAt >= 0 && items.Count > 0 && CursorIndex >= 0
            && (CursorIndex / perRow * rowStride >= openAt + viewportHeight || (CursorIndex / perRow + 1) * rowStride <= openAt))
        {
            _cursorIndex = Math.Min(firstOnScreen, items.Count - 1);
            _pendingCursorFocus = true;
        }
        if (shownAgain)
        {
            // As a list made to open there: the cursor is on screen or nowhere, and the builds after
            // leave the grid where it is until a key moves it (OpenGridWhereItWasLeft).
            _gridCursorShown = (CursorIndex, perRow, rowStride);
            _gridArrivalScrollY = -1;
        }

        if (items.Count == 0)
        {
            column.AddChild(GridEmptyState());
            return column;
        }

        int rowCount = asList ? items.Count : (items.Count + perRow - 1) / perRow;

        RevealGridCursor(perRow, rowStride);

        // The list keeps a row while its item is equal — its place and the column the cursor
        // is in, so an arrow makes the row or two the cursor left and reached. Everything else
        // a row shows is in the shape, and a change makes the rows on screen again.
        bool columnsChanged = perRow != _gridRowShape.PerRow && _gridList.Current is { IsHosted: true };
        if (GridRowShapeChanged(perRow)) _gridList.Current?.InvalidateRows();
        // A change of columns on screen moves the cursor's card to another row, and a card handed
        // back keeps the keyboard. One made again does not: its ring moved with no build since it
        // was made (its state is the old cursor's), or it was the last card of the first row, the
        // one row both widths key alike, which the list patches and so lets go of it first. That one
        // takes the keyboard once the frame that makes it is drawn, as after a key that builds.
        if (columnsChanged && _cursorCard.Current is { IsFocused: true })
            _window.RequestPostFrameCallback(FocusCursorCardMadeInTheFrame);
        // A row made again keeps the cards whose item, cursor and picture did not change (GridRow),
        // as long as what every card shows besides those is what they were made with.
        var cardShape = new GridCardShape(_settings.Data.ShowCardTitles, Ui.DisplayScale);
        if (cardShape != _gridCardShape) _gridCards.Clear();
        _gridCardShape = cardShape;

        var list = VirtualListElement<GridRowItem>.New();
        OpenGridWhereItWasLeft(list, perRow, rowStride, rowCount, viewportHeight);
        // Each item here is a row of posters, and every poster is its own button — so the
        // realized rows answer for themselves rather than the list publishing one node per
        // row (see VirtualListElement.RowsAreComposite).
        list.RowsAreComposite = true;
        column.AddChild(list
            .ItemHeight((int)rowStride)
            // A trailing row for the "loading more" line, when there is more to come.
            .Items(new GridRowItems(rowCount, GridHasMore, perRow, CursorIndex))
            .RenderWith((r, _) => GridRowFor(r, items, perRow, asList))
            .WithRef(_gridList)
            .Padding(new Thickness(0, Halide.S5, Halide.S5, Halide.S5))
            .AutomationId("library-grid")
            .FlexGrow(1));
        return column;
    }

    /// <summary>
    /// Keeps the cursor on screen as the arrows walk past the realized window. A build reveals it only
    /// when it moved, or its row did (another number of columns), since the grid last showed it
    /// (<paramref name="always"/>: a window that changed under it): the wheel takes the grid away from
    /// the cursor, and the builds that come while it does (a page asked for, a picture landing) used to
    /// pull it back. A cursor Back put on the first card on screen is one a mouse user never moved.
    /// </summary>
    private void RevealGridCursor(int perRow, float rowStride, bool always = false)
    {
        if (_gridList.Current is not { IsHosted: true } mounted || CursorIndex < 0) return;
        if (!always && _gridCursorShown == (CursorIndex, perRow, rowStride)) return;
        _gridCursorShown = (CursorIndex, perRow, rowStride);
        int cursorRow = CursorIndex / perRow;
        mounted.ScrollRangeIntoView(cursorRow * rowStride, rowStride);
        // A page that landed took the cursor past the rows the list has laid out (End on its
        // way to the last item): the reveal stops at the old end, so it is made again once
        // the list has the new rows.
        if ((cursorRow + 1) * rowStride > mounted.ContentHeight)
            _window.RequestPostFrameCallback(() => _gridList.Current?.ScrollRangeIntoView(cursorRow * rowStride, rowStride));
    }

    /// <summary>The cursor, and the columns and row height it was revealed in, as the grid last showed it
    /// (<see cref="RevealGridCursor"/>, <see cref="OpenGridWhereItWasLeft"/>).</summary>
    private (int Cursor, int PerRow, float Stride) _gridCursorShown = (-1, 0, 0f);

    /// <summary>How far the grid of the page arrived at was scrolled when that visit was left
    /// (<see cref="Visit.GridScrollY"/>), until a list made for it opens there; -1 for none.</summary>
    private float _gridArrivalScrollY = -1;

    /// <summary>
    /// A library arrived at by Back, Forward or the rail opens its grid where its visit left it, as a
    /// browser puts you back where you were scrolled: at the offset it was left at, the wheel's as the
    /// keys', or with none kept at the cursor's row. It used to open at the top: another view had let
    /// go of the grid (<see cref="LetGoOfGrid"/>), so <see cref="RevealGridCursor"/> had no list to
    /// scroll, the cursor's card was not made and the keyboard went nowhere, and the first key after
    /// made the grid again (279 KB and 289 elements at 300 films; the second key 76 KB more).
    /// <para>The list is new (another view showed in between, or the empty state while a page
    /// loaded), so the offset is its own, held for its first layout: the rows there are the ones made,
    /// the cursor's card among them, ringed, and it takes the keyboard once the frame is drawn
    /// (<see cref="FocusCursorCard"/>), all in the arrival's own frame. An offset past the rows loaded
    /// so far (the library was loaded again since) is put again on the list on screen once a page
    /// that reaches it has been laid out, as the cursor's reveal is.</para>
    /// </summary>
    private void OpenGridWhereItWasLeft(VirtualListElement<GridRowItem> list, int perRow, float rowStride, int rowCount,
        float viewportHeight)
    {
        float at = _gridArrivalScrollY;
        if (_gridList.Current is { IsHosted: true })
        {
            // A build of the grid on screen: only an offset still waiting for its rows to load.
            if (at >= 0)
                _window.RequestPostFrameCallback(() =>
                {
                    if (_gridList.Current is { IsHosted: true } shown) shown.ScrollOffsetY = at;
                });
        }
        else
        {
            if (at >= 0) list.ScrollOffsetY = at;
            else if (CursorIndex >= 0) list.ScrollRangeIntoView(CursorIndex / perRow * rowStride, rowStride);
            // The cursor is on screen as the list opens, or nowhere: the builds after leave the grid
            // where it is until a key moves it.
            _gridCursorShown = (CursorIndex, perRow, rowStride);
        }
        if (!GridHasMore || rowCount * rowStride >= at + viewportHeight) _gridArrivalScrollY = -1;
    }

    private Element BuildLibraryBar(string title, string libraryId)
    {
        EnsureGenres(libraryId);

        var bar = Flex.Row(gap: Halide.S3).FlexShrink(0)
            .Height(56)
            .AlignItems(AlignItems.Center)
            .Padding(new Thickness(0, Halide.S5, 0, Halide.S5))
            .BorderColor(Halide.Border).BorderWidth(new Thickness(0, 0, 1, 0));

        bar.AddChild(Text.New(title).FontSize(18f).FontWeight(FontWeight.SemiBold).TextColor(Halide.Text).AccessibleRole(AccessibilityRole.Heading));
        // With a filter on, the count is of what the filter leaves — say so, or "12 of 16"
        // reads as a paging bug.
        var count = GridTotal > 0
            ? GridHasFilters ? $"{GridItems.Count} of {GridTotal} matching"
                             : $"{GridItems.Count} of {GridTotal}"
            : GridIsLoading ? "…" : "";
        if (count.Length > 0) bar.AddChild(Ui.Mono(count, 11.5f, Halide.TextFaint));

        bar.AddChild(Box.New().FlexGrow(1));

        // On a phone the chips are a strip of their own under the title, swiped sideways: in the
        // bar beside it they ran off the screen, and the count was squeezed to two lines.
        var chips = Halide.Phone ? Flex.Row(gap: Halide.S3).AlignItems(AlignItems.Center).FlexShrink(0) : bar;

        // Clear comes and goes with the filters, on the side of them away from where the chips are
        // held, so the chip just turned on or off stays where it was: the bar holds them at its
        // right end, the phone's strip at its left. In front of them on a phone it pushed every
        // chip right as a filter turned on, Favourites partly out of view (a Galaxy S25).
        Element? clear = null;
        if (GridHasFilters)
        {
            clear = Ui.IconButton(Icons.WinClose, "Clear filters", ClearGridFilters, 12f,
                Halide.TextMuted, "filter-clear");
            clear.Key = "filter-clear";
        }

        if (clear is not null && !Halide.Phone) chips.AddChild(clear);
        chips.AddChild(GenreChip());
        chips.AddChild(FilterChip("Unwatched", GridPlayedFilter == false,
            () => SetGridPlayedFilter(GridPlayedFilter == false ? null : false), "filter-unwatched"));
        chips.AddChild(FilterChip("Favourites", GridFavouritesOnly,
            () => SetGridFavouritesOnly(!GridFavouritesOnly), "filter-favourites"));
        if (clear is not null && Halide.Phone) chips.AddChild(clear);

        chips.AddChild(Box.New().Width(Halide.S2));
        chips.AddChild(SortChip("A–Z", ItemSort.Name, false, "sort-name"));
        chips.AddChild(SortChip("Newest", ItemSort.DateAdded, true, "sort-added"));
        chips.AddChild(SortChip("Year", ItemSort.PremiereDate, true, "sort-year"));
        chips.AddChild(SortChip("Rating", ItemSort.CommunityRating, true, "sort-rating"));

        chips.AddChild(Box.New().Width(Halide.S2));
        chips.AddChild(ViewChip());
        var refresh = Ui.IconButton(Icons.Refresh, "Refresh (F5)", Refresh, 15f, Halide.TextMuted, "library-refresh");
        if (!Halide.Phone)
        {
            bar.AddChild(refresh);
            return bar;
        }

        bar.AddChild(refresh);
        bar.Padding(new Thickness(0, Halide.S4, 0, Halide.S4)).BorderWidth(0f);
        var strip = ScrollView.Horizontal().FlexShrink(0).Height(44)
            .Padding(new Thickness(0, Halide.S4, 0, Halide.S4))
            .Children(chips);
        return Flex.Column(gap: 0).FlexShrink(0)
            .BorderColor(Halide.Border).BorderWidth(new Thickness(0, 0, 1, 0))
            .Children(bar, strip);
    }

    /// <summary>
    /// The genre filter: a chip that opens the library's own genres. A menu rather than a
    /// row of chips because there are eleven of them here and thirty on a bigger server,
    /// and a filter bar that wraps to two lines has stopped being a bar.
    /// </summary>
    private Element GenreChip()
    {
        bool active = GridGenre is not null;
        var chip = Box.New()
            .Padding(new Thickness(5, Halide.S3, 5, Halide.S3))
            .CornerRadius(Halide.Radius1)
            .BorderColor(active ? Halide.Accent : Halide.Border).BorderWidth(1f)
            .Background(active ? Halide.Tint(Halide.Accent) : Color.Transparent)
            .Cursor(CursorType.Pointer)
            .AccessibleRole(HultUI.Core.Accessibility.AccessibilityRole.ComboBox)
            .AccessibleName(active ? $"Genre: {GridGenre!.Name}" : "Any genre")
            .AutomationId("filter-genre")
            .Pressable()
            .ContextMenu(GenreMenu)
            .Children(Ui.Line(active ? GridGenre!.Name : "Genre", 11.5f,
                active ? Halide.Accent300 : Halide.TextMuted));

        // Left click opens the same menu the right button would — a chip that only
        // answers to a right click is a chip nobody finds.
        chip.OnClick(e =>
        {
            if (Genres.Count == 0) return;
            if (e.CurrentTarget.OverlayHost is HultUI.Window window)
                window.OpenContextMenu(e.CurrentTarget, e.Position);
        });
        if (!active) chip.HoverBg(Halide.Raised);
        chip.Key = "filter-genre";
        return chip;
    }

    /// <summary>How the library is drawn — a shelf or a list — and whether the shelf is
    /// captioned. A menu because these are settings, not filters: they belong together and
    /// they are changed rarely.</summary>
    private Element ViewChip()
    {
        bool list = _settings.Data.Layout == LibraryLayout.List;
        var chip = Box.New()
            .Padding(new Thickness(5, Halide.S3, 5, Halide.S3))
            .CornerRadius(Halide.Radius1)
            .BorderColor(Halide.Border).BorderWidth(1f)
            .Cursor(CursorType.Pointer)
            .AccessibleRole(HultUI.Core.Accessibility.AccessibilityRole.ComboBox)
            .AccessibleName(list ? "View: list" : "View: grid")
            .AutomationId("library-view")
            .Pressable()
            .ContextMenu(ViewMenu)
            .Children(Ui.Line(list ? "List" : "Grid", 11.5f, Halide.TextMuted));
        chip.OnClick(e => _window.OpenContextMenu(e.CurrentTarget, e.Position));
        chip.HoverBg(Halide.Raised);
        chip.Key = "library-view";
        return chip;
    }

    private IReadOnlyList<ContextMenuItem> ViewMenu()
    {
        var data = _settings.Data;
        return
        [
            new ContextMenuItem
            {
                Label = "Grid",
                IsChecked = data.Layout == LibraryLayout.Grid,
                Action = () => SetLayout(LibraryLayout.Grid),
            },
            new ContextMenuItem
            {
                Label = "List",
                IsChecked = data.Layout == LibraryLayout.List,
                Action = () => SetLayout(LibraryLayout.List),
            },
            ContextMenuItem.Separator(),
            new ContextMenuItem
            {
                Label = "Titles under posters",
                IsChecked = data.ShowCardTitles,
                Action = () => { data.ShowCardTitles = !data.ShowCardTitles; _settings.Save(); Rerender(); },
            },
        ];
    }

    internal void SetLayout(LibraryLayout layout)
    {
        if (_settings.Data.Layout == layout) return;
        _settings.Data.Layout = layout;
        _settings.Save();
        Rerender();
    }

    private IReadOnlyList<ContextMenuItem> GenreMenu()
    {
        var items = new List<ContextMenuItem>
        {
            new() { Label = "Any genre", IsChecked = GridGenre is null, Action = () => SetGridGenre(null) },
        };
        foreach (var genre in Genres)
        {
            var g = genre;
            items.Add(new ContextMenuItem
            {
                Label = g.Name,
                IsChecked = GridGenre?.Id == g.Id,
                Action = () => SetGridGenre(g),
            });
        }
        return items;
    }

    /// <summary>A filter that is on or off: a toggle for a reader, which says which.</summary>
    private Element FilterChip(string label, bool active, Action onClick, string automationId)
    {
        var chip = Chip(label, active, onClick, automationId);
        chip.AccessibleToggled = active;
        return chip;
    }

    /// <summary>One sort of the set: the one in use said as selected.</summary>
    private Element SortChip(string label, ItemSort sort, bool descending, string automationId)
    {
        var chip = Chip(label, GridSort == sort, () => SetGridSort(sort, descending), automationId);
        chip.AccessibleSelected = GridSort == sort;
        return chip;
    }

    /// <summary>
    /// A chip of the library's bar, keyed by what it is. Keyed, it keeps its element across the
    /// bar's builds. Clear filters, added at the front once a filter is on, shifted every chip after it
    /// into the next one's element: TalkBack's focus, on Unwatched, read Genre after the double tap
    /// that turned it on (a Galaxy S25).
    /// </summary>
    private BoxElement Chip(string label, bool active, Action onClick, string automationId)
    {
        var chip = Box.New()
            .Padding(new Thickness(5, Halide.S3, 5, Halide.S3))
            .CornerRadius(Halide.Radius1)
            .BorderColor(active ? Halide.Accent : Halide.Border).BorderWidth(1f)
            .Background(active ? Halide.Tint(Halide.Accent) : Color.Transparent)
            .Cursor(CursorType.Pointer)
            .AccessibleRole(HultUI.Core.Accessibility.AccessibilityRole.Button)
            .AccessibleName(label)
            .AutomationId(automationId)
            .Pressable()
            .Children(Ui.Line(label, 11.5f, active ? Halide.Accent300 : Halide.TextMuted));
        chip.Key = automationId;
        chip.OnClick(_ => onClick());
        if (!active) chip.HoverBg(Halide.Raised);
        return chip;
    }

    /// <summary>
    /// One row of the grid as its list sees it: its place (-1 for the "loading more" row) and
    /// the column the cursor is in there (-1 when it is elsewhere; -2 on the first row while the
    /// cursor is nowhere, whose first card is then the grid's Tab stop). The cards themselves are
    /// the items at that place, which the shape answers for.
    /// </summary>
    internal readonly record struct GridRowItem(int Row, int CursorColumn);

    /// <summary>The grid's rows as the list's items, each made when the list asks for it.</summary>
    private sealed class GridRowItems(int rows, bool more, int perRow, int cursor) : IReadOnlyList<GridRowItem>
    {
        public int Count => rows + (more ? 1 : 0);

        public GridRowItem this[int index] => index >= rows
            ? new GridRowItem(-1, -1)
            : new GridRowItem(index, cursor >= 0 && cursor / perRow == index ? cursor % perRow
                : cursor < 0 && index == 0 ? -2 : -1);

        public IEnumerator<GridRowItem> GetEnumerator()
        {
            for (var i = 0; i < Count; i++) yield return this[i];
        }

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }

    /// <summary>
    /// What a grid row shows besides its place and its cursor column: how much of the list has
    /// loaded (a page fills the last row), the cards a watched or favourite toggle changed in
    /// place, how many cards fit across, a picture that landed and could not go into its card in
    /// place (<see cref="_artworkRemade"/>; one that could needs no row), and the display scale
    /// (glyphs and artwork are cut for it, and a change retires the old rasters). Another library,
    /// sort or filter empties the list first, which takes the grid off screen; the layout and the
    /// titles setting are another row height: both make every row again by themselves.
    /// </summary>
    private readonly record struct GridRowShape(int Loaded, int Patched, int PerRow, int ArtworkRemade, float Scale);

    private GridRowShape _gridRowShape;

    /// <summary>Rows the grid's template has made, for tests.</summary>
    internal int GridRowsMade { get; private set; }

    /// <summary>Whether anything the grid rows show besides their items changed since the last
    /// build asked; takes the new state either way.</summary>
    private bool GridRowShapeChanged(int perRow)
    {
        var shape = new GridRowShape(_gridItems.Count, _gridPatches, perRow, _artworkRemade, Ui.DisplayScale);
        bool changed = shape != _gridRowShape;
        _gridRowShape = shape;
        return changed;
    }

    /// <summary>How many items the rows made so far reach through: the furthest the grid has been
    /// scrolled (a row is made as it comes into view, the list's overscan with it).</summary>
    private int _gridReached;

    /// <summary>The loaded count at which a row near the end last asked for a build (see
    /// <see cref="GridRowFor"/>): once per page, however many such rows are made.</summary>
    private int _gridAheadAsked = -1;

    private Element GridRowFor(GridRowItem r, IReadOnlyList<MediaItem> items, int perRow, bool asList)
    {
        GridRowsMade++;
        // A row made within a page of the loaded end, as it came into view, asks for a build,
        // which asks for the next page. The wheel, the scroll bar and autoscroll move the list
        // without a build, so nothing else would ask. Posted: rows are made while the frame is
        // drawn.
        int loaded = items.Count;
        _gridReached = Math.Max(_gridReached, r.Row < 0 ? loaded : Math.Min(loaded, (r.Row + 1) * perRow));
        // So does one a taller window brings in when the build's estimate, two screenfuls and a row
        // at the list's height now, reaches past the loaded end: a build would load that far, and a
        // resize builds nothing. A picture landing used to build, and loaded it by the way.
        float viewport = _gridList.Current?.Layout.AbsoluteContentBox.Height ?? 0f;
        int screens = viewport > 0f && _fitGridStride > 0f
            ? ((int)Math.Ceiling(viewport / _fitGridStride) * 2 + 1) * perRow : 0;
        if (GridHasMore && _gridAheadAsked != loaded && Math.Max(_gridReached + GridPageSize, screens) > loaded)
        {
            _gridAheadAsked = loaded;
            HultUI.Application.InvokeOnMainThread(Rerender);
        }
        return r.Row < 0 ? GridTailRow(perRow)
            : asList ? ListRow(items[r.Row], r.Row)
            : GridRow(items, r.Row * perRow, perRow);
    }

    /// <summary>
    /// A row of posters. A row made again — a page filled it, the cursor moved onto it with a
    /// build, the columns changed — hands back each card whose item, cursor and picture are what it
    /// was made with, a picture that landed since having gone into its card in place (REACTIVITY.md,
    /// "Hand back the elements a key does not change"): the row is keyed by its first item's place, so the list patches the row it replaces
    /// from it, and the cards by their items, so the patch puts the live ones back untouched, with the
    /// pictures they show. A change of columns moves places to other rows, and the cards go with them
    /// (<see cref="CanTakeCard"/>). A cursor key mostly makes no row at all (MoveRingWithoutBuild).
    /// </summary>
    private Element GridRow(IReadOnlyList<MediaItem> items, int start, int perRow)
    {
        string key = GridRowKey(start);
        var row = Flex.Row(gap: Halide.PosterGap).AlignItems(AlignItems.FlexStart)
            .Height(Halide.PosterHeight + Halide.PosterCaption)
            .Padding(new Thickness(0, 0, Halide.PosterGap, 0));
        row.Key = key;
        for (int k = 0; k < perRow; k++)
        {
            int i = start + k;
            if (i >= items.Count) break;
            var item = items[i];
            // Asked for a card handed back as well: a make of the row is what keeps a picture on
            // screen recent in the cache, and how a card whose picture the cache let go of is found.
            var picture = ShownPicture(item, ImageKind.Primary, Halide.PosterWidth, Halide.PosterHeight);
            var state = new GridCardState(item, i, MarkOf(i), picture);
            if (_gridCards.TryKeep(item.Id, state, out var kept) && CanTakeCard(kept, key))
            {
                row.AddChild(kept);
                continue;
            }
            row.AddChild(_gridCards.Made(item.Id, state, PosterCard(item, Halide.PosterWidth, i, picture)));
        }
        return row;
    }

    /// <summary>
    /// Whether the row keyed <paramref name="row"/> can take a card handed back: the card's own row
    /// is still a child of the list — the row this one replaces, or, after a change of columns, one
    /// the list is about to let go of; a row that scrolled away is out of the tree. A row on screen is
    /// never asked for another's card: a row is made for the places it holds, and only a change of
    /// columns, which makes every row again, moves a place to another row. A card under the pointer
    /// or pressed goes only back into its own row, as a full build leaves it: moved, it would take its
    /// hover and its press to where the pointer is not, as a row the list moves would.
    /// </summary>
    private bool CanTakeCard(Element card, string row) =>
        card.Parent is { } own && ReferenceEquals(own.Parent, _gridList.Current)
        && (string.Equals(own.Key, row, StringComparison.Ordinal) || !(card.IsHovered || card.IsPressed));

    /// <summary>
    /// Whether the place <paramref name="index"/> in the grid (or -1, nowhere) can take or lose the
    /// ring on the live cards, and its card while a realized row holds it: null when its row is not
    /// realized. False unless the latest build drew the grid (not the list layout) and its list is on
    /// screen.
    /// </summary>
    private bool GridCardAt(int index, out Element? card)
    {
        card = null;
        if (_fitGridStride <= 0f || _settings.Data.Layout == LibraryLayout.List
            || _gridList.Current is not { IsHosted: true } list) return false;
        if (index < 0) return true;
        if (index >= _cursorItems.Count || _cursorItems[index] is not MediaItem item) return false;
        if (_gridCards.Live(item.Id) is { Parent.Parent: { } owner } live && ReferenceEquals(owner, list)) card = live;
        return true;
    }

    private readonly List<string> _gridRowKeys = [];

    /// <summary>"g" and the row's first item's place, made once per place.</summary>
    private string GridRowKey(int start)
    {
        while (_gridRowKeys.Count <= start) _gridRowKeys.Add("g" + _gridRowKeys.Count);
        return _gridRowKeys[start];
    }

    /// <summary>What a card shows besides what every card shows: its item (a watched or favourite
    /// toggle changes the copy), its place, the cursor on it or the Tab stop (<see cref="MarkOf"/>),
    /// and the picture it shows (<see cref="CardArt"/>: the placeholder until its picture lands in place).</summary>
    private readonly record struct GridCardState(MediaItem Item, int Index, CardMark Mark, CardArt Picture);

    /// <summary>What every card shows: whether it has a caption, and the scale its glyphs are cut
    /// at, which retires them. (Artwork's scale goes with that one; the pictures are compared.)</summary>
    private readonly record struct GridCardShape(bool Titles, float Scale);

    /// <summary>
    /// The cards the grid's rows were made with, by item, and the live element each is now. The rows
    /// are made in the frame, outside the app's builds, so there is no <c>Begin</c>: a card is handed
    /// back while it is in the window and its row is still in the list (<see cref="CanTakeCard"/>).
    /// A card in a row that scrolled away is out of the tree. All are let go when what every card
    /// shows changes (<see cref="GridCardShape"/>); those out of the tree as more are made.
    /// </summary>
    private readonly KeptElements<GridCardState> _gridCards = new();

    private GridCardShape _gridCardShape;

    /// <summary>Cards the grid remembers, for tests.</summary>
    internal int GridCardsKept => _gridCards.Count;

    internal const float ListRowHeight = 68f;

    /// <summary>
    /// One item as a row: a small still, the title, and the facts that fit on a line. What
    /// a list is for is comparing — year, runtime, rating side by side — so the row leads
    /// with the title and lines the rest up behind it.
    /// </summary>
    private Element ListRow(MediaItem item, int cursorIndex)
    {
        var row = Flex.Row(gap: Halide.S4).AlignItems(AlignItems.Center)
            .Height(ListRowHeight)
            .Padding(new Thickness(Halide.S1, Halide.S2, Halide.S1, Halide.S2))
            .CornerRadius(Halide.Radius1)
            .Cursor(CursorType.Pointer)
            .AccessibleRole(HultUI.Core.Accessibility.AccessibilityRole.Button)
            .AccessibleName(AccessibleNameFor(item))
            .AutomationId("card-" + item.Id);
        MarkCursor(row, cursorIndex, item);

        row.AddChild(Artwork(item, ImageKind.Primary, 38f, 56f, Halide.Radius1));

        var title = Flex.Column(gap: 2).FlexGrow(1).FlexShrink(1);
        title.AddChild(Ui.Line(item.Name, 13.5f, Halide.Text));
        if (item.Genres.Count > 0)
            title.AddChild(Ui.Line(string.Join(" · ", item.Genres.Take(3)), 11.5f, Halide.TextFaint));
        row.AddChild(title);

        if (item.CommunityRating is { } score)
        {
            var star = Flex.Row(gap: 4).AlignItems(AlignItems.Center).FlexShrink(0).Width(Dimension.Px(56));
            star.AddChild(Ui.Icon(Icons.Rating, 11f, Halide.Accent));
            star.AddChild(Ui.Mono(score.ToString("0.0"), 11.5f, Halide.TextDim));
            row.AddChild(star);
        }
        if (item.RunTime is { } run)
            row.AddChild(Ui.Mono(Ui.Runtime(run), 11.5f, Halide.TextFaint).AccessibleName(Ui.SpokenRuntime(run)).Width(Dimension.Px(64)).FlexShrink(0));
        row.AddChild(Ui.Mono(item.ProductionYear?.ToString() ?? "", 11.5f, Halide.TextFaint)
            .Width(Dimension.Px(44)).FlexShrink(0));

        // The tick lives at the end of the row rather than over the artwork: at this size
        // a badge on a 38-pixel poster is a smudge.
        row.AddChild(item.Played
            ? Ui.Icon(Icons.Watched, 14f, Halide.StatusOk)
            : Box.New().Width(14).Height(14));

        row.OnClick(_ => Open(item));
        if (!IsCursor(cursorIndex)) row.HoverBg(Halide.Raised);
        return row;
    }

    /// <summary>The row past the end: what tells someone the library keeps going.</summary>
    private Element GridTailRow(int perRow)
    {
        var row = Flex.Row(gap: Halide.S2).Height(Halide.PosterHeight + Halide.PosterCaption)
            .AlignItems(AlignItems.FlexStart);
        row.AddChild(Ui.Icon(Icons.Loading, 14f, Halide.TextFaint));
        row.AddChild(Ui.Line($"Loading {Math.Min(GridPageSize, GridTotal - GridItems.Count)} more…",
            12f, Halide.TextFaint));
        return row;
    }

    private Element GridEmptyState()
    {
        var column = Flex.Column(gap: Halide.S3).FlexGrow(1)
            .AlignItems(AlignItems.Center).JustifyContent(JustifyContent.Center)
            .AutomationId("library-empty");

        if (GridIsLoading)
        {
            column.AddChild(Ui.Icon(Icons.Loading, 22f, Halide.TextFaint));
            column.AddChild(Text.New("Opening the library…").FontSize(13f).TextColor(Halide.TextMuted));
            return column;
        }

        if (GridPlayedFilter == false)
        {
            column.AddChild(Ui.Icon(Icons.Watched, 26f, Halide.StatusOk));
            column.AddChild(Text.New("Everything here is watched").FontSize(15f).FontWeight(FontWeight.SemiBold)
                .TextColor(Halide.TextDim));
            column.AddChild(Ui.Button(ButtonVariant.Secondary, "Show all", () => SetGridPlayedFilter(null),
                "empty-show-all"));
            return column;
        }

        column.AddChild(Ui.Icon(Icons.OtherLibrary, 26f, Halide.TextFaint));
        column.AddChild(Text.New("This library is empty").FontSize(15f).FontWeight(FontWeight.SemiBold)
            .TextColor(Halide.TextDim));
        column.AddChild(Text.New("Nothing has been scanned into it yet.")
            .FontSize(12f).TextColor(Halide.TextMuted));
        return column;
    }
}
