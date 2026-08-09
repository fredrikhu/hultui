using HultUI.Core.Builders;
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
    // ── Diff view (F3) — full-shell builtin ──────────────────────────────
    private enum DiffViewState { Ok, Identical, Binary, Missing }

    private bool _diffOpen;
    private char _diffSide;
    private string _diffLeftPath = "", _diffRightPath = "";
    private DiffViewState _diffState;
    private DiffEngine.Result? _diffResult;
    private List<DiffRow> _diffRows = new();
    private List<int> _diffChangeRows = new();
    private int _diffChangeIdx;
    private bool _pendingDiffScroll;
    private readonly Ref<ScrollViewElement> _diffScrollRef = new();

    // Horizontal position, shared by both sides so they stay aligned, and applied as a
    // negative left margin on the code text inside an overflow-hidden cell rather than by
    // a real horizontal ScrollView. A nested ScrollView would swallow the wheel (its
    // OnScroll always stops propagation), which would cost the outer vertical scroll its
    // wheel; and scrolling the *outer* view instead would drag the line-number gutters off
    // screen and push the right-hand side out of view entirely.
    private float _diffScrollX;
    // Width of the longest line in the comparison, measured once per comparison. Zero when
    // no measure context is installed yet, which just means no horizontal scrolling.
    private float _diffLongestPx;

    // Selection scopes, one per side. The tokens only need identity, and they live for the
    // app's lifetime so a rebuild mid-drag doesn't break the group the window is tracking.
    private readonly object _diffGroupLeft = new();
    private readonly object _diffGroupRight = new();

    // Bumped per comparison, so a syntax tokenize that finishes after the user has moved on
    // can tell that it has.
    private int _diffGen;

    private sealed record DiffRow(DiffEngine.Line? Line, int? CollapseCount);

    /// <summary>F3 — compare the cursor file in <paramref name="side"/> with its partner in
    /// the other pane. Honors the DiffTool setting (VS Code launches externally;
    /// Builtin/Junction use this view).</summary>
    private void OpenDiff(char side)
    {
        if (AnyOverlayOpen) return;
        var list = Rows(side);
        int cur = Cursor(side);
        if (cur < 0 || cur >= list.Count || list[cur].IsParent || list[cur].IsDir) return;
        var e = list[cur];
        char other = side == 'L' ? 'R' : 'L';

        _diffLeftPath = e.FullPath;
        _diffRightPath = DiffPartner(other, e);
        _diffSide = side;

        if (_settings.DiffTool == DiffToolKind.VSCode && File.Exists(_diffRightPath))
        {
            FileOps.LaunchDiff("code", _diffLeftPath, _diffRightPath);
            return;
        }

        ComputeDiff();
        _diffOpen = true;
        MarkDirty();
    }

    /// <summary>The file F3 compares <paramref name="e"/> against. The same-named file in the
    /// other pane's folder wins — two copies of one file is the common case — and otherwise
    /// the two cursors pair up, which is what lets F3 compare unrelated names across two
    /// folders instead of reporting the other side missing. A same-named hit that resolves
    /// back to the file we started from is no comparison at all, so it defers to the cursor
    /// too. With nothing to pair against, the same-named path is returned anyway, so the
    /// missing-file report names the path the user expected.</summary>
    private string DiffPartner(char other, FileEntry e)
    {
        var sameName = System.IO.Path.Combine(Dir(other), e.Name);
        if (File.Exists(sameName) && !PathEquals(sameName, e.FullPath)) return sameName;

        var list = Rows(other);
        int cur = Cursor(other);
        if (cur >= 0 && cur < list.Count && list[cur] is { IsParent: false, IsDir: false } partner
            && !PathEquals(partner.FullPath, e.FullPath))
            return partner.FullPath;

        return sameName;
    }

    private void CloseDiff()
    {
        _diffOpen = false;
        _diffResult = null;
        // The panes don't exist yet — this build still shows the diff. Ask for the focus
        // once they're back; PaneRef still holds the element from before the view opened.
        _pendingPaneFocus = _diffSide;
        MarkDirty();
    }

    private void ComputeDiff()
    {
        _diffResult = null;
        _diffRows = new();
        _diffChangeRows = new();
        _diffChangeIdx = 0;
        _diffScrollX = 0;
        _diffLongestPx = 0;

        if (!File.Exists(_diffLeftPath) || !File.Exists(_diffRightPath)) { _diffState = DiffViewState.Missing; return; }
        var (la, lbin) = LoadForDiff(_diffLeftPath);
        var (ra, rbin) = LoadForDiff(_diffRightPath);
        if (lbin || rbin || la is null || ra is null) { _diffState = DiffViewState.Binary; return; }
        if (DiffEngine.TooLarge(la, ra)) { _diffState = DiffViewState.Binary; return; }

        _diffResult = DiffEngine.Diff(la, ra);
        _diffState = _diffResult.Identical ? DiffViewState.Identical : DiffViewState.Ok;
        if (_diffState == DiffViewState.Ok)
        {
            BuildDiffRowModel();
            _diffLongestPx = MeasureLongestLine(la, ra);
            _diffGen++;
            StartDiffHighlight(la, ra);
        }
    }

    /// <summary>How wide the widest line renders, which is how far right the view can
    /// scroll. The font is monospace, so the line with the most characters is the widest
    /// one and a single measurement settles it — measuring every line of a 4 MB file would
    /// not. A proportional fallback font would make this an estimate, and the only cost of
    /// being wrong is scrolling slightly too far or not quite far enough.</summary>
    private static float MeasureLongestLine(string[] left, string[] right)
    {
        var longest = "";
        foreach (var line in left) if (line.Length > longest.Length) longest = line;
        foreach (var line in right) if (line.Length > longest.Length) longest = line;
        return TextMeasure.MeasureWidth(longest, DiffFontSize, Fonts.Mono);
    }

    private static (string[]? lines, bool binary) LoadForDiff(string path)
    {
        try
        {
            var fi = new FileInfo(path);
            if (fi.Length > 4L * 1024 * 1024) return (null, true);
            byte[] bytes = File.ReadAllBytes(path);
            int scan = Math.Min(bytes.Length, 8192);
            for (int i = 0; i < scan; i++) if (bytes[i] == 0) return (null, true);
            return (System.Text.Encoding.UTF8.GetString(bytes).Replace("\r\n", "\n").Split('\n'), false);
        }
        catch { return (null, true); }
    }

    /// <summary>Flatten the diff into rendered rows, collapsing long unchanged runs to
    /// 3 lines of context each side of a "⋯ N ⋯" marker, and record the first row of each
    /// change block for n/p navigation.</summary>
    private void BuildDiffRowModel()
    {
        var lines = _diffResult!.Lines;
        var rows = new List<DiffRow>();
        const int ctx = 3, thresh = 8;
        int i = 0;
        while (i < lines.Count)
        {
            if (lines[i].Op == DiffEngine.Op.Equal)
            {
                int start = i;
                while (i < lines.Count && lines[i].Op == DiffEngine.Op.Equal) i++;
                int run = i - start;
                if (run > thresh)
                {
                    for (int k = 0; k < ctx; k++) rows.Add(new DiffRow(lines[start + k], null));
                    rows.Add(new DiffRow(null, run - 2 * ctx));
                    for (int k = run - ctx; k < run; k++) rows.Add(new DiffRow(lines[start + k], null));
                }
                else for (int k = 0; k < run; k++) rows.Add(new DiffRow(lines[start + k], null));
            }
            else rows.Add(new DiffRow(lines[i++], null));
        }

        var changeRows = new List<int>();
        for (int r = 0; r < rows.Count; r++)
        {
            bool changed = rows[r].Line is { Op: not DiffEngine.Op.Equal };
            bool prevChanged = r > 0 && rows[r - 1].Line is { Op: not DiffEngine.Op.Equal };
            if (changed && !prevChanged) changeRows.Add(r);
        }
        _diffRows = rows;
        _diffChangeRows = changeRows;
    }

    private void DiffNav(int dir)
    {
        if (_diffChangeRows.Count == 0) return;
        _diffChangeIdx = Math.Clamp(_diffChangeIdx + dir, 0, _diffChangeRows.Count - 1);
        _pendingDiffScroll = true;
        MarkDirty();
    }

    /// <summary>Scroll the diff body by <paramref name="delta"/> px (or to <paramref
    /// name="to"/> when given). The offset survives reconciliation — ScrollViewElement
    /// deliberately doesn't copy it from the incoming build — so it can be set directly,
    /// and the setter clamps to the content and repaints. Only the Ok state builds a
    /// scroll body; without that guard the Ref would still point at the detached
    /// ScrollView left over from a previous comparison.</summary>
    private void DiffScroll(float delta, float? to = null)
    {
        if (_diffState != DiffViewState.Ok || _diffScrollRef.Current is not { } sv) return;
        sv.ScrollOffsetY = to ?? sv.ScrollOffsetY + delta;
    }

    /// <summary>A page is the viewport less one line, so the line you were reading stays
    /// on screen across a PgUp/PgDn.</summary>
    private float DiffPage() => _diffScrollRef.Current is { } sv
        ? Math.Max(DiffLineH, sv.Layout.AbsoluteContentBox.Height - DiffLineH)
        : DiffLineH;

    /// <summary>Width of one code column — the two split whatever the fixed tracks leave
    /// over. Measured from the laid-out body when there is one, and from the window before
    /// that: the first Build after opening a comparison runs before any layout, and it still
    /// has to decide whether there is a scrollbar to show.</summary>
    private float DiffCodeWidth()
    {
        float body = _diffScrollRef.Current is { Layout.AbsoluteContentBox.Width: > 0 } sv
            ? sv.Layout.AbsoluteContentBox.Width
            : _window.LayoutWidth;
        return Math.Max(0, (body - DiffGutterW * 2 - DiffMarkerW) / 2);
    }

    /// <summary>How far right the view can scroll: the longest line, less what one code
    /// column shows of it.</summary>
    private float DiffMaxScrollX() => Math.Max(0, _diffLongestPx - DiffCodeWidth());

    /// <summary>Move the view sideways. Unlike the vertical offset this is app state, not
    /// something the ScrollView holds, so it needs a rebuild to take effect.</summary>
    private void DiffScrollH(float delta, float? to = null)
    {
        if (_diffState != DiffViewState.Ok) return;
        float next = Math.Clamp(to ?? _diffScrollX + delta, 0, DiffMaxScrollX());
        if (next == _diffScrollX) return;
        _diffScrollX = next;
        MarkDirty();
    }

    /// <summary>One press of ← / →. Roughly four monospace characters — small enough to
    /// line something up, large enough to cross a long line without wearing out the key.</summary>
    private const float DiffHStep = 28f;

    private void HandleDiffKey(KeyDownEvent e)
    {
        bool ctrl = e.Modifiers.HasFlag(KeyModifiers.Ctrl) || e.Modifiers.HasFlag(KeyModifiers.Super);
        switch (e.Key)
        {
            case KeyCode.Escape:
            case KeyCode.F3: CloseDiff(); e.StopPropagation = true; break;
            case KeyCode.N: DiffNav(1); e.StopPropagation = true; break;
            case KeyCode.P: DiffNav(-1); e.StopPropagation = true; break;
            // The body is read-only, so the arrows scroll it rather than moving a cursor;
            // n / p remain the way to jump between changes.
            case KeyCode.Up: DiffScroll(-DiffLineH); e.StopPropagation = true; break;
            case KeyCode.Down: DiffScroll(DiffLineH); e.StopPropagation = true; break;
            case KeyCode.PageUp: DiffScroll(-DiffPage()); e.StopPropagation = true; break;
            case KeyCode.PageDown: DiffScroll(DiffPage()); e.StopPropagation = true; break;
            case KeyCode.Home: DiffScroll(0, to: 0); e.StopPropagation = true; break;
            case KeyCode.End: DiffScroll(0, to: float.MaxValue); e.StopPropagation = true; break;
            // Sideways, for lines wider than the pane. Ctrl jumps to either margin.
            case KeyCode.Left when ctrl: DiffScrollH(0, to: 0); e.StopPropagation = true; break;
            case KeyCode.Right when ctrl: DiffScrollH(0, to: float.MaxValue); e.StopPropagation = true; break;
            case KeyCode.Left: DiffScrollH(-DiffHStep); e.StopPropagation = true; break;
            case KeyCode.Right: DiffScrollH(DiffHStep); e.StopPropagation = true; break;
            case KeyCode.F5:
                if (_diffState == DiffViewState.Missing) { CloseDiff(); StartCopy(); }
                e.StopPropagation = true; break;
        }
    }

    // ── Diff rendering ───────────────────────────────────────────────────
    private const float DiffLineH = 20f;
    private const float DiffFontSize = 11.5f;
    // The fixed tracks of a line row. Kept as constants because the horizontal extent has
    // to work out how much width is left for the two code columns.
    private const float DiffGutterW = 34f;
    private const float DiffMarkerW = 18f;

    private Element BuildDiff()
    {
        var col = Flex.Column(gap: 0).FlexGrow(1).Background(Nocturne.Bg);
        col.AddChild(BuildDiffHeader());
        col.AddChild(BuildDiffSubHeader());
        col.AddChild(_diffState switch
        {
            DiffViewState.Ok => BuildDiffBody(),
            DiffViewState.Identical => DiffMessage(Glyphs.Check, "Files are identical",
                $"{(_diffResult?.Lines.Count ?? 0)} lines · same content"),
            DiffViewState.Binary => DiffMessage(Glyphs.BinaryFile, "Can't show a text diff",
                "One side is binary or larger than 4 MB."),
            _ => DiffMissingBody(),
        });
        if (_diffState == DiffViewState.Ok && DiffMaxScrollX() > 0) col.AddChild(BuildDiffHScrollbar());
        col.AddChild(BuildDiffFooter());
        return col;
    }

    private Element BuildDiffHeader()
    {
        var h = Flex.Row(gap: 10).FlexShrink(0).AlignItems(AlignItems.Center)
            .Padding(new Thickness(8, 14, 8, 14))
            .Background(Nocturne.Surface)
            .BorderColor(Nocturne.Divider).BorderWidth(new Thickness(0, 0, 1, 0));

        var back = Box.New().Cursor(CursorType.Pointer).Padding(new Thickness(2, 8, 2, 8)).CornerRadius(5f)
            .Children(Ui.Icon(Glyphs.Back, 16f, Nocturne.Neutral300)).Tooltip("Back · Esc");
        back.OnClick(_ => CloseDiff());
        h.AddChild(back.HoverBg(Nocturne.Neutral900, Color.Transparent));

        h.AddChild(Ui.Icon(Glyphs.Swap, 13f, Nocturne.Accent));
        h.AddChild(Text.New(System.IO.Path.GetFileName(_diffLeftPath)).FontSize(13f)
            .FontWeight(FontWeight.Medium).TextColor(Nocturne.Text));

        if (_diffResult is { } r && _diffState == DiffViewState.Ok)
        {
            h.AddChild(DiffPill($"+{r.Adds}", Nocturne.DiffGreen));
            h.AddChild(DiffPill($"−{r.Dels}", Nocturne.MarkRed));
            h.AddChild(DiffPill($"~{r.Changes}", Nocturne.Accent300));
        }
        h.AddChild(Box.New().FlexGrow(1));

        if (_diffChangeRows.Count > 0)
        {
            h.AddChild(Ui.Mono($"change {_diffChangeIdx + 1} / {_diffChangeRows.Count}", 10.5f, Nocturne.On(0.50)));
            h.AddChild(DiffNavBtn(Glyphs.ArrowUp, "p", () => DiffNav(-1)));
            h.AddChild(DiffNavBtn(Glyphs.ArrowDown, "n", () => DiffNav(1)));
        }
        return h;
    }

    private Element DiffPill(string text, Color c) =>
        Box.New().Padding(new Thickness(1, 7, 2, 7)).CornerRadius(9f)
            .Background(Nocturne.Alpha(c, 0.16))
            .Children(Ui.Mono(text, 10.5f, c));

    private Element DiffNavBtn(string icon, string tip, Action onClick)
    {
        var b = Box.New().Cursor(CursorType.Pointer).Padding(new Thickness(2, 7, 2, 7)).CornerRadius(4f)
            .BorderColor(Nocturne.Neutral700).BorderWidth(1f)
            .Children(Ui.Icon(icon, 12f, Nocturne.Neutral300)).Tooltip($"{tip}");
        b.OnClick(_ => onClick());
        return b.HoverBg(Nocturne.Neutral900, Color.Transparent);
    }

    private Element BuildDiffSubHeader()
    {
        var row = Grid.Of("1fr 1fr", gap: 0).FlexShrink(0)
            .BorderColor(Nocturne.Divider).BorderWidth(new Thickness(0, 0, 1, 0));
        row.AddChild(DiffPathCell("LEFT", _diffLeftPath).GridColumn(0));
        row.AddChild(DiffPathCell("RIGHT", _diffRightPath).GridColumn(1));
        return row;
    }

    private Element DiffPathCell(string tag, string path)
    {
        var cell = Flex.Row(gap: 8).AlignItems(AlignItems.Center).Padding(new Thickness(5, 12, 5, 12));
        cell.AddChild(Ui.Mono(tag, 9.5f, Nocturne.On(0.40)));
        cell.AddChild(Ui.Mono(path, 11f, Nocturne.On(0.60)).MaxLines(1).TextOverflow(TextOverflow.Ellipsis).FlexGrow(1));
        if (File.Exists(path))
        {
            try
            {
                var fi = new FileInfo(path);
                // Fixed size, never wrapped: the path beside it grows and ellipsises, so it
                // is the one that should give way. Without this the timestamp folded onto a
                // second line and spilled over the first row of the diff.
                cell.AddChild(Ui.Mono($"{FileSystem.ByteCount(fi.Length)} · {fi.LastWriteTime:yyyy-MM-dd HH:mm}",
                    10f, Nocturne.On(0.40)).MaxLines(1).FlexShrink(0));
            }
            catch { }
        }
        return cell;
    }

    private Element BuildDiffBody()
    {
        var scroll = ScrollView.Vertical().FlexGrow(1).WithRef(_diffScrollRef);
        // A trackpad's sideways swipe arrives as DeltaX; a mouse reports Shift+wheel as
        // DeltaY. The ScrollView's own handler sees both first, but its content is exactly
        // as wide as its viewport, so it clamps them to nothing and leaves them to us.
        scroll.On<ScrollEvent>(ev =>
        {
            float dx = ev.DeltaX != 0 ? ev.DeltaX
                : ev.Modifiers.HasFlag(KeyModifiers.Shift) ? ev.DeltaY : 0;
            if (dx != 0) DiffScrollH(-dx * DiffHStep);
        });
        var col = Flex.Column(gap: 0);
        for (int r = 0; r < _diffRows.Count; r++)
        {
            bool current = _diffChangeRows.Count > 0 && _diffChangeIdx < _diffChangeRows.Count
                && r == _diffChangeRows[_diffChangeIdx];
            col.AddChild(_diffRows[r].CollapseCount is int n
                ? DiffCollapseRow(n)
                : DiffLineRow(_diffRows[r].Line!, current));
        }
        scroll.AddChild(col);
        return scroll;
    }

    private Element DiffCollapseRow(int n) =>
        Box.New().Height(DiffLineH).AlignItems(AlignItems.Center).JustifyContent(JustifyContent.Center)
            .Background(Nocturne.Alpha(Nocturne.Text, 0.02))
            .BorderColor(Nocturne.Divider).BorderWidth(new Thickness(1, 0, 1, 0))
            .Children(Ui.Mono($"⋯ {n} unchanged line{(n == 1 ? "" : "s")} ⋯", 10f, Nocturne.On(0.35)));

    private Element DiffLineRow(DiffEngine.Line line, bool current)
    {
        var (leftBg, rightBg, marker, markColor) = line.Op switch
        {
            DiffEngine.Op.Change => (Nocturne.DiffChangeBg, Nocturne.DiffChangeBg, "~", Nocturne.Accent300),
            DiffEngine.Op.Del => (Nocturne.DiffDelBg, HatchBg, "−", Nocturne.MarkRed),
            DiffEngine.Op.Add => (HatchBg, Nocturne.DiffAddBg, "+", Nocturne.DiffGreen),
            _ => (Color.Transparent, Color.Transparent, " ", Color.Transparent),
        };

        var row = Grid.Of($"{DiffGutterW}px 1fr {DiffMarkerW}px {DiffGutterW}px 1fr", gap: 0)
            .Height(DiffLineH).AlignItems(AlignItems.Stretch);
        if (current) row.BorderColor(Nocturne.Accent600).BorderWidth(1f);

        // On a changed line, mark only the differing middle — the common prefix/suffix stay
        // plain, so the eye lands on what actually changed.
        (int start, int len)? hiL = null, hiR = null;
        if (line.Op is DiffEngine.Op.Change && line.Left != null && line.Right != null)
        {
            var (pre, suf) = DiffEngine.IntraSpan(line.Left, line.Right);
            hiL = (pre, Math.Max(0, line.Left.Length - suf - pre));
            hiR = (pre, Math.Max(0, line.Right.Length - suf - pre));
        }

        var spansL = MergeDiffSpans(SyntaxFor(leftSide: true, line.LeftNo), hiL, line.Left?.Length ?? 0);
        var spansR = MergeDiffSpans(SyntaxFor(leftSide: false, line.RightNo), hiR, line.Right?.Length ?? 0);

        row.AddChild(DiffGutter(line.LeftNo).GridColumn(0));
        row.AddChild(DiffTextCell(line.Left, leftBg, line.Op is DiffEngine.Op.Del ? Nocturne.MarkRed : Nocturne.On(0.78), _diffGroupLeft, spansL).GridColumn(1));
        row.AddChild(Box.New().AlignItems(AlignItems.Center).JustifyContent(JustifyContent.Center)
            .Children(Ui.Mono(marker, 11f, markColor)).GridColumn(2));
        row.AddChild(DiffGutter(line.RightNo).GridColumn(3));
        row.AddChild(DiffTextCell(line.Right, rightBg, line.Op is DiffEngine.Op.Add ? Nocturne.DiffGreenText : Nocturne.On(0.78), _diffGroupRight, spansR).GridColumn(4));
        return row;
    }

    private static Color HatchBg => Nocturne.Alpha(Nocturne.Text, 0.02);

    // A Box lays out as a *column*, so JustifyContent is the vertical axis and AlignItems
    // the horizontal one — the opposite of the row-shaped cells everywhere else in this
    // file. Both cells below want "vertically centred, horizontally not centred", which is
    // JustifyContent.Center; using AlignItems.Center for it centred the text instead.
    private Element DiffGutter(int no) =>
        Box.New().AlignItems(AlignItems.FlexEnd).JustifyContent(JustifyContent.Center)
            .Padding(new Thickness(0, 6, 0, 0))
            .Children(Ui.Mono(no > 0 ? no.ToString() : "", 10.5f, Nocturne.Neutral700));

    private Element DiffTextCell(string? text, Color bg, Color fg, object selectionGroup,
        IReadOnlyList<TextSpan>? spans = null)
    {
        // AlignItems is left at its Stretch default so the line fills the cell and clips at
        // the cell's own edge rather than at the width of the text. OverflowX.Hidden is what
        // makes the horizontal offset below safe: without it the shifted text would spill
        // left over the line-number gutter.
        var box = Box.New().Background(bg).JustifyContent(JustifyContent.Center)
            .Padding(new Thickness(0, 8, 0, 8)).OverflowX(Overflow.Hidden);

        var mono = Ui.Mono(text ?? "", DiffFontSize, fg).MaxLines(1).TextOverflow(TextOverflow.Clip);
        if (spans is { Count: > 0 }) mono.Spans(spans);

        // Horizontal scroll. A negative left margin moves the line left and — because the
        // stretch that sizes it subtracts the horizontal margin — widens it by the same
        // amount, so the text that scrolls in from the right is actually laid out.
        if (_diffScrollX > 0) mono.Margin(new Thickness(0, 0, 0, -_diffScrollX));

        // Selectable, with one group per side: a drag runs down the lines of one file
        // rather than zig-zagging between the two. The window owns the group drag and the
        // Ctrl+C that copies it.
        mono.Selectable().SelectionGroup(selectionGroup);

        box.AddChild(mono);
        return box;
    }

    private Element DiffMessage(string icon, string title, string sub)
    {
        var box = Flex.Column(gap: 8).FlexGrow(1).AlignItems(AlignItems.Center).JustifyContent(JustifyContent.Center);
        box.AddChild(Ui.Icon(icon, 26f, Nocturne.Accent300));
        box.AddChild(Text.New(title).FontSize(13f).TextColor(Nocturne.Text));
        box.AddChild(Ui.Mono(sub, 11f, Nocturne.On(0.45)));
        return box;
    }

    private Element DiffMissingBody()
    {
        bool rightMissing = !File.Exists(_diffRightPath);
        var box = Flex.Column(gap: 10).FlexGrow(1).AlignItems(AlignItems.Center).JustifyContent(JustifyContent.Center);
        box.AddChild(Ui.Icon(Glyphs.Prohibit, 24f, Nocturne.MarkRed));
        box.AddChild(Text.New(rightMissing ? "Right file missing" : "Left file missing")
            .FontSize(13f).TextColor(Nocturne.Text));
        box.AddChild(Ui.Mono((rightMissing ? _diffRightPath : _diffLeftPath) + " — not found", 10.5f, Nocturne.On(0.45)));
        if (rightMissing)
        {
            var btn = Box.New().Cursor(CursorType.Pointer).Padding(new Thickness(5, 12, 5, 12))
                .CornerRadius(Nocturne.RadiusSm).BorderColor(Nocturne.Accent700).BorderWidth(1f);
            var lbl = Flex.Row(gap: 6).AlignItems(AlignItems.Center);
            lbl.AddChild(Text.New("Copy left → right").FontSize(11.5f).TextColor(Nocturne.Accent200));
            lbl.AddChild(Ui.Kbd("F5"));
            btn.AddChild(lbl);
            btn.OnClick(_ => { CloseDiff(); StartCopy(); });
            box.AddChild(btn);
        }
        return box;
    }

    // ── Horizontal scrollbar ─────────────────────────────────────────────
    // Hand-built, because the horizontal offset is app state rather than a ScrollView's
    // (see _diffScrollX). It spans the whole body: both code columns move together, so one
    // bar describes both.
    private const float DiffBarH = 10f;
    private const float DiffThumbMin = 30f;

    private readonly Ref<BoxElement> _diffHTrackRef = new();
    private bool _diffHDragging;
    private float _diffHGrab;   // where in the thumb the drag took hold

    /// <summary>Track geometry, from the laid-out track when there is one. Build and drag
    /// both go through this so the thumb lands where the pointer left it.</summary>
    private (float x, float width, float thumb) DiffBarGeometry()
    {
        var box = _diffHTrackRef.Current?.Layout.AbsoluteContentBox;
        float x = box?.X ?? 0;
        float width = box is { Width: > 0 } ? box.Value.Width : _window.LayoutWidth;
        float code = DiffCodeWidth();
        float content = code + DiffMaxScrollX();
        float thumb = content > 0 ? Math.Clamp(width * (code / content), DiffThumbMin, width) : width;
        return (x, width, thumb);
    }

    private Element BuildDiffHScrollbar()
    {
        var (_, width, thumb) = DiffBarGeometry();
        float max = DiffMaxScrollX();
        float offset = max > 0 ? (width - thumb) * (_diffScrollX / max) : 0;

        var track = Box.New().Height(DiffBarH).FlexShrink(0)
            .Background(Nocturne.Surface)
            .BorderColor(Nocturne.Divider).BorderWidth(new Thickness(1, 0, 0, 0))
            .WithRef(_diffHTrackRef);

        track.AddChild(Box.New()
            .Height(DiffBarH - 4).Width(thumb).Margin(new Thickness(1, 0, 0, offset))
            .CornerRadius(3f).Background(Nocturne.Neutral700).Cursor(CursorType.Pointer));

        // Press anywhere on the track: on the thumb it grabs, off it the thumb jumps under
        // the pointer first — the same thing either way once the grab offset is worked out.
        track.On<PointerDownEvent>(ev =>
        {
            var (tx, w, th) = DiffBarGeometry();
            float local = ev.Position.X - tx;
            float thumbAt = max > 0 ? (w - th) * (_diffScrollX / max) : 0;
            _diffHGrab = local >= thumbAt && local <= thumbAt + th ? local - thumbAt : th / 2;
            _diffHDragging = true;
            DragDiffBarTo(ev.Position.X);
            ev.StopPropagation = true;
        });
        return track;
    }

    /// <summary>Map a pointer x onto the horizontal offset, for a scrollbar drag.</summary>
    private void DragDiffBarTo(float pointerX)
    {
        var (x, width, thumb) = DiffBarGeometry();
        float travel = width - thumb;
        if (travel <= 0) return;
        float thumbAt = Math.Clamp(pointerX - x - _diffHGrab, 0, travel);
        DiffScrollH(0, to: thumbAt / travel * DiffMaxScrollX());
    }

    private Element BuildDiffFooter()
    {
        var footer = Flex.Row(gap: 8).FlexShrink(0).AlignItems(AlignItems.Center)
            .Padding(new Thickness(5, 12, 5, 12))
            .Background(Nocturne.Surface)
            .BorderColor(Nocturne.Divider).BorderWidth(new Thickness(1, 0, 0, 0));
        footer.AddChild(Ui.Mono("read-only", 10f, Nocturne.On(0.40)));
        footer.AddChild(Box.New().FlexGrow(1));
        if (_diffState == DiffViewState.Ok)
        {
            footer.AddChild(Ui.Kbd("↑↓"));
            if (DiffMaxScrollX() > 0) footer.AddChild(Ui.Kbd("←→"));
            footer.AddChild(Ui.Mono("scroll", 10f, Nocturne.On(0.45)));
        }
        if (_diffChangeRows.Count > 0)
        {
            footer.AddChild(Ui.Kbd("n")); footer.AddChild(Ui.Kbd("p"));
            footer.AddChild(Ui.Mono("change", 10f, Nocturne.On(0.45)));
        }
        footer.AddChild(Ui.Kbd("Esc")); footer.AddChild(Ui.Mono("back", 10f, Nocturne.On(0.45)));
        return footer;
    }
}
