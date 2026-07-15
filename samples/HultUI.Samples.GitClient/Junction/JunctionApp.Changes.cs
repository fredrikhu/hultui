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
    private Element BuildChangesView()
    {
        if (!HasActiveRepo)
            return BuildStubView("Changes", "Open a repository first.");

        _changes.Load($"{_activeRepoPath}|{_summaryGen}", LoadChanges, onLoaded: PickInitialChange);
        var data = _changes.Value;

        GitFileDiff? diff = null;
        if (_changeSelPath != null)
        {
            var dict = _changeSelStaged ? data.StagedDiffs : data.UnstagedDiffs;
            dict.TryGetValue(_changeSelPath, out diff);
        }

        var row = Flex.Row(gap: 0).FlexGrow(1).AlignItems(AlignItems.Stretch);
        row.AddChild(BuildChangesColumn(data));
        row.AddChild(BuildDiffPane(_changeSelPath ?? "", diff));
        return row;
    }

    // Runs off the UI thread.
    private ChangesData LoadChanges()
    {
        var repo = RepoFor(_activeRepoPath);
        var staged = repo.GetStagedChanges();
        var unstaged = repo.GetUnstagedChanges();

        var stagedDiffs = repo.GetWorkingDiff(staged: true)
            .GroupBy(d => d.Path).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        var unstagedDiffs = repo.GetWorkingDiff(staged: false)
            .GroupBy(d => d.Path).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        // Untracked files aren't in `git diff`; synthesize an all-additions diff.
        foreach (var f in unstaged.Where(f => f.Kind == FileChangeKind.Untracked))
        {
            if (unstagedDiffs.ContainsKey(f.Path)) continue;
            var ud = repo.GetUntrackedFileDiff(f.Path).FirstOrDefault();
            if (ud != null) unstagedDiffs[f.Path] = ud;
        }

        return new ChangesData(staged, unstaged, stagedDiffs, unstagedDiffs);
    }

    private void PickInitialChange(ChangesData data)
    {
        var exists = _changeSelPath != null && (_changeSelStaged
            ? data.Staged.Any(f => f.Path == _changeSelPath)
            : data.Unstaged.Any(f => f.Path == _changeSelPath));
        if (exists) return;

        if (data.Staged.Count > 0) { _changeSelPath = data.Staged[0].Path; _changeSelStaged = true; }
        else if (data.Unstaged.Count > 0) { _changeSelPath = data.Unstaged[0].Path; _changeSelStaged = false; }
        else _changeSelPath = null;
    }

    // ── Left column: file lists + composer ───────────────────────────────

    private Element BuildChangesColumn(ChangesData data)
    {
        var col = Flex.Column(gap: 0).Width(330).FlexShrink(0)
            .BorderColor(Nocturne.Divider).BorderWidth(new Thickness(0, 1, 0, 0));

        var scroll = ScrollView.New().FlexGrow(1).Padding(new Thickness(8, 8, 4, 8));

        if (data.Staged.Count == 0 && data.Unstaged.Count == 0)
        {
            scroll.AddChild(Box.New().Padding(new Thickness(26)).JustifyContent(JustifyContent.Center)
                .AlignItems(AlignItems.Center)
                .Children(Text.New("No changes").FontSize(13f).TextColor(Nocturne.On(0.45))));
        }
        else
        {
            scroll.AddChild(SectionHeader($"Staged · {data.Staged.Count}", Nocturne.Accent300,
                "Unstage all", data.Staged.Count > 0 ? UnstageAll : null));
            foreach (var f in data.Staged)
                scroll.AddChild(ChangeRow(f, isStaged: true));

            scroll.AddChild(SectionHeader($"Changes · {data.Unstaged.Count}", Nocturne.On(0.42),
                "Stage all", data.Unstaged.Count > 0 ? StageAll : null, topGap: true));
            foreach (var f in data.Unstaged)
                scroll.AddChild(ChangeRow(f, isStaged: false));
        }

        col.AddChild(scroll);
        col.AddChild(BuildComposer(data.Staged.Count));
        return col;
    }

    private Element SectionHeader(string label, Color labelColor, string action, Action? onAction, bool topGap = false)
    {
        var row = Flex.Row(gap: 6).AlignItems(AlignItems.Center)
            .Padding(new Thickness(topGap ? 14 : 7, 8, 5, 8));
        row.AddChild(Text.New(label.ToUpperInvariant()).FontSize(10f).TextColor(labelColor).FlexGrow(1));
        if (onAction != null)
            row.AddChild(Ui.GhostButton(action, null, onAction, 11f));
        return row;
    }

    private Element ChangeRow(GitFileChange f, bool isStaged)
    {
        var selected = f.Path == _changeSelPath && _changeSelStaged == isStaged;
        var (letter, color) = StatusLetter(f.Kind);
        var name = System.IO.Path.GetFileName(f.Path);
        var dir = f.Path.Length > name.Length ? f.Path[..^name.Length] : "";

        var row = Flex.Row(gap: 8).AlignItems(AlignItems.Center)
            .Padding(new Thickness(6, 8, 6, 8)).CornerRadius(Nocturne.RadiusRow)
            .Cursor(CursorType.Pointer).Tooltip(f.Path);
        // Selection in the key so a selection change gives a fresh element (see
        // FileTreeRow — the stale hover handler would otherwise clobber the highlight).
        row.Key = (isStaged ? "s:" : "u:") + f.Path + (selected ? "#s" : "");
        if (selected) row.Background(Nocturne.Accent900);

        // Checkbox toggles staging without selecting the row.
        var check = Box.New().FlexShrink(0).Cursor(CursorType.Pointer)
            .Tooltip(isStaged ? "Unstage" : "Stage")
            .Children(Ui.Icon(isStaged ? Icons.CheckSquare : Icons.Square, 14f,
                isStaged ? Nocturne.Accent : Nocturne.On(0.38)));
        check.OnClick(e =>
        {
            e.StopPropagation = true;
            if (isStaged) Unstage(f.Path); else Stage(f.Path);
        });
        row.AddChild(check);

        row.AddChild(Box.New().Width(14).FlexShrink(0)
            .Children(Ui.Mono(letter, 11f, color).FontWeight(FontWeight.Bold)));

        var text = Flex.Column(gap: 0).FlexGrow(1);
        text.AddChild(Text.New(name).FontSize(12f).OverflowMode(TextOverflow.Ellipsis).MaxLines(1));
        if (dir.Length > 0)
            text.AddChild(Text.New(dir).FontSize(9.5f).TextColor(Nocturne.On(0.40))
                .OverflowMode(TextOverflow.Ellipsis).MaxLines(1));
        row.AddChild(text);

        if (f.Additions > 0 || f.Deletions > 0)
            row.AddChild(Flex.Row(gap: 4).FlexShrink(0).Children(
                Text.New($"+{f.Additions}").FontSize(10f).TextColor(Nocturne.AddCount),
                Text.New($"−{f.Deletions}").FontSize(10f).TextColor(Nocturne.DelCount)));

        row.OnClick(_ => SelectChange(f.Path, isStaged));
        row.On<PointerDownEvent>(e => { if (e.Button == 2) SelectChange(f.Path, isStaged); });
        row.ContextMenu(() => ChangeFileMenuItems(f, isStaged));
        if (!selected) row.HoverBg(Nocturne.Neutral900, Color.Transparent);
        return row;
    }

    // ── File context menu ────────────────────────────────────────────────

    private IReadOnlyList<ContextMenuItem> ChangeFileMenuItems(GitFileChange f, bool isStaged)
    {
        var items = new List<ContextMenuItem>();
        if (isStaged)
        {
            items.Add(Item(Icons.MinusCircle, "Unstage", () => Unstage(f.Path)));
            items.Add(Item(Icons.Square, "Unstage all", UnstageAll));
        }
        else
        {
            items.Add(Item(Icons.PlusCircle, "Stage", () => Stage(f.Path)));
            items.Add(Item(Icons.CheckSquare, "Stage all", StageAll));
        }
        items.Add(ContextMenuItem.Separator());
        items.Add(Item(Icons.Copy, "Copy path", () => CopyToClipboard(f.Path)));
        // Discard only from the worktree (unstaged) list, where `git restore`
        // / `git clean` are well-defined; staged rows should be unstaged first.
        if (!isStaged)
        {
            items.Add(ContextMenuItem.Separator());
            items.Add(Danger(Icons.CounterClockwise, "Discard changes", () => DiscardChange(f)));
        }
        return items;
    }

    private void DiscardChange(GitFileChange f)
    {
        if (ActiveRepo is not { } repo) return;
        var ok = repo.DiscardChanges(f);
        Flash(ok ? $"Discarded {System.IO.Path.GetFileName(f.Path)}" : "Discard failed");
        RefreshAll();
    }

    // ── Composer ─────────────────────────────────────────────────────────

    private Element BuildComposer(int stagedCount)
    {
        var box = Flex.Column(gap: 8).FlexShrink(0)
            .Padding(new Thickness(12))
            .BorderColor(Nocturne.Divider).BorderWidth(new Thickness(1, 0, 0, 0));

        // Ctrl/Cmd+Enter commits from either the summary or description field.
        // Handled in the capture phase (on the composer, an ancestor of both
        // fields) so it fires before the text area's own Enter handler inserts
        // a newline.
        box.On<KeyDownEvent>(EventPhase.Capture, e =>
        {
            var mod = e.Modifiers.HasFlag(KeyModifiers.Ctrl) || e.Modifiers.HasFlag(KeyModifiers.Super);
            if (mod && e.Key == KeyCode.Enter)
            {
                e.StopPropagation = true;
                DoCommit();
            }
        });

        // Summary
        var sumBox = Flex.Row(gap: 0).AlignItems(AlignItems.Center)
            .Height(34).Padding(new Thickness(0, 10, 0, 10))
            .Background(Nocturne.Bg).BorderColor(Nocturne.Divider).BorderWidth(1f).CornerRadius(Nocturne.RadiusMd);
        var summary = TextInput.New(_commitSummary);
        summary.PlaceholderText("Summary (required)")
            .OnTextChanged(q => { _commitSummary = q; MarkDirty(); })
            .Height(Dimension.Px(30)).FlexGrow(1).FlexShrink(1).FlexBasis(Dimension.Px(0))
            .TextColor(Nocturne.Text).WithRef(_summaryRef);
        sumBox.AddChild(summary);
        box.AddChild(sumBox);

        // Description
        var descBox = Flex.Column(gap: 0)
            .Padding(new Thickness(6, 10, 6, 10))
            .Background(Nocturne.Bg).BorderColor(Nocturne.Divider).BorderWidth(1f).CornerRadius(Nocturne.RadiusMd);
        var desc = TextArea.New(_commitDescription);
        desc.PlaceholderText("Description")
            .OnTextChanged(q => { _commitDescription = q; MarkDirty(); })
            .Height(Dimension.Px(56))
            .TextColor(Nocturne.Text).WithRef(_descRef);
        descBox.AddChild(desc);
        box.AddChild(descBox);

        var label = $"Commit {stagedCount} staged file{(stagedCount == 1 ? "" : "s")}";
        box.AddChild(Ui.PrimaryButton(label, Icons.GitCommit, DoCommit).Width(Dimension.Percent(100)));
        return box;
    }

    // ── Actions ──────────────────────────────────────────────────────────

    private void SelectChange(string path, bool staged)
    {
        _changeSelPath = path;
        _changeSelStaged = staged;
        MarkDirty();
    }

    private void Stage(string path)
    {
        if (ActiveRepo is not { } repo) return;
        repo.Stage(path);
        _changeSelStaged = true;   // keep the file selected as it moves to staged
        RefreshAll();
    }

    private void Unstage(string path)
    {
        if (ActiveRepo is not { } repo) return;
        repo.Unstage(path);
        _changeSelStaged = false;
        RefreshAll();
    }

    private void StageAll()
    {
        ActiveRepo?.StageAll();
        RefreshAll();
    }

    private void UnstageAll()
    {
        ActiveRepo?.UnstageAll();
        RefreshAll();
    }

    private void DoCommit()
    {
        if (ActiveRepo is not { } repo) return;
        if (_changes.Value.Staged.Count == 0) { Flash("Nothing staged to commit"); return; }
        if (string.IsNullOrWhiteSpace(_commitSummary)) { Flash("Enter a commit summary"); return; }

        var message = _commitSummary.Trim();
        if (!string.IsNullOrWhiteSpace(_commitDescription))
            message += "\n\n" + _commitDescription.Trim();

        var result = repo.Commit(message);
        if (result.Success)
        {
            _commitSummary = "";
            _commitDescription = "";
            Flash("Committed");
            RefreshAll();
        }
        else
        {
            Flash(FirstLine(result.Stderr, "Commit failed"));
        }
    }
}
