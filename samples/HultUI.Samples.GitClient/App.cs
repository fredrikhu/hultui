using HultUI;
using HultUI.Core.Builders;
using HultUI.Core.Components;
using HultUI.Core.Elements;
using HultUI.Core.Events;
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
public partial class App : Component, IDisposable
{
    private readonly Window _window;
    private readonly GitRepository _repo;
    private readonly GitClientSettingsData _settings;
    private readonly Func<Element>? _buildBelowTitleBar;

    // Sub-components
    private readonly Sidebar _sidebar;
    private readonly CommitList _commitList;
    private readonly CommitDetail _commitDetail;
    private readonly DiffViewer _workingDiffViewer;
    private readonly StagingArea _stagingArea;
    private readonly SplitPane _sidebarSplit;
    private readonly SplitPane _mainSplit;
    private readonly SplitPane _workingCopySplit;
    private readonly GitWatcher _watcher;

    // State
    [Reactive] public partial bool ShowWorkingCopy { get; set; }

    // Branch name + ahead/behind for the title bar come from `git for-each-ref`,
    // loaded off the UI thread so it doesn't hitch every rebuild (including view
    // toggles). Cached by _refreshGen, which RefreshAll bumps on repo changes.
    private readonly AsyncLoader<HeaderInfo> _headerData = new(new HeaderInfo("HEAD", 0, 0));
    private int _refreshGen;

    private readonly record struct HeaderInfo(string Branch, int Ahead, int Behind);

    private string? _statusMessage;
    private bool _statusIsError;
    private CancellationTokenSource? _messageCts;
    private bool _remoteOpRunning;
    private ConfirmDialogState? _pendingConfirm;
    private InteractiveRebaseState? _pendingRebase;
    private PasswordDialogState? _pendingPassword;
    private RemoteBranchDialogState? _pendingRemoteBranch;
    private string? _bisectBadHash;
    private bool _showShortcuts;

    public App(Window window, GitRepository repo, GitClientSettingsData settings,
        Func<Element>? buildBelowTitleBar = null)
    {
        _window = window;
        _repo = repo;
        _settings = settings;
        // Captured before the Mount() call below so the eager initial Build()
        // already sees this lambda — wiring it via a setter after construction
        // would leave the first render missing the tab strip.
        _buildBelowTitleBar = buildBelowTitleBar;

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
        _commitList.OnInteractiveRebase = DoInteractiveRebase;
        _commitList.OnBisectStart = DoBisectStart;
        _commitList.OnRepoChanged = RefreshAll;
        _sidebar.OnBranchSelected = OnBranchSelected;
        _sidebar.OnBranchChanged = OnBranchChanged;
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

        // Attach the global Escape handler to the ComponentHost itself.
        // When no element is focused, Window dispatches KeyDown to _root
        // (= this host); EventDispatcher.BuildPath walks up via Parent, so
        // handlers on children of the host are never visited in that case.
        // OnMount() won't fire (SetRoot bypasses AddChild), so wire it here
        // — calling Mount() from the constructor materialises the Host.
        Mount().On<KeyDownEvent>(HandleRootKeyDown);

        // Watch the repo for external changes (other editors, terminal git
        // commands). The debounced callback fires RefreshAll on the UI thread
        // via MarkDirty().
        _watcher = new GitWatcher(repo.Path, RefreshAll);
    }


    private void OnCommitHashSelected(string hash)
    {
        OnCommitSelected(hash, scrollToCommit: false);
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
        _headerData.Load(_refreshGen, LoadHeaderInfo);
        var header = _headerData.Value;
        var currentBranch = header.Branch;

        var showWc = ShowWorkingCopy;
        var merging = _repo.IsMerging();      // cheap: checks .git/MERGE_HEAD
        var rebasing = _repo.IsRebasing();    // cheap: checks .git/rebase-* dirs
        var bisecting = _repo.IsBisecting();  // cheap: checks .git/BISECT_START

        // Update bisect label for context menu in commit list
        _commitList.BisectLabel = _bisectBadHash != null
            ? "Bisect \u2013 mark as good (start)"
            : bisecting ? null  // hide during active bisect
            : null;             // default label
        // Hide the bisect start menu item during active bisect
        if (bisecting) _commitList.OnBisectStart = null;
        else _commitList.OnBisectStart = DoBisectStart;

        var children = new List<Element>
        {
            TitleBar.Build(
                _window,
                currentBranch,
                header.Ahead,
                header.Behind,
                onFetch: DoFetch,
                onPull: DoPull,
                onPush: DoPush,
                onForcePush: DoForcePush,
                onPullFrom: () => ShowRemoteBranchDialog(RemoteBranchMode.Pull),
                onPushTo: () => ShowRemoteBranchDialog(RemoteBranchMode.Push),
                isMerging: merging,
                onMergeAbort: DoMergeAbort,
                isRebasing: rebasing,
                onRebaseAbort: DoRebaseAbort,
                onRebaseContinue: DoRebaseContinue,
                isBisecting: bisecting,
                onBisectGood: bisecting ? DoBisectGood : null,
                onBisectBad: bisecting ? DoBisectBad : null,
                onBisectSkip: bisecting ? DoBisectSkip : null,
                onBisectReset: bisecting ? DoBisectReset : null,
                message: _statusMessage,
                messageIsError: _statusIsError
            ),
        };

        if (_buildBelowTitleBar != null)
            children.Add(_buildBelowTitleBar());

        children.Add(BuildSidebarLayout(showWc));

        var root = Flex.Column(gap: 0)
            .TextColor(GitClientColors.TextPrimary)
            .FontSize(13f)
            .BorderWidth(1f)
            .BorderColor(GitClientColors.BorderLight)
            .Children(children.ToArray());

        if (_pendingConfirm != null)
            root.AddChild(ConfirmDialog.Build(_pendingConfirm, DismissConfirm));

        if (_pendingRebase != null)
            root.AddChild(InteractiveRebaseDialog.Build(_pendingRebase, DismissRebase));

        if (_pendingPassword != null)
            root.AddChild(PasswordDialog.Build(_pendingPassword, DismissPassword));

        if (_pendingRemoteBranch != null)
            root.AddChild(RemoteBranchDialog.Build(_pendingRemoteBranch, DismissRemoteBranch, MarkDirty));

        if (_showShortcuts)
            root.AddChild(KeyboardShortcutsOverlay.Build(() => { _showShortcuts = false; MarkDirty(); }));

        return root;
    }

    /// <summary>
    /// Dispatch focus requests parked on dialog state during the previous
    /// rebuild. Done here (not in the dialog) because the rebase dialog is
    /// a static builder — it has no lifecycle hook of its own. Doing the
    /// focus call before reconciliation completes would target an
    /// about-to-be-replaced element instead of the live one.
    /// </summary>
    protected override void OnAfterBuild()
    {
        if (_pendingRebase is { } rebase)
        {
            if (rebase.PendingRowFocus is { } hash
                && rebase.RowRefs.TryGetValue(hash, out var rowRef))
            {
                rowRef.Current?.Focus();
                rebase.PendingRowFocus = null;
            }
            if (rebase.PendingDropdownFocus)
            {
                rebase.PendingDropdownFocus = false;
                rebase.DropdownRef.Current?.Focus();
            }
        }
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

    private void OnCommitSelected(string hash, bool scrollToCommit)
    {
        ShowWorkingCopy = false;
        _sidebar.WorkingCopyActive = false;
        _sidebar.Refresh();

        var commit = _repo.GetCommit(hash);
        if (commit == null) return;
        _commitList.SelectedCommitHash = hash;
        if (scrollToCommit)
            _commitList.ScrollTo(hash);

        // Bias the file pre-selection to the file whose history we're viewing, if any.
        _commitDetail.PreferredFilePath = _commitList.FollowedFilePath;
        _commitDetail.Commit = commit;
    }

    private void OnWorkingCopySelected()
    {
        // Pure view switch: no git data changed. The WorkingCopyActive setter
        // does a UI-only sidebar rebuild, and the staging area loads its own
        // data (cached) when Build mounts it. Forcing Refresh() here would
        // re-query git on every toggle.
        ShowWorkingCopy = !ShowWorkingCopy;
        _sidebar.WorkingCopyActive = !_sidebar.WorkingCopyActive;
    }

    private void OnBranchSelected(string hash)
    {
        OnCommitSelected(hash, scrollToCommit: true);
    }

    private void OnBranchChanged(string branchName)
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
        _commitDetail.PreferredFilePath = null;
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

    private void DoInteractiveRebase(string baseHash)
    {
        var range = _repo.GetRebaseRange(baseHash);
        if (range.Count == 0)
        {
            ShowMessage($"No commits between {baseHash[..7]} and HEAD", isError: true);
            return;
        }

        var entries = range.Select(c => new RebaseEntry
        {
            Hash = c.Hash,
            ShortHash = c.ShortHash,
            Subject = c.Subject,
        }).ToList();

        _pendingRebase = new InteractiveRebaseState
        {
            BaseRef = baseHash,
            BaseShortHash = baseHash[..7],
            Entries = entries,
            OnConfirm = plan => RunInteractiveRebase(baseHash, plan),
            // Initial focus lands on the first row when the dialog mounts.
            PendingRowFocus = entries[0].Hash,
        };
        _pendingRebase.RefreshDialog = MarkDirty;
        MarkDirty();
    }

    private void RunInteractiveRebase(string baseHash, IReadOnlyList<RebaseEntry> plan)
    {
        // Squash/fixup only make sense *after* a pick, so the first entry must be a pick.
        if (plan.Count > 0 && (plan[0].Op == RebaseOp.Squash || plan[0].Op == RebaseOp.Fixup))
        {
            ShowMessage("The first commit cannot be squashed or fixed up — git has nothing to squash it into.", isError: true);
            return;
        }

        var result = _repo.InteractiveRebase(baseHash, plan);
        if (result.Success)
        {
            ShowMessage($"Rebased {plan.Count(e => e.Op != RebaseOp.Drop)} commit(s) onto {baseHash[..7]}", isError: false);
        }
        else if (_repo.IsRebasing())
        {
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

    private void DismissRebase()
    {
        _pendingRebase = null;
        MarkDirty();
    }

    private void HandleRootKeyDown(KeyDownEvent e) => HandleGlobalKeyDown(e);

    // Global keyboard shortcuts. KeyDownEvents bubble from focused elements
    // inside this repository app. When no element is focused, TabbedApp
    // receives the event at its root and forwards it here.
    public void HandleGlobalKeyDown(KeyDownEvent e)
    {
        var ctrl = e.Modifiers.HasFlag(KeyModifiers.Ctrl);
        var shift = e.Modifiers.HasFlag(KeyModifiers.Shift);
        var typing = e.Target is TextInputElement or TextAreaElement;

        // Escape — dismiss the topmost modal/overlay
        if (e.Key == KeyCode.Escape)
        {
            if (_showShortcuts) { _showShortcuts = false; MarkDirty(); e.StopPropagation = true; }
            else if (_pendingPassword != null) { _pendingPassword.OnResult(null); DismissPassword(); e.StopPropagation = true; }
            else if (_pendingRemoteBranch != null) { DismissRemoteBranch(); e.StopPropagation = true; }
            else if (_pendingRebase != null) { DismissRebase(); e.StopPropagation = true; }
            else if (_pendingConfirm != null) { _pendingConfirm.OnCancel?.Invoke(); DismissConfirm(); e.StopPropagation = true; }
            return;
        }

        // Ctrl+F — show/focus commit search; hide it when the search field is focused.
        if (e.Key == KeyCode.F && ctrl)
        {
            if (ShowWorkingCopy)
            {
                ShowWorkingCopy = false;
                _sidebar.WorkingCopyActive = false;
                _sidebar.Refresh();
            }
            _commitList.ToggleSearchFromShortcut();
            e.StopPropagation = true;
            return;
        }

        // Don't intercept the remaining shortcuts while typing in a text field
        if (typing) return;

        // ? (Shift+Slash) — toggle keyboard shortcuts overlay
        if (e.Key == KeyCode.Slash && shift)
        {
            _showShortcuts = !_showShortcuts;
            MarkDirty();
            e.StopPropagation = true;
            return;
        }

        // Ctrl+L — focus the commit log search
        if (e.Key == KeyCode.L && ctrl)
        {
            if (ShowWorkingCopy)
            {
                ShowWorkingCopy = false;
                _sidebar.WorkingCopyActive = false;
                _sidebar.Refresh();
            }
            _commitList.FocusSearch();
            e.StopPropagation = true;
            return;
        }

        // Ctrl+W — toggle working copy view
        if (e.Key == KeyCode.W && ctrl)
        {
            OnWorkingCopySelected();
            e.StopPropagation = true;
            return;
        }

        // Ctrl+D - open selected file in configured external diff tool
        if (e.Key == KeyCode.D && ctrl)
        {
            OpenExternalDiffForSelection();
            e.StopPropagation = true;
            return;
        }

    }

    private void OpenExternalDiffForSelection()
    {
        bool ok;
        string error;
        if (ShowWorkingCopy)
            ok = _stagingArea.TryOpenExternalDiff(_settings.ExternalTools, out error);
        else
            ok = _commitDetail.TryOpenExternalDiff(_settings.ExternalTools, out error);

        if (!ok)
            ShowMessage(error, isError: true);
    }

    /// <summary>
    /// Callback invoked by <see cref="AskPassBridge"/> when SSH or git needs
    /// a passphrase. Shows the password dialog and returns the user's input
    /// via a <see cref="TaskCompletionSource{T}"/>. Runs on the monitor
    /// thread — marshals state changes to the main thread via
    /// <see cref="Application.InvokeOnMainThread"/>.
    /// </summary>
    private Task<string?> HandleAskPass(string prompt)
    {
        var tcs = new TaskCompletionSource<string?>();
        Application.InvokeOnMainThread(() =>
        {
            _pendingPassword = new PasswordDialogState
            {
                Prompt = prompt,
                OnResult = result =>
                {
                    _pendingPassword = null;
                    tcs.TrySetResult(result);
                },
            };
            MarkDirty();
        });
        return tcs.Task;
    }

    private void DismissPassword()
    {
        _pendingPassword = null;
        MarkDirty();
    }

    private void ShowRemoteBranchDialog(RemoteBranchMode mode)
    {
        var remotes = _repo.GetRemotes();
        if (remotes.Count == 0)
        {
            ShowMessage("No remotes configured", isError: true);
            return;
        }

        _pendingRemoteBranch = new RemoteBranchDialogState
        {
            Mode = mode,
            Remotes = remotes,
            GetBranches = _repo.GetRemoteBranches,
            OnConfirm = mode == RemoteBranchMode.Push ? DoPushTo : DoPullFrom,
        };
        // Auto-select the first remote
        _pendingRemoteBranch.SelectedRemote = remotes[0];
        _pendingRemoteBranch.Branches = _repo.GetRemoteBranches(remotes[0]);
        MarkDirty();
    }

    private void DismissRemoteBranch()
    {
        _pendingRemoteBranch = null;
        MarkDirty();
    }

    private async void DoPushTo(string remote, string branch)
    {
        if (_remoteOpRunning) return;
        _remoteOpRunning = true;
        ShowMessage($"Pushing to {remote}/{branch}\u2026", isError: false);
        var result = await _repo.PushToAsync(remote, branch, HandleAskPass);
        _remoteOpRunning = false;
        ShowResult(result, $"Pushed to {remote}/{branch}");
        RefreshAll();
    }

    private async void DoPullFrom(string remote, string branch)
    {
        if (_remoteOpRunning) return;
        _remoteOpRunning = true;
        ShowMessage($"Pulling from {remote}/{branch}\u2026", isError: false);
        var result = await _repo.PullFromAsync(remote, branch, HandleAskPass, CreatePullOptions());
        _remoteOpRunning = false;
        HandlePullResult(result, $"Pulled from {remote}/{branch}");
        RefreshAll();
    }

    // ── Bisect ──

    private void DoBisectStart(string hash)
    {
        if (_repo.IsBisecting())
        {
            ShowMessage("Already bisecting \u2014 reset first", isError: true);
            return;
        }

        if (_bisectBadHash == null)
        {
            // First step: mark as bad
            _bisectBadHash = hash;
            ShowMessage($"Marked {hash[..7]} as bad \u2014 now right-click an older commit to mark as good", isError: false);
            MarkDirty();
        }
        else
        {
            // Second step: mark as good, start bisect
            var badHash = _bisectBadHash;
            _bisectBadHash = null;

            var result = _repo.BisectStart(badHash, hash);
            if (result.Success)
            {
                ShowMessage("Bisect started \u2014 test the current commit and mark Good or Bad", isError: false);
            }
            else
            {
                ShowResult(result);
            }
            RefreshAll();
        }
    }

    private void DoBisectGood()
    {
        var result = _repo.BisectGood();
        HandleBisectStep(result);
    }

    private void DoBisectBad()
    {
        var result = _repo.BisectBad();
        HandleBisectStep(result);
    }

    private void DoBisectSkip()
    {
        var result = _repo.BisectSkip();
        HandleBisectStep(result);
    }

    private void HandleBisectStep(GitResult result)
    {
        if (!result.Success)
        {
            ShowResult(result);
        }
        else
        {
            // git bisect prints its status to stdout — check if it found the culprit
            var output = result.Stdout.Trim();
            if (output.Contains("is the first bad commit"))
            {
                ShowMessage("Bisect complete \u2014 found the first bad commit!", isError: false);
            }
            else
            {
                // Extract the short status line for the user
                var lines = output.Split('\n');
                var statusLine = lines.FirstOrDefault(l => l.StartsWith("Bisecting:")) ?? "Testing next commit\u2026";
                ShowMessage(statusLine, isError: false);
            }
        }
        RefreshAll();
    }

    private void DoBisectReset()
    {
        ShowConfirm(new ConfirmDialogState
        {
            Title = "Reset Bisect",
            Body = "Stop bisecting and return to the original branch?",
            ConfirmLabel = "Reset",
            Destructive = false,
            OnConfirm = () =>
            {
                var result = _repo.BisectReset();
                ShowResult(result, "Bisect reset");
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
        var result = await _repo.FetchAsync(HandleAskPass, _settings.Git.PruneOnFetch);
        _remoteOpRunning = false;
        ShowResult(result, "Fetched all remotes");
        RefreshAll();
    }

    public async Task AutoFetchAsync()
    {
        if (_remoteOpRunning) return;

        _remoteOpRunning = true;
        var result = await _repo.FetchAsync(HandleAskPass, _settings.Git.PruneOnFetch);
        _remoteOpRunning = false;

        if (!result.Success)
        {
            ShowResult(result);
            return;
        }

        RefreshAll();
    }

    private async void DoPull()
    {
        if (_remoteOpRunning) return;
        _remoteOpRunning = true;
        ShowMessage("Pulling\u2026", isError: false);

        GitResult result;
        using (_window.Tracer.BeginAsync("Git", "Pull"))
        {
            result = await _repo.PullAsync(HandleAskPass, CreatePullOptions());
        }

        _remoteOpRunning = false;
        HandlePullResult(result, "Pulled latest changes");
        RefreshAll();
    }

    private PullOptions CreatePullOptions() => new()
    {
        Strategy = _settings.Git.PullStrategy,
        AutoStash = _settings.Git.PullAutoStash,
        Prune = _settings.Git.PruneOnFetch,
    };

    private void HandlePullResult(GitResult result, string successLabel)
    {
        if (result.Success)
        {
            ShowMessage(successLabel, isError: false);
            return;
        }

        if (_repo.IsRebasing())
        {
            ShowMessage("Pull rebase conflicts - resolve, stage, then continue or abort", isError: true);
            ShowWorkingCopy = true;
            _sidebar.WorkingCopyActive = true;
            _sidebar.Refresh();
            _stagingArea.Refresh();
            return;
        }

        ShowResult(result);
    }

    private async void DoPush()
    {
        if (_remoteOpRunning) return;
        _remoteOpRunning = true;
        ShowMessage("Pushing\u2026", isError: false);
        var result = await _repo.PushAsync(HandleAskPass);
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
            OnConfirm = DoForcePushConfirmed,
        });
    }

    private async void DoForcePushConfirmed()
    {
        if (_remoteOpRunning) return;
        _remoteOpRunning = true;
        ShowMessage("Force pushing\u2026", isError: false);
        var result = await _repo.ForcePushAsync(HandleAskPass);
        _remoteOpRunning = false;
        ShowResult(result, "Force pushed successfully");
        RefreshAll();
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

    private async void ShowMessage(string message, bool isError)
    {
        _messageCts?.Cancel();
        _statusMessage = message;
        _statusIsError = isError;

        _messageCts = new CancellationTokenSource();
        var token = _messageCts.Token;
        MarkDirty();

        try
        {
            await Task.Delay(isError ? 8000 : 4000, token);
            _statusMessage = null;
            MarkDirty();
        }
        catch (OperationCanceledException) { }
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
        _refreshGen++;
        _sidebar.Refresh();
        _commitList.Refresh();
        _stagingArea.Refresh();
        MarkDirty();
    }

    // Runs on a thread-pool thread — no UI/reactive access.
    private HeaderInfo LoadHeaderInfo()
    {
        var currentBranch = _repo.GetCurrentBranch();
        var branches = _repo.GetBranches();
        var info = branches.FirstOrDefault(b => b.IsCurrent || b.Name == currentBranch);
        return new HeaderInfo(currentBranch, info?.AheadCount ?? 0, info?.BehindCount ?? 0);
    }

    public void Dispose()
    {
        _watcher.Dispose();
        _messageCts?.Cancel();
        _messageCts?.Dispose();
    }

    public void RefreshChrome() => MarkDirty();
}
