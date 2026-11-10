using HultUI;
using HultUI.Core.Builders;
using HultUI.Core.Components;
using HultUI.Core.Elements;
using HultUI.Core.Events;
using HultUI.Core.Layout;
using HultUI.Core.Primitives;
using HultUI.Core.Styling;
using VesperCommander.Theme;

namespace VesperCommander;

public partial class CommanderApp
{
    // ── Synchronize directories (F9) — full-shell ────────────────────────
    private enum SyncDir { Skip, ToRight, ToLeft }
    private sealed class SyncRow(SyncEngine.Row data, SyncDir dir)
    {
        public readonly SyncEngine.Row Data = data;
        public SyncDir Dir = dir;
    }

    private bool _syncOpen;
    private string _syncLeft = "", _syncRight = "";
    private SyncEngine.CompareMode _syncMode = SyncEngine.CompareMode.DateSize;
    private bool _syncRecurse = true, _syncShowEqual;
    private bool _syncComparing;
    private readonly List<SyncRow> _syncRows = new();
    private int _syncSel;
    private int _syncScanned;
    private string _syncCurrent = "";
    private System.Threading.CancellationTokenSource? _syncCts;

    private void OpenSync()
    {
        if (AnyOverlayOpen) return;
        // Sync compares and then writes into two folders. A pane showing a fed-in result set
        // has a folder behind it, but that is the folder its search ran under — not what is
        // on screen — so syncing it would act on something the user isn't looking at.
        if (ResultsBlock('L') || ResultsBlock('R')) return;
        _syncLeft = Dir('L');
        _syncRight = Dir('R');
        if (PathEquals(_syncLeft, _syncRight)) return;   // nothing to sync against itself
        RememberPaneScroll();   // the panes are about to be replaced by the view
        _syncOpen = true;
        RunSyncCompare();
        MarkDirty();
    }

    private void CloseSync()
    {
        _syncCts?.Cancel();
        _syncCts = null;
        _syncComparing = false;
        _syncOpen = false;
        _pendingPaneFocus = _active;   // see CloseDiff — the panes are rebuilt after this
        MarkDirty();
    }

    private static SyncDir DefaultDir(SyncEngine.Rel r) => r switch
    {
        SyncEngine.Rel.OnlyLeft or SyncEngine.Rel.LeftNewer => SyncDir.ToRight,
        SyncEngine.Rel.OnlyRight or SyncEngine.Rel.RightNewer => SyncDir.ToLeft,
        _ => SyncDir.Skip,   // Equal, Conflict
    };

    private void RunSyncCompare()
    {
        _syncCts?.Cancel();
        _syncRows.Clear();
        _syncCells.Clear();   // the rows are new: nothing to hand back, and no state to hold
        _syncSel = 0; _syncScanned = 0; _syncCurrent = "";
        _syncComparing = true;
        var cts = new System.Threading.CancellationTokenSource();
        _syncCts = cts;

        var task = SyncEngine.Compare(_syncLeft, _syncRight, _syncMode, _syncRecurse,
            onProgress: (n, p) => Application.InvokeOnMainThread(() =>
            {
                if (_syncCts == cts) { _syncScanned = n; _syncCurrent = p; MarkDirty(); }
            }),
            cts.Token);
        task.ContinueWith(t => Application.InvokeOnMainThread(() =>
        {
            if (_syncCts != cts) return;
            _syncComparing = false;
            if (t.IsCompletedSuccessfully)
                foreach (var r in t.Result) _syncRows.Add(new SyncRow(r, DefaultDir(r.Rel)));
            MarkDirty();
        }));
    }

    /// <summary>Calls of <see cref="VisibleSyncRows"/> on this thread. With "Show equal" off each
    /// makes a filtered copy of the rows, so a build asks once, never once a row; a test counts.</summary>
    [ThreadStatic] internal static int VisibleSyncRowsCalls;

    private List<SyncRow> VisibleSyncRows()
    {
        VisibleSyncRowsCalls++;
        return _syncShowEqual ? _syncRows : _syncRows.Where(r => r.Data.Rel != SyncEngine.Rel.Equal).ToList();
    }

    /// <summary>Cycle a row's direction through the sensible options for its relationship
    /// (one-sided rows can only go one way or skip).</summary>
    private void CycleSyncDir(SyncRow row)
    {
        row.Dir = row.Data.Rel switch
        {
            SyncEngine.Rel.OnlyLeft => row.Dir == SyncDir.ToRight ? SyncDir.Skip : SyncDir.ToRight,
            SyncEngine.Rel.OnlyRight => row.Dir == SyncDir.ToLeft ? SyncDir.Skip : SyncDir.ToLeft,
            SyncEngine.Rel.Equal => SyncDir.Skip,
            _ => row.Dir switch { SyncDir.ToRight => SyncDir.ToLeft, SyncDir.ToLeft => SyncDir.Skip, _ => SyncDir.ToRight },
        };
        MarkDirty();
    }

    /// <summary>How many rows are shown, counted without a list of them.</summary>
    private int VisibleSyncCount()
    {
        if (_syncShowEqual) return _syncRows.Count;
        int n = 0;
        foreach (var r in _syncRows) if (r.Data.Rel != SyncEngine.Rel.Equal) n++;
        return n;
    }

    /// <summary>The row shown at <paramref name="index"/>, found without a list of the rows shown;
    /// null past the end.</summary>
    private SyncRow? SyncRowAt(int index)
    {
        if (index < 0) return null;
        if (_syncShowEqual) return index < _syncRows.Count ? _syncRows[index] : null;
        foreach (var r in _syncRows)
            if (r.Data.Rel != SyncEngine.Rel.Equal && index-- == 0) return r;
        return null;
    }

    private void HandleSyncKey(KeyDownEvent e)
    {
        // The diff key compares the selected pair, as it does in a pane.
        if (Pressed(Cmd.Diff, e))
        {
            if (SyncRowAt(_syncSel) is { } pair) DiffSyncRow(pair);
            e.StopPropagation = true; return;
        }
        switch (e.Key)
        {
            case KeyCode.Escape: CloseSync(); e.StopPropagation = true; break;
            case KeyCode.Up: MoveSyncCursor(Math.Max(0, _syncSel - 1)); e.StopPropagation = true; break;
            case KeyCode.Down: MoveSyncCursor(Math.Min(Math.Max(0, VisibleSyncCount() - 1), _syncSel + 1)); e.StopPropagation = true; break;
            case KeyCode.Space:
                if (SyncRowAt(_syncSel) is { } row) CycleSyncDir(row);
                e.StopPropagation = true; break;
            case KeyCode.Enter: OpenSyncPreflight(); e.StopPropagation = true; break;
        }
    }

    // ── The cursor a key moves with no build ─────────────────────────────
    // The rows are kept between builds (BuildSyncBody), and nothing on screen but the two rows
    // the cursor moves between shows it. So a cursor key takes the ring off the live row it leaves
    // and puts it on the one it reaches (KeptElements.Live), with the row's fade and the selected
    // state a reader is told, as Brief and Thumbnails do, and builds nothing. Those two then show
    // what no build made them with, so each build of the rows first puts them back as they were
    // made (SettleSyncRing), and compares each row's state as always.

    /// <summary>The cursor to the row shown at <paramref name="to"/>: on the live rows with no build
    /// when the latest build made them (<see cref="MoveSyncRingLive"/>); otherwise the view builds
    /// and the parts stay. Each key built the window, both parts and every row.</summary>
    private void MoveSyncCursor(int to)
    {
        int from = _syncSel;
        if (to == from) return;   // at an end of the list: nothing moves, nothing builds
        _syncSel = to;
        if (MoveSyncRingLive(from, to)) return;
        MarkViewDirty();
    }

    /// <summary>The ring off the live row at <paramref name="from"/> and onto the one at
    /// <paramref name="to"/>, with no build. False, for a build, when an overlay is open, the latest
    /// build did not make the rows (another view, the comparison's progress, no rows), or the row
    /// reached has no live element (past the end).</summary>
    private bool MoveSyncRingLive(int from, int to)
    {
        if (AnyOverlayOpen || !_syncOpen || _syncRowsBuild != _builds) return false;
        if (SyncRowAt(to) is not { } reached || _syncCells.Live(reached.Data.RelPath) is not BoxElement reachedEl) return false;
        if (SyncRowAt(from) is { } left && _syncCells.Live(left.Data.RelPath) is BoxElement leftEl) MoveSyncRing(leftEl, left, on: false);
        MoveSyncRing(reachedEl, reached, on: true);
        return true;
    }

    /// <summary>Each row a key moved the ring on or off with no build since the rows' last build,
    /// and whether it was made with the ring. One that is back as it was made is dropped, so a long
    /// walk holds the two it is between.</summary>
    private readonly List<(BoxElement Element, SyncRow Row, bool MadeSelected)> _syncRingMoved = [];

    private void MoveSyncRing(BoxElement el, SyncRow r, bool on)
    {
        bool shows = el.BorderColor == Nocturne.Accent600;
        if (shows == on) return;
        int i = 0;
        while (i < _syncRingMoved.Count && !ReferenceEquals(_syncRingMoved[i].Element, el)) i++;
        if (i == _syncRingMoved.Count) _syncRingMoved.Add((el, r, shows));
        else if (_syncRingMoved[i].MadeSelected == on) _syncRingMoved.RemoveAt(i);
        ShowSyncCursor(el, r, on);
    }

    /// <summary>At the start of a build of the rows: each row a key moved the ring on or off goes
    /// back as it was made, so a row handed back shows its state (the cursor can move with a build
    /// too: a rescan, "Show equal").</summary>
    private void SettleSyncRing()
    {
        foreach (var (el, r, madeSelected) in _syncRingMoved)
            if (el.IsHosted) ShowSyncCursor(el, r, madeSelected);
        _syncRingMoved.Clear();
    }

    private void DiffSyncRow(SyncRow row)
    {
        if (!row.Data.LeftExists || !row.Data.RightExists) return;
        _diffLeftPath = System.IO.Path.Combine(_syncLeft, row.Data.RelPath);
        _diffRightPath = System.IO.Path.Combine(_syncRight, row.Data.RelPath);
        _diffSide = 'L';
        _syncOpen = false;   // hand the shell to the diff view; Esc returns to panes
        ComputeDiff();
        _diffOpen = true;
        MarkDirty();
    }

    private (int right, int left, int conflicts, long bytes) SyncCounts()
    {
        int right = 0, left = 0, conflicts = 0; long bytes = 0;
        foreach (var r in _syncRows)
        {
            if (r.Data.Rel == SyncEngine.Rel.Conflict && r.Dir == SyncDir.Skip) conflicts++;
            if (r.Dir == SyncDir.ToRight) { right++; bytes += r.Data.LeftSize; }
            else if (r.Dir == SyncDir.ToLeft) { left++; bytes += r.Data.RightSize; }
        }
        return (right, left, conflicts, bytes);
    }

    private void OpenSyncPreflight()
    {
        var (right, left, _, bytes) = SyncCounts();
        if (right + left == 0) return;
        _confirm = new ConfirmSpec(
            "Synchronize",
            $"Copy {right} file(s) → right and {left} file(s) ← left\nTotal {FileSystem.ByteLabel(bytes)}",
            "Synchronize", Danger: false,
            CommitSync);
        MarkDirty();
    }

    private void CommitSync()
    {
        _confirm = null;
        var rightItems = new List<FileEntry>();
        var leftItems = new List<FileEntry>();
        foreach (var r in _syncRows)
        {
            if (r.Dir == SyncDir.ToRight)
                rightItems.Add(new FileEntry(r.Data.RelPath, System.IO.Path.Combine(_syncLeft, r.Data.RelPath),
                    IsDir: false, r.Data.LeftSize, r.Data.LeftTime));
            else if (r.Dir == SyncDir.ToLeft)
                leftItems.Add(new FileEntry(r.Data.RelPath, System.IO.Path.Combine(_syncRight, r.Data.RelPath),
                    IsDir: false, r.Data.RightSize, r.Data.RightTime));
        }
        if (rightItems.Count == 0 && leftItems.Count == 0) return;

        // Directions already confirmed in the view, so overwrite without re-prompting.
        TransferOp? rightOp = rightItems.Count > 0
            ? new TransferOp(rightItems) { Source = 'L', DestDir = _syncRight, OverwriteAll = true } : null;
        TransferOp? leftOp = leftItems.Count > 0
            ? new TransferOp(leftItems) { Source = 'R', DestDir = _syncLeft, OverwriteAll = true } : null;

        _syncOpen = false;
        _syncSecondBatch = rightOp != null ? leftOp : null;
        _op = rightOp ?? leftOp!;
        _progressOpen = true;
        MarkDirty();
        var first = _op;
        System.Threading.Tasks.Task.Run(() => RunTransfer(first));
    }

    // ── Sync rendering ───────────────────────────────────────────────────
    private const string SyncCols = "1fr 60px 96px 34px 96px 60px 1fr";

    private Element BuildSync()
    {
        var col = Flex.Column(gap: 0).FlexGrow(1).Background(Nocturne.Bg);
        col.AddChild(BuildSyncHeader());
        col.AddChild(BuildSyncColumnHeader());
        col.AddChild(_syncComparing && _syncRows.Count == 0
            ? BuildSyncComparing()
            : BuildSyncBody());
        col.AddChild(BuildSyncCommitBar());
        return col;
    }

    private Element BuildSyncHeader()
    {
        var h = Flex.Row(gap: 10).FlexShrink(0).AlignItems(AlignItems.Center)
            .Padding(new Thickness(8, 14, 8, 14))
            .Background(Nocturne.Surface)
            .BorderColor(Nocturne.Divider).BorderWidth(new Thickness(0, 0, 1, 0));

        var back = Box.New().Cursor(CursorType.Pointer).Padding(new Thickness(2, 8, 2, 8)).CornerRadius(5f)
            .Children(Ui.Icon(Glyphs.Back, 16f, Nocturne.Neutral300)).Tooltip("Back · Esc");
        back.OnClick(_ => CloseSync());
        h.AddChild(back.HoverBg(Nocturne.Neutral900, Color.Transparent));

        h.AddChild(Text.New("Synchronize").FlexShrink(0).FontSize(13f).FontWeight(FontWeight.Medium).TextColor(Nocturne.Text));
        // Paths share a growing, clipped region so long paths truncate instead of
        // colliding with the title / toolbar.
        var paths = Flex.Row(gap: 8).FlexGrow(1).AlignItems(AlignItems.Center).Overflow(Overflow.Hidden);
        paths.AddChild(Ui.Mono(_syncLeft, 10.5f, Nocturne.On(0.50)).FlexShrink(1).MaxLines(1).TextOverflow(TextOverflow.Ellipsis));
        paths.AddChild(Ui.Icon(Glyphs.Swap, 12f, Nocturne.Accent).FlexShrink(0));
        paths.AddChild(Ui.Mono(_syncRight, 10.5f, Nocturne.On(0.50)).FlexShrink(1).MaxLines(1).TextOverflow(TextOverflow.Ellipsis));
        h.AddChild(paths);

        var tools = Flex.Row(gap: 12).FlexShrink(0).AlignItems(AlignItems.Center);
        tools.AddChild(Seg(
            ("Date + size", _syncMode == SyncEngine.CompareMode.DateSize, () => { _syncMode = SyncEngine.CompareMode.DateSize; RunSyncCompare(); }),
            ("Content", _syncMode == SyncEngine.CompareMode.Content, () => { _syncMode = SyncEngine.CompareMode.Content; RunSyncCompare(); })));
        tools.AddChild(SyncCheck("Recurse", _syncRecurse, () => { _syncRecurse = !_syncRecurse; RunSyncCompare(); }));
        tools.AddChild(SyncCheck("Show equal", _syncShowEqual, () => { _syncShowEqual = !_syncShowEqual; MarkDirty(); }));
        h.AddChild(tools);
        return h;
    }

    private Element SyncCheck(string label, bool on, Action toggle)
    {
        var row = Flex.Row(gap: 6).AlignItems(AlignItems.Center).Cursor(CursorType.Pointer);
        var box = Box.New().Width(14).Height(14).CornerRadius(3f)
            .AlignItems(AlignItems.Center).JustifyContent(JustifyContent.Center)
            .BorderColor(on ? Nocturne.Accent : Nocturne.Neutral600).BorderWidth(1.5f)
            .Background(on ? Nocturne.Accent : Color.Transparent);
        if (on) box.AddChild(Ui.Icon(Glyphs.Check, 9f, Nocturne.Accent100));
        row.AddChild(box);
        row.AddChild(Text.New(label).FontSize(11.5f).TextColor(Nocturne.Neutral300));
        row.OnClick(_ => toggle());
        return row;
    }

    private Element BuildSyncColumnHeader()
    {
        var h = Grid.Of(SyncCols, gap: 8).FlexShrink(0)
            .Padding(new Thickness(4, 12, 4, 12))
            .BorderColor(Nocturne.Divider).BorderWidth(new Thickness(0, 0, 1, 0));
        h.AddChild(SyncHdr("LEFT").GridColumn(0));
        h.AddChild(SyncHdr("SIZE").TextAlign(TextAlign.Right).GridColumn(1));
        h.AddChild(SyncHdr("MODIFIED").GridColumn(2));
        h.AddChild(Box.New().GridColumn(3));
        h.AddChild(SyncHdr("MODIFIED").GridColumn(4));
        h.AddChild(SyncHdr("SIZE").TextAlign(TextAlign.Right).GridColumn(5));
        h.AddChild(SyncHdr("RIGHT").TextAlign(TextAlign.Right).GridColumn(6));
        return h;
    }

    private static TextElement SyncHdr(string t) =>
        Text.New(t).FontSize(9.5f).LetterSpacing(0.08f * 9.5f).TextColor(Nocturne.Neutral600);

    private Element BuildSyncComparing()
    {
        var box = Flex.Column(gap: 10).FlexGrow(1).AlignItems(AlignItems.Center).JustifyContent(JustifyContent.Center);
        box.AddChild(Spinner.New(20f));
        box.AddChild(Ui.Mono($"Comparing… {_syncScanned} files", 11.5f, Nocturne.On(0.55)));
        if (_syncCurrent.Length > 0)
            box.AddChild(Ui.Mono(_syncCurrent, 10f, Nocturne.On(0.35)).MaxLines(1).TextOverflow(TextOverflow.Ellipsis));
        return box;
    }

    // ── The rows a build keeps ───────────────────────────────────────────
    // A row shows its pair (the SyncRow, whose data never changes), its direction and whether the
    // cursor is on it; besides those only what every row shows, the row height and the theme. A
    // build hands back each row whose state is what it was made with (KeptElements, keyed by the
    // path), so a Space makes the row it turned and "Show equal" the Equal rows it brings in; a
    // cursor key makes none (MoveSyncCursor). The row's handlers capture the SyncRow, and look up
    // its place when they run.

    /// <summary>What a Sync row shows and acts on besides the shape.</summary>
    private readonly record struct SyncRowState(SyncRow Row, SyncDir Dir, bool Selected);

    /// <summary>What every Sync row shows.</summary>
    private readonly record struct SyncRowShape(float RowH, object? Theme);

    private readonly KeptElements<SyncRowState> _syncCells = new();
    private readonly ShapeState<SyncRowShape> _syncCellsShape = new();
    /// <summary>The build that made the rows on screen: a cursor key moves the ring on them only
    /// while it is the latest.</summary>
    private int _syncRowsBuild = -1;

    private Element BuildSyncBody()
    {
        SettleSyncRing();
        var vis = VisibleSyncRows();
        if (vis.Count == 0)
            return Flex.Column(gap: 8).FlexGrow(1).AlignItems(AlignItems.Center).JustifyContent(JustifyContent.Center)
                .Children(Ui.Icon(Glyphs.Check, 22f, Nocturne.DiffGreen),
                          Text.New("Folders are in sync").FontSize(13f).TextColor(Nocturne.Text));

        if (_syncCellsShape.Changed(new SyncRowShape(RowHeight, _app.Theme))) _syncCells.Clear();
        _syncCells.Begin(_builds);
        _syncRowsBuild = _builds;
        var scroll = ScrollView.Vertical().FlexGrow(1).Padding(new Thickness(4, 4, 4, 4));
        var colb = Flex.Column(gap: 0);
        for (int i = 0; i < vis.Count; i++)
        {
            var r = vis[i];
            var state = new SyncRowState(r, r.Dir, i == _syncSel);
            colb.AddChild(_syncCells.TryKeep(r.Data.RelPath, state, out var kept) ? kept
                : _syncCells.Made(r.Data.RelPath, state, SyncRowView(r, state.Selected)));
        }
        scroll.AddChild(colb);
        return scroll;
    }

    /// <summary>How opaque an Equal row is drawn. The row fades each colour it draws, not itself:
    /// <c>Opacity</c> on an element with children draws it offscreen, as a layer of its own, so
    /// every Equal row on screen was a layer on every Sync frame (26 at 1240×820, 36–38 % of a GL
    /// frame; plan 17 round 9 step 13).</summary>
    private const float EqualRowOpacity = 0.55f;

    /// <summary>A Sync row's colour at the row's <paramref name="opacity"/>.</summary>
    private static Color Faded(Color c, float opacity) => opacity < 1f ? Nocturne.Fade(c, opacity) : c;

    /// <summary>What a Sync row draws its texts in: an Equal row reads dimmer, every colour it draws
    /// at 55 % (see <see cref="EqualRowOpacity"/>), unless it is the cursor's.</summary>
    private readonly record struct SyncInk(Color Left, Color Meta, Color Rail, Color Right);

    private SyncInk InkOf(SyncRow r, bool selected)
    {
        var d = r.Data;
        float fade = !selected && d.Rel == SyncEngine.Rel.Equal ? EqualRowOpacity : 1f;
        return new SyncInk(
            Faded(d.LeftExists ? Nocturne.Text : Nocturne.On(0.30), fade),
            Faded(Nocturne.On(0.45), fade),
            Faded(SyncRail(r).color, fade),
            Faded(d.RightExists ? Nocturne.Text : Nocturne.On(0.30), fade));
    }

    /// <summary>A Sync row. Its handlers capture the pair and look up its place when they run: a
    /// kept row may stand elsewhere by then ("Show equal" moves the rows after an Equal one). Each
    /// row looked itself up in a fresh list of the rows shown as it was made, O(rows²) a build (plan
    /// 17 round 10 step 7).</summary>
    private BoxElement SyncRowView(SyncRow r, bool selected)
    {
        var d = r.Data;
        // One 1fr track, so the cells centre in the whole row — the default auto track hugs
        // the tallest cell and leaves them along the row's top (docs/GRID.md §6).
        var row = Grid.Of(SyncCols, rows: "1fr", gap: 8).Height(RowHeight).AlignItems(AlignItems.Center)
            .Padding(new Thickness(0, 8, 0, 8)).CornerRadius(Nocturne.RadiusSm)
            .Cursor(CursorType.Pointer);
        // A button to a reader, as a clickable row is, named by what it shows; set here so that
        // whether it is the cursor's is told on its node with no tree (SyncRowLook).
        row.Role = HultUI.Core.Accessibility.AccessibilityRole.Button;
        SyncRowLook(row, selected, d.Rel == SyncEngine.Rel.Conflict);
        var ink = InkOf(r, selected);

        row.AddChild(Ui.Mono(d.LeftExists ? d.RelPath : "— missing —", 11.5f, ink.Left)
            .MaxLines(1).TextOverflow(TextOverflow.Ellipsis).GridColumn(0));
        row.AddChild(Ui.Mono(d.LeftExists ? FileSystem.ByteCount(d.LeftSize) : "", 10.5f, ink.Meta)
            .TextAlign(TextAlign.Right).GridColumn(1));
        row.AddChild(Ui.Mono(d.LeftExists ? $"{d.LeftTime:MM-dd HH:mm}" : "", 10.5f, ink.Meta).GridColumn(2));

        // Center rail — glyph + color; clicking cycles the direction. It spans the row's
        // height, so the whole cell is the click target, not just the glyph.
        var rail = Box.New().AlignSelf(AlignSelf.Stretch)
            .AlignItems(AlignItems.Center).JustifyContent(JustifyContent.Center)
            .Cursor(CursorType.Pointer).Children(Ui.Mono(SyncRail(r).glyph, 12f, ink.Rail));
        rail.OnClick(ev => { ev.StopPropagation = true; CycleSyncDir(r); });
        row.AddChild(rail.GridColumn(3));

        row.AddChild(Ui.Mono(d.RightExists ? $"{d.RightTime:MM-dd HH:mm}" : "", 10.5f, ink.Meta).GridColumn(4));
        row.AddChild(Ui.Mono(d.RightExists ? FileSystem.ByteCount(d.RightSize) : "", 10.5f, ink.Meta)
            .TextAlign(TextAlign.Right).GridColumn(5));
        row.AddChild(Ui.Mono(d.RightExists ? d.RelPath : "— missing —", 11.5f, ink.Right)
            .TextAlign(TextAlign.Right).MaxLines(1).TextOverflow(TextOverflow.Ellipsis).GridColumn(6));

        row.OnClick(_ =>
        {
            int at = VisibleSyncRows().IndexOf(r);
            if (at >= 0) MoveSyncCursor(at);
        });
        return row;
    }

    /// <summary>The cursor on a Sync row or not, as a build makes it and as a key moves it with no
    /// build: the accent fill and the 1 px ring (a conflict's red tint without it), and to a reader
    /// the row selected or not, which the window tells on the row's node with no tree (its role is
    /// the app's Button). The ring's width moves the row's cells by a pixel, so a live row is laid
    /// out again.</summary>
    private static void SyncRowLook(BoxElement row, bool selected, bool conflict)
    {
        row.BackgroundColor = selected ? Nocturne.Alpha(Nocturne.Accent500, 0.13)
            : conflict ? Nocturne.Alpha(Nocturne.MarkRed, 0.08) : Color.Transparent;
        row.BorderColor = selected ? Nocturne.Accent600 : Color.Transparent;
        var width = selected ? new Thickness(1f) : default;
        if (row.Box.BorderWidth != width)
        {
            row.Box.BorderWidth = width;
            row.MarkLayoutDirty();
        }
        row.AccessibleSelected = selected;
    }

    private static readonly Action<Element> SyncRowOn = static e => SyncRowLook((BoxElement)e, selected: true, conflict: false);
    private static readonly Action<Element> SyncRowOff = static e => SyncRowLook((BoxElement)e, selected: false, conflict: false);
    private static readonly Action<Element> SyncConflictOff = static e => SyncRowLook((BoxElement)e, selected: false, conflict: true);

    /// <summary>The cursor onto or off a live row, with no build: its look (through
    /// <see cref="Element.Restyle"/>) and, for an Equal row, its texts' fade.</summary>
    private void ShowSyncCursor(BoxElement row, SyncRow r, bool selected)
    {
        row.Restyle(selected ? SyncRowOn : r.Data.Rel == SyncEngine.Rel.Conflict ? SyncConflictOff : SyncRowOff);
        if (r.Data.Rel != SyncEngine.Rel.Equal || row.Children.Count != 7) return;
        var ink = InkOf(r, selected);
        InkText(row.Children[0], ink.Left);
        InkText(row.Children[1], ink.Meta);
        InkText(row.Children[2], ink.Meta);
        if (row.Children[3].Children is [var glyph]) InkText(glyph, ink.Rail);
        InkText(row.Children[4], ink.Meta);
        InkText(row.Children[5], ink.Meta);
        InkText(row.Children[6], ink.Right);
    }

    /// <summary>A live text's colour, as <c>.TextColor</c> sets it in a build (an inherited style,
    /// which a direct write does not mark).</summary>
    private static void InkText(Element text, Color color)
    {
        if (text.Inherited.TextColor == color) return;
        text.Inherited.TextColor = color;
        text.MarkStyleDirty();
        text.MarkPaintDirty();
    }

    /// <summary>Rail glyph + color: always a glyph, never color alone (a11y).</summary>
    private (string glyph, Color color) SyncRail(SyncRow r)
    {
        if (r.Data.Rel == SyncEngine.Rel.Equal) return ("=", Nocturne.Neutral500);
        if (r.Data.Rel == SyncEngine.Rel.Conflict && r.Dir == SyncDir.Skip) return ("≠", Nocturne.MarkRed);
        return r.Dir switch
        {
            SyncDir.ToRight => (r.Data.Rel == SyncEngine.Rel.OnlyLeft ? "+→" : "→",
                                r.Data.Rel == SyncEngine.Rel.OnlyLeft ? Nocturne.DiffGreen : Nocturne.Accent300),
            SyncDir.ToLeft => (r.Data.Rel == SyncEngine.Rel.OnlyRight ? "←+" : "←",
                               r.Data.Rel == SyncEngine.Rel.OnlyRight ? Nocturne.DiffGreen : Nocturne.Accent300),
            _ => ("∅", Nocturne.Neutral600),
        };
    }

    private Element BuildSyncCommitBar()
    {
        var (right, left, conflicts, bytes) = SyncCounts();
        int equal = _syncRows.Count(r => r.Data.Rel == SyncEngine.Rel.Equal);
        int skipped = _syncRows.Count(r => r.Dir == SyncDir.Skip && r.Data.Rel != SyncEngine.Rel.Equal && r.Data.Rel != SyncEngine.Rel.Conflict);

        var bar = Flex.Row(gap: 10).FlexShrink(0).AlignItems(AlignItems.Center)
            .Padding(new Thickness(6, 12, 6, 12))
            .Background(Nocturne.Surface)
            .BorderColor(Nocturne.Divider).BorderWidth(new Thickness(1, 0, 0, 0));

        bar.AddChild(Ui.Mono($"{right} →", 11f, Nocturne.Accent300));
        bar.AddChild(Ui.Mono($"{left} ←", 11f, Nocturne.Accent300));
        if (conflicts > 0) bar.AddChild(Ui.Mono($"{conflicts} conflict", 11f, Nocturne.MarkRed));
        if (skipped > 0) bar.AddChild(Ui.Mono($"{skipped} skipped", 11f, Nocturne.On(0.45)));
        if (equal > 0) bar.AddChild(Ui.Mono($"{equal} equal", 11f, Nocturne.On(0.45)));
        bar.AddChild(Box.New().FlexGrow(1));

        // Diff selected (ghost).
        var diffBtn = Box.New().Cursor(CursorType.Pointer).Padding(new Thickness(4, 11, 4, 11)).CornerRadius(Nocturne.RadiusSm)
            .BorderColor(Nocturne.Neutral700).BorderWidth(1f);
        var dl = Flex.Row(gap: 6).AlignItems(AlignItems.Center);
        dl.AddChild(Text.New("Diff").FontSize(11.5f).TextColor(Nocturne.Neutral300));
        if (KeyText(Cmd.Diff) is { Length: > 0 } diffKeys) dl.AddChild(Ui.Kbd(diffKeys));
        diffBtn.AddChild(dl);
        diffBtn.OnClick(_ => { var v = VisibleSyncRows(); if (_syncSel < v.Count) DiffSyncRow(v[_syncSel]); });
        bar.AddChild(diffBtn.HoverBg(Nocturne.Neutral900, Color.Transparent));

        // Synchronize (accent outline, never filled).
        int n = right + left;
        var sync = Box.New().Cursor(n > 0 ? CursorType.Pointer : CursorType.Default)
            .Padding(new Thickness(4, 13, 4, 13)).CornerRadius(Nocturne.RadiusSm)
            .BorderColor(n > 0 ? Nocturne.Accent : Nocturne.Neutral800).BorderWidth(1f);
        var sl = Flex.Row(gap: 6).AlignItems(AlignItems.Center);
        sl.AddChild(Text.New($"Synchronize {n} file{(n == 1 ? "" : "s")}…").FontSize(11.5f)
            .TextColor(n > 0 ? Nocturne.Accent200 : Nocturne.On(0.35)));
        sl.AddChild(Ui.Kbd("↵"));
        sync.AddChild(sl);
        if (n > 0) sync.OnClick(_ => OpenSyncPreflight());
        bar.AddChild(sync);
        return bar;
    }
}
