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

        var url = ImageUrlFor(item, kind, (int)(width * 2));
        var (image, status) = _posters.Get(url);

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

        if (item.Played)
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

    /// <summary>
    /// Which image to ask the server for. An episode's own primary image is a still from
    /// the episode; for a backdrop it borrows the series', which is what
    /// <see cref="MediaItem.BackdropItemId"/> is for.
    /// </summary>
    internal Uri? ImageUrlFor(MediaItem item, ImageKind kind, int maxWidth)
    {
        if (_library is not { } lib) return null;
        if (kind == ImageKind.Backdrop)
        {
            if (item.BackdropImageTag is { Length: > 0 } own)
                return lib.ImageUrl(item.Id, ImageKind.Backdrop, own, maxWidth);
            if (item.BackdropItemId is { Length: > 0 } parent)
                return lib.ImageUrl(parent, ImageKind.Backdrop, null, maxWidth);
            // No backdrop anywhere: the primary image is better than a grey box.
            return lib.ImageUrl(item.Id, ImageKind.Primary, item.PrimaryImageTag, maxWidth);
        }
        return lib.ImageUrl(item.Id, kind, item.PrimaryImageTag, maxWidth);
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
        float cardWidth, string automationId, string? trailing = null)
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
