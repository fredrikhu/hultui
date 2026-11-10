using HultUI.Core.Builders;
using HultUI.Core.Components;
using HultUI.Core.Elements;
using HultUI.Core.Events;
using HultUI.Core.Layout;
using HultUI.Core.Primitives;
using HultUI.Core.Reactivity;
using HultUI.Core.Rendering;
using HultUI.Core.Styling;
using VesperCommander.Theme;

namespace VesperCommander;

public partial class CommanderApp
{
    // ── View modes (Detail / Brief / Thumbnails) ─────────────────────────
    // Detail is the classic row list (BuildList). Brief and Thumbnails are alternate
    // per-pane layouts over the same listing; the toggle lives in the breadcrumb row.
    private static int Si(char side) => side == 'L' ? 0 : 1;

    // ── Pure layout math (unit-tested; drives both rendering and navigation) ──
    /// <summary>Brief grid geometry: rows per column (the height fills before wrapping),
    /// total columns, the column width (186px fixed once columns overflow the pane, else
    /// stretched to fill), and whether the pane scrolls horizontally.</summary>
    internal const float BriefColWidth = 186f;
    internal static (int rows, int cols, float colW, bool overflow) BriefGrid(int count, float availW, float availH, float rowH)
    {
        int rows = Math.Max(1, (int)(availH / rowH));
        int cols = Math.Max(1, (count + rows - 1) / rows);
        int maxFit = Math.Max(1, (int)(availW / BriefColWidth));
        // Fixed-width columns, left-packed (Total Commander style); overflow scrolls horizontally.
        return (rows, cols, BriefColWidth, cols > maxFit);
    }

    /// <summary>Thumbnail tiles that fit across the pane width, at least one.</summary>
    internal static int ThumbPerRow(float availW, float tileW, float gap) =>
        Math.Max(1, (int)((availW + gap) / (tileW + gap)));

    // Per-side geometry cached at Build time so the key handler can navigate without
    // re-measuring: Brief rows-per-column, Thumbnail columns-per-row, and a page step.
    private readonly int[] _briefRows = { 1, 1 };
    private readonly int[] _thumbCols = { 1, 1 };
    private readonly int[] _viewPage = { 1, 1 };
    private readonly float[] _briefColW = { 186f, 186f };
    private readonly Ref<ScrollViewElement> _briefScrollL = new(), _briefScrollR = new();
    private Ref<ScrollViewElement> BriefScroll(char side) => side == 'L' ? _briefScrollL : _briefScrollR;
    /// <summary>Brief's scroll offset as its scroll view left it, kept once the view left the window
    /// (<see cref="LetGoOfLeftViews"/>): the next Brief build makes its columns for it, as it did from
    /// the old scroll view.</summary>
    private readonly float[] _briefScrollX = new float[2];
    private readonly Ref<VirtualListElement<ThumbRowItem>> _thumbVListL = new(), _thumbVListR = new();
    private Ref<VirtualListElement<ThumbRowItem>> ThumbVList(char side) => side == 'L' ? _thumbVListL : _thumbVListR;

    // Shared thumbnail decode cache. A decode lands on a worker thread and posts a rebuild so
    // the tile swaps its thumbnail in, one post at a time (see PostThumbnailLanded).
    private ThumbnailCache? _thumbsCache;
    private ThumbnailCache Thumbs => _thumbsCache ??= new ThumbnailCache(PostThumbnailLanded);

    /// <summary>1 while a build for decoded thumbnails is posted to the UI thread and has not
    /// run. The cache calls back once per decode, and a screenful of tiles decodes at once: a
    /// build posted for each made 44 thumbnails 45 builds, up to 44 of them in one frame.
    /// Posted one at a time, the decodes that land before the build runs share it. The build
    /// clears the flag before it reads the cache, so a decode that lands while it runs posts the
    /// next one.</summary>
    private int _thumbnailPosted;

    /// <summary>The cache's callback, on a worker thread.</summary>
    private void PostThumbnailLanded()
    {
        if (Interlocked.Exchange(ref _thumbnailPosted, 1) == 0)
            HultUI.Application.InvokeOnMainThread(OnThumbnailLanded);
    }

    private void OnThumbnailLanded()
    {
        Interlocked.Exchange(ref _thumbnailPosted, 0);
        MarkViewDirty();
    }

    private const float ViewToggleWidth = 88f;   // reserved so breadcrumbs truncate first

    // Tile / grid geometry, per density (design §D). tileH = pad 4 + image + gap 4 + name + pad 4.
    private readonly record struct ThumbM(float TileW, float TileH, float ImgW, float ImgH, float Icon, int NameLines, float Gap);
    private ThumbM ThumbMetrics() => _settings.Density == Density.Compact
        ? new ThumbM(TileW: 84, TileH: 82, ImgW: 76, ImgH: 56, Icon: 24, NameLines: 1, Gap: 7)
        : new ThumbM(TileW: 100, TileH: 110, ImgW: 92, ImgH: 70, Icon: 30, NameLines: 2, Gap: 8);

    private Element BuildViewArea(char side, IReadOnlyList<FileEntry> list, bool active) => ViewOf(side) switch
    {
        ViewMode.Brief => BuildBrief(side, list, active),
        ViewMode.Thumbnails => BuildThumbs(side, list, active),
        _ => BuildList(side, list, active),
    };

    // ── Thumbnail grid (§D) ──────────────────────────────────────────────
    // A wrap grid of fixed tiles. Items are chunked into rows-of-tiles and driven through a
    // virtual list (one item per grid row) so large folders virtualize for free — only the
    // on-screen rows realize, so only their tiles request thumbnails (P3). The rows are item
    // rows: a row is its entries and the cursor's place among them (ThumbRowItem), so a key
    // makes the row the cursor left and the row it reached, and keeps the rest.
    private Element BuildThumbs(char side, IReadOnlyList<FileEntry> list, bool active)
    {
        var m = ThumbMetrics();
        var marks = Marks(side);

        var (paneW, perRow, visRows) = ThumbGeometry(side);
        _thumbCols[Si(side)] = perRow;
        _viewPage[Si(side)] = perRow * visRows;
        NoteFit(side, ViewMode.Thumbnails, perRow: perRow);

        // A kept row shows what it showed when it was made; everything its tiles show besides
        // their entries and the cursor is in the shape — the marks, the files cut for a move, a
        // result set (the tooltips), a folder a drag hovers, the density, the scale the icons are
        // cut at, and the decodes landed so far, which turn placeholders into thumbnails. A
        // change makes the rows again; the mounted list is told before this build patches it. So
        // it is after a cursor key moved the ring with no build (SettleRing).
        var shape = new ThumbRowShape(m, ResultsOf(side), Icons.DisplayScale, _dropTarget, _thumbsCache?.Landed ?? 0);
        if (ThumbRowsState(side).Changed(shape, marks, _cutPending) && ThumbVList(side).Current is { } mounted)
            mounted.InvalidateRows();
        // A row made again keeps the tiles whose entry and state did not change (ThumbRow), as
        // long as everything a tile shows besides those is what they were made with.
        if (ThumbTileShapeState(side).Changed(new ThumbTileShape(m, ResultsOf(side), Icons.DisplayScale, _dropTarget, _app.Theme)))
            ThumbTilesOf(side).Clear();

        // A reader reads the rows as they are (RowsAreComposite): each tile is an item of its own,
        // named after its file, and the cursor's is selected (Tile, ShowCursor), so a key that moves
        // the ring with no build tells a reader the item it reached, with no tree. A row of tiles
        // was one item named by all the files on it, and the cursor was in none of them. The rows
        // off screen are not in the tree, as in any composite list; they come in as they realize.
        var thumbs = VirtualListElement<ThumbRowItem>.New();
        thumbs.RowsAreComposite = true;
        return thumbs
            .ItemHeight((int)(m.TileH + m.Gap))
            .Items(new ThumbRows(list, perRow, ThumbCursor(side, list)))
            .RenderWith((row, _) => ThumbRow(side, row, m, marks))
            .WithRef(ThumbVList(side))
            .FlexGrow(1)
            .Width(Dimension.Px(paneW));
    }

    /// <summary>The thumbnail grid's geometry for a pane, as a build sizes it: the list's width,
    /// the tiles that fit across it, and the rows that fit down it. The width comes from the
    /// window, not the list's own box: a virtual list of rigid fixed-width tiles reports its
    /// content width (one tile) as its intrinsic size, and the flex stretch resolves the cross
    /// axis to that — so reading it back would collapse the tiles per row to 1. The list's width
    /// is set explicitly instead. The height still reads the box.</summary>
    private (float PaneW, int PerRow, int VisRows) ThumbGeometry(char side)
    {
        var m = ThumbMetrics();
        const float pad = 10f;
        float paneW = PaneContentWidthEstimate();
        float availW = paneW - 2 * pad;
        float availH = ThumbVList(side).Current?.Layout.AbsoluteContentBox.Height
            ?? (_window.LayoutHeight > 0 ? _window.LayoutHeight - 160f : 600f);
        int perRow = ThumbPerRow(availW, m.TileW, m.Gap);
        int visRows = Math.Max(1, (int)(availH / (m.TileH + m.Gap)));
        return (paneW, perRow, visRows);
    }

    /// <summary>
    /// One row of the thumbnail grid as its list keeps it: the entries it shows (where they
    /// start in the listing, and how many) and which of them the cursor is on, -1 for none. Two
    /// are equal when they show equal entries with the cursor in the same place — compared entry
    /// by entry, so a reload or a filter that hands over a new list of the same entries keeps
    /// the row, and one that changes an entry makes it again.
    /// </summary>
    internal readonly struct ThumbRowItem(IReadOnlyList<FileEntry> list, int start, int count, int cursor)
        : IEquatable<ThumbRowItem>
    {
        public IReadOnlyList<FileEntry> List { get; } = list;
        public int Start { get; } = start;
        public int Count { get; } = count;
        public int Cursor { get; } = cursor;

        public bool Equals(ThumbRowItem other)
        {
            if (Start != other.Start || Count != other.Count || Cursor != other.Cursor) return false;
            if (List is null || other.List is null) return List is null && other.List is null;
            for (int k = 0; k < Count; k++)
                if (!List[Start + k].Equals(other.List[Start + k])) return false;
            return true;
        }

        public override bool Equals(object? obj) => obj is ThumbRowItem other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(Start, Count, Cursor);
    }

    /// <summary>The grid's rows over a listing, made per index as the list asks: nothing per row is
    /// held, and one of these per build is all a build allocates for them.</summary>
    private sealed class ThumbRows(IReadOnlyList<FileEntry> list, int perRow, int cursor) : IReadOnlyList<ThumbRowItem>
    {
        public int Count => (list.Count + perRow - 1) / perRow;

        public ThumbRowItem this[int row]
        {
            get
            {
                int start = row * perRow;
                int count = Math.Min(perRow, list.Count - start);
                int at = cursor - start;
                return new ThumbRowItem(list, start, count, at >= 0 && at < count ? at : -1);
            }
        }

        public IEnumerator<ThumbRowItem> GetEnumerator()
        {
            for (int i = 0; i < Count; i++) yield return this[i];
        }

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }

    private readonly record struct ThumbRowShape(ThumbM Metrics, ResultListing? Results, float IconScale,
        (char Side, string Dir)? DropTarget, int Landed);

    private readonly GridState<ThumbRowShape> _thumbRowsL = new(), _thumbRowsR = new();
    private GridState<ThumbRowShape> ThumbRowsState(char side) => side == 'L' ? _thumbRowsL : _thumbRowsR;

    /// <summary>
    /// What a grid view's kept rows or cells show besides their entries and the cursor, as they
    /// were made with it: a shape, and the sets an entry looks itself up in — the marks and the
    /// files cut for a move. <see cref="Changed"/> says whether any of it moved since the last
    /// build asked, and takes the new state either way.
    /// </summary>
    private sealed class GridState<TShape> where TShape : struct, IEquatable<TShape>
    {
        private TShape _shape;
        private bool _made;
        private readonly HashSet<string> _marks = PathSet();
        private readonly HashSet<string> _cut = new(StringComparer.OrdinalIgnoreCase);

        public bool Changed(TShape shape, HashSet<string> marks, HashSet<string> cut)
        {
            bool changed = !_made || !shape.Equals(_shape);
            _made = true;
            _shape = shape;
            if (!_marks.SetEquals(marks)) { changed = true; _marks.Clear(); _marks.UnionWith(marks); }
            if (!_cut.SetEquals(cut)) { changed = true; _cut.Clear(); _cut.UnionWith(cut); }
            return changed;
        }
    }

    /// <summary>
    /// What a grid view's kept cells show besides their entries, the cursor and what each cell
    /// keeps itself (Brief's marks and cut): a shape, as the last build made them with it.
    /// <see cref="Changed"/> says whether it moved since the last build asked, and takes the new
    /// shape either way.
    /// </summary>
    private sealed class ShapeState<TShape> where TShape : struct, IEquatable<TShape>
    {
        private TShape _shape;
        private bool _made;

        public bool Changed(TShape shape)
        {
            bool changed = !_made || !shape.Equals(_shape);
            _made = true;
            _shape = shape;
            return changed;
        }
    }

    /// <summary>
    /// A row of tiles. A row made again — a mark, a thumbnail landing, the cursor moved onto it or
    /// off it with a build — hands back each tile whose entry, cursor, mark, cut and thumbnail are
    /// what it was made with (REACTIVITY.md, "Hand back the elements a key does not change"): the
    /// row is keyed by its first entry's place, so the list patches the row it replaces from it,
    /// and the tiles are keyed by their paths, so the patch puts the live ones back untouched. A
    /// cursor key mostly makes no row at all (MoveCursorLive). The cursor is read as it is now, not
    /// from the item: the list makes the rows that scroll in from its last build's items, which
    /// know nothing of the keys that moved the ring since, and a row made from them would ring the
    /// tile the cursor left.
    /// </summary>
    private Element ThumbRow(char side, ThumbRowItem item, ThumbM m, HashSet<string> marks)
    {
        var tiles = ThumbTilesOf(side);
        string key = ThumbRowKey(item.Start);
        var row = Flex.Row(gap: m.Gap).Height(m.TileH).AlignItems(AlignItems.FlexStart)
            .Padding(new Thickness(0, 10, 0, 10));
        row.Key = key;
        int cursorAt = ThumbCursor(side, item.List);
        for (int k = 0; k < item.Count; k++)
        {
            var e = item.List[item.Start + k];
            bool cursor = item.Start + k == cursorAt;
            bool marked = marks.Contains(MarkKey(e));
            bool cut = !marked && _cutPending.Contains(e.FullPath);
            var (thumb, status) = TileThumb(e, m);
            var state = new ThumbTileState(e, cursor, marked, cut, thumb, status);
            if (tiles.TryKeep(e.FullPath, state, out var kept, place: key))
            {
                // A key may have moved the cursor's ref to another tile since this one was made.
                if (cursor) CursorRowRef(side).Current = (BoxElement)kept;
                row.AddChild(kept);
                continue;
            }
            row.AddChild(tiles.Made(e.FullPath, state, Tile(side, e, m, cursor, marked, cut, thumb, status)));
        }
        return row;
    }

    /// <summary>The cursor's place in a pane's grid now: -1 in the pane without the keyboard, which
    /// shows none.</summary>
    private int ThumbCursor(char side, IReadOnlyList<FileEntry> list) =>
        side == _active && list.Count > 0 ? Math.Clamp(Cursor(side), 0, list.Count - 1) : -1;

    private static readonly List<string> ThumbRowKeys = [];

    /// <summary>"t" and the row's first entry's place, made once per place.</summary>
    private static string ThumbRowKey(int start)
    {
        while (ThumbRowKeys.Count <= start) ThumbRowKeys.Add("t" + ThumbRowKeys.Count);
        return ThumbRowKeys[start];
    }

    /// <summary>A tile's thumbnail and its state, for an image file; none for anything else.</summary>
    private (IImageSource? Thumb, ThumbnailCache.Status Status) TileThumb(FileEntry e, ThumbM m)
    {
        if (Icons.KindFor(e) != IconKind.Image || e.IsDir) return (null, default);
        int target = (int)(Math.Max(m.ImgW, m.ImgH) * 2f);
        return Thumbs.Get(e.FullPath, e.Size, e.Modified, target);
    }

    /// <summary>What a tile shows besides the grid's shape: its entry, the cursor on it, a mark, a
    /// cut, and its thumbnail as the cache had it.</summary>
    private readonly record struct ThumbTileState(FileEntry Entry, bool Cursor, bool Marked, bool Cut,
        IImageSource? Thumb, ThumbnailCache.Status Status);

    /// <summary>What every tile shows besides its own state: the density's sizes, a result set
    /// (the tooltips), the scale the icons are cut at, a folder a drag hovers, the theme.</summary>
    private readonly record struct ThumbTileShape(ThumbM Metrics, ResultListing? Results, float IconScale,
        (char Side, string Dir)? DropTarget, object? Theme);

    /// <summary>
    /// The tiles a pane's thumbnail rows were made with, by path, and the live element each is
    /// now. The rows are made in the frame, outside the app's builds, so there is no <c>Begin</c>:
    /// a tile is handed back only into a row made again in its own row's place (its parent carries
    /// that row's key), as a row still on screen keeps its tiles, and a tile in a row that
    /// scrolled away is out of the tree. All are let go when what every tile shows changes
    /// (<see cref="ThumbTileShape"/>); those out of the tree as more are made.
    /// </summary>
    private readonly KeptElements<ThumbTileState> _thumbTilesL = new(), _thumbTilesR = new();
    private KeptElements<ThumbTileState> ThumbTilesOf(char side) => side == 'L' ? _thumbTilesL : _thumbTilesR;
    private readonly ShapeState<ThumbTileShape> _thumbTileShapeL = new(), _thumbTileShapeR = new();
    private ShapeState<ThumbTileShape> ThumbTileShapeState(char side) => side == 'L' ? _thumbTileShapeL : _thumbTileShapeR;

    private BoxElement Tile(char side, FileEntry e, ThumbM m, bool cursor, bool marked, bool cut,
        IImageSource? thumb, ThumbnailCache.Status tstatus)
    {
        Color nameFg = marked ? Nocturne.MarkRed : e.IsDir ? Nocturne.DirText : Nocturne.Text;
        if (cut) nameFg = Nocturne.Alpha(nameFg, 0.45);
        var kind = Icons.KindFor(e);
        Color? tint = marked ? Nocturne.MarkRed : null;

        var tile = Flex.Column(gap: 4).Width(m.TileW).Height(m.TileH).AlignItems(AlignItems.Center)
            .Padding(new Thickness(4)).CornerRadius(Nocturne.RadiusRow)
            .BorderWidth(1f)
            .Cursor(CursorType.Default).Tooltip(EntryTooltip(side, e));
        // An item to a reader, named after its entry (ItemName), the cursor's selected (ShowCursor).
        tile.Role = HultUI.Core.Accessibility.AccessibilityRole.ListItem;
        tile.AccessibleName = ItemName(e);
        ShowCursor(tile, cursor);

        // Image area on a Neutral900 backing. P2 shows the kind icon (images use a dimmed
        // image glyph as a placeholder); P3 swaps in the decoded thumbnail.
        var area = Box.New().Width(m.ImgW).Height(m.ImgH).FlexShrink(0)
            .CornerRadius(Nocturne.RadiusSm).Background(Nocturne.Neutral900)
            .AlignItems(AlignItems.Center).JustifyContent(JustifyContent.Center);
        if (kind == IconKind.Image && !e.IsDir)
        {
            if (tstatus == ThumbnailCache.Status.Ready && thumb is { IsLoaded: true } src && src.IntrinsicSize.Width > 0)
            {
                var im = Image.New(src);
                im.Fit = ImageFit.Contain;   // letterbox on the backing
                area.AddChild(im.Size(Dimension.Percent(100), Dimension.Percent(100)));
            }
            else if (tstatus == ThumbnailCache.Status.Failed)
            {
                // Undecodable / oversized → full-strength icon + ext badge ("won't preview").
                area.AddChild(Icons.Draw(IconKind.Image, tint, m.Icon));
                if (!e.IsParent && ExtBadge(e) is { } fb) area.AddChild(fb);
            }
            else
            {
                var ph = Icons.Draw(IconKind.Image, tint, m.Icon);
                ph.Opacity = 0.4f;   // dimmed placeholder while the decode runs
                area.AddChild(ph);
                area.AddChild(ShimmerBand(m));
            }
        }
        else
        {
            area.AddChild(Icons.Draw(kind, tint, m.Icon));
            // Ext badge on non-image files (dirs / parent get none; images use the thumbnail).
            if (!e.IsDir && !e.IsParent && ExtBadge(e) is { } badge) area.AddChild(badge);
        }
        // Marked chip stays visible on bright thumbnails.
        if (marked) area.AddChild(MarkChip());
        tile.AddChild(area);

        tile.AddChild(Ui.Mono(e.Name, 10.5f, nameFg).TextAlign(TextAlign.Center)
            .MaxLines(m.NameLines).TextOverflow(TextOverflow.Ellipsis).Width(m.TileW - 8));

        if (cut) tile.Opacity = 0.45f;                 // whole-tile ghost
        else tile.OnHover(TileHover);
        AttachEntryPointer(tile, side, e, cursor);
        return tile;
    }

    /// <summary>The hover tint, on a tile without the cursor's ring: a key moves the ring with no
    /// build (MoveCursorLive), so a tile cannot know at its build whether it will have it. The ring
    /// is the tile's resting border, which the hover sees (Element.Restyle).</summary>
    private static readonly Action<BoxElement> TileHover = static t =>
    {
        if (t.BorderColor == Color.Transparent) t.BackgroundColor = Nocturne.Alpha(Nocturne.Accent500, 0.06);
    };

    /// <summary>The cursor on a Brief cell or a thumbnail tile, as a build makes it and as a key
    /// moves it with no build: the accent ring on the 1 px border every cell reserves, and the
    /// accent fill; and to a reader the item selected, which the window tells with no tree (an
    /// item's selected state is marked on its node, as a list's is).</summary>
    private static void ShowCursor(BoxElement el, bool cursor)
    {
        el.BorderColor = cursor ? Nocturne.Accent600 : Color.Transparent;
        el.BackgroundColor = cursor ? Nocturne.Alpha(Nocturne.Accent500, 0.13) : Color.Transparent;
        el.AccessibleSelected = cursor;
    }

    /// <summary>A Brief cell or a thumbnail tile as a reader says it: what the view shows, the entry's
    /// name, a folder said to be one. Not its size and date, which these views do not show, and not
    /// its mark, which its colour shows: a mark would rename the item, a node with children, and a
    /// new name there builds the tree with a reader (60 files: 0 → 1 tree and +26–34 KB a mark,
    /// where it is walked in place); Detail's row says it. The name is the entry's own string (a
    /// folder's is made with its cell), so making a cell costs next to nothing more.</summary>
    private static string ItemName(FileEntry e) => e.IsParent ? "Parent folder" : e.IsDir ? e.Name + ", folder" : e.Name;

    private static readonly Action<Element> CursorOn = static e => ShowCursor((BoxElement)e, cursor: true);
    private static readonly Action<Element> CursorOff = static e => ShowCursor((BoxElement)e, cursor: false);

    private static Element? ExtBadge(FileEntry e)
    {
        string ext = ExtensionOf(e.Name).TrimStart('.').ToUpperInvariant();
        if (ext.Length == 0) return null;
        if (ext.Length > 4) ext = ext[..4];
        var badge = Box.New().Padding(new Thickness(1, 3, 1, 3)).CornerRadius(3f)
            .Background(Nocturne.Alpha(Nocturne.Neutral800, 0.88))
            .Children(Ui.Mono(ext, 8.5f, Nocturne.Neutral300));
        badge.Position.Type = PositionType.Absolute;
        badge.Position.Right = Dimension.Px(3);
        badge.Position.Bottom = Dimension.Px(3);
        return badge;
    }

    /// <summary>A faint light band across the loading placeholder (design §E) — a static
    /// "shimmer" cue that a thumbnail is decoding, without a strobing per-tile spinner.</summary>
    private static Element ShimmerBand(ThumbM m)
    {
        var band = Box.New().Width(m.ImgW).Height(m.ImgH * 0.3f)
            .Background(Nocturne.Alpha(Nocturne.Neutral100, 0.045));
        band.Position.Type = PositionType.Absolute;
        band.Position.Left = Dimension.Px(0);
        band.Position.Top = Dimension.Px(m.ImgH * 0.35f);
        return band;
    }

    private static Element MarkChip()
    {
        var chip = Box.New().Width(7).Height(7).CornerRadius(2f).Background(Nocturne.MarkRed);
        chip.Position.Type = PositionType.Absolute;
        chip.Position.Left = Dimension.Px(3);
        chip.Position.Top = Dimension.Px(3);
        return chip;
    }

    // ── View-mode toggle (breadcrumb-row end) ────────────────────────────
    private Element ViewToggle(char side)
    {
        var cur = ViewOf(side);
        var box = Flex.Row(gap: 2).FlexShrink(0).AlignItems(AlignItems.Center)
            .Padding(new Thickness(2)).CornerRadius(Nocturne.RadiusSm)
            .Background(Nocturne.Bg).BorderColor(Nocturne.Divider).BorderWidth(1f);
        box.AddChild(ViewToggleBtn(side, ViewMode.Detail, IconKind.ViewDetail, cur));
        box.AddChild(ViewToggleBtn(side, ViewMode.Brief, IconKind.ViewBrief, cur));
        box.AddChild(ViewToggleBtn(side, ViewMode.Thumbnails, IconKind.ViewThumb, cur));
        return box;
    }

    private Element ViewToggleBtn(char side, ViewMode mode, IconKind glyph, ViewMode cur)
    {
        bool on = cur == mode;
        var bind = KeyText(ViewCmd(mode));
        var b = Box.New().Width(22).Height(15).CornerRadius(3f).Cursor(CursorType.Pointer)
            .AlignItems(AlignItems.Center).JustifyContent(JustifyContent.Center)
            .Tooltip(bind.Length > 0 ? $"{ModeLabel(mode)} · {bind}" : ModeLabel(mode));
        if (on) b.Background(Nocturne.Alpha(Nocturne.Accent500, 0.14));
        b.AddChild(Icons.Draw(glyph, on ? Nocturne.Accent : Nocturne.Neutral500, 11f));
        b.OnClick(_ => { _active = side; SetView(side, mode); });
        return on ? b : b.HoverBg(Nocturne.Alpha(Nocturne.Accent500, 0.08), Color.Transparent);
    }

    private static string ModeLabel(ViewMode m) => m switch
    {
        ViewMode.Brief => "Brief", ViewMode.Thumbnails => "Thumbnails", _ => "Detail",
    };

    private static Cmd ViewCmd(ViewMode m) => m switch
    {
        ViewMode.Brief => Cmd.ViewBrief, ViewMode.Thumbnails => Cmd.ViewThumbnails, _ => Cmd.ViewDetail,
    };

    // ── Brief view — names only, fixed columns, top-to-bottom flow ───────
    // Vertical never scrolls (columns fill the height); overflow grows rightward with a
    // horizontal scroll. Only the columns intersecting the viewport are realized
    // (column-window virtualization) — a spacer stands in for the off-screen left columns
    // and the content's fixed width keeps the scrollbar range correct.
    private Element BuildBrief(char side, IReadOnlyList<FileEntry> list, bool active)
    {
        int n = list.Count;
        int cur = Math.Clamp(Cursor(side), 0, Math.Max(0, n - 1));
        string? cursorPath = active && n > 0 ? list[cur].FullPath : null;
        var marks = Marks(side);
        float rowH = RowHeight;

        var g = ScrollBriefBeforeBuild(side, n, cur, BriefGeometry(side, n));
        _briefRows[Si(side)] = g.Rows;
        _briefColW[Si(side)] = BriefColWidth;
        _viewPage[Si(side)] = g.Rows * Math.Max(1, g.VisCols);
        NoteFit(side, ViewMode.Brief, brief: g.Fit);

        // The cells are kept between builds. A cell shows its entry, whether the cursor is on it,
        // whether it is marked and whether it is cut for a move, and besides those only what the
        // shape holds — a result set (the tooltips), a folder a drag hovers, the row height, the
        // scale the icons are cut at, the theme. While the shape is unchanged, a cell whose entry,
        // cursor, mark and cut are what it was made with is handed back as it is: the live
        // element, which the reconcile puts back where it was without touching it (it is keyed by
        // its path, so its column matches it by key), as it puts back a persistent child
        // component. Only the cells that changed are made, and patched into the ones they
        // replace: a key in Brief makes two cells instead of every cell on screen, and a mark the
        // cell it marks. Kept only from the build just before (cells another build dropped are
        // not in the tree any more). A cursor key mostly makes no cell at all (MoveCursorLive).
        var cells = BriefCells(side);
        var shape = new BriefCellShape(rowH, Icons.DisplayScale, _app.Theme, ResultsOf(side), _dropTarget);
        if (BriefCellsState(side).Changed(shape)) cells.Clear();
        cells.Begin(_builds);

        var content = Flex.Row(gap: 0).FlexShrink(0);
        if (g.Overflow) content.Width(g.TotalCols * BriefColWidth); else content.FlexGrow(1);
        if (g.FirstCol > 0)
        {
            var spacer = Box.New().Width(g.FirstCol * BriefColWidth).FlexShrink(0);
            spacer.Key = "spacer";
            content.AddChild(spacer);
        }

        for (int c = g.FirstCol; c <= g.LastCol; c++)
        {
            var column = Flex.Column(gap: 0).Width(BriefColWidth).FlexShrink(0);
            // Keyed by its place in the grid, so a column scrolled into view is the column it is
            // rather than whichever column used to stand at its place in the row.
            column.Key = BriefColumnKey(c);
            // 1px rule left of every column but the first (columns are full except the last).
            if (c > 0) column.BorderColor(Nocturne.Alpha(Nocturne.Text, 0.10)).BorderWidth(new Thickness(1, 0, 0, 0));
            for (int r = 0; r < g.Rows; r++)
            {
                int i = c * g.Rows + r;
                if (i >= n) break;
                var e = list[i];
                bool cursor = cursorPath != null && PathEquals(e.FullPath, cursorPath);
                bool marked = marks.Contains(MarkKey(e));
                bool cut = !marked && _cutPending.Contains(e.FullPath);   // a mark shows over a cut
                var state = new BriefCellState(e, cursor, marked, cut);
                if (cells.TryKeep(e.FullPath, state, out var kept))
                {
                    // A key may have moved the cursor's ref to another cell since this one was made.
                    if (cursor) CursorRowRef(side).Current = (BoxElement)kept;
                    column.AddChild(kept);
                    continue;
                }
                column.AddChild(cells.Made(e.FullPath, state, BriefCell(side, e, cursor, marked, cut)));
            }
            content.AddChild(column);
        }

        // Explicit width + height so the horizontal ScrollView can't shrink-wrap to content.
        var scroll = ScrollView.Horizontal().WithRef(BriefScroll(side)).FlexShrink(0)
            .Width(Dimension.Px(g.PaneW)).Height(Dimension.Px(g.AvailH));
        scroll.Children(content);
        // Any scroll re-windows which columns realize on the next build.
        scroll.On<ScrollEvent>(_ => MarkViewDirty());
        return scroll;
    }

    /// <summary>The Brief grid's geometry for a pane, as a build sizes it. Both dimensions come
    /// from the window geometry, never from the ScrollView's own box: a horizontal ScrollView
    /// shrink-wraps its height to its content, so reading its height back would collapse the
    /// rows per column to 1 (items would spread across single-item columns); reading the pane
    /// height back is equally circular (it reflects the collapsed state). The window height
    /// minus the fixed chrome (title bar + tab strip + breadcrumb + footer) is a stable input
    /// that can't feed the loop. <see cref="BriefFit"/> is the part a cell's place depends on.</summary>
    private BriefGeom BriefGeometry(char side, int n)
    {
        float rowH = RowHeight;
        float availH = Math.Max(rowH, (_window.LayoutHeight > 0 ? _window.LayoutHeight : 820f) - BriefChromeHeight);
        float paneW = PaneContentWidthEstimate();
        var (rows, totalCols, colW, overflow) = BriefGrid(n, paneW, availH, rowH);
        int visCols = overflow ? Math.Max(1, (int)(paneW / colW)) : totalCols;
        float scrollX = overflow ? Math.Max(0, BriefScroll(side).Current?.ScrollOffsetX ?? _briefScrollX[Si(side)]) : 0;
        int firstCol = overflow ? Math.Clamp((int)(scrollX / colW), 0, Math.Max(0, totalCols - 1)) : 0;
        int lastCol = Math.Min(totalCols - 1, firstCol + visCols + 1);   // +1 buffer column
        return new BriefGeom(paneW, availH, new BriefFit(rows, totalCols, overflow, visCols, firstCol, lastCol));
    }

    /// <summary>
    /// A build that a scroll to the cursor waits for (<see cref="RequestScroll"/>: a key that moves
    /// the cursor out of view, a filter, a folder loaded) scrolls first, on the live scroll view,
    /// and makes the columns for that scroll. The scroll used to come after the build
    /// (OnAfterBuild), and a scroll set from code fires no ScrollEvent to build again: the build
    /// had made the columns for the scroll before it, so the columns a page jump scrolled to stayed
    /// blank until the next key, and that key built again. OnAfterBuild still scrolls, and finds
    /// the cursor in view. Never past the end of the columns this build makes (a filter that drops
    /// columns): the layout clamps the scroll there, short of the columns made from it.
    /// </summary>
    private BriefGeom ScrollBriefBeforeBuild(char side, int n, int cur, BriefGeom g)
    {
        if (!g.Overflow || !(side == 'L' ? _pendingScrollL : _pendingScrollR)
            || BriefScroll(side).Current is not { IsHosted: true } sv) return g;
        float before = sv.ScrollOffsetX, colW = BriefColWidth;
        float viewW = sv.Layout.AbsoluteContentBox.Width;
        float x = (cur / g.Rows) * colW;
        float want = x < before ? x : x + colW > before + viewW ? x + colW - viewW : before;
        sv.ScrollOffsetX = Math.Min(want, Math.Max(0, g.TotalCols * colW - viewW));
        return sv.ScrollOffsetX == before ? g : BriefGeometry(side, n);
    }

    /// <summary>Where the Brief grid puts its cells: rows per column, the columns, whether they
    /// overflow the pane (and scroll), how many fit, and the window of columns realized.</summary>
    private readonly record struct BriefFit(int Rows, int TotalCols, bool Overflow, int VisCols, int FirstCol, int LastCol);

    /// <summary><see cref="BriefFit"/> and the sizes the scroll view is given.</summary>
    private readonly record struct BriefGeom(float PaneW, float AvailH, BriefFit Fit)
    {
        public int Rows => Fit.Rows;
        public int TotalCols => Fit.TotalCols;
        public bool Overflow => Fit.Overflow;
        public int VisCols => Fit.VisCols;
        public int FirstCol => Fit.FirstCol;
        public int LastCol => Fit.LastCol;
    }

    private static string BriefColumnKey(int c) => "col" + c;

    /// <summary>What a Brief cell shows besides the grid's shape: its entry, whether the cursor is
    /// on it, whether it is marked or cut for a move (only some cells show those, so they are the
    /// cell's and not the shape's).</summary>
    private readonly record struct BriefCellState(FileEntry Entry, bool Cursor, bool Marked, bool Cut);

    private readonly record struct BriefCellShape(float RowH, float IconScale, object? Theme,
        ResultListing? Results, (char Side, string Dir)? DropTarget);

    /// <summary>The cells the last build that built a pane in Brief made or handed back, by path.</summary>
    private readonly KeptElements<BriefCellState> _briefCellsL = new(), _briefCellsR = new();
    private KeptElements<BriefCellState> BriefCells(char side) => side == 'L' ? _briefCellsL : _briefCellsR;
    private readonly ShapeState<BriefCellShape> _briefStateL = new(), _briefStateR = new();
    private ShapeState<BriefCellShape> BriefCellsState(char side) => side == 'L' ? _briefStateL : _briefStateR;

    // Title bar + tab strip + breadcrumb + footer around the Brief list, subtracted from the
    // window height to get the column-fill height. Approximate — a few px off just shifts the
    // wrap point by one cell; kept a touch generous so the list never overflows the footer.
    private const float BriefChromeHeight = 142f;

    private BoxElement BriefCell(char side, FileEntry e, bool cursor, bool marked, bool cut)
    {
        Color fg = marked ? Nocturne.MarkRed : e.IsDir ? Nocturne.DirText : Nocturne.Text;
        if (cut) fg = Nocturne.Alpha(fg, 0.45);

        var cell = Flex.Row(gap: 7).Height(RowHeight).AlignItems(AlignItems.Center)
            .Padding(new Thickness(0, 9, 0, 9))
            .BorderWidth(1f)
            .Cursor(CursorType.Default).Tooltip(EntryTooltip(side, e));
        // An item to a reader, named after its entry (ItemName), the cursor's selected (ShowCursor).
        cell.Role = HultUI.Core.Accessibility.AccessibilityRole.ListItem;
        cell.AccessibleName = ItemName(e);
        ShowCursor(cell, cursor);

        var icon = Icons.Draw(Icons.KindFor(e), marked ? Nocturne.MarkRed : (Color?)null);
        if (cut) icon.Opacity = 0.45f;
        cell.AddChild(icon);
        cell.AddChild(Ui.Mono(e.Name, 11.5f, fg).MaxLines(1).TextOverflow(TextOverflow.Ellipsis).FlexGrow(1));
        AttachEntryPointer(cell, side, e, cursor);
        return cell;
    }

    // ── The ring a cursor key moves with no build ────────────────────────
    // A key that moves only the cursor in Brief or Thumbnails takes the ring off the live cell or
    // tile it leaves and puts it on the one it reaches (MoveCursorLive). Those two then show what
    // no build made them with, and the next build would hand them back as they are: one made
    // without the ring and reached by a key, in the pane Tab leaves, kept its ring. And the
    // thumbnail list keeps a row while its item is unchanged, and its items are its last build's,
    // so a row that a key moved the ring into or out of is not even made again. So each build of
    // the pane first puts the cells and tiles back as they were made, and has the thumbnail list
    // make its rows again; each then shows its new state as a build makes it.

    /// <summary>Per pane: each cell or tile a key moved the ring on or off with no build since the
    /// pane's last build, and whether it was made with the ring. One that is back as it was made is
    /// dropped, so a long walk holds the two it is between.</summary>
    private readonly List<(BoxElement Element, bool MadeRinged)>[] _ringMoved = [[], []];
    private readonly bool[] _ringMovedSinceBuild = new bool[2];

    /// <summary>The live Brief cell or thumbnail tile showing <paramref name="e"/>, as the pane's
    /// latest build made or handed it back (a tile: in a row of the list on screen); null when
    /// none is.</summary>
    private BoxElement? LiveCell(char side, FileEntry e)
    {
        if (ViewOf(side) == ViewMode.Brief) return BriefCells(side).Live(e.FullPath) as BoxElement;
        return ThumbTilesOf(side).Live(e.FullPath) is BoxElement { Parent.Parent: { } owner } tile
            && ReferenceEquals(owner, ThumbVList(side).Current) ? tile : null;
    }

    /// <summary>The ring on or off a live cell or tile, with no build (<see cref="ShowCursor"/>,
    /// as a build draws it; Restyle keeps it through a hover).</summary>
    private void MoveRing(char side, BoxElement el, bool on)
    {
        bool shows = el.BorderColor == Nocturne.Accent600;
        if (shows == on) return;
        var moved = _ringMoved[Si(side)];
        int i = 0;
        while (i < moved.Count && !ReferenceEquals(moved[i].Element, el)) i++;
        if (i == moved.Count) moved.Add((el, shows));
        else if (moved[i].MadeRinged == on) moved.RemoveAt(i);
        el.Restyle(on ? CursorOn : CursorOff);
        _ringMovedSinceBuild[Si(side)] = true;
    }

    /// <summary>
    /// After a build: a ref whose element left the window held the whole view that element was in,
    /// through its parents (REACTIVITY.md, "A ref into a view keeps the whole view"): Brief's cells
    /// and columns behind Detail or Thumbnails. The cursor's row (a cell, a tile or a row, whichever
    /// view drew the cursor) is let go, as nothing reads one out of the window (CursorRowBox wants a
    /// row on screen). Brief's scroll view too, keeping the one thing a later build reads from it, its
    /// offset (<see cref="BriefGeometry"/>, <see cref="RememberPaneScroll"/>). Both are set again by the
    /// build or the key that next draws them. The Sync rows a key moved the ring on and Sync's scroll
    /// view the same once they left (only Sync's next build and keys read them).
    /// </summary>
    private void LetGoOfLeftViews()
    {
        for (int si = 0; si < 2; si++)
        {
            char side = si == 0 ? 'L' : 'R';
            var cursor = CursorRowRef(side);
            if (cursor.Current is { IsHosted: false }) cursor.Current = null;
            var brief = BriefScroll(side);
            if (brief.Current is { IsHosted: false } scroll)
            {
                _briefScrollX[si] = scroll.ScrollOffsetX;
                brief.Current = null;
            }
        }
        // The Sync rows a key moved the ring on, and Sync's scroll view, once they left the window.
        if (_syncRingMoved is [var (moved, _, _), ..] && !moved.IsHosted) _syncRingMoved.Clear();
        if (_syncScroll.Current is { IsHosted: false }) _syncScroll.Current = null;
    }

    /// <summary>At the start of a pane's build: the cells and tiles a key moved the ring on or off
    /// go back as they were made, and the thumbnail list makes its rows again (see above).</summary>
    private void SettleRing(char side)
    {
        int si = Si(side);
        if (!_ringMovedSinceBuild[si]) return;
        _ringMovedSinceBuild[si] = false;
        foreach (var (el, madeRinged) in _ringMoved[si])
            if (el.IsHosted) el.Restyle(madeRinged ? CursorOn : CursorOff);
        _ringMoved[si].Clear();
        if (ThumbVList(side).Current is { IsHosted: true } list) list.InvalidateRows();
    }

    /// <summary>Whether the cursor's column is in full view in Brief, where
    /// <see cref="ScrollBriefToCursor"/> leaves the scroll as it is, and the columns made are the
    /// ones a build would make at this scroll. A key that scrolls builds, and scrolls before it
    /// builds (<see cref="ScrollBriefBeforeBuild"/>), so the next key finds the columns in view
    /// made.</summary>
    private bool BriefCursorShown(char side)
    {
        if (BriefScroll(side).Current is not { IsHosted: true } sv
            || !BriefGeometry(side, Rows(side).Count).Fit.Equals(_fit[Si(side)].Brief)) return false;
        int rows = Math.Max(1, _briefRows[Si(side)]);
        float colW = _briefColW[Si(side)];
        float x = (Cursor(side) / rows) * colW;
        return x >= sv.ScrollOffsetX && x + colW <= sv.ScrollOffsetX + sv.Layout.AbsoluteContentBox.Width;
    }

    /// <summary>Keep the cursor's row in the vertical viewport (Thumbnails).</summary>
    private void ScrollThumbToCursor(char side)
    {
        var tv = ThumbVList(side).Current;
        if (tv == null) return;
        var m = ThumbMetrics();
        int perRow = Math.Max(1, _thumbCols[Si(side)]);
        int rowIdx = Cursor(side) / perRow;
        tv.ScrollRangeIntoView(rowIdx * (m.TileH + m.Gap), m.TileH + m.Gap);
    }

    /// <summary>Keep the cursor's column in the horizontal viewport (Brief only).</summary>
    private void ScrollBriefToCursor(char side)
    {
        var sv = BriefScroll(side).Current;
        if (sv == null) return;
        int rows = Math.Max(1, _briefRows[Si(side)]);
        float colW = _briefColW[Si(side)];
        float x = (Cursor(side) / rows) * colW;
        float viewW = sv.Layout.AbsoluteContentBox.Width;
        if (x < sv.ScrollOffsetX) sv.ScrollOffsetX = x;
        else if (x + colW > sv.ScrollOffsetX + viewW) sv.ScrollOffsetX = x + colW - viewW;
    }

    // ── Mode-aware arrow navigation ──────────────────────────────────────
    // Returns true when it consumed the key. Detail (and, until P2, Thumbnails) fall
    // through to the default per-pane handler.
    private bool ViewNav(char side, KeyCode key)
    {
        var mode = ViewOf(side);
        if (mode == ViewMode.Detail) return false;
        int page = Math.Max(1, _viewPage[Si(side)]);
        // Brief: ↑↓ step one item (column-major), ←→ jump a whole column (rowsPerCol).
        // Thumbnails: ←→ step one tile, ↑↓ jump a whole row (perRow).
        int rowStep = mode == ViewMode.Brief ? 1 : Math.Max(1, _thumbCols[Si(side)]);
        int colStep = mode == ViewMode.Brief ? Math.Max(1, _briefRows[Si(side)]) : 1;
        switch (key)
        {
            case KeyCode.Up: MoveCursor(side, -rowStep); return true;
            case KeyCode.Down: MoveCursor(side, rowStep); return true;
            case KeyCode.Left: MoveCursor(side, -colStep); return true;
            case KeyCode.Right: MoveCursor(side, colStep); return true;
            case KeyCode.Home: MoveCursor(side, int.MinValue / 2); return true;
            case KeyCode.End: MoveCursor(side, int.MaxValue / 2); return true;
            case KeyCode.PageDown: MoveCursor(side, page); return true;
            case KeyCode.PageUp: MoveCursor(side, -page); return true;
            default: return false;
        }
    }

    /// <summary>The hover text for a Brief cell / thumbnail tile. Normally the name (these
    /// two views truncate hard); in a fed-in result set the full path, since neither view has
    /// room for the source folder the Detail rows show.</summary>
    private string EntryTooltip(char side, FileEntry e) =>
        InResults(side) && !e.IsParent ? e.FullPath : e.Name;

    // ── Shared entry pointer interaction (rows, brief cells, tiles) ───────
    private void AttachEntryPointer(BoxElement el, char side, FileEntry e, bool cursor)
    {
        // The context-menu key has no pointer to anchor to, so the cursor row keeps a Ref for
        // the menu to open under. Set here rather than in each view builder — all three call
        // through this one place.
        if (cursor) el.WithRef(CursorRowRef(side));

        el.On<PointerDownEvent>(ev =>
        {
            // A right press starts marking straight away — but held still, it becomes the
            // context menu instead, and the mark it applied is taken back (see FireContextHold).
            if (ev.Button == 2)
            {
                BeginRightMark(side, e);
                ArmContextHold(side, e, ev.Position.X, ev.Position.Y);
                return;
            }
            if (ev.Button != 0) return;
            _active = side;
            int idx = IndexOfPath(Rows(side), e.FullPath);
            if (idx >= 0) SetCursor(side, idx);
            if (IsDoublePress(side, e.FullPath)) ActivateCursor(side);
            // The cursor is the view's; a pane made active reaches the trails through
            // MarkViewDirty's compare, and a folder opened rebuilt its own parts (NavigateTo).
            MarkViewDirty();
        });
        // Passive and heard only while a right-drag marks: ApplyRightMark rebuilds for what it
        // changes, and a move over a row otherwise changes nothing.
        el.OnPassive<PointerMoveEvent>(_ => { if (_rmarkActive) ApplyRightMark(side, e); },
            _rowMoveListening ??= () => _rmarkActive);

        // Press-and-drag hands the selection to the desktop (see CommanderApp.DragDrop).
        // ".." is a way back, not a file, so it is neither dragged nor dropped onto.
        if (!e.IsParent && CanDragOut)
        {
            el.Draggable();
            el.OnDragStart(_ => StartFileDragOut(side, e));
        }

        // A folder row is its own drop target, so a drag can be aimed at a subfolder without
        // opening it first. Files are not: dropping "onto" a file has no meaning here.
        if (e.IsDir && !e.IsParent)
        {
            AttachDropTarget(el, side, e.FullPath, isRow: true);
            // Tinted, not ringed: rows carry no border, and adding one would nudge every
            // row below by a pixel the moment a drag passed over this one.
            if (IsDropHighlighted(side, e.FullPath))
                el.Background(Nocturne.Alpha(Nocturne.Accent, 0.28));
        }
    }

    // ── Window fit: what a build sizes from the window ───────────────────
    /// <summary>
    /// What a build decided for a pane from the window's size: the view it built there, the
    /// path trail's first kept segment (or the results root as it collapsed it), and the grid's
    /// geometry — Brief's rows, columns and window of realized columns, Thumbnails' tiles per
    /// row. The grids' sizes (an explicit width, and Brief's height) follow the window too, but
    /// those can be written on the live element; everything here changes what the build makes.
    /// A resize that leaves all of it as it is needs no build (<see cref="OnWindowResized"/>).
    /// </summary>
    private readonly record struct PaneFit(ViewMode View, int CrumbStart, string? ResultsRoot, BriefFit Brief, int PerRow);

    private readonly PaneFit[] _fit = new PaneFit[2];
    // The build that built each pane: a pane the latest build did not build is not on screen.
    private readonly int[] _fitBuild = { -1, -1 };

    private void BeginFit(char side)
    {
        SettleRing(side);
        // The path trail is a part the build may keep: what it noted when it last built stands
        // (it builds again when the trail would change, OnWindowResized).
        var trail = _fit[Si(side)];
        _fit[Si(side)] = new PaneFit(default, trail.CrumbStart, trail.ResultsRoot, default, 0);
        _fitBuild[Si(side)] = _builds;
    }

    private void NoteFitCrumbs(char side, int start, string? resultsRoot) =>
        _fit[Si(side)] = _fit[Si(side)] with { CrumbStart = start, ResultsRoot = resultsRoot };

    private void NoteFit(char side, ViewMode view, int perRow = 0, BriefFit brief = default) =>
        _fit[Si(side)] = _fit[Si(side)] with { View = view, PerRow = perRow, Brief = brief };

    /// <summary>What a build now would decide for a pane the latest build built, at the window's
    /// present size — read the way the build reads it.</summary>
    private PaneFit FitNow(char side)
    {
        var built = _fit[Si(side)];
        var fit = ResultsOf(side) is { } results
            ? new PaneFit(built.View, -1, ResultsRootLabel(results), default, 0)
            : new PaneFit(built.View, PlanCrumbs(Dir(side)).Start, null, default, 0);
        return built.View switch
        {
            ViewMode.Brief => fit with { Brief = BriefGeometry(side, Rows(side).Count).Fit },
            ViewMode.Thumbnails => fit with { PerRow = ThumbGeometry(side).PerRow },
            _ => fit,
        };
    }

    /// <summary>Whether a build now would make anything the latest build did not, for the size
    /// the window has now: a pane's path trail, or a grid's rows, columns or tiles per row.</summary>
    private bool WindowFitChanged()
    {
        for (int si = 0; si < 2; si++)
        {
            char side = si == 0 ? 'L' : 'R';
            if (_fitBuild[si] == _builds && !_fit[si].Equals(FitNow(side))) return true;
        }
        return false;
    }

    /// <summary>
    /// A resize that changes nothing a build makes: the grids take the window's size on their
    /// live elements — the thumbnail list its width, Brief's scroll view its width and height,
    /// exactly what the build would have given them — and the page a PgDn moves in Thumbnails
    /// follows the height. The rows, cells and tiles stay as they are.
    /// </summary>
    private void FitGridsInPlace()
    {
        for (int si = 0; si < 2; si++)
        {
            if (_fitBuild[si] != _builds) continue;
            char side = si == 0 ? 'L' : 'R';
            switch (_fit[si].View)
            {
                case ViewMode.Thumbnails when ThumbVList(side).Current is { } list:
                {
                    var (paneW, perRow, visRows) = ThumbGeometry(side);
                    _viewPage[si] = perRow * visRows;
                    SetSize(list, Dimension.Px(paneW), list.Box.Height);
                    break;
                }
                case ViewMode.Brief when BriefScroll(side).Current is { } scroll:
                {
                    var g = BriefGeometry(side, Rows(side).Count);
                    SetSize(scroll, Dimension.Px(g.PaneW), Dimension.Px(g.AvailH));
                    break;
                }
            }
        }

        static void SetSize(Element e, Dimension width, Dimension height)
        {
            if (e.Box.Width == width && e.Box.Height == height) return;
            e.Box.Width = width;
            e.Box.Height = height;
            e.MarkLayoutDirty();
        }
    }

    private float PaneContentWidthEstimate()
    {
        float win = _window.LayoutWidth > 0 ? _window.LayoutWidth : 1240f;
        float paneW = (win - RailWidth - 1f) / 2f;   // two 1fr columns either way
        return paneW - 4f;
    }
}
