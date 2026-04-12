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

    // Currently-highlighted file row. Path + section together because the same
    // path can appear under both Unstaged and Staged when partially staged.
    private string? _selectedPath;
    private bool _selectedIsStaged;
    private bool _canStash;

    public Action? OnCommitted { get; set; }

    /// <summary>
    /// Fired after any staging-area action that mutates the repo beyond a
    /// regular commit (e.g. creating a stash). The host wires this to
    /// refresh peer components like the sidebar (so the new stash appears).
    /// </summary>
    public Action? OnRepoChanged { get; set; }

    public StagingArea(GitRepository repo, DiffViewer diffViewer)
    {
        _repo = repo;
        _diffViewer = diffViewer;
    }

    public override Element Build()
    {
        var statusFiles = _repo.GetStatus();

        // Split into staged and unstaged based on git status porcelain codes
        var stagedResult = GitProcess.Run(_repo.Path, "diff", "--cached", "--name-status");
        var stagedPaths = new HashSet<string>();
        foreach (var line in stagedResult.Lines)
        {
            var parts = line.Split('\t');
            if (parts.Length >= 2) stagedPaths.Add(parts[1]);
        }

        var unstaged = statusFiles.Where(f => !stagedPaths.Contains(f.Path)).ToList();
        var staged = statusFiles.Where(f => stagedPaths.Contains(f.Path)).ToList();

        // Drop the highlight if the previously-selected file is no longer in
        // the section it was clicked from (e.g. it was just staged or discarded).
        if (_selectedPath != null)
        {
            var sectionFiles = _selectedIsStaged ? staged : unstaged;
            if (!sectionFiles.Any(f => f.Path == _selectedPath))
            {
                _selectedPath = null;
                _diffViewer.Diffs = [];
            }
        }

        return Flex.Column(gap: 0).FlexGrow(1).Children(
            // Unstaged changes
            BuildFileSection("Unstaged Changes", unstaged, isStaged: false),

            // Separator
            Box.New().Height(1).Background(GitClientColors.Border).FlexShrink(0),

            // Staged changes
            BuildFileSection("Staged Changes", staged, isStaged: true),

            // Separator
            Box.New().Height(1).Background(GitClientColors.Border).FlexShrink(0),

            // Commit area
            BuildCommitArea(staged.Count, unstaged.Count + staged.Count)
        );
    }

    public void Refresh() => MarkDirty();

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

        var fileList = Flex.Column(gap: 0);
        var rows = new Element[files.Count];
        for (int i = 0; i < files.Count; i++)
        {
            var file = files[i];
            var isSelected = file.Path == _selectedPath && isStaged == _selectedIsStaged;

            var row = Flex.Row(gap: 6)
                .FlexShrink(0)
                .Padding(new Thickness(4, 8, 4, 24))
                .AlignItems(AlignItems.Center)
                .Background(isSelected ? GitClientColors.Selection : Color.Transparent)
                .Cursor(CursorType.Pointer);
            row.Key = $"staging-{(isStaged ? "s" : "u")}-{file.Path}";

            row.AddChild(Badge.FileStatus(file.Kind));
            row.AddChild(
                Text.New(Path.GetFileName(file.Path)).FontSize(12f)
                    .TextColor(isSelected ? GitClientColors.TextPrimary : GitClientColors.TextSecondary)
                    .FlexGrow(1)
                    .OverflowMode(TextOverflow.Ellipsis).MaxLines(1)
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

            // Click row to view diff
            row.OnPointerEnter(_ => { if (!isSelected) row.BackgroundColor = GitClientColors.SurfaceHover; });
            row.OnPointerLeave(_ => { if (!isSelected) row.BackgroundColor = Color.Transparent; });
            row.OnClick(_ =>
            {
                _selectedPath = file.Path;
                _selectedIsStaged = isStaged;
                var diffs = _repo.GetWorkingDiff(isStaged);
                var fileDiff = diffs.Where(d => d.Path == file.Path || d.Path.EndsWith(file.Path)).ToList();
                _diffViewer.Diffs = fileDiff.Count > 0 ? fileDiff : diffs;
                Refresh();
            });

            rows[i] = row;
        }
        fileList.ReplaceChildren(rows);

        return Flex.Column(gap: 0).FlexGrow(1).Children(
            header,
            ScrollView.New().FlexGrow(1).Children(fileList)
        );
    }

    private Element BuildCommitArea(int stagedCount, int totalChanged)
    {
        _canStash = totalChanged > 0;

        return Flex.Column(gap: 8)
            .Padding(10)
            .Background(GitClientColors.Surface)
            .FlexShrink(0)
            .Children(
                TextArea.New(_commitMessage)
                    .PlaceholderText("Commit message...")
                    .OnTextChanged(text => _commitMessage = text)
                    .Height(Dimension.Px(80))
                    .Background(GitClientColors.Background)
                    .CornerRadius(4f)
                    .BorderColor(GitClientColors.Border)
                    .TextColor(GitClientColors.TextPrimary)
                    .FontSize(13f),
                Flex.Row(gap: 8).Children(
                    CommitButton(stagedCount),
                    StashButton(totalChanged)
                )
            );
    }

    private Element CommitButton(int stagedCount)
    {
        var btn = Box.New()
            .Padding(new Thickness(8, 0, 8, 0))
            .CornerRadius(4f)
            .Background(GitClientColors.ButtonPrimary)
            .JustifyContent(JustifyContent.Center)
            .AlignItems(AlignItems.Center)
            .Height(Dimension.Px(32))
            .FlexGrow(1)
            .Cursor(CursorType.Pointer)
            .Children(
                Text.New($"Commit ({stagedCount} file{(stagedCount == 1 ? "" : "s")})")
                    .FontSize(13f).FontWeight(FontWeight.SemiBold)
                    .TextColor(Color.White)
            );

        btn.On<PointerEnterEvent>(_ => btn.BackgroundColor = GitClientColors.ButtonPrimaryHover);
        btn.On<PointerLeaveEvent>(_ => btn.BackgroundColor = GitClientColors.ButtonPrimary);
        btn.On<ClickEvent>(_ => DoCommit());

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

        var result = _repo.Commit(_commitMessage);
        if (result.Success)
        {
            _commitMessage = "";
            OnCommitted?.Invoke();
            Refresh();
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

