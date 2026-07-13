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
    private Element BuildCommitView()
    {
        if (!HasActiveRepo)
            return BuildStubView("Commit", "Open a repository first.");

        var hash = _selectedCommitHash ?? _history.Value.Commits.FirstOrDefault()?.Hash;
        if (hash == null)
            return BuildStubView("Commit", "Select a commit in History to inspect it.");

        _detailHash = hash;
        _commitDetail.Load($"{_activeRepoPath}|{hash}|{_historyGen}",
            () => LoadDetail(hash), onLoaded: PickInitialFile);
        var detail = _commitDetail.Value;

        var commit = detail.Commit
            ?? _history.Value.Commits.FirstOrDefault(c => c.Hash == hash);

        var col = Flex.Column(gap: 0).FlexGrow(1);
        col.AddChild(BuildCommitHeader(commit, hash));
        col.AddChild(BuildCommitBody(detail));
        return col;
    }

    // Runs off the UI thread.
    private CommitDetail LoadDetail(string hash)
    {
        var repo = RepoFor(_activeRepoPath);
        var commit = repo.GetCommit(hash);
        var files = repo.GetCommitFiles(hash);
        var diffs = repo.GetCommitDiff(hash);
        var byPath = diffs
            .GroupBy(d => d.Path)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        return new CommitDetail(commit, files, byPath);
    }

    private void PickInitialFile(CommitDetail detail)
    {
        if (detail.Files.Count == 0) { _selectedFilePath = null; return; }
        if (_selectedFilePath == null || detail.Files.All(f => f.Path != _selectedFilePath))
            _selectedFilePath = detail.Files[0].Path;
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
        row1.AddChild(Ui.IconButton(Icons.CaretUp, () => MoveCommitSelection(-1), "Previous commit", 30f));
        row1.AddChild(Ui.IconButton(Icons.CaretDown, () => MoveCommitSelection(1), "Next commit", 30f));
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
            row2.AddChild(Text.New(RelativeTime(commit.AuthorDate)).FontSize(11.5f).TextColor(Nocturne.On(0.55)));
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
        row.AddChild(BuildFileTree(detail.Files));

        detail.DiffByPath.TryGetValue(_selectedFilePath ?? "", out var diff);
        row.AddChild(BuildDiffPane(_selectedFilePath ?? "", diff));
        return row;
    }

    private Element BuildFileTree(IReadOnlyList<GitFileChange> files)
    {
        var scroll = ScrollView.New().Width(238).FlexShrink(0)
            .Padding(new Thickness(8, 6, 8, 6))
            .BorderColor(Nocturne.Divider).BorderWidth(new Thickness(0, 1, 0, 0));

        scroll.AddChild(Text.New($"{files.Count} FILES CHANGED").FontSize(10f)
            .TextColor(Nocturne.On(0.40)).Padding(new Thickness(6, 8, 6, 8)));

        foreach (var f in files)
            scroll.AddChild(FileTreeRow(f));
        return scroll;
    }

    private bool MoveFileByKey(KeyDownEvent e) => e.Key switch
    {
        KeyCode.Down => MoveFileSelection(1),
        KeyCode.Up => MoveFileSelection(-1),
        KeyCode.PageDown => MoveFileSelection(8),
        KeyCode.PageUp => MoveFileSelection(-8),
        KeyCode.Home => MoveFileSelection(int.MinValue / 2),
        KeyCode.End => MoveFileSelection(int.MaxValue / 2),
        _ => false,
    };

    private bool MoveFileSelection(int delta)
    {
        var files = _commitDetail.Value.Files;
        if (files.Count == 0) return true;
        var i = _selectedFilePath == null ? 0 : IndexOfPath(files, _selectedFilePath);
        if (i < 0) i = 0;
        i = Math.Clamp(i + delta, 0, files.Count - 1);
        _selectedFilePath = files[i].Path;
        MarkDirty();
        return true;
    }

    private static int IndexOfPath(IReadOnlyList<GitFileChange> files, string path)
    {
        for (var i = 0; i < files.Count; i++)
            if (files[i].Path == path) return i;
        return -1;
    }

    private Element FileTreeRow(GitFileChange f)
    {
        var selected = f.Path == _selectedFilePath;
        var (letter, color) = StatusLetter(f.Kind);
        var name = System.IO.Path.GetFileName(f.Path);
        var dir = f.Path.Length > name.Length ? f.Path[..^name.Length] : "";

        var row = Flex.Row(gap: 8).AlignItems(AlignItems.Center)
            .Padding(new Thickness(6, 8, 6, 8)).CornerRadius(Nocturne.RadiusRow)
            .Cursor(CursorType.Pointer).Tooltip(f.Path);
        // Include selection in the key so a selection change replaces the row with
        // fresh handlers — otherwise the stale hover handler (handlers aren't copied
        // on reconcile) resets the background and the highlight never appears.
        row.Key = f.Path + (selected ? "#s" : "");
        if (selected) row.Background(Nocturne.Accent900);

        row.AddChild(Box.New().Width(16).FlexShrink(0)
            .Children(Ui.Mono(letter, 11f, color).FontWeight(FontWeight.Bold)));

        var text = Flex.Column(gap: 0).FlexGrow(1);
        text.AddChild(Text.New(name).FontSize(12f)
            .OverflowMode(TextOverflow.Ellipsis).MaxLines(1));
        if (dir.Length > 0)
            text.AddChild(Text.New(dir).FontSize(9.5f).TextColor(Nocturne.On(0.40))
                .OverflowMode(TextOverflow.Ellipsis).MaxLines(1));
        row.AddChild(text);

        row.OnClick(_ => { _selectedFilePath = f.Path; MarkDirty(); });
        if (!selected) row.HoverBg(Nocturne.Neutral900, Color.Transparent);
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
