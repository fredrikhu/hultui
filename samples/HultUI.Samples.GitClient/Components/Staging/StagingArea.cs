using HultUI;
using HultUI.Core.Builders;
using HultUI.Core.Components;
using HultUI.Core.Elements;
using HultUI.Core.Events;
using HultUI.Core.Layout;
using HultUI.Core.Primitives;
using HultUI.Core.Styling;
using GitClient.Components.Detail;
using GitClient.Components.Shared;
using GitClient.Git;
using GitClient.Theme;

namespace GitClient.Components.Staging;

/// <summary>
/// Working copy staging area: shows unstaged and staged files, commit message, and commit button.
/// Displayed when the "working copy" pseudo-commit is selected.
/// </summary>
public partial class StagingArea : Component
{
    private readonly GitRepository _repo;
    private readonly DiffViewer _diffViewer;
    private string _commitMessage = "";
    private bool _amend;

    // Working-copy file data (status + staged/unstaged partition) is loaded off
    // the UI thread — see LoadWorkingCopy. _refreshGen is the loader key: it's
    // bumped by Refresh() whenever the repo state changes (stage, commit,
    // external edit), so re-showing the view without changes is a cache hit.
    private readonly AsyncLoader<WorkingCopy> _data = new(WorkingCopy.Empty);
    private int _refreshGen;

    private sealed record WorkingCopy(
        List<GitFileChange> Conflicted,
        List<GitFileChange> Unstaged,
        List<GitFileChange> Staged,
        bool Merging)
    {
        public static readonly WorkingCopy Empty = new([], [], [], false);
    }

    // Currently-highlighted file row. Path + section together because the same
    // path can appear under both Unstaged and Staged when partially staged.
    private string? _selectedPath;
    private FileChangeKind? _selectedKind;
    private bool _selectedIsStaged;
    private bool _selectedIsConflict;
    private bool _canCommit;
    private bool _canStash;

    // Folder-expansion state for the tree-rendered file sections. Keyed by
    // "{section}:{fullPath}" where section is "u" (unstaged), "s" (staged), "c"
    // (conflict) — the same folder can appear in multiple sections when files
    // are partially staged, so they need independent expand state.
    private readonly Dictionary<string, bool> _expanded = new();

    private const int IndentPx = 14;
    private const int ChevronWidth = 14;

    private bool IsExpanded(string section, string fullPath) =>
        !_expanded.TryGetValue($"{section}:{fullPath}", out var v) || v;

    private void ToggleExpanded(string section, string fullPath)
    {
        _expanded[$"{section}:{fullPath}"] = !IsExpanded(section, fullPath);
        // Folder expand/collapse is a pure UI change — re-render without
        // re-querying git (MarkDirty, not Refresh).
        MarkDirty();
    }

    public Action? OnCommitted { get; set; }

    /// <summary>
    /// Fired after any staging-area action that mutates the repo beyond a
    /// regular commit (e.g. creating a stash). The host wires this to
    /// refresh peer components like the sidebar (so the new stash appears).
    /// </summary>
    public Action? OnRepoChanged { get; set; }

    /// <summary>
    /// Surfaces git operation results (errors) to the status bar.
    /// </summary>
    public Action<string, bool>? ShowStatusMessage { get; set; }

    public StagingArea(GitRepository repo, DiffViewer diffViewer)
    {
        _repo = repo;
        _diffViewer = diffViewer;
        _diffViewer.OnHunkAction = OnHunkAction;
    }

    public override Element Build()
    {
        // Kick off (or reuse) the off-thread load of status + staged/unstaged
        // partition. Build stays cheap — it renders whatever is currently
        // loaded; when a fresh load lands it triggers another rebuild.
        _data.Load(_refreshGen, LoadWorkingCopy, ReconcileSelection);
        var data = _data.Value;

        var conflicted = data.Conflicted;
        var unstaged = data.Unstaged;
        var staged = data.Staged;
        var merging = data.Merging;

        var children = new List<Element>();

        // Conflicts section (shown when there are unresolved conflicts)
        if (conflicted.Count > 0)
        {
            children.Add(BuildConflictSection(conflicted));
            children.Add(Box.New().Height(1).Background(GitClientColors.Border).FlexShrink(0));
        }

        children.Add(BuildFileSection("Unstaged Changes", unstaged, isStaged: false));
        children.Add(Box.New().Height(1).Background(GitClientColors.Border).FlexShrink(0));
        children.Add(BuildFileSection("Staged Changes", staged, isStaged: true));
        children.Add(Box.New().Height(1).Background(GitClientColors.Border).FlexShrink(0));
        children.Add(BuildCommitArea(staged.Count, unstaged.Count + staged.Count, conflicted.Count, merging));

        return Flex.Column(gap: 0).FlexGrow(1).Children(children.ToArray());
    }

    /// <summary>
    /// Signals that the repo state changed (stage/unstage/commit/external edit),
    /// so the working-copy file data is reloaded off the UI thread. For pure UI
    /// re-renders (selection, folder expand) use <c>MarkDirty()</c> instead so
    /// git isn't re-queried.
    /// </summary>
    public void Refresh()
    {
        _refreshGen++;
        MarkDirty();
    }

    // Runs on a thread-pool thread — must not touch UI or reactive state.
    // Mirrors the old synchronous Build() logic, just relocated off-thread.
    private WorkingCopy LoadWorkingCopy()
    {
        var statusFiles = _repo.GetStatus();
        var merging = _repo.IsMerging();

        // Separate conflicted files from the rest
        var conflicted = statusFiles.Where(f => f.Kind == FileChangeKind.Conflicted).ToList();
        var nonConflict = statusFiles.Where(f => f.Kind != FileChangeKind.Conflicted).ToList();

        List<GitFileChange> unstaged, staged;

        if (_amend)
        {
            // In amend mode, staged = index vs HEAD~1 (includes previous commit's changes).
            staged = _repo.GetStagedFiles("HEAD~1");
            var stagedPaths = staged.Select(f => f.Path).ToHashSet();
            unstaged = nonConflict.Where(f => !stagedPaths.Contains(f.Path)).ToList();
        }
        else
        {
            // Query both sides independently so partially-staged files
            // (some hunks staged, others not) appear in both sections.
            var stagedResult = GitProcess.Run(_repo.Path, "diff", "--cached", "--name-status");
            var stagedPaths = new HashSet<string>();
            foreach (var line in stagedResult.Lines)
            {
                var parts = line.Split('\t');
                if (parts.Length >= 2) stagedPaths.Add(parts[1]);
            }

            var unstagedResult = GitProcess.Run(_repo.Path, "diff", "--name-status");
            var unstagedPaths = new HashSet<string>();
            foreach (var line in unstagedResult.Lines)
            {
                var parts = line.Split('\t');
                if (parts.Length >= 2) unstagedPaths.Add(parts[1]);
            }

            var conflictPaths = conflicted.Select(f => f.Path).ToHashSet();
            unstaged = nonConflict.Where(f => unstagedPaths.Contains(f.Path) || f.Kind == FileChangeKind.Untracked).ToList();
            staged = nonConflict.Where(f => stagedPaths.Contains(f.Path) && !conflictPaths.Contains(f.Path)).ToList();
        }

        return new WorkingCopy(conflicted, unstaged, staged, merging);
    }

    // Runs on the UI thread once fresh data lands — drop a selection that no
    // longer exists so the diff pane doesn't keep showing a stale file.
    private void ReconcileSelection(WorkingCopy data)
    {
        if (_selectedPath == null) return;

        bool stillPresent = _selectedIsConflict
            ? data.Conflicted.Any(f => f.Path == _selectedPath)
            : _selectedIsStaged
                ? data.Staged.Any(f => f.Path == _selectedPath)
                : data.Unstaged.Any(f => f.Path == _selectedPath);

        if (!stillPresent)
        {
            _selectedPath = null;
            _selectedKind = null;
            _diffViewer.Diffs = [];
        }
    }

    public bool TryOpenExternalDiff(ExternalToolOptions options, out string error)
    {
        error = "";
        if (_selectedPath == null)
        {
            error = "Select a working copy file first.";
            return false;
        }

        string left;
        string right;

        if (_selectedIsConflict)
        {
            error = "External merge launch is not wired for conflicts yet.";
            return false;
        }
        else if (_selectedIsStaged)
        {
            left = _repo.GetFileText("HEAD", _selectedPath) ?? "";
            right = _repo.GetIndexFileText(_selectedPath) ?? "";
        }
        else if (_selectedKind == FileChangeKind.Untracked)
        {
            left = "";
            right = _repo.GetWorkingFileText(_selectedPath) ?? "";
        }
        else
        {
            left = _repo.GetIndexFileText(_selectedPath)
                   ?? _repo.GetFileText("HEAD", _selectedPath)
                   ?? "";
            right = _repo.GetWorkingFileText(_selectedPath) ?? "";
        }

        return ExternalDiffLauncher.TryLaunch(options, new ExternalDiffRequest
        {
            RepoPath = _repo.Path,
            RelativePath = _selectedPath,
            LeftLabel = _selectedIsStaged ? "index-base" : "head",
            LeftContent = left,
            RightLabel = _selectedIsStaged ? "index" : "worktree",
            RightContent = right,
        }, out error);
    }

    // ── Conflict section ──

    private Element BuildConflictSection(List<GitFileChange> conflicted)
    {
        var header = Flex.Row(gap: 8)
            .Padding(new Thickness(6, 10, 6, 10))
            .AlignItems(AlignItems.Center)
            .Background(new Color(GitClientColors.StatusConflicted.R, GitClientColors.StatusConflicted.G, GitClientColors.StatusConflicted.B, 20))
            .FlexShrink(0)
            .Children(
                Text.New("Conflicts").FontSize(12f).FontWeight(FontWeight.SemiBold)
                    .TextColor(GitClientColors.StatusConflicted),
                Text.New($"({conflicted.Count})").FontSize(11f).TextColor(GitClientColors.StatusConflicted)
            );

        var fileList = Flex.Column(gap: 0);
        var rows = new List<Element>();
        foreach (var node in PathTree.Build(conflicted))
            AppendConflictNode(node, depth: 0, rows);
        fileList.ReplaceChildren(rows.ToArray());

        return Flex.Column(gap: 0).FlexShrink(0).MaxHeight(Dimension.Px(200)).Children(
            header,
            ScrollView.New().FlexGrow(1).Children(fileList)
        );
    }

    private void AppendConflictNode(PathTreeNode node, int depth, List<Element> rows)
    {
        if (node.IsFile)
        {
            rows.Add(BuildConflictFileRow(node, depth));
            return;
        }
        rows.Add(BuildFolderRow("c", node, depth));
        if (IsExpanded("c", node.FullPath))
            foreach (var child in node.Children) AppendConflictNode(child, depth + 1, rows);
    }

    private Element BuildConflictFileRow(PathTreeNode node, int depth)
    {
        var file = node.File!;
        var isSelected = file.Path == _selectedPath && _selectedIsConflict;
        var leftPad = 8 + depth * IndentPx + ChevronWidth;

        var row = Flex.Row(gap: 6)
            .FlexShrink(0)
            .Padding(new Thickness(4, 8, 4, leftPad))
            .AlignItems(AlignItems.Center)
            .Background(isSelected ? GitClientColors.Selection : Color.Transparent)
            .Cursor(CursorType.Pointer);
        row.Key = $"conflict-file-{file.Path}";

        row.AddChild(Badge.FileStatus(file.Kind));
        row.AddChild(
            Text.New(node.Segment).FontSize(12f)
                .TextColor(isSelected ? GitClientColors.TextPrimary : GitClientColors.TextSecondary)
                .FlexGrow(1)
                .OverflowMode(TextOverflow.Ellipsis).MaxLines(1)
                .Tooltip(file.Path)
        );

        row.AddChild(ConflictActionButton("Ours", "Use current branch version", () =>
        {
            var result = _repo.CheckoutOurs(file.Path);
            if (!result.Success) ShowStatusMessage?.Invoke(result.Stderr.Trim(), true);
            Refresh();
        }));

        row.AddChild(ConflictActionButton("Theirs", "Use incoming branch version", () =>
        {
            var result = _repo.CheckoutTheirs(file.Path);
            if (!result.Success) ShowStatusMessage?.Invoke(result.Stderr.Trim(), true);
            Refresh();
        }));

        row.AddChild(ConflictActionButton("\u2713", "Mark as resolved (stage current content)", () =>
        {
            _repo.Stage(file.Path);
            Refresh();
        }));

        row.OnPointerEnter(_ =>
        {
            if (!IsSelected(file.Path, isStaged: false, isConflict: true))
                row.BackgroundColor = GitClientColors.SurfaceHover;
        });
        row.OnPointerLeave(_ =>
        {
            row.BackgroundColor = IsSelected(file.Path, isStaged: false, isConflict: true)
                ? GitClientColors.Selection
                : Color.Transparent;
        });
        row.OnClick(_ =>
        {
            _selectedPath = file.Path;
            _selectedKind = file.Kind;
            _selectedIsStaged = false;
            _selectedIsConflict = true;
            RefreshConflictDiff(file.Path);
            // Selection highlight is a UI-only change — don't re-query git.
            MarkDirty();
        });

        return row;
    }

    // Folder row shared by all three file sections — the `section` key ("u"/"s"/"c")
    // namespaces expansion state so the same folder can be independently open/closed
    // under Unstaged, Staged, and Conflicts.
    private Element BuildFolderRow(string section, PathTreeNode node, int depth)
    {
        var expanded = IsExpanded(section, node.FullPath);
        var leftPad = 8 + depth * IndentPx;

        var row = Flex.Row(gap: 4)
            .FlexShrink(0)
            .Padding(new Thickness(4, 8, 4, leftPad))
            .AlignItems(AlignItems.Center)
            .Cursor(CursorType.Pointer);
        row.Key = $"staging-{section}-dir-{node.FullPath}";

        row.AddChild(Text.New(expanded ? "\u25BE" : "\u25B8")
            .FontSize(11f).TextColor(GitClientColors.TextMuted).Width(ChevronWidth));
        row.AddChild(Text.New(node.Segment).FontSize(12f)
            .TextColor(GitClientColors.TextSecondary)
            .FlexShrink(1).OverflowMode(TextOverflow.Ellipsis).MaxLines(1));
        row.AddChild(Text.New($"({node.FileCount})").FontSize(11f).TextColor(GitClientColors.TextMuted));

        var fullPath = node.FullPath;
        row.OnPointerEnter(_ => row.BackgroundColor = GitClientColors.SurfaceHover);
        row.OnPointerLeave(_ => row.BackgroundColor = Color.Transparent);
        row.OnClick(_ => ToggleExpanded(section, fullPath));

        return row;
    }

    private static Element ConflictActionButton(string label, string tooltip, Action onClick)
    {
        var btn = Box.New()
            .Padding(new Thickness(2, 6, 2, 6))
            .CornerRadius(3f)
            .Background(GitClientColors.ButtonSecondary)
            .Cursor(CursorType.Pointer)
            .Tooltip(tooltip)
            .Children(Text.New(label).FontSize(10f).FontWeight(FontWeight.SemiBold)
                .TextColor(GitClientColors.TextPrimary));

        btn.On<PointerEnterEvent>(_ => btn.BackgroundColor = GitClientColors.ButtonSecondaryHover);
        btn.On<PointerLeaveEvent>(_ => btn.BackgroundColor = GitClientColors.ButtonSecondary);
        btn.On<ClickEvent>(e =>
        {
            e.StopPropagation = true;
            onClick();
        });

        return btn;
    }

    private void RefreshConflictDiff(string path)
    {
        var diffs = _repo.GetConflictDiff(path);
        _diffViewer.IsStaged = false;
        _diffViewer.Diffs = diffs;
    }

    private Element BuildFileSection(string title, List<GitFileChange> files, bool isStaged)
    {
        var header = Flex.Row(gap: 8)
            .Padding(new Thickness(6, 10, 6, 10))
            .AlignItems(AlignItems.Center)
            .Background(GitClientColors.Surface)
            .FlexShrink(0)
            .Children(
                Text.New(title).FontSize(12f).FontWeight(FontWeight.SemiBold)
                    .TextColor(GitClientColors.TextPrimary),
                Text.New($"({files.Count})").FontSize(11f).TextColor(GitClientColors.TextMuted),
                Box.New().FlexGrow(1),
                StageAllButton(isStaged ? "Unstage All" : "Stage All", () =>
                {
                    if (isStaged) _repo.UnstageAll();
                    else _repo.StageAll();
                    Refresh();
                })
            );

        var section = isStaged ? "s" : "u";
        var fileList = Flex.Column(gap: 0);
        var rows = new List<Element>();
        foreach (var node in PathTree.Build(files))
            AppendStagingNode(node, depth: 0, section, isStaged, rows);
        fileList.ReplaceChildren(rows.ToArray());

        return Flex.Column(gap: 0).FlexGrow(1).Children(
            header,
            ScrollView.New().FlexGrow(1).Children(fileList)
        );
    }

    private void AppendStagingNode(PathTreeNode node, int depth, string section, bool isStaged, List<Element> rows)
    {
        if (node.IsFile)
        {
            rows.Add(BuildStagingFileRow(node, depth, isStaged));
            return;
        }
        rows.Add(BuildFolderRow(section, node, depth));
        if (IsExpanded(section, node.FullPath))
            foreach (var child in node.Children) AppendStagingNode(child, depth + 1, section, isStaged, rows);
    }

    private Element BuildStagingFileRow(PathTreeNode node, int depth, bool isStaged)
    {
        var file = node.File!;
        var isSelected = file.Path == _selectedPath && isStaged == _selectedIsStaged && !_selectedIsConflict;
        var leftPad = 8 + depth * IndentPx + ChevronWidth;

        var row = Flex.Row(gap: 6)
            .FlexShrink(0)
            .Padding(new Thickness(4, 8, 4, leftPad))
            .AlignItems(AlignItems.Center)
            .Background(isSelected ? GitClientColors.Selection : Color.Transparent)
            .Cursor(CursorType.Pointer);
        row.Key = $"staging-{(isStaged ? "s" : "u")}-file-{file.Path}";

        row.AddChild(Badge.FileStatus(file.Kind));
        row.AddChild(
            Text.New(node.Segment).FontSize(12f)
                .TextColor(isSelected ? GitClientColors.TextPrimary : GitClientColors.TextSecondary)
                .FlexGrow(1)
                .OverflowMode(TextOverflow.Ellipsis).MaxLines(1)
                .Tooltip(file.Path)
        );

        // Discard button (unstaged only — destructive, no confirmation in this sample)
        if (!isStaged)
        {
            var discardBtn = Box.New()
                .Width(20).Height(18)
                .CornerRadius(3f)
                .Background(GitClientColors.ButtonSecondary)
                .JustifyContent(JustifyContent.Center)
                .AlignItems(AlignItems.Center)
                .Cursor(CursorType.Pointer)
                .Tooltip("Discard changes")
                .Children(Text.New("\u2715").FontSize(11f).TextColor(GitClientColors.StatusDeleted));

            discardBtn.On<ClickEvent>(e =>
            {
                e.StopPropagation = true;
                _repo.DiscardChanges(file);
                Refresh();
            });
            row.AddChild(discardBtn);
        }

        // Stage/unstage button
        var actionLabel = isStaged ? "\u2212" : "+";
        var actionBtn = Box.New()
            .Width(20).Height(18)
            .CornerRadius(3f)
            .Background(GitClientColors.ButtonSecondary)
            .JustifyContent(JustifyContent.Center)
            .AlignItems(AlignItems.Center)
            .Cursor(CursorType.Pointer)
            .Children(Text.New(actionLabel).FontSize(12f).TextColor(GitClientColors.TextPrimary));

        actionBtn.On<ClickEvent>(e =>
        {
            e.StopPropagation = true;
            if (isStaged) _repo.Unstage(file.Path);
            else _repo.Stage(file.Path);
            Refresh();
        });
        row.AddChild(actionBtn);

        row.OnPointerEnter(_ =>
        {
            if (!IsSelected(file.Path, isStaged, isConflict: false))
                row.BackgroundColor = GitClientColors.SurfaceHover;
        });
        row.OnPointerLeave(_ =>
        {
            row.BackgroundColor = IsSelected(file.Path, isStaged, isConflict: false)
                ? GitClientColors.Selection
                : Color.Transparent;
        });
        row.OnClick(_ =>
        {
            _selectedPath = file.Path;
            _selectedKind = file.Kind;
            _selectedIsStaged = isStaged;
            _selectedIsConflict = false;
            RefreshDiffViewer();
            // Selection highlight is a UI-only change — don't re-query git.
            MarkDirty();
        });

        return row;
    }

    private Element BuildCommitArea(int stagedCount, int totalChanged, int conflictCount = 0, bool merging = false)
    {
        _canCommit = conflictCount == 0;
        _canStash = totalChanged > 0;

        var placeholder = merging ? "Merge commit message..."
            : _amend ? "Amend commit message..."
            : "Commit message...";

        var messageInput = TextArea.New(_commitMessage)
            .PlaceholderText(placeholder)
            .OnTextChanged(text => _commitMessage = text)
            .Height(Dimension.Px(80))
            .Background(GitClientColors.Background)
            .CornerRadius(4f)
            .BorderColor(GitClientColors.Border)
            .TextColor(GitClientColors.TextPrimary)
            .FontSize(13f);

        var children = new List<Element> { messageInput };

        // Hide amend checkbox during merge (it doesn't apply)
        if (!merging)
        {
            var amendCheckbox = Checkbox.New(_amend, "Amend last commit")
                .OnCheckedChanged(ToggleAmend)
                .FontSize(12f)
                .TextColor(GitClientColors.TextSecondary);
            amendCheckbox.CheckboxSize = 14f;
            amendCheckbox.CheckedColor = GitClientColors.ButtonPrimary;
            children.Add(amendCheckbox);
        }

        if (conflictCount > 0)
        {
            children.Add(
                Text.New($"{conflictCount} conflict{(conflictCount == 1 ? "" : "s")} remaining")
                    .FontSize(11f).TextColor(GitClientColors.StatusConflicted)
            );
        }

        children.Add(Flex.Row(gap: 8).Children(
            CommitButton(stagedCount, conflictCount > 0),
            StashButton(totalChanged)
        ));

        var commitArea = Flex.Column(gap: 8)
            .Padding(10)
            .Background(GitClientColors.Surface)
            .FlexShrink(0)
            .Children(children.ToArray());

        commitArea.On<KeyDownEvent>(EventPhase.Capture, e =>
        {
            if (e.Target is not TextAreaElement
                || e.Key != KeyCode.Enter
                || !e.Modifiers.HasFlag(KeyModifiers.Ctrl))
                return;

            e.StopPropagation = true;
            if (_canCommit) DoCommit();
        });

        return commitArea;
    }

    private void ToggleAmend(bool amend)
    {
        _amend = amend;
        if (amend)
        {
            var lastMessage = _repo.GetLastCommitMessage();
            if (lastMessage != null)
                _commitMessage = lastMessage;
        }
        Refresh();
    }

    private Element CommitButton(int stagedCount, bool hasConflicts = false)
    {
        var label = _amend
            ? "Amend Commit"
            : $"Commit ({stagedCount} file{(stagedCount == 1 ? "" : "s")})";

        var bg = _canCommit ? GitClientColors.ButtonPrimary : GitClientColors.Surface;
        var fg = _canCommit ? Color.White : GitClientColors.TextMuted;

        var btn = Box.New()
            .Padding(new Thickness(8, 0, 8, 0))
            .CornerRadius(4f)
            .Background(bg)
            .JustifyContent(JustifyContent.Center)
            .AlignItems(AlignItems.Center)
            .Height(Dimension.Px(32))
            .FlexGrow(1)
            .Cursor(_canCommit ? CursorType.Pointer : CursorType.Default)
            .Children(
                Text.New(label)
                    .FontSize(13f).FontWeight(FontWeight.SemiBold)
                    .TextColor(fg)
            );

        if (hasConflicts)
            btn.TooltipText = "Resolve all conflicts before committing";

        btn.On<PointerEnterEvent>(_ => { if (_canCommit) btn.BackgroundColor = GitClientColors.ButtonPrimaryHover; });
        btn.On<PointerLeaveEvent>(_ => btn.BackgroundColor = _canCommit ? GitClientColors.ButtonPrimary : GitClientColors.Surface);
        btn.On<ClickEvent>(_ => { if (_canCommit) DoCommit(); });

        return btn;
    }

    private Element StashButton(int totalChanged)
    {
        var enabled = totalChanged > 0;
        var bg = enabled ? GitClientColors.ButtonSecondary : GitClientColors.Surface;
        var fg = enabled ? GitClientColors.TextPrimary : GitClientColors.TextMuted;

        var btn = Box.New()
            .Padding(new Thickness(8, 12, 8, 12))
            .CornerRadius(4f)
            .Background(bg)
            .JustifyContent(JustifyContent.Center)
            .AlignItems(AlignItems.Center)
            .Height(Dimension.Px(32))
            .FlexShrink(0)
            .Cursor(enabled ? CursorType.Pointer : CursorType.Default)
            .Tooltip(enabled
                ? "Stash all changes (uses commit message as label if non-empty)"
                : "Nothing to stash")
            .Children(
                Text.New("Stash")
                    .FontSize(13f).FontWeight(FontWeight.SemiBold)
                    .TextColor(fg)
            );

        // Handlers are always attached — PatchFrom keeps the first Build's
        // handlers, so conditional attachment only works on the first render.
        // Read _canStash (a field) inside the handler instead.
        btn.On<PointerEnterEvent>(_ => { if (_canStash) btn.BackgroundColor = GitClientColors.ButtonSecondaryHover; });
        btn.On<PointerLeaveEvent>(_ => { if (_canStash) btn.BackgroundColor = GitClientColors.ButtonSecondary; });
        btn.On<ClickEvent>(_ => { if (_canStash) DoStash(); });

        return btn;
    }

    private void DoCommit()
    {
        if (string.IsNullOrWhiteSpace(_commitMessage)) return;

        var result = _amend
            ? _repo.CommitAmend(_commitMessage)
            : _repo.Commit(_commitMessage);

        if (result.Success)
        {
            _commitMessage = "";
            _amend = false;
            OnCommitted?.Invoke();
            Refresh();
        }
        else
        {
            var msg = result.Stderr.Trim();
            if (string.IsNullOrEmpty(msg)) msg = "Commit failed";
            ShowStatusMessage?.Invoke(msg, true);
        }
    }

    private void DoStash()
    {
        var label = string.IsNullOrWhiteSpace(_commitMessage) ? null : _commitMessage;
        var result = _repo.StashCreate(label);
        if (result.Success)
        {
            _commitMessage = "";
            OnRepoChanged?.Invoke();
            Refresh();
        }
        else
        {
            var msg = result.Stderr.Trim();
            if (string.IsNullOrEmpty(msg)) msg = "Stash failed";
            ShowStatusMessage?.Invoke(msg, true);
        }
    }

    private void RefreshDiffViewer()
    {
        if (_selectedPath == null) return;
        if (!_selectedIsStaged && _selectedKind == FileChangeKind.Untracked)
        {
            _diffViewer.IsStaged = false;
            _diffViewer.Diffs = _repo.GetUntrackedFileDiff(_selectedPath);
            return;
        }

        var baseRef = _amend && _selectedIsStaged ? "HEAD~1" : null;
        var diffs = _repo.GetWorkingDiff(_selectedIsStaged, baseRef);
        var fileDiff = diffs.Where(d => d.Path == _selectedPath || d.OldPath == _selectedPath).ToList();
        _diffViewer.IsStaged = _selectedIsStaged;
        _diffViewer.Diffs = fileDiff;
    }

    private bool IsSelected(string path, bool isStaged, bool isConflict) =>
        _selectedPath == path
        && _selectedIsStaged == isStaged
        && _selectedIsConflict == isConflict;

    private void OnHunkAction(GitFileDiff file, GitDiffHunk hunk, HunkActionKind action)
    {
        var patch = GitRepository.BuildHunkPatch(file, hunk);

        var result = action switch
        {
            HunkActionKind.Stage => _repo.ApplyPatch(patch, reverse: false, cached: true),
            HunkActionKind.Unstage => _repo.ApplyPatch(patch, reverse: true, cached: true),
            HunkActionKind.Discard => _repo.ApplyPatch(patch, reverse: true, cached: false),
            _ => throw new ArgumentOutOfRangeException(nameof(action)),
        };

        if (!result.Success)
        {
            var msg = result.Stderr.Trim();
            if (string.IsNullOrEmpty(msg)) msg = $"Hunk {action.ToString().ToLower()} failed";
            ShowStatusMessage?.Invoke(msg, true);
            return;
        }

        // Re-fetch diff for the same file to reflect the new state
        RefreshDiffViewer();
        Refresh();
    }

    private static Element StageAllButton(string label, Action onClick)
    {
        var btn = Box.New()
            .Padding(new Thickness(3, 8, 3, 8))
            .CornerRadius(3f)
            .Background(GitClientColors.ButtonSecondary)
            .Cursor(CursorType.Pointer)
            .Children(Text.New(label).FontSize(10f).TextColor(GitClientColors.TextPrimary));

        btn.On<PointerEnterEvent>(_ => btn.BackgroundColor = GitClientColors.ButtonSecondaryHover);
        btn.On<PointerLeaveEvent>(_ => btn.BackgroundColor = GitClientColors.ButtonSecondary);
        btn.On<ClickEvent>(_ => onClick());

        return btn;
    }
}
