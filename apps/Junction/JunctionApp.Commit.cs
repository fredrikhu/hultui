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
    private Element BuildCommitView()
    {
        if (!HasActiveRepo)
            return BuildStubView("Commit", "Open a repository first.");

        // Two or more commits selected in History → Fork-style compare between
        // the oldest and newest of the selection.
        if (SelectedRange() is { } range)
            return BuildRangeCompareView(range);
        _detailRange = null;

        var hash = _selectedCommitHash ?? _history.Value.Commits.FirstOrDefault()?.Hash;
        if (hash == null)
            return BuildStubView("Commit", "Select a commit in History to inspect it.");

        _detailHash = hash;
        SyncCommitFileTree(hash);
        PrefetchCommitDetail(hash);   // usually already loaded from the History selection
        var detail = _commitDetail.Value;

        // While a newer commit's detail is still loading, _commitDetail.Value (and
        // _selectedFilePath) still describe the PREVIOUS commit. Rendering that would
        // flash the wrong commit's files/diff for a frame, so only show the loaded
        // body once its hash matches; until then keep the header (sourced from the
        // history list) correct and show a neutral loading pane.
        var ready = detail.Commit != null && detail.Commit.Hash == hash && detail.RangeBaseHash == null;
        var commit = ready
            ? detail.Commit
            : _history.Value.Commits.FirstOrDefault(c => c.Hash == hash);

        var col = Flex.Column(gap: 0).FlexGrow(1);
        col.AddChild(BuildCommitHeader(commit, hash));
        col.AddChild(ready ? BuildCommitBody(detail) : BuildCommitBodyLoading());
        return col;
    }

    // ── Multi-select compare (oldest…newest of the History selection) ────

    private Element BuildRangeCompareView((string BaseHash, string HeadHash) range)
    {
        _detailHash = null;
        _detailRange = range;
        SyncCommitFileTree($"{range.BaseHash}..{range.HeadHash}");

        var col = Flex.Column(gap: 0).FlexGrow(1);
        col.AddChild(BuildRangeHeader(range));

        // Two branches on the same commit: nothing to diff.
        if (range.BaseHash == range.HeadHash)
        {
            col.AddChild(BuildNoDiffPane(range.BaseHash));
            return col;
        }

        PrefetchRangeDetail(range);
        var detail = _commitDetail.Value;
        var ready = detail.Commit != null && detail.Commit.Hash == range.HeadHash
            && detail.RangeBaseHash == range.BaseHash;
        col.AddChild(ready ? BuildCommitBody(detail) : BuildCommitBodyLoading());
        return col;
    }

    private Element BuildNoDiffPane(string hash)
    {
        var col = Flex.Column(gap: 6).FlexGrow(1)
            .JustifyContent(JustifyContent.Center).AlignItems(AlignItems.Center);
        col.AddChild(Ui.Icon(Icons.Check, 26f, Nocturne.On(0.35)));
        col.AddChild(Text.New("No differences").FontSize(14f).FontWeight(FontWeight.Medium)
            .TextColor(Nocturne.On(0.7)));
        col.AddChild(Text.New($"Both branches point to the same commit ({Short7(hash)}).")
            .FontSize(12f).TextColor(Nocturne.On(0.45)));
        return col;
    }

    // The ignore-whitespace flag is part of the key so toggling it reloads the diff.
    private string RangeDetailKey((string BaseHash, string HeadHash) range) =>
        $"{_activeRepoPath}|{range.BaseHash}..{range.HeadHash}|{DataGen(_activeRepoPath)}|w{_settings.Git.IgnoreWhitespace}";

    private void PrefetchRangeDetail((string BaseHash, string HeadHash) range)
    {
        if (!HasActiveRepo) return;
        var r = range;
        _commitDetail.Load(RangeDetailKey(r), () => LoadRangeDetail(r), onLoaded: PickInitialFile);
    }

    // Runs off the UI thread.
    private CommitDetail LoadRangeDetail((string BaseHash, string HeadHash) range)
    {
        var repo = RepoFor(_activeRepoPath);
        var commit = repo.GetCommit(range.HeadHash);
        var files = repo.GetRangeFiles(range.BaseHash, range.HeadHash);
        var diffs = repo.GetRangeDiff(range.BaseHash, range.HeadHash, _settings.Git.IgnoreWhitespace);
        var byPath = diffs
            .GroupBy(d => d.Path)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        return new CommitDetail(commit, files, byPath, range.BaseHash);
    }

    private Element BuildRangeHeader((string BaseHash, string HeadHash) range)
    {
        var commits = _history.Value.Commits;
        var baseCommit = commits.FirstOrDefault(c => c.Hash == range.BaseHash);
        var headCommit = commits.FirstOrDefault(c => c.Hash == range.HeadHash);

        var box = Flex.Column(gap: 8).FlexShrink(0)
            .Padding(new Thickness(12, 16, 12, 16))
            .BorderColor(Nocturne.Divider).BorderWidth(new Thickness(0, 0, 1, 0));

        // A branch-tab compare shows branch names and returns to Branches; a
        // History multi-select compare shows commit subjects and returns to History.
        var branchCmp = BranchCompareActive()
            && _branchCompare!.Value.BaseHash == range.BaseHash
            && _branchCompare!.Value.HeadHash == range.HeadHash
            ? _branchCompare : null;

        var row1 = Flex.Row(gap: 10).AlignItems(AlignItems.Center);
        row1.AddChild(branchCmp != null
            ? Ui.GhostButton("Branches", Icons.ArrowLeft, () => SetView(JunctionView.Branches))
            : Ui.GhostButton("History", Icons.ArrowLeft, () => SetView(JunctionView.History)));
        row1.AddChild(Text.New(branchCmp != null
                ? "Comparing 2 branches"
                : $"Comparing {_selectedCommitHashes.Count} selected commits")
            .FontSize(15f).FontWeight(FontWeight.Medium)
            .FlexGrow(1).FlexShrink(1).OverflowMode(TextOverflow.Ellipsis).MaxLines(1));
        box.AddChild(row1);

        var row2 = Flex.Row(gap: 10).AlignItems(AlignItems.Center);
        row2.AddChild(Ui.Mono($"{Short7(range.BaseHash)} → {Short7(range.HeadHash)}", 11.5f, Nocturne.On(0.55))
            .FlexShrink(0));
        if (branchCmp is { } bc)
            row2.AddChild(Text.New($"{bc.BaseLabel}  →  {bc.HeadLabel}")
                .FontSize(11.5f).TextColor(Nocturne.On(0.55))
                .FlexShrink(1).OverflowMode(TextOverflow.Ellipsis).MaxLines(1));
        else if (baseCommit != null && headCommit != null)
            row2.AddChild(Text.New($"{baseCommit.Subject}  →  {headCommit.Subject}")
                .FontSize(11.5f).TextColor(Nocturne.On(0.55))
                .FlexShrink(1).OverflowMode(TextOverflow.Ellipsis).MaxLines(1));
        box.AddChild(row2);

        return box;
    }

    private static string Short7(string hash) => hash.Length >= 7 ? hash[..7] : hash;

    /// <summary>Placeholder body shown while a newly selected commit's detail loads,
    /// so the previous commit's files/diff never flash. Keeps the two-column shell so
    /// the layout doesn't jump when the real content lands.</summary>
    private Element BuildCommitBodyLoading()
    {
        var row = Flex.Row(gap: 0).FlexGrow(1).AlignItems(AlignItems.Stretch);
        row.AddChild(ScrollView.New().Width(238).FlexShrink(0)
            .Padding(new Thickness(8, 6, 8, 6))
            .BorderColor(Nocturne.Divider).BorderWidth(new Thickness(0, 1, 0, 0)));
        row.AddChild(Flex.Column(gap: 0).FlexGrow(1)
            .JustifyContent(JustifyContent.Center).AlignItems(AlignItems.Center)
            .Children(Text.New("Loading…").FontSize(12f).TextColor(Nocturne.On(0.40))));
        return row;
    }

    private string CommitDetailKey(string hash) =>
        $"{_activeRepoPath}|{hash}|{DataGen(_activeRepoPath)}|w{_settings.Git.IgnoreWhitespace}";

    /// <summary>Eagerly loads a commit's files+diff into <see cref="_commitDetail"/>.
    /// Called both when a commit is selected in History (prefetch) and when the
    /// Commit view builds; the shared key makes the second call a cache hit.</summary>
    private void PrefetchCommitDetail(string? hash)
    {
        if (string.IsNullOrEmpty(hash) || !HasActiveRepo) return;
        var h = hash;
        _commitDetail.Load(CommitDetailKey(h), () => LoadDetail(h), onLoaded: PickInitialFile);
    }

    // Runs off the UI thread.
    private CommitDetail LoadDetail(string hash)
    {
        var repo = RepoFor(_activeRepoPath);
        var commit = repo.GetCommit(hash);
        var files = repo.GetCommitFiles(hash);
        var diffs = repo.GetCommitDiff(hash, _settings.Git.IgnoreWhitespace);
        var byPath = diffs
            .GroupBy(d => d.Path)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        return new CommitDetail(commit, files, byPath);
    }

    private void PickInitialFile(CommitDetail detail)
    {
        if (detail.Files.Count == 0) { _selectedFilePath = null; _commitCursorPath = null; return; }
        if (_selectedFilePath == null || detail.Files.All(f => f.Path != _selectedFilePath))
            _selectedFilePath = detail.Files[0].Path;
        // The cursor starts on the open file; the build re-checks that its row is
        // actually visible before drawing the highlight.
        _commitCursorPath = _selectedFilePath;
    }

    // ── Header ───────────────────────────────────────────────────────────

    private Element BuildCommitHeader(GitCommit? commit, string hash)
    {
        var box = Flex.Column(gap: 8).FlexShrink(0)
            .Padding(new Thickness(12, 16, 12, 16))
            .BorderColor(Nocturne.Divider).BorderWidth(new Thickness(0, 0, 1, 0));

        var row1 = Flex.Row(gap: 10).AlignItems(AlignItems.Center);
        row1.AddChild(Ui.GhostButton("History", Icons.ArrowLeft, () => SetView(JunctionView.History)));
        row1.AddChild(Text.New(commit?.Subject ?? "Loading…").FontSize(15f).FontWeight(FontWeight.Medium)
            .FlexGrow(1).FlexShrink(1).OverflowMode(TextOverflow.Ellipsis).MaxLines(1));
        row1.AddChild(Ui.IconButton(Icons.CaretUp, () => MoveCommitSelection(-1), "Previous commit (Ctrl ↑)", 30f));
        row1.AddChild(Ui.IconButton(Icons.CaretDown, () => MoveCommitSelection(1), "Next commit (Ctrl ↓)", 30f));
        box.AddChild(row1);

        var author = commit?.AuthorName ?? "";
        var row2 = Flex.Row(gap: 10).AlignItems(AlignItems.Center);
        row2.AddChild(Flex.Row(gap: 5).AlignItems(AlignItems.Center).Children(
            Box.New().Width(18).Height(18).CornerRadius(9f).Background(Nocturne.Accent800)
                .JustifyContent(JustifyContent.Center).AlignItems(AlignItems.Center)
                .Children(Text.New(Initials(author)).FontSize(9f).TextColor(Nocturne.Accent100)),
            Text.New(author).FontSize(11.5f).TextColor(Nocturne.On(0.55))));
        row2.AddChild(Ui.Mono($"commit {(commit?.ShortHash ?? hash[..Math.Min(7, hash.Length)])}", 11.5f, Nocturne.On(0.55)));
        if (commit != null)
            row2.AddChild(Text.New(commit.AuthorDate.ToString("g")).FontSize(11.5f).TextColor(Nocturne.On(0.55)));
        row2.AddChild(Flex.Row(gap: 5).AlignItems(AlignItems.Center).Children(
            Ui.Icon(Icons.GitBranch, 12f, Nocturne.On(0.55)),
            Text.New(CommitBranchTitle(commit)).FontSize(11.5f).TextColor(Nocturne.On(0.55))));
        box.AddChild(row2);

        return box;
    }

    private string CommitBranchTitle(GitCommit? commit)
    {
        // Prefer a local branch decoration on the commit; fall back to the repo's branch.
        if (commit != null)
        {
            foreach (var raw in commit.Refs)
            {
                var d = raw.Trim();
                if (d.StartsWith("HEAD -> ", StringComparison.Ordinal))
                    return BranchName.Parse(d["HEAD -> ".Length..]).Title;
                if (d is "HEAD" || d.StartsWith("tag: ", StringComparison.Ordinal) || d.Contains('/'))
                    continue;
                return BranchName.Parse(d).Title;
            }
        }
        return BranchName.Parse(SummaryFor(_activeRepoPath).Branch).Title;
    }

    // ── Body: file tree + diff ───────────────────────────────────────────

    private Element BuildCommitBody(CommitDetail detail)
    {
        var row = Flex.Row(gap: 0).FlexGrow(1).AlignItems(AlignItems.Stretch);
        // Narrow windows hide the diff pane; the file tree fills instead (open the
        // diff in VS Code with Ctrl+D — see OpenSelectedDiffExternally).
        row.AddChild(BuildFileTree(detail.Files, fill: _hideDiff));

        if (!_hideDiff)
        {
            detail.DiffByPath.TryGetValue(_selectedFilePath ?? "", out var diff);
            row.AddChild(BuildDiffPane(_selectedFilePath ?? "", diff));
        }
        return row;
    }

    private Element BuildFileTree(IReadOnlyList<GitFileChange> files, bool fill = false)
    {
        // When the diff pane is hidden the tree fills the view; otherwise it's a
        // fixed-width sidebar next to the diff.
        var scroll = ScrollView.New()
            .Padding(new Thickness(8, 6, 8, 6))
            .BorderColor(Nocturne.Divider).BorderWidth(new Thickness(0, 1, 0, 0))
            .WithRef(_fileScrollRef);
        if (fill) scroll.FlexGrow(1);
        else scroll.Width(238).FlexShrink(0);

        scroll.AddChild(Text.New($"{files.Count} FILES CHANGED").FontSize(10f)
            .TextColor(Nocturne.On(0.40)).Padding(new Thickness(6, 8, 6, 8)));

        // The rows are the visible part of the tree — folders and files in one flat
        // list, which is what the keyboard steps through and what the scroll-into-view
        // ref attaches to.
        _commitFilesTree.Bind(files, f => f.Path, sort: ChangeTreeSort);
        _commitFileRows = _commitFilesTree.Rows();
        // A collapse (or a reload) can take the cursor's row away; fall back to the open
        // file, then to the top of the list.
        if (_commitCursorPath == null || _commitFileRows.All(r => r.Path != _commitCursorPath))
            _commitCursorPath = _commitFileRows.Any(r => r.Path == _selectedFilePath)
                ? _selectedFilePath
                : _commitFileRows.FirstOrDefault()?.Path;

        foreach (var node in _commitFileRows)
        {
            var rowEl = node.IsFolder ? CommitFolderRow(node) : FileTreeRow(node);
            if (node.Path == _commitCursorPath) rowEl.WithRef(_fileSelRef);
            scroll.AddChild(rowEl);
        }
        return scroll;
    }

    /// <summary>Resets the tree when the Commit view moves to another commit (or range).
    /// Folders someone collapsed belong to the list they were collapsed in; carrying the
    /// state to the next commit would fold away files they never chose to hide.</summary>
    private void SyncCommitFileTree(string key)
    {
        if (_commitTreeKey == key) return;
        _commitTreeKey = key;
        _commitFilesTree.ClearOverrides();
        _commitCursorPath = null;
    }

    private bool MoveFileByKey(KeyDownEvent e)
    {
        if (_commitFileRows.Count == 0) return false;
        var i = CommitCursorIndex();
        switch (e.Key)
        {
            case KeyCode.Down: return MoveCommitCursor(i + 1);
            case KeyCode.Up: return MoveCommitCursor(i - 1);
            case KeyCode.PageDown: return MoveCommitCursor(i + 8);
            case KeyCode.PageUp: return MoveCommitCursor(i - 8);
            case KeyCode.Home: return MoveCommitCursor(0);
            case KeyCode.End: return MoveCommitCursor(_commitFileRows.Count - 1);
            // The conventional folder keys, via the tree model: right opens a closed
            // folder and otherwise descends, left closes an open one and otherwise
            // steps out to the parent.
            case KeyCode.Right: return CommitTreeStep(i, right: true);
            case KeyCode.Left: return CommitTreeStep(i, right: false);
            case KeyCode.Enter or KeyCode.Space when _commitFileRows[i].IsFolder:
                _commitFilesTree.Toggle(_commitFileRows[i]);   // MarkDirty via onChanged
                return true;
            default: return false;
        }
    }

    private int CommitCursorIndex()
    {
        var target = _commitCursorPath ?? _selectedFilePath;
        for (var i = 0; i < _commitFileRows.Count; i++)
            if (_commitFileRows[i].Path == target) return i;
        return 0;
    }

    /// <summary>Moves the cursor to a visible row. A file row opens too, so arrowing
    /// through the list still previews each diff; a folder row only moves the highlight
    /// and leaves the open file on screen — the same as the Files tab, and better than
    /// blanking the pane every time the cursor crosses a folder.</summary>
    private bool MoveCommitCursor(int index)
    {
        if (_commitFileRows.Count == 0) return true;
        index = Math.Clamp(index, 0, _commitFileRows.Count - 1);
        var node = _commitFileRows[index];
        _commitCursorPath = node.Path;
        _pendingScrollToSelected = true;
        if (node.IsFolder || node.Path == _selectedFilePath) { MarkDirty(); return true; }
        _selectedFilePath = node.Path;
        // Rerender: selecting an image file activates a new per-file image-diff loader.
        Rerender();
        return true;
    }

    private bool CommitTreeStep(int index, bool right)
    {
        var landed = _commitFilesTree.Step(_commitFileRows, index, right);
        if (landed != index) MoveCommitCursor(landed);
        else _pendingScrollToSelected = true;   // the step expanded or collapsed instead
        return true;
    }

    private static int IndexOfPath(IReadOnlyList<GitFileChange> files, string path)
    {
        for (var i = 0; i < files.Count; i++)
            if (files[i].Path == path) return i;
        return -1;
    }

    private Element CommitFolderRow(TreeNode<GitFileChange> node)
    {
        var expanded = _commitFilesTree.IsExpanded(node);
        var cursor = node.Path == _commitCursorPath;

        var row = ChangeFolderRow(node, expanded);
        // Cursor and open state both change how the row looks, and a collapse moves
        // every row below it — without them in the key a patched row would keep the
        // previous occupant's handler and clicking one folder would toggle another.
        row.Key = "d:" + node.Path + (cursor ? "#c" : "") + (expanded ? "#o" : "");
        if (cursor) row.Background(Nocturne.Neutral800);
        row.OnClick(_ =>
        {
            _commitCursorPath = node.Path;
            _commitFilesTree.Toggle(node);   // MarkDirty via the model's onChanged
        });
        if (!cursor) row.HoverBg(Nocturne.Neutral900, Color.Transparent);
        return row;
    }

    private Element FileTreeRow(TreeNode<GitFileChange> node)
    {
        var f = node.Item;
        var selected = f.Path == _selectedFilePath;
        var cursor = f.Path == _commitCursorPath;
        var (letter, color) = StatusLetter(f.Kind);

        var row = Flex.Row(gap: 8).AlignItems(AlignItems.Center)
            .Padding(ChangeRowPadding(node.Depth)).CornerRadius(Nocturne.RadiusRow)
            .Cursor(CursorType.Pointer).Tooltip(f.Path);
        // Include selection in the key so a selection change replaces the row outright:
        // the hover baseline differs per state, and a fresh element can't be left
        // showing the other state's hover tint.
        row.Key = f.Path + (selected ? "#s" : "") + (cursor ? "#c" : "");
        if (selected) row.Background(Nocturne.Accent900);
        else if (cursor) row.Background(Nocturne.Neutral800);

        row.AddChild(Box.New().Width(16).FlexShrink(0)
            .Children(Ui.Mono(letter, 11f, color).FontWeight(FontWeight.Bold)));
        // Only the file name: the folder above it is the row that carries the directory,
        // which is the whole point of grouping them.
        row.AddChild(Text.New(node.Segment).FontSize(12f).FlexGrow(1).FlexShrink(1)
            .OverflowMode(TextOverflow.Ellipsis).MaxLines(1));

        row.OnClick(_ => { _commitCursorPath = f.Path; _selectedFilePath = f.Path; Rerender(); });
        if (!selected && !cursor) row.HoverBg(Nocturne.Neutral900, Color.Transparent);
        return row;
    }

    private static (string Letter, Color Color) StatusLetter(FileChangeKind kind) => kind switch
    {
        FileChangeKind.Added => ("A", Nocturne.StatusLetter('A')),
        FileChangeKind.Modified => ("M", Nocturne.StatusLetter('M')),
        FileChangeKind.Deleted => ("D", Nocturne.StatusLetter('D')),
        FileChangeKind.Renamed => ("R", Nocturne.StatusLetter('R')),
        FileChangeKind.Copied => ("C", Nocturne.StatusLetter('R')),
        FileChangeKind.Untracked => ("U", Nocturne.StatusLetter('U')),
        FileChangeKind.Conflicted => ("!", Nocturne.DangerText),
        _ => ("?", Nocturne.Neutral400),
    };

    private static string Initials(string name)
    {
        var parts = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return "?";
        var s = parts[0][..1];
        if (parts.Length > 1) s += parts[^1][..1];
        return s.ToUpperInvariant();
    }
}
