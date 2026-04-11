using HultUI;
using HultUI.Core.Builders;
using HultUI.Core.Components;
using HultUI.Core.Elements;
using HultUI.Core.Layout;
using HultUI.Core.Primitives;
using HultUI.Core.Reactivity;
using HultUI.Core.Styling;
using GitClient.Components.Detail;
using GitClient.Components.History;
using GitClient.Components.Layout;
using GitClient.Components.Shared;
using GitClient.Components.Staging;
using GitClient.Git;
using GitClient.Theme;

namespace GitClient;

/// <summary>
/// Root application component. Orchestrates the main layout and connects
/// the git backend to all sub-components via reactive state.
/// </summary>
public partial class App : Component
{
    private readonly Window _window;
    private readonly GitRepository _repo;

    // Sub-components
    private readonly Sidebar _sidebar;
    private readonly CommitList _commitList;
    private readonly CommitDetail _commitDetail;
    private readonly DiffViewer _workingDiffViewer;
    private readonly StagingArea _stagingArea;
    private readonly SplitPane _sidebarSplit;
    private readonly SplitPane _mainSplit;

    // State
    [Reactive] public partial bool ShowWorkingCopy { get; set; }

    private Element? _workingCopyLayout;

    public App(Window window, GitRepository repo)
    {
        _window = window;
        _repo = repo;

        _sidebar = new Sidebar(repo);
        _commitList = new CommitList(repo);
        _commitDetail = new CommitDetail(repo);
        _workingDiffViewer = new DiffViewer();
        _stagingArea = new StagingArea(repo, _workingDiffViewer);
        _sidebarSplit = new SplitPane(horizontal: true, initialRatio: 0.18f);
        _mainSplit = new SplitPane(horizontal: false, initialRatio: 0.45f);

        // Wire up events
        _commitList.OnCommitSelected = OnCommitSelected;
        _commitList.OnWorkingCopySelected = OnWorkingCopySelected;
        _commitList.CopyToClipboard = CopyToClipboard;
        _commitList.OnCheckoutCommit = DoCheckoutCommit;
        _commitList.OnCherryPick = DoCherryPick;
        _sidebar.OnBranchSelected = OnBranchSelected;
        _sidebar.OnStashSelected = OnStashSelected;
        _sidebar.CopyToClipboard = CopyToClipboard;
        _sidebar.OnRepoChanged = RefreshAll;
        _stagingArea.OnCommitted = OnCommitted;
        _stagingArea.OnRepoChanged = RefreshAll;
    }

    public override Element Build()
    {
        var currentBranch = _repo.GetCurrentBranch();
        var branches = _repo.GetBranches();
        var currentBranchInfo = branches.FirstOrDefault(b => b.IsCurrent || b.Name == currentBranch);
        var status = _repo.GetStatus();
        var showWc = ShowWorkingCopy;
        var repoName = Path.GetFileName(_repo.Path.TrimEnd(Path.DirectorySeparatorChar));

        return Flex.Column(gap: 0)
            .TextColor(GitClientColors.TextPrimary)
            .FontSize(13f)
            .Children(
                // Title bar
                TitleBar.Build(_window, repoName, currentBranch),

                // Tab bar
                TabBar.Build(_repo.Path),

                // Main content area (sidebar + history/detail)
                BuildSidebarLayout(showWc),

                // Status bar
                StatusBar.Build(
                    currentBranch,
                    currentBranchInfo?.AheadCount ?? 0,
                    currentBranchInfo?.BehindCount ?? 0,
                    status.Count,
                    onFetch: DoFetch,
                    onPull: DoPull,
                    onPush: DoPush
                )
            );
    }

    private Element BuildSidebarLayout(bool showWc)
    {
        return _sidebarSplit.Build(
            _sidebar.Mount(),
            _mainSplit.Build(
                _commitList.Mount(),
                showWc ? BuildWorkingCopyPanel() : _commitDetail.Mount()
            )
        );
    }

    private Element BuildWorkingCopyPanel()
    {
        if (_workingCopyLayout == null)
        {
            var splitPane = new SplitPane(horizontal: true, initialRatio: 0.35f);
            _workingCopyLayout = splitPane.Build(
                _stagingArea.Mount(),
                _workingDiffViewer.Mount()
            );
        }
        return _workingCopyLayout;
    }

    // ── Event handlers ──

    private void OnCommitSelected(GitCommit? commit)
    {
        if (commit != null)
        {
            ShowWorkingCopy = false;
            _commitDetail.Commit = commit;
        }
    }

    private void OnWorkingCopySelected()
    {
        ShowWorkingCopy = true;
        _stagingArea.Refresh();
    }

    private void OnBranchSelected(string branchName)
    {
        _repo.Checkout(branchName);
        RefreshAll();
    }

    private void OnStashSelected(GitStash stash)
    {
        // Stashes are real commit objects — load the WIP commit and feed it
        // into CommitDetail just like a regular commit. The diff against the
        // stash's first parent is what `git stash show -p` would print.
        var commit = _repo.GetCommit(stash.Ref);
        if (commit == null) return;

        ShowWorkingCopy = false;
        _commitDetail.Commit = commit;
    }

    private void OnCommitted()
    {
        RefreshAll();
    }

    private void DoCheckoutCommit(string hash)
    {
        _repo.CheckoutDetached(hash);
        RefreshAll();
    }

    private void DoCherryPick(string hash)
    {
        _repo.CherryPick(hash);
        RefreshAll();
    }

    private void DoFetch()
    {
        _repo.Fetch();
        RefreshAll();
    }

    private void DoPull()
    {
        _repo.Pull();
        RefreshAll();
    }

    private void DoPush()
    {
        _repo.Push();
        RefreshAll();
    }

    private void CopyToClipboard(string text)
    {
        _window.Clipboard.SetText(text);
    }

    private void RefreshAll()
    {
        _sidebar.Refresh();
        _commitList.Refresh();
        _stagingArea.Refresh();
        MarkDirty();
    }
}
