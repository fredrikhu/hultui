using HultUI;
using HultUI.Core.Builders;
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
        _syncLeft = Dir('L');
        _syncRight = Dir('R');
        if (PathEquals(_syncLeft, _syncRight)) return;   // nothing to sync against itself
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

    private List<SyncRow> VisibleSyncRows() =>
        _syncShowEqual ? _syncRows : _syncRows.Where(r => r.Data.Rel != SyncEngine.Rel.Equal).ToList();

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

    private void HandleSyncKey(KeyDownEvent e)
    {
        var vis = VisibleSyncRows();
        switch (e.Key)
        {
            case KeyCode.Escape: CloseSync(); e.StopPropagation = true; break;
            case KeyCode.Up: _syncSel = Math.Max(0, _syncSel - 1); MarkDirty(); e.StopPropagation = true; break;
            case KeyCode.Down: _syncSel = Math.Min(Math.Max(0, vis.Count - 1), _syncSel + 1); MarkDirty(); e.StopPropagation = true; break;
            case KeyCode.Space:
                if (_syncSel >= 0 && _syncSel < vis.Count) CycleSyncDir(vis[_syncSel]);
                e.StopPropagation = true; break;
            case KeyCode.F3:
                if (_syncSel >= 0 && _syncSel < vis.Count) DiffSyncRow(vis[_syncSel]);
                e.StopPropagation = true; break;
            case KeyCode.Enter: OpenSyncPreflight(); e.StopPropagation = true; break;
        }
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
        box.AddChild(Ui.Icon(Glyphs.Spinner, 20f, Nocturne.Accent));
        box.AddChild(Ui.Mono($"Comparing… {_syncScanned} files", 11.5f, Nocturne.On(0.55)));
        if (_syncCurrent.Length > 0)
            box.AddChild(Ui.Mono(_syncCurrent, 10f, Nocturne.On(0.35)).MaxLines(1).TextOverflow(TextOverflow.Ellipsis));
        return box;
    }

    private Element BuildSyncBody()
    {
        var vis = VisibleSyncRows();
        if (vis.Count == 0)
            return Flex.Column(gap: 8).FlexGrow(1).AlignItems(AlignItems.Center).JustifyContent(JustifyContent.Center)
                .Children(Ui.Icon(Glyphs.Check, 22f, Nocturne.DiffGreen),
                          Text.New("Folders are in sync").FontSize(13f).TextColor(Nocturne.Text));

        var scroll = ScrollView.Vertical().FlexGrow(1).Padding(new Thickness(4, 4, 4, 4));
        var colb = Flex.Column(gap: 0);
        for (int i = 0; i < vis.Count; i++) colb.AddChild(SyncRowView(vis[i], i == _syncSel));
        scroll.AddChild(colb);
        return scroll;
    }

    private Element SyncRowView(SyncRow r, bool selected)
    {
        var d = r.Data;
        bool conflict = d.Rel == SyncEngine.Rel.Conflict;
        var row = Grid.Of(SyncCols, gap: 8).Height(RowHeight).AlignItems(AlignItems.Center)
            .Padding(new Thickness(0, 8, 0, 8)).CornerRadius(Nocturne.RadiusSm)
            .Cursor(CursorType.Pointer);
        if (selected)
            row.Background(Nocturne.Alpha(Nocturne.Accent500, 0.13)).BorderColor(Nocturne.Accent600).BorderWidth(1f);
        else if (conflict)
            row.Background(Nocturne.Alpha(Nocturne.MarkRed, 0.08));
        else if (d.Rel == SyncEngine.Rel.Equal)
            row.Opacity = 0.55f;

        Color leftFg = d.LeftExists ? Nocturne.Text : Nocturne.On(0.30);
        Color rightFg = d.RightExists ? Nocturne.Text : Nocturne.On(0.30);

        row.AddChild(Ui.Mono(d.LeftExists ? d.RelPath : "— missing —", 11.5f, leftFg)
            .MaxLines(1).TextOverflow(TextOverflow.Ellipsis).GridColumn(0));
        row.AddChild(Ui.Mono(d.LeftExists ? FileSystem.ByteCount(d.LeftSize) : "", 10.5f, Nocturne.On(0.45))
            .TextAlign(TextAlign.Right).GridColumn(1));
        row.AddChild(Ui.Mono(d.LeftExists ? $"{d.LeftTime:MM-dd HH:mm}" : "", 10.5f, Nocturne.On(0.45)).GridColumn(2));

        // Center rail — glyph + color; clicking cycles the direction.
        var (glyph, color) = SyncRail(r);
        var rail = Box.New().AlignItems(AlignItems.Center).JustifyContent(JustifyContent.Center)
            .Cursor(CursorType.Pointer).Children(Ui.Mono(glyph, 12f, color));
        rail.OnClick(ev => { ev.StopPropagation = true; CycleSyncDir(r); });
        row.AddChild(rail.GridColumn(3));

        row.AddChild(Ui.Mono(d.RightExists ? $"{d.RightTime:MM-dd HH:mm}" : "", 10.5f, Nocturne.On(0.45)).GridColumn(4));
        row.AddChild(Ui.Mono(d.RightExists ? FileSystem.ByteCount(d.RightSize) : "", 10.5f, Nocturne.On(0.45))
            .TextAlign(TextAlign.Right).GridColumn(5));
        row.AddChild(Ui.Mono(d.RightExists ? d.RelPath : "— missing —", 11.5f, rightFg)
            .TextAlign(TextAlign.Right).MaxLines(1).TextOverflow(TextOverflow.Ellipsis).GridColumn(6));

        int idx = VisibleSyncRows().IndexOf(r);
        row.OnClick(_ => { _syncSel = idx; MarkDirty(); });
        return row;
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
        dl.AddChild(Ui.Kbd("F3"));
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
