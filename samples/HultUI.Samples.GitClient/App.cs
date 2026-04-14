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
    private readonly SplitPane _workingCopySplit;

    // State
    [Reactive] public partial bool ShowWorkingCopy { get; set; }

    private string? _statusMessage;
    private bool _statusIsError;
    private CancellationTokenSource? _messageCts;
    private bool _remoteOpRunning;
    private ConfirmDialogState? _pendingConfirm;

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
        _workingCopySplit = new SplitPane(horizontal: true, initialRatio: 0.35f);

        // Wire up events
        _commitList.OnCommitSelected = OnCommitSelected;
        _commitList.CopyToClipboard = CopyToClipboard;
        _commitList.OnCheckoutCommit = DoCheckoutCommit;
        _commitList.OnCherryPick = DoCherryPick;
        _commitList.OnRevert = DoRevert;
        _commitList.OnReset = DoReset;
        _commitList.OnRepoChanged = RefreshAll;
        _sidebar.OnBranchSelected = OnBranchSelected;
        _sidebar.OnStashSelected = OnStashSelected;
        _sidebar.OnWorkingCopySelected = OnWorkingCopySelected;
        _sidebar.CopyToClipboard = CopyToClipboard;
        _sidebar.OnRepoChanged = RefreshAll;
        _sidebar.ShowStatusMessage = ShowMessage;
        _sidebar.ShowConfirm = ShowConfirm;
        _sidebar.OnMergeBranch = DoMerge;
        _sidebar.OnRebaseBranch = DoRebase;
        _stagingArea.OnCommitted = OnCommitted;
        _stagingArea.OnRepoChanged = RefreshAll;
        _stagingArea.ShowStatusMessage = ShowMessage;
        _commitDetail.OnCommitHashSelected = OnCommitHashSelected;
        _commitDetail.OnFileHistoryRequested = OnFileHistoryRequested;
    }

    private void OnCommitHashSelected(string hash)
    {
        var commit = _repo.GetCommit(hash);
        if (commit == null) return;
        _commitList.SelectedCommitHash = hash;
        OnCommitSelected(commit);
    }

    private void OnFileHistoryRequested(string filePath)
    {
        ShowWorkingCopy = false;
        _sidebar.WorkingCopyActive = false;
        _sidebar.Refresh();
        _commitList.ShowFileHistory(filePath);
    }

    public override Element Build()
    {
        var currentBranch = _repo.GetCurrentBranch();
        var branches = _repo.GetBranches();
        var currentBranchInfo = branches.FirstOrDefault(b => b.IsCurrent || b.Name == currentBranch);
        var status = _repo.GetStatus();
        var showWc = ShowWorkingCopy;
        var repoName = Path.GetFileName(_repo.Path.TrimEnd(Path.DirectorySeparatorChar));
        var merging = _repo.IsMerging();
        var rebasing = _repo.IsRebasing();

        var root = Flex.Column(gap: 0)
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
                    onPush: DoPush,
                    onForcePush: DoForcePush,
                    isMerging: merging,
                    onMergeAbort: DoMergeAbort,
                    isRebasing: rebasing,
                    onRebaseAbort: DoRebaseAbort,
                    onRebaseContinue: DoRebaseContinue,
                    message: _statusMessage,
                    messageIsError: _statusIsError
                )
            );

        if (_pendingConfirm != null)
            root.AddChild(ConfirmDialog.Build(_pendingConfirm, DismissConfirm));

        return root;
    }

    private Element BuildSidebarLayout(bool showWc)
    {
        var rightPanel = showWc
            ? BuildWorkingCopyPanel()
            : _mainSplit.Build(
                _commitList.Mount(),
                _commitDetail.Mount()
            );

        return _sidebarSplit.Build(
            _sidebar.Mount(),
            rightPanel
        );
    }

    private Element BuildWorkingCopyPanel()
    {
        return _workingCopySplit.Build(
            _stagingArea.Mount(),
            _workingDiffViewer.Mount()
        );
    }

    // ── Event handlers ──

    private void OnCommitSelected(GitCommit? commit)
    {
        if (commit != null)
        {
            ShowWorkingCopy = false;
            _sidebar.WorkingCopyActive = false;
            _sidebar.Refresh();
            _commitDetail.Commit = commit;
        }
    }

    private void OnWorkingCopySelected()
    {
        ShowWorkingCopy = !ShowWorkingCopy;
        _sidebar.WorkingCopyActive = !_sidebar.WorkingCopyActive;
        _sidebar.Refresh();
        _stagingArea.Refresh();
    }

    private void OnBranchSelected(string branchName)
    {
        ShowResult(_repo.Checkout(branchName), $"Switched to {branchName}");
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
        _sidebar.WorkingCopyActive = false;
        _sidebar.Refresh();
        _commitDetail.Commit = commit;
    }

    private void OnCommitted()
    {
        RefreshAll();
    }

    private void DoCheckoutCommit(string hash)
    {
        ShowResult(_repo.CheckoutDetached(hash), $"Detached HEAD at {hash[..7]}");
        RefreshAll();
    }

    private void DoCherryPick(string hash)
    {
        ShowResult(_repo.CherryPick(hash), $"Cherry-picked {hash[..7]}");
        RefreshAll();
    }

    private void DoRevert(string hash)
    {
        ShowResult(_repo.Revert(hash), $"Reverted {hash[..7]}");
        RefreshAll();
    }

    private void DoReset(string hash, ResetMode mode)
    {
        if (mode == ResetMode.Hard)
        {
            ShowConfirm(new ConfirmDialogState
            {
                Title = "Hard Reset",
                Body = $"Reset current branch to {hash[..7]}?\n\nThis will discard all uncommitted changes in your working tree. This cannot be undone.",
                ConfirmLabel = "Reset Hard",
                Destructive = true,
                OnConfirm = () =>
                {
                    ShowResult(_repo.Reset(hash, ResetMode.Hard), $"Hard reset to {hash[..7]}");
                    RefreshAll();
                },
            });
        }
        else
        {
            var label = mode == ResetMode.Soft ? "Soft" : "Mixed";
            ShowResult(_repo.Reset(hash, mode), $"{label} reset to {hash[..7]}");
            RefreshAll();
        }
    }

    private void DoMerge(string branchName)
    {
        var result = _repo.Merge(branchName);
        if (result.Success)
        {
            ShowMessage($"Merged '{branchName}' successfully", isError: false);
        }
        else if (_repo.IsMerging())
        {
            // Merge started but has conflicts — switch to working copy view
            ShowMessage($"Merge conflicts — resolve and commit, or abort", isError: true);
            ShowWorkingCopy = true;
            _sidebar.WorkingCopyActive = true;
            _sidebar.Refresh();
            _stagingArea.Refresh();
        }
        else
        {
            ShowResult(result);
        }
        RefreshAll();
    }

    private void DoMergeAbort()
    {
        ShowConfirm(new ConfirmDialogState
        {
            Title = "Abort Merge",
            Body = "Abort the current merge and return to the pre-merge state?\n\nAll merge progress will be lost.",
            ConfirmLabel = "Abort",
            Destructive = true,
            OnConfirm = () =>
            {
                ShowResult(_repo.MergeAbort(), "Merge aborted");
                RefreshAll();
            },
        });
    }

    private void DoRebase(string branchName)
    {
        var result = _repo.Rebase(branchName);
        if (result.Success)
        {
            ShowMessage($"Rebased onto '{branchName}' successfully", isError: false);
        }
        else if (_repo.IsRebasing())
        {
            // Rebase paused due to conflicts
            ShowMessage("Rebase conflicts \u2014 resolve, stage, then continue or abort", isError: true);
            ShowWorkingCopy = true;
            _sidebar.WorkingCopyActive = true;
            _sidebar.Refresh();
            _stagingArea.Refresh();
        }
        else
        {
            ShowResult(result);
        }
        RefreshAll();
    }

    private void DoRebaseAbort()
    {
        ShowConfirm(new ConfirmDialogState
        {
            Title = "Abort Rebase",
            Body = "Abort the current rebase and return to the original branch state?\n\nAll rebase progress will be lost.",
            ConfirmLabel = "Abort",
            Destructive = true,
            OnConfirm = () =>
            {
                ShowResult(_repo.RebaseAbort(), "Rebase aborted");
                RefreshAll();
            },
        });
    }

    private void DoRebaseContinue()
    {
        var result = _repo.RebaseContinue();
        if (result.Success)
        {
            ShowMessage("Rebase continued", isError: false);
        }
        else if (_repo.IsRebasing())
        {
            // Still conflicts on next commit
            ShowMessage("Rebase conflicts \u2014 resolve remaining conflicts", isError: true);
        }
        else
        {
            ShowResult(result);
        }
        RefreshAll();
    }

    private async void DoFetch()
    {
        if (_remoteOpRunning) return;
        _remoteOpRunning = true;
        ShowMessage("Fetching\u2026", isError: false);
        var result = await _repo.FetchAsync();
        _remoteOpRunning = false;
        ShowResult(result, "Fetched all remotes");
        RefreshAll();
    }

    private async void DoPull()
    {
        if (_remoteOpRunning) return;
        _remoteOpRunning = true;
        ShowMessage("Pulling\u2026", isError: false);
        var result = await _repo.PullAsync();
        _remoteOpRunning = false;
        ShowResult(result, "Pulled latest changes");
        RefreshAll();
    }

    private async void DoPush()
    {
        if (_remoteOpRunning) return;
        _remoteOpRunning = true;
        ShowMessage("Pushing\u2026", isError: false);
        var result = await _repo.PushAsync();
        _remoteOpRunning = false;
        ShowResult(result, "Pushed successfully");
        RefreshAll();
    }

    private void DoForcePush()
    {
        ShowConfirm(new ConfirmDialogState
        {
            Title = "Force Push",
            Body = "This will overwrite the remote branch history using --force-with-lease. Other collaborators may lose work.\n\nAre you sure?",
            ConfirmLabel = "Force Push",
            Destructive = true,
            OnConfirm = async () =>
            {
                if (_remoteOpRunning) return;
                _remoteOpRunning = true;
                ShowMessage("Force pushing\u2026", isError: false);
                var result = await _repo.ForcePushAsync();
                _remoteOpRunning = false;
                ShowResult(result, "Force pushed successfully");
                RefreshAll();
            },
        });
    }

    private void CopyToClipboard(string text)
    {
        _window.Clipboard.SetText(text);
    }

    private void ShowResult(GitResult result, string? successLabel = null)
    {
        if (!result.Success)
        {
            var msg = result.Stderr.Trim();
            if (string.IsNullOrEmpty(msg)) msg = "Operation failed";
            ShowMessage(msg, isError: true);
        }
        else if (successLabel != null)
        {
            ShowMessage(successLabel, isError: false);
        }
    }

    private void ShowMessage(string message, bool isError)
    {
        _messageCts?.Cancel();
        _statusMessage = message;
        _statusIsError = isError;

        _messageCts = new CancellationTokenSource();
        var token = _messageCts.Token;
        _ = Task.Run(async () =>
        {
            await Task.Delay(isError ? 8000 : 4000, token);
            if (!token.IsCancellationRequested)
            {
                _statusMessage = null;
                MarkDirty();
            }
        }, token);

        MarkDirty();
    }

    private void ShowConfirm(ConfirmDialogState state)
    {
        _pendingConfirm = state;
        MarkDirty();
    }

    private void DismissConfirm()
    {
        _pendingConfirm = null;
        MarkDirty();
    }

    private void RefreshAll()
    {
        _sidebar.Refresh();
        _commitList.Refresh();
        _stagingArea.Refresh();
        MarkDirty();
    }
}
