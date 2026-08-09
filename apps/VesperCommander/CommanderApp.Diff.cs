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

        if (!File.Exists(_diffLeftPath) || !File.Exists(_diffRightPath)) { _diffState = DiffViewState.Missing; return; }
        var (la, lbin) = LoadForDiff(_diffLeftPath);
        var (ra, rbin) = LoadForDiff(_diffRightPath);
        if (lbin || rbin || la is null || ra is null) { _diffState = DiffViewState.Binary; return; }
        if (DiffEngine.TooLarge(la, ra)) { _diffState = DiffViewState.Binary; return; }

        _diffResult = DiffEngine.Diff(la, ra);
        _diffState = _diffResult.Identical ? DiffViewState.Identical : DiffViewState.Ok;
        if (_diffState == DiffViewState.Ok) BuildDiffRowModel();
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

    private void HandleDiffKey(KeyDownEvent e)
    {
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
            case KeyCode.F5:
                if (_diffState == DiffViewState.Missing) { CloseDiff(); StartCopy(); }
                e.StopPropagation = true; break;
        }
    }

    // ── Diff rendering ───────────────────────────────────────────────────
    private const float DiffLineH = 20f;

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

        var row = Grid.Of("34px 1fr 18px 34px 1fr", gap: 0).Height(DiffLineH).AlignItems(AlignItems.Stretch);
        if (current) row.BorderColor(Nocturne.Accent600).BorderWidth(1f);

        // On a changed line, highlight only the differing middle — the common prefix/suffix
        // stay dim, so the eye lands on what actually changed.
        (int start, int len)? hiL = null, hiR = null;
        if (line.Op is DiffEngine.Op.Change && line.Left != null && line.Right != null)
        {
            var (pre, suf) = DiffEngine.IntraSpan(line.Left, line.Right);
            hiL = (pre, Math.Max(0, line.Left.Length - suf - pre));
            hiR = (pre, Math.Max(0, line.Right.Length - suf - pre));
        }

        row.AddChild(DiffGutter(line.LeftNo).GridColumn(0));
        row.AddChild(DiffTextCell(line.Left, leftBg, line.Op is DiffEngine.Op.Del ? Nocturne.MarkRed : Nocturne.On(0.78), hiL).GridColumn(1));
        row.AddChild(Box.New().AlignItems(AlignItems.Center).JustifyContent(JustifyContent.Center)
            .Children(Ui.Mono(marker, 11f, markColor)).GridColumn(2));
        row.AddChild(DiffGutter(line.RightNo).GridColumn(3));
        row.AddChild(DiffTextCell(line.Right, rightBg, line.Op is DiffEngine.Op.Add ? Nocturne.DiffGreenText : Nocturne.On(0.78), hiR).GridColumn(4));
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

    private Element DiffTextCell(string? text, Color bg, Color fg, (int start, int len)? highlight = null)
    {
        // AlignItems is left at its Stretch default so the line fills the cell and clips at
        // the cell's own edge rather than at the width of the text.
        var box = Box.New().Background(bg).JustifyContent(JustifyContent.Center).Padding(new Thickness(0, 8, 0, 8));
        var mono = Ui.Mono(text ?? "", 11.5f, fg).MaxLines(1).TextOverflow(TextOverflow.Clip);
        if (highlight is (var s, var l) && l > 0 && text != null && s + l <= text.Length)
            mono.Spans([new TextSpan(s, l, Nocturne.DiffChangeMark)]);   // the changed run pops
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
