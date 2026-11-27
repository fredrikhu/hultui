using Aperture.Core;
using Aperture.Theme;
using HultUI.Core.Accessibility;
using HultUI.Core.Builders;
using HultUI.Core.Elements;
using HultUI.Core.Layout;
using HultUI.Core.Primitives;
using HultUI.Core.Reactivity;
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
    internal Element PosterCard(MediaItem item, float width = 0f, int cursorIndex = -1)
    {
        // 0: the poster's width (Halide.PosterWidth, a phone's smaller).
        if (width <= 0f) width = Halide.PosterWidth;
        return PosterCard(item, width, cursorIndex, ShownPicture(item, ImageKind.Primary, width, MathF.Round(width * 1.5f)));
    }

    /// <summary>A poster tile drawing <paramref name="picture"/>; see <see cref="ShownPicture"/>.</summary>
    private BoxElement PosterCard(MediaItem item, float width, int cursorIndex, CardArt picture)
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

        card.AddChild(Artwork(item, width, height, Halide.Radius2, picture));

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
    internal Element StillCard(MediaItem item, int cursorIndex = -1) =>
        StillCard(item, cursorIndex, ShownPicture(item, ImageKind.Backdrop, Halide.StillWidth, Halide.StillHeight));

    /// <summary>A continue-watching card drawing <paramref name="picture"/>; see <see cref="ShownPicture"/>.</summary>
    private BoxElement StillCard(MediaItem item, int cursorIndex, CardArt picture)
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
        var art = Artwork(item, Halide.StillWidth, Halide.StillHeight, Halide.Radius2, picture);
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
    internal Element Artwork(MediaItem item, ImageKind kind, float width, float height, float radius) =>
        Artwork(item, width, height, radius, ShownPicture(item, kind, width, height));

    /// <summary>
    /// The picture an artwork box of this size draws now, asked of the cache (which fetches it
    /// when it does not have it): null for the placeholder, while it loads or when the server
    /// has none; and, until its own picture is in, the wait that puts it into the box when it
    /// lands (<see cref="CardArt"/>).
    /// </summary>
    private CardArt ShownPicture(MediaItem item, ImageKind kind, float width, float height)
    {
        // The box in device pixels, by the side that decides a cover fit; the server scales
        // the other side to keep the picture's shape. A picture wider than its box (an
        // episode's 16:9 still on a 2:3 card) is asked for by height, so it still has a
        // pixel for every pixel drawn. Never both sides: Jellyfin's fill covers the box but
        // rounds both sides up, and the spare pixel on the side that already fits shifts
        // the picture by half a pixel, which is soft at one to one.
        bool byHeight = ArtAspect(item, kind) is { } aspect && aspect > width / height * 1.005;
        var url = CardArtworkUrl(item, kind, width, height, byHeight);
        return ArtInPlace(url, _standInScale is { } old && _posters.HasRetired
            ? ArtworkUrl(item, kind, width, height, byHeight, old) : null);
    }

    /// <summary>
    /// Each card's picture address as it was made, by the item, the kind and the size in pixels (which
    /// carries the display scale), and checked against the item's picture tags, so an item whose
    /// picture changed asks for the new one. Every build asks for every card's picture, kept or not,
    /// and made the address and its string again each time: 577 B a card against the fake library,
    /// 1,136 B against Jellyfin's. The cache's lookup reads the string the address keeps. Let go when
    /// the data is (a sign-in, a refresh: <see cref="_dataGeneration"/>), and whenever it holds
    /// <see cref="ArtworkUrlsKept"/>, which is many screenfuls.
    /// </summary>
    private readonly Dictionary<ArtworkKey, ArtworkAddress> _artworkUrls = new();
    private int _artworkUrlsGeneration;
    private const int ArtworkUrlsKept = 1024;

    private readonly record struct ArtworkKey(string Id, ImageKind Kind, int Pixels, bool ByHeight);

    private readonly record struct ArtworkAddress(string? Primary, string? Backdrop, string? BackdropItem, Uri? Url);

    /// <summary>Card picture addresses made, not remembered, for tests.</summary>
    internal int ArtworkUrlsMade { get; private set; }

    /// <summary><see cref="ArtworkUrl"/> at the current scale, remembered; see <see cref="_artworkUrls"/>.</summary>
    private Uri? CardArtworkUrl(MediaItem item, ImageKind kind, float width, float height, bool byHeight)
    {
        if (_artworkUrlsGeneration != _dataGeneration || _artworkUrls.Count >= ArtworkUrlsKept)
        {
            _artworkUrls.Clear();
            _artworkUrlsGeneration = _dataGeneration;
        }
        var key = new ArtworkKey(item.Id, kind, ArtworkPixels(byHeight ? height : width), byHeight);
        if (_artworkUrls.TryGetValue(key, out var known) && known.Primary == item.PrimaryImageTag
            && known.Backdrop == item.BackdropImageTag && known.BackdropItem == item.BackdropItemId)
            return known.Url;
        ArtworkUrlsMade++;
        var url = ArtworkUrl(item, kind, width, height, byHeight, _artworkScale);
        _artworkUrls[key] = new ArtworkAddress(item.PrimaryImageTag, item.BackdropImageTag, item.BackdropItemId, url);
        return url;
    }

    private Element Artwork(MediaItem item, float width, float height, float radius, CardArt art)
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

        // Pending and Failed look the same on purpose: a spinner per tile in a grid of
        // sixty is a strobe, and "no artwork" is not an error worth shouting about. The glyph
        // is the image element the picture goes into when it lands (ShowPicture).
        box.AddChild(Waiting(art.Picture is { } shown ? PictureLook(Image.New(), shown)
            : Glyph(Icons.ForItem(item.Kind), MathF.Min(40f, width * 0.28f)), art.Wait));

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

        // The first episode of a new season on Home's row: Dismiss, and Stop watching the series.
        if (NoticeFor(item) is { } notice) AddNewSeasonMenu(items, notice);
        // A series with seasons out that the library does not have: ask for them, or not.
        else if (OutCardFor(item) is { } outCard) AddOutSeasonMenu(items, outCard);
        return items;
    }

    /// <summary>
    /// Makes a card reachable by keyboard, and rings it when the cursor is on it. The ring
    /// is an outline rather than a border: a border would take layout space and shift every
    /// card beside it as the cursor moves through the row. A card no list walks (a season on
    /// a series' page) is a button of its own: a Tab stop that Enter and Space press, and
    /// whose menu the menu key opens.
    /// </summary>
    private void MarkCursor(BoxElement card, int cursorIndex, MediaItem? item = null)
    {
        card.IsFocusable = true;
        if (item is { } subject) card.ContextMenu(() => CardMenu(subject));
        if (cursorIndex < 0)
        {
            card.Pressable();
            return;
        }
        TrackCursor(card, cursorIndex);
        if (IsCursor(cursorIndex))
            card.Outline = CursorRing;
    }

    /// <summary>The cursor's ring on a card. Home moves it on the live cards too (MoveRingWithoutBuild).</summary>
    private static readonly Outline CursorRing = new(2f, Halide.Accent, 3f);

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

    /// <summary>Builds that had to make the cards again for pictures that landed, because a picture
    /// could not go in place (see <see cref="Land"/>): the grid's kept rows go with this.</summary>
    private int _artworkRemade;

    /// <summary>1 while taking what landed is posted to the UI thread and has not run. The cache
    /// calls back once per picture, on the thread pool, and a whole screenful lands within a few
    /// frames: posted one at a time, the pictures that land before it runs are taken together
    /// (when each landing built the view, a post for each made Home's 36 posters 41 builds, up
    /// to 10 of them in one frame). It clears the flag before it takes them, so a picture that
    /// lands while it runs posts the next one.</summary>
    private int _artworkPosted;

    /// <summary>The cache's callback, on the thread pool.</summary>
    private void PostArtworkLanded()
    {
        if (Interlocked.Exchange(ref _artworkPosted, 1) == 0)
            HultUI.Application.InvokeOnMainThread(OnArtworkLanded);
    }

    private readonly List<PosterCache.Landing> _landings = [];

    /// <summary>
    /// Pictures landed. Each goes into the picture elements on screen that wait for it, in place
    /// (<see cref="Waiting"/>), with no build: until plan 17 round 18 every landing built the whole
    /// view to make the cards that show it again: 32 KB a landing on a library's grid, with its rows
    /// made again besides, 84 KB on a film's page, 121 KB on a season's, and a library's first open
    /// was 12 or 13 builds. A picture that failed changes nothing: the placeholder stays. The view is
    /// built when a picture was asked for by something that is made again to show it (Discover's
    /// cards, a chapter's still), and when one could not go in place: it failed under the stand-in
    /// of another scale, or the cache let it go before it was taken.
    /// </summary>
    /// <remarks>A picture on screen is never the next the cache's count cap pushes out: it landed
    /// last or was asked for when its card was made, and the cap is many screenfuls. A picture the
    /// cap pushes out is disposed at the next ask, in a build, which makes again every card still
    /// drawing it: a card's state compares the picture it shows (<see cref="CardArt"/>).</remarks>
    private void OnArtworkLanded()
    {
        Interlocked.Exchange(ref _artworkPosted, 0);
        _posters.TakeLanded(_landings);
        bool build = false, hero = false;
        foreach (var landing in _landings)
        {
            build |= landing.Build;
            if (_artWaits.Remove(landing.Key, out var waits)) build |= Land(landing.Key, waits);
            hero |= HeroLanded(landing.Url);
        }
        _landings.Clear();
        if (hero) LetGoOfHeroes();
        if (build)
        {
            _artworkRemade++;
            RerenderView();
        }
        else
        {
            // What the pictures that landed stood in for: the next ask would drop it, and with no
            // build there may be none for a long time.
            _posters.Collect();
        }
        // A trickplay sheet is artwork too, and the card over the scrubber is the player's, which
        // the app's build does not rebuild: a sheet landing under a pointer resting on the
        // scrubber showed only when the pointer next moved. Nothing when it is not hovered.
        _videoPlayer?.RefreshPreview();
    }

    // ── Pictures that land in place ──────────────────────────────────────

    /// <summary>
    /// What an artwork box draws, as a build asked the cache: the picture (its own, or the stand-in
    /// from the scale before), or null for the placeholder; and, while its own picture is on the
    /// way, the wait that puts it into the box when it lands.
    ///
    /// <para>A kept card's state holds this, and compares what the box shows: a card made while its
    /// picture was on the way, and given it in place since, equals one made now with the picture,
    /// so it is handed back; one whose picture the cache let go of does not, so it is made again
    /// with the placeholder and waits again.</para>
    /// </summary>
    internal readonly struct CardArt(HultUI.Core.Rendering.IImageSource? picture, ArtWait? wait) : IEquatable<CardArt>
    {
        /// <summary>What a box made now draws: the picture, a stand-in, or null for the placeholder.</summary>
        public HultUI.Core.Rendering.IImageSource? Picture { get; } = picture;

        /// <summary>The wait for its own picture, or null when that is in or will not come.</summary>
        public ArtWait? Wait { get; } = wait;

        /// <summary>What the box made from this shows now: the picture landed in place since, or
        /// what it was made with.</summary>
        private HultUI.Core.Rendering.IImageSource? Shown => Wait?.Landed ?? Picture;

        /// <summary>The picture it still waits for, by URL.</summary>
        private string? Awaits => Wait is { IsWaiting: true } wait ? wait.Key : null;

        public bool Equals(CardArt other) =>
            ReferenceEquals(Shown, other.Shown) && string.Equals(Awaits, other.Awaits, StringComparison.Ordinal);

        public override bool Equals(object? obj) => obj is CardArt other && Equals(other);

        /// <summary>Constant: what a box shows moves as its picture lands, and a state is compared,
        /// never hashed.</summary>
        public override int GetHashCode() => 0;
    }

    /// <summary>
    /// A picture element waiting for its picture: its URL, and the element itself, as a ref the
    /// reconcile points at the live element the one made is patched into. The element is keyed by
    /// the URL while it waits, so the reconcile patches into it only an element waiting for the same
    /// picture: a card reused for another item, or the same one at another size, is a new element,
    /// and this one leaves the window. Ended when the picture lands, or fails.
    /// </summary>
    internal sealed class ArtWait(string key, HultUI.Core.Rendering.IImageSource? standIn) : Ref<ImageElement>
    {
        public string Key { get; } = key;

        /// <summary>The picture from the scale before it draws until its own lands.</summary>
        public HultUI.Core.Rendering.IImageSource? StandIn { get; } = standIn;

        /// <summary>The next wait for the same picture: a card made again before it landed.</summary>
        public ArtWait? Next;

        public bool IsWaiting { get; private set; } = true;

        /// <summary>The picture its element took when it landed; null when it did not take one.</summary>
        public HultUI.Core.Rendering.IImageSource? Landed { get; private set; }

        /// <summary>Lets go of the element: a card kept since still holds this in its state.</summary>
        public void End(HultUI.Core.Rendering.IImageSource? landed)
        {
            IsWaiting = false;
            Landed = landed;
            Current = null;
        }
    }

    /// <summary>The picture elements waiting for their pictures, by URL: each the latest of a chain
    /// (<see cref="ArtWait.Next"/>). A wait is taken out when its picture lands, and after a frame when
    /// its element has left the window (<see cref="PruneArtWaits"/>), so this holds only what is on the
    /// way to the window, or on the page kept for Back (ApertureApp.KeptPage.cs).</summary>
    private readonly Dictionary<string, ArtWait> _artWaits = new(StringComparer.Ordinal);

    /// <summary>The cache asked for a picture another element puts into itself when it lands
    /// (<see cref="Waiting"/>): what to draw now, and the wait while its own picture is on the way.</summary>
    private CardArt ArtInPlace(Uri? url, Uri? standIn)
    {
        var (image, status) = _posters.Get(url, standIn, inPlace: true, out bool own);
        var picture = status == PosterCache.Status.Ready && image is { IsLoaded: true } ? image : null;
        return new CardArt(picture, own || status == PosterCache.Status.Failed || url is null
            ? null : new ArtWait(url.ToString(), picture));
    }

    /// <summary>The element waits for its picture when <paramref name="wait"/> says it is on the way:
    /// keyed by its URL and found by it when it lands (<see cref="OnArtworkLanded"/>).</summary>
    private ImageElement Waiting(ImageElement element, ArtWait? wait)
    {
        if (wait is null) return element;
        element.Key = wait.Key;
        element.WithRef(wait);
        wait.Next = _artWaits.GetValueOrDefault(wait.Key);
        _artWaits[wait.Key] = wait;
        AskToPruneArtWaits();
        return element;
    }

    private bool _artWaitsPruneAsked;
    private Action? _pruneArtWaits;

    /// <summary>Drops the waits that will never take their picture once the next frame is drawn
    /// (<see cref="PruneArtWaits"/>). From a build that made a waiting element, and from each app
    /// build while any wait is left, as a page that left the window may have been the last to ask.</summary>
    private void AskToPruneArtWaits()
    {
        if (_artWaitsPruneAsked || _artWaits.Count == 0) return;
        _artWaitsPruneAsked = true;
        _window.RequestPostFrameCallback(_pruneArtWaits ??= PruneArtWaits);
    }

    private readonly List<(string Key, ArtWait? Head)> _artWaitsMoved = [];
    private HashSet<ImageElement>? _artWaitsSeen;

    /// <summary>
    /// Lets go of the waits whose element left the window, and of the older of two waits whose
    /// element is the same (a card made again while its picture was on the way: its ref was pointed
    /// at the live element by the patch). Each wait holds its element, and through it the page it was
    /// on: a picture whose body stalled never lands, and every page visited after it was kept (plan 17
    /// round 19: 0.33 MB a page, 41 MB after 250). A wait that stays is the newest for its element,
    /// the one the card's kept state holds (<see cref="CardArt"/>). The page kept for Back is out of the
    /// window but comes back as it is, so its waits stay while it is kept, and its pictures go into it
    /// when they land (<see cref="CanTakePicture"/>); once it is let go of, they go with the next prune.
    /// </summary>
    /// <remarks>Only after a frame, never in a build: in the app's build a kept Home shelf and the
    /// cards it hands back are out of the tree, and a card just made is out of it until its build is
    /// patched in, so a prune there took live cards' waits and they kept the glyph. After a frame an
    /// element out of the tree never comes back but on the page kept for Back: a kept element is handed
    /// back only from the window (<c>KeptElements.TryKeep</c>), and a part whose host left is made again
    /// by its next mount.</remarks>
    private void PruneArtWaits()
    {
        _artWaitsPruneAsked = false;
        var seen = _artWaitsSeen ??= new HashSet<ImageElement>(ReferenceEqualityComparer.Instance);
        foreach (var (key, chain) in _artWaits)
        {
            ArtWait? head = null, tail = null;
            bool moved = false;
            for (var wait = chain; wait is not null;)
            {
                var next = wait.Next;
                wait.Next = null;
                if (wait.Current is { } element && element.Key == key && CanTakePicture(element) && seen.Add(element))
                {
                    if (tail is null) head = wait;
                    else tail.Next = wait;
                    tail = wait;
                }
                else
                {
                    // Its element is gone, or another wait's: nothing will take it.
                    wait.Current = null;
                    moved = true;
                }
                wait = next;
            }
            seen.Clear();
            if (moved) _artWaitsMoved.Add((key, head));
        }
        foreach (var (key, head) in _artWaitsMoved)
        {
            if (head is null) _artWaits.Remove(key);
            else _artWaits[key] = head;
        }
        _artWaitsMoved.Clear();
    }

    /// <summary>For the tests: the waits held, and the elements they wait in that can still take their
    /// picture: in the window, or on the page kept for Back.</summary>
    internal (int Waits, int Elements) ArtWaitCounts
    {
        get
        {
            var elements = new HashSet<ImageElement>(ReferenceEqualityComparer.Instance);
            int waits = 0;
            foreach (var (key, chain) in _artWaits)
                for (var wait = chain; wait is not null; wait = wait.Next)
                {
                    waits++;
                    if (wait.Current is { } element && element.Key == key && CanTakePicture(element)) elements.Add(element);
                }
            return (waits, elements.Count);
        }
    }

    /// <summary>
    /// A picture landed for these waits. The elements still waiting for it in the window (or on the page
    /// kept for Back) take it,
    /// as a make would draw it (<see cref="PictureLook"/>). That marks their layout up to the root,
    /// and the next frame lays out the element and the boxes above it again, measuring nothing
    /// beside them: a library card's landing 9 elements and 10 measures, where the build it replaces
    /// laid out 20 and measured 15. A picture that failed leaves the placeholder. True when the view
    /// has to be built to show what landed: the cache no longer holds it, or it failed under a
    /// stand-in.
    /// </summary>
    private bool Land(string key, ArtWait waits)
    {
        _posters.TryPeek(key, out var image, out var status);
        var picture = status == PosterCache.Status.Ready && image is { IsLoaded: true } ? image : null;
        bool build = false;
        for (var wait = waits; wait is not null; wait = wait.Next)
        {
            if (wait.Current is not { } element || element.Key != key || !CanTakePicture(element)) continue;
            if (picture is not null)
            {
                ShowPicture(element, picture);
                continue;
            }
            // The placeholder as a make shows it when the picture has failed: no longer keyed.
            element.Key = null;
            // Failed under the stand-in of another scale, which goes once every download has
            // landed; or let go of before it was taken (the count cap, past four hundred).
            if (wait.StandIn is not null || status != PosterCache.Status.Failed) build = true;
        }
        for (var wait = waits; wait is not null; wait = wait.Next)
            wait.End(picture is not null && wait.Current is { } element && ReferenceEquals(element.Source, picture)
                ? picture : null);
        return build;
    }

    /// <summary>A picture element as a make with the picture makes it: the picture covering the box
    /// it sits in. Also what turns a waiting element, the placeholder glyph or an empty frame, into
    /// the picture when it lands, so the element is what the next build would make.</summary>
    private static ImageElement PictureLook(ImageElement element, HultUI.Core.Rendering.IImageSource? picture)
    {
        element.Fit = ImageFit.Cover;
        element.Box.Width = Dimension.Percent(100);
        element.Box.Height = Dimension.Percent(100);
        element.Source = picture;
        return element;
    }

    private static void ShowPicture(ImageElement element, HultUI.Core.Rendering.IImageSource picture)
    {
        element.Key = null;
        // A glyph does not shrink; a picture has the default. Layout styles mark nothing: the
        // source marks the layout.
        element.Flex.FlexShrink = 1;
        PictureLook(element, picture);
    }

    /// <summary>A placeholder glyph, as the image element a picture can go into.</summary>
    private static ImageElement Glyph(string name, float size) =>
        Ui.Icon(name, size, Halide.TextFaint) as ImageElement
        // A glyph Phosphor does not have is an empty box: an empty image of its size draws the same.
        ?? Image.New().Width(Ui.IconBox(size)).Height(Ui.IconBox(size)).FlexShrink(0);

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
    internal float HeroWidth => HeroWidthAt(_window.LayoutWidth);

    internal float HeroHeight => HeroHeightAt(_window.LayoutHeight);

    private static float HeroWidthAt(float windowWidth) => MathF.Max(1f, windowWidth - Halide.RailWidth);

    private static float HeroHeightAt(float windowHeight) => Math.Clamp(windowHeight * 0.44f, 280f, 420f);

    /// <summary>The side that decides a hero's cover fit in a window of this size, and the device
    /// pixels asked for along it (<see cref="HeroPixels"/>).</summary>
    private (bool ByHeight, int Pixels) HeroAsk(double? aspect, float windowWidth, float windowHeight)
    {
        float width = HeroWidthAt(windowWidth), height = HeroHeightAt(windowHeight);
        bool byHeight = aspect is { } a && a > width / height * 1.005;
        return (byHeight, HeroPixels(byHeight ? height : width, _artworkScale));
    }

    /// <summary>The device pixels a hero asks for across a side <paramref name="layoutSize"/>
    /// long: what the side covers at this scale, rounded up to a <see cref="HeroStep"/>.</summary>
    internal static int HeroPixels(float layoutSize, float scale) =>
        (ArtworkPixels(layoutSize, scale) + HeroStep - 1) / HeroStep * HeroStep;

    /// <summary>
    /// How many heroes' pictures the app keeps at most: the page on screen and the three behind
    /// it, for Back, while they fit <see cref="HeroBytesKept"/>. The cache bounds pictures by
    /// count, 400 of them, and a hero counted as one, like a poster: every page visited kept its
    /// own, 9 MB at 150 % in a 1,400-wide window (33 MB on a 4K monitor). A page further back
    /// than the heroes kept fetches its hero again.
    /// </summary>
    internal const int HeroesKept = 4;

    /// <summary>The heroes kept whatever their size: those of the last two pages shown with
    /// one, the page on screen and the one before it.</summary>
    internal const int HeroesAlwaysKept = 2;

    /// <summary>
    /// The decoded bytes the heroes kept may hold together; past it, only
    /// <see cref="HeroesAlwaysKept"/> stay. A hero is held twice, its pixels in memory and its
    /// texture on the GPU, and its size follows the window: four were 41 MB of pixels at 150 % in
    /// a 1,440 by 900 window (2,176 by 1,224 each), 53 MB with a film whose poster is its hero
    /// (2,000 by 3,000), and about 120 MB in a window filling a 4K monitor. Four heroes the size
    /// of a 1,440-wide window at 100 % (4.5 MB each) still fit.
    /// </summary>
    internal const long HeroBytesKept = 24L * 1024 * 1024;

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
    /// size the window passed over before it landed. The picture of a page further back than
    /// the heroes kept goes at once (<see cref="LetGoOfHeroes"/>).</para>
    /// </summary>
    private (HultUI.Core.Rendering.IImageSource? Image, PosterCache.Status Status) HeroArtwork(
        string? art, double? aspect, Func<int?, int?, Uri?> sized)
    {
        var (url, standIn) = HeroAsked(art, aspect, sized);
        var result = _posters.Get(url, standIn);
        HeroAnswered(url);
        return result;
    }

    /// <summary><see cref="HeroArtwork"/> for a hero whose picture goes into it in place when it lands
    /// (<see cref="Waiting"/>): an item page's.</summary>
    private CardArt HeroInPlace(string? art, double? aspect, Func<int?, int?, Uri?> sized)
    {
        var (url, standIn) = HeroAsked(art, aspect, sized);
        var result = ArtInPlace(url, standIn);
        HeroAnswered(url);
        return result;
    }

    /// <summary>The URL a hero asks for now, and the picture on screen that stands in for it; the
    /// hero kept moves to the latest.</summary>
    private (Uri? Url, Uri? StandIn) HeroAsked(string? art, double? aspect, Func<int?, int?, Uri?> sized)
    {
        _fitHero = true;
        _fitHeroAspect = aspect;
        var (byHeight, pixels) = HeroAsk(aspect, _window.LayoutWidth, _window.LayoutHeight);
        if (art is null || (byHeight ? sized(null, pixels) : sized(pixels, null)) is not { } url)
            return (null, null);

        var hero = _heroes.Find(h => h.Art == art);
        if (hero is null) hero = new HeroPicture(art);
        else _heroes.Remove(hero);
        _heroes.Add(hero);

        // Every other size of this artwork goes once the new picture is in: the one on screen
        // stands in until then, and one the window passed over before it landed (a drag across
        // several steps) is not wanted at all.
        if (hero.Asked is { } previous && previous != url && previous != hero.Shown) _posters.Retire(previous);
        var standIn = hero.Shown is { } shown && shown != url ? shown : null;
        if (standIn is not null) _posters.Retire(standIn);
        hero.Asked = url;
        return (url, standIn);
    }

    /// <summary>The cache has answered a hero's ask: the picture on screen is its own once that is
    /// in, and the heroes kept are held to their bounds.</summary>
    private void HeroAnswered(Uri? url)
    {
        if (url is null) return;
        if (_posters.IsReady(url) && _heroes.Count > 0) _heroes[^1].Shown = url;
        LetGoOfHeroes();
    }

    /// <summary>A picture landed that a hero kept asked for last: it is what that hero shows now,
    /// put in place without a build. True when one did, for <see cref="LetGoOfHeroes"/>.</summary>
    private bool HeroLanded(Uri url)
    {
        bool any = false;
        foreach (var hero in _heroes)
            if (hero.Asked == url && hero.Shown != url && _posters.IsReady(url))
            {
                hero.Shown = url;
                any = true;
            }
        return any;
    }

    /// <summary>
    /// Holds the heroes kept to <see cref="HeroesKept"/> and <see cref="HeroBytesKept"/>, letting
    /// go of the page furthest back first and never of the <see cref="HeroesAlwaysKept"/> latest.
    /// Run whenever a hero is asked for, so a picture whose size is known only once it has landed
    /// counts from the build that draws it. What is let go is dropped at once
    /// (<see cref="PosterCache.Drop"/>), not retired until the next downloads land: no page on
    /// screen draws it, this is the UI thread, and after a page's last picture has landed
    /// nothing may land again for a long time, so a retired hero stayed a page longer.
    /// </summary>
    private void LetGoOfHeroes()
    {
        while (_heroes.Count > HeroesAlwaysKept && (_heroes.Count > HeroesKept || HeroBytes() > HeroBytesKept))
        {
            var oldest = _heroes[0];
            _heroes.RemoveAt(0);
            if (oldest.Shown is { } shown) _posters.Drop(shown);
            if (oldest.Asked is { } asked && asked != oldest.Shown) _posters.Drop(asked);
        }
    }

    /// <summary>The decoded bytes of the heroes kept: each one's picture on screen.</summary>
    private long HeroBytes()
    {
        long bytes = 0;
        foreach (var kept in _heroes)
            if (kept.Shown is { } shown) bytes += _posters.DecodedBytes(shown);
        return bytes;
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
        else if (item.ResumePosition is not null) parts.Add("partly watched");
        if (item.UnplayedItemCount is > 0 and var unplayed) parts.Add($"{unplayed} unwatched");
        return string.Join(", ", parts);
    }

    private static string ResumeAccessibleName(MediaItem item)
    {
        var name = AccessibleNameFor(item);
        if (item.RunTime is { } run && item.ResumePosition is { } at)
            return $"Resume {name}, {Ui.SpokenRemaining(run, at)}";
        return "Play " + name;
    }

    /// <summary>Opening an item: a film or an episode goes to its detail page, a series or
    /// season to its children, a folder into itself.</summary>
    internal void Open(MediaItem item)
    {
        CursorOnto(item);
        Navigate(new Route(ApertureView.Detail, item.Id, item.Name));
    }

    /// <summary>
    /// The cursor onto <paramref name="item"/>'s card in the list it is walking, however the card
    /// was opened (a click, a finger, a reader's double tap), so Back returns to it, the card focused.
    /// Only the keys moved the cursor. Back from a page opened any other way started at the first card
    /// on screen, which TalkBack read instead of the card it had opened (a Galaxy S25).
    /// </summary>
    private void CursorOnto(MediaItem item)
    {
        int byId = -1;
        for (int i = 0; i < _cursorItems.Count; i++)
        {
            if (ReferenceEquals(_cursorItems[i], item))
            {
                _cursorIndex = i;
                return;
            }
            if (byId < 0 && _cursorItems[i] is MediaItem same && same.Id == item.Id) byId = i;
        }
        if (byId >= 0) _cursorIndex = byId;
    }

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

        int page = ShelfPage(automationId, count, cardWidth, out int perPage);
        int pages = Math.Max(1, (count + perPage - 1) / perPage);

        var heading = Flex.Row(gap: Halide.S2).AlignItems(AlignItems.Center);
        // A heading for a reader, which TalkBack moves between by headings and says as one.
        heading.AddChild(Text.New(title).FontSize(15f).FontWeight(FontWeight.SemiBold).TextColor(Halide.Text)
            .AccessibleRole(AccessibilityRole.Heading));
        if (trailing is not null)
        {
            heading.AddChild(Box.New().Width(Halide.S2));
            heading.AddChild(Ui.Line(trailing, 12f, Halide.TextFaint));
        }
        heading.AddChild(Box.New().FlexGrow(1));
        if (pages > 1)
        {
            // Not on a phone, where the title wrapped for it ("Latest in / Films"): the arrows say
            // there is more.
            if (!Halide.Phone) heading.AddChild(Ui.Mono($"{page + 1}/{pages}", 11f, Halide.TextFaint));
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

    /// <summary>The page the row <paramref name="rowId"/> of <paramref name="count"/> cards shows (see
    /// <see cref="CardRow(string, int, Func{int, Element}, float, string, string?, Element?)"/>), and
    /// how many cards a page holds.</summary>
    private int ShelfPage(string rowId, int count, float cardWidth, out int perPage)
    {
        perPage = Math.Max(1, VisibleCards(cardWidth));
        int pages = Math.Max(1, (count + perPage - 1) / perPage);
        return Math.Clamp(_rowPages.GetValueOrDefault(rowId), 0, pages - 1);
    }

    /// <summary>
    /// The cursor's place <paramref name="index"/> on a shelf that starts at <paramref name="start"/> in
    /// the walk and holds <paramref name="count"/> cards, <paramref name="perPage"/> a page, showing
    /// <paramref name="page"/>: when the place is on the shelf but on another page, turns the shelf to
    /// it and says so, and the build to come draws that page (a Home shelf's own, Discover's view). A shelf
    /// used to stay where it was, so the cursor walked on with no card ringed and the keyboard left behind.
    /// </summary>
    private bool TurnShelfTo(int index, int start, int count, string rowId, int perPage, int page)
    {
        if (index < start || index >= start + count) return false;
        int wanted = (index - start) / perPage;
        if (wanted == page) return false;
        _rowPages[rowId] = wanted;
        return true;
    }

    private Element PageButton(string glyph, string label, string rowId, int target, bool enabled)
    {
        var b = Ui.IconButton(glyph, label, () => TurnRowTo(rowId, target),
            14f, enabled ? Halide.TextDim : Halide.TextFaint, $"{rowId}-{label.ToLowerInvariant()}");
        if (!enabled)
        {
            b.Opacity(0.4f);
            // Nowhere to page to: no Tab stop, as a disabled button is none.
            b.IsFocusable = false;
        }
        return b;
    }

    /// <summary>How many cards of a width fit across the content area at this window size.
    /// Read from the window rather than from a measured box: a flex row of rigid cards
    /// reports one card as its intrinsic width, so reading the box back would say one.</summary>
    internal int VisibleCards(float cardWidth)
    {
        if (!_fitCardWidths.Contains(cardWidth)) _fitCardWidths.Add(cardWidth);
        return CardsAcross(cardWidth, _window.LayoutWidth);
    }
}
