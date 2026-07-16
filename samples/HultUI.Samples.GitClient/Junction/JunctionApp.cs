using HultUI;
using HultUI.Core.Builders;
using HultUI.Core.Components;
using HultUI.Core.Elements;
using HultUI.Core.Events;
using HultUI.Core.Layout;
using HultUI.Core.Primitives;
using HultUI.Core.Styling;
using HultUI.Core.Reactivity;
using GitClient.Git;
using GitClient.Platform;
using GitClient.Theme;

namespace GitClient.Junction;

public enum JunctionView { Overview, History, Commit, Changes, Branches }

/// <summary>
/// Root of the Junction multi-repo git client: a workspace-oriented shell
/// (title bar · sidebar · five segmented views) over the real <see cref="GitRepository"/>
/// backend. This is the Nocturne redesign of the GitClient sample.
///
/// <para>Foundation slice: the shell, sidebar (workspaces / repositories /
/// co-usage), command palette, and a fully wired History view are live; the
/// Overview / Commit / Changes / Branches views are placeholders that switch
/// but don't yet render their real content.</para>
/// </summary>
public partial class JunctionApp : Component, IDisposable
{
    private readonly Application _app;
    private readonly Window _window;
    private readonly GitClientSettingsData _settings;
    private readonly IFolderPicker _folderPicker;
    private readonly ISaveFilePicker _saveFilePicker;

    // One GitRepository per path, plus a background summary loader per path so
    // the sidebar's branch / ahead-behind / dirty badges never block Build().
    private readonly Dictionary<string, GitRepository> _repos = new();
    private readonly Dictionary<string, AsyncLoader<RepoSummary>> _summaryLoaders = new();

    // Per-repo data generation. Every cached loader keys on it, so a repo's
    // views/summary reload only when *that* repo's data actually changed (a git
    // action here, or an external change the watcher reports). Unrelated repos
    // keep their cached values, which is what makes switching between them fast.
    private readonly Dictionary<string, int> _dataGen = new();

    // One filesystem watcher per repo in the active workspace, so commits, pushes
    // and edits made by other git clients are noticed and the affected repo's
    // cache is invalidated.
    private readonly Dictionary<string, GitWatcher> _watchers = new(PathComparer);

    // Active pointers.
    private string _activeWorkspaceId;
    private string _activeRepoPath;

    // View + chrome state.
    [Reactive] public partial JunctionView View { get; set; }
    private bool _sidebarCollapsed;

    // Responsive breakpoints, measured against the *content* width (viewport minus
    // the sidebar) since the toolbar and the views live to the right of it — so
    // collapsing the sidebar frees room and re-evaluates the breakpoints too. Both
    // flags are flipped from the window's Resized event (and ToggleSidebar); the
    // reactive build doesn't re-run on resize on its own, so RecomputeResponsive
    // bumps _revision only when a breakpoint actually crosses.
    //
    // CompactChromeContentWidth: below it the title-bar breadcrumb + toolbar
    // collapse to icon-only and the commit-row ref chips hide. The full-label
    // toolbar needs ~890 dip of content to lay out without clipping the stash
    // button (the tabs are FlexShrink(0), so they no longer compress to fit).
    private const float CompactChromeContentWidth = 910f;
    // HideDiffContentWidth: below it the Commit/Changes views drop the diff pane
    // and let the file list fill (the 3-pane split gets unusable when narrow).
    private const float HideDiffContentWidth = 620f;
    private const float SidebarExpandedWidth = 266f;
    private const float SidebarRailWidth = 58f;
    private bool _compactChrome;
    private bool _hideDiff;

    private float ContentWidth =>
        _window.LayoutWidth - (_sidebarCollapsed ? SidebarRailWidth : SidebarExpandedWidth);

    // Bumped to force a *reactive* rebuild (via the build Effect) rather than the
    // untracked MarkDirty/ForceRebuild path. Switching the active repo activates a
    // different per-repo loader, and only a tracked rebuild re-subscribes the
    // Effect to that loader's signals — otherwise its async load completes unseen
    // and the view stays on stale/empty data until the next tracked rebuild.
    private readonly Signal<int> _revision = new(0);

    // History state. Per-repo loaders (kept in a dictionary) retain each repo's
    // last-loaded value so returning to a repo is instant; see LoaderFor.
    private readonly Dictionary<string, AsyncLoader<HistoryData>> _historyLoaders = new(PathComparer);
    // Multi-selection à la Windows lists: _selectedCommitHash is the focused/lead
    // commit (keyboard nav, context menus), _selectedCommitHashes the full selection
    // (always contains the focused one), _selectionAnchorHash the Shift-range anchor.
    private string? _selectedCommitHash;
    private readonly HashSet<string> _selectedCommitHashes = new(StringComparer.Ordinal);
    private string? _selectionAnchorHash;
    private string _historyQuery = "";
    private readonly Ref<TextInputElement> _historySearchRef = new();
    private bool _pendingHistorySearchFocus;
    // Per-repo "include in history" branch sets. Empty ⇒ all branches.
    private readonly Dictionary<string, HashSet<string>> _includeBranches = new();
    private readonly Ref<VirtualListElement<GitCommit>> _commitListRef = new();
    private bool _pendingScrollToSelected;

    // Commit-details state.
    private readonly AsyncLoader<CommitDetail> _commitDetail = new(CommitDetail.Empty);
    private string? _detailHash;          // commit the loaded detail belongs to
    // Multi-select compare shown instead: diff between the oldest and newest
    // selected commits (Base = oldest, Head = newest). Null in single-commit mode.
    private (string BaseHash, string HeadHash)? _detailRange;
    private string? _selectedFilePath;    // file selected in the Commit view

    // Changes (staging) state.
    private readonly Dictionary<string, AsyncLoader<ChangesData>> _changesLoaders = new(PathComparer);
    private string? _changeSelPath;
    private bool _changeSelStaged;
    // True when the selected file is a merge conflict; the right pane then shows the
    // ours/theirs resolution UI instead of a diff.
    private bool _changeSelConflicted;
    private string _commitSummary = "";
    private string _commitDescription = "";
    private readonly Ref<TextInputElement> _summaryRef = new();
    private readonly Ref<TextAreaElement> _descRef = new();

    // Branches (management) state.
    private readonly Dictionary<string, AsyncLoader<BranchesData>> _branchesLoaders = new(PathComparer);
    private string _branchQuery = "";
    private int _branchSel;
    private readonly Ref<TextInputElement> _branchSearchRef = new();
    private readonly Ref<ScrollViewElement> _branchScrollRef = new();
    private bool _pendingBranchSearchFocus;

    // Diff mode is a single app-level toggle shared by all diff panes.
    private bool _diffSplit;
    private bool _diffFull;

    // Command palette.
    private bool _paletteOpen;
    private string _paletteQuery = "";
    private int _paletteSel;
    private readonly Ref<TextInputElement> _paletteRef = new();
    private bool _pendingPaletteFocus;

    // Modal text-input dialog (new branch / rename / set upstream…).
    private DialogSpec? _dialog;
    private string _dialogValue = "";
    private bool _dialogChecked;
    private readonly Ref<TextInputElement> _dialogRef = new();
    private bool _pendingDialogFocus;

    // Repository / workspace management modal + the Repositories "+" popover.
    private ModalForm? _modal;
    private readonly Ref<TextInputElement> _modalNameRef = new();
    private bool _pendingModalFocus;
    private IReadOnlyList<ContextMenuItem>? _popover;
    private float _popoverX, _popoverY;

    // Interactive rebase dialog (newest-first todo editor + reword sub-dialog).
    private RebaseState? _rebase;
    private int _rbSel;
    private int? _rbRewordIndex;          // row whose message the sub-dialog edits
    private string _rbRewordText = "";
    private readonly Ref<TextAreaElement> _rbRewordRef = new();
    private bool _pendingRewordFocus;

    // Toast.
    private string? _toast;
    private CancellationTokenSource? _toastCts;

    private bool _remoteOpRunning;

    private readonly record struct RepoSummary(string Branch, int Ahead, int Behind, int Dirty, string Version = "")
    {
        public static readonly RepoSummary Pending = new("…", 0, 0, 0);
    }

    private readonly record struct HistoryData(
        IReadOnlyList<GitCommit> Commits,
        Dictionary<string, GitGraphRow> Graph,
        Dictionary<string, (int Added, int Deleted)> Stats,
        HashSet<string> Remotes)
    {
        public static readonly HistoryData Empty = new([], new(), new(), new());
    }

    private readonly record struct CommitDetail(
        GitCommit? Commit,
        IReadOnlyList<GitFileChange> Files,
        Dictionary<string, GitFileDiff> DiffByPath,
        string? RangeBaseHash = null)   // non-null: a multi-select compare against this base
    {
        public static readonly CommitDetail Empty = new(null, [], new());
    }

    private readonly record struct ChangesData(
        IReadOnlyList<GitFileChange> Staged,
        IReadOnlyList<GitFileChange> Unstaged,
        Dictionary<string, GitFileDiff> StagedDiffs,
        Dictionary<string, GitFileDiff> UnstagedDiffs,
        bool IsMerging,
        IReadOnlyList<GitFileChange> Conflicted,
        MergeSides? Merge)
    {
        public static readonly ChangesData Empty = new([], [], new(), new(), false, [], null);
    }

    // Identity of the two sides of an in-progress merge: "ours" = HEAD (the branch
    // being merged into), "theirs" = MERGE_HEAD (the branch being merged in).
    private readonly record struct MergeSides(
        string OursBranch, GitCommit? Ours,
        string? TheirsBranch, GitCommit? Theirs);

    private readonly record struct BranchesData(IReadOnlyList<GitBranch> Local)
    {
        public static readonly BranchesData Empty = new([]);
    }

    public JunctionApp(Application app, Window window, GitClientSettingsData settings,
        IFolderPicker? folderPicker = null, ISaveFilePicker? saveFilePicker = null)
    {
        _app = app;
        _window = window;
        _settings = settings;
        _folderPicker = folderPicker ?? new NativeFolderPicker();
        _saveFilePicker = saveFilePicker ?? new NativeSaveFilePicker();
        _settings.Normalize();

        _activeWorkspaceId = _settings.ActiveWorkspaceId ?? _settings.Workspaces.FirstOrDefault()?.Id ?? "";
        _activeRepoPath = _settings.ActiveRepoPath ?? ActiveWorkspace?.RepoPaths.FirstOrDefault() ?? "";

        View = JunctionView.History;

        _compactChrome = ContentWidth < CompactChromeContentWidth;
        _hideDiff = ContentWidth < HideDiffContentWidth;
        _window.Resized += OnWindowResize;

        // Global keys land on the host when nothing is focused (see App.cs for
        // the same rationale). Materialise the host so the handler is wired.
        Mount().On<KeyDownEvent>(HandleKeyDown);
    }

    // Toggle compact chrome as the window crosses the breakpoint. Only rerender on
    // an actual crossing so ordinary resizes above/below the threshold don't churn
    // the build every frame of a drag.
    private void OnWindowResize()
    {
        if (RecomputeResponsive()) Rerender();
    }

    // Recompute the responsive flags from the current content width; returns true
    // if either flipped. Called on resize and when the sidebar collapses/expands
    // (which changes the content width available to the toolbar and views).
    private bool RecomputeResponsive()
    {
        float content = ContentWidth;
        bool compact = content < CompactChromeContentWidth;
        bool hideDiff = content < HideDiffContentWidth;
        if (compact == _compactChrome && hideDiff == _hideDiff) return false;
        _compactChrome = compact;
        _hideDiff = hideDiff;
        return true;
    }

    // ── Model access ─────────────────────────────────────────────────────

    private WorkspaceData? ActiveWorkspace =>
        _settings.Workspaces.FirstOrDefault(w => w.Id == _activeWorkspaceId)
        ?? _settings.Workspaces.FirstOrDefault();

    private IReadOnlyList<string> ActiveWorkspaceRepos =>
        ActiveWorkspace?.RepoPaths ?? [];

    private bool HasActiveRepo => !string.IsNullOrEmpty(_activeRepoPath);

    // RepoFor is called from background load threads (LoadHistory/LoadChanges/…)
    // as well as the UI thread, so the cache dictionary must be guarded — a plain
    // Dictionary corrupts (and throws) under concurrent structural writes, which
    // AsyncLoader would then cache as a permanently-empty "loaded" result.
    private readonly object _reposLock = new();

    private GitRepository RepoFor(string path)
    {
        lock (_reposLock)
        {
            if (!_repos.TryGetValue(path, out var repo))
            {
                repo = new GitRepository(path);
                _repos[path] = repo;
            }
            return repo;
        }
    }

    private GitRepository? ActiveRepo => HasActiveRepo ? RepoFor(_activeRepoPath) : null;

    private RepoSummary SummaryFor(string path)
    {
        if (!_summaryLoaders.TryGetValue(path, out var loader))
        {
            loader = new AsyncLoader<RepoSummary>(RepoSummary.Pending);
            _summaryLoaders[path] = loader;
        }
        loader.Load($"{path}#{DataGen(path)}", () => LoadSummary(path));
        return loader.Value;
    }

    // ── Per-repo caching + change detection ──────────────────────────────

    private static readonly StringComparer PathComparer =
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    private int DataGen(string path) => _dataGen.GetValueOrDefault(path);

    /// <summary>Invalidate a repo's cached data (views + summary) so it reloads
    /// on next display. Called by our own git actions and by the change watcher.</summary>
    private void BumpDataGen(string path)
    {
        if (string.IsNullOrEmpty(path)) return;
        _dataGen[path] = DataGen(path) + 1;
    }

    private static AsyncLoader<T> LoaderFor<T>(Dictionary<string, AsyncLoader<T>> map, string path, T empty)
    {
        if (!map.TryGetValue(path, out var loader))
        {
            loader = new AsyncLoader<T>(empty);
            map[path] = loader;
        }
        return loader;
    }

    // The active repo's loaders. Each repo has its own, so its last-loaded value
    // survives while another repo is on screen — switching back is a cache hit.
    private AsyncLoader<HistoryData> _history => LoaderFor(_historyLoaders, _activeRepoPath, HistoryData.Empty);
    private AsyncLoader<ChangesData> _changes => LoaderFor(_changesLoaders, _activeRepoPath, ChangesData.Empty);
    private AsyncLoader<BranchesData> _branches => LoaderFor(_branchesLoaders, _activeRepoPath, BranchesData.Empty);

    /// <summary>Reconcile the watcher set with the repos in the active workspace
    /// (plus the active repo). Cheap to call every Build — it only diffs the sets.</summary>
    private void EnsureWatchers()
    {
        var desired = new HashSet<string>(ActiveWorkspaceRepos, PathComparer);
        if (HasActiveRepo) desired.Add(_activeRepoPath);

        foreach (var path in _watchers.Keys.ToList())
            if (!desired.Contains(path)) { _watchers[path].Dispose(); _watchers.Remove(path); }

        foreach (var path in desired)
        {
            if (_watchers.ContainsKey(path)) continue;
            try { _watchers[path] = new GitWatcher(path, () => OnExternalChange(path)); }
            catch { /* not a plain git dir (missing/submodule/worktree) — skip watching it */ }
        }
    }

    /// <summary>A watched repo changed on disk (another git client committed,
    /// pushed, staged, switched branch, or an editor saved a file). Invalidate its
    /// cache; if it's the one on screen, the rebuild reloads it. Runs on the UI
    /// thread (GitWatcher marshals the callback).</summary>
    private void OnExternalChange(string path)
    {
        BumpDataGen(path);
        MarkDirty();
    }

    // Runs off the UI thread — no UI / reactive access.
    private RepoSummary LoadSummary(string path)
    {
        var repo = RepoFor(path);
        var branch = repo.GetCurrentBranch();
        var branches = repo.GetBranches();
        var info = branches.FirstOrDefault(b => b.IsCurrent || b.Name == branch);
        var dirty = repo.GetStatus().Count;
        var version = repo.GetDescribeVersion();
        return new RepoSummary(branch, info?.AheadCount ?? 0, info?.BehindCount ?? 0, dirty, version);
    }

    private HashSet<string> IncludeSet(string path)
    {
        if (!_includeBranches.TryGetValue(path, out var set))
        {
            set = new HashSet<string>(StringComparer.Ordinal);
            _includeBranches[path] = set;
        }
        return set;
    }

    /// <summary>Repositories most often opened with the active repo, derived
    /// from shared workspace membership (a real, deterministic co-usage signal).
    /// Excludes the active repo and repos already visible in the current
    /// workspace.</summary>
    private List<string> CoUsageSuggestions()
    {
        if (!HasActiveRepo) return [];
        var inWorkspace = new HashSet<string>(ActiveWorkspaceRepos, StringComparer.OrdinalIgnoreCase);

        var scores = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var ws in _settings.Workspaces)
        {
            if (!ws.RepoPaths.Contains(_activeRepoPath)) continue;
            foreach (var p in ws.RepoPaths)
            {
                if (PathEquals(p, _activeRepoPath) || inWorkspace.Contains(p)) continue;
                scores[p] = scores.GetValueOrDefault(p) + 1;
            }
        }
        return scores.OrderByDescending(kv => kv.Value).Select(kv => kv.Key).Take(3).ToList();
    }

    private static bool PathEquals(string a, string b) =>
        string.Equals(a, b,
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    // ── Build ────────────────────────────────────────────────────────────

    public override Element Build()
    {
        // Track the revision signal so a Rerender() re-runs Build through the
        // Effect (re-subscribing to the active repo's loaders — see _revision).
        _ = _revision.Value;

        // Keep a filesystem watcher on every repo in the workspace so external
        // git activity (commits, pushes, staging, edits) invalidates the right
        // cache. Cheap: only diffs the desired vs. current watcher set.
        EnsureWatchers();

        // Key every child. The overlays below are added conditionally, so without
        // keys they reconcile by position — and when one overlay disappears while
        // another appears at the same index, the framework patches one subtree onto
        // the other and (since it doesn't copy handlers) leaks a stale handler onto
        // the new element. Distinct keys make each overlay reconcile only against
        // its own kind. (Bit us as the "+" popover's Clone handler leaking onto the
        // add-repo modal.)
        var titleBar = BuildTitleBar(); titleBar.Key = "titlebar";
        var body = BuildBody(); body.Key = "body";
        var root = Flex.Column(gap: 0)
            .FlexGrow(1)
            .Background(Nocturne.Bg)
            .TextColor(Nocturne.Text)
            .FontSize(13f)
            .Children(titleBar, body);

        void Overlay(string key, Element el) { el.Key = key; root.AddChild(el); }

        if (_diffFull) Overlay("fullscreen-diff", BuildFullscreenDiff());
        if (_toast != null) Overlay("toast", BuildToast(_toast));
        if (_paletteOpen) Overlay("palette", BuildPalette());
        if (_dialog != null) Overlay("dialog", BuildDialog(_dialog));
        if (_rebase != null) Overlay("rebase", BuildRebaseDialog(_rebase));
        if (_popover != null) Overlay("popover", BuildPopover(_popover));
        if (_modal != null) Overlay("modal", BuildModal(_modal));

        return root;
    }

    private Element BuildBody()
    {
        var body = Flex.Row(gap: 0).FlexGrow(1).AlignItems(AlignItems.Stretch);
        // Key the sidebar by collapsed-state so toggling replaces the subtree
        // instead of patching the expanded layout into the rail (which would
        // otherwise reuse a handler-less element as the expand button — the
        // framework doesn't copy event handlers on reconcile). Key the main
        // column too so the two body children reconcile by key, not position.
        var sidebar = BuildSidebar();
        sidebar.Key = _sidebarCollapsed ? "sidebar-rail" : "sidebar-expanded";
        var main = BuildMainColumn();
        main.Key = "main-column";
        body.AddChild(sidebar);
        body.AddChild(main);
        return body;
    }

    private Element BuildMainColumn()
    {
        // Key the toolbar and the view root so the two children reconcile by key.
        // Without keys they reconcile by position, which patches one view's
        // elements onto another's when switching views — and since event handlers
        // aren't copied on patch, a stale handler from the previous view leaks
        // onto the new element (e.g. History's "open commit details" footer button
        // patched onto Commit's fullscreen button, firing SetView on click).
        var toolbar = BuildToolbar();
        toolbar.Key = "toolbar";
        var col = Flex.Column(gap: 0).FlexGrow(1).Children(toolbar);

        Element view = View switch
        {
            JunctionView.History => BuildHistoryView(),
            JunctionView.Overview => BuildOverviewView(),
            JunctionView.Commit => BuildCommitView(),
            JunctionView.Changes => BuildChangesView(),
            JunctionView.Branches => BuildBranchesView(),
            _ => BuildHistoryView(),
        };
        view.Key = "view-" + View;
        col.AddChild(view);
        return col;
    }

    protected override void OnAfterBuild()
    {
        if (_pendingPaletteFocus)
        {
            _pendingPaletteFocus = false;
            _paletteRef.Current?.Focus();
        }

        if (_pendingHistorySearchFocus)
        {
            _pendingHistorySearchFocus = false;
            _historySearchRef.Current?.Focus();
        }

        if (_pendingBranchSearchFocus)
        {
            _pendingBranchSearchFocus = false;
            _branchSearchRef.Current?.Focus();
        }

        if (_pendingDialogFocus)
        {
            _pendingDialogFocus = false;
            _dialogRef.Current?.Focus();
        }

        if (_pendingRewordFocus)
        {
            _pendingRewordFocus = false;
            _rbRewordRef.Current?.Focus();
        }

        if (_pendingModalFocus)
        {
            _pendingModalFocus = false;
            _modalNameRef.Current?.Focus();
        }

        if (_pendingScrollToSelected)
        {
            _pendingScrollToSelected = false;
            if (View == JunctionView.History)
                EnsureSelectedCommitVisible();
        }
    }

    // ── View / selection actions ─────────────────────────────────────────

    private void SetView(JunctionView v)
    {
        if (View == v) return;
        _diffFull = false;   // switching views exits fullscreen (matches the design)
        View = v;   // reactive → rebuild
    }

    /// <summary>Force a rebuild through the reactive Effect (not the untracked
    /// MarkDirty path), so a newly-activated per-repo loader gets subscribed.</summary>
    private void Rerender() => _revision.Value++;

    /// <summary>Collapses the commit selection to a single commit (or none).</summary>
    private void SetCommitSelection(string? hash)
    {
        _selectedCommitHash = hash;
        _selectionAnchorHash = hash;
        _selectedCommitHashes.Clear();
        if (hash != null) _selectedCommitHashes.Add(hash);
    }

    // Selection and focus are separate, like a Windows list: _selectedCommitHash
    // is the focus caret and may sit outside (or alongside an empty) selection
    // after Ctrl+arrows / Ctrl+Space / Ctrl+click toggles.
    private bool SelectionContains(string hash) => _selectedCommitHashes.Contains(hash);

    /// <summary>Base (oldest) and head (newest) of the multi-selection compare, or
    /// null when fewer than two selected commits exist in the loaded history.</summary>
    private (string BaseHash, string HeadHash)? SelectedRange()
    {
        if (_selectedCommitHashes.Count < 2) return null;
        string? head = null, baseHash = null;
        foreach (var c in _history.Value.Commits)   // newest-first
        {
            if (!_selectedCommitHashes.Contains(c.Hash)) continue;
            head ??= c.Hash;
            baseHash = c.Hash;
        }
        return head != null && baseHash != null && head != baseHash ? (baseHash, head) : null;
    }

    private void SelectRepo(string path)
    {
        if (PathEquals(path, _activeRepoPath)) return;
        _activeRepoPath = path;
        SetCommitSelection(null);   // reset selection; History reloads and picks first
        _settings.ActiveRepoPath = path;
        Persist();
        Rerender();
    }

    private void SelectWorkspace(string id)
    {
        var ws = _settings.Workspaces.FirstOrDefault(w => w.Id == id);
        if (ws == null || id == _activeWorkspaceId) return;
        _activeWorkspaceId = id;
        _settings.ActiveWorkspaceId = id;
        // If the active repo isn't in the new workspace, switch to its first.
        if (!ws.RepoPaths.Any(p => PathEquals(p, _activeRepoPath)))
        {
            _activeRepoPath = ws.RepoPaths.FirstOrDefault() ?? "";
            SetCommitSelection(null);
            _settings.ActiveRepoPath = _activeRepoPath;
        }
        Persist();
        Rerender();
    }

    // ── Keyboard navigation across repos / workspaces ────────────────────

    /// <summary>Step the active repo within the current workspace's ordered list.
    /// With no active repo, lands on the first (delta &gt; 0) or last entry.</summary>
    private void NavigateRepo(int delta)
    {
        var repos = ActiveWorkspaceRepos;
        if (repos.Count == 0) return;
        var idx = IndexOfPath(repos, _activeRepoPath);
        var next = idx < 0
            ? (delta > 0 ? 0 : repos.Count - 1)
            : Math.Clamp(idx + delta, 0, repos.Count - 1);
        SelectRepo(repos[next]);
    }

    private void SelectRepoByIndex(int index)
    {
        var repos = ActiveWorkspaceRepos;
        if (index < 0 || index >= repos.Count) return;
        SelectRepo(repos[index]);
    }

    private void SelectWorkspaceByIndex(int index)
    {
        var list = _settings.Workspaces;
        if (index < 0 || index >= list.Count) return;
        SelectWorkspace(list[index].Id);
    }

    private static int IndexOfPath(IReadOnlyList<string> paths, string target)
    {
        if (string.IsNullOrEmpty(target)) return -1;
        for (var i = 0; i < paths.Count; i++)
            if (PathEquals(paths[i], target)) return i;
        return -1;
    }

    private void ToggleSidebar()
    {
        _sidebarCollapsed = !_sidebarCollapsed;
        // Collapsing/expanding changes the content width, which may cross a
        // responsive breakpoint (e.g. reveal the full toolbar); recompute first.
        RecomputeResponsive();
        MarkDirty();
    }

    private void RefreshAll()
    {
        BumpDataGen(_activeRepoPath);
        // Rerender (reactive) rather than MarkDirty: some callers (repo/workspace
        // mutations) have just changed the active repo, so the rebuild must
        // re-subscribe the Effect to the newly-active repo's loaders.
        Rerender();
    }

    private void Persist()
    {
        _settings.ActiveWorkspaceId = _activeWorkspaceId;
        _settings.ActiveRepoPath = _activeRepoPath;
        GitClientSettings.Save(_settings);
    }

    // ── Toast ────────────────────────────────────────────────────────────

    private async void Flash(string message)
    {
        _toastCts?.Cancel();
        _toast = message;
        _toastCts = new CancellationTokenSource();
        var token = _toastCts.Token;
        MarkDirty();
        try
        {
            await Task.Delay(1700, token);
            _toast = null;
            MarkDirty();
        }
        catch (OperationCanceledException) { }
    }

    private void CopyToClipboard(string text)
    {
        _window.Clipboard.SetText(text);
        Flash($"Copied {text}");
    }

    // ── Keyboard ─────────────────────────────────────────────────────────

    private void HandleKeyDown(KeyDownEvent e)
    {
        var ctrl = e.Modifiers.HasFlag(KeyModifiers.Ctrl) || e.Modifiers.HasFlag(KeyModifiers.Super);
        var alt = e.Modifiers.HasFlag(KeyModifiers.Alt);
        var typing = e.Target is TextInputElement or TextAreaElement;

        // The repo/workspace popover and modal are lightweight; Esc dismisses them,
        // and (for the modal) typing flows to the focused field.
        if (_popover != null)
        {
            if (e.Key == KeyCode.Escape) { _popover = null; MarkDirty(); e.StopPropagation = true; }
            return;
        }
        if (_modal != null)
        {
            if (e.Key == KeyCode.Escape) { CloseModal(); e.StopPropagation = true; }
            return;
        }

        // The interactive-rebase modal owns the keyboard while open. Its reword
        // sub-dialog (a textarea) only reacts to Esc; everything else types.
        if (_rebase != null)
        {
            if (_rbRewordIndex != null)
            {
                if (e.Key == KeyCode.Escape) { CloseReword(); e.StopPropagation = true; }
                return;
            }
            if (HandleRebaseKey(e)) e.StopPropagation = true;
            return;
        }

        // A modal dialog swallows every global shortcut; only Esc (cancel) acts,
        // and typing flows straight to the focused field.
        if (_dialog != null)
        {
            if (e.Key == KeyCode.Escape) { CloseDialog(); e.StopPropagation = true; }
            return;
        }

        // ⌘K / Ctrl+K toggles the palette from anywhere.
        if (ctrl && e.Key == KeyCode.K)
        {
            TogglePalette();
            e.StopPropagation = true;
            return;
        }

        // ⌘D / Ctrl+D opens the selected file's diff in VS Code (Commit/Changes
        // views) — the primary way to inspect a diff when the pane is hidden on
        // narrow windows, but available at any size.
        if (ctrl && e.Key == KeyCode.D && View is JunctionView.Commit or JunctionView.Changes)
        {
            OpenSelectedDiffExternally();
            e.StopPropagation = true;
            return;
        }

        if (_paletteOpen)
        {
            if (e.Key == KeyCode.Escape) { ClosePalette(); e.StopPropagation = true; }
            return;
        }

        if (e.Key == KeyCode.Escape && _diffFull)
        {
            _diffFull = false;
            MarkDirty();
            e.StopPropagation = true;
            return;
        }

        // Global repo / workspace navigation. Alt = repositories, Ctrl/Cmd =
        // workspaces. Handled before the typing guard so they act like true
        // global shortcuts even while a field is focused (only modal overlays,
        // handled above, take precedence). Alt+↑/↓ steps through the repos;
        // Alt+N / Ctrl+N jump to the Nth entry (1-based). Ctrl+↑/↓ belongs to
        // the History list (focus caret) — workspaces switch via Ctrl+N only.
        if (alt || ctrl)
        {
            if (e.Key is >= KeyCode.Digit1 and <= KeyCode.Digit9)
            {
                var index = e.Key - KeyCode.Digit1;
                if (alt) SelectRepoByIndex(index); else SelectWorkspaceByIndex(index);
                e.StopPropagation = true;
                return;
            }
            if (alt && e.Key is KeyCode.Up or KeyCode.Down)
            {
                NavigateRepo(e.Key == KeyCode.Down ? 1 : -1);
                e.StopPropagation = true;
                return;
            }
        }

        if (typing) return;

        // Backtick (the key left of 1) toggles the sidebar.
        if (e.Key == KeyCode.Backtick)
        {
            ToggleSidebar();
            e.StopPropagation = true;
            return;
        }

        // Number keys switch views.
        var viewForKey = e.Key switch
        {
            KeyCode.Digit1 => JunctionView.Overview,
            KeyCode.Digit2 => JunctionView.History,
            KeyCode.Digit3 => JunctionView.Commit,
            KeyCode.Digit4 => JunctionView.Changes,
            KeyCode.Digit5 => JunctionView.Branches,
            _ => (JunctionView?)null,
        };
        if (viewForKey is { } v) { SetView(v); e.StopPropagation = true; return; }

        // Commit view: arrows/page/home/end move the file selection — unless the
        // diff pane is focused, which consumes those keys to scroll (StopPropagation).
        if (View == JunctionView.Commit)
        {
            if (MoveFileByKey(e))
                e.StopPropagation = true;
            return;
        }

        if (View == JunctionView.History)
        {
            if (e.Key == KeyCode.Slash)
            {
                _pendingHistorySearchFocus = true;
                MarkDirty();
                e.StopPropagation = true;
                return;
            }
            if (HandleHistoryNavKey(e))
                e.StopPropagation = true;
        }

        if (View == JunctionView.Branches)
        {
            if (e.Key == KeyCode.Slash)
            {
                _pendingBranchSearchFocus = true;
                MarkDirty();
                e.StopPropagation = true;
                return;
            }
            if (HandleBranchKey(e))
                e.StopPropagation = true;
        }
    }

    /// <summary>Commit-list navigation shared by the global handler and the history
    /// search box (so arrows/PageUp-Down/Enter work while typing a query too).</summary>
    private bool HandleHistoryNavKey(KeyDownEvent e)
    {
        // Windows-list modifiers: Shift extends the selection from the anchor,
        // Ctrl moves only the focus caret (Ctrl+Shift adds the range instead of
        // replacing the selection), Ctrl+Space toggles the focused commit.
        var shift = e.Modifiers.HasFlag(KeyModifiers.Shift);
        var ctrl = e.Modifiers.HasFlag(KeyModifiers.Ctrl);
        var focusOnly = ctrl && !shift;
        switch (e.Key)
        {
            case KeyCode.Down: MoveCommitSelection(1, shift, focusOnly, ctrl); return true;
            case KeyCode.Up: MoveCommitSelection(-1, shift, focusOnly, ctrl); return true;
            case KeyCode.PageDown: MoveCommitSelection(HistoryPageSize(), shift, focusOnly, ctrl); return true;
            case KeyCode.PageUp: MoveCommitSelection(-HistoryPageSize(), shift, focusOnly, ctrl); return true;
            case KeyCode.Home: MoveCommitSelection(int.MinValue / 2, shift, focusOnly, ctrl); return true;
            case KeyCode.End: MoveCommitSelection(int.MaxValue / 2, shift, focusOnly, ctrl); return true;
            case KeyCode.Space when ctrl: ToggleFocusedCommitSelection(); return true;
            case KeyCode.Enter: SetView(JunctionView.Commit); return true;
            default: return false;
        }
    }

    private int HistoryPageSize()
    {
        var viewH = _commitListRef.Current?.Layout.AbsoluteContentBox.Height ?? (10 * RowHeight);
        return Math.Max(1, (int)(viewH / RowHeight) - 1);
    }

    public void Dispose()
    {
        _window.Resized -= OnWindowResize;
        _toastCts?.Cancel();
        _toastCts?.Dispose();
        foreach (var w in _watchers.Values) w.Dispose();
        _watchers.Clear();
    }
}
