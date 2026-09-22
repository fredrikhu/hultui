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
    // A flat row can carry a second line (the version · team subtitle); inside a folder
    // that line is what the folder rows already say, so the tree's rows are one line and
    // pack tighter. The list wants one height for every row, and this is it.
    private const float BranchRowFlat = 56f;
    private const float BranchRowTree = 44f;

    private float BranchRowHeight => _refTreeView ? BranchRowTree : BranchRowFlat;

    // Column widths for the branch table (Branch/LastCommit flex; rest fixed).
    private const float ColUpdated = 118f;
    private const float ColAheadBehind = 120f;
    private const float ColInHistory = 96f;
    private const float ColActions = 250f;
    // One template for the header and every leaf row, so the columns line up by
    // construction rather than by each row repeating the same widths.
    private static readonly string BranchColumns = FormattableString.Invariant(
        $"38fr 30fr {ColUpdated}px {ColAheadBehind}px {ColInHistory}px {ColActions}px");

    private Element BuildBranchesView()
    {
        if (!HasActiveRepo)
            return BuildStubView("Branches", "Open a repository first.");

        // No load of its own: the list rides on the repo summary (see ActiveBranches).
        var rows = BranchRows();
        if (_branchSel >= rows.Count) _branchSel = Math.Max(0, rows.Count - 1);

        var col = Flex.Column(gap: 0).FlexGrow(1);
        col.AddChild(BuildBranchesHeader());
        if (BranchCompareActive())
            col.AddChild(BuildBranchCompareBar(_branchCompare!.Value));
        col.AddChild(BuildBranchTableHeader());
        col.AddChild(BuildBranchScroll(rows));
        return col;
    }

    /// <summary>The active repository's branches in display order. The list comes from the
    /// repo summary — the same <c>for-each-ref</c> the sidebar badge needs, asked for once —
    /// so this tab is as warm as the sidebar, for every repository it shows.</summary>
    private IReadOnlyList<GitBranch> ActiveBranches => BranchesFor(_activeRepoPath);

    private IReadOnlyList<GitBranch> BranchesFor(string path)
    {
        var all = SummaryFor(path).Branches;
        if (ReferenceEquals(all, _sortedBranches.Source)) return _sortedBranches.Sorted;
        // Local branches first (current pinned to top, then most-recent), then the
        // remote-tracking branches (most-recent). GetBranches already drops the
        // origin/HEAD symbolic ref.
        var local = all.Where(b => !b.IsRemote)
            .OrderByDescending(b => b.IsCurrent)
            .ThenByDescending(b => b.CommitDate);
        var remote = all.Where(b => b.IsRemote)
            .OrderByDescending(b => b.CommitDate);
        var sorted = local.Concat(remote).ToList();
        _sortedBranches = (all, sorted);
        return sorted;
    }

    private static (string Remote, string Leaf) SplitRemote(string name)
    {
        var i = name.IndexOf('/');
        return i > 0 ? (name[..i], name[(i + 1)..]) : ("", name);
    }

    /// <summary>The branches the query keeps. A plain query is a substring, matched against
    /// the ref name, its leaf title and the last commit's subject. A query carrying
    /// <c>*</c> or <c>?</c> is a wildcard pattern matched against the whole ref name and
    /// its title instead — <c>feature/*</c>, <c>*-hotfix</c>, <c>PC-1?4-*</c> — the same
    /// thing <c>git branch --list &lt;pattern&gt;</c> means by it. Commit subjects are left
    /// out of that: a pattern anchored to both ends of a sentence never matches one.
    /// </summary>
    private List<GitBranch> FilterBranches(IReadOnlyList<GitBranch> all)
    {
        var q = _branchQuery.Trim();
        if (q.Length == 0) return all.ToList();
        if (Glob.HasWildcard(q))
            return all.Where(b =>
                Glob.IsMatch(b.Name, q) ||
                Glob.IsMatch(BranchName.Parse(b.Name).Title, q)).ToList();
        return all.Where(b =>
            b.Name.Contains(q, StringComparison.OrdinalIgnoreCase) ||
            BranchName.Parse(b.Name).Title.Contains(q, StringComparison.OrdinalIgnoreCase) ||
            b.Subject.Contains(q, StringComparison.OrdinalIgnoreCase)).ToList();
    }

    /// <summary>The rows the table shows: the filtered branches grouped into the expanded
    /// tree, or the same branches flat. Remote-tracking refs carry their remote as the first
    /// name segment (<c>origin/feature/x</c>), so the tree groups them under it for free —
    /// and the folder says so with a cloud, because every ref beneath it is remote.</summary>
    private List<TreeNode<GitBranch>> BranchRowsOf(IReadOnlyList<GitBranch> all) =>
        RefRows(FilterBranches(all), RefKindBranch, b => b.Name, b => b.IsRemote,
            filtering: _branchQuery.Trim().Length > 0);

    private List<TreeNode<GitBranch>> BranchRows() => BranchRowsOf(ActiveBranches);

    // ── Header (title · search · new branch) ─────────────────────────────

    private Element BuildBranchesHeader()
    {
        var bar = Flex.Row(gap: 12).FlexShrink(0).AlignItems(AlignItems.Center)
            .Padding(new Thickness(14, 26, 10, 26));

        bar.AddChild(Text.New("Branches").FontSize(18f).FontWeight(FontWeight.SemiBold));
        bar.AddChild(BuildBranchSearch());
        bar.AddChild(RefViewToggle());
        bar.AddChild(Ui.PrimaryButton("New branch", Icons.Plus, PromptNewBranch, 12.5f));
        // Worktrees are branch-adjacent — the branch context menu already offers to create
        // one — and until now the panel could only be reached by typing into the palette,
        // which is not where anyone looks for "this branch, checked out somewhere else".
        bar.AddChild(Ui.SecondaryButton("Worktrees", Icons.TreeStructure, OpenWorktrees, 12.5f));
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
        input.PlaceholderText("Press / to search branches — * and ? match…")
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
        var row = Grid.Of(BranchColumns).FlexShrink(0).AlignItems(AlignItems.Center)
            .Padding(new Thickness(6, 26, 6, 26))
            .BorderColor(Nocturne.Divider).BorderWidth(new Thickness(0, 0, 1, 0));

        row.AddChild(HeaderCell("BRANCH", 0, JustifyContent.FlexStart));
        row.AddChild(HeaderCell("LAST COMMIT", 1, JustifyContent.FlexStart));
        row.AddChild(HeaderCell("UPDATED", 2, JustifyContent.FlexStart));
        row.AddChild(HeaderCell("AHEAD / BEHIND", 3, JustifyContent.Center));
        row.AddChild(HeaderCell("IN HISTORY", 4, JustifyContent.Center));
        row.AddChild(HeaderCell("ACTIONS", 5, JustifyContent.FlexEnd));
        return row;
    }

    /// <summary>A table header label in its grid column. The cell is stretched to the
    /// track, so the label's alignment within it is a flex justification.</summary>
    private static Element HeaderCell(string label, int column, JustifyContent align)
    {
        var box = Flex.Row(gap: 0).JustifyContent(align).GridColumn(column);
        box.AddChild(Text.New(label).FontSize(10.5f).LetterSpacing(0.6f).TextColor(Nocturne.On(0.55)));
        return box;
    }

    private Element BuildBranchScroll(List<TreeNode<GitBranch>> rows)
    {
        if (rows.Count == 0)
        {
            return Box.New().FlexGrow(1).Padding(new Thickness(26))
                .JustifyContent(JustifyContent.Center).AlignItems(AlignItems.Center)
                .Children(Text.New(_branchQuery.Trim().Length > 0
                        ? $"No branches match \"{_branchQuery.Trim()}\""
                        : "No local branches")
                    .FontSize(13f).TextColor(Nocturne.On(0.45)));
        }

        // Virtualized: with 1000+ branches a plain ScrollView costs hundreds of ms
        // per rebuild (every row built and measured); the virtual list realizes only
        // the visible ~20. The tree feeds it the same way — one flat row list of the
        // expanded nodes — so grouping costs a row per open folder, never a subtree.
        // The template gets the row alone, so rows find their index through this map
        // (nodes are rebuilt per build, so identity is the natural key — and unlike a
        // name it can't collide when a ref is also a folder prefix).
        var indexOf = new Dictionary<TreeNode<GitBranch>, int>(rows.Count);
        for (var i = 0; i < rows.Count; i++) indexOf[rows[i]] = i;

        return VirtualListElement<TreeNode<GitBranch>>.New()
            .ItemHeight(BranchRowHeight)
            .Items(rows)
            .RenderWith(n => BranchRow(n, indexOf[n]))
            // The virtual list surfaces items to assistive tech by this label alone
            // (rows off-screen are never realized as elements), so it says what the
            // row says: name, current marker, ahead/behind, last commit.
            .AccessibleItemLabel(n => n.IsFolder ? RefFolderLabel(RefKindBranch, n) : BranchLabel(n.Item))
            .OnActivateItem(n =>
            {
                // Assistive-tech activation mirrors a plain row click.
                if (indexOf.TryGetValue(n, out var i)) _branchSel = i;
                if (n.IsFolder) ToggleFolder(RefKindBranch, n);
                else BranchSelectSingle(n.Item);
                MarkDirty();
            })
            .SelectedIndex(_branchSel)
            .FlexGrow(1)
            // No side padding here. A scroll view draws its scrollbar against the inside
            // edge of its *padding* box, so a right pad parks the bar 26px shy of the
            // panel edge with a strip of nothing beside it. The rows and the table header
            // carry the 26px gutter themselves — which is also what lines their columns up.
            .Padding(new Thickness(0, 0, 12, 0))
            .WithRef(_branchScrollRef);
    }

    private static string BranchLabel(GitBranch b) => b.IsRemote
        ? $"{b.Name}, remote, {b.Subject}"
        : $"{b.Name}{(b.IsCurrent ? ", current" : "")}, ahead {b.AheadCount} behind {b.BehindCount}, {b.Subject}";

    private Element BranchRow(TreeNode<GitBranch> node, int index)
    {
        var focused = index == _branchSel;
        return node.IsFolder
            ? RefFolderRow(RefKindBranch, node, index, focused, BranchRowHeight, SelectBranch)
            : BranchLeafRow(node.Item, node.Depth, index, focused);
    }

    private Element BranchLeafRow(GitBranch b, int depth, int index, bool focused)
    {
        // Rows in the compare selection are highlighted; the base/head extremes tagged.
        var comparePick = _selectedBranches.Contains(b.FullName);
        var compareRole = _branchCompare is { } bc0
            ? (b.FullName == bc0.BaseFull ? "base" : b.FullName == bc0.HeadFull ? "head" : null)
            : null;
        // The focus caret (Ctrl+arrows) can sit outside the selection. Same colours
        // as the History list: the selection is accent, the caret a neutral tint.
        var (remoteName, leaf) = b.IsRemote ? SplitRemote(b.Name) : ("", b.Name);
        var pb = BranchName.Parse(leaf);
        // Inside a folder, the rows above already carry the whole prefix — the remote, the
        // kind, the version, the team. Repeating them per row is what made the tree twice
        // the height of the information in it, so a nested row is the leaf and nothing else.
        var inFolder = depth > 0;

        // A 1fr row track: the row has a fixed height, and the cells centre within the
        // track, so the track has to be the whole row rather than hug the tallest cell.
        var row = Grid.Of(BranchColumns, rows: "1fr").AlignItems(AlignItems.Center)
            .Height(BranchRowHeight).FlexShrink(0)
            .Padding(new Thickness(0, 26, 0, 26))
            .CornerRadius(Nocturne.RadiusRow)
            .BorderColor(Nocturne.On(0.06)).BorderWidth(new Thickness(0, 0, 1, 0))
            .Cursor(CursorType.Pointer);
        // Focus + compare-pick in the key so a state flip replaces the row outright:
        // the hover baseline differs per state, and a fresh element can't be left
        // showing the other state's hover tint.
        row.Key = b.Name + (focused ? "#f" : "") + (comparePick ? "#c" + compareRole : "");
        row.Background(comparePick ? Nocturne.Accent900
            : focused ? Nocturne.Neutral900
            : Color.Transparent);

        // Branch cell: chip + leaf title + current/remote tag + version·team subtitle.
        // The tree's indent lives inside this cell so every column right of it stays
        // aligned however deep the ref sits.
        var branchCell = Flex.Row(gap: 9).AlignItems(AlignItems.Center).GridColumn(0);
        if (depth > 0)
            branchCell.AddChild(Box.New().Width(depth * RefIndent).FlexShrink(0));
        if (inFolder)
        {
            // A plain branch glyph in the chip's place, so the leaf rows still start on a
            // common gutter and read as refs rather than as more folders.
            branchCell.AddChild(Ui.Icon(b.IsRemote ? Icons.Cloud : Icons.GitBranch, 12f,
                Nocturne.On(0.45)).FlexShrink(0));
        }
        else
        {
            var (glyph, chipBg, chipFg) = pb.ChipLabel switch
            {
                "FEAT" => (Icons.GitBranch, Nocturne.Accent800, Nocturne.Accent100),
                "WIP" => (Icons.Flask, Nocturne.Accent2_800, Nocturne.Accent2_100),
                _ => (Icons.GitBranch, Nocturne.Neutral800, Nocturne.Neutral100),
            };
            branchCell.AddChild(Ui.Chip(glyph, pb.ChipLabel, chipBg, chipFg));
        }

        var titleCol = Flex.Column(gap: 1).FlexShrink(1).FlexBasis(Dimension.Px(0)).FlexGrow(1);
        var titleRow = Flex.Row(gap: 6).AlignItems(AlignItems.Center);
        titleRow.AddChild(Ui.Mono(pb.Title, 12.5f, b.IsRemote ? Nocturne.On(0.72) : Nocturne.Text)
            .FlexShrink(1).OverflowMode(TextOverflow.Ellipsis).MaxLines(1));
        if (compareRole != null)
            titleRow.AddChild(Ui.TagOutline(compareRole, Nocturne.Accent, 9f).FlexShrink(0));
        if (b.IsCurrent)
            titleRow.AddChild(Ui.TagOutline("current", Nocturne.Accent, 9f));
        // The remote's name is the folder this row sits under, so only a flat row says it.
        if (b.IsRemote && !inFolder)
            titleRow.AddChild(Ui.Chip(Icons.Cloud, remoteName, Nocturne.Neutral800, Nocturne.Neutral300));
        titleCol.AddChild(titleRow);
        if (pb.HasSub && !inFolder)
            titleCol.AddChild(Text.New(pb.Sub).FontSize(10.5f).TextColor(Nocturne.On(0.42))
                .OverflowMode(TextOverflow.Ellipsis).MaxLines(1));
        branchCell.AddChild(titleCol);
        row.AddChild(branchCell);

        // Last commit + updated. On a GitLab-backed repository the cell also carries what
        // the host knows about the branch — lock · subject · pipeline glyph · !iid chip —
        // in the cell rather than a column of its own, per the design's answer.
        var lastCommit = Flex.Row(gap: 7).AlignItems(AlignItems.Center).GridColumn(1)
            .Padding(new Thickness(0, 12, 0, 0));
        bool hosted = GitLabActive(_activeRepoPath);
        if (hosted && !b.IsRemote && ProtectionFor(_activeRepoPath, b.Name) is { } rule)
            lastCommit.AddChild(Ui.Icon(Icons.Lock, 11f, Nocturne.On(0.5)).FlexShrink(0)
                .Tooltip(ProtectionSummary(rule)));
        // The subject takes the slack, so the glyph and the chip sit flush right in the cell
        // — one column of marks down the list, as the design draws it — rather than trailing
        // each subject at whatever width it happens to have.
        lastCommit.AddChild(Text.New(b.Subject.Length > 0 ? b.Subject : "—").FontSize(12.5f)
            .TextColor(Nocturne.On(0.68)).OverflowMode(TextOverflow.Ellipsis).MaxLines(1)
            .FlexGrow(1).FlexShrink(1).FlexBasis(Dimension.Px(0)));
        if (hosted)
        {
            if (PipelineForSha(_activeRepoPath, b.Hash) is { } pipe && Ui.PipelineGlyph(pipe.Status, 13f) is { } glyph)
                lastCommit.AddChild(glyph.FlexShrink(0).Tooltip(PipelineSummary(pipe)));
            // By leaf for a remote-tracking row: after a clone the branch an MR comes from
            // usually exists only as origin/<name>, and that row is where the chip is wanted.
            if (MergeRequestForBranch(_activeRepoPath, b.IsRemote ? leaf : b.Name) is { } mr)
                lastCommit.AddChild(MergeRequestChip(mr).FlexShrink(0));
        }
        row.AddChild(lastCommit);
        row.AddChild(Box.New().GridColumn(2)
            .Children(Text.New(b.CommitDate == default ? "" : b.CommitDate.ToString("g"))
                .FontSize(12f).TextColor(Nocturne.On(0.55))));

        // Ahead / behind (remote-tracking refs have no upstream of their own).
        var ab = Flex.Row(gap: 4).GridColumn(3)
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
        var inHist = Flex.Row(gap: 0).GridColumn(4).JustifyContent(JustifyContent.Center);
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
        var box = Flex.Row(gap: 5).GridColumn(5)
            .JustifyContent(JustifyContent.FlexEnd).AlignItems(AlignItems.Center);

        // The current branch's Checkout is inert and dimmed: already here.
        box.AddChild(Ui.SecondaryButton("Checkout", Icons.BendDownRight,
            () => CheckoutBranch(checkoutTarget), 11.5f).Enabled(!b.IsCurrent));

        // Push / Pull / Delete apply to local branches; a remote-tracking ref
        // is checked out (creating a local tracking branch) but not managed here.
        if (!b.IsRemote)
        {
            // A merge request for a branch that has none yet, on a GitLab-backed
            // repository; the default branch is the target, never the source.
            if (GitLabActive(_activeRepoPath)
                && b.Name != (ProjectFor(_activeRepoPath).Value?.Project.DefaultBranch ?? "main")
                && MergeRequestForBranch(_activeRepoPath, b.Name) is null)
                box.AddChild(SmallIconButton(Icons.GitPullRequest, () => OpenCreateMergeRequest(b.Name), "Create merge request (m)"));
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

    // ── Keyboard ─────────────────────────────────────────────────────────

    private bool HandleBranchKey(KeyDownEvent e)
    {
        if (HandleBranchNavKey(e)) return true;
        switch (e.Key)
        {
            case KeyCode.C: CheckoutSelected(); return true;
            case KeyCode.M: CreateMergeRequestForSelected(); return true;
            case KeyCode.P: PushSelected(); return true;
            case KeyCode.L: PullSelected(); return true;
            case KeyCode.I: ToggleIncludeSelected(); return true;
            case KeyCode.T: ToggleRefTreeView(); return true;
            // The folder keys. Left/right stay out of HandleBranchNavKey deliberately:
            // that subset also runs from inside the search box, where the arrows are
            // the text caret's.
            case KeyCode.Right when _refTreeView: StepBranchTree(right: true); return true;
            case KeyCode.Left when _refTreeView: StepBranchTree(right: false); return true;
            default: return false;
        }
    }

    private void StepBranchTree(bool right)
    {
        var rows = BranchRows();
        var next = RefTreeStep(rows, RefKindBranch, _branchSel, right);
        if (next != _branchSel)
        {
            _branchSel = next;
            if (!rows[next].IsFolder) BranchSelectSingle(rows[next].Item);
            EnsureBranchVisible();
        }
        MarkDirty();
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
            case KeyCode.Enter: ActivateSelectedBranchRow(); return true;
            default: return false;
        }
    }

    /// <summary>Enter on the caret row: a folder opens or closes, a branch is checked out.</summary>
    private void ActivateSelectedBranchRow()
    {
        var rows = BranchRows();
        if (_branchSel >= 0 && _branchSel < rows.Count && rows[_branchSel] is { IsFolder: true } folder)
            ToggleFolder(RefKindBranch, folder);
        else
            CheckoutSelected();
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
        var sel = ActiveBranches
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
        var rows = BranchRows();
        if (rows.Count == 0) return;
        _branchSel = Math.Clamp(_branchSel + delta, 0, rows.Count - 1);
        // Plain nav single-selects — folder rows have nothing to select, so walking
        // through one leaves the previous branch selection standing.
        if (!focusOnly && rows[_branchSel] is { IsFolder: false } leaf) BranchSelectSingle(leaf.Item);
        EnsureBranchVisible();
        MarkDirty();
    }

    private void EnsureBranchVisible()
    {
        // Fixed row height, so the selected row's content-space band needs no
        // per-row element — index * height locates it directly.
        _branchScrollRef.Current?.ScrollRangeIntoView(_branchSel * BranchRowHeight, BranchRowHeight);
    }

    /// <summary>The branch under the caret, or null when the caret is on a folder row.</summary>
    private GitBranch? SelectedBranch()
    {
        var rows = BranchRows();
        return _branchSel >= 0 && _branchSel < rows.Count && rows[_branchSel] is { IsFolder: false } leaf
            ? leaf.Item
            : null;
    }

    private void CheckoutSelected()
    {
        if (SelectedBranch() is { IsCurrent: false } b)
            CheckoutBranch(b.IsRemote ? SplitRemote(b.Name).Leaf : b.Name);
    }

    // Push / Pull only apply to local branches (remote rows silently ignore them).
    private void PushSelected() { if (SelectedBranch() is { IsRemote: false } b) PushBranch(b); }
    /// <summary><c>m</c>: the selected branch's merge request — opened when it has one,
    /// created when it has not. A remote-tracking row answers for its leaf.</summary>
    private void CreateMergeRequestForSelected()
    {
        if (SelectedBranch() is not { } b || !GitLabActive(_activeRepoPath)) return;
        OpenCreateMergeRequest(b.IsRemote ? SplitRemote(b.Name).Leaf : b.Name);
    }
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

    /// <summary>Push one branch from the list. Asks first, on the same card the toolbar's
    /// Push uses — a branch pushed from here can need the lease exactly as often, and the
    /// row's own ahead/behind counts are what the card has to say about it.</summary>
    private void PushBranch(GitBranch b)
    {
        if (!HasActiveRepo || RemoteOpRunning) return;
        // A branch with no upstream is published rather than refused — same rule as the
        // toolbar's Push, and this list is where unpublished branches actually live.
        var (remote, _) = UpstreamOf(b);
        bool publish = string.IsNullOrEmpty(b.TrackingBranch);
        var body = publish
            ? $"{b.Name} has no upstream yet. Pushing publishes it to {remote} and tracks it from now on."
            : b.AheadCount == 0 && b.BehindCount == 0
                ? $"Pushes {b.Name} to {remote}. Nothing to send — the branch and its upstream are level."
                : $"Pushes {b.Name} to {remote}. {b.AheadCount} ahead, {b.BehindCount} behind.";
        var guard = PushGuard(b.Name);
        OpenDialog(new DialogSpec(
            Title: publish ? $"Publish {b.Name}" : $"Push {b.Name}",
            Body: guard.NobodyCanPush ? $"{remote} will reject a direct push to {b.Name}. Open a merge request instead." : body,
            Placeholder: null,
            ConfirmLabel: guard.NobodyCanPush ? "Try anyway" : publish ? "Publish" : "Push",
            OnConfirm: (_, force) => RunPushBranch(b, remote, publish, force),
            CheckboxLabel: ForceWithLeaseLabel,
            CheckboxInitial: false,
            Info: guard.Info,
            CheckboxDisabledReason: guard.ForceDisabledReason));
    }

    private async void RunPushBranch(GitBranch b, string remote, bool publish, bool forceWithLease)
    {
        if (ActiveRepo is not { } repo || RemoteOpRunning) return;
        BeginRemoteOp(RemoteOp.Push);
        Flash($"Pushing {b.Name}…");
        GitResult res;
        try
        {
            res = publish
                ? await repo.RunPushAsync(new PushPlan(remote, b.Name, SetUpstream: true, Problem: null),
                    NoAskPass, forceWithLease)
                : await repo.PushToAsync(remote, b.Name, NoAskPass, forceWithLease);
        }
        finally { EndRemoteOp(); }
        if (!res.Success)
        {
            Flash(FirstLine(res.Stderr, "Push failed"));
            RefreshAll();
            return;
        }
        RefreshAll();
        AfterPushToast(b.Name, publish ? $"Published {b.Name} to {remote}"
            : forceWithLease ? $"Force-pushed {b.Name}" : $"Pushed {b.Name} to {remote}");
    }

    private async void PullBranch(GitBranch b)
    {
        if (ActiveRepo is not { } repo || RemoteOpRunning) return;
        BeginRemoteOp(RemoteOp.Pull);
        Flash($"Pulling {b.Name}…");
        GitResult res;
        try
        {
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
        }
        finally { EndRemoteOp(); }
        Flash(res.Success ? $"Pulled {b.Name}" : FirstLine(res.Stderr, "Pull failed"));
        RefreshAll();
    }

    /// <summary>
    /// Asks before deleting. The card says what is lost — commits not on the default branch
    /// — and offers <c>-D</c> only when there are any, so an unmerged branch cannot be dropped
    /// by reflex; and on a GitLab-backed repository it carries the branch's protection rule.
    /// </summary>
    private void DeleteBranchAction(GitBranch b)
    {
        if (ActiveRepo is not { } repo) return;
        if (b.IsCurrent) { Flash("Can't delete the checked-out branch"); return; }
        var leaf = BranchName.Parse(b.Name).Title;
        var defaultBranch = GitLabActive(_activeRepoPath)
            ? ProjectFor(_activeRepoPath).Value?.Project.DefaultBranch ?? "main"
            : "main";
        int unmerged = repo.GetSubjectsBetween(defaultBranch, b.Name).Count;
        var remoteName = HostingRemoteName(_activeRepoPath);
        var body = unmerged > 0
            ? $"{leaf} has {unmerged} commit{(unmerged == 1 ? "" : "s")} not on {defaultBranch}. The local branch is removed; {remoteName}/{b.Name} is kept."
            : $"The local branch is removed. {remoteName}/{b.Name} is kept.";
        string? info = GitLabActive(_activeRepoPath) && GitLabRemoteFor(_activeRepoPath) is { } r
            && ProtectionFor(_activeRepoPath, b.Name) is { } rule
            ? PushRuleSummary(b.Name, rule, r.Host) : null;
        // A merged branch needs no question when Options › General says so; one with
        // unmerged work always asks — that card is a choice (-D or not), not a confirmation.
        if (unmerged == 0 && !_settings.General.ConfirmDestructive) { DoDeleteBranch(b, false); return; }
        OpenDialog(new DialogSpec(
            Title: $"Delete {leaf}?",
            Body: body,
            Placeholder: null,
            ConfirmLabel: "Delete branch",
            OnConfirm: (_, discard) => DoDeleteBranch(b, discard && unmerged > 0),
            CheckboxLabel: unmerged > 0 ? "Discard unmerged work (git branch -D)" : null,
            CheckboxInitial: false,
            Info: info));
    }

    private void DoDeleteBranch(GitBranch b, bool force)
    {
        if (ActiveRepo is not { } repo) return;
        // Non-force delete: git refuses to drop an unmerged branch, which keeps this safe.
        RunGitCommand(() => repo.DeleteBranch(b.Name, force), res =>
        {
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
        });
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
