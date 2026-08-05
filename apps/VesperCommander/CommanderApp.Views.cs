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

    // Per-side geometry cached at Build time so the key handler can navigate without
    // re-measuring: Brief rows-per-column, Thumbnail columns-per-row, and a page step.
    private readonly int[] _briefRows = { 1, 1 };
    private readonly int[] _thumbCols = { 1, 1 };
    private readonly int[] _viewPage = { 1, 1 };
    private readonly float[] _briefColW = { 186f, 186f };
    private readonly Ref<ScrollViewElement> _briefScrollL = new(), _briefScrollR = new();
    private Ref<ScrollViewElement> BriefScroll(char side) => side == 'L' ? _briefScrollL : _briefScrollR;

    private const float ViewToggleWidth = 88f;   // reserved so breadcrumbs truncate first

    private Element BuildViewArea(char side, IReadOnlyList<FileEntry> list, bool active) => ViewOf(side) switch
    {
        ViewMode.Brief => BuildBrief(side, list, active),
        ViewMode.Thumbnails => BuildThumbs(side, list, active),
        _ => BuildList(side, list, active),
    };

    // P1 stub — replaced by the real tile grid in P2. Thumbnails render as Detail for now.
    private Element BuildThumbs(char side, IReadOnlyList<FileEntry> list, bool active) =>
        BuildList(side, list, active);

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
        var b = Box.New().Width(22).Height(15).CornerRadius(3f).Cursor(CursorType.Pointer)
            .AlignItems(AlignItems.Center).JustifyContent(JustifyContent.Center)
            .Tooltip($"{ModeLabel(mode)} · {Keybindings.Format(CurrentChord(ViewCmd(mode), _settings))}");
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
        string? cursorName = active && n > 0 ? list[cur].Name : null;
        var marks = Marks(side);
        float rowH = RowHeight;

        var sv = BriefScroll(side).Current;
        float availH = sv?.Layout.AbsoluteContentBox.Height ?? (_window.LayoutHeight > 0 ? _window.LayoutHeight - 150f : 620f);
        float availW = sv?.Layout.AbsoluteContentBox.Width ?? PaneContentWidthEstimate();
        if (availH < rowH) availH = rowH;

        int rows = Math.Max(1, (int)(availH / rowH));
        int totalCols = Math.Max(1, (n + rows - 1) / rows);
        int maxFit = Math.Max(1, (int)(availW / 186f));
        bool overflow = totalCols > maxFit;
        float colW = overflow ? 186f : availW / totalCols;
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
                column.AddChild(BriefCell(side, e, cursorName, marks.Contains(e.Name)));
            }
            content.AddChild(column);
        }

        var scroll = ScrollView.Horizontal().WithRef(BriefScroll(side)).FlexGrow(1);
        scroll.Children(content);
        // Any scroll re-windows which columns realize on the next build.
        scroll.On<ScrollEvent>(_ => MarkDirty());
        return scroll;
    }

    private Element BriefCell(char side, FileEntry e, string? cursorName, bool marked)
    {
        bool cursor = e.Name == cursorName;
        Color fg = marked ? Nocturne.MarkRed : e.IsDir ? Nocturne.DirText : Nocturne.Text;
        bool cut = !marked && _cutPending.Contains(e.FullPath);
        if (cut) fg = Nocturne.Alpha(fg, 0.45);

        var cell = Flex.Row(gap: 7).Height(RowHeight).AlignItems(AlignItems.Center)
            .Padding(new Thickness(0, 9, 0, 9))
            .BorderWidth(1f).BorderColor(cursor ? Nocturne.Accent600 : Color.Transparent)
            .Cursor(CursorType.Default).Tooltip(e.Name);
        if (cursor) cell.Background(Nocturne.Alpha(Nocturne.Accent500, 0.13));

        var icon = Icons.Draw(Icons.KindFor(e), marked ? Nocturne.MarkRed : (Color?)null);
        if (cut) icon.Opacity = 0.45f;
        cell.AddChild(icon);
        cell.AddChild(Ui.Mono(e.Name, 11.5f, fg).MaxLines(1).TextOverflow(TextOverflow.Ellipsis).FlexGrow(1));
        AttachEntryPointer(cell, side, e);
        return cell;
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
        if (ViewOf(side) != ViewMode.Brief) return false;
        int rows = Math.Max(1, _briefRows[Si(side)]);
        int page = Math.Max(1, _viewPage[Si(side)]);
        switch (key)
        {
            case KeyCode.Up: MoveCursor(side, -1); return true;
            case KeyCode.Down: MoveCursor(side, 1); return true;
            case KeyCode.Left: MoveCursor(side, -rows); return true;      // one column left
            case KeyCode.Right: MoveCursor(side, rows); return true;      // one column right
            case KeyCode.Home: MoveCursor(side, int.MinValue / 2); return true;
            case KeyCode.End: MoveCursor(side, int.MaxValue / 2); return true;
            case KeyCode.PageDown: MoveCursor(side, page); return true;
            case KeyCode.PageUp: MoveCursor(side, -page); return true;
            default: return false;
        }
    }

    // ── Shared entry pointer interaction (rows, brief cells, tiles) ───────
    private void AttachEntryPointer(BoxElement el, char side, FileEntry e)
    {
        el.On<PointerDownEvent>(ev =>
        {
            if (ev.Button == 2) { BeginRightMark(side, e); return; }   // right-drag marking
            if (ev.Button != 0) return;
            _active = side;
            int idx = IndexOfName(Listing(side), e.Name);
            if (idx >= 0) SetCursor(side, idx);
            if (IsDoublePress(side, e.Name)) ActivateCursor(side);
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
