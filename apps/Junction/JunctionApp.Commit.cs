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

        // A review on its way in: its refs are being fetched, or did not come.
        if (_review is { Ready: false } pendingReview)
            return BuildReviewPendingView(pendingReview);

        // Two or more commits selected in History → Fork-style compare between
        // the oldest and newest of the selection. A review is one too: base to head.
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
        col.AddChild(_review is { Ready: true } review ? BuildReviewHeader(review) : BuildRangeHeader(range));

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
        var path = _activeRepoPath;
        _commitDetail.Load(RangeDetailKey(r), () => LoadOrRetry(path, () => LoadRangeDetail(r)),
            onLoaded: PickInitialFile);
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

    /// <summary>The commit message split into lines, with the trailing blank ones dropped.
    /// Falls back to the subject while the full message is still loading — the History list
    /// has that much, and a header that says "Loading…" where the title goes reads as a
    /// stall even when everything else on screen is right.</summary>
    private static List<string> MessageLines(GitCommit? commit)
    {
        if (commit is null) return [];
        var text = commit.Message.Length > 0 ? commit.Message : commit.Subject;
        var lines = text.ReplaceLineEndings("\n").Split('\n').ToList();
        while (lines.Count > 1 && lines[^1].Trim().Length == 0) lines.RemoveAt(lines.Count - 1);
        return lines;
    }

    /// <summary>One line of the message: selectable, and with any issue references the
    /// repository's <c>.issuetracker</c> recognises turned into links.</summary>
    private TextElement MessageLine(string text, int index, float size, Color color, FontWeight weight) =>
        // A blank line is drawn as a space, the way the diff draws its empty rows: empty
        // text measures to no height at all, which silently closes up the paragraph breaks
        // the author put in. The copy is unaffected — it comes from the group's model, not
        // from what the label happens to be showing.
        IssueLinked(Text.New(text.Length == 0 ? " " : text)
            .FontSize(size).FontWeight(weight).TextColor(color)
            .Selectable().SelectionGroup(_commitMsgGroup, index), text);

    private readonly TextSelectionGroup _commitMsgGroup = new();

    private static string Short7(string hash) => hash.Length >= 7 ? hash[..7] : hash;

    /// <summary>Placeholder body shown while a newly selected commit's detail loads,
    /// so the previous commit's files/diff never flash. Keeps the two-column shell so
    /// the layout doesn't jump when the real content lands.</summary>
    private Element BuildCommitBodyLoading()
    {
        var row = Flex.Row(gap: 0).FlexGrow(1).AlignItems(AlignItems.Stretch);
        // The same width the loaded pane will use, so the layout does not jump when it lands.
        row.AddChild(ScrollView.New().Width(_commitSplit.Size).FlexShrink(0)
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
        var path = _activeRepoPath;   // captured: the producer runs off-thread, after any switch
        _commitDetail.Load(CommitDetailKey(h), () => LoadOrRetry(path, () => LoadDetail(path, h)),
            onLoaded: PickInitialFile);
    }

    // Runs off the UI thread.
    private CommitDetail LoadDetail(string path, string hash)
    {
        var repo = RepoFor(path);
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
        {
            _selectedFilePath = detail.Files[0].Path;
            // The cursor starts on the open file; the build re-checks that its row is
            // actually visible before drawing the highlight. A reload that keeps the open
            // file leaves the cursor where it was parked — the build moves it only if its
            // row is gone.
            _commitCursorPath = _selectedFilePath;
        }
        PrefetchHighlight(detail);
    }

    /// <summary>
    /// Starts tokenizing the file the diff will open on, as soon as the commit's detail
    /// lands — which, when the detail was prefetched from History, is while the reader is
    /// still looking at the log.
    ///
    /// <para>The detail was already prefetched on selection, so switching to the Commit tab
    /// had its files and diffs ready; the colours were not, because
    /// <see cref="HighlightForCurrent"/> only asks once the Commit view is the one
    /// building. Measured on this repository: the detail was warm with 0 ms to wait, and
    /// then the tab switch was followed by ~350 ms of uncoloured diff. Nothing here is new
    /// work — it is the same content-addressed tokenize the pane would ask for, started one
    /// view earlier, so the pane's own request is a cache hit.</para>
    ///
    /// <para>Only the opening file. A commit of sixteen files would otherwise tokenize
    /// sixteen of them to show one, and moving between files inside the Commit view is a
    /// different (and much shorter) wait.</para>
    /// </summary>
    private void PrefetchHighlight(CommitDetail detail)
    {
        if (_selectedFilePath is not { Length: > 0 } path) return;
        if (detail.Commit?.Hash is not { } hash) return;

        // The same gate BuildDiffBody applies. An image or any other binary never reaches
        // the text pane, so tokenizing one is waste that would be invisible on screen —
        // and prefetching it early is exactly how a rule enforced at the point of use gets
        // quietly broken from somewhere else. A file with no diff entry (a mode-only
        // change) has no text to colour either.
        if (IsImagePath(path)) return;
        if (!detail.DiffByPath.TryGetValue(path, out var diff) || diff.IsBinary) return;

        // Same arguments HighlightForCurrent will pass, so the keys agree: for a range
        // compare the base is explicit, and for a single commit it is null → "<hash>^".
        HighlightFor(path, ExternalDiffMode.Commit, hash, detail.RangeBaseHash);
    }

    // ── Header ───────────────────────────────────────────────────────────

    private Element BuildCommitHeader(GitCommit? commit, string hash)
    {
        var box = Flex.Column(gap: 8).FlexShrink(0)
            .Padding(new Thickness(12, 16, 12, 16))
            .BorderColor(Nocturne.Divider).BorderWidth(new Thickness(0, 0, 1, 0));

        // Subject and body are one selection: they are one message, and a reader who drags
        // from the title into the body expects to get what they dragged over. The group is
        // bound to the message's lines, so the subject is line 0 of the same model.
        var lines = MessageLines(commit);
        _commitMsgGroup.Bind(commit?.Hash ?? hash, lines.Count, i => i >= 0 && i < lines.Count ? lines[i] : null);

        var row1 = Flex.Row(gap: 10).AlignItems(AlignItems.Center);
        row1.AddChild(Ui.GhostButton("History", Icons.ArrowLeft, () => SetView(JunctionView.History)));
        row1.AddChild(MessageLine(lines.Count > 0 ? lines[0] : commit?.Subject ?? "Loading…", 0, 15f,
                Nocturne.Text, FontWeight.Medium)
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
        row2.AddChild(CommitShaChip(commit?.Hash ?? hash, commit?.ShortHash));
        if (commit != null)
            row2.AddChild(Text.New(commit.AuthorDate.ToString("g")).FontSize(11.5f).TextColor(Nocturne.On(0.55)));
        row2.AddChild(Flex.Row(gap: 5).AlignItems(AlignItems.Center).Children(
            Ui.Icon(Icons.GitBranch, 12f, Nocturne.On(0.55)),
            Text.New(CommitBranchTitle(commit)).FontSize(11.5f).TextColor(Nocturne.On(0.55))));
        // What GitLab ran on this commit, and the merge request it heads, when the
        // repository is hosted there.
        if (BuildCommitHostedLine(commit?.Hash ?? hash) is { } hostedLine)
            row2.AddChild(hostedLine);

        // The toggle rides at the end of the metadata row, not beside the subject: row 1
        // already ends in two carets for stepping between commits, and a third would read
        // as one of them. It only exists when there is a body to hide.
        if (lines.Count > 1)
        {
            row2.AddChild(Box.New().FlexGrow(1));
            // Lines with something on them, not lines: git puts a blank one between subject
            // and body, and counting it tells the reader there is a third line of prose
            // waiting for them when there are two.
            int bodyLines = lines.Skip(1).Count(l => l.Trim().Length > 0);
            row2.AddChild((_commitMsgExpanded
                    ? Ui.GhostButton("Hide message", Icons.CaretUp, ToggleCommitMessage, 11.5f)
                    // Collapsed, the button is also the only sign there is anything there,
                    // so it says how much rather than just which way it opens.
                    : Ui.GhostButton(bodyLines > 0 ? $"{bodyLines} more line{(bodyLines == 1 ? "" : "s")}" : "Show message",
                        Icons.CaretDown, ToggleCommitMessage, 11.5f))
                .FlexShrink(0)
                .Tooltip($"{(_commitMsgExpanded ? "Hide" : "Show")} the message body ({Shortcuts.Keys("commit.message")})"));
        }
        box.AddChild(row2);

        // The body, when there is one and it is wanted. Bounded and scrollable: a long
        // release note of a commit message is not a reason for the diff to lose half its
        // height.
        if (lines.Count > 1 && _commitMsgExpanded)
        {
            // Skip the blank line git puts between subject and body. The header already
            // sets the subject apart — its own row, its own weight, the metadata beneath —
            // so drawing the separator as well leaves a gap that reads as a mistake.
            // Interior blank lines stay: those are the author's paragraphs.
            int first = 1;
            while (first < lines.Count && lines[first].Trim().Length == 0) first++;

            // Skipped for drawing only. The line stays in the model the selection group is
            // bound to, so a copy still yields the message as git stores it — the group
            // reads its text from there and tolerates a line with no label on screen.
            if (first < lines.Count)
            {
                var body = ScrollView.New().MaxHeight(132f).FlexShrink(0);
                for (int i = first; i < lines.Count; i++)
                    body.AddChild(MessageLine(lines[i], i, 12f, Nocturne.On(0.72), FontWeight.Regular));
                box.AddChild(body);
            }
        }

        return box;
    }

    /// <summary>
    /// The commit's hash, as something you can take away with you.
    ///
    /// <para>The hash was already on screen and was already the thing people came to this
    /// header for — to paste into a message, a ticket, or another terminal — but the only
    /// way to get it was to right-click the row back in History. A click copies the full
    /// forty characters (what every other tool wants); the context menu offers the
    /// abbreviated form for the places a person is going to read it.</para>
    /// </summary>
    private Element CommitShaChip(string hash, string? shortHash)
    {
        var abbreviated = shortHash ?? hash[..Math.Min(7, hash.Length)];
        var chip = Flex.Row(gap: 5).FlexShrink(0).AlignItems(AlignItems.Center)
            .Padding(new Thickness(1, 5, 1, 5)).CornerRadius(Nocturne.RadiusSm)
            .Cursor(CursorType.Pointer)
            .Tooltip($"{hash}\nClick to copy");
        chip.AddChild(Ui.Mono($"commit {abbreviated}", 11.5f, Nocturne.On(0.55)));
        chip.AddChild(Ui.Icon(Icons.Copy, 11f, Nocturne.On(0.4)));
        chip.OnClick(e =>
        {
            e.StopPropagation = true;
            CopyToClipboard(hash);
            Flash($"Copied {abbreviated}");
        });
        chip.ContextMenu(() =>
        [
            Item(Icons.Copy, "Copy commit SHA", () => CopyToClipboard(hash)),
            Item(Icons.Copy, $"Copy short SHA ({abbreviated})", () => CopyToClipboard(abbreviated)),
        ]);
        chip.HoverBg(Nocturne.Neutral900, Color.Transparent);
        return chip;
    }

    /// <summary>Whether the Commit tab shows the message body under the subject.</summary>
    private bool _commitMsgExpanded;

    private void ToggleCommitMessage()
    {
        _commitMsgExpanded = !_commitMsgExpanded;
        Persist();
        MarkDirty();
    }

    /// <summary>True when the Commit tab is showing a commit whose message has a body —
    /// what gates the <c>m</c> key, so the letter stays free everywhere it would do
    /// nothing.</summary>
    private bool CommitMessageHasBody =>
        View == JunctionView.Commit && _detailRange == null
        && MessageLines(_commitDetail.Value.Commit
            ?? _history.Value.Commits.FirstOrDefault(c => c.Hash == _detailHash)).Count > 1;

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
            row.AddChild(_commitSplit.Handle(Nocturne.Alpha(Nocturne.Accent, 0.18), Nocturne.Alpha(Nocturne.Accent, 0.40)));
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
        else scroll.Width(_commitSplit.Size).FlexShrink(0);

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
        // Reviewing: how many threads the file has, and whether any is still open.
        if (ReviewFilePill(f.Path) is { } pill) row.AddChild(pill);

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
