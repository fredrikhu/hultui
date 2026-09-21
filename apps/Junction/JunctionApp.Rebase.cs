using System.Linq;
using HultUI.Core.Builders;
using HultUI.Core.Elements;
using HultUI.Core.Events;
using HultUI.Core.Layout;
using HultUI.Core.Primitives;
using HultUI.Core.Styling;
using Junction.Git;
using Junction.Theme;

namespace Junction;

public partial class JunctionApp
{
    // ── State model ──────────────────────────────────────────────────────

    /// <summary>One editable line in the rebase plan. Rows are held
    /// <b>newest-first</b> (matching the History graph); the git todo is the
    /// reverse of this order.</summary>
    private sealed class RebaseRow
    {
        public required string Hash;
        public required string ShortHash;
        public required string Subject;
        public required string Author;
        public required string When;
        public int Add;
        public int Del;
        public RebaseOp Op = RebaseOp.Pick;
        public string Reword = "";   // pending message when Op == Reword
    }

    private sealed class RebaseState
    {
        public required string BaseRef;       // the ref we rebase onto (clicked commit's parent)
        public required string BaseLabel;     // short sha shown in the header
        public required string BranchTitle;   // current branch's leaf title
        public required List<RebaseRow> Rows; // newest-first
    }

    private static readonly (RebaseOp Op, string Glyph, string Label, string Hint)[] RebaseActions =
    [
        (RebaseOp.Pick,   Icons.Check,                "Pick",   "Keep the commit as-is"),
        (RebaseOp.Reword, Icons.PencilSimple,         "Reword", "Keep changes, edit the message"),
        (RebaseOp.Edit,   Icons.Wrench,               "Edit",   "Stop to amend this commit"),
        (RebaseOp.Squash, Icons.ArrowsInLineVertical, "Squash", "Meld into previous, keep both messages"),
        (RebaseOp.Fixup,  Icons.ArrowLineUp,          "Fixup",  "Meld into previous, discard this message"),
        (RebaseOp.Drop,   Icons.Trash,                "Drop",   "Remove the commit"),
    ];

    private static readonly Color WarnBorder = Color.FromHex("#a06868");
    private static readonly Color WarnText = Color.FromHex("#e0a76a");
    private static readonly Color DropActiveBg = Color.FromHex("#7d4a4a");

    // ── Open / run ───────────────────────────────────────────────────────

    /// <summary>Opens the rebase editor for the clicked commit plus every newer
    /// commit on HEAD, replayed onto the clicked commit's parent. Loads the
    /// range + per-commit stats off-thread so Build() never blocks.</summary>
    private async void OpenRebase(GitCommit c)
    {
        if (ActiveRepo is not { } repo) return;
        if (c.ParentHashes.Length == 0) { Flash("Can't interactively rebase from the root commit"); return; }

        var parent = c.ParentHashes[0];
        var branchTitle = CurrentBranchTitle();

        List<RebaseRow> rows;
        try { rows = await Task.Run(() => LoadRebaseRows(repo, parent)); }
        catch { Flash("Couldn't load the rebase range"); return; }

        if (rows.Count == 0) { Flash("Nothing to rebase"); return; }

        _rebase = new RebaseState
        {
            BaseRef = parent,
            BaseLabel = Short(parent),
            BranchTitle = branchTitle,
            Rows = rows,
        };
        _rbSel = 0;
        _rbRewordIndex = null;
        MarkDirty();
    }

    // Off the UI thread — no UI / reactive access.
    private static List<RebaseRow> LoadRebaseRows(GitRepository repo, string baseRef)
    {
        var range = repo.GetRebaseRange(baseRef);                       // oldest-first
        var stats = repo.GetLogStats(500, [$"{baseRef}..HEAD"]);
        var rows = new List<RebaseRow>(range.Count);
        foreach (var cm in range)
        {
            var (add, del) = stats.TryGetValue(cm.Hash, out var s) ? s : (0, 0);
            rows.Add(new RebaseRow
            {
                Hash = cm.Hash,
                ShortHash = cm.ShortHash,
                Subject = cm.Subject,
                Author = cm.AuthorName,
                When = cm.AuthorDate.ToString("g"),
                Add = add,
                Del = del,
                Reword = cm.Subject,
            });
        }
        rows.Reverse();   // present newest-first, matching History
        return rows;
    }

    /// <summary>The oldest row that survives (bottom of the list, skipping anything
    /// dropped) can't squash/fixup — there is nothing older left to meld into. Mirrors
    /// git's own todo constraint, which is checked against the todo we actually write:
    /// dropped rows never reach it, so a drop can push a squash into first place.</summary>
    private bool RebaseHasBadMeld() =>
        _rebase is { } rb
        && rb.Rows.FindLast(r => r.Op != RebaseOp.Drop) is { } oldestKept
        && oldestKept.Op is RebaseOp.Squash or RebaseOp.Fixup;

    private async void RunRebase()
    {
        if (_rebase is not { } rb || RebaseHasBadMeld()) return;
        if (ActiveRepo is not { } repo) return;

        // git's todo runs oldest-first — reverse our newest-first rows.
        var plan = Enumerable.Reverse(rb.Rows).Select(r => new RebaseEntry
        {
            Hash = r.Hash,
            ShortHash = r.ShortHash,
            Subject = r.Subject,
            Op = r.Op,
            NewMessage = r.Op == RebaseOp.Reword ? r.Reword : null,
        }).ToList();

        var kept = rb.Rows.Count(r => r.Op != RebaseOp.Drop);
        var melded = rb.Rows.Count(r => r.Op is RebaseOp.Squash or RebaseOp.Fixup);
        var dropped = rb.Rows.Count - kept;
        var branchTitle = rb.BranchTitle;
        var baseRef = rb.BaseRef;

        _rebase = null;
        _rbRewordIndex = null;
        MarkDirty();

        var res = await Task.Run(() => repo.InteractiveRebase(baseRef, plan));
        if (res.Success)
        {
            var bits = new List<string> { $"{kept} commit{(kept == 1 ? "" : "s")}" };
            if (melded > 0) bits.Add($"{melded} melded");
            if (dropped > 0) bits.Add($"{dropped} dropped");
            Flash($"Rebased {branchTitle} — {string.Join(", ", bits)}");
        }
        else if (!StoppedOnConflict(repo, "Rebase"))
        {
            Flash(FirstLine(res.Stderr, "Rebase failed"));
        }
        RefreshAll();
    }

    private void CloseRebase()
    {
        _rebase = null;
        _rbRewordIndex = null;
        MarkDirty();
    }

    // ── Row mutation (identity-based so reordering can't leak stale handlers) ──

    private void SelectRebaseRow(RebaseRow r)
    {
        if (_rebase is not { } rb) return;
        var i = rb.Rows.IndexOf(r);
        if (i < 0 || i == _rbSel) return;
        _rbSel = i;
        MarkDirty();
    }

    private void RbSetActionRow(RebaseRow r, RebaseOp op)
    {
        if (_rebase is not { } rb) return;
        r.Op = op;
        _rbSel = rb.Rows.IndexOf(r);
        MarkDirty();
        if (op == RebaseOp.Reword) OpenRewordRow(r);
    }

    private void RbMoveRow(RebaseRow r, int dir)
    {
        if (_rebase is not { } rb) return;
        var i = rb.Rows.IndexOf(r);
        if (i < 0) return;
        var j = i + dir;
        if (j < 0 || j >= rb.Rows.Count) return;
        (rb.Rows[i], rb.Rows[j]) = (rb.Rows[j], rb.Rows[i]);
        _rbSel = j;   // keep the cursor on the row that moved
        MarkDirty();
    }

    private void MoveSelectedRow(int dir)
    {
        if (_rebase is not { } rb || _rbSel < 0 || _rbSel >= rb.Rows.Count) return;
        RbMoveRow(rb.Rows[_rbSel], dir);
    }

    // ── Reword sub-dialog ────────────────────────────────────────────────

    private void OpenRewordRow(RebaseRow r)
    {
        if (_rebase is not { } rb) return;
        _rbRewordIndex = rb.Rows.IndexOf(r);
        _rbRewordText = string.IsNullOrEmpty(r.Reword) ? r.Subject : r.Reword;
        _pendingRewordFocus = true;
        MarkDirty();
    }

    private void SaveReword()
    {
        if (_rebase is not { } rb || _rbRewordIndex is not { } i || i < 0 || i >= rb.Rows.Count) return;
        rb.Rows[i].Op = RebaseOp.Reword;
        rb.Rows[i].Reword = _rbRewordText;
        _rbRewordIndex = null;
        MarkDirty();
    }

    private void CloseReword()
    {
        _rbRewordIndex = null;
        MarkDirty();
    }

    // ── Keyboard ─────────────────────────────────────────────────────────

    private bool HandleRebaseKey(KeyDownEvent e)
    {
        if (_rebase is not { } rb) return false;
        var n = rb.Rows.Count;

        switch (e.Key)
        {
            case KeyCode.Escape: CloseRebase(); return true;
            case KeyCode.Enter: if (!RebaseHasBadMeld()) RunRebase(); return true;
            case KeyCode.Down:
                if (e.Modifiers.HasFlag(KeyModifiers.Alt)) MoveSelectedRow(1);
                else { _rbSel = Math.Min(n - 1, _rbSel + 1); MarkDirty(); }
                return true;
            case KeyCode.Up:
                if (e.Modifiers.HasFlag(KeyModifiers.Alt)) MoveSelectedRow(-1);
                else { _rbSel = Math.Max(0, _rbSel - 1); MarkDirty(); }
                return true;
        }

        var op = e.Key switch
        {
            KeyCode.P => (RebaseOp?)RebaseOp.Pick,
            KeyCode.R => RebaseOp.Reword,
            KeyCode.E => RebaseOp.Edit,
            KeyCode.S => RebaseOp.Squash,
            KeyCode.F => RebaseOp.Fixup,
            KeyCode.D => RebaseOp.Drop,
            _ => null,
        };
        if (op is { } o && _rbSel >= 0 && _rbSel < n)
        {
            RbSetActionRow(rb.Rows[_rbSel], o);
            return true;
        }
        return false;
    }

    // ── Build ────────────────────────────────────────────────────────────

    private Element BuildRebaseDialog(RebaseState rb)
    {
        // Backdrop is a column: JustifyContent is the vertical axis (top-anchored
        // via padding), AlignItems the horizontal axis (centered).
        var backdrop = Box.New()
            .Width(Dimension.Percent(100)).Height(Dimension.Percent(100))
            .Background(new Color(0, 0, 0, 140))
            .JustifyContent(JustifyContent.FlexStart).AlignItems(AlignItems.Center)
            .Padding(new Thickness(52, 24, 24, 24))
            .ZIndex(72);
        backdrop.Position.Type = PositionType.Absolute;
        backdrop.Position.Top = Dimension.Px(0);
        backdrop.Position.Left = Dimension.Px(0);
        backdrop.On<ClickEvent>(e => { e.StopPropagation = true; CloseRebase(); });

        var card = Flex.Column(gap: 0)
            .Width(760).MaxWidth(Dimension.Percent(94)).MaxHeight(780)
            .Background(Nocturne.Surface)
            .BorderColor(Nocturne.Neutral700).BorderWidth(1f)
            .CornerRadius(Nocturne.RadiusLg);
        card.On<ClickEvent>(e => e.StopPropagation = true);

        card.AddChild(BuildRebaseHeader(rb));
        card.AddChild(BuildRebaseLegend());
        card.AddChild(BuildRebaseRows(rb));
        card.AddChild(BuildRebaseActions(rb));

        backdrop.AddChild(card);

        // The reword sub-dialog is a nested modal over the rebase card.
        if (_rbRewordIndex is { } ri && ri >= 0 && ri < rb.Rows.Count)
            backdrop.AddChild(BuildRewordDialog(rb.Rows[ri]));

        return backdrop;
    }

    private Element BuildRebaseHeader(RebaseState rb)
    {
        var header = Flex.Row(gap: 12).AlignItems(AlignItems.FlexStart).FlexShrink(0)
            .Padding(new Thickness(16, 18, 13, 18))
            .BorderColor(Nocturne.Divider).BorderWidth(new Thickness(0, 0, 1, 0));

        header.AddChild(Box.New().Width(34).Height(34).FlexShrink(0).CornerRadius(Nocturne.RadiusMd)
            .Background(Nocturne.Accent800).JustifyContent(JustifyContent.Center).AlignItems(AlignItems.Center)
            .Children(Ui.Icon(Icons.ListChecks, 17f, Nocturne.Accent200)));

        var titleCol = Flex.Column(gap: 2).FlexGrow(1).FlexShrink(1);
        titleCol.AddChild(Text.New("Interactive rebase").FontSize(16f).FontWeight(FontWeight.Medium));

        // "Replaying N commits on <branch> onto <base>. Newest first, matching History."
        var sub = Flex.Row(gap: 4).AlignItems(AlignItems.Center).FlexWrap(FlexWrap.Wrap);
        var muted = Nocturne.On(0.52);
        sub.AddChild(Text.New("Replaying").FontSize(11.5f).TextColor(muted));
        sub.AddChild(Text.New(rb.Rows.Count.ToString()).FontSize(11.5f).TextColor(Nocturne.Accent300));
        sub.AddChild(Text.New($"commit{(rb.Rows.Count == 1 ? "" : "s")} on").FontSize(11.5f).TextColor(muted));
        sub.AddChild(Ui.Mono(rb.BranchTitle, 11.5f, Nocturne.Accent300));
        sub.AddChild(Text.New("onto").FontSize(11.5f).TextColor(muted));
        sub.AddChild(Ui.Mono(rb.BaseLabel, 11.5f, Nocturne.On(0.68)));
        sub.AddChild(Text.New("· newest first, matching History").FontSize(11.5f).TextColor(muted));
        titleCol.AddChild(sub);
        header.AddChild(titleCol);

        header.AddChild(Ui.IconButton(Icons.X, CloseRebase, "Close", 28f));
        return header;
    }

    private Element BuildRebaseLegend()
    {
        var legend = Flex.Row(gap: 14).AlignItems(AlignItems.Center).FlexShrink(0)
            .Padding(new Thickness(8, 18, 8, 18))
            .BorderColor(Nocturne.Divider).BorderWidth(new Thickness(0, 0, 1, 0));
        foreach (var a in RebaseActions)
        {
            var color = a.Op == RebaseOp.Pick ? Nocturne.Accent300 : Nocturne.On(0.46);
            legend.AddChild(Flex.Row(gap: 5).AlignItems(AlignItems.Center).Children(
                Ui.Icon(a.Glyph, 11f, color),
                Text.New(a.Label).FontSize(10.5f).TextColor(Nocturne.On(0.46))));
        }
        return legend;
    }

    private Element BuildRebaseRows(RebaseState rb)
    {
        // Pad the inner column, not the ScrollView: the scroll clips at its content
        // box, so a bordered child flush against that edge loses its top/left border.
        var scroll = ScrollView.New().FlexGrow(1).FlexShrink(1);
        var col = Flex.Column(gap: 8).Padding(new Thickness(14, 18, 14, 18));
        for (var i = 0; i < rb.Rows.Count; i++)
            col.AddChild(BuildRebaseRow(rb, rb.Rows[i], i));
        scroll.AddChild(col);
        return scroll;
    }

    private Element BuildRebaseRow(RebaseState rb, RebaseRow r, int index)
    {
        var selected = index == _rbSel;
        var isDrop = r.Op == RebaseOp.Drop;
        var isMeld = r.Op is RebaseOp.Squash or RebaseOp.Fixup;
        var noMeldTarget = isMeld && index == rb.Rows.Count - 1;

        var border = noMeldTarget ? WarnBorder : selected ? Nocturne.Accent : Nocturne.Divider;
        var row = Flex.Row(gap: 10).AlignItems(AlignItems.FlexStart)
            .Padding(new Thickness(9, 11, 9, 11))
            .Background(selected ? Nocturne.Accent900 : Nocturne.Bg)
            .BorderColor(border).BorderWidth(1f).CornerRadius(Nocturne.RadiusMd)
            .Cursor(CursorType.Pointer);
        // Reordering swaps rows, so key by commit identity — rows keep their own
        // retained state across moves instead of patching into each other by position.
        row.Key = "rb-" + r.Hash;
        row.On<ClickEvent>(_ => SelectRebaseRow(r));

        // Drag handle (visual affordance; reorder is via the carets / ⌥↑↓).
        row.AddChild(Box.New().Width(14).AlignSelf(AlignSelf.Center)
            .JustifyContent(JustifyContent.Center).AlignItems(AlignItems.Center)
            .Children(Ui.Icon(Icons.DotsSixVertical, 15f, Nocturne.On(0.34))));

        // Reorder carets.
        var carets = Flex.Column(gap: 1).FlexShrink(0);
        carets.AddChild(RbCaret(Icons.CaretUp, index > 0, () => RbMoveRow(r, -1), "Move up"));
        carets.AddChild(RbCaret(Icons.CaretDown, index < rb.Rows.Count - 1, () => RbMoveRow(r, 1), "Move down"));
        row.AddChild(carets);

        // Action segmented control.
        row.AddChild(BuildActionSegments(r));

        // Message + meta.
        var body = Flex.Column(gap: 3).FlexGrow(1).FlexShrink(1).Padding(new Thickness(1, 0, 0, 0));
        var msgRow = Flex.Row(gap: 6).AlignItems(AlignItems.Center);
        var msg = Text.New(r.Op == RebaseOp.Reword ? r.Reword : r.Subject)
            .FontSize(12.5f)
            .FlexGrow(1).FlexShrink(1)
            .OverflowMode(TextOverflow.Ellipsis).MaxLines(1);
        if (isDrop) msg.TextColor(Nocturne.On(0.55)).Strikethrough();
        msgRow.AddChild(msg);
        if (r.Op == RebaseOp.Reword)
            msgRow.AddChild(Ui.IconButton(Icons.PencilSimple, () => OpenRewordRow(r), "Edit message", 22f)
                .Height(20f).BorderWidth(0f));
        body.AddChild(msgRow);

        var meta = Flex.Row(gap: 8).AlignItems(AlignItems.Center).FlexWrap(FlexWrap.Wrap);
        meta.AddChild(Ui.Mono(r.ShortHash, 10.5f, Nocturne.On(0.44)));
        meta.AddChild(Text.New(r.Author).FontSize(10.5f).TextColor(Nocturne.On(0.44)));
        meta.AddChild(Text.New("·").FontSize(10.5f).TextColor(Nocturne.On(0.3)));
        meta.AddChild(Text.New(r.When).FontSize(10.5f).TextColor(Nocturne.On(0.44)));
        meta.AddChild(Text.New($"+{r.Add}").FontSize(10.5f).TextColor(Nocturne.AddCount));
        meta.AddChild(Text.New($"−{r.Del}").FontSize(10.5f).TextColor(Nocturne.DelCount));
        if (noMeldTarget)
            meta.AddChild(Flex.Row(gap: 3).AlignItems(AlignItems.Center).Children(
                Ui.Icon(Icons.Warning, 10f, WarnText),
                Text.New("nothing below to meld into").FontSize(10.5f).TextColor(WarnText)));
        body.AddChild(meta);
        row.AddChild(body);

        return row;
    }

    private Element RbCaret(string glyph, bool enabled, Action onClick, string tooltip)
    {
        var b = Flex.Row(gap: 0).JustifyContent(JustifyContent.Center).AlignItems(AlignItems.Center)
            .Width(22).Height(16).CornerRadius(Nocturne.RadiusSm)
            .Children(Ui.Icon(glyph, 12f, Nocturne.On(enabled ? 0.5 : 0.2)));
        if (enabled)
        {
            b.Cursor(CursorType.Pointer).Tooltip(tooltip);
            b.On<ClickEvent>(e => { e.StopPropagation = true; onClick(); });
            b.HoverBg(Nocturne.Neutral900, Color.Transparent);
        }
        return b;
    }

    private Element BuildActionSegments(RebaseRow r)
    {
        var seg = Flex.Row(gap: 1).FlexShrink(0)
            .Padding(new Thickness(2))
            .Background(Nocturne.Bg)
            .BorderColor(Nocturne.Divider).BorderWidth(1f).CornerRadius(Nocturne.RadiusMd);
        foreach (var a in RebaseActions)
        {
            var active = r.Op == a.Op;
            var bg = active ? (a.Op == RebaseOp.Drop ? DropActiveBg : Nocturne.Accent700) : Color.Transparent;
            var fg = active ? Nocturne.Accent100 : Nocturne.On(0.55);
            var btn = Box.New()
                .Width(30).Height(28).CornerRadius(Nocturne.RadiusRow)
                .Background(bg)
                .JustifyContent(JustifyContent.Center).AlignItems(AlignItems.Center)
                .Cursor(CursorType.Pointer).Tooltip(a.Hint)
                .Children(Ui.Icon(a.Glyph, 14f, fg));
            var op = a.Op;
            btn.On<ClickEvent>(e => { e.StopPropagation = true; RbSetActionRow(r, op); });
            if (!active) btn.HoverBg(Nocturne.Neutral900, Color.Transparent);
            seg.AddChild(btn);
        }
        return seg;
    }

    private Element BuildRebaseActions(RebaseState rb)
    {
        var dropped = rb.Rows.Count(r => r.Op == RebaseOp.Drop);
        var melded = rb.Rows.Count(r => r.Op is RebaseOp.Squash or RebaseOp.Fixup);
        var reworded = rb.Rows.Count(r => r.Op == RebaseOp.Reword);
        var parts = new List<string> { $"{rb.Rows.Count - dropped} kept" };
        if (melded > 0) parts.Add($"{melded} melded");
        if (reworded > 0) parts.Add($"{reworded} reworded");
        if (dropped > 0) parts.Add($"{dropped} dropped");
        var bad = RebaseHasBadMeld();

        var bar = Flex.Row(gap: 12).AlignItems(AlignItems.Center).FlexShrink(0)
            .Padding(new Thickness(12, 18, 12, 18))
            .BorderColor(Nocturne.Divider).BorderWidth(new Thickness(1, 0, 0, 0));

        bar.AddChild(Text.New(string.Join(" · ", parts)).FontSize(11.5f).TextColor(Nocturne.On(0.55)));
        bar.AddChild(Text.New("↑↓ select · ⌥↑↓ move · p/r/e/s/f/d action")
            .FontSize(10.5f).TextColor(Nocturne.On(0.38)));
        if (bad)
            bar.AddChild(Flex.Row(gap: 4).AlignItems(AlignItems.Center).Children(
                Ui.Icon(Icons.Warning, 11f, WarnText),
                Text.New("Oldest kept commit can't be squash/fixup").FontSize(11f).TextColor(WarnText)));

        bar.AddChild(Box.New().FlexGrow(1));
        bar.AddChild(Ui.SecondaryButton("Cancel", null, CloseRebase, 12.5f));
        // Inert and dimmed while the oldest row is squash/fixup (nothing older to meld
        // into). One element either way: the enabled state patches across rebuilds.
        var start = Ui.PrimaryButton("Start rebase", Icons.Play, RunRebase, 12.5f).Enabled(!bad);
        if (bad) start.Tooltip("The oldest commit has nothing older to squash into");
        start.Key = "rebase-start";
        bar.AddChild(start);
        return bar;
    }

    // ── Reword sub-dialog ────────────────────────────────────────────────

    private Element BuildRewordDialog(RebaseRow r)
    {
        var backdrop = Box.New()
            .Width(Dimension.Percent(100)).Height(Dimension.Percent(100))
            .Background(new Color(0, 0, 0, 115))
            .JustifyContent(JustifyContent.Center).AlignItems(AlignItems.Center)
            .Padding(new Thickness(24))
            .ZIndex(74);
        backdrop.Position.Type = PositionType.Absolute;
        backdrop.Position.Top = Dimension.Px(0);
        backdrop.Position.Left = Dimension.Px(0);
        backdrop.On<ClickEvent>(e => { e.StopPropagation = true; CloseReword(); });

        var card = Flex.Column(gap: 0)
            .Width(520).MaxWidth(Dimension.Percent(100))
            .Background(Nocturne.Surface)
            .BorderColor(Nocturne.Neutral700).BorderWidth(1f)
            .CornerRadius(Nocturne.RadiusLg);
        card.On<ClickEvent>(e => e.StopPropagation = true);

        // Header.
        var header = Flex.Row(gap: 10).AlignItems(AlignItems.Center)
            .Padding(new Thickness(14, 16, 14, 16))
            .BorderColor(Nocturne.Divider).BorderWidth(new Thickness(0, 0, 1, 0));
        header.AddChild(Box.New().Width(30).Height(30).FlexShrink(0).CornerRadius(Nocturne.RadiusRow)
            .Background(Nocturne.Accent800).JustifyContent(JustifyContent.Center).AlignItems(AlignItems.Center)
            .Children(Ui.Icon(Icons.PencilSimple, 15f, Nocturne.Accent200)));
        var htext = Flex.Column(gap: 1).FlexGrow(1);
        htext.AddChild(Text.New("Reword commit").FontSize(14.5f).FontWeight(FontWeight.Medium));
        htext.AddChild(Ui.Mono(r.ShortHash, 11f, Nocturne.On(0.5)));
        header.AddChild(htext);
        header.AddChild(Ui.IconButton(Icons.X, CloseReword, "Close", 26f));
        card.AddChild(header);

        // Body: label + textarea + hint.
        var body = Flex.Column(gap: 5).Padding(new Thickness(15, 16, 15, 16));
        body.AddChild(Text.New("Commit message").FontSize(11f).TextColor(Nocturne.On(0.55)));
        var taBox = Flex.Column(gap: 0)
            .Padding(new Thickness(6, 10, 6, 10))
            .Background(Nocturne.Bg).BorderColor(Nocturne.Divider).BorderWidth(1f).CornerRadius(Nocturne.RadiusMd);
        var ta = TextArea.New(_rbRewordText);
        ta.OnTextChanged(v => { _rbRewordText = v; MarkDirty(); })
            .Height(Dimension.Px(140))
            .FontFamily(Fonts.Mono).FontSize(12.5f)
            .TextColor(Nocturne.Text).WithRef(_rbRewordRef);
        taBox.AddChild(ta);
        body.AddChild(taBox);
        body.AddChild(Text.New("First line is the summary; leave a blank line before the body.")
            .FontSize(10.5f).TextColor(Nocturne.On(0.4)));
        card.AddChild(body);

        // Actions.
        var actions = Flex.Row(gap: 8).AlignItems(AlignItems.Center)
            .Padding(new Thickness(11, 16, 11, 16))
            .BorderColor(Nocturne.Divider).BorderWidth(new Thickness(1, 0, 0, 0));
        actions.AddChild(Box.New().FlexGrow(1));
        actions.AddChild(Ui.SecondaryButton("Cancel", null, CloseReword, 12.5f));
        actions.AddChild(Ui.PrimaryButton("Save message", Icons.Check, SaveReword, 12.5f));
        card.AddChild(actions);

        backdrop.AddChild(card);
        return backdrop;
    }
}
