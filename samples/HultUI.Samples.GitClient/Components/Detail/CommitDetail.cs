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
    private readonly SplitPane _splitPane;

    [Reactive] public partial GitCommit? Commit { get; set; }

    public CommitDetail(GitRepository repo)
    {
        _repo = repo;
        _fileTree = new FileTree();
        _diffViewer = new DiffViewer();
        _splitPane = new SplitPane(horizontal: true, initialRatio: 0.3f);

        _fileTree.OnFileSelected = OnFileSelected;
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

        // Load commit data
        var files = _repo.GetCommitFiles(commit.Hash);
        _fileTree.Files = files;

        // Load diff and show only the first file by default
        var diffs = _repo.GetCommitDiff(commit.Hash);
        if (diffs.Count > 0)
        {
            _diffViewer.Diffs = [diffs[0]];
            if (files.Count > 0)
                _fileTree.SelectedFile = files[0];
        }
        else
        {
            _diffViewer.Diffs = [];
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
                    Text.New($"{files.Count} file{(files.Count == 1 ? "" : "s")} changed").FontSize(12f)
                        .TextColor(GitClientColors.TextMuted)
                )
            );

        return Flex.Column(gap: 0).FlexGrow(1).Children(
            header,
            _splitPane.Build(
                _fileTree.Mount(),
                _diffViewer.Mount()
            )
        );
    }

    private void OnFileSelected(GitFileChange file)
    {
        var commit = Commit;
        if (commit == null) return;

        // Filter diffs to show only the selected file
        var allDiffs = _repo.GetCommitDiff(commit.Hash);
        var fileDiff = allDiffs.Where(d =>
            d.Path == file.Path ||
            d.Path.EndsWith(file.Path)
        ).ToList();

        _diffViewer.Diffs = fileDiff.Count > 0 ? fileDiff : allDiffs;
    }
}
