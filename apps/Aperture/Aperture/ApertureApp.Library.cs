using Aperture.Core;
using Aperture.Theme;
using HultUI.Core.Builders;
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
/// for artwork. Paging follows the viewport: the row that scrolls into view asks for the
/// page that would contain it.
/// </summary>
public sealed partial class ApertureApp
{
    private readonly Ref<VirtualListElement<int>> _gridList = new();

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
        int perRow = VisibleCards(Halide.PosterWidth);
        float rowStride = Halide.PosterHeight + Halide.PosterCaption + Halide.PosterGap;
        float viewportHeight = _gridList.Current?.Layout.AbsoluteContentBox.Height
            ?? Math.Max(200f, _window.LayoutHeight - Halide.TitleBarHeight - 110f);
        int visibleRows = Math.Max(1, (int)Math.Ceiling(viewportHeight / rowStride));

        // Ask for a screenful beyond what is realized, so scrolling meets loaded rows.
        EnsureGridPage(libraryId, (visibleRows * 2 + 1) * perRow);

        var items = GridItems;
        // What the arrows walk in this view, and how many fit across.
        SetCursorList($"library:{libraryId}:{GridSort}:{GridDescending}:{GridPlayedFilter}", items, perRow);

        if (items.Count == 0)
        {
            column.AddChild(GridEmptyState());
            return column;
        }

        int rowCount = (items.Count + perRow - 1) / perRow;
        // A trailing row for the "loading more" line, when there is more to come.
        var rows = new List<int>(rowCount + 1);
        for (int r = 0; r < rowCount; r++) rows.Add(r);
        if (GridHasMore) rows.Add(-1);

        // Keep the cursor on screen as the arrows walk past the realized window.
        if (_gridList.Current is { } mounted && CursorIndex >= 0)
        {
            int cursorRow = CursorIndex / perRow;
            mounted.ScrollRangeIntoView(cursorRow * rowStride, rowStride);
        }

        var list = VirtualListElement<int>.New();
        // Each item here is a row of posters, and every poster is its own button — so the
        // realized rows answer for themselves rather than the list publishing one node per
        // row (see VirtualListElement.RowsAreComposite).
        list.RowsAreComposite = true;
        column.AddChild(list
            .ItemHeight((int)rowStride)
            .Items(rows)   // a fresh list each build → visible rows re-realize against current state
            .RenderWith(r => r < 0 ? GridTailRow(perRow) : GridRow(items, r * perRow, perRow))
            .WithRef(_gridList)
            .Padding(new Thickness(0, Halide.S5, Halide.S5, Halide.S5))
            .AutomationId("library-grid")
            .FlexGrow(1));
        return column;
    }

    private Element BuildLibraryBar(string title, string libraryId)
    {
        EnsureGenres(libraryId);

        var bar = Flex.Row(gap: Halide.S3).FlexShrink(0)
            .Height(56)
            .AlignItems(AlignItems.Center)
            .Padding(new Thickness(0, Halide.S5, 0, Halide.S5))
            .BorderColor(Halide.Border).BorderWidth(new Thickness(0, 0, 1, 0));

        bar.AddChild(Text.New(title).FontSize(18f).FontWeight(FontWeight.SemiBold).TextColor(Halide.Text));
        // With a filter on, the count is of what the filter leaves — say so, or "12 of 16"
        // reads as a paging bug.
        var count = GridTotal > 0
            ? GridHasFilters ? $"{GridItems.Count} of {GridTotal} matching"
                             : $"{GridItems.Count} of {GridTotal}"
            : GridIsLoading ? "…" : "";
        if (count.Length > 0) bar.AddChild(Ui.Mono(count, 11.5f, Halide.TextFaint));

        bar.AddChild(Box.New().FlexGrow(1));

        if (GridHasFilters)
            bar.AddChild(Ui.IconButton(Icons.WinClose, "Clear filters", ClearGridFilters, 12f,
                Halide.TextMuted, "filter-clear"));

        bar.AddChild(GenreChip());
        bar.AddChild(FilterChip("Unwatched", GridPlayedFilter == false,
            () => SetGridPlayedFilter(GridPlayedFilter == false ? null : false), "filter-unwatched"));
        bar.AddChild(FilterChip("Favourites", GridFavouritesOnly,
            () => SetGridFavouritesOnly(!GridFavouritesOnly), "filter-favourites"));

        bar.AddChild(Box.New().Width(Halide.S2));
        bar.AddChild(SortChip("A–Z", ItemSort.Name, false, "sort-name"));
        bar.AddChild(SortChip("Newest", ItemSort.DateAdded, true, "sort-added"));
        bar.AddChild(SortChip("Year", ItemSort.PremiereDate, true, "sort-year"));
        bar.AddChild(SortChip("Rating", ItemSort.CommunityRating, true, "sort-rating"));

        bar.AddChild(Box.New().Width(Halide.S2));
        bar.AddChild(Ui.IconButton(Icons.Refresh, "Refresh (F5)", Refresh, 15f, Halide.TextMuted, "library-refresh"));
        return bar;
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
        return chip;
    }

    private IReadOnlyList<ContextMenuItem> GenreMenu()
    {
        var items = new List<ContextMenuItem>
        {
            new() { Label = (GridGenre is null ? "• " : "   ") + "Any genre", Action = () => SetGridGenre(null) },
        };
        foreach (var genre in Genres)
        {
            var g = genre;
            items.Add(new ContextMenuItem
            {
                Label = (GridGenre?.Id == g.Id ? "• " : "   ") + g.Name,
                Action = () => SetGridGenre(g),
            });
        }
        return items;
    }

    private Element FilterChip(string label, bool active, Action onClick, string automationId) =>
        Chip(label, active, onClick, automationId);

    private Element SortChip(string label, ItemSort sort, bool descending, string automationId) =>
        Chip(label, GridSort == sort, () => SetGridSort(sort, descending), automationId);

    private Element Chip(string label, bool active, Action onClick, string automationId)
    {
        var chip = Box.New()
            .Padding(new Thickness(5, Halide.S3, 5, Halide.S3))
            .CornerRadius(Halide.Radius1)
            .BorderColor(active ? Halide.Accent : Halide.Border).BorderWidth(1f)
            .Background(active ? Halide.Tint(Halide.Accent) : Color.Transparent)
            .Cursor(CursorType.Pointer)
            .AccessibleRole(HultUI.Core.Accessibility.AccessibilityRole.Button)
            .AccessibleName(active ? label + " (on)" : label)
            .AutomationId(automationId)
            .Children(Ui.Line(label, 11.5f, active ? Halide.Accent300 : Halide.TextMuted));
        chip.OnClick(_ => onClick());
        if (!active) chip.HoverBg(Halide.Raised);
        return chip;
    }

    private Element GridRow(IReadOnlyList<MediaItem> items, int start, int perRow)
    {
        var row = Flex.Row(gap: Halide.PosterGap).AlignItems(AlignItems.FlexStart)
            .Height(Halide.PosterHeight + Halide.PosterCaption)
            .Padding(new Thickness(0, 0, Halide.PosterGap, 0));
        for (int k = 0; k < perRow; k++)
        {
            int i = start + k;
            if (i >= items.Count) break;
            row.AddChild(PosterCard(items[i], cursorIndex: i));
        }
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
