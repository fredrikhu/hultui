using HultUI.Core.Builders;
using HultUI.Core.Components;
using HultUI.Core.Elements;
using HultUI.Core.Layout;
using HultUI.Core.Primitives;
using HultUI.Core.Reactivity;
using HultUI.Core.Styling;
using GitClient.Git;
using GitClient.Theme;

namespace GitClient.Components.History;

public partial class CommitList : Component
{
    private readonly GitRepository _repo;
    private List<GitCommit> _commits = [];

    [Reactive] public partial GitCommit? SelectedCommit { get; set; }
    [Reactive] public partial int RefreshTrigger { get; set; }

    public Action<GitCommit?>? OnCommitSelected { get; set; }

    public CommitList(GitRepository repo)
    {
        _repo = repo;
    }

    public override Element Build()
    {
        _ = RefreshTrigger;
        var selected = SelectedCommit;
        _commits = _repo.GetLog();

        if (_commits.Count == 0)
        {
            return Box.New().FlexGrow(1).JustifyContent(JustifyContent.Center).AlignItems(AlignItems.Center)
                .Children(
                    Text.New("No commits yet").FontSize(14f).TextColor(GitClientColors.TextMuted)
                );
        }

        return VirtualListElement<GitCommit>.New()
            .ItemHeight(36)
            .Items(_commits)
            .RenderWith(commit => CommitRow.Build(commit, commit == selected, SelectCommit))
            .FlexGrow(1)
            .Background(GitClientColors.Background);
    }

    public void Refresh()
    {
        RefreshTrigger++;
    }

    private void SelectCommit(GitCommit commit)
    {
        SelectedCommit = commit;
        OnCommitSelected?.Invoke(commit);
    }
}
