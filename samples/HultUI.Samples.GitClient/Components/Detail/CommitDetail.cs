using HultUI.Core.Builders;
using HultUI.Core.Components;
using HultUI.Core.Elements;
using HultUI.Core.Layout;
using HultUI.Core.Primitives;
using HultUI.Core.Reactivity;
using HultUI.Core.Styling;
using GitClient.Components.Shared;
using GitClient.Git;
using GitClient.Theme;

namespace GitClient.Components.Detail;

/// <summary>
/// Shows the detail for a selected commit: file list + diff viewer in a horizontal split.
/// </summary>
public partial class CommitDetail : Component
{
    private readonly GitRepository _repo;
    private readonly FileTree _fileTree;
    private readonly DiffViewer _diffViewer;
    private readonly BlameViewer _blameViewer;
    private readonly SplitPane _splitPane;

    // Memoized per-commit data — only refreshed when Commit.Hash changes.
    // Keeps file selection stable across rebuilds and avoids redundant `git diff` calls.
    private string? _loadedCommitHash;
    private List<GitFileChange> _loadedFiles = [];
    private List<GitFileDiff> _loadedDiffs = [];

    // Blame mode — when non-null, the right pane shows the blame for this file
    // at the current commit instead of the diff.
    private string? _blameFilePath;

    [Reactive] public partial GitCommit? Commit { get; set; }

    /// <summary>
    /// When set, CommitDetail pre-selects this file path (if present in the current
    /// commit's file list) instead of defaulting to the first file. Used by the file
    /// history flow to keep focus on the file the user is following.
    /// </summary>
    public string? PreferredFilePath { get; set; }

    public Action<string>? OnCommitHashSelected { get; set; }
    public Action<string>? OnFileHistoryRequested { get; set; }

    public CommitDetail(GitRepository repo)
    {
        _repo = repo;
        _fileTree = new FileTree();
        _diffViewer = new DiffViewer();
        _blameViewer = new BlameViewer();
        _splitPane = new SplitPane(horizontal: true, initialRatio: 0.3f);

        _fileTree.OnFileSelected = OnFileSelected;
        _fileTree.OnBlameRequested = OnBlameRequested;
        _fileTree.OnHistoryRequested = file => OnFileHistoryRequested?.Invoke(file.Path);
        _blameViewer.OnCommitSelected = hash => OnCommitHashSelected?.Invoke(hash);
        _blameViewer.OnClose = () => { _blameFilePath = null; MarkDirty(); };
    }

    public override Element Build()
    {
        var commit = Commit;

        if (commit == null)
        {
            return Box.New().FlexGrow(1)
                .JustifyContent(JustifyContent.Center).AlignItems(AlignItems.Center)
                .Background(GitClientColors.Background)
                .Children(
                    Text.New("Select a commit to view details").FontSize(13f).TextColor(GitClientColors.TextMuted)
                );
        }

        if (commit.Hash != _loadedCommitHash)
        {
            _loadedFiles = _repo.GetCommitFiles(commit.Hash);
            _loadedDiffs = _repo.GetCommitDiff(commit.Hash);
            _loadedCommitHash = commit.Hash;
            _blameFilePath = null;

            _fileTree.Files = _loadedFiles;

            if (_loadedFiles.Count > 0)
            {
                var initialFile = (PreferredFilePath != null
                    ? _loadedFiles.FirstOrDefault(f => f.Path == PreferredFilePath)
                    : null) ?? _loadedFiles[0];
                _fileTree.SelectedFile = initialFile;
                _diffViewer.Diffs = DiffsForFile(initialFile);
            }
            else
            {
                _fileTree.SelectedFile = null;
                _diffViewer.Diffs = [];
            }
        }

        // Commit info header
        var header = Flex.Column(gap: 4)
            .Padding(new Thickness(8, 12, 8, 12))
            .Background(GitClientColors.Surface)
            .FlexShrink(0)
            .Children(
                Flex.Row(gap: 8).AlignItems(AlignItems.Center).Children(
                    Text.New(commit.Subject).FontSize(13f).FontWeight(FontWeight.SemiBold)
                        .TextColor(GitClientColors.TextPrimary)
                        .FlexGrow(1)
                        .OverflowMode(TextOverflow.Ellipsis).MaxLines(1),
                    Text.New(commit.ShortHash).FontSize(11f).TextColor(GitClientColors.TextMuted)
                        .Tooltip(commit.Hash)
                ),
                Flex.Row(gap: 8).AlignItems(AlignItems.Center).Children(
                    Text.New(commit.AuthorName).FontSize(12f).TextColor(GitClientColors.TextSecondary),
                    Text.New("\u2022").FontSize(8f).TextColor(GitClientColors.TextMuted),
                    Text.New(commit.AuthorDate.ToString("yyyy-MM-dd HH:mm")).FontSize(12f)
                        .TextColor(GitClientColors.TextMuted),
                    Text.New("\u2022").FontSize(8f).TextColor(GitClientColors.TextMuted),
                    Text.New($"{_loadedFiles.Count} file{(_loadedFiles.Count == 1 ? "" : "s")} changed").FontSize(12f)
                        .TextColor(GitClientColors.TextMuted)
                )
            );

        var rightPane = _blameFilePath != null
            ? (Element)_blameViewer.Mount()
            : _diffViewer.Mount();

        return Flex.Column(gap: 0).FlexGrow(1).Children(
            header,
            _splitPane.Build(_fileTree.Mount(), rightPane)
        );
    }

    private void OnFileSelected(GitFileChange file)
    {
        // Selecting a different file exits blame mode — blame is file-scoped.
        _blameFilePath = null;
        _diffViewer.Diffs = DiffsForFile(file);
    }

    private void OnBlameRequested(GitFileChange file)
    {
        var commit = Commit;
        if (commit == null) return;

        _blameFilePath = file.Path;
        _blameViewer.FilePath = file.Path;
        _blameViewer.Lines = _repo.Blame(file.Path, commit.Hash);
        MarkDirty();
    }

    private List<GitFileDiff> DiffsForFile(GitFileChange file)
    {
        var fileDiff = _loadedDiffs
            .Where(d => d.Path == file.Path || d.Path.EndsWith(file.Path))
            .ToList();
        return fileDiff.Count > 0 ? fileDiff : _loadedDiffs;
    }
}
