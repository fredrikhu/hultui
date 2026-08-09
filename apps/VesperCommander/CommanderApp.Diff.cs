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
    // The row holding the change n/p last landed on, so its position can be read rather
    // than computed when rows are not all the same height.
    private readonly Ref<Element> _diffChangeRowRef = new();

    // Sideways position, shared by both sides so they stay aligned. See HorizontalPan for
    // why this is not a horizontal ScrollView: a nested one would swallow the wheel the
    // outer vertical scroll needs, and scrolling the outer view would drag the line-number
    // gutters off screen and push the right-hand side out of view entirely.
    private readonly HorizontalPan _diffPan;

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
        _diffPan.Reset();
        _diffPan.ContentWidth = 0;

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
            _diffPan.ContentWidth = HorizontalPan.MeasureWidest(la.Concat(ra), DiffFontSize, Fonts.Mono);
            _diffGen++;
            StartDiffHighlight(la, ra);
        }
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
    /// column shows of it. Nowhere, when lines wrap instead.</summary>
    /// <summary>Tell the pan how much of a line one code column shows. Wrapped, nothing
    /// scrolls sideways, so the two modes cannot both be on.</summary>
    private void UpdateDiffPan() => _diffPan.ViewportWidth = DiffWrap ? float.MaxValue : DiffCodeWidth();

    /// <summary>Whether a line too wide for its column wraps rather than scrolling sideways.
    /// The two are alternatives: wrapping means every line is visible in full, so there is
    /// nothing to scroll to.</summary>
    private bool DiffWrap => _settings.DiffWrap;

    private void ToggleDiffWrap()
    {
        _settings.DiffWrap = !_settings.DiffWrap;
        SettingsStore.Save(_settings);
        _diffPan.Reset();         // the other mode's offset means nothing in this one
        _pendingDiffScroll = true; // keep the current change in view across the switch
        MarkDirty();
    }

    /// <summary>
    /// Where the current change block starts, in the body's own content space.
    ///
    /// <para>With every row the same height that is arithmetic. Wrapped, rows differ, so it
    /// has to come from the row's laid-out position — and that is only meaningful once the
    /// row has been through layout. <c>OnAfterBuild</c> runs after reconciliation, which for
    /// a tree that just changed shape (toggling wrap) can be before layout; a zero height
    /// says so, and returning null there leaves the scroll pending for the next frame rather
    /// than acting on a position that isn't real yet.</para>
    /// </summary>
    private float? DiffChangeTop(ScrollViewElement sv)
    {
        if (!DiffWrap) return _diffChangeRows[_diffChangeIdx] * DiffLineH;
        if (_diffChangeRowRef.Current is not { } row) return null;
        var box = row.Layout.AbsoluteBorderBox;
        return box.Height > 0 ? box.Y - sv.Layout.AbsoluteContentBox.Y + sv.ScrollOffsetY : null;
    }

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
            case KeyCode.Left or KeyCode.Right:
                _diffPan.HandleKey(e); e.StopPropagation = true; break;
            case KeyCode.W: ToggleDiffWrap(); e.StopPropagation = true; break;
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
        if (_diffState == DiffViewState.Ok && _diffPan.CanPan)
            col.AddChild(_diffPan.Scrollbar(Nocturne.Surface, Nocturne.Neutral700, Nocturne.Divider));
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
        UpdateDiffPan();
        var scroll = ScrollView.Vertical().FlexGrow(1).WithRef(_diffScrollRef);
        // A trackpad's sideways swipe arrives as DeltaX; a mouse reports Shift+wheel as
        // DeltaY. The ScrollView's own handler sees both first, but its content is exactly
        // as wide as its viewport, so it clamps them to nothing and leaves them to us.
        scroll.On<ScrollEvent>(ev =>
        {
            _diffPan.HandleScroll(ev);
        });
        var col = Flex.Column(gap: 0);
        for (int r = 0; r < _diffRows.Count; r++)
        {
            bool current = _diffChangeRows.Count > 0 && _diffChangeIdx < _diffChangeRows.Count
                && r == _diffChangeRows[_diffChangeIdx];
            var built = _diffRows[r].CollapseCount is int n
                ? DiffCollapseRow(n)
                : DiffLineRow(_diffRows[r].Line!, current);
            // Wrapped rows have no common height, so n/p can't compute where a change is —
            // it has to ask the row. See DiffChangeTop.
            if (current) built.WithRef(_diffChangeRowRef);
            col.AddChild(built);
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

        // Wrapping needs no alignment work of its own: the row track is auto, so it grows to
        // whichever side wrapped taller and every cell in it — both gutters included — shares
        // that band. A minimum keeps a blank line a row tall.
        var row = Grid.Of($"{DiffGutterW}px 1fr {DiffMarkerW}px {DiffGutterW}px 1fr", gap: 0)
            .AlignItems(AlignItems.Stretch);
        if (DiffWrap) row.MinHeight(DiffLineH); else row.Height(DiffLineH);
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
    // Wrapped, the number belongs beside the *first* of the line's rows rather than floating
    // in the middle of a five-row block, so it sits at the top instead of centred.
    private Element DiffGutter(int no) =>
        Box.New().AlignItems(AlignItems.FlexEnd)
            .JustifyContent(DiffWrap ? JustifyContent.FlexStart : JustifyContent.Center)
            .Padding(new Thickness(DiffWrap ? 3 : 0, 6, 0, 0))
            .Children(Ui.Mono(no > 0 ? no.ToString() : "", 10.5f, Nocturne.Neutral700));

    private Element DiffTextCell(string? text, Color bg, Color fg, object selectionGroup,
        IReadOnlyList<TextSpan>? spans = null)
    {
        // AlignItems is left at its Stretch default so the line fills the cell and clips at
        // the cell's own edge rather than at the width of the text. OverflowX.Hidden is what
        // makes the horizontal offset below safe: without it the shifted text would spill
        // left over the line-number gutter. Wrapped, the cell grows instead of clipping, so
        // the content sits at the top rather than centred in a row it no longer defines.
        var box = Box.New().Background(bg)
            .JustifyContent(DiffWrap ? JustifyContent.FlexStart : JustifyContent.Center)
            .Padding(new Thickness(DiffWrap ? 2 : 0, 8, DiffWrap ? 2 : 0, 8))
            .OverflowX(Overflow.Hidden);
        // Wrapped there is no offset to apply, and the cell already clips.

        var mono = Ui.Mono(text ?? "", DiffFontSize, fg);
        if (!DiffWrap) mono.MaxLines(1).TextOverflow(TextOverflow.Clip);
        if (spans is { Count: > 0 }) mono.Spans(spans);

        // Horizontal scroll. A negative left margin moves the line left and — because the
        // stretch that sizes it subtracts the horizontal margin — widens it by the same
        // amount, so the text that scrolls in from the right is actually laid out.

        // Selectable, with one group per side: a drag runs down the lines of one file
        // rather than zig-zagging between the two. The window owns the group drag and the
        // Ctrl+C that copies it.
        mono.Selectable().SelectionGroup(selectionGroup);

        box.AddChild(DiffWrap ? mono : _diffPan.Cell(mono));
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
            if (_diffPan.CanPan) footer.AddChild(Ui.Kbd("←→"));
            footer.AddChild(Ui.Mono("scroll", 10f, Nocturne.On(0.45)));
            footer.AddChild(Ui.Kbd("w"));
            footer.AddChild(Ui.Mono(DiffWrap ? "no wrap" : "wrap", 10f, Nocturne.On(0.45)));
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
