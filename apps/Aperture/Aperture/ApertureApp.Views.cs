using Aperture.Core;
using Aperture.Core.Requests;
using Aperture.Theme;
using HultUI.Core.Builders;
using HultUI.Core.Elements;
using HultUI.Core.Events;
using HultUI.Core.Layout;
using HultUI.Core.Primitives;
using HultUI.Core.Styling;

namespace Aperture;

/// <summary>The two small views: search, and what the app knows about this server.</summary>
public sealed partial class ApertureApp
{
    // ── Search ───────────────────────────────────────────────────────────

    private readonly HultUI.Core.Reactivity.Ref<TextInputElement> _searchField = new();

    /// <summary>Set when the Search view is opened, cleared once the field has the
    /// keyboard. Focus is taken in <see cref="OnAfterBuild"/> rather than during the build:
    /// the element does not exist until the tree is mounted.</summary>
    private bool _pendingSearchFocus;

    /// <summary>Called from the app's own OnAfterBuild — a component has one.</summary>
    private void FocusPendingSearchField()
    {
        if (!_pendingSearchFocus) return;
        _pendingSearchFocus = false;
        _searchField.Current?.Focus();
    }

    private Element BuildSearchView()
    {
        EnsureSearch();

        var column = Flex.Column(gap: Halide.S4).FlexGrow(1).Padding(new Thickness(Halide.S5));

        var field = TextInput.New(_searchTerm)
            .PlaceholderText("Search films, series and episodes")
            .OnTextChanged(v => { SetSearchTerm(v); })
            .Height(Dimension.Px(40)).FlexShrink(0)
            .TextColor(Halide.Text)
            .AutomationId("search-input")
            .WithRef(_searchField);
        field.AccessibleName = "Search";
        field.On<KeyDownEvent>(e =>
        {
            if (e.Key == KeyCode.Escape) { SetSearchTerm(""); e.Handled = true; }
        });
        field.Key = "search-input";
        column.AddChild(field);

        var term = _searchTerm.Trim();
        if (term.Length < 2)
        {
            column.AddChild(Hint(Icons.Search, "Type at least two letters."));
            return column;
        }

        // The request server answers the same search, for what the library does not have
        // — which is exactly when a search comes back empty and someone wants to ask for it.
        EnsureRequestSearch(term);
        var library = SearchResults;
        IReadOnlyList<DiscoverItem> elsewhere = _requestServer is null ? [] : RequestSearchResults;

        var scroll = ScrollView.Vertical().FlexGrow(1);
        var sections = Flex.Column(gap: Halide.S5);

        if (SearchIsLoading && library.Count == 0)
            sections.AddChild(Hint(Icons.Loading, $"Searching {_library?.Server.Name}…"));
        else if (library.Count == 0)
            sections.AddChild(Hint(Icons.Warning, _requestServer is null
                ? $"Nothing matches “{term}”."
                : $"Nothing in your library matches “{term}”."));
        else
        {
            var heading = $"{library.Count} result{(library.Count == 1 ? "" : "s")}";
            if (_requestServer is not null) heading += " in your library";
            sections.AddChild(Ui.SectionHeading(heading));
            // A wrap of posters: a search crosses libraries, so the shelf shape is the one
            // thing every result has in common.
            sections.AddChild(PosterWrap(library.Count, i => PosterCard(library[i], 140f, cursorIndex: i)));
        }

        if (_requestServer is not null)
        {
            var section = Flex.Column(gap: Halide.S3).AutomationId("search-requestable");
            if (RequestSearchLoading && elsewhere.Count == 0)
                section.AddChild(Hint(Icons.Loading, $"Asking {RequestServerTitle}…"));
            else if (elsewhere.Count > 0)
            {
                section.AddChild(Ui.SectionHeading($"Not in your library · {RequestServerTitle}"));
                int offset = library.Count;
                section.AddChild(PosterWrap(elsewhere.Count,
                    i => DiscoverCard(elsewhere[i], 140f, cursorIndex: offset + i)));
            }
            sections.AddChild(section);
        }

        // The field keeps the keyboard while you type; the arrows walk the results only
        // once one has been reached, which is why the cursor starts unset. One walk, both
        // sections, in the order drawn.
        var walk = new List<object>(library.Count + elsewhere.Count);
        walk.AddRange(library);
        walk.AddRange(elsewhere);
        SetCursorList($"search:{term}:{library.Count}:{elsewhere.Count}", walk, VisibleCards(140f));

        scroll.AddChild(sections);
        column.AddChild(scroll);
        return column;
    }

    private static Element PosterWrap(int count, Func<int, Element> card)
    {
        var wrap = Flex.Row(gap: Halide.PosterGap).AlignItems(AlignItems.FlexStart);
        wrap.Flex.Wrap = FlexWrap.Wrap;
        wrap.Gap(Halide.PosterGap);
        for (int i = 0; i < count; i++) wrap.AddChild(card(i));
        return wrap;
    }

    private static Element Hint(string glyph, string text)
    {
        var row = Flex.Row(gap: Halide.S2).AlignItems(AlignItems.Center).FlexShrink(0);
        row.AddChild(Ui.Icon(glyph, 14f, Halide.TextFaint));
        row.AddChild(Text.New(text).FontSize(12.5f).TextColor(Halide.TextMuted));
        return row;
    }

    // ── One person, and what has been hearted ────────────────────────────

    /// <summary>Everything the server credits this person with, newest first.</summary>
    private Element BuildPersonView(Route route)
    {
        var personId = route.Id!;
        EnsurePersonItems(personId);
        return ShelfView(route.Title ?? "Person", PersonItems, PersonItemsLoading,
            "person-view", Icons.User,
            emptyHeadline: $"Nothing here credits {route.Title}",
            emptyHint: "The server knows the name, but nothing in your libraries lists it.");
    }

    private Element BuildFavouritesView()
    {
        EnsureFavourites();
        return ShelfView("Favourites", Favourites, FavouritesLoading,
            "favourites-view", Icons.FavoriteOn,
            emptyHeadline: "Nothing hearted yet",
            emptyHint: "The heart on a film or series puts it here.");
    }

    /// <summary>Home's Continue watching row in full: the same stills, with their progress
    /// bars, as a wrap.</summary>
    private Element BuildContinueWatchingView()
    {
        EnsureResumeAll();
        return ShelfView("Continue watching", ResumeAll, ResumeAllLoading,
            "continue-watching-view", Icons.Play,
            emptyHeadline: "Nothing half-watched",
            emptyHint: "Something stopped partway through lands here.",
            stills: true);
    }

    /// <summary>Home's Next up row in full: the next episode of every series being
    /// watched, as posters, as on the row.</summary>
    private Element BuildNextUpView()
    {
        EnsureNextUpAll();
        return ShelfView("Next up", NextUpAll, NextUpAllLoading,
            "next-up-view", Icons.Shows,
            emptyHeadline: "Nothing up next",
            emptyHint: "Finish an episode and the one after it lands here.");
    }

    /// <summary>
    /// A titled wrap of posters over a list that is already loaded in full — a person's
    /// credits, the favourites, what is part-watched (as stills). Not the library grid:
    /// these lists are tens of items, not thousands, so they need no virtualization and no
    /// paging, and a wrap reads better than a strict grid when the count is odd.
    /// </summary>
    private Element ShelfView(string title, IReadOnlyList<MediaItem> items, bool loading,
        string automationId, string glyph, string emptyHeadline, string emptyHint, bool stills = false)
    {
        var column = Flex.Column(gap: Halide.S4).FlexGrow(1).AutomationId(automationId);

        var bar = Flex.Row(gap: Halide.S3).FlexShrink(0)
            .Height(56).AlignItems(AlignItems.Center)
            .Padding(new Thickness(0, Halide.S5, 0, Halide.S5))
            .BorderColor(Halide.Border).BorderWidth(new Thickness(0, 0, 1, 0));
        bar.AddChild(Ui.IconButton(Icons.Back, "Back (Esc)", () => GoBack(), 15f, Halide.TextMuted,
            automationId + "-back"));
        bar.AddChild(Text.New(title).FontSize(18f).FontWeight(FontWeight.SemiBold).TextColor(Halide.Text));
        if (items.Count > 0) bar.AddChild(Ui.Mono(items.Count.ToString(), 11.5f, Halide.TextFaint));
        column.AddChild(bar);

        if (items.Count == 0)
        {
            var empty = Flex.Column(gap: Halide.S2).FlexGrow(1)
                .AlignItems(AlignItems.Center).JustifyContent(JustifyContent.Center);
            empty.AddChild(Ui.Icon(loading ? Icons.Loading : glyph, 24f, Halide.TextFaint));
            empty.AddChild(Text.New(loading ? "Asking the server…" : emptyHeadline)
                .FontSize(15f).FontWeight(FontWeight.SemiBold).TextColor(Halide.TextDim));
            if (!loading)
                empty.AddChild(Text.New(emptyHint).FontSize(12f).TextColor(Halide.TextMuted));
            column.AddChild(empty);
            return column;
        }

        var scroll = ScrollView.Vertical().FlexGrow(1);
        var wrap = Flex.Row(gap: Halide.PosterGap).AlignItems(AlignItems.FlexStart)
            .Padding(new Thickness(0, Halide.S5, Halide.S5, Halide.S5));
        wrap.Flex.Wrap = FlexWrap.Wrap;
        wrap.Gap(Halide.PosterGap);
        SetCursorList(automationId, items, VisibleCards(stills ? Halide.StillWidth : 150f));
        for (int i = 0; i < items.Count; i++)
            wrap.AddChild(stills ? StillCard(items[i], cursorIndex: i) : PosterCard(items[i], 150f, cursorIndex: i));
        scroll.AddChild(wrap);
        column.AddChild(scroll);
        return column;
    }

    // ── Settings ─────────────────────────────────────────────────────────

    /// <summary>
    /// What this app knows and where it keeps it — deliberately a page of facts rather
    /// than a page of switches. The two things worth changing here are the session and
    /// the library data, and both are one button.
    /// </summary>
    private Element BuildSettingsView()
    {
        var scroll = ScrollView.Vertical().FlexGrow(1);
        var page = Flex.Column(gap: Halide.S5).Padding(new Thickness(Halide.S5)).MaxWidth(Dimension.Px(720));

        page.AddChild(Text.New("Settings").FontSize(20f).FontWeight(FontWeight.SemiBold).TextColor(Halide.Text));

        if (_library is { } lib)
        {
            var server = Card("Server");
            server.AddChild(Row("Name", lib.Server.Name));
            server.AddChild(Row("Address", lib.Server.Address));
            server.AddChild(Row("Version", lib.Server.Version));
            server.AddChild(Row("Signed in as", lib.User.Name));
            server.AddChild(Row("This device", _settings.Device.DeviceName));
            page.AddChild(server);

            var session = Flex.Row(gap: Halide.S2).AlignItems(AlignItems.Center);
            session.AddChild(Ui.Button(ButtonVariant.Secondary, "Refresh everything", Refresh,
                "settings-refresh", Icons.Refresh));
            session.AddChild(Ui.Button(ButtonVariant.Danger, "Sign out", () => SignOut(keepAccount: false),
                "settings-signout", Icons.SignOut));
            page.AddChild(session);

            page.AddChild(RequestServerCard());
        }

        var storage = Card("Where things are kept");
        storage.AddChild(Row("Settings", ApertureSettings.DefaultPath));
        storage.AddChild(Row("Access token", OperatingSystem.IsWindows()
            ? "Windows Credential Manager"
            : "not stored on this platform — you sign in each launch"));
        storage.AddChild(Row("Artwork", $"in memory, {_posters.PendingCount} loading"));
        page.AddChild(storage);

        var playback = Card("Playback");
        playback.AddChild(Row("Decoder", _backends is null ? "no libmpv found" : "libmpv"));
        playback.AddChild(Row("Volume", $"{(int)Math.Round(_settings.Data.Volume * 100)}%"));
        playback.AddChild(SettingRow("Quality", QualityChip()));
        playback.AddChild(Text.New(_settings.Data.MaxStreamingKbps is null
                ? "The file as it is: the server sends it untouched and spends no CPU."
                : "A ceiling makes the server re-encode anything bigger — for a link that can't carry the original.")
            .FontSize(11.5f).TextColor(Halide.TextFaint));
        page.AddChild(playback);

        scroll.AddChild(page);
        return scroll;
    }

    private static BoxElement Card(string title)
    {
        var card = Flex.Column(gap: Halide.S2)
            .Padding(new Thickness(Halide.S4))
            .CornerRadius(Halide.Radius2)
            .Background(Halide.Surface)
            .BorderColor(Halide.Border).BorderWidth(1f);
        card.AddChild(Text.New(title).FontSize(13f).FontWeight(FontWeight.SemiBold).TextColor(Halide.TextDim));
        return card;
    }

    /// <summary>A settings row whose right-hand side is something to press rather than
    /// something to read.</summary>
    private static Element SettingRow(string label, Element control)
    {
        var row = Flex.Row(gap: Halide.S4).AlignItems(AlignItems.Center);
        row.AddChild(Text.New(label).FontSize(12f).TextColor(Halide.TextMuted).Width(Dimension.Px(130)));
        row.AddChild(control);
        return row;
    }

    /// <summary>The streaming ceilings worth offering, coarsest first. Kilobits, because
    /// that is what the server's own settings page speaks.</summary>
    private static readonly (int? Kbps, string Label)[] QualityChoices =
    [
        (null, "Original"),
        (20_000, "20 Mbps"),
        (10_000, "10 Mbps"),
        (4_000, "4 Mbps"),
        (2_000, "2 Mbps"),
        (1_000, "1 Mbps"),
    ];

    internal static string QualityLabel(int? kbps) =>
        QualityChoices.FirstOrDefault(c => c.Kbps == kbps).Label ?? $"{kbps} kbps";

    private Element QualityChip()
    {
        var current = _settings.Data.MaxStreamingKbps;
        var chip = Box.New()
            .Padding(new Thickness(5, Halide.S3, 5, Halide.S3))
            .CornerRadius(Halide.Radius1)
            .BorderColor(Halide.Border).BorderWidth(1f)
            .Cursor(CursorType.Pointer)
            .AccessibleRole(HultUI.Core.Accessibility.AccessibilityRole.ComboBox)
            .AccessibleName($"Quality: {QualityLabel(current)}")
            .AutomationId("settings-quality")
            .ContextMenu(QualityMenu)
            .Children(Ui.Line(QualityLabel(current), 11.5f, Halide.TextDim));
        chip.OnClick(e => _window.OpenContextMenu(e.CurrentTarget, e.Position));
        return chip.HoverBg(Halide.Raised);
    }

    private IReadOnlyList<ContextMenuItem> QualityMenu()
    {
        var current = _settings.Data.MaxStreamingKbps;
        var items = new List<ContextMenuItem>();
        foreach (var (kbps, label) in QualityChoices)
        {
            var choice = kbps;
            items.Add(new ContextMenuItem
            {
                Label = label,
                IsChecked = current == choice,
                Action = () => SetMaxStreamingKbps(choice),
            });
        }
        return items;
    }

    private static Element Row(string label, string value)
    {
        var row = Flex.Row(gap: Halide.S4).AlignItems(AlignItems.Center);
        row.AddChild(Text.New(label).FontSize(12f).TextColor(Halide.TextMuted).Width(Dimension.Px(130)));
        row.AddChild(Ui.Mono(value, 12f, Halide.TextDim).FlexShrink(1));
        return row;
    }
}
