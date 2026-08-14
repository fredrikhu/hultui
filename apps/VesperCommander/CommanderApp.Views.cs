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
    private readonly Ref<VirtualListElement<int>> _thumbVListL = new(), _thumbVListR = new();
    private Ref<VirtualListElement<int>> ThumbVList(char side) => side == 'L' ? _thumbVListL : _thumbVListR;

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
    // VirtualListElement<int> (one entry per grid row) so large folders virtualize for free —
    // only the on-screen rows realize, so only their tiles request thumbnails (P3).
    private Element BuildThumbs(char side, IReadOnlyList<FileEntry> list, bool active)
    {
        var m = ThumbMetrics();
        int n = list.Count;
        int cur = Math.Clamp(Cursor(side), 0, Math.Max(0, n - 1));
        string? cursorPath = active && n > 0 ? list[cur].FullPath : null;
        var marks = Marks(side);

        var tv = ThumbVList(side).Current;
        // Width comes from the pane geometry, not the list's own box: a VirtualList of rigid
        // fixed-width tiles reports its content width (one tile) as its intrinsic size, and the
        // flex stretch resolves the cross axis to that — so reading it back would collapse
        // perRow to 1. We set the list width explicitly instead. Height still reads the box.
        const float pad = 10f;
        float paneW = PaneContentWidthEstimate();
        float availW = paneW - 2 * pad;
        float availH = tv?.Layout.AbsoluteContentBox.Height ?? (_window.LayoutHeight > 0 ? _window.LayoutHeight - 160f : 600f);
        int perRow = ThumbPerRow(availW, m.TileW, m.Gap);
        float stride = m.TileH + m.Gap;
        int visRows = Math.Max(1, (int)(availH / stride));
        _thumbCols[Si(side)] = perRow;
        _viewPage[Si(side)] = perRow * visRows;

        int rowCount = (n + perRow - 1) / perRow;
        var rows = new List<int>(rowCount);
        for (int r = 0; r < rowCount; r++) rows.Add(r);

        return VirtualListElement<int>.New()
            .ItemHeight((int)stride)
            .Items(rows)   // fresh list reference each build → re-realizes visible rows
            .RenderWith(r => ThumbRow(side, list, r * perRow, perRow, m, cursorPath, marks))
            .WithRef(ThumbVList(side))
            .FlexGrow(1)
            .Width(Dimension.Px(paneW));
    }

    private Element ThumbRow(char side, IReadOnlyList<FileEntry> list, int start, int perRow, ThumbM m,
        string? cursorPath, HashSet<string> marks)
    {
        var row = Flex.Row(gap: m.Gap).Height(m.TileH).AlignItems(AlignItems.FlexStart)
            .Padding(new Thickness(0, 10, 0, 10));
        for (int k = 0; k < perRow; k++)
        {
            int i = start + k;
            if (i >= list.Count) break;
            var e = list[i];
            row.AddChild(Tile(side, e, m, cursorPath, marks.Contains(MarkKey(e))));
        }
        return row;
    }

    private Element Tile(char side, FileEntry e, ThumbM m, string? cursorPath, bool marked)
    {
        bool cursor = cursorPath != null && PathEquals(e.FullPath, cursorPath);
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
            .Background(Nocturne.Bg).BorderColor(Nocturne.Neutral800).BorderWidth(1f);
        box.AddChild(ViewToggleBtn(side, ViewMode.Detail, IconKind.ViewDetail, cur));
        box.AddChild(ViewToggleBtn(side, ViewMode.Brief, IconKind.ViewBrief, cur));
        box.AddChild(ViewToggleBtn(side, ViewMode.Thumbnails, IconKind.ViewThumb, cur));
        return box;
    }

    private Element ViewToggleBtn(char side, ViewMode mode, IconKind glyph, ViewMode cur)
    {
        bool on = cur == mode;
        var bind = CurrentChord(ViewCmd(mode), _settings);
        var b = Box.New().Width(22).Height(15).CornerRadius(3f).Cursor(CursorType.Pointer)
            .AlignItems(AlignItems.Center).JustifyContent(JustifyContent.Center)
            .Tooltip(bind is { } c ? $"{ModeLabel(mode)} · {Keybindings.Format(c)}" : ModeLabel(mode));
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

        // Both dimensions come from the window geometry, never from the ScrollView's own box:
        // a horizontal ScrollView shrink-wraps its height to its content, so reading its height
        // back would collapse rowsPerCol to 1 (items would spread across single-item columns);
        // reading the pane height back is equally circular (it reflects the collapsed state).
        // The window height minus the fixed chrome (title bar + tab strip + breadcrumb + footer)
        // is a stable input that can't feed the loop.
        var sv = BriefScroll(side).Current;
        float availH = Math.Max(rowH, (_window.LayoutHeight > 0 ? _window.LayoutHeight : 820f) - BriefChromeHeight);
        float paneW = PaneContentWidthEstimate();
        float availW = paneW;

        var (rows, totalCols, colW, overflow) = BriefGrid(n, availW, availH, rowH);
        int visCols = overflow ? Math.Max(1, (int)(availW / colW)) : totalCols;

        _briefRows[Si(side)] = rows;
        _briefColW[Si(side)] = colW;
        _viewPage[Si(side)] = rows * Math.Max(1, visCols);

        float scrollX = overflow ? Math.Max(0, sv?.ScrollOffsetX ?? 0) : 0;
        int firstCol = overflow ? Math.Clamp((int)(scrollX / colW), 0, Math.Max(0, totalCols - 1)) : 0;
        int lastCol = Math.Min(totalCols - 1, firstCol + visCols + 1);   // +1 buffer column

        var content = Flex.Row(gap: 0).FlexShrink(0);
        if (overflow) content.Width(totalCols * colW); else content.FlexGrow(1);
        if (firstCol > 0) content.AddChild(Box.New().Width(firstCol * colW).FlexShrink(0));

        for (int c = firstCol; c <= lastCol; c++)
        {
            var column = Flex.Column(gap: 0).Width(colW).FlexShrink(0);
            // 1px rule left of every column but the first (columns are full except the last).
            if (c > 0) column.BorderColor(Nocturne.Alpha(Nocturne.Text, 0.10)).BorderWidth(new Thickness(1, 0, 0, 0));
            for (int r = 0; r < rows; r++)
            {
                int i = c * rows + r;
                if (i >= n) break;
                var e = list[i];
                column.AddChild(BriefCell(side, e, cursorPath, marks.Contains(MarkKey(e))));
            }
            content.AddChild(column);
        }

        // Explicit width + height so the horizontal ScrollView can't shrink-wrap to content.
        var scroll = ScrollView.Horizontal().WithRef(BriefScroll(side)).FlexShrink(0)
            .Width(Dimension.Px(paneW)).Height(Dimension.Px(availH));
        scroll.Children(content);
        // Any scroll re-windows which columns realize on the next build.
        scroll.On<ScrollEvent>(_ => MarkDirty());
        return scroll;
    }

    // Title bar + tab strip + breadcrumb + footer around the Brief list, subtracted from the
    // window height to get the column-fill height. Approximate — a few px off just shifts the
    // wrap point by one cell; kept a touch generous so the list never overflows the footer.
    private const float BriefChromeHeight = 142f;

    private Element BriefCell(char side, FileEntry e, string? cursorPath, bool marked)
    {
        bool cursor = cursorPath != null && PathEquals(e.FullPath, cursorPath);
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
                ArmContextHold(side, ev.Position.X, ev.Position.Y);
                return;
            }
            if (ev.Button != 0) return;
            _active = side;
            int idx = IndexOfPath(Rows(side), e.FullPath);
            if (idx >= 0) SetCursor(side, idx);
            if (IsDoublePress(side, e.FullPath)) ActivateCursor(side);
            MarkDirty();
        });
        el.On<PointerMoveEvent>(_ => { if (_rmarkActive) ApplyRightMark(side, e); });
    }

    private float PaneContentWidthEstimate()
    {
        float win = _window.LayoutWidth > 0 ? _window.LayoutWidth : 1240f;
        float paneW = _settings.Preview == PreviewMode.Normal
            ? (win - RailWidth - PreviewWidth - 2f) / 2f
            : (win - RailWidth - 1f) / 2f;
        return paneW - 4f;
    }
}
