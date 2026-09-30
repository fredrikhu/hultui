using Aperture.Core;
using Aperture.Core.Requests;
using Aperture.Theme;
using HultUI.Core.Accessibility;
using HultUI.Core.Builders;
using HultUI.Core.Elements;
using HultUI.Core.Layout;
using HultUI.Core.Primitives;
using HultUI.Core.Styling;

namespace Aperture;

/// <summary>
/// Discover: what the library does not have yet. Shelves from the request server —
/// trending, popular, coming soon — drawn as the same posters as the library's own, with
/// one difference: every card says where the title stands (in the library, asked for, on
/// its way), because on this page that is the thing you came to find out.
///
/// <para>A title the library already has opens the <i>library's</i> page, with its Play
/// button — the request server knowing about a film is not a reason to make someone go
/// the long way round to watch it.</para>
/// </summary>
public sealed partial class ApertureApp
{
    private Element BuildDiscoverView()
    {
        EnsureDiscoverRows();
        EnsureMyRecentRequests();

        var scroll = ScrollView.Vertical().FlexGrow(1).AutomationId("discover");
        var page = Flex.Column(gap: Halide.S6).Padding(new Thickness(Halide.S5));

        var header = Flex.Row(gap: Halide.S3).AlignItems(AlignItems.Center).FlexShrink(0);
        header.AddChild(Text.New("Discover").FontSize(20f).FontWeight(FontWeight.SemiBold).TextColor(Halide.Text));
        header.AddChild(Ui.Chip(RequestServerTitle));
        header.AddChild(Box.New().FlexGrow(1));
        header.AddChild(Ui.Button(ButtonVariant.Secondary, "Search", () => Navigate(new Route(ApertureView.Search)),
            "discover-search", Icons.Search));
        header.AddChild(Ui.Button(ButtonVariant.Secondary, "Requests", () => Navigate(new Route(ApertureView.Requests)),
            "discover-requests", Icons.Requests));
        page.AddChild(header);

        // The administrator's rows, in their order, minus any with nothing in them — an
        // empty heading is worse than a missing one. Requests are a row of their own
        // loader, and become titles here.
        var recent = MyRecentRequests
            .Where(r => r.Title is not null)
            .Select(RequestAsItem)
            .DistinctBy(i => i.Key)
            .ToList();
        var rows = DiscoverRows
            .Select(r => r.Kind == SliderKind.RecentRequests ? r with { Items = recent } : r)
            .Where(r => !r.IsEmpty)
            .ToList();

        if (rows.Count == 0)
        {
            // Nothing to walk — and not the last view's cards either.
            SetCursorList("discover:empty", [], 1);
            page.AddChild(EmptyState(DiscoverRowsLoading ? Icons.Loading : Icons.Discover,
                DiscoverRowsLoading ? $"Asking {RequestServerTitle}…" : "Nothing to discover",
                DiscoverRowsLoading ? null : $"{RequestServerTitle} sent back empty shelves. Is it connected to TMDB?",
                "discover-empty"));
            scroll.AddChild(page);
            return scroll;
        }

        // The walk runs through every row in the order drawn, like home's — genre cards
        // included, which open their genre.
        var walk = new List<object>();
        foreach (var row in rows)
        {
            walk.AddRange(row.Items);
            walk.AddRange(row.Genres);
            walk.AddRange(row.Companies);
        }
        SetCursorList($"discover:{string.Join(',', rows.Select(r => r.Key))}:{walk.Count}", walk,
            VisibleCards(Halide.PosterWidth));

        int cursorBase = 0;
        foreach (var row in rows)
        {
            int start = cursorBase;
            if (row.Genres.Count > 0)
            {
                var genres = row.Genres;
                page.AddChild(CardRow(row.Title, genres.Count,
                    i => GenreCard(genres[i], cursorIndex: start + i), GenreCardWidth, "discover-row-" + row.Key));
                cursorBase += genres.Count;
                continue;
            }
            if (row.Companies.Count > 0)
            {
                var companies = row.Companies;
                page.AddChild(CardRow(row.Title, companies.Count,
                    i => CompanyCard(companies[i], cursorIndex: start + i), CompanyCardWidth, "discover-row-" + row.Key));
                cursorBase += companies.Count;
                continue;
            }
            var items = row.Items;
            page.AddChild(CardRow(row.Title, items.Count,
                i => DiscoverCard(items[i], Halide.PosterWidth, cursorIndex: start + i),
                Halide.PosterWidth, "discover-row-" + row.Key,
                headingExtra: row.CanSeeAll ? SeeAllLink(row) : null));
            cursorBase += items.Count;
        }

        scroll.AddChild(page);
        return scroll;
    }

    private const float GenreCardWidth = 240f;

    /// <summary>
    /// A genre to browse: one of its backdrops, darkened, with the name over it — the
    /// server's own web page draws them the same way, and a row of backdrops reads as
    /// "places to go" rather than as more titles.
    /// </summary>
    private Element GenreCard(DiscoverGenre genre, int cursorIndex = -1)
    {
        float height = MathF.Round(GenreCardWidth * 9 / 16);
        var card = Box.New().Width(GenreCardWidth).Height(height).FlexShrink(0)
            .CornerRadius(Halide.Radius2).Background(Halide.Raised)
            .AlignItems(AlignItems.Center).JustifyContent(JustifyContent.Center)
            .Cursor(CursorType.Pointer)
            .AccessibleRole(AccessibilityRole.Button)
            .AccessibleName($"{genre.Name} {(genre.Kind == DiscoverKind.Movie ? "films" : "series")}")
            .AutomationId($"genre-{(genre.Kind == DiscoverKind.Movie ? "movie" : "tv")}-{genre.Id}");
        card.Box.OverflowX = Overflow.Hidden;
        card.Box.OverflowY = Overflow.Hidden;
        card.IsFocusable = true;
        if (cursorIndex >= 0)
        {
            TrackCursor(card, cursorIndex);
            if (IsCursor(cursorIndex)) card.Outline(2f, Halide.Accent, offset: 3f);
        }

        var (image, status) = _posters.Get(RequestImages.Backdrop(genre.BackdropPath, (int)(GenreCardWidth * 2), RequestImageBase));
        if (status == PosterCache.Status.Ready && image is { IsLoaded: true })
        {
            var picture = Image.New(image);
            picture.Fit = ImageFit.Cover;
            picture.Position.Type = PositionType.Absolute;
            picture.Position.Left = Dimension.Px(0);
            picture.Position.Top = Dimension.Px(0);
            card.AddChild(picture.Size(Dimension.Percent(100), Dimension.Percent(100)));
        }
        var scrim = Box.New().Size(Dimension.Percent(100), Dimension.Percent(100)).Background(Halide.Alpha(Halide.Bg, 0.55));
        scrim.IsHitTestVisible = false;
        scrim.Position.Type = PositionType.Absolute;
        scrim.Position.Left = Dimension.Px(0);
        scrim.Position.Top = Dimension.Px(0);
        card.AddChild(scrim);
        card.AddChild(Text.New(genre.Name).FontSize(17f).FontWeight(FontWeight.SemiBold).TextColor(Halide.Text)
            .MaxLines(2).TextOverflow(TextOverflow.Ellipsis));

        card.OnClick(_ => OpenGenre(genre));
        return card.HoverBg(Halide.Raised);
    }

    /// <summary>"See all", at the end of a custom row's heading: the row's first page is all
    /// a row shows, and the rest is a wall of its own. A real button, small, so Enter and
    /// Space open it as they do any other.</summary>
    private Element SeeAllLink(DiscoverRow row)
    {
        var link = Ui.Button(ButtonVariant.Ghost, "See all",
            () => Navigate(new Route(ApertureView.DiscoverAll,
                row.Slider!.Id.ToString(System.Globalization.CultureInfo.InvariantCulture), row.Title)),
            $"discover-row-{row.Key}-see-all", style: SeeAllStyle);
        link.AccessibleName = $"See all of {row.Title}";
        return link;
    }

    private static readonly ButtonStyle SeeAllStyle = new()
    {
        Padding = new Thickness(4, 8, 4, 8),
        FontSize = 12f,
        Text = Halide.Accent300,
    };

    /// <summary>A custom row in full: its titles as a wall, a page at a time. The row is
    /// found in the layout as last loaded — the route carries only its id.</summary>
    private Element BuildDiscoverAllView(Route route)
    {
        int id = int.TryParse(route.Id, System.Globalization.NumberStyles.Integer,
            System.Globalization.CultureInfo.InvariantCulture, out var parsed) ? parsed : int.MinValue;
        EnsureDiscoverRows();
        if (DiscoverRows.FirstOrDefault(r => r.Slider?.Id == id)?.Slider is not { } slider)
        {
            SetCursorList("see-all:empty", [], 1);
            var column = Flex.Column(gap: 0).FlexGrow(1).AutomationId("see-all-view");
            column.AddChild(ViewBar(route.Title ?? "Discover", 0, "see-all-view"));
            column.AddChild(DiscoverRowsLoading
                ? EmptyState(Icons.Loading, $"Asking {RequestServerTitle}…", null, "see-all-view-empty")
                : EmptyState(Icons.Discover, "That row is gone",
                    $"It is no longer in {RequestServerTitle}'s Discover layout.", "see-all-view-empty"));
            return column;
        }
        EnsureSliderPage(slider);
        return TitleWall(route.Title ?? slider.Title ?? "Discover", WallItems, WallLoading, WallHasMore,
            () => { EnsureSliderPage(slider, more: true); Rerender(); }, "see-all-view", Icons.Discover,
            "Nothing here now", $"{RequestServerTitle} has nothing to show for this row just now.");
    }

    private const float CompanyCardWidth = 224f;

    /// <summary>
    /// A studio or network: its logo, white, on a plain card — as the server's own web page
    /// draws them, and a row of logos reads as "places to go" as the genre backdrops do. The
    /// name stands in for a logo that has not loaded, or will not.
    /// </summary>
    private Element CompanyCard(DiscoverCompany company, int cursorIndex = -1)
    {
        var kind = company.Kind == DiscoverKind.Movie ? "films" : "series";
        var card = Box.New().Width(CompanyCardWidth).Height(128).FlexShrink(0)
            .Padding(new Thickness(28, 32, 28, 32))
            .CornerRadius(Halide.Radius2).Background(Halide.Surface)
            .BorderColor(Halide.Border).BorderWidth(1f)
            .AlignItems(AlignItems.Center).JustifyContent(JustifyContent.Center)
            .Cursor(CursorType.Pointer)
            .AccessibleRole(AccessibilityRole.Button)
            .AccessibleName($"{company.Name} {kind}")
            .AutomationId($"company-{company.Key.Replace(':', '-')}");
        card.IsFocusable = true;
        if (cursorIndex >= 0)
        {
            TrackCursor(card, cursorIndex);
            if (IsCursor(cursorIndex)) card.Outline(2f, Halide.Accent, offset: 3f);
        }

        var (image, status) = _posters.Get(RequestImages.Logo(company.LogoPath, (int)(CompanyCardWidth * 2), RequestImageBase));
        if (status == PosterCache.Status.Ready && image is { IsLoaded: true })
        {
            var logo = Image.New(image);
            logo.Fit = ImageFit.Contain;
            card.AddChild(logo.Size(Dimension.Percent(100), Dimension.Percent(100)));
        }
        else
        {
            card.AddChild(Text.New(company.Name).FontSize(16f).FontWeight(FontWeight.SemiBold).TextColor(Halide.TextDim)
                .MaxLines(2).TextOverflow(TextOverflow.Ellipsis));
        }

        card.OnClick(_ => OpenCompany(company));
        return card.HoverBg(Halide.Raised);
    }

    internal void OpenCompany(DiscoverCompany company) =>
        Navigate(new Route(ApertureView.DiscoverCompany, company.Key, company.Name));

    /// <summary>A studio's films or a network's series, most popular first, a page at a time.</summary>
    private Element BuildDiscoverCompanyView(Route route)
    {
        if (!DiscoverItem.TryParseKey(route.Id, out var kind, out var companyId))
            return EmptyState(Icons.Warning, "That isn't a studio or a network", null, "company-bad");
        EnsureCompanyPage(kind, companyId);
        var name = route.Title ?? (kind == DiscoverKind.Movie ? "Studio" : "Network");
        return TitleWall($"{name} {(kind == DiscoverKind.Movie ? "films" : "series")}", WallItems, WallLoading, WallHasMore,
            () => { EnsureCompanyPage(kind, companyId, more: true); Rerender(); }, "company-view", Icons.Discover,
            "Nothing here", $"{RequestServerTitle} has nothing to show for {name}.");
    }

    internal void OpenGenre(DiscoverGenre genre) =>
        Navigate(new Route(ApertureView.DiscoverGenre,
            $"{(genre.Kind == DiscoverKind.Movie ? "movie" : "tv")}:{genre.Id}",
            $"{genre.Name} {(genre.Kind == DiscoverKind.Movie ? "films" : "series")}"));

    /// <summary>A genre: its titles as a wrap of posters, most popular first, a page at a
    /// time with "Show more" — TMDB has thousands of dramas, and nobody scrolls past a few
    /// hundred.</summary>
    private Element BuildDiscoverGenreView(Route route)
    {
        if (!DiscoverItem.TryParseKey(route.Id, out var kind, out var genreId))
            return EmptyState(Icons.Warning, "That isn't a genre", null, "genre-bad");
        EnsureGenrePage(kind, genreId);
        return TitleWall(route.Title ?? "Genre", WallItems, WallLoading, WallHasMore,
            () => { EnsureGenrePage(kind, genreId, more: true); Rerender(); }, "genre-view", Icons.Discover,
            "Nothing in that genre", $"{RequestServerTitle} has nothing to show under it.");
    }

    /// <summary>
    /// A person: their picture, what they are known for, the biography TMDB keeps, and
    /// everything they are credited with — the discovery twin of the library's person
    /// page, reaching past what the library holds.
    /// </summary>
    private Element BuildDiscoverPersonView(Route route)
    {
        if (!int.TryParse(route.Id, System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture, out var tmdbId))
            return EmptyState(Icons.Warning, "That isn't a person", null, "person-bad");
        EnsurePerson(tmdbId);
        var person = Person;
        if (person is null || person.TmdbId != tmdbId)
            return EmptyState(PersonLoading ? Icons.Loading : Icons.Warning,
                PersonLoading ? "Loading…" : $"{RequestServerTitle} doesn't know that person", null, "person-loading");

        var column = Flex.Column(gap: 0).FlexGrow(1).AutomationId("discover-person");
        column.AddChild(ViewBar(person.Name, person.Credits.Count, "discover-person"));

        var scroll = ScrollView.Vertical().FlexGrow(1);
        var page = Flex.Column(gap: Halide.S5).Padding(new Thickness(Halide.S5));

        var head = Flex.Row(gap: Halide.S5).AlignItems(AlignItems.FlexStart);
        var photo = Box.New().Width(150).Height(210).FlexShrink(0).CornerRadius(Halide.Radius3).Background(Halide.Raised)
            .AlignItems(AlignItems.Center).JustifyContent(JustifyContent.Center);
        photo.Box.OverflowX = Overflow.Hidden;
        photo.Box.OverflowY = Overflow.Hidden;
        var (image, status) = _posters.Get(RequestImages.Profile(person.ProfilePath, 300, RequestImageBase));
        if (status == PosterCache.Status.Ready && image is { IsLoaded: true })
        {
            var picture = Image.New(image);
            picture.Fit = ImageFit.Cover;
            photo.AddChild(picture.Size(Dimension.Percent(100), Dimension.Percent(100)));
        }
        else photo.AddChild(Ui.Icon(Icons.User, 36f, Halide.TextFaint));
        head.AddChild(photo);

        var facts = Flex.Column(gap: Halide.S2).FlexGrow(1).FlexShrink(1);
        var line = new List<string>();
        if (person.KnownFor is { } known) line.Add(known);
        if (person.Birthday is { } born)
            line.Add(person.Deathday is { } died ? $"{born.Year}–{died.Year}" : $"born {born:d MMMM yyyy}");
        if (person.PlaceOfBirth is { } place) line.Add(place);
        if (line.Count > 0) facts.AddChild(Ui.Line(string.Join(" · ", line), 12.5f, Halide.TextMuted));
        if (person.Biography is { } bio)
            facts.AddChild(Text.New(bio).FontSize(13f).TextColor(Halide.TextDim).MaxWidth(Dimension.Px(820))
                .MaxLines(9).TextOverflow(TextOverflow.Ellipsis));
        head.AddChild(facts);
        page.AddChild(head);

        var credits = person.Credits;
        if (credits.Count > 0)
        {
            SetCursorList("person:" + tmdbId, credits, VisibleCards(150f));
            page.AddChild(Ui.SectionHeading("Films and series"));
            var wrap = Flex.Row(gap: Halide.PosterGap).AlignItems(AlignItems.FlexStart);
            wrap.Flex.Wrap = FlexWrap.Wrap;
            wrap.Gap(Halide.PosterGap);
            for (int i = 0; i < credits.Count; i++) wrap.AddChild(DiscoverCard(credits[i], 150f, cursorIndex: i));
            page.AddChild(wrap);
        }
        else SetCursorList("person:" + tmdbId + ":none", [], 1);

        scroll.AddChild(page);
        column.AddChild(scroll);
        return column;
    }

    /// <summary>A page's top bar — back, the title, a count — as the library's shelves have.</summary>
    private Element ViewBar(string title, int count, string automationId)
    {
        var bar = Flex.Row(gap: Halide.S3).FlexShrink(0)
            .Height(56).AlignItems(AlignItems.Center)
            .Padding(new Thickness(0, Halide.S5, 0, Halide.S5))
            .BorderColor(Halide.Border).BorderWidth(new Thickness(0, 0, 1, 0));
        bar.AddChild(Ui.IconButton(Icons.Back, "Back (Esc)", () => GoBack(), 15f, Halide.TextMuted, automationId + "-back"));
        bar.AddChild(Text.New(title).FontSize(18f).FontWeight(FontWeight.SemiBold).TextColor(Halide.Text));
        if (count > 0) bar.AddChild(Ui.Mono(count.ToString(), 11.5f, Halide.TextFaint));
        return bar;
    }

    /// <summary>A titled wall of discovery posters with a "Show more" at its foot — a genre.</summary>
    private Element TitleWall(string title, IReadOnlyList<DiscoverItem> items, bool loading, bool hasMore,
        Action showMore, string automationId, string glyph, string emptyHeadline, string emptyHint)
    {
        var column = Flex.Column(gap: 0).FlexGrow(1).AutomationId(automationId);
        column.AddChild(ViewBar(title, items.Count, automationId));

        if (items.Count == 0)
        {
            SetCursorList(automationId + ":empty", [], 1);
            column.AddChild(EmptyState(loading ? Icons.Loading : glyph,
                loading ? $"Asking {RequestServerTitle}…" : emptyHeadline, loading ? null : emptyHint,
                automationId + "-empty"));
            return column;
        }

        var scroll = ScrollView.Vertical().FlexGrow(1);
        var page = Flex.Column(gap: Halide.S4).Padding(new Thickness(Halide.S5));
        var wrap = Flex.Row(gap: Halide.PosterGap).AlignItems(AlignItems.FlexStart);
        wrap.Flex.Wrap = FlexWrap.Wrap;
        wrap.Gap(Halide.PosterGap);
        SetCursorList($"{automationId}:{title}", items, VisibleCards(150f));
        for (int i = 0; i < items.Count; i++) wrap.AddChild(DiscoverCard(items[i], 150f, cursorIndex: i));
        page.AddChild(wrap);
        if (hasMore)
            page.AddChild(Ui.Button(ButtonVariant.Secondary, loading ? "Loading…" : "Show more",
                loading ? null : showMore, automationId + "-more"));
        scroll.AddChild(page);
        column.AddChild(scroll);
        return column;
    }

    /// <summary>A request as a card on the "Your requests" shelf: the title's own state,
    /// which is newer than the request's — asked for last week and since arrived reads as
    /// available.</summary>
    private static DiscoverItem RequestAsItem(MediaRequest request) => new()
    {
        TmdbId = request.TmdbId,
        Kind = request.Kind,
        Title = request.Title ?? "",
        Year = request.Year,
        PosterPath = request.PosterPath,
        Availability = request.Availability is Availability.Unknown && request.Status == RequestStatus.Pending
            ? Availability.Pending
            : request.Availability,
        JellyfinItemId = request.JellyfinItemId,
    };

    // ── The card ─────────────────────────────────────────────────────────

    /// <summary>
    /// A discovery poster: TMDB's artwork, the title, and a badge for where it stands.
    /// Deliberately the library card's twin — same size, same caption — so a shelf of
    /// things you could have reads like a shelf of things you do.
    /// </summary>
    internal Element DiscoverCard(DiscoverItem item, float width = Halide.PosterWidth, int cursorIndex = -1)
    {
        float height = MathF.Round(width * 1.5f);
        var card = Flex.Column(gap: Halide.S2)
            .Width(width).FlexShrink(0)
            .CornerRadius(Halide.Radius2)
            .Cursor(CursorType.Pointer)
            .AccessibleRole(AccessibilityRole.Button)
            .AccessibleName(DiscoverAccessibleName(item))
            .AutomationId("discover-card-" + item.Key)
            .Tooltip(item.Title);
        card.IsFocusable = true;
        card.ContextMenu(() => DiscoverMenu(item));
        if (cursorIndex >= 0)
        {
            TrackCursor(card, cursorIndex);
            if (IsCursor(cursorIndex)) card.Outline(2f, Halide.Accent, offset: 3f);
        }

        var art = Box.New()
            .Width(width).Height(height).FlexShrink(0)
            .CornerRadius(Halide.Radius2)
            .Background(Halide.Raised)
            .AlignItems(AlignItems.Center).JustifyContent(JustifyContent.Center);
        art.Box.OverflowX = Overflow.Hidden;
        art.Box.OverflowY = Overflow.Hidden;
        var (image, status) = _posters.Get(RequestImages.Poster(item.PosterPath, (int)(width * 2), RequestImageBase));
        if (status == PosterCache.Status.Ready && image is { IsLoaded: true })
        {
            var picture = Image.New(image);
            picture.Fit = ImageFit.Cover;
            art.AddChild(picture.Size(Dimension.Percent(100), Dimension.Percent(100)));
        }
        else
        {
            art.AddChild(Ui.Icon(item.Kind == DiscoverKind.Movie ? Icons.Movies : Icons.Shows,
                MathF.Min(40f, width * 0.28f), Halide.TextFaint));
        }
        if (AvailabilityBadge(item.Availability) is { } badge) art.AddChild(badge);
        card.AddChild(art);

        if (_settings.Data.ShowCardTitles)
        {
            var caption = Flex.Column(gap: 1).Width(width);
            caption.AddChild(Ui.Line(item.Title, 12.5f, Halide.Text));
            var sub = item.Kind == DiscoverKind.Movie ? "Film" : "Series";
            if (item.Year is { } year) sub = $"{year} · {sub}";
            caption.AddChild(Ui.Line(sub, 11.5f, Halide.TextMuted));
            card.AddChild(caption);
        }

        card.OnClick(_ => OpenDiscover(item));
        return card;
    }

    /// <summary>The badge in a card's corner, or null for a title nobody has asked for —
    /// the ordinary case on a discovery shelf, and not worth a mark.</summary>
    private static Element? AvailabilityBadge(Availability availability)
    {
        var (glyph, colour) = availability switch
        {
            Availability.Available => (Icons.Available, Halide.StatusOk),
            Availability.PartiallyAvailable => (Icons.Available, Halide.StatusWarn),
            Availability.Pending => (Icons.Pending, Halide.Accent),
            Availability.Processing => (Icons.Processing, Halide.Accent300),
            Availability.Blocklisted => (Icons.Blocklisted, Halide.StatusFail),
            _ => ((string?)null, Halide.TextFaint),
        };
        if (glyph is null) return null;

        var badge = Box.New().Width(24).Height(24)
            .CornerRadius(12f)
            .Background(Halide.Alpha(Halide.Bg, 0.78))
            .AlignItems(AlignItems.Center).JustifyContent(JustifyContent.Center);
        badge.AddChild(Ui.Icon(glyph, 14f, colour));
        badge.IsHitTestVisible = false;
        badge.Position.Type = PositionType.Absolute;
        badge.Position.Right = Dimension.Px(Halide.S2);
        badge.Position.Top = Dimension.Px(Halide.S2);
        return badge;
    }

    /// <summary>Where a title stands, as a phrase — the badge's words, for a screen reader
    /// and for the detail page's status line.</summary>
    internal static string AvailabilityText(Availability availability) => availability switch
    {
        Availability.Available => "In your library",
        Availability.PartiallyAvailable => "Partly in your library",
        Availability.Pending => "Requested, waiting for approval",
        Availability.Processing => "Requested, on its way",
        Availability.Blocklisted => "Not available to request",
        Availability.Deleted => "Removed from your library",
        _ => "",
    };

    private static string DiscoverAccessibleName(DiscoverItem item)
    {
        var parts = new List<string> { item.Title };
        parts.Add(item.Kind == DiscoverKind.Movie ? "film" : "series");
        if (item.Year is { } year) parts.Add(year.ToString());
        if (AvailabilityText(item.Availability) is { Length: > 0 } state) parts.Add(state.ToLowerInvariant());
        return string.Join(", ", parts);
    }

    /// <summary>Opens a title: the library's own page when the library has all of it, the
    /// discovery page otherwise — including a series with only some seasons in, whose
    /// discovery page offers both the library and the seasons still to ask for.</summary>
    internal void OpenDiscover(DiscoverItem item)
    {
        if (item.InLibrary && item.Availability == Availability.Available && _library is not null)
            Navigate(new Route(ApertureView.Detail, item.JellyfinItemId, item.Title));
        else
            Navigate(new Route(ApertureView.DiscoverTitle, item.Key, item.Title));
    }

    private IReadOnlyList<ContextMenuItem> DiscoverMenu(DiscoverItem item)
    {
        var items = new List<ContextMenuItem>();
        if (item.InLibrary)
            items.Add(new ContextMenuItem
            {
                Label = "Open in your library",
                Action = () => Navigate(new Route(ApertureView.Detail, item.JellyfinItemId, item.Title)),
            });
        items.Add(new ContextMenuItem
        {
            Label = $"Open on {RequestServerTitle}",
            Action = () => Navigate(new Route(ApertureView.DiscoverTitle, item.Key, item.Title)),
        });
        if (item.Kind == DiscoverKind.Movie && CanRequestNow(item))
        {
            items.Add(ContextMenuItem.Separator());
            items.Add(new ContextMenuItem
            {
                Label = "Request",
                Action = () => MakeRequest(new NewRequest(DiscoverKind.Movie, item.TmdbId, []), item.Title),
            });
        }
        return items;
    }

    /// <summary>Whether a title can be requested as it stands: nobody has asked for it,
    /// and this user is allowed to. A series with some seasons in is decided on its
    /// detail page, where the seasons are.</summary>
    private bool CanRequestNow(DiscoverItem item) =>
        _requestServer is { } server
        && server.User.CanRequest(item.Kind)
        && item.Availability is Availability.Unknown or Availability.Deleted;

    // ── The title page ───────────────────────────────────────────────────

    /// <summary>The series whose season picker is open, by key; null when none is. Kept
    /// on the app rather than the page so a rebuild does not close it.</summary>
    private string? _seasonPickerFor;
    private readonly HashSet<int> _seasonPick = [];

    private Element BuildDiscoverTitleView(Route route)
    {
        if (!DiscoverItem.TryParseKey(route.Id, out var kind, out var tmdbId))
            return EmptyState(Icons.Warning, "That isn't a title", null, "discover-title-bad");
        EnsureDiscoverDetail(kind, tmdbId);

        var detail = DiscoverDetail;
        if (detail is null || detail.Item.Key != route.Id)
            return EmptyState(DiscoverDetailLoading ? Icons.Loading : Icons.Warning,
                DiscoverDetailLoading ? "Loading…" : $"{RequestServerTitle} doesn't know that title",
                null, "discover-title-loading");

        var item = detail.Item;
        // Something on its way re-reads itself while the page is up, so the bar moves.
        KeepProgressFresh(detail.Downloads.Count + detail.Downloads4k.Count > 0);
        if (_requestServer?.User.CanRequest(item.Kind) == true) EnsureQuota();

        var scroll = ScrollView.Vertical().FlexGrow(1).AutomationId("discover-title");
        var page = Flex.Column(gap: 0);
        page.AddChild(DiscoverHero(detail));

        var body = Flex.Column(gap: Halide.S5).Padding(new Thickness(Halide.S5));

        if (_optionsFor == item.Key && item.Kind == DiscoverKind.Movie)
            body.AddChild(RequestOptionsCard(detail));
        if (_seasonPickerFor == item.Key && item.Kind == DiscoverKind.Tv)
            body.AddChild(SeasonPicker(detail));

        if (detail.Tagline is { Length: > 0 } tagline)
            body.AddChild(Text.New(tagline).FontSize(14f).Italic().TextColor(Halide.TextDim));
        if (item.Overview is { Length: > 0 } overview)
            body.AddChild(Text.New(overview).FontSize(13.5f).TextColor(Halide.TextDim).MaxWidth(Dimension.Px(820)));

        if (detail.Requests.Count > 0) body.AddChild(TitleRequests(detail));

        if (item.Kind == DiscoverKind.Tv && detail.Seasons.Count > 0 && _seasonPickerFor != item.Key)
            body.AddChild(SeasonList(detail));

        if (detail.Cast.Count > 0)
        {
            var cast = detail.Cast.Take(16).ToList();
            body.AddChild(CardRow("Cast", cast.Count, i => CastCard(cast[i]), 110f, "discover-cast"));
        }

        // The arrows walk the recommendations — the one shelf on the page — and nothing at
        // all when there are none, rather than the cards of the view before.
        var recs = Recommendations;
        SetCursorList($"recs:{item.Key}:{recs.Count}", recs, VisibleCards(Halide.PosterWidth));
        if (recs.Count > 0)
        {
            body.AddChild(CardRow("More like this", recs.Count,
                i => DiscoverCard(recs[i], Halide.PosterWidth, cursorIndex: i),
                Halide.PosterWidth, "discover-recs"));
        }

        page.AddChild(body);
        scroll.AddChild(page);
        return scroll;
    }

    private Element DiscoverHero(DiscoverDetail detail)
    {
        var item = detail.Item;
        float heroHeight = Math.Clamp(_window.LayoutHeight * 0.44f, 280f, 420f);
        var hero = Box.New().Height(heroHeight).FlexShrink(0)
            .AlignItems(AlignItems.Stretch)
            .Background(Halide.Raised);

        var (image, status) = _posters.Get(RequestImages.Backdrop(item.BackdropPath, (int)_window.LayoutWidth, RequestImageBase));
        if (status == PosterCache.Status.Ready && image is { IsLoaded: true })
        {
            var picture = Image.New(image);
            picture.Fit = ImageFit.Cover;
            hero.AddChild(picture.Size(Dimension.Percent(100), Dimension.Percent(100)));
        }

        foreach (var vertical in new[] { true, false })
        {
            var scrim = Box.New().Size(Dimension.Percent(vertical ? 100 : 70), Dimension.Percent(100));
            scrim.BackgroundGradient = Halide.BackdropScrim(vertical);
            scrim.IsHitTestVisible = false;
            scrim.Position.Type = PositionType.Absolute;
            scrim.Position.Left = Dimension.Px(0);
            scrim.Position.Top = Dimension.Px(0);
            hero.AddChild(scrim);
        }

        var content = Flex.Row(gap: Halide.S5)
            .Padding(new Thickness(Halide.S5))
            .AlignItems(AlignItems.FlexEnd)
            .Size(Dimension.Percent(100), Dimension.Percent(100));
        content.Position.Type = PositionType.Absolute;
        content.Position.Left = Dimension.Px(0);
        content.Position.Bottom = Dimension.Px(0);

        var poster = Box.New().Width(150).Height(225).FlexShrink(0)
            .CornerRadius(Halide.Radius3).Background(Halide.Raised)
            .AlignItems(AlignItems.Center).JustifyContent(JustifyContent.Center);
        poster.Box.OverflowX = Overflow.Hidden;
        poster.Box.OverflowY = Overflow.Hidden;
        var (posterImage, posterStatus) = _posters.Get(RequestImages.Poster(item.PosterPath, 300, RequestImageBase));
        if (posterStatus == PosterCache.Status.Ready && posterImage is { IsLoaded: true })
        {
            var picture = Image.New(posterImage);
            picture.Fit = ImageFit.Cover;
            poster.AddChild(picture.Size(Dimension.Percent(100), Dimension.Percent(100)));
        }
        else
        {
            poster.AddChild(Ui.Icon(item.Kind == DiscoverKind.Movie ? Icons.Movies : Icons.Shows, 36f, Halide.TextFaint));
        }
        content.AddChild(poster);

        var block = Flex.Column(gap: Halide.S3).FlexShrink(1).FlexGrow(1);
        block.AddChild(Text.New(item.Title).FontSize(30f).FontWeight(FontWeight.Bold).TextColor(Halide.Text)
            .MaxLines(2).TextOverflow(TextOverflow.Ellipsis));

        var meta = Flex.Row(gap: Halide.S2).AlignItems(AlignItems.Center);
        meta.AddChild(Ui.Chip(item.Kind == DiscoverKind.Movie ? "Film" : "Series"));
        if (item.Year is { } year) meta.AddChild(Ui.Chip(year.ToString()));
        if (detail.Runtime is { } run) meta.AddChild(Ui.Chip(Ui.Runtime(run)));
        if (item.Kind == DiscoverKind.Tv && detail.Seasons.Count > 0)
            meta.AddChild(Ui.Chip($"{detail.Seasons.Count} season{(detail.Seasons.Count == 1 ? "" : "s")}"));
        if (item.VoteAverage is { } score and > 0)
        {
            var star = Flex.Row(gap: 4).AlignItems(AlignItems.Center).FlexShrink(0);
            star.AddChild(Ui.Icon(Icons.Rating, 12f, Halide.Accent));
            star.AddChild(Ui.Mono(score.ToString("0.0"), 11.5f, Halide.TextDim));
            meta.AddChild(star);
        }
        if (detail.Genres.Count > 0)
            meta.AddChild(Ui.Line(string.Join(" · ", detail.Genres.Take(3)), 12f, Halide.TextMuted));
        block.AddChild(meta);

        if (AvailabilityText(item.Availability) is { Length: > 0 } state)
        {
            var line = Flex.Row(gap: 6).AlignItems(AlignItems.Center).AutomationId("discover-state");
            line.AddChild(Ui.Icon(item.Availability switch
            {
                Availability.Available or Availability.PartiallyAvailable => Icons.Available,
                Availability.Pending => Icons.Pending,
                Availability.Processing => Icons.Processing,
                Availability.Blocklisted => Icons.Blocklisted,
                _ => Icons.Warning,
            }, 14f, item.Availability is Availability.Available ? Halide.StatusOk : Halide.Accent300));
            line.AddChild(Ui.Line(state, 12.5f, Halide.TextDim));
            block.AddChild(line);
        }

        // How far along it is, when Radarr or Sonarr is fetching it now.
        foreach (var (downloads, label) in new[] { (detail.Downloads, ""), (detail.Downloads4k, "4K · ") })
            if (DownloadSummary.Of(downloads) is { } summary)
                block.AddChild(DownloadBar(summary, label, 320f, "discover-download" + (label.Length > 0 ? "-4k" : "")));

        block.AddChild(DiscoverActions(detail));
        content.AddChild(block);
        hero.AddChild(content);

        var back = Ui.IconButton(Icons.Back, "Back (Esc)", () => GoBack(), 16f, Halide.Text, "discover-title-back");
        back.Background(Halide.Alpha(Halide.Bg, 0.55)).CornerRadius(Halide.Radius1);
        back.Position.Type = PositionType.Absolute;
        back.Position.Left = Dimension.Px(Halide.S4);
        back.Position.Top = Dimension.Px(Halide.S4);
        hero.AddChild(back);
        return hero;
    }

    /// <summary>
    /// The verbs for a title, in order of what someone most likely came to do: watch it if
    /// it is here, ask for it if it is not, and — for someone who approves requests — say
    /// yes or no to a request that is waiting.
    /// </summary>
    private Element DiscoverActions(DiscoverDetail detail)
    {
        var item = detail.Item;
        var row = Flex.Row(gap: Halide.S2).AlignItems(AlignItems.Center);
        var user = _requestServer?.User;
        // A spent allowance is said before the button is pressed, not by the server after.
        var quota = QuotaFor(item.Kind);
        bool spent = quota?.Restricted == true;
        bool offered = false;

        ButtonElement RequestButton(ButtonVariant variant, string label, Action onClick, string automationId)
        {
            offered = true;
            var b = Ui.Button(variant, label, spent ? null : onClick, automationId, Icons.Request);
            b.IsEnabled = !spent;
            return b;
        }

        if (item.InLibrary && _library is not null)
            row.AddChild(Ui.Button(ButtonVariant.Primary, "Open in your library",
                () => Navigate(new Route(ApertureView.Detail, item.JellyfinItemId, item.Title)),
                "discover-open-library", Icons.Play));

        if (user is not null && user.CanRequest(item.Kind))
        {
            if (item.Kind == DiscoverKind.Movie)
            {
                if (item.Availability is Availability.Unknown or Availability.Deleted)
                    row.AddChild(RequestButton(ButtonVariant.Primary, "Request",
                        () => MakeRequest(new NewRequest(DiscoverKind.Movie, item.TmdbId, []), item.Title),
                        "discover-request"));
                if (CanRequestAdvanced && !spent && item.Availability is Availability.Unknown or Availability.Deleted)
                    row.AddChild(Ui.IconButton(Icons.Options, "Request with options…", () => OpenRequestOptions(item, false),
                        15f, Halide.TextDim, "discover-request-options"));
            }
            else if (RequestableSeasons(detail).Count > 0)
            {
                bool some = detail.Seasons.Any(s => s.Availability is not (Availability.Unknown or Availability.Deleted));
                var label = some ? "Request more seasons" : "Request";
                row.AddChild(RequestButton(item.InLibrary ? ButtonVariant.Secondary : ButtonVariant.Primary, label,
                    () => OpenSeasonPicker(detail), "discover-request"));
            }

            if (Can4k(item) && item.Availability4k is Availability.Unknown or Availability.Deleted
                && item.Kind == DiscoverKind.Movie)
                row.AddChild(RequestButton(ButtonVariant.Secondary, "Request in 4K",
                    () => MakeRequest(new NewRequest(DiscoverKind.Movie, item.TmdbId, [], Is4k: true), item.Title + " (4K)"),
                    "discover-request-4k"));
        }

        // The watchlist: for something to watch later that nobody has fetched yet — once
        // it is in the library there is nothing left to be reminded of.
        if (_requestServer is not null && !(item.InLibrary && item.Availability == Availability.Available))
            row.AddChild(ToggleButton(detail.OnWatchlist ? Icons.WatchlistOn : Icons.Watchlist,
                detail.OnWatchlist ? "On your watchlist" : "Add to your watchlist", detail.OnWatchlist,
                () => SetWatchlist(item, !detail.OnWatchlist), "discover-watchlist"));

        // The allowance, under the verbs — only where there is something to request with it.
        if (offered && quota is not null)
        {
            var column = Flex.Column(gap: Halide.S2);
            column.AddChild(row);
            column.AddChild(Text.New(QuotaPhrase(quota, item.Kind))
                .FontSize(11.5f).TextColor(spent ? Halide.StatusWarn : Halide.TextMuted)
                .AutomationId("discover-quota"));
            AddDecisions(row, detail, user);
            return column;
        }

        AddDecisions(row, detail, user);
        return row;
    }

    /// <summary>"3 of 5 film requests left this week", or — spent — "No film requests left
    /// this week (the limit is 5)". Series are counted in seasons, as the server counts them.</summary>
    internal static string QuotaPhrase(RequestQuota quota, DiscoverKind kind)
    {
        var what = kind == DiscoverKind.Movie ? "film" : "season";
        if (quota.Restricted || quota.Remaining is 0)
            return $"No {what} requests left {quota.Period} (the limit is {quota.Limit})";
        var left = quota.Remaining ?? Math.Max(0, (quota.Limit ?? 0) - quota.Used);
        return $"{left} of {quota.Limit} {what} request{(quota.Limit == 1 ? "" : "s")} left {quota.Period}";
    }

    /// <summary>
    /// A download's progress: the bar, then "42% · about 12 min left". Deliberately a thin
    /// accent track rather than a filled block — it is the same mark as a half-watched film's
    /// sliver, and means the same thing: part of the way there.
    /// </summary>
    private static Element DownloadBar(DownloadSummary summary, string label, float width, string automationId)
    {
        var line = Flex.Column(gap: 5).FlexShrink(0).AutomationId(automationId)
            .AccessibleRole(HultUI.Core.Accessibility.AccessibilityRole.ProgressBar)
            .AccessibleName($"Downloading, {label}{summary.Phrase}");
        line.AddChild(Ui.ProgressSliver(summary.Fraction * 100, width));
        line.AddChild(Ui.Line($"Downloading · {label}{summary.Phrase}", 11.5f, Halide.TextMuted));
        return line;
    }

    /// <summary>A waiting request is decided right here, by someone who may.</summary>
    private void AddDecisions(BoxElement row, DiscoverDetail detail, RequestUser? user)
    {
        var item = detail.Item;
        if (user?.CanManageRequests == true
            && detail.Requests.FirstOrDefault(r => r.Status == RequestStatus.Pending) is { } waiting)
        {
            var named = waiting with { Title = item.Title };
            row.AddChild(Ui.Button(ButtonVariant.Primary, "Approve", () => DecideRequest(named, true),
                "discover-approve", Icons.Approve));
            row.AddChild(Ui.Button(ButtonVariant.Secondary, "Decline", () => DecideRequest(named, false),
                "discover-decline", Icons.Decline));
        }
    }

    private bool Can4k(DiscoverItem item) =>
        _requestServer is { } server
        && (item.Kind == DiscoverKind.Movie ? server.Server.Movie4kEnabled : server.Server.Series4kEnabled)
        && server.User.CanRequest4k(item.Kind);

    /// <summary>The seasons nobody has asked for and the library does not have.</summary>
    private static IReadOnlyList<DiscoverSeason> RequestableSeasons(DiscoverDetail detail) =>
        detail.Seasons.Where(s => s.Availability is Availability.Unknown or Availability.Deleted).ToList();

    private void OpenSeasonPicker(DiscoverDetail detail)
    {
        _seasonPickerFor = detail.Item.Key;
        ResetOptionChoices();
        _seasonPick.Clear();
        // Everything that can be asked for, ticked: "the rest of the series" is what
        // nearly everyone wants, and unticking one is easier than ticking eight. A rationed
        // user gets as many as the allowance covers, earliest first.
        var allowance = QuotaFor(DiscoverKind.Tv) is { } quota ? (quota.Restricted ? 0 : quota.Remaining) : null;
        foreach (var season in RequestableSeasons(detail).Take(allowance ?? int.MaxValue)) _seasonPick.Add(season.Number);
        Rerender();
    }

    /// <summary>
    /// Which seasons to ask for. Inline under the hero rather than a dialog: the seasons
    /// are what the page is about at that moment, and a dialog would hide the poster you
    /// are deciding over. A server with partial requests switched off takes the whole
    /// series or nothing, and the picker says so rather than offering boxes it will ignore.
    /// </summary>
    private Element SeasonPicker(DiscoverDetail detail)
    {
        var item = detail.Item;
        bool partial = _requestServer?.Server.PartialRequestsEnabled ?? true;
        var requestable = RequestableSeasons(detail);

        var card = Flex.Column(gap: Halide.S2)
            .Padding(new Thickness(Halide.S4))
            .CornerRadius(Halide.Radius2)
            .Background(Halide.Surface)
            .BorderColor(Halide.Border).BorderWidth(1f)
            .MaxWidth(Dimension.Px(560))
            .AutomationId("season-picker");

        var heading = Flex.Row(gap: Halide.S2).AlignItems(AlignItems.Center);
        heading.AddChild(Text.New(partial ? "Which seasons?" : "Request the series")
            .FontSize(14f).FontWeight(FontWeight.SemiBold).TextColor(Halide.Text));
        heading.AddChild(Box.New().FlexGrow(1));
        if (partial && requestable.Count > 1)
        {
            bool all = requestable.All(s => _seasonPick.Contains(s.Number));
            heading.AddChild(Ui.Button(ButtonVariant.Ghost, all ? "None" : "All", () =>
            {
                _seasonPick.Clear();
                if (!all) foreach (var s in requestable) _seasonPick.Add(s.Number);
                Rerender();
            }, "season-picker-all"));
        }
        card.AddChild(heading);

        foreach (var season in detail.Seasons)
        {
            bool open = season.Availability is Availability.Unknown or Availability.Deleted;
            bool ticked = open && (_seasonPick.Contains(season.Number) || !partial);
            var number = season.Number;

            var line = Flex.Row(gap: Halide.S3).AlignItems(AlignItems.Center)
                .Padding(new Thickness(6, Halide.S2, 6, Halide.S2))
                .CornerRadius(Halide.Radius1);
            if (open && partial)
            {
                // A real checkbox: its checked state is what a screen reader announces,
                // which a drawn tick is not.
                var check = Checkbox.New(ticked, season.Name);
                check.LabelGap = 10f;
                check.CheckedColor = Halide.Accent;
                check.CheckmarkColor = new Color(35, 23, 4);
                check.BoxColor = Halide.TextMuted;
                check.TextColor(Halide.Text).FontSize(12.5f);
                check.AutomationId = $"season-pick-{season.Number}";
                check.Key = $"season-pick-{season.Number}-{(ticked ? "on" : "off")}";
                check.OnCheckedChanged(on =>
                {
                    if (on) _seasonPick.Add(number); else _seasonPick.Remove(number);
                    Rerender();
                });
                // Not grown: a checkbox answers clicks on its box and label only, so bounds
                // stretched across the row would be a strip that looks live and is not.
                line.AddChild(check);
                line.AddChild(Box.New().FlexGrow(1));
            }
            else
            {
                // Not a choice: already in (or asked for), or a server that takes the
                // whole series — in which case every open season is going in the request.
                line.AddChild(Box.New().Width(18).Height(18).AlignItems(AlignItems.Center)
                    .JustifyContent(JustifyContent.Center)
                    .Children(Ui.Icon(open ? "check-square" : Icons.Available, 15f, open ? Halide.Accent : Halide.TextFaint)));
                line.AddChild(Ui.Line(season.Name, 12.5f, open ? Halide.Text : Halide.TextMuted).FlexGrow(1));
            }
            line.AddChild(Ui.Mono($"{season.EpisodeCount} ep", 11f, Halide.TextFaint));
            if (!open) line.AddChild(Ui.Chip(AvailabilityText(season.Availability)));
            card.AddChild(line);
        }

        var chosen = partial
            ? requestable.Where(s => _seasonPick.Contains(s.Number)).Select(s => s.Number).ToList()
            : requestable.Select(s => s.Number).ToList();

        // Seasons are what a series quota counts, so a rationed user is told how many they
        // have before ticking more than that — the server would refuse the whole request.
        // Where it goes, for someone who may say.
        if (CanRequestAdvanced) card.AddChild(OptionChips(DiscoverKind.Tv, false));

        var quota = QuotaFor(DiscoverKind.Tv);
        int? allowed = quota is null ? null : quota.Restricted ? 0 : quota.Remaining;
        bool over = allowed is { } cap && chosen.Count > cap;

        var actions = Flex.Row(gap: Halide.S2).AlignItems(AlignItems.Center);
        if (quota is not null)
            actions.AddChild(Ui.Line(over ? $"Only {allowed} left {quota.Period} — untick some" : QuotaPhrase(quota, DiscoverKind.Tv),
                11.5f, over || quota.Restricted ? Halide.StatusWarn : Halide.TextMuted).AutomationId("season-picker-quota"));
        actions.AddChild(Box.New().FlexGrow(1));
        actions.AddChild(Ui.Button(ButtonVariant.Secondary, "Cancel", () =>
        {
            _seasonPickerFor = null;
            Rerender();
        }, "season-picker-cancel"));
        bool ready = chosen.Count > 0 && !over;
        var confirm = Ui.Button(ButtonVariant.Primary,
            chosen.Count == 0 ? "Pick a season" : $"Request {chosen.Count} season{(chosen.Count == 1 ? "" : "s")}",
            !ready ? null : () => MakeRequest(
                new NewRequest(DiscoverKind.Tv, item.TmdbId, chosen, TvdbId: detail.TvdbId,
                    Options: CanRequestAdvanced ? CurrentOptions(DiscoverKind.Tv, false) : null), item.Title),
            "season-picker-confirm", Icons.Request);
        confirm.IsEnabled = ready;
        actions.AddChild(confirm);
        card.AddChild(actions);
        return card;
    }

    /// <summary>The seasons and where each stands — the picker's read-only twin, shown
    /// when the picker is closed.</summary>
    private static Element SeasonList(DiscoverDetail detail)
    {
        var column = Flex.Column(gap: Halide.S2).MaxWidth(Dimension.Px(560))
            .AutomationId("discover-seasons");
        column.AddChild(Ui.SectionHeading("Seasons"));
        foreach (var season in detail.Seasons)
        {
            var line = Flex.Row(gap: Halide.S3).AlignItems(AlignItems.Center);
            line.AddChild(Ui.Line(season.Name, 12.5f, Halide.TextDim).FlexGrow(1));
            if (season.Year is { } year) line.AddChild(Ui.Mono(year.ToString(), 11f, Halide.TextFaint));
            line.AddChild(Ui.Mono($"{season.EpisodeCount} ep", 11f, Halide.TextFaint));
            if (AvailabilityText(season.Availability) is { Length: > 0 } state) line.AddChild(Ui.Chip(state));
            column.AddChild(line);
        }
        return column;
    }

    /// <summary>The requests already made for this title, who made them, and what became of
    /// them — the answer to "has someone already asked for this?"</summary>
    private Element TitleRequests(DiscoverDetail detail)
    {
        var column = Flex.Column(gap: Halide.S2).MaxWidth(Dimension.Px(720))
            .AutomationId("discover-requests");
        column.AddChild(Ui.SectionHeading("Requests"));
        foreach (var request in detail.Requests)
        {
            var line = Flex.Row(gap: Halide.S3).AlignItems(AlignItems.Center);
            line.AddChild(RequestStatusChip(request));
            var who = $"{request.RequestedBy} · {Ui.Ago(request.CreatedAt)}";
            if (request.Seasons.Count > 0) who += " · " + SeasonsPhrase(request.Seasons);
            if (request.Is4k) who += " · 4K";
            line.AddChild(Ui.Line(who, 12f, Halide.TextMuted).FlexGrow(1));
            if (CanWithdraw(request))
                line.AddChild(Ui.Button(ButtonVariant.Ghost, "Withdraw",
                    () => WithdrawRequest(request with { Title = detail.Item.Title }),
                    $"discover-withdraw-{request.Id}", Icons.Withdraw));
            column.AddChild(line);
        }
        return column;
    }

    private Element CastCard(DiscoverPerson person)
    {
        const float width = 110f;
        var card = Flex.Column(gap: Halide.S2).Width(width).FlexShrink(0)
            .Cursor(CursorType.Pointer)
            .AccessibleRole(AccessibilityRole.Button)
            .AccessibleName(person.Role is { Length: > 0 } r ? $"{person.Name} as {r}" : person.Name)
            .AutomationId($"cast-{person.TmdbId}");
        // A name opens everything they are in — the library's cast row does the same.
        card.OnClick(_ => Navigate(new Route(ApertureView.DiscoverPerson,
            person.TmdbId.ToString(System.Globalization.CultureInfo.InvariantCulture), person.Name)));
        var art = Box.New().Width(width).Height(width * 1.4f).FlexShrink(0)
            .CornerRadius(Halide.Radius2).Background(Halide.Raised)
            .AlignItems(AlignItems.Center).JustifyContent(JustifyContent.Center);
        art.Box.OverflowX = Overflow.Hidden;
        art.Box.OverflowY = Overflow.Hidden;
        var (image, status) = _posters.Get(RequestImages.Profile(person.ProfilePath, (int)(width * 2), RequestImageBase));
        if (status == PosterCache.Status.Ready && image is { IsLoaded: true })
        {
            var picture = Image.New(image);
            picture.Fit = ImageFit.Cover;
            art.AddChild(picture.Size(Dimension.Percent(100), Dimension.Percent(100)));
        }
        else art.AddChild(Ui.Icon(Icons.User, 28f, Halide.TextFaint));
        card.AddChild(art);
        card.AddChild(Ui.Line(person.Name, 12f, Halide.Text));
        if (person.Role is { Length: > 0 } role) card.AddChild(Ui.Line(role, 11f, Halide.TextMuted));
        return card;
    }

    // ── Shared bits ──────────────────────────────────────────────────────

    /// <summary>"Season 1", "Seasons 1–3", "Seasons 1, 3, 4".</summary>
    internal static string SeasonsPhrase(IReadOnlyList<int> seasons)
    {
        if (seasons.Count == 0) return "";
        var sorted = seasons.Order().ToList();
        if (sorted.Count == 1) return $"Season {sorted[0]}";
        bool run = sorted.Zip(sorted.Skip(1)).All(p => p.Second == p.First + 1);
        return run ? $"Seasons {sorted[0]}–{sorted[^1]}" : "Seasons " + string.Join(", ", sorted);
    }

    /// <summary>A request's state as a chip: the request's own status while it is being
    /// decided, the title's once it has been approved — "approved" is not news to someone
    /// waiting for a film, "available" is.</summary>
    internal static Element RequestStatusChip(MediaRequest request)
    {
        var (text, colour) = request.Status switch
        {
            RequestStatus.Pending => ("Waiting for approval", Halide.Accent),
            RequestStatus.Declined => ("Declined", Halide.StatusFail),
            RequestStatus.Failed => ("Failed", Halide.StatusFail),
            _ when request.Availability == Availability.Available => ("Available", Halide.StatusOk),
            _ when request.Availability == Availability.PartiallyAvailable => ("Partly available", Halide.StatusWarn),
            RequestStatus.Completed => ("Completed", Halide.StatusOk),
            // Downloading now: say how far, which is the question "on its way" leaves open.
            _ when DownloadSummary.Of(request.Downloads) is { } downloading =>
                ($"On its way · {Math.Round(downloading.Fraction * 100):0}%", Halide.Accent300),
            _ => ("On its way", Halide.Accent300),
        };
        return Ui.Chip(text, colour);
    }

    /// <summary>
    /// A request still waiting — your own, or anyone's for a manager. Only while it waits:
    /// the server lets a manager delete a request that has been approved or fulfilled too,
    /// but that erases the household's record of it, and a one-click bin on every row of a
    /// list is how that happens by accident. Deciding a waiting one is Decline's job.
    /// </summary>
    private bool CanWithdraw(MediaRequest request) =>
        _requestServer is { } server
        && request.Status == RequestStatus.Pending
        && (server.User.CanManageRequests || request.RequestedById == server.User.Id);

    private static Element EmptyState(string glyph, string headline, string? hint, string automationId)
    {
        var column = Flex.Column(gap: Halide.S2).FlexGrow(1)
            .AlignItems(AlignItems.Center).JustifyContent(JustifyContent.Center)
            .AutomationId(automationId);
        column.AddChild(Ui.Icon(glyph, 24f, Halide.TextFaint));
        column.AddChild(Text.New(headline).FontSize(15f).FontWeight(FontWeight.SemiBold).TextColor(Halide.TextDim));
        if (hint is not null) column.AddChild(Text.New(hint).FontSize(12f).TextColor(Halide.TextMuted));
        return column;
    }
}
