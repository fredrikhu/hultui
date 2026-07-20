using HultUI.Core.Builders;
using HultUI.Core.Elements;
using HultUI.Core.Events;
using HultUI.Core.Layout;
using HultUI.Core.Primitives;
using HultUI.Core.Styling;
using GitClient.Git;
using GitClient.Theme;

namespace GitClient.Junction;

public partial class JunctionApp
{
    private const float BranchRowHeight = 56f;

    // Column widths for the branch table (Branch/LastCommit flex; rest fixed).
    private const float ColUpdated = 118f;
    private const float ColAheadBehind = 120f;
    private const float ColInHistory = 96f;
    private const float ColActions = 250f;

    private Element BuildBranchesView()
    {
        if (!HasActiveRepo)
            return BuildStubView("Branches", "Open a repository first.");

        _branches.Load($"{_activeRepoPath}|{DataGen(_activeRepoPath)}", LoadBranches, onLoaded: ClampBranchSel);
        var list = FilterBranches(_branches.Value.Local);
        if (_branchSel >= list.Count) _branchSel = Math.Max(0, list.Count - 1);

        var col = Flex.Column(gap: 0).FlexGrow(1);
        col.AddChild(BuildBranchesHeader());
        if (BranchCompareActive())
            col.AddChild(BuildBranchCompareBar(_branchCompare!.Value));
        col.AddChild(BuildBranchTableHeader());
        col.AddChild(BuildBranchScroll(list));
        col.AddChild(BuildBranchesFooter());
        return col;
    }

    // Runs off the UI thread.
    private BranchesData LoadBranches()
    {
        var repo = RepoFor(_activeRepoPath);
        var all = repo.GetBranches();
        // Local branches first (current pinned to top, then most-recent), then the
        // remote-tracking branches (most-recent). GetBranches already drops the
        // origin/HEAD symbolic ref.
        var local = all.Where(b => !b.IsRemote)
            .OrderByDescending(b => b.IsCurrent)
            .ThenByDescending(b => b.CommitDate);
        var remote = all.Where(b => b.IsRemote)
            .OrderByDescending(b => b.CommitDate);
        return new BranchesData(local.Concat(remote).ToList());
    }

    private static (string Remote, string Leaf) SplitRemote(string name)
    {
        var i = name.IndexOf('/');
        return i > 0 ? (name[..i], name[(i + 1)..]) : ("", name);
    }

    private void ClampBranchSel(BranchesData data)
    {
        var count = FilterBranches(data.Local).Count;
        if (_branchSel >= count) _branchSel = Math.Max(0, count - 1);
    }

    private List<GitBranch> FilterBranches(IReadOnlyList<GitBranch> all)
    {
        var q = _branchQuery.Trim();
        if (q.Length == 0) return all.ToList();
        return all.Where(b =>
            b.Name.Contains(q, StringComparison.OrdinalIgnoreCase) ||
            BranchName.Parse(b.Name).Title.Contains(q, StringComparison.OrdinalIgnoreCase) ||
            b.Subject.Contains(q, StringComparison.OrdinalIgnoreCase)).ToList();
    }

    private List<GitBranch> FilteredBranches() => FilterBranches(_branches.Value.Local);

    // ── Header (title · search · new branch) ─────────────────────────────

    private Element BuildBranchesHeader()
    {
        var bar = Flex.Row(gap: 12).FlexShrink(0).AlignItems(AlignItems.Center)
            .Padding(new Thickness(14, 26, 10, 26));

        bar.AddChild(Text.New("Branches").FontSize(18f).FontWeight(FontWeight.SemiBold));
        bar.AddChild(BuildBranchSearch());
        bar.AddChild(Ui.PrimaryButton("New branch", Icons.Plus, PromptNewBranch, 12.5f));
        return bar;
    }

    // ── Two-branch compare bar (shown while two tips are picked) ──────────

    private Element BuildBranchCompareBar(BranchCompare bc)
    {
        var bar = Flex.Row(gap: 8).FlexShrink(0).AlignItems(AlignItems.Center)
            .Padding(new Thickness(8, 26, 8, 26))
            .Background(Nocturne.Alpha(Nocturne.Accent, 0.10))
            .BorderColor(Nocturne.Divider).BorderWidth(new Thickness(0, 0, 1, 0));

        bar.AddChild(Ui.Icon(Icons.GitDiff, 14f, Nocturne.Accent300).FlexShrink(0));
        bar.AddChild(Text.New("Comparing").FontSize(12.5f).TextColor(Nocturne.On(0.62)).FlexShrink(0));
        bar.AddChild(Ui.Chip(Icons.GitBranch, bc.BaseLabel, Nocturne.Neutral800, Nocturne.Neutral100).FlexShrink(0));
        bar.AddChild(Ui.Icon(Icons.ArrowRight, 12f, Nocturne.On(0.5)).FlexShrink(0));
        bar.AddChild(Ui.Chip(Icons.GitBranch, bc.HeadLabel, Nocturne.Accent800, Nocturne.Accent100).FlexShrink(0));
        bar.AddChild(Box.New().FlexGrow(1));
        bar.AddChild(Ui.PrimaryButton("View diff", Icons.ArrowRight, () => SetView(JunctionView.Commit), 12f).FlexShrink(0));
        bar.AddChild(Ui.GhostButton("Clear", Icons.X, ClearBranchCompare, 12f).FlexShrink(0));
        return bar;
    }

    private Element BuildBranchSearch()
    {
        var box = Flex.Row(gap: 6).AlignItems(AlignItems.Center)
            .Height(34).FlexGrow(1).MaxWidth(420)
            .Padding(new Thickness(0, 10, 0, 10))
            .Background(Nocturne.Bg)
            .BorderColor(Nocturne.Divider).BorderWidth(1f)
            .CornerRadius(Nocturne.RadiusMd);
        box.AddChild(Ui.Icon(Icons.MagnifyingGlass, 12f, Nocturne.On(0.45)).FlexShrink(0));

        var input = TextInput.New(_branchQuery);
        input.PlaceholderText("Press / to search branches…")
            .OnTextChanged(OnBranchQueryChanged)
            .Height(Dimension.Px(30))
            .FlexGrow(1).FlexShrink(1).FlexBasis(Dimension.Px(0))
            .TextColor(Nocturne.Text)
            .WithRef(_branchSearchRef);
        input.On<KeyDownEvent>(BranchSearchKey);
        box.AddChild(input);
        return box;
    }

    private void OnBranchQueryChanged(string q)
    {
        _branchQuery = q;
        _branchSel = 0;
        MarkDirty();
    }

    // ── Table ────────────────────────────────────────────────────────────

    private Element BuildBranchTableHeader()
    {
        var row = Flex.Row(gap: 0).FlexShrink(0).AlignItems(AlignItems.Center)
            .Padding(new Thickness(6, 26, 6, 26))
            .BorderColor(Nocturne.Divider).BorderWidth(new Thickness(0, 0, 1, 0));

        row.AddChild(HeaderCell("BRANCH", flexGrow: 38, align: JustifyContent.FlexStart));
        row.AddChild(HeaderCell("LAST COMMIT", flexGrow: 30, align: JustifyContent.FlexStart));
        row.AddChild(HeaderCell("UPDATED", width: ColUpdated, align: JustifyContent.FlexStart));
        row.AddChild(HeaderCell("AHEAD / BEHIND", width: ColAheadBehind, align: JustifyContent.Center));
        row.AddChild(HeaderCell("IN HISTORY", width: ColInHistory, align: JustifyContent.Center));
        row.AddChild(HeaderCell("ACTIONS", width: ColActions, align: JustifyContent.FlexEnd));
        return row;
    }

    private static Element HeaderCell(string label, float flexGrow = 0, float width = 0,
        JustifyContent align = JustifyContent.FlexStart)
    {
        var box = Flex.Row(gap: 0).JustifyContent(align);
        if (width > 0) box.Width(width).FlexShrink(0);
        else box.FlexGrow(flexGrow).FlexBasis(Dimension.Px(0));
        box.AddChild(Text.New(label).FontSize(10.5f).LetterSpacing(0.6f).TextColor(Nocturne.On(0.55)));
        return box;
    }

    private Element BuildBranchScroll(List<GitBranch> list)
    {
        var scroll = ScrollView.New().FlexGrow(1)
            .Padding(new Thickness(0, 26, 12, 26));
        scroll.WithRef(_branchScrollRef);

        if (list.Count == 0)
        {
            scroll.AddChild(Box.New().Padding(new Thickness(26)).JustifyContent(JustifyContent.Center)
                .AlignItems(AlignItems.Center)
                .Children(Text.New(_branchQuery.Trim().Length > 0
                        ? $"No branches match \"{_branchQuery.Trim()}\""
                        : "No local branches")
                    .FontSize(13f).TextColor(Nocturne.On(0.45))));
            return scroll;
        }

        for (var i = 0; i < list.Count; i++)
            scroll.AddChild(BranchRow(list[i], i));
        return scroll;
    }

    private Element BranchRow(GitBranch b, int index)
    {
        // Rows in the compare selection are highlighted; the base/head extremes tagged.
        var comparePick = _selectedBranches.Contains(b.FullName);
        var compareRole = _branchCompare is { } bc0
            ? (b.FullName == bc0.BaseFull ? "base" : b.FullName == bc0.HeadFull ? "head" : null)
            : null;
        // The focus caret (Ctrl+arrows) can sit outside the selection. Same colours
        // as the History list: the selection is accent, the caret a neutral tint.
        var focused = index == _branchSel;
        var (remoteName, leaf) = b.IsRemote ? SplitRemote(b.Name) : ("", b.Name);
        var pb = BranchName.Parse(leaf);

        var row = Flex.Row(gap: 0).AlignItems(AlignItems.Center)
            .Height(BranchRowHeight).FlexShrink(0)
            .Padding(new Thickness(0, 26, 0, 26))
            .CornerRadius(Nocturne.RadiusRow)
            .BorderColor(Nocturne.On(0.06)).BorderWidth(new Thickness(0, 0, 1, 0))
            .Cursor(CursorType.Pointer);
        // Focus + compare-pick in the key so the highlight/handlers refresh (handlers
        // aren't copied on patch — a stale one would otherwise clobber the background).
        row.Key = b.Name + (focused ? "#f" : "") + (comparePick ? "#c" + compareRole : "");
        row.Background(comparePick ? Nocturne.Accent900
            : focused ? Nocturne.Neutral900
            : Color.Transparent);

        // Branch cell: chip + leaf title + current/remote tag + version·team subtitle.
        var branchCell = Flex.Row(gap: 9).AlignItems(AlignItems.Center)
            .FlexGrow(38).FlexBasis(Dimension.Px(0));
        var (glyph, chipBg, chipFg) = pb.ChipLabel switch
        {
            "FEAT" => (Icons.GitBranch, Nocturne.Accent800, Nocturne.Accent100),
            "WIP" => (Icons.Flask, Nocturne.Accent2_800, Nocturne.Accent2_100),
            _ => (Icons.GitBranch, Nocturne.Neutral800, Nocturne.Neutral100),
        };
        branchCell.AddChild(Ui.Chip(glyph, pb.ChipLabel, chipBg, chipFg));

        var titleCol = Flex.Column(gap: 1).FlexShrink(1).FlexBasis(Dimension.Px(0)).FlexGrow(1);
        var titleRow = Flex.Row(gap: 6).AlignItems(AlignItems.Center);
        titleRow.AddChild(Ui.Mono(pb.Title, 12.5f, b.IsRemote ? Nocturne.On(0.72) : Nocturne.Text)
            .FlexShrink(1).OverflowMode(TextOverflow.Ellipsis).MaxLines(1));
        if (compareRole != null)
            titleRow.AddChild(Ui.TagOutline(compareRole, Nocturne.Accent, 9f).FlexShrink(0));
        if (b.IsCurrent)
            titleRow.AddChild(Ui.TagOutline("current", Nocturne.Accent, 9f));
        if (b.IsRemote)
            titleRow.AddChild(Ui.Chip(Icons.Cloud, remoteName, Nocturne.Neutral800, Nocturne.Neutral300));
        titleCol.AddChild(titleRow);
        if (pb.HasSub)
            titleCol.AddChild(Text.New(pb.Sub).FontSize(10.5f).TextColor(Nocturne.On(0.42))
                .OverflowMode(TextOverflow.Ellipsis).MaxLines(1));
        branchCell.AddChild(titleCol);
        row.AddChild(branchCell);

        // Last commit + updated.
        row.AddChild(Box.New().FlexGrow(30).FlexBasis(Dimension.Px(0)).Padding(new Thickness(0, 8, 0, 0))
            .Children(Text.New(b.Subject.Length > 0 ? b.Subject : "—").FontSize(12.5f)
                .TextColor(Nocturne.On(0.68)).OverflowMode(TextOverflow.Ellipsis).MaxLines(1)));
        row.AddChild(Box.New().Width(ColUpdated).FlexShrink(0)
            .Children(Text.New(b.CommitDate == default ? "" : RelativeTime(b.CommitDate))
                .FontSize(12f).TextColor(Nocturne.On(0.55))));

        // Ahead / behind (remote-tracking refs have no upstream of their own).
        var ab = Flex.Row(gap: 4).Width(ColAheadBehind).FlexShrink(0)
            .JustifyContent(JustifyContent.Center).AlignItems(AlignItems.Center);
        if (b.IsRemote)
            ab.AddChild(Text.New("—").FontSize(12f).TextColor(Nocturne.On(0.30)));
        else
        {
            ab.AddChild(Text.New($"↑{b.AheadCount}").FontSize(12f).TextColor(Nocturne.AddCount));
            ab.AddChild(Text.New("/").FontSize(12f).TextColor(Nocturne.On(0.40)));
            ab.AddChild(Text.New($"↓{b.BehindCount}").FontSize(12f).TextColor(Nocturne.DelCount));
        }
        row.AddChild(ab);

        // In history (eye toggle) — keyed on the full ref name, so it works for
        // remote branches too (git log accepts origin/<branch> as a ref).
        var inHist = Flex.Row(gap: 0).Width(ColInHistory).FlexShrink(0)
            .JustifyContent(JustifyContent.Center);
        inHist.AddChild(IncludeToggle(b.Name));
        row.AddChild(inHist);

        // Actions.
        row.AddChild(BranchActions(b, leaf));

        row.OnClick(e => OnBranchRowClick(b, index, e.Modifiers));
        // Right-click selects the row first, then the framework opens the menu.
        row.On<PointerDownEvent>(e => { if (e.Button == 2) SelectBranch(index); });
        row.ContextMenu(() => BranchMenuItems(b));
        return row;
    }

    private Element IncludeToggle(string name)
    {
        var set = IncludeSet(_activeRepoPath);
        var explicitInc = set.Contains(name);
        var included = set.Count == 0 || explicitInc;

        var btn = Flex.Row(gap: 0).JustifyContent(JustifyContent.Center).AlignItems(AlignItems.Center)
            .Width(30).Height(30).CornerRadius(Nocturne.RadiusRow)
            .Cursor(CursorType.Pointer)
            .Tooltip(included ? "Exclude from history (i)" : "Include in history (i)");
        if (explicitInc)
            btn.Background(Nocturne.Alpha(Nocturne.Accent, 0.12))
               .BorderColor(Nocturne.Accent).BorderWidth(1f);
        btn.AddChild(Ui.Icon(included ? Icons.Eye : Icons.EyeSlash, 15f,
            included ? Nocturne.Accent300 : Nocturne.On(0.40)));
        // Key by state so the toggle refreshes its handler/appearance on change.
        btn.Key = "inc:" + name + (explicitInc ? "#e" : included ? "#i" : "#x");
        btn.OnClick(e => { e.StopPropagation = true; ToggleInclude(name); });
        return btn;
    }

    private Element BranchActions(GitBranch b, string checkoutTarget)
    {
        var box = Flex.Row(gap: 5).Width(ColActions).FlexShrink(0)
            .JustifyContent(JustifyContent.FlexEnd).AlignItems(AlignItems.Center);

        if (b.IsCurrent)
            box.AddChild(DisabledSecondary("Checkout", Icons.BendDownRight, 11.5f));
        else
            box.AddChild(Ui.SecondaryButton("Checkout", Icons.BendDownRight,
                () => CheckoutBranch(checkoutTarget), 11.5f));

        // Push / Pull / Delete apply to local branches; a remote-tracking ref
        // is checked out (creating a local tracking branch) but not managed here.
        if (!b.IsRemote)
        {
            box.AddChild(SmallIconButton(Icons.ArrowUp, () => PushBranch(b), "Push (p)"));
            box.AddChild(SmallIconButton(Icons.ArrowDown, () => PullBranch(b), "Pull (l)"));
            box.AddChild(SmallIconButton(Icons.Trash, () => DeleteBranchAction(b), "Delete"));
        }
        return box;
    }

    private static Element SmallIconButton(string glyph, Action onClick, string tooltip)
    {
        var b = Ui.IconButton(glyph, onClick, tooltip, 30f);
        // Icon buttons in a row shouldn't also trigger the row's select handler.
        b.OnClick(e => e.StopPropagation = true);
        return b;
    }

    private static Element DisabledSecondary(string label, string glyph, float fontSize)
    {
        var b = Flex.Row(gap: 6).AlignItems(AlignItems.Center).JustifyContent(JustifyContent.Center)
            .Padding(new Thickness(5, 10, 5, 10)).CornerRadius(Nocturne.RadiusMd)
            .BorderColor(Nocturne.Divider).BorderWidth(1f);
        b.AddChild(Ui.Icon(glyph, fontSize + 1f, Nocturne.On(0.32)));
        b.AddChild(Text.New(label).FontSize(fontSize).FontWeight(FontWeight.Medium).TextColor(Nocturne.On(0.32)));
        return b;
    }

    // ── Footer legend ────────────────────────────────────────────────────

    private Element BuildBranchesFooter()
    {
        var bar = Flex.Row(gap: 16).FlexShrink(0).AlignItems(AlignItems.Center)
            .Padding(new Thickness(8, 26, 8, 26))
            .BorderColor(Nocturne.Divider).BorderWidth(new Thickness(1, 0, 0, 0));
        bar.AddChild(Hint("/", "search"));
        bar.AddChild(Hint("↑ ↓", "navigate"));
        bar.AddChild(Hint("↵", "checkout"));
        bar.AddChild(Hint("p", "push"));
        bar.AddChild(Hint("l", "pull"));
        bar.AddChild(Hint("i", "include in history"));
        return bar;
    }

    // ── Keyboard ─────────────────────────────────────────────────────────

    private bool HandleBranchKey(KeyDownEvent e)
    {
        if (HandleBranchNavKey(e)) return true;
        switch (e.Key)
        {
            case KeyCode.C: CheckoutSelected(); return true;
            case KeyCode.P: PushSelected(); return true;
            case KeyCode.L: PullSelected(); return true;
            case KeyCode.I: ToggleIncludeSelected(); return true;
            default: return false;
        }
    }

    // Nav subset that also drives the list from inside the search box.
    private bool HandleBranchNavKey(KeyDownEvent e)
    {
        // Windows-list modifiers: plain nav single-selects, Ctrl moves the focus
        // caret only, Ctrl+Space toggles the focused branch in the compare selection.
        var focusOnly = e.Modifiers.HasFlag(KeyModifiers.Ctrl);
        switch (e.Key)
        {
            case KeyCode.Down: MoveBranchSel(1, focusOnly); return true;
            case KeyCode.Up: MoveBranchSel(-1, focusOnly); return true;
            case KeyCode.PageDown: MoveBranchSel(6, focusOnly); return true;
            case KeyCode.PageUp: MoveBranchSel(-6, focusOnly); return true;
            case KeyCode.Home: MoveBranchSel(int.MinValue / 2, focusOnly); return true;
            case KeyCode.End: MoveBranchSel(int.MaxValue / 2, focusOnly); return true;
            case KeyCode.Space when focusOnly: ToggleFocusedBranch(); return true;
            case KeyCode.Enter: CheckoutSelected(); return true;
            default: return false;
        }
    }

    private void BranchSearchKey(KeyDownEvent e)
    {
        // Let Alt-modified keys bubble to the global repo/workspace shortcuts. Ctrl
        // bubbles too, except the Windows-list keys (Ctrl+↑/↓ moves the focus caret,
        // Ctrl+Space toggles the branch in the compare selection) which the list
        // handles even while the search box is focused — so a comparison can be
        // built keyboard-only from here.
        if (e.Modifiers.HasFlag(KeyModifiers.Alt)) return;
        if (e.Modifiers.HasFlag(KeyModifiers.Ctrl)
            && e.Key is not (KeyCode.Up or KeyCode.Down or KeyCode.Space))
            return;
        if (e.Key == KeyCode.Escape)
        {
            _branchQuery = "";
            _branchSel = 0;
            MarkDirty();
            e.StopPropagation = true;
            return;
        }
        // Home/End (± Shift) and Shift+PageUp/Down belong to the text box — they move
        // the caret or select within the query. Plain PageUp/Down page the list.
        if (IsFieldEditKey(e)) return;
        if (HandleBranchNavKey(e))
            e.StopPropagation = true;
    }

    private void SelectBranch(int index)
    {
        _branchSel = index;
        MarkDirty();
    }

    /// <summary>Click on a branch row. Plain click single-selects the branch (its tip
    /// commit); Ctrl/Cmd-click toggles it in the compare selection. Two (or more)
    /// selected branches diff between the oldest and newest tip — all without leaving
    /// the Branches tab.</summary>
    private void OnBranchRowClick(GitBranch b, int index, KeyModifiers mods)
    {
        _branchSel = index;
        if (mods.HasFlag(KeyModifiers.Ctrl) || mods.HasFlag(KeyModifiers.Super))
            BranchToggle(b);
        else
            BranchSelectSingle(b);
        MarkDirty();
    }

    private void BranchSelectSingle(GitBranch b)
    {
        _selectedBranches.Clear();
        _selectedBranches.Add(b.FullName);
        OnBranchSelectionChanged(b);
    }

    private void BranchToggle(GitBranch b)
    {
        if (!_selectedBranches.Add(b.FullName)) _selectedBranches.Remove(b.FullName);
        OnBranchSelectionChanged(b);
    }

    private void ToggleFocusedBranch()
    {
        if (SelectedBranch() is { } b) { BranchToggle(b); MarkDirty(); }
    }

    /// <summary>After a selection change: recompute the pair and keep the commit
    /// selection coherent — the focused branch's tip drives the single-commit view,
    /// and the History multi-select is cleared so no stale range lingers under a
    /// single-branch (no-compare) selection.</summary>
    private void OnBranchSelectionChanged(GitBranch focus)
    {
        RecomputeBranchCompare();
        _selectedCommitHash = focus.Hash;
        _selectedCommitHashes.Clear();
    }

    /// <summary>Caches the oldest→newest tip pair (base = older, head = newer) when ≥2
    /// branches are selected, else clears it. Ordered by identity, so two branches at
    /// the same commit still resolve to a (base, head) pair the compare renders as a
    /// "no differences" state.</summary>
    private void RecomputeBranchCompare()
    {
        var sel = _branches.Value.Local
            .Where(b => _selectedBranches.Contains(b.FullName))
            .OrderBy(b => b.CommitDate).ToList();
        if (sel.Count < 2) { _branchCompare = null; return; }
        var lo = sel[0]; var hi = sel[^1];
        _branchCompare = new BranchCompare(lo.FullName, hi.FullName, lo.Hash, hi.Hash, lo.Name, hi.Name);
    }

    /// <summary>Ends the compare, clearing the picked branches.</summary>
    private void ClearBranchCompare()
    {
        EndBranchCompare();
        _selectedCommitHashes.Clear();
        MarkDirty();
    }

    private void MoveBranchSel(int delta, bool focusOnly = false)
    {
        var list = FilteredBranches();
        if (list.Count == 0) return;
        _branchSel = Math.Clamp(_branchSel + delta, 0, list.Count - 1);
        if (!focusOnly) BranchSelectSingle(list[_branchSel]);   // plain nav single-selects
        EnsureBranchVisible();
        MarkDirty();
    }

    private void EnsureBranchVisible()
    {
        // Fixed row height, so the selected row's content-space band needs no
        // per-row element — index * height locates it directly.
        _branchScrollRef.Current?.ScrollRangeIntoView(_branchSel * BranchRowHeight, BranchRowHeight);
    }

    private GitBranch? SelectedBranch()
    {
        var list = FilteredBranches();
        return _branchSel >= 0 && _branchSel < list.Count ? list[_branchSel] : null;
    }

    private void CheckoutSelected()
    {
        if (SelectedBranch() is { IsCurrent: false } b)
            CheckoutBranch(b.IsRemote ? SplitRemote(b.Name).Leaf : b.Name);
    }

    // Push / Pull only apply to local branches (remote rows silently ignore them).
    private void PushSelected() { if (SelectedBranch() is { IsRemote: false } b) PushBranch(b); }
    private void PullSelected() { if (SelectedBranch() is { IsRemote: false } b) PullBranch(b); }
    private void ToggleIncludeSelected() { if (SelectedBranch() is { } b) ToggleInclude(b.Name); }

    // ── Actions ──────────────────────────────────────────────────────────

    private void ToggleInclude(string name)
    {
        var set = IncludeSet(_activeRepoPath);
        if (!set.Remove(name)) set.Add(name);
        BumpDataGen(_activeRepoPath);   // History reloads with the new include set
        MarkDirty();
    }

    // CheckoutBranch(string) is shared with the command palette (JunctionApp.Palette.cs).

    private async void PushBranch(GitBranch b)
    {
        if (ActiveRepo is not { } repo || _remoteOpRunning) return;
        var (remote, _) = UpstreamOf(b);
        _remoteOpRunning = true;
        Flash($"Pushing {b.Name}…");
        var res = await repo.PushToAsync(remote, b.Name, NoAskPass);
        _remoteOpRunning = false;
        Flash(res.Success ? $"Pushed {b.Name}" : FirstLine(res.Stderr, "Push failed"));
        RefreshAll();
    }

    private async void PullBranch(GitBranch b)
    {
        if (ActiveRepo is not { } repo || _remoteOpRunning) return;
        _remoteOpRunning = true;
        Flash($"Pulling {b.Name}…");
        GitResult res;
        if (b.IsCurrent)
        {
            res = await repo.PullAsync(NoAskPass, new PullOptions
            {
                Strategy = _settings.Git.PullStrategy,
                AutoStash = _settings.Git.PullAutoStash,
                Prune = _settings.Git.PruneOnFetch,
            });
        }
        else
        {
            // Fast-forward the non-current branch to its upstream without checkout.
            var (remote, remoteBranch) = UpstreamOf(b);
            res = await repo.FastForwardBranchAsync(remote, remoteBranch, b.Name, NoAskPass);
        }
        _remoteOpRunning = false;
        Flash(res.Success ? $"Pulled {b.Name}" : FirstLine(res.Stderr, "Pull failed"));
        RefreshAll();
    }

    private void DeleteBranchAction(GitBranch b)
    {
        if (ActiveRepo is not { } repo) return;
        if (b.IsCurrent) { Flash("Can't delete the checked-out branch"); return; }
        // Non-force delete: git refuses to drop an unmerged branch, which keeps this safe.
        var res = repo.DeleteBranch(b.Name);
        if (res.Success)
        {
            IncludeSet(_activeRepoPath).Remove(b.Name);
            Flash($"Deleted {b.Name}");
        }
        else
        {
            Flash(FirstLine(res.Stderr, "Delete failed (branch not fully merged?)"));
        }
        RefreshAll();
    }

    private static (string Remote, string RemoteBranch) UpstreamOf(GitBranch b)
    {
        if (!string.IsNullOrEmpty(b.TrackingBranch))
        {
            var i = b.TrackingBranch!.IndexOf('/');
            if (i > 0) return (b.TrackingBranch[..i], b.TrackingBranch[(i + 1)..]);
        }
        return ("origin", b.Name);
    }
}
