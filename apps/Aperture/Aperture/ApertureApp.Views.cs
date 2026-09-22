using Aperture.Core;
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

    protected override void OnAfterBuild()
    {
        if (_pendingSearchFocus)
        {
            _pendingSearchFocus = false;
            _searchField.Current?.Focus();
        }
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
        if (SearchIsLoading && SearchResults.Count == 0)
        {
            column.AddChild(Hint(Icons.Loading, $"Searching {_library?.Server.Name}…"));
            return column;
        }
        if (SearchResults.Count == 0)
        {
            column.AddChild(Hint(Icons.Warning, $"Nothing matches “{term}”."));
            return column;
        }

        column.AddChild(Ui.SectionHeading($"{SearchResults.Count} result{(SearchResults.Count == 1 ? "" : "s")}"));

        // A wrap of posters: a search crosses libraries, so the shelf shape is the one
        // thing every result has in common.
        var scroll = ScrollView.Vertical().FlexGrow(1);
        var wrap = Flex.Row(gap: Halide.PosterGap).AlignItems(AlignItems.FlexStart);
        wrap.Flex.Wrap = FlexWrap.Wrap;
        wrap.Gap(Halide.PosterGap);
        foreach (var item in SearchResults)
            wrap.AddChild(PosterCard(item, 140f));
        scroll.AddChild(wrap);
        column.AddChild(scroll);
        return column;
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

    /// <summary>
    /// A titled wrap of posters over a list that is already loaded in full — a person's
    /// credits, the favourites. Not the library grid: these lists are tens of items, not
    /// thousands, so they need no virtualization and no paging, and a wrap reads better
    /// than a strict grid when the count is odd.
    /// </summary>
    private Element ShelfView(string title, IReadOnlyList<MediaItem> items, bool loading,
        string automationId, string glyph, string emptyHeadline, string emptyHint)
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
        foreach (var item in items)
            wrap.AddChild(PosterCard(item, 150f));
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

    private static Element Row(string label, string value)
    {
        var row = Flex.Row(gap: Halide.S4).AlignItems(AlignItems.Center);
        row.AddChild(Text.New(label).FontSize(12f).TextColor(Halide.TextMuted).Width(Dimension.Px(130)));
        row.AddChild(Ui.Mono(value, 12f, Halide.TextDim).FlexShrink(1));
        return row;
    }
}
