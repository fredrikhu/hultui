using Aperture.Core;
using Aperture.Theme;
using HultUI.Core.Accessibility;
using HultUI.Core.Builders;
using HultUI.Core.Elements;
using HultUI.Core.Layout;
using HultUI.Core.Primitives;
using HultUI.Core.Styling;

namespace Aperture;

/// <summary>
/// One item, in full: a backdrop with the artwork and the verbs over it, then whatever
/// sits under it — a series' seasons, a season's episodes. The page is the same shape for
/// every kind, so the Play button is always in the same place.
/// </summary>
public sealed partial class ApertureApp
{
    private Element BuildDetailView(Route route)
    {
        var itemId = route.Id!;
        EnsureDetail(itemId);

        var item = DetailItem;
        if (item is null || item.Id != itemId)
        {
            var loading = Flex.Column(gap: Halide.S3).FlexGrow(1)
                .AlignItems(AlignItems.Center).JustifyContent(JustifyContent.Center)
                .AutomationId("detail-loading");
            loading.AddChild(Ui.Icon(DetailIsLoading ? Icons.Loading : Icons.Warning, 22f, Halide.TextFaint));
            loading.AddChild(Text.New(DetailIsLoading ? "Loading…" : "That item is no longer here")
                .FontSize(13f).TextColor(Halide.TextMuted));
            return loading;
        }

        // Series → its seasons; season → its episodes. A film has no children and asks
        // for none.
        if (item.Kind is MediaKind.Series or MediaKind.Season or MediaKind.BoxSet or MediaKind.Folder)
            EnsureChildren(item.Id, item.Kind);
        // A series' verb is its next episode, and only the server knows which that is.
        if (item.Kind == MediaKind.Series) EnsureNextInSeries(item.Id);

        var scroll = ScrollView.Vertical().FlexGrow(1).AutomationId("detail");
        // No FlexGrow on a scroll view's content: the viewport gives it an unbounded
        // height to measure in, and a growing child resolves that to infinity — which
        // collapses the scroll view itself and everything laid out beside it.
        var page = Flex.Column(gap: 0);
        page.AddChild(BuildHero(item));

        var body = Flex.Column(gap: Halide.S5).Padding(new Thickness(Halide.S5));

        if (item.Overview is { Length: > 0 } overview)
            body.AddChild(Text.New(overview).FontSize(13.5f).TextColor(Halide.TextDim).MaxWidth(Dimension.Px(820)));

        switch (item.Kind)
        {
            case MediaKind.Series:
                body.AddChild(SeasonsSection(item));
                break;
            case MediaKind.Season or MediaKind.BoxSet or MediaKind.Folder:
                body.AddChild(EpisodesSection(item));
                break;
            case MediaKind.Episode when item.SeriesId is { Length: > 0 } seriesId:
                body.AddChild(Ui.Button(ButtonVariant.Ghost, $"All of {item.SeriesName}",
                    () => Navigate(new Route(ApertureView.Detail, seriesId, item.SeriesName)),
                    "detail-series", Icons.Episodes));
                break;
        }

        page.AddChild(body);
        scroll.AddChild(page);
        return scroll;
    }

    /// <summary>
    /// The hero: the backdrop, darkened from the bottom and from the left so text keeps
    /// its contrast whatever the frame behind it holds, with the poster, the title block
    /// and the verbs laid over it.
    /// </summary>
    private Element BuildHero(MediaItem item)
    {
        float heroHeight = Math.Clamp(_window.LayoutHeight * 0.44f, 280f, 420f);

        var hero = Box.New().Height(heroHeight).FlexShrink(0)
            .AlignItems(AlignItems.Stretch)
            .Background(Halide.Raised);

        var url = ImageUrlFor(item, ImageKind.Backdrop, (int)_window.LayoutWidth);
        var (image, status) = _posters.Get(url);
        if (status == PosterCache.Status.Ready && image is { IsLoaded: true })
        {
            var picture = Image.New(image);
            picture.Fit = ImageFit.Cover;
            hero.AddChild(picture.Size(Dimension.Percent(100), Dimension.Percent(100)));
        }

        // Two scrims: one up from the bottom edge for the title block, one in from the
        // left for the verbs. Both are the app ground's own colour, so the hero dissolves
        // into the page rather than ending at a line.
        // Both are decoration laid over the picture, so the pointer passes straight
        // through them: an absolutely positioned box that covers the hero would otherwise
        // swallow every click meant for the Play button underneath it.
        var vertical = Box.New().Size(Dimension.Percent(100), Dimension.Percent(100));
        vertical.BackgroundGradient = Halide.BackdropScrim();
        vertical.IsHitTestVisible = false;
        vertical.Position.Type = PositionType.Absolute;
        vertical.Position.Left = Dimension.Px(0);
        vertical.Position.Top = Dimension.Px(0);
        hero.AddChild(vertical);

        var horizontal = Box.New().Size(Dimension.Percent(70), Dimension.Percent(100));
        horizontal.BackgroundGradient = Halide.BackdropScrim(vertical: false);
        horizontal.IsHitTestVisible = false;
        horizontal.Position.Type = PositionType.Absolute;
        horizontal.Position.Left = Dimension.Px(0);
        horizontal.Position.Top = Dimension.Px(0);
        hero.AddChild(horizontal);

        var content = Flex.Row(gap: Halide.S5)
            .Padding(new Thickness(Halide.S5))
            .AlignItems(AlignItems.FlexEnd)
            .Size(Dimension.Percent(100), Dimension.Percent(100));
        content.Position.Type = PositionType.Absolute;
        content.Position.Left = Dimension.Px(0);
        content.Position.Bottom = Dimension.Px(0);

        // The poster, unless this is an episode — an episode's primary image is a still
        // from the episode, which the backdrop behind it already is.
        if (item.Kind != MediaKind.Episode)
            content.AddChild(Artwork(item, ImageKind.Primary, 150f, 225f, Halide.Radius3));

        content.AddChild(TitleBlock(item));
        hero.AddChild(content);

        // Back, top-left over the artwork.
        var back = Ui.IconButton(Icons.Back, "Back (Esc)", () => GoBack(), 16f, Halide.Text, "detail-back");
        back.Background(Halide.Alpha(Halide.Bg, 0.55)).CornerRadius(Halide.Radius1);
        back.Position.Type = PositionType.Absolute;
        back.Position.Left = Dimension.Px(Halide.S4);
        back.Position.Top = Dimension.Px(Halide.S4);
        hero.AddChild(back);
        return hero;
    }

    private Element TitleBlock(MediaItem item)
    {
        var block = Flex.Column(gap: Halide.S3).FlexShrink(1).FlexGrow(1);

        if (item.Kind == MediaKind.Episode && item.SeriesName is { Length: > 0 } series)
            block.AddChild(Ui.Line(series, 13f, Halide.Accent300));

        block.AddChild(Text.New(item.Name).FontSize(30f).FontWeight(FontWeight.Bold).TextColor(Halide.Text)
            .MaxLines(2).TextOverflow(TextOverflow.Ellipsis));

        var meta = Flex.Row(gap: Halide.S2).AlignItems(AlignItems.Center);
        if (item.Kind == MediaKind.Episode && item.SubLine.Length > 0) meta.AddChild(Ui.Chip(item.SubLine));
        if (item.ProductionYear is { } year) meta.AddChild(Ui.Chip(year.ToString()));
        if (item.RunTime is { } run) meta.AddChild(Ui.Chip(Ui.Runtime(run)));
        if (item.OfficialRating is { Length: > 0 } rating) meta.AddChild(Ui.Chip(rating));
        if (item.CommunityRating is { } score)
        {
            var star = Flex.Row(gap: 4).AlignItems(AlignItems.Center).FlexShrink(0);
            star.AddChild(Ui.Icon(Icons.Rating, 12f, Halide.Accent));
            star.AddChild(Ui.Mono(score.ToString("0.0"), 11.5f, Halide.TextDim));
            meta.AddChild(star);
        }
        if (item.UnplayedItemCount is > 0 and var unplayed)
            meta.AddChild(Ui.Chip($"{unplayed} unwatched", Halide.Accent300));
        if (item.Genres.Count > 0)
            meta.AddChild(Ui.Line(string.Join(" · ", item.Genres.Take(3)), 12f, Halide.TextMuted));
        block.AddChild(meta);

        block.AddChild(ActionRow(item));
        return block;
    }

    /// <summary>Play (or Resume, with where from), then the two toggles. Nothing else: a
    /// detail page that opens with six buttons hides the one that matters.</summary>
    private Element ActionRow(MediaItem item)
    {
        var row = Flex.Row(gap: Halide.S2).AlignItems(AlignItems.Center);

        if (item.IsPlayable)
        {
            var resumeAt = item.ResumePosition;
            var label = resumeAt is { } at ? $"Resume {Ui.Runtime(at)} in" : "Play";
            row.AddChild(Ui.Button(ButtonVariant.Primary, label,
                () => Play(item, resumeAt ?? TimeSpan.Zero), "detail-play",
                resumeAt is null ? Icons.Play : Icons.Resume));

            if (resumeAt is not null)
                row.AddChild(Ui.Button(ButtonVariant.Secondary, "From the start",
                    () => Play(item, TimeSpan.Zero), "detail-play-start", Icons.Play));
        }
        else if (item.Kind is MediaKind.Series or MediaKind.Season)
        {
            // The server's next-up for a series; for a season, the first episode without a
            // tick. Either way the label names the episode, so pressing it is never a
            // surprise about which one starts.
            var next = item.Kind == MediaKind.Series
                ? NextInSeries(item.Id)
                : Children.FirstOrDefault(c => c.Kind == MediaKind.Episode && !c.Played);
            if (next is not null)
                row.AddChild(Ui.Button(ButtonVariant.Primary,
                    next.ResumePosition is null ? $"Play {next.SubLine}" : $"Resume {next.SubLine}",
                    () => Play(next, next.ResumePosition ?? TimeSpan.Zero), "detail-play-next",
                    next.ResumePosition is null ? Icons.Play : Icons.Resume));
        }

        row.AddChild(ToggleButton(item.Played ? Icons.Watched : "check", item.Played ? "Watched" : "Mark watched",
            item.Played, () => ToggleWatched(item), "detail-watched"));
        row.AddChild(ToggleButton(item.IsFavorite ? Icons.FavoriteOn : Icons.Favorite,
            item.IsFavorite ? "In favourites" : "Add to favourites",
            item.IsFavorite, () => ToggleFavorite(item), "detail-favorite"));
        return row;
    }

    private Element ToggleButton(string glyph, string label, bool on, Action onClick, string automationId)
    {
        var b = Flex.Row(gap: 6).AlignItems(AlignItems.Center)
            .Padding(new Thickness(8, Halide.S3, 8, Halide.S3))
            .CornerRadius(Halide.Radius1)
            .Background(on ? Halide.Tint(Halide.Accent) : Halide.Alpha(Halide.Text, 0.10))
            .Cursor(CursorType.Pointer)
            .Tooltip(label)
            .AccessibleRole(AccessibilityRole.Button).AccessibleName(label)
            .AutomationId(automationId);
        b.AddChild(Ui.Icon(glyph, 15f, on ? Halide.Accent : Halide.TextDim));
        b.OnClick(_ => onClick());
        return b;
    }

    // ── Children ─────────────────────────────────────────────────────────

    private Element SeasonsSection(MediaItem series)
    {
        var seasons = Children.Where(c => c.Kind == MediaKind.Season).ToList();
        if (seasons.Count > 0)
            return CardRow($"{seasons.Count} season{(seasons.Count == 1 ? "" : "s")}", seasons,
                item => PosterCard(item, 130f), 130f, "row-seasons-" + series.Id);

        // A series whose episodes hang straight off it — a mini-series, or a library
        // whose folders were never split into seasons. Showing "no seasons" over a series
        // that plainly has episodes is the kind of dead end a client should never reach.
        return EpisodesSection(series);
    }

    /// <summary>
    /// A season's episodes as rows rather than posters: an episode is identified by its
    /// number and title, and a column of 16:9 stills with two lines each reads far better
    /// than a shelf of them.
    /// </summary>
    private Element EpisodesSection(MediaItem parent)
    {
        var episodes = Children.Where(c => c.Kind is MediaKind.Episode or MediaKind.Movie or MediaKind.Video).ToList();
        if (episodes.Count == 0)
            return ChildrenPlaceholder("episodes");

        var column = Flex.Column(gap: Halide.S3).AutomationId("episodes");
        column.AddChild(Ui.SectionHeading($"{episodes.Count} episode{(episodes.Count == 1 ? "" : "s")}"));
        foreach (var episode in episodes)
            column.AddChild(EpisodeRow(episode));
        return column;
    }

    private Element EpisodeRow(MediaItem episode)
    {
        var row = Flex.Row(gap: Halide.S4).AlignItems(AlignItems.Center)
            .Padding(new Thickness(Halide.S2))
            .CornerRadius(Halide.Radius2)
            .Cursor(CursorType.Pointer)
            .AccessibleRole(AccessibilityRole.Button)
            .AccessibleName(AccessibleNameFor(episode))
            .AutomationId("episode-" + episode.Id);

        row.AddChild(Artwork(episode, ImageKind.Primary, 160f, 90f, Halide.Radius1));

        var text = Flex.Column(gap: 3).FlexGrow(1).FlexShrink(1);
        var heading = Flex.Row(gap: Halide.S2).AlignItems(AlignItems.Center);
        if (episode.IndexNumber is { } number)
            heading.AddChild(Ui.Mono(number.ToString("00"), 12f, Halide.TextFaint));
        heading.AddChild(Ui.Line(episode.Name, 13.5f, Halide.Text));
        text.AddChild(heading);
        if (episode.Overview is { Length: > 0 } overview)
            text.AddChild(Text.New(overview).FontSize(12f).TextColor(Halide.TextMuted)
                .MaxLines(2).TextOverflow(TextOverflow.Ellipsis));
        row.AddChild(text);

        var right = Flex.Column(gap: 4).AlignItems(AlignItems.FlexEnd).FlexShrink(0);
        if (episode.RunTime is { } run) right.AddChild(Ui.Mono(Ui.Runtime(run), 11.5f, Halide.TextFaint));
        if (episode.Played) right.AddChild(Ui.Icon(Icons.Watched, 14f, Halide.StatusOk));
        else if (episode.ResumePosition is { } at && episode.RunTime is { } total)
            right.AddChild(Ui.Line(Ui.Remaining(total, at), 11.5f, Halide.Accent300));
        row.AddChild(right);

        row.OnClick(_ => Play(episode, episode.ResumePosition ?? TimeSpan.Zero));
        row.HoverBg(Halide.Raised);
        return row;
    }

    private Element ChildrenPlaceholder(string what)
    {
        var row = Flex.Row(gap: Halide.S2).AlignItems(AlignItems.Center).AutomationId("children-" + what);
        row.AddChild(Ui.Icon(ChildrenAreLoading ? Icons.Loading : Icons.Warning, 14f, Halide.TextFaint));
        row.AddChild(Ui.Line(ChildrenAreLoading ? $"Loading {what}…" : $"No {what} here", 12f, Halide.TextMuted));
        return row;
    }

    // ── The two toggles ──────────────────────────────────────────────────

    private void ToggleWatched(MediaItem item)
    {
        if (_library is not { } lib) return;
        bool played = !item.Played;
        _ = ApplyAsync(() => lib.SetPlayedAsync(item.Id, played),
            played ? "Marked watched" : "Marked unwatched");
    }

    private void ToggleFavorite(MediaItem item)
    {
        if (_library is not { } lib) return;
        bool favorite = !item.IsFavorite;
        _ = ApplyAsync(() => lib.SetFavoriteAsync(item.Id, favorite),
            favorite ? "Added to favourites" : "Removed from favourites");
    }

    /// <summary>
    /// A change the server owns: send it, say what happened, and re-read. The re-read is
    /// the point — the watched flag lives on the server, and a client that flips its own
    /// copy is one failed request away from lying.
    /// </summary>
    private async Task ApplyAsync(Func<Task<ApertureResult<bool>>> call, string done)
    {
        var result = await call().ConfigureAwait(false);
        HultUI.Application.InvokeOnMainThread(() =>
        {
            if (result.Ok)
            {
                Flash(done);
                Refresh();
            }
            else
            {
                FlashFailure(result.Failure!);
            }
        });
    }
}
