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
/// The two cards everything is made of: a <b>poster</b> (2:3 cover art — a shelf) and a
/// <b>still</b> (16:9 — the frame you stopped on). Both follow the same rules: the
/// artwork is the brightest thing in the card, the caption sits outside it where a
/// screen reader and a scanning eye both find it, and a half-watched item carries a
/// three-pixel accent sliver rather than a percentage.
/// </summary>
public sealed partial class ApertureApp
{
    /// <summary>A poster tile: artwork, title, subline. Clicking opens the item.</summary>
    internal Element PosterCard(MediaItem item, float width = Halide.PosterWidth, int cursorIndex = -1)
    {
        float height = MathF.Round(width * 1.5f);
        var card = Flex.Column(gap: Halide.S2)
            .Width(width).FlexShrink(0)
            .CornerRadius(Halide.Radius2)
            .Cursor(CursorType.Pointer)
            .AccessibleRole(AccessibilityRole.Button)
            .AccessibleName(AccessibleNameFor(item))
            .AutomationId("card-" + item.Id)
            .Tooltip(item.Name);
        MarkCursor(card, cursorIndex, item);

        card.AddChild(Artwork(item, ImageKind.Primary, width, height, Halide.Radius2));

        // The caption is a setting: a shelf of covers with no words under it is a real way
        // to browse, and the card's accessible name still says the title either way.
        if (!_settings.Data.ShowCardTitles)
        {
            card.OnClick(_ => Open(item));
            return card;
        }

        var caption = Flex.Column(gap: 1).Width(width);
        caption.AddChild(Ui.Line(item.Name, 12.5f, Halide.Text));
        var sub = item.Kind switch
        {
            // "Season 2 / Season 2" reads as a bug; what a season card can add is its size.
            MediaKind.Season when item.ChildCount is { } episodes and > 0 =>
                $"{episodes} episode{(episodes == 1 ? "" : "s")}",
            MediaKind.Season => "",
            MediaKind.Episode when item.SeriesName is { Length: > 0 } series => $"{series} · {item.SubLine}",
            _ => item.SubLine,
        };
        if (sub.Length > 0) caption.AddChild(Ui.Line(sub, 11.5f, Halide.TextMuted));
        card.AddChild(caption);

        card.OnClick(_ => Open(item));
        return card;
    }

    /// <summary>
    /// A continue-watching card: the still, the title over it, how much is left, and the
    /// progress sliver. Clicking <i>plays</i> rather than opening the detail page — this
    /// row exists to get someone back into a film in one click.
    /// </summary>
    internal Element StillCard(MediaItem item, int cursorIndex = -1)
    {
        var card = Flex.Column(gap: Halide.S2)
            .Width(Halide.StillWidth).FlexShrink(0)
            .Cursor(CursorType.Pointer)
            .AccessibleRole(AccessibilityRole.Button)
            .AccessibleName(ResumeAccessibleName(item))
            .AutomationId("resume-" + item.Id)
            .Tooltip(item.Name);
        MarkCursor(card, cursorIndex, item);

        // The backdrop of an episode usually lives on its series, which is what
        // BackdropItemId points at; the item's own thumb is the fallback.
        var art = Artwork(item, ImageKind.Backdrop, Halide.StillWidth, Halide.StillHeight, Halide.Radius2);
        card.AddChild(art);

        var caption = Flex.Column(gap: 1).Width(Halide.StillWidth);
        caption.AddChild(Ui.Line(item.Kind == MediaKind.Episode && item.SeriesName is { Length: > 0 } s ? s : item.Name,
            12.5f, Halide.Text));
        var line = item.Kind == MediaKind.Episode
            ? $"{item.SubLine} · {item.Name}"
            : item.RunTime is { } run && item.ResumePosition is { } at ? Ui.Remaining(run, at) : item.SubLine;
        if (line.Length > 0) caption.AddChild(Ui.Line(line, 11.5f, Halide.TextMuted));
        card.AddChild(caption);

        card.OnClick(_ => Play(item, item.ResumePosition ?? TimeSpan.Zero));
        return card;
    }

    /// <summary>
    /// The artwork box: the picture when the cache has it, a placeholder glyph while it
    /// loads or when the server has none, plus the watched tick and progress sliver. The
    /// box is always the same size, so a row of cards never reflows as pictures arrive.
    /// </summary>
    internal Element Artwork(MediaItem item, ImageKind kind, float width, float height, float radius)
    {
        var box = Box.New()
            .Width(width).Height(height).FlexShrink(0)
            .CornerRadius(radius)
            .Background(Halide.Raised)
            .AlignItems(AlignItems.Center).JustifyContent(JustifyContent.Center);
        // Artwork fills the box edge to edge, so the box's corners have to cut it —
        // otherwise every poster is a square sitting on a rounded backing.
        box.Box.OverflowX = Overflow.Hidden;
        box.Box.OverflowY = Overflow.Hidden;

        // The box in device pixels, by the side that decides a cover fit; the server scales
        // the other side to keep the picture's shape. A picture wider than its box (an
        // episode's 16:9 still on a 2:3 card) is asked for by height, so it still has a
        // pixel for every pixel drawn. Never both sides: Jellyfin's fill covers the box but
        // rounds both sides up, and the spare pixel on the side that already fits shifts
        // the picture by half a pixel, which is soft at one to one.
        bool byHeight = ArtAspect(item, kind) is { } aspect && aspect > width / height * 1.005;
        var url = ArtworkUrl(item, kind, width, height, byHeight, _artworkScale);
        var (image, status) = _posters.Get(url, _standInScale is { } old && _posters.HasRetired
            ? ArtworkUrl(item, kind, width, height, byHeight, old) : null);

        if (status == PosterCache.Status.Ready && image is { IsLoaded: true })
        {
            var picture = Image.New(image);
            picture.Fit = ImageFit.Cover;
            box.AddChild(picture.Size(Dimension.Percent(100), Dimension.Percent(100)));
        }
        else
        {
            // Pending and Failed look the same on purpose: a spinner per tile in a grid of
            // sixty is a strobe, and "no artwork" is not an error worth shouting about.
            var glyph = Ui.Icon(Icons.ForItem(item.Kind), MathF.Min(40f, width * 0.28f), Halide.TextFaint);
            box.AddChild(glyph);
        }

        // The tick is a badge on a poster, not on a thumbnail: at list size it would cover
        // most of the artwork, and the row draws its own mark at the end instead.
        if (item.Played && width >= 100f)
            box.AddChild(WatchedTick());

        // The sliver rides the bottom edge of the artwork, inside its corner radius.
        double percent = item.PlayedPercentage
            ?? (item.ResumePosition is { } at && item.RunTime is { Ticks: > 0 } run
                ? at.TotalSeconds / run.TotalSeconds * 100
                : 0);
        if (percent is > 0.5 and < 99.5)
        {
            var sliver = Ui.ProgressSliver(percent, width);
            sliver.Position.Type = PositionType.Absolute;
            sliver.Position.Left = Dimension.Px(0);
            sliver.Position.Bottom = Dimension.Px(0);
            box.AddChild(sliver);
        }
        return box;
    }

    /// <summary>
    /// What a card offers on a right click — and what the More key (the context-menu key,
    /// or Shift+F10) opens on the card under the cursor.
    ///
    /// <para>Everything here is a thing you would otherwise open the item to do. The point
    /// of a shelf is that you can work along it: mark an episode watched, heart a film,
    /// jump to the series an episode belongs to, without losing your place.</para>
    /// </summary>
    internal IReadOnlyList<ContextMenuItem> CardMenu(MediaItem item)
    {
        var items = new List<ContextMenuItem>();

        if (item.IsPlayable)
        {
            if (item.ResumePosition is { } at)
            {
                items.Add(new ContextMenuItem { Label = $"Resume {Ui.Runtime(at)} in", Action = () => Play(item, at) });
                items.Add(new ContextMenuItem { Label = "Play from the start", Action = () => Play(item, TimeSpan.Zero) });
            }
            else
            {
                items.Add(new ContextMenuItem { Label = "Play", Action = () => Play(item, TimeSpan.Zero) });
            }
        }

        items.Add(new ContextMenuItem { Label = "Open", Action = () => Open(item) });
        items.Add(ContextMenuItem.Separator());
        items.Add(new ContextMenuItem
        {
            Label = item.Played ? "Mark unwatched" : "Mark watched",
            Action = () => ToggleWatched(item, reload: false),
        });
        items.Add(new ContextMenuItem
        {
            Label = item.IsFavorite ? "Remove from favourites" : "Add to favourites",
            Action = () => ToggleFavorite(item, reload: false),
        });

        if (item.Kind == MediaKind.Episode && item.SeriesId is { Length: > 0 } seriesId)
        {
            items.Add(ContextMenuItem.Separator());
            items.Add(new ContextMenuItem
            {
                Label = $"Go to {item.SeriesName}",
                Action = () => Navigate(new Route(ApertureView.Detail, seriesId, item.SeriesName)),
            });
        }
        return items;
    }

    /// <summary>
    /// Makes a card reachable by keyboard, and rings it when the cursor is on it. The ring
    /// is an outline rather than a border: a border would take layout space and shift every
    /// card beside it as the cursor moves through the row.
    /// </summary>
    private void MarkCursor(BoxElement card, int cursorIndex, MediaItem? item = null)
    {
        card.IsFocusable = true;
        if (item is { } subject) card.ContextMenu(() => CardMenu(subject));
        if (cursorIndex < 0) return;
        TrackCursor(card, cursorIndex);
        if (IsCursor(cursorIndex))
            card.Outline(2f, Halide.Accent, offset: 3f);
    }

    /// <summary>The watched mark: a filled accent disc with a tick, top-right.</summary>
    private static Element WatchedTick()
    {
        var badge = Box.New().Width(22).Height(22)
            .CornerRadius(11f)
            .Background(Halide.Accent)
            .AlignItems(AlignItems.Center).JustifyContent(JustifyContent.Center);
        badge.AddChild(Ui.Icon("check", 12f, new Color(35, 23, 4)));
        badge.Position.Type = PositionType.Absolute;
        badge.Position.Right = Dimension.Px(Halide.S2);
        badge.Position.Top = Dimension.Px(Halide.S2);
        return badge;
    }

    // ── Artwork sizes ────────────────────────────────────────────────────

    /// <summary>The display scale artwork is fetched for; see <see cref="ArtworkScaleFor"/>.</summary>
    private float _artworkScale = 1f;

    /// <summary>Pictures that have landed in the cache, counted on the UI thread: a card made
    /// before its picture landed shows a placeholder (or an old size's stand-in) until it is
    /// made again, so the grid's kept rows go with this. (A kept card does not ask the cache
    /// again, so it does not refresh its picture's place in the LRU; the cache only drops a
    /// picture when one lands, every landing makes the rows on screen again, and the cap is
    /// many screenfuls, so a picture on screen is never the oldest.)</summary>
    private int _artworkLanded;

    private void OnArtworkLanded()
    {
        _artworkLanded++;
        Rerender();
    }

    /// <summary>The scale before the last change, while the cache may still hold pictures
    /// fetched for it — the stand-ins a tile draws until its new picture lands.</summary>
    private float? _standInScale;

    internal float ArtworkScale => _artworkScale;

    /// <summary>
    /// The display scale to fetch artwork for: the window's, rounded up to a quarter.
    /// Windows offers scales in 25 % steps, so there it is the scale itself; a zoom or a
    /// fractional scale in between asks for the step above rather than a new size for
    /// every small change, which would fetch every picture again.
    /// </summary>
    internal static float ArtworkScaleFor(float displayScale) =>
        MathF.Max(0.25f, MathF.Ceiling(displayScale * 4f - 0.01f) / 4f);

    /// <summary>
    /// How many pixels to ask for, across a side of artwork drawn <paramref name="layoutSize"/>
    /// layout units long: the device pixels it covers, so a picture is drawn at about one
    /// source pixel per screen pixel. A fixed "twice the layout width" held four times the
    /// pixels it drew at 100 % and 1.8 times at 150 %, in a cache of up to 400 pictures.
    /// </summary>
    internal static int ArtworkPixels(float layoutSize, float scale) =>
        (int)MathF.Ceiling(layoutSize * scale);

    internal int ArtworkPixels(float layoutSize) => ArtworkPixels(layoutSize, _artworkScale);

    /// <summary>The size the same artwork was asked for at before the window changed
    /// scale, while the cache still holds such pictures; null otherwise. Passed to
    /// <see cref="PosterCache.Get"/> as the stand-in.</summary>
    private int? StandInPixels(float layoutSize) =>
        _standInScale is { } old && _posters.HasRetired ? ArtworkPixels(layoutSize, old) : null;

    /// <summary>
    /// Follows the window to a new display scale: from now on artwork is asked for at the
    /// new size, lazily, as tiles are built. What the cache holds is retired rather than
    /// dropped, so each tile keeps its old picture (soft on a denser screen) until the new
    /// one lands. True when the scale changed.
    /// </summary>
    private bool SyncArtworkScale()
    {
        float scale = ArtworkScaleFor(_window.DisplayScale);
        if (scale == _artworkScale) return false;
        _standInScale = _artworkScale;
        _artworkScale = scale;
        _posters.Retire();
        return true;
    }

    /// <summary>A card's picture asked for by width, or by height (see
    /// <see cref="Artwork"/>), at a display scale.</summary>
    private Uri? ArtworkUrl(MediaItem item, ImageKind kind, float width, float height, bool byHeight, float scale) =>
        byHeight
            ? ImageUrlFor(item, kind, null, ArtworkPixels(height, scale))
            : ImageUrlFor(item, kind, ArtworkPixels(width, scale));

    /// <summary>The width-to-height ratio of the picture <see cref="ImageUrlFor"/> picks, when
    /// it is known: a backdrop is 16:9, a primary image is what the server reported, and an
    /// episode's primary image is a 16:9 still when the server did not say.</summary>
    internal static double? ArtAspect(MediaItem item, ImageKind kind) =>
        kind == ImageKind.Backdrop && (item.BackdropImageTag is { Length: > 0 } || item.BackdropItemId is { Length: > 0 })
            ? 16.0 / 9.0
            : item.PrimaryAspectRatio ?? (item.Kind == MediaKind.Episode ? 16.0 / 9.0 : null);

    /// <summary>A TMDB picture (the request server's pages) at the size this scale needs,
    /// with the stand-in from the scale before. TMDB serves fixed sizes, so the size picker
    /// rounds the width up to one of them.</summary>
    private (HultUI.Core.Rendering.IImageSource? Image, PosterCache.Status Status) RequestArtwork(
        Func<string?, int, string?, Uri?> sized, string? path, float layoutWidth) =>
        _posters.Get(sized(path, ArtworkPixels(layoutWidth), RequestImageBase),
            StandInPixels(layoutWidth) is { } old ? sized(path, old, RequestImageBase) : null);

    // ── Heroes ───────────────────────────────────────────────────────────

    /// <summary>
    /// A hero's picture is asked for in device pixels rounded up to a multiple of this, along
    /// the side that decides its cover fit. A hero's box follows the window, and a window
    /// resized by dragging passes through every width on the way: asking at each one fetched
    /// a new picture per frame and kept every one (seven steps of a resize held eight heroes,
    /// 40 MB at 150 %). With the step it asks again only when the window has grown past one,
    /// which is before the picture has fewer pixels than the screen, or shrunk past one; and
    /// it holds at most 127 pixels more than the box needs.
    /// </summary>
    internal const int HeroStep = 128;

    /// <summary>A hero's box, in layout units: the whole view beside the rail, and a height
    /// that follows the window's.</summary>
    internal float HeroWidth => MathF.Max(1f, _window.LayoutWidth - Halide.RailWidth);

    internal float HeroHeight => Math.Clamp(_window.LayoutHeight * 0.44f, 280f, 420f);

    /// <summary>The device pixels a hero asks for across a side <paramref name="layoutSize"/>
    /// long: what the side covers at this scale, rounded up to a <see cref="HeroStep"/>.</summary>
    internal static int HeroPixels(float layoutSize, float scale) =>
        (ArtworkPixels(layoutSize, scale) + HeroStep - 1) / HeroStep * HeroStep;

    /// <summary>
    /// How many heroes' pictures the app keeps: the page on screen and the three behind it,
    /// for Back. The cache bounds pictures by count, 400 of them, and a hero counted as one,
    /// like a poster: every page visited kept its own, 9 MB at 150 % in a 1,400-wide window
    /// (33 MB on a 4K monitor). A page further back than this fetches its hero again.
    /// </summary>
    internal const int HeroesKept = 4;

    /// <summary>A hero kept: which artwork it is, the URL of the picture it draws, and the URL
    /// it last asked for (the same one, once that has landed).</summary>
    private sealed class HeroPicture(string art)
    {
        public string Art { get; } = art;
        public Uri? Shown, Asked;
    }

    /// <summary>The heroes kept, the latest last.</summary>
    private readonly List<HeroPicture> _heroes = [];

    /// <summary>
    /// The picture for a hero (an item page's, a request title page's): asked for at the
    /// device pixels its box covers, as a card's is, by the side that decides the cover fit,
    /// rounded up to a <see cref="HeroStep"/>. Asking for the layout width at any scale drew
    /// 0.7 source pixels per screen pixel at 150 %. <paramref name="art"/> says which artwork
    /// it is whatever the size; <paramref name="sized"/> makes its URL for a width or a height
    /// in device pixels.
    ///
    /// <para>When the same artwork is asked for at another size — the window was resized past
    /// a step, or moved to a monitor with another scale — the picture on screen stays up until
    /// the new one lands, and then goes rather than staying in the cache beside it. So does a
    /// size the window passed over before it landed, and so does the picture of a page more
    /// than <see cref="HeroesKept"/> heroes back.</para>
    /// </summary>
    private (HultUI.Core.Rendering.IImageSource? Image, PosterCache.Status Status) HeroArtwork(
        string? art, double? aspect, Func<int?, int?, Uri?> sized)
    {
        float width = HeroWidth, height = HeroHeight;
        bool byHeight = aspect is { } a && a > width / height * 1.005;
        int pixels = HeroPixels(byHeight ? height : width, _artworkScale);
        if (art is null || (byHeight ? sized(null, pixels) : sized(pixels, null)) is not { } url)
            return _posters.Get(null);

        var hero = _heroes.Find(h => h.Art == art);
        if (hero is null)
        {
            hero = new HeroPicture(art);
            if (_heroes.Count == HeroesKept)
            {
                // The page furthest back: its picture goes once the downloads that follow
                // have landed.
                var oldest = _heroes[0];
                _heroes.RemoveAt(0);
                if (oldest.Shown is { } kept) _posters.Retire(kept);
                if (oldest.Asked is { } asked) _posters.Retire(asked);
            }
        }
        else
        {
            _heroes.Remove(hero);
        }
        _heroes.Add(hero);

        // Every other size of this artwork goes once the new picture is in: the one on screen
        // stands in until then, and one the window passed over before it landed (a drag across
        // several steps) is not wanted at all.
        if (hero.Asked is { } previous && previous != url && previous != hero.Shown) _posters.Retire(previous);
        var standIn = hero.Shown is { } shown && shown != url ? shown : null;
        if (standIn is not null) _posters.Retire(standIn);
        hero.Asked = url;

        var result = _posters.Get(url, standIn);
        if (_posters.IsReady(url)) hero.Shown = url;
        return result;
    }

    /// <summary>
    /// Which image to ask the server for. An episode's own primary image is a still from
    /// the episode; for a backdrop it borrows the series', which is what
    /// <see cref="MediaItem.BackdropItemId"/> is for.
    /// </summary>
    internal Uri? ImageUrlFor(MediaItem item, ImageKind kind, int? maxWidth, int? maxHeight = null)
    {
        if (_library is not { } lib) return null;
        if (kind == ImageKind.Backdrop)
        {
            if (item.BackdropImageTag is { Length: > 0 } own)
                return lib.ImageUrl(item.Id, ImageKind.Backdrop, own, maxWidth, maxHeight);
            if (item.BackdropItemId is { Length: > 0 } parent)
                return lib.ImageUrl(parent, ImageKind.Backdrop, null, maxWidth, maxHeight);
            // No backdrop anywhere: the primary image is better than a grey box.
            return lib.ImageUrl(item.Id, ImageKind.Primary, item.PrimaryImageTag, maxWidth, maxHeight);
        }
        return lib.ImageUrl(item.Id, kind, item.PrimaryImageTag, maxWidth, maxHeight);
    }

    /// <summary>What a screen reader says for a card: the title, what it is, and the state
    /// that the tick and the sliver carry visually.</summary>
    internal static string AccessibleNameFor(MediaItem item)
    {
        var parts = new List<string> { item.Name };
        if (item.Kind == MediaKind.Episode && item.SeriesName is { Length: > 0 } series)
            parts.Add($"{series}, {item.SubLine}");
        else if (item.SubLine.Length > 0)
            parts.Add(item.SubLine);
        if (item.Played) parts.Add("watched");
        else if (item.ResumePosition is not null) parts.Add("part-watched");
        if (item.UnplayedItemCount is > 0 and var unplayed) parts.Add($"{unplayed} unwatched");
        return string.Join(", ", parts);
    }

    private static string ResumeAccessibleName(MediaItem item)
    {
        var name = AccessibleNameFor(item);
        if (item.RunTime is { } run && item.ResumePosition is { } at)
            return $"Resume {name}, {Ui.Remaining(run, at)}";
        return "Play " + name;
    }

    /// <summary>Opening an item: a film or an episode goes to its detail page, a series or
    /// season to its children, a folder into itself.</summary>
    internal void Open(MediaItem item) =>
        Navigate(new Route(ApertureView.Detail, item.Id, item.Name));

    /// <summary>A row of cards that pages sideways with two buttons rather than scrolling.
    /// A horizontal scroller nested in the page's vertical one is the framework's known
    /// wheel-capture trap, and a row of twelve posters does not need momentum — it needs
    /// to show the next six.</summary>
    internal Element CardRow(string title, IReadOnlyList<MediaItem> items, Func<MediaItem, Element> card,
        float cardWidth, string automationId, string? trailing = null) =>
        CardRow(title, items.Count, i => card(items[i]), cardWidth, automationId, trailing);

    /// <summary>The same row over anything that can be built by index — what the cast row
    /// needs, since a person is not a <see cref="MediaItem"/>.</summary>
    internal Element CardRow(string title, int count, Func<int, Element> card,
        float cardWidth, string automationId, string? trailing = null, Element? headingExtra = null)
    {
        var column = Flex.Column(gap: Halide.S3).FlexShrink(0).AutomationId(automationId);

        int perPage = Math.Max(1, VisibleCards(cardWidth));
        int pages = Math.Max(1, (count + perPage - 1) / perPage);
        int page = Math.Clamp(_rowPages.GetValueOrDefault(automationId), 0, pages - 1);

        var heading = Flex.Row(gap: Halide.S2).AlignItems(AlignItems.Center);
        heading.AddChild(Text.New(title).FontSize(15f).FontWeight(FontWeight.SemiBold).TextColor(Halide.Text));
        if (trailing is not null)
        {
            heading.AddChild(Box.New().Width(Halide.S2));
            heading.AddChild(Ui.Line(trailing, 12f, Halide.TextFaint));
        }
        heading.AddChild(Box.New().FlexGrow(1));
        if (pages > 1)
        {
            heading.AddChild(Ui.Mono($"{page + 1}/{pages}", 11f, Halide.TextFaint));
            heading.AddChild(PageButton(Icons.Previous, "Previous", automationId, page - 1, page > 0));
            heading.AddChild(PageButton(Icons.Next, "Next", automationId, page + 1, page < pages - 1));
        }
        if (headingExtra is not null) heading.AddChild(headingExtra);
        column.AddChild(heading);

        var row = Flex.Row(gap: Halide.PosterGap).AlignItems(AlignItems.FlexStart);
        for (int i = page * perPage; i < Math.Min(count, (page + 1) * perPage); i++)
            row.AddChild(card(i));
        column.AddChild(row);
        return column;
    }

    /// <summary>Which page each home row is showing, by row id. View state, never stored.</summary>
    private readonly Dictionary<string, int> _rowPages = new(StringComparer.Ordinal);

    private Element PageButton(string glyph, string label, string rowId, int target, bool enabled)
    {
        var b = Ui.IconButton(glyph, label, () =>
        {
            _rowPages[rowId] = Math.Max(0, target);
            Rerender();
        }, 14f, enabled ? Halide.TextDim : Halide.TextFaint, $"{rowId}-{label.ToLowerInvariant()}");
        if (!enabled) b.Opacity(0.4f);
        return b;
    }

    /// <summary>How many cards of a width fit across the content area at this window size.
    /// Read from the window rather than from a measured box: a flex row of rigid cards
    /// reports one card as its intrinsic width, so reading the box back would say one.</summary>
    internal int VisibleCards(float cardWidth)
    {
        float content = _window.LayoutWidth - Halide.RailWidth - Halide.S5 * 2;
        return Math.Max(1, (int)((content + Halide.PosterGap) / (cardWidth + Halide.PosterGap)));
    }
}
