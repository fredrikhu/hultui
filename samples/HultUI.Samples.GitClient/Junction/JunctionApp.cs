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

    // One GitRepository per path, plus a background summary loader per path so
    // the sidebar's branch / ahead-behind / dirty badges never block Build().
    private readonly Dictionary<string, GitRepository> _repos = new();
    private readonly Dictionary<string, AsyncLoader<RepoSummary>> _summaryLoaders = new();
    private int _summaryGen;

    // Active pointers.
    private string _activeWorkspaceId;
    private string _activeRepoPath;

    // View + chrome state.
    [Reactive] public partial JunctionView View { get; set; }
    private bool _sidebarCollapsed;

    // History state.
    private readonly AsyncLoader<HistoryData> _history = new(HistoryData.Empty);
    private int _historyGen;
    private string? _selectedCommitHash;
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
    private string? _selectedFilePath;    // file selected in the Commit view

    // Changes (staging) state.
    private readonly AsyncLoader<ChangesData> _changes = new(ChangesData.Empty);
    private string? _changeSelPath;
    private bool _changeSelStaged;
    private string _commitSummary = "";
    private string _commitDescription = "";
    private readonly Ref<TextInputElement> _summaryRef = new();
    private readonly Ref<TextAreaElement> _descRef = new();

    // Diff mode is a single app-level toggle shared by all diff panes.
    private bool _diffSplit;
    private bool _diffFull;

    // Command palette.
    private bool _paletteOpen;
    private string _paletteQuery = "";
    private int _paletteSel;
    private readonly Ref<TextInputElement> _paletteRef = new();
    private bool _pendingPaletteFocus;

    // Toast.
    private string? _toast;
    private CancellationTokenSource? _toastCts;

    private bool _remoteOpRunning;

    private readonly record struct RepoSummary(string Branch, int Ahead, int Behind, int Dirty)
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
        Dictionary<string, GitFileDiff> DiffByPath)
    {
        public static readonly CommitDetail Empty = new(null, [], new());
    }

    private readonly record struct ChangesData(
        IReadOnlyList<GitFileChange> Staged,
        IReadOnlyList<GitFileChange> Unstaged,
        Dictionary<string, GitFileDiff> StagedDiffs,
        Dictionary<string, GitFileDiff> UnstagedDiffs)
    {
        public static readonly ChangesData Empty = new([], [], new(), new());
    }

    public JunctionApp(Application app, Window window, GitClientSettingsData settings)
    {
        _app = app;
        _window = window;
        _settings = settings;
        _settings.Normalize();

        _activeWorkspaceId = _settings.ActiveWorkspaceId ?? _settings.Workspaces.FirstOrDefault()?.Id ?? "";
        _activeRepoPath = _settings.ActiveRepoPath ?? ActiveWorkspace?.RepoPaths.FirstOrDefault() ?? "";

        View = JunctionView.History;

        // Global keys land on the host when nothing is focused (see App.cs for
        // the same rationale). Materialise the host so the handler is wired.
        Mount().On<KeyDownEvent>(HandleKeyDown);
    }

    // ── Model access ─────────────────────────────────────────────────────

    private WorkspaceData? ActiveWorkspace =>
        _settings.Workspaces.FirstOrDefault(w => w.Id == _activeWorkspaceId)
        ?? _settings.Workspaces.FirstOrDefault();

    private IReadOnlyList<string> ActiveWorkspaceRepos =>
        ActiveWorkspace?.RepoPaths ?? [];

    private bool HasActiveRepo => !string.IsNullOrEmpty(_activeRepoPath);

    private GitRepository RepoFor(string path)
    {
        if (!_repos.TryGetValue(path, out var repo))
        {
            repo = new GitRepository(path);
            _repos[path] = repo;
        }
        return repo;
    }

    private GitRepository? ActiveRepo => HasActiveRepo ? RepoFor(_activeRepoPath) : null;

    private RepoSummary SummaryFor(string path)
    {
        if (!_summaryLoaders.TryGetValue(path, out var loader))
        {
            loader = new AsyncLoader<RepoSummary>(RepoSummary.Pending);
            _summaryLoaders[path] = loader;
        }
        loader.Load($"{path}#{_summaryGen}", () => LoadSummary(path));
        return loader.Value;
    }

    // Runs off the UI thread — no UI / reactive access.
    private RepoSummary LoadSummary(string path)
    {
        var repo = RepoFor(path);
        var branch = repo.GetCurrentBranch();
        var branches = repo.GetBranches();
        var info = branches.FirstOrDefault(b => b.IsCurrent || b.Name == branch);
        var dirty = repo.GetStatus().Count;
        return new RepoSummary(branch, info?.AheadCount ?? 0, info?.BehindCount ?? 0, dirty);
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
        var root = Flex.Column(gap: 0)
            .FlexGrow(1)
            .Background(Nocturne.Bg)
            .TextColor(Nocturne.Text)
            .FontSize(13f)
            .Children(
                BuildTitleBar(),
                BuildBody()
            );

        if (_diffFull)
            root.AddChild(BuildFullscreenDiff());

        if (_toast != null)
            root.AddChild(BuildToast(_toast));

        if (_paletteOpen)
            root.AddChild(BuildPalette());

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
        var col = Flex.Column(gap: 0).FlexGrow(1).Children(BuildToolbar());

        Element view = View switch
        {
            JunctionView.History => BuildHistoryView(),
            JunctionView.Overview => BuildStubView("Overview", "Workspace dashboard — coming next."),
            JunctionView.Commit => BuildCommitView(),
            JunctionView.Changes => BuildChangesView(),
            JunctionView.Branches => BuildStubView("Branches", "Branch management table — coming next."),
            _ => BuildHistoryView(),
        };
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
        _diffFull = false;   // switching views exits fullscreen (matches the design)
        if (View == v) return;
        View = v;   // reactive → rebuild
    }

    private void SelectRepo(string path)
    {
        if (PathEquals(path, _activeRepoPath)) return;
        _activeRepoPath = path;
        _selectedCommitHash = null;   // reset selection; History reloads and picks first
        _settings.ActiveRepoPath = path;
        Persist();
        MarkDirty();
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
            _selectedCommitHash = null;
            _settings.ActiveRepoPath = _activeRepoPath;
        }
        Persist();
        MarkDirty();
    }

    private void ToggleSidebar()
    {
        _sidebarCollapsed = !_sidebarCollapsed;
        MarkDirty();
    }

    private void RefreshAll()
    {
        _summaryGen++;
        _historyGen++;
        MarkDirty();
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
        var typing = e.Target is TextInputElement or TextAreaElement;

        // ⌘K / Ctrl+K toggles the palette from anywhere.
        if (ctrl && e.Key == KeyCode.K)
        {
            TogglePalette();
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
    }

    /// <summary>Commit-list navigation shared by the global handler and the history
    /// search box (so arrows/PageUp-Down/Enter work while typing a query too).</summary>
    private bool HandleHistoryNavKey(KeyDownEvent e)
    {
        switch (e.Key)
        {
            case KeyCode.Down: MoveCommitSelection(1); return true;
            case KeyCode.Up: MoveCommitSelection(-1); return true;
            case KeyCode.PageDown: MoveCommitSelection(HistoryPageSize()); return true;
            case KeyCode.PageUp: MoveCommitSelection(-HistoryPageSize()); return true;
            case KeyCode.Home: MoveCommitSelection(int.MinValue / 2); return true;
            case KeyCode.End: MoveCommitSelection(int.MaxValue / 2); return true;
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
        _toastCts?.Cancel();
        _toastCts?.Dispose();
    }
}
