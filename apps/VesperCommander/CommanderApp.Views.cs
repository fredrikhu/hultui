using HultUI.Core.Builders;
using HultUI.Core.Elements;
using HultUI.Core.Events;
using HultUI.Core.Layout;
using HultUI.Core.Primitives;
using HultUI.Core.Reactivity;
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
    private readonly Ref<VirtualListElement<ThumbRowItem>> _thumbVListL = new(), _thumbVListR = new();
    private Ref<VirtualListElement<ThumbRowItem>> ThumbVList(char side) => side == 'L' ? _thumbVListL : _thumbVListR;

    // Shared thumbnail decode cache. A decode lands on a worker thread and marshals a
    // rebuild so the tile swaps its thumbnail in.
    private ThumbnailCache? _thumbsCache;
    private ThumbnailCache Thumbs => _thumbsCache ??= new ThumbnailCache(
        () => HultUI.Application.InvokeOnMainThread(MarkDirty));

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
        int n = list.Count;
        int cur = Math.Clamp(Cursor(side), 0, Math.Max(0, n - 1));
        var marks = Marks(side);

        var (paneW, perRow, visRows) = ThumbGeometry(side);
        _thumbCols[Si(side)] = perRow;
        _viewPage[Si(side)] = perRow * visRows;
        NoteFit(side, ViewMode.Thumbnails, perRow: perRow);

        // A kept row shows what it showed when it was made; everything its tiles show besides
        // their entries and the cursor is in the shape — the marks, the files cut for a move, a
        // result set (the tooltips), a folder a drag hovers, the density, the scale the icons are
        // cut at, and the decodes landed so far, which turn placeholders into thumbnails. A
        // change makes the rows again; the mounted list is told before this build patches it.
        var shape = new ThumbRowShape(m, ResultsOf(side), Icons.DisplayScale, _dropTarget, _thumbsCache?.Landed ?? 0);
        if (ThumbRowsState(side).Changed(shape, marks, _cutPending) && ThumbVList(side).Current is { } mounted)
            mounted.InvalidateRows();

        return VirtualListElement<ThumbRowItem>.New()
            .ItemHeight((int)(m.TileH + m.Gap))
            .Items(new ThumbRows(list, perRow, active && n > 0 ? cur : -1))
            .RenderWith((row, _) => ThumbRow(side, row, m, marks))
            // A row of tiles is one item to assistive technology (the tiles are not controls of
            // their own), named by the files on it, as the list view names one. One delegate per
            // pane, reading the marks as they are: the list keeps each row's label while the row
            // is unchanged, and a change of marks (in the shape) makes them again.
            .AccessibleItemLabel(ThumbLabelFn(side))
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

    private Func<ThumbRowItem, string?>? _thumbLabelL, _thumbLabelR;

    private Func<ThumbRowItem, string?> ThumbLabelFn(char side) => side == 'L'
        ? _thumbLabelL ??= row => ThumbRowLabel(row, Marks('L'))
        : _thumbLabelR ??= row => ThumbRowLabel(row, Marks('R'));

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

    /// <summary>A row of tiles as a reader says it: each file's name, a folder said to be one,
    /// and the marked ones said to be marked.</summary>
    private string ThumbRowLabel(ThumbRowItem row, HashSet<string> marks)
    {
        var sb = new System.Text.StringBuilder();
        for (int k = 0; k < row.Count; k++)
        {
            var e = row.List[row.Start + k];
            if (k > 0) sb.Append("; ");
            if (e.IsParent) { sb.Append("Parent folder"); continue; }
            sb.Append(e.Name);
            if (e.IsDir) sb.Append(", folder");
            if (marks.Contains(MarkKey(e))) sb.Append(", marked");
        }
        return sb.ToString();
    }

    private Element ThumbRow(char side, ThumbRowItem item, ThumbM m, HashSet<string> marks)
    {
        var row = Flex.Row(gap: m.Gap).Height(m.TileH).AlignItems(AlignItems.FlexStart)
            .Padding(new Thickness(0, 10, 0, 10));
        for (int k = 0; k < item.Count; k++)
        {
            var e = item.List[item.Start + k];
            row.AddChild(Tile(side, e, m, cursor: k == item.Cursor, marks.Contains(MarkKey(e))));
        }
        return row;
    }

    private Element Tile(char side, FileEntry e, ThumbM m, bool cursor, bool marked)
    {
        bool cut = !marked && _cutPending.Contains(e.FullPath);
        Color nameFg = marked ? Nocturne.MarkRed : e.IsDir ? Nocturne.DirText : Nocturne.Text;
        if (cut) nameFg = Nocturne.Alpha(nameFg, 0.45);
        var kind = Icons.KindFor(e);
        Color? tint = marked ? Nocturne.MarkRed : null;

        var tile = Flex.Column(gap: 4).Width(m.TileW).Height(m.TileH).AlignItems(AlignItems.Center)
            .Padding(new Thickness(4)).CornerRadius(Nocturne.RadiusRow)
            .BorderWidth(1f).BorderColor(cursor ? Nocturne.Accent600 : Color.Transparent)
            .Cursor(CursorType.Default).Tooltip(EntryTooltip(side, e));
        if (cursor) tile.Background(Nocturne.Alpha(Nocturne.Accent500, 0.13));

        // Image area on a Neutral900 backing. P2 shows the kind icon (images use a dimmed
        // image glyph as a placeholder); P3 swaps in the decoded thumbnail.
        var area = Box.New().Width(m.ImgW).Height(m.ImgH).FlexShrink(0)
            .CornerRadius(Nocturne.RadiusSm).Background(Nocturne.Neutral900)
            .AlignItems(AlignItems.Center).JustifyContent(JustifyContent.Center);
        if (kind == IconKind.Image && !e.IsDir)
        {
            int target = (int)(Math.Max(m.ImgW, m.ImgH) * 2f);
            var (thumb, tstatus) = Thumbs.Get(e.FullPath, e.Size, e.Modified, target);
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
        else if (!cursor) tile.HoverBg(Nocturne.Alpha(Nocturne.Accent500, 0.06), Color.Transparent);
        AttachEntryPointer(tile, side, e, cursor);
        return tile;
    }

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

        var g = BriefGeometry(side, n);
        _briefRows[Si(side)] = g.Rows;
        _briefColW[Si(side)] = BriefColWidth;
        _viewPage[Si(side)] = g.Rows * Math.Max(1, g.VisCols);
        NoteFit(side, ViewMode.Brief, brief: g.Fit);

        // The cells are kept between builds. A cell shows its entry and whether the cursor is on
        // it, and besides those only what the shape holds — the marks, the files cut for a move,
        // a result set (the tooltips), a folder a drag hovers, the row height, the scale the
        // icons are cut at, the theme. While all of that is unchanged, a cell whose entry and
        // cursor are what it was made with is handed back as it is: the live element, which the
        // reconcile puts back where it was without touching it (it is keyed by its path, so its
        // column matches it by key), as it puts back a persistent child component. Only the
        // cells that changed are made, and patched into the ones they replace: a key in Brief
        // makes two cells instead of every cell on screen. Kept only from the build just before
        // (cells another build dropped are not in the tree any more).
        var cells = BriefCells(side);
        var shape = new BriefCellShape(rowH, Icons.DisplayScale, _app.Theme, ResultsOf(side), _dropTarget);
        bool keep = !BriefCellsState(side).Changed(shape, marks, _cutPending) && cells.FilledBy == _builds - 1;
        cells.Next.Clear();

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
                if (keep && cells.Kept.TryGetValue(e.FullPath, out var kept) && kept.Cursor == cursor
                    && kept.Entry.Equals(e) && kept.Live.Current is { IsDetached: false } live)
                {
                    column.AddChild(live);
                    cells.Next[e.FullPath] = kept;
                    continue;
                }
                var made = new Ref<BoxElement>();
                var cell = BriefCell(side, e, cursor, marks.Contains(MarkKey(e)));
                cell.Key = e.FullPath;
                cell.WithRef(made);   // follows the cell to the live element it is patched into
                column.AddChild(cell);
                cells.Next[e.FullPath] = new BriefKeptCell(e, cursor, made);
            }
            content.AddChild(column);
        }
        (cells.Kept, cells.Next) = (cells.Next, cells.Kept);
        cells.FilledBy = _builds;

        // Explicit width + height so the horizontal ScrollView can't shrink-wrap to content.
        var scroll = ScrollView.Horizontal().WithRef(BriefScroll(side)).FlexShrink(0)
            .Width(Dimension.Px(g.PaneW)).Height(Dimension.Px(g.AvailH));
        scroll.Children(content);
        // Any scroll re-windows which columns realize on the next build.
        scroll.On<ScrollEvent>(_ => MarkDirty());
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
        float scrollX = overflow ? Math.Max(0, BriefScroll(side).Current?.ScrollOffsetX ?? 0) : 0;
        int firstCol = overflow ? Math.Clamp((int)(scrollX / colW), 0, Math.Max(0, totalCols - 1)) : 0;
        int lastCol = Math.Min(totalCols - 1, firstCol + visCols + 1);   // +1 buffer column
        return new BriefGeom(paneW, availH, new BriefFit(rows, totalCols, overflow, visCols, firstCol, lastCol));
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

    /// <summary>A Brief cell as it was made: its entry, whether the cursor was on it, and a ref
    /// to the live element (the cell itself, or the one it was patched into).</summary>
    private readonly record struct BriefKeptCell(FileEntry Entry, bool Cursor, Ref<BoxElement> Live);

    private sealed class BriefCellCache
    {
        /// <summary>The cells the last build that built this pane in Brief made or kept, by path.</summary>
        public Dictionary<string, BriefKeptCell> Kept = new(StringComparer.Ordinal);
        /// <summary>The ones this build has made or kept so far; swapped with <see cref="Kept"/>.</summary>
        public Dictionary<string, BriefKeptCell> Next = new(StringComparer.Ordinal);
        /// <summary>The build that filled <see cref="Kept"/>.</summary>
        public int FilledBy = -1;
    }

    private readonly record struct BriefCellShape(float RowH, float IconScale, object? Theme,
        ResultListing? Results, (char Side, string Dir)? DropTarget);

    private readonly BriefCellCache _briefCellsL = new(), _briefCellsR = new();
    private BriefCellCache BriefCells(char side) => side == 'L' ? _briefCellsL : _briefCellsR;
    private readonly GridState<BriefCellShape> _briefStateL = new(), _briefStateR = new();
    private GridState<BriefCellShape> BriefCellsState(char side) => side == 'L' ? _briefStateL : _briefStateR;

    // Title bar + tab strip + breadcrumb + footer around the Brief list, subtracted from the
    // window height to get the column-fill height. Approximate — a few px off just shifts the
    // wrap point by one cell; kept a touch generous so the list never overflows the footer.
    private const float BriefChromeHeight = 142f;

    private BoxElement BriefCell(char side, FileEntry e, bool cursor, bool marked)
    {
        Color fg = marked ? Nocturne.MarkRed : e.IsDir ? Nocturne.DirText : Nocturne.Text;
        bool cut = !marked && _cutPending.Contains(e.FullPath);
        if (cut) fg = Nocturne.Alpha(fg, 0.45);

        var cell = Flex.Row(gap: 7).Height(RowHeight).AlignItems(AlignItems.Center)
            .Padding(new Thickness(0, 9, 0, 9))
            .BorderWidth(1f).BorderColor(cursor ? Nocturne.Accent600 : Color.Transparent)
            .Cursor(CursorType.Default).Tooltip(EntryTooltip(side, e));
        if (cursor) cell.Background(Nocturne.Alpha(Nocturne.Accent500, 0.13));

        var icon = Icons.Draw(Icons.KindFor(e), marked ? Nocturne.MarkRed : (Color?)null);
        if (cut) icon.Opacity = 0.45f;
        cell.AddChild(icon);
        cell.AddChild(Ui.Mono(e.Name, 11.5f, fg).MaxLines(1).TextOverflow(TextOverflow.Ellipsis).FlexGrow(1));
        AttachEntryPointer(cell, side, e, cursor);
        return cell;
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
            MarkDirty();
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
        _fit[Si(side)] = default;
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
