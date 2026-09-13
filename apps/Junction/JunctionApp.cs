using HultUI;
using HultUI.Core.Builders;
using HultUI.Core.Components;
using HultUI.Core.Elements;
using HultUI.Core.Events;
using HultUI.Core.Layout;
using HultUI.Core.Platform;
using HultUI.Core.Primitives;
using HultUI.Core.Styling;
using HultUI.Core.Reactivity;
using Junction.Git;
using Junction.Theme;

namespace Junction;

public enum JunctionView { Overview, History, Commit, Changes, Branches, Tags, Files }

/// <summary>
/// Root of the Junction multi-repo git client: a workspace-oriented shell
/// (title bar · sidebar · seven segmented views) over the real <see cref="GitRepository"/>
/// backend. This is the Nocturne redesign of the Junction sample.
///
/// <para>The shell, sidebar (workspaces / repositories / co-usage), command palette and all
/// seven views — Overview, History, Commit, Changes, Branches, Tags, Files, on keys 1–7 —
/// render real repository data.</para>
/// </summary>
public partial class JunctionApp : Component, IDisposable
{
    private readonly Application _app;
    private readonly Window _window;
    private readonly JunctionSettingsData _settings;
    private readonly IFileDialogs _fileDialogs;

    // One GitRepository per path, plus a background summary loader per path so
    // the sidebar's branch / ahead-behind / dirty badges never block Build().
    private readonly Dictionary<string, GitRepository> _repos = new();
    private readonly KeyedAsyncLoader<string, RepoSummary> _summaryLoaders =
        new(RepoSummary.Pending, capacity: 32, comparer: PathComparer);

    // Per-repo data generation. Every cached loader keys on it, so a repo's
    // views/summary reload only when *that* repo's data actually changed (a git
    // action here, or an external change the watcher reports). Unrelated repos
    // keep their cached values, which is what makes switching between them fast.
    private readonly Dictionary<string, int> _dataGen = new();

    // One filesystem watcher per repo in the active workspace, so commits, pushes
    // and edits made by other git clients are noticed and the affected repo's
    // cache is invalidated.
    private readonly Dictionary<string, GitWatcher> _watchers = new(PathComparer);

    // The GitLab identities the app is signed in with, and the clients they yield. Owns the
    // token/identity split (see GitLab.GitLabAccounts); the pane that shows them is in
    // JunctionApp.GitLabAccounts.cs.
    private readonly GitLab.GitLabAccounts _gitlab;

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
    // toolbar needs ~1100 dip of content to lay out without clipping its trailing
    // buttons (the tabs are FlexShrink(0), so they no longer compress to fit); the
    // rest is headroom for the wider system fonts other platforms resolve.
    //
    // Measured, not estimated — and re-measured whenever the toolbar gains
    // something (see RepositoryToolsTests). The 910 this used to be was measured
    // against a five-tab toolbar; the two tabs added since pushed the real
    // requirement to ~1050, so every window between the two numbers had been
    // laying out a toolbar that ran off its own row and took the stash button
    // with it.
    private const float CompactChromeContentWidth = 1140f;
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
    private readonly KeyedAsyncLoader<string, HistoryData> _historyLoaders =
        new(HistoryData.Empty, capacity: 16, comparer: PathComparer);
    // The +/- counts per commit, by hash. The value only ever grows: a hash's numstat never
    // changes, so each chunk landing merges into what was there, and a reload after a fetch
    // or a commit computes only the commits it did not have (see EnsureHistoryStatsLoaded).
    private readonly KeyedAsyncLoader<string, Dictionary<string, (int Added, int Deleted)>> _historyStatLoaders =
        new(NoStats, capacity: 16, comparer: PathComparer);
    // Multi-selection à la Windows lists: _selectedCommitHash is the focused/lead
    // commit (keyboard nav, context menus), _selectedCommitHashes the full selection
    // (always contains the focused one), _selectionAnchorHash the Shift-range anchor.
    private string? _selectedCommitHash;
    private readonly HashSet<string> _selectedCommitHashes = new(StringComparer.Ordinal);
    private string? _selectionAnchorHash;

    // Branch-tab compare: the Branches list is a Windows-style multi-select (plain
    // click/nav single-selects, Ctrl toggles, Ctrl+arrows move the caret only,
    // Ctrl+Space toggles the focused row). Selection is tracked by branch identity
    // (full ref name) in _selectedBranches, so two branches at the SAME commit are
    // both representable (→ a "no differences" compare), which a tip-hash set could
    // not. When ≥2 are selected, _branchCompare caches the oldest→newest tip pair
    // (base = older, head = newer) plus labels; SelectedRange returns it directly, so
    // the diff resolves even when a tip is filtered out of the loaded History list.
    // A History selection edit clears it (see EndBranchCompare / SetCommitSelection).
    private readonly record struct BranchCompare(
        string BaseFull, string HeadFull,
        string BaseHash, string HeadHash,
        string BaseLabel, string HeadLabel);
    private BranchCompare? _branchCompare;
    private readonly HashSet<string> _selectedBranches = new(StringComparer.Ordinal);
    private string _historyQuery = "";
    private readonly Ref<TextInputElement> _historySearchRef = new();
    private bool _pendingHistorySearchFocus;
    // Content search (pickaxe): a server-side `git log -S/-G` re-run, distinct from
    // the client-side _historyQuery quick filter. Empty ⇒ inactive.
    private bool _contentSearchOpen;
    private string _contentQuery = "";
    private string _contentApplied = "";       // the query actually loaded (drives the key)
    private bool _contentRegex;                 // false = -S literal, true = -G regex
    private readonly Ref<TextInputElement> _contentSearchRef = new();
    private bool _pendingContentSearchFocus;
    // Per-repo "include in history" branch sets. Empty ⇒ all branches.
    private readonly Dictionary<string, HashSet<string>> _includeBranches = new();
    private readonly Ref<VirtualListElement<GitCommit>> _commitListRef = new();
    private bool _pendingScrollToSelected;
    /// <summary>A commit another view asked History to reveal (the blame gutter), held until
    /// the log it has to be found in is loaded. See <c>ResolvePendingReveal</c>.</summary>
    private string? _pendingRevealHash;

    // Commit-details state.
    // The file lists in Commit and Changes are as wide as the user last dragged them.
    // Bounded so a drag can neither hide the list nor squeeze the diff out of existence.
    private const float MinFileListWidth = 150f;
    private const float MaxFileListWidth = 720f;
    private readonly PaneSplitter _commitSplit;
    private readonly PaneSplitter _changesSplit;

    private readonly AsyncLoader<CommitDetail> _commitDetail = new(CommitDetail.Empty);
    private string? _detailHash;          // commit the loaded detail belongs to
    // Multi-select compare shown instead: diff between the oldest and newest
    // selected commits (Base = oldest, Head = newest). Null in single-commit mode.
    private (string BaseHash, string HeadHash)? _detailRange;
    private string? _selectedFilePath;    // file selected in the Commit view
    // The Commit view's file list is a folder tree (JunctionApp.ChangeTree.cs). The
    // cursor is the highlighted row and may sit on a folder; _selectedFilePath stays the
    // file the diff pane shows, so crossing a folder doesn't blank it. _commitFileRows is
    // the visible-row order, rebuilt each Build so the arrow keys step what's on screen.
    private readonly TreeView<GitFileChange> _commitFilesTree;
    private List<TreeNode<GitFileChange>> _commitFileRows = [];
    private string? _commitCursorPath;
    private string? _commitTreeKey;       // the commit (or range) the expansion belongs to

    // Changes (staging) state. Multi-selection à la Windows lists, the same model as
    // the History list above: the three _changeSel* fields are the FOCUS caret — the
    // one file the diff pane, Ctrl+D and the line-level staging selection follow —
    // while _changeSelection is the selection set and _changeAnchor anchors Shift
    // ranges. The selection may legitimately go empty, and the caret may sit outside
    // it after Ctrl+arrows / Ctrl+Space.
    //
    // One rule is specific to this list: a selection never spans two of the three
    // sections (Conflicts / Staged / Changes). "Stage these files" has no single
    // meaning for a set that is half staged and half not, so any range or toggle that
    // would cross a section boundary starts a fresh selection in the clicked row's
    // section instead — see OnChangeRowClick and MoveChangeSelection.
    private readonly KeyedAsyncLoader<string, ChangesData> _changesLoaders =
        new(ChangesData.Empty, capacity: 16, comparer: PathComparer);
    private string? _changeSelPath;
    private bool _changeSelStaged;
    // True when the selected file is a merge conflict; the right pane then shows the
    // ours/theirs resolution UI instead of a diff.
    private bool _changeSelConflicted;
    private readonly HashSet<ChangeRef> _changeSelection = new();
    private ChangeRef? _changeAnchor;
    // The focus caret. It may sit on a folder row, which the three _changeSel* fields
    // above deliberately do not follow: the right pane keeps showing the file it was
    // showing, rather than blanking every time the caret crosses a folder.
    private ChangeRef? _changeCaret;
    // One folder tree per section (JunctionApp.ChangeTree.cs). Separate trees because
    // they are separate lists: collapsing `src` among the staged files says nothing
    // about the same folder among the unstaged ones.
    private readonly Dictionary<ChangeSection, TreeView<GitFileChange>> _changeTrees = new();
    private string? _changeTreeRepo;   // the repository the expansion state belongs to
    // How many files the selection covers, counted once per build so every row that
    // offers an action can label itself with it without walking the list again.
    private int _changeSelFileCount;
    // Scroll the keyboard-focused file row into view (the list isn't focused, so it
    // won't follow the caret on its own).
    private readonly Ref<ScrollViewElement> _changeScrollRef = new();
    private readonly Ref<Element> _changeFocusRef = new();
    private string _commitSummary = "";
    private string _commitDescription = "";
    private readonly Ref<TextInputElement> _summaryRef = new();
    private readonly Ref<TextAreaElement> _descRef = new();
    /// <summary>Whether the composer rewrites HEAD instead of adding a commit. Not persisted
    /// and cleared after every commit: amending is a decision about one commit, and a checkbox
    /// that survived would quietly rewrite the next one too.</summary>
    private bool _amendCommit;
    /// <summary>HEAD's message as it was poured into the fields when amend was switched on, so
    /// switching it back off can take out exactly what it put in and leave anything typed
    /// since then alone.</summary>
    private string? _amendPrefill;

    // Branches (management) state. The list itself lives in the repo summary (see
    // RepoSummary); _sortedBranches memoizes its display order per loaded list.
    private (IReadOnlyList<GitBranch> Source, IReadOnlyList<GitBranch> Sorted) _sortedBranches = ([], []);
    private string _branchQuery = "";
    private int _branchSel;
    private readonly Ref<TextInputElement> _branchSearchRef = new();
    private readonly Ref<VirtualListElement<TreeNode<GitBranch>>> _branchScrollRef = new();
    private bool _pendingBranchSearchFocus;

    // Tags state, shaped like the branches state above.
    private readonly KeyedAsyncLoader<string, TagsData> _tagsLoaders =
        new(TagsData.Empty, capacity: 16, comparer: PathComparer);
    private string _tagQuery = "";
    private int _tagSel;
    private readonly Ref<TextInputElement> _tagSearchRef = new();
    private readonly Ref<VirtualListElement<TreeNode<GitTag>>> _tagScrollRef = new();
    private bool _pendingTagSearchFocus;

    // Diff mode is a single app-level toggle shared by all diff panes.
    private bool _diffSplit;
    private bool _diffFull;

    // Commit-view file list: scroll the keyboard-selected file into view.
    private readonly Ref<ScrollViewElement> _fileScrollRef = new();
    private readonly Ref<Element> _fileSelRef = new();

    // Command palette.
    private bool _paletteOpen;
    private string _paletteQuery = "";
    private int _paletteSel;
    private readonly Ref<TextInputElement> _paletteRef = new();
    private bool _pendingPaletteFocus;
    private readonly Ref<ScrollViewElement> _paletteScrollRef = new();
    private readonly Ref<Element> _paletteSelRef = new();

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

    /// <summary>What the sidebar badge and the Overview card say about a repository — and,
    /// since the ahead/behind counts come from listing every ref anyway, the whole branch
    /// list. The Branches view reads it from here rather than asking git a second time,
    /// which also means every repository the sidebar shows has its Branches tab warm.</summary>
    private readonly record struct RepoSummary(string Branch, int Ahead, int Behind, int Dirty, string Version = "")
    {
        public static readonly RepoSummary Pending = new("…", 0, 0, 0);
        public IReadOnlyList<GitBranch> Branches { get; init; } = [];
        /// <summary>The configured remotes — what says where the repository is hosted (see
        /// GitLabRemoteFor). One <c>git remote -v</c>, read with the rest of the summary.</summary>
        public IReadOnlyList<GitRemote> Remotes { get; init; } = [];
    }

    /// <summary>The commit list itself. The per-commit +/- counts are deliberately not
    /// here: they come from a second <c>git log --numstat</c> pass that costs many times
    /// what the log does (about a second against a hundred milliseconds on a repository of
    /// a few hundred commits), so they load behind the list in <see cref="_historyStatLoaders"/>
    /// and fill in once the rows are already on screen.</summary>
    /// <param name="Key">The log this window of commits belongs to — repository, include set,
    /// content search and data generation. The next page is appended only onto data with the
    /// same key; anything else is reloaded from the top (see EnsureHistoryLoaded).</param>
    /// <param name="Depth">How many commits were asked for.</param>
    /// <param name="Exhausted">True when git had fewer commits than were asked for — there is
    /// no older page to load.</param>
    private readonly record struct HistoryData(
        IReadOnlyList<GitCommit> Commits,
        Dictionary<string, GitGraphRow> Graph,
        HashSet<string> Remotes,
        string Key = "",
        int Depth = 0,
        bool Exhausted = false)
    {
        public static readonly HistoryData Empty = new([], new(), new());
    }

    private static readonly Dictionary<string, (int Added, int Deleted)> NoStats = new();

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
        IReadOnlyList<GitFileChange> Conflicted,
        ConflictSides? Sequencer,
        IReadOnlyList<string> AssumeUnchanged,
        // HEAD's full message, or null in a repository with no commits yet. Read here rather
        // than on the UI thread because that is the one place in this view already off it,
        // and it answers both of the amend checkbox's questions: whether there is anything to
        // amend, and what to put in the fields when you say so.
        string? HeadMessage = null)
    {
        public static readonly ChangesData Empty = new([], [], new(), new(), [], null, []);

        /// <summary>Whether a merge, rebase, cherry-pick or revert is half-finished. The
        /// banner, the always-visible right pane and the "no changes" placeholder all key
        /// off this rather than off the conflict count: an operation with every conflict
        /// resolved is still an operation waiting to be continued or aborted.</summary>
        public bool InSequencer => Sequencer != null;
    }

    /// <summary>Which of the Changes view's three lists a file row lives in. Part of a
    /// row's identity rather than a property of the file: a partially-staged file has a
    /// row in <see cref="ChangeSection.Staged"/> AND one in <see cref="ChangeSection.Unstaged"/>,
    /// and they select independently.</summary>
    private enum ChangeSection { Conflicted, Staged, Unstaged }

    /// <summary>A row in the Changes list: a file, or — since the three lists group by
    /// folder — the folder gathering some of them. A folder row selects, stages and
    /// unstages as everything below it, so it needs an identity of its own; a file and
    /// the folder of the same name could never both exist in git, but the flag keeps the
    /// two kinds of row from ever comparing equal by accident.</summary>
    private readonly record struct ChangeRef(string Path, ChangeSection Section, bool IsFolder = false);

    /// <summary>The half-finished operation plus the two commits its conflicts are
    /// between, resolved once per load so the resolution pane can name them.</summary>
    /// <remarks>Which side is which comes from <see cref="SequencerState"/>, not from an
    /// assumption here — during a rebase "ours" is the branch being rebased onto and
    /// "theirs" is the user's own commit, the reverse of a merge.</remarks>
    private readonly record struct ConflictSides(
        SequencerState State, GitCommit? Ours, GitCommit? Theirs);

    private readonly record struct TagsData(IReadOnlyList<GitTag> All)
    {
        public static readonly TagsData Empty = new([]);
    }

    /// <param name="historyPageCommits">How many commits a page of the History log holds.
    /// A test shrinks it to page through a small repository; the first page loads in this
    /// constructor, so it cannot be set afterwards.</param>
    /// <param name="credentialStore">Where GitLab tokens are kept. Defaults to the OS store;
    /// tests hand in an in-memory one so nothing lands in the developer's Credential Manager.</param>
    /// <param name="gitLabHandler">Per host, the HTTP handler GitLab calls go through — null
    /// for the network. Tests hand in a recorded one.</param>
    public JunctionApp(Application app, Window window, JunctionSettingsData settings,
        IFileDialogs? fileDialogs = null, int historyPageCommits = DefaultHistoryPageCommits,
        ICredentialStore? credentialStore = null,
        Func<string, System.Net.Http.HttpMessageHandler?>? gitLabHandler = null)
    {
        _app = app;
        _window = window;
        _settings = settings;
        _historyPageCommits = historyPageCommits;
        _settings.Normalize();
        _gitlab = new GitLab.GitLabAccounts(_settings.GitLabAccounts,
            credentialStore ?? CredentialStore.Default, Persist, gitLabHandler);
        // The built-in widgets read the Application theme rather than the colours this app
        // paints with, and TextInputElement.ApplyTheme re-reads it on every style pass — so
        // an unthemed Application leaves the search boxes and commit fields flat white in an
        // otherwise Nocturne window. Program.cs sets this too, before the first frame; doing
        // it here as well means anything that hosts JunctionApp directly (tests, screenshot
        // harnesses) gets the same window the user sees. Mirrors CommanderApp.
        Nocturne.Use(_settings.Appearance.Theme);
        _app.Theme = Nocturne.ToHultTheme();
        // The window's dialogs are the real ones on a desktop and the scriptable
        // headless pair under test, so nothing needs substituting for either.
        _fileDialogs = fileDialogs ?? window.FileDialogs;
        // The diff's sideways offset is app state, so moving it has to rebuild the rows.
        _diffPan = new HorizontalPan(MarkDirty);
        // Same for the two file-list widths: the panes are rebuilt at the current width, so
        // a drag marks dirty rather than writing to an element that the next build replaces.
        _commitSplit = new PaneSplitter(MarkDirty, Persist, MinFileListWidth, MaxFileListWidth,
            settings.CommitFileListWidth);
        _changesSplit = new PaneSplitter(MarkDirty, Persist, MinFileListWidth, MaxFileListWidth,
            settings.ChangesFileListWidth);
        _diffFind = new FindBarState(MarkDirty);
        _filesFind = new FindBarState(MarkDirty);
        _filesTree = new TreeView<string>(MarkDirty);
        _commitFilesTree = NewChangeTree();
        _diffWrap = _settings.DiffWrap;
        _diffSplit = _settings.DiffSplit;
        _commitMsgExpanded = _settings.CommitMessageExpanded;
        _refTreeView = _settings.RefTreeView;

        _activeWorkspaceId = _settings.ActiveWorkspaceId ?? _settings.Workspaces.FirstOrDefault()?.Id ?? "";
        _activeRepoPath = _settings.ActiveRepoPath ?? ActiveWorkspace?.RepoPaths.FirstOrDefault() ?? "";

        View = JunctionView.History;
        // Startup opens on History; warm the other tabs behind it.
        RequestPreload(_activeRepoPath, full: true);

        // The user's zoom goes on before the first layout, so the window opens at the size
        // they left it at rather than snapping a frame later.
        _window.UserScale = _settings.Appearance.UiScale;
        _compactChrome = ContentWidth < CompactChromeContentWidth;
        _hideDiff = ContentWidth < HideDiffContentWidth;
        Ui.DisplayScale = _window.DisplayScale;
        _window.Resized += OnWindowResize;

        // Global keys land on the host when nothing is focused (see App.cs for
        // the same rationale). Materialise the host so the handler is wired.
        Mount().On<KeyDownEvent>(HandleKeyDown);

        RestartAutoFetch();
    }

    // Toggle compact chrome as the window crosses the breakpoint. Only rerender on
    // an actual crossing so ordinary resizes above/below the threshold don't churn
    // the build every frame of a drag.
    private void OnWindowResize()
    {
        // Dragging to a monitor with different scaling arrives as a resize. The rasters
        // cached for the old scale are retired here, so a rebuild has to follow
        // unconditionally: without it the tree keeps holding retired sources and every
        // icon disappears until something else happens to rebuild — which is why the
        // icons only came back on crossing the responsive breakpoint.
        float scale = _window.DisplayScale;
        bool scaleChanged = MathF.Abs(Ui.DisplayScale - scale) >= 0.001f;
        Ui.DisplayScale = scale;
        if (RecomputeResponsive() || scaleChanged || DiffWrapNeedsRemeasure()) Rerender();
    }

    /// <summary>
    /// Whether a wrapped diff is on screen, and so the resize has to reach the build.
    ///
    /// <para>Every other row in this app is a fixed height, which is why an ordinary resize
    /// deliberately does not rebuild: layout alone repositions everything. A wrapped row is
    /// the exception — its height <i>is</i> a function of the width, measured in the build
    /// and handed to the virtual list as a per-row number. Resize without rebuilding and the
    /// list lays out at the new width using heights measured at the old one, so every line
    /// that now takes an extra visual line overflows onto the row beneath it.</para>
    ///
    /// <para>Narrowed by dragging the pane's own splitter this never showed, because that
    /// drag is app state and rebuilds anyway; it took a window resize, which does not.</para>
    /// </summary>
    private bool DiffWrapNeedsRemeasure() =>
        _diffWrap && (_diffFull || View is JunctionView.Commit or JunctionView.Changes);

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
        var loader = _summaryLoaders.For(path);
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
        Invalidate(path);
        // A change of the user's or the watcher's making: the retry budget starts over.
        _loadRetries.Remove(path);
    }

    private void Invalidate(string path) => _dataGen[path] = DataGen(path) + 1;

    // ── Refused loads ────────────────────────────────────────────────────
    //
    // The queries behind the History, Changes, Commit and Files views throw
    // GitCommandException when git refuses them, and a loader keeps its previous value
    // when the producer throws — so a refused query leaves the view as it was. It used
    // to land as an empty answer instead, and the selection callbacks read "no rows" as
    // "nothing to select": the next load then opened on its first row, which is how a
    // commit or a file stopped being selected while nobody was at the keyboard. Git does
    // refuse these in ordinary use — `git diff` dies with "cannot hash" on a file whose
    // writer still holds it open, which is exactly when the working-tree watcher asks.
    //
    // Keeping the old value is half of it. The watcher event that raced the writer may
    // have been the last one, so a refused load asks again shortly, a bounded number of
    // times per change; the writer finishing usually gets there first.

    private readonly Dictionary<string, int> _loadRetries = new(PathComparer);
    private readonly HashSet<string> _retryPending = new(PathComparer);
    private const int MaxLoadRetries = 3;

    /// <summary>Runs a view's load off the UI thread. A refused query schedules a retry
    /// and still throws, so the loader keeps what it had.</summary>
    private T LoadOrRetry<T>(string path, Func<T> load)
    {
        try { return load(); }
        catch (GitCommandException)
        {
            HultUI.Application.InvokeOnMainThread(() => ScheduleLoadRetry(path));
            throw;
        }
    }

    private void ScheduleLoadRetry(string path)
    {
        // Several loaders share a generation; one retry covers them all.
        var attempt = _loadRetries.GetValueOrDefault(path);
        if (attempt >= MaxLoadRetries || !_retryPending.Add(path)) return;
        _loadRetries[path] = attempt + 1;
        _ = RetryLoadAfter(path, TimeSpan.FromMilliseconds(500 * Math.Pow(3, attempt)));   // 0.5 s, 1.5 s, 4.5 s
    }

    private async Task RetryLoadAfter(string path, TimeSpan delay)
    {
        await Task.Delay(delay);
        if (!_retryPending.Remove(path)) return;   // disposed meanwhile
        Invalidate(path);
        // Rerender, not MarkDirty — the same reason as OnExternalChange.
        Rerender();
    }


    // The active repo's loaders. Each repo has its own, so its last-loaded value
    // survives while another repo is on screen — switching back is a cache hit.
    private AsyncLoader<HistoryData> _history => _historyLoaders.For(_activeRepoPath);
    private AsyncLoader<Dictionary<string, (int Added, int Deleted)>> _historyStats =>
        _historyStatLoaders.For(_activeRepoPath);
    private AsyncLoader<ChangesData> _changes => _changesLoaders.For(_activeRepoPath);
    private AsyncLoader<TagsData> _tags => _tagsLoaders.For(_activeRepoPath);

    // ── Preloading ───────────────────────────────────────────────────────
    //
    // A view only ever asked for its data when it was the one building, so the tab
    // you were NOT on was cold after every repository switch: land on Branches,
    // switch repos, click History, wait a second. The loaders are per repository and
    // keyed on the data generation already, so warming them early costs nothing on
    // the next tab switch — it is the same Load call, made sooner.
    //
    // The request is queued rather than issued on the spot: it runs at the end of
    // the next Build, after the visible view has scheduled its own load, so the one
    // the user is looking at is first in the thread pool's queue.

    /// <summary>The repository whose views the next Build should warm, and whether the
    /// expensive ones (the numstat pass, the working-tree diffs) are included.</summary>
    private (string Path, bool Full)? _preload;

    private void RequestPreload(string path, bool full)
    {
        if (string.IsNullOrEmpty(path)) return;
        // A full request outranks a cheap one already waiting for the same repo.
        if (_preload is { } p && PathEquals(p.Path, path) && p.Full) return;
        _preload = (path, full);
    }

    /// <summary>Warms the given repository's views. Branches comes free with the summary
    /// the sidebar keeps warm; History's list, Tags and (when <paramref name="full"/>) the
    /// stats and Changes are the loads that used to wait for their tab.</summary>
    private void PreloadViews(string path, bool full)
    {
        if (string.IsNullOrEmpty(path) || !System.IO.Directory.Exists(path)) return;
        _ = SummaryFor(path);
        EnsureHistoryLoaded(path, stats: full);
        EnsureTagsLoaded(path);
        if (full) EnsureChangesLoaded(path);
    }

    private void RunPendingPreload()
    {
        if (_preload is not { } p) return;
        _preload = null;
        PreloadViews(p.Path, p.Full);
    }

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
        // Re-warm the cheap views of the repo on screen. Not the full set: the working
        // tree watcher fires on every editor save, and the numstat pass and the
        // working-tree diffs are the expensive loads — those wait for their tab.
        if (PathEquals(path, _activeRepoPath)) RequestPreload(path, full: false);
        // Rerender, not MarkDirty: bumping the generation changes every loader key
        // for this repo, so the next build activates NEW AsyncLoader instances. A
        // MarkDirty rebuild is untracked, so the Effect would never subscribe to
        // them and their async completions would go unobserved — the view sits on
        // its loading placeholder until some other tracked rebuild (e.g. switching
        // tabs) happens to re-subscribe. See RefreshAll, which already does this.
        Rerender();
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
        var remotes = repo.GetRemotesDetailed();
        return new RepoSummary(branch, info?.AheadCount ?? 0, info?.BehindCount ?? 0, dirty, version)
            { Branches = branches, Remotes = remotes };
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

    /// <summary>Keep the OS window title on the repository in front of you. Junction draws
    /// its own chrome, so this is not the caption — it is what the task bar and Alt+Tab show,
    /// and two Junction windows over two repos are otherwise indistinguishable there.</summary>
    private void SyncWindowTitle()
    {
        var repo = HasActiveRepo ? System.IO.Path.GetFileName(
            _activeRepoPath.TrimEnd(System.IO.Path.DirectorySeparatorChar,
                                    System.IO.Path.AltDirectorySeparatorChar)) : null;
        // A no-op when unchanged, here and in SDL, so once a build needs no invalidation path.
        _window.Title = string.IsNullOrEmpty(repo) ? "Junction" : $"{repo} — Junction";
    }

    public override Element Build()
    {
        SyncWindowTitle();

        // Track the revision signal so a Rerender() re-runs Build through the
        // Effect (re-subscribing to the active repo's loaders — see _revision).
        _ = _revision.Value;

        // Keep a filesystem watcher on every repo in the workspace so external
        // git activity (commits, pushes, staging, edits) invalidates the right
        // cache. Cheap: only diffs the desired vs. current watcher set.
        EnsureWatchers();

        // Key every child. The overlays below are added conditionally, so without
        // keys they reconcile by position — when one overlay disappears while another
        // appears at the same index, the framework would patch one subtree onto the
        // other, bleeding retained state (focus, caret, scroll) across kinds. Distinct
        // keys make each overlay reconcile only against its own kind.
        var titleBar = BuildTitleBar(); titleBar.Key = "titlebar";
        var body = BuildBody(); body.Key = "body";
        var root = Flex.Column(gap: 0)
            .FlexGrow(1)
            .Background(Nocturne.Bg)
            .TextColor(Nocturne.Text)
            .FontSize(13f)
            .Children(titleBar, body);

        // A scrollbar drag ends wherever the button comes up, which is routinely off the
        // thumb, so the move and release are heard at the root rather than on the track.
        _diffPan.AttachDrag(root);
        _commitSplit.AttachDrag(root);
        _changesSplit.AttachDrag(root);

        void Overlay(string key, Element el) { el.Key = key; root.AddChild(el); }

        if (_diffFull) Overlay("fullscreen-diff", BuildFullscreenDiff());
        if (_toast != null) Overlay("toast", BuildToast(_toast));
        if (_paletteOpen) Overlay("palette", BuildPalette());
        if (_worktreesOpen) Overlay("worktrees", BuildWorktreesPanel());
        if (_reflogOpen) Overlay("reflog", BuildReflogPanel());
        if (_submodulesOpen) Overlay("submodules", BuildSubmodulesPanel());
        if (_remotesOpen) Overlay("remotes", BuildRemotesPanel());
        if (_mrPanelOpen) Overlay("merge-requests", BuildMergeRequestsPanel());
        if (_optionsOpen) Overlay("options", BuildOptions());
        if (_dialog != null) Overlay("dialog", BuildDialog(_dialog));
        if (_createMr != null) Overlay("create-mr", BuildCreateMrDialog(_createMr));
        if (_rebase != null) Overlay("rebase", BuildRebaseDialog(_rebase));
        if (_popover != null) Overlay("popover", BuildPopover(_popover));
        if (_modal != null) Overlay("modal", BuildModal(_modal));

        // Last, so the visible view's own load was scheduled first (see _preload).
        RunPendingPreload();

        return root;
    }

    private Element BuildBody()
    {
        var body = Flex.Row(gap: 0).FlexGrow(1).AlignItems(AlignItems.Stretch);
        // Key the sidebar by collapsed-state so toggling replaces the subtree
        // instead of patching the expanded layout into the rail — the two shapes
        // share no meaningful retained state. Key the main column too so the two
        // body children reconcile by key, not position.
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
        // elements onto another's when switching views, bleeding retained state
        // (focus, scroll positions) across unrelated views.
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
            JunctionView.Tags => BuildTagsView(),
            JunctionView.Files => BuildFilesView(),
            _ => BuildHistoryView(),
        };
        view.Key = "view-" + View;
        col.AddChild(view);
        return col;
    }

    protected override void OnAfterBuild()
    {
        SettleDiffScrollbar();
        ServiceReviewAfterBuild();

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

        if (_pendingMrSearchFocus)
        {
            _pendingMrSearchFocus = false;
            _mrSearchRef.Current?.Focus();
        }

        if (_pendingContentSearchFocus)
        {
            _pendingContentSearchFocus = false;
            _contentSearchRef.Current?.Focus();
        }

        if (_pendingConflictEditFocus)
        {
            _pendingConflictEditFocus = false;
            _conflictEditRef.Current?.Focus();
        }

        if (_pendingBranchSearchFocus)
        {
            _pendingBranchSearchFocus = false;
            _branchSearchRef.Current?.Focus();
        }

        if (_pendingTagSearchFocus)
        {
            _pendingTagSearchFocus = false;
            _tagSearchRef.Current?.Focus();
        }

        if (_pendingFilesSearchFocus)
        {
            _pendingFilesSearchFocus = false;
            _filesSearchRef.Current?.Focus();
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

        ServiceFindBar(_diffFind, () => _diffBodyRef.Current, RevealDiffHit);
        ServiceFindBar(_filesFind, () => _fileBodyRef.Current, RevealFilesHit);

        if (_pendingFilesTreeScroll)
        {
            _pendingFilesTreeScroll = false;
            // Tree rows are a fixed height, so index math needs no per-row layout.
            _filesTreeScrollRef.Current?.ScrollRangeIntoView(
                FilesCursorIndex() * FileTreeRowHeight, FileTreeRowHeight);
        }

        if (_pendingScrollToSelected)
        {
            _pendingScrollToSelected = false;
            if (View == JunctionView.History && !_paletteOpen)
            {
                // Fixed row height, so index math needs no per-row layout.
                EnsureSelectedCommitVisible();
            }
            else if (_paletteOpen || View is JunctionView.Commit or JunctionView.Changes)
            {
                // OnAfterBuild runs before the layout pass, so the selected row's box
                // for this frame isn't available yet (and the file row, re-keyed on
                // selection, is a fresh element with no prior layout at all). Read it
                // in a post-frame callback, once the frame has been laid out.
                var changes = !_paletteOpen && View == JunctionView.Changes;
                var scrollRef = _paletteOpen ? _paletteScrollRef
                    : changes ? _changeScrollRef : _fileScrollRef;
                var rowRef = _paletteOpen ? _paletteSelRef
                    : changes ? _changeFocusRef : _fileSelRef;
                _window.RequestPostFrameCallback(() => ScrollRowIntoView(scrollRef, rowRef));
            }
        }
    }

    /// <summary>Scrolls a list so its keyboard-selected row is fully visible. Reads the
    /// actual laid-out row box (not a fixed row height), so it works with the
    /// variable-height palette and file rows. Call after the layout pass.</summary>
    private static void ScrollRowIntoView(Ref<ScrollViewElement> scrollRef, Ref<Element> rowRef)
    {
        if (scrollRef.Current is { } scroll && rowRef.Current is { } row)
            scroll.ScrollIntoView(row);
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

    /// <summary>Automation hook for scripted perf captures (JUNCTION_BENCH in
    /// Program.cs): a view name switches tabs, "row:N" selects the Nth branch row
    /// like a click on it would, and "tree"/"flat" picks the ref presentation so a
    /// capture can compare the two without editing settings.json. Main thread only.</summary>
    internal void BenchStep(string step)
    {
        if (step.StartsWith("row:", StringComparison.Ordinal))
        {
            var rows = BranchRows();
            if (rows.Count == 0) return;
            _branchSel = Math.Clamp(int.Parse(step[4..]), 0, rows.Count - 1);
            if (rows[_branchSel] is { IsFolder: false } leaf) BranchSelectSingle(leaf.Item);
            MarkDirty();
        }
        else if (step is "tree" or "flat")
        {
            SetRefTreeView(step == "tree");
        }
        else if (step.StartsWith("expand:", StringComparison.Ordinal))
        {
            // Opens every branch folder, so a capture can measure the worst case the
            // tree can reach: as many rows as the flat list, plus the folders.
            var set = RefExpandedSet(RefKindBranch);
            foreach (var root in Tree.Build(ActiveBranches, b => b.Name))
                foreach (var folder in Tree.Folders(root))
                    set[folder.Path] = step[7..] == "all";
            MarkDirty();
        }
        else if (Enum.TryParse<JunctionView>(step, out var v))
        {
            SetView(v);
        }
    }

    /// <summary>Collapses the commit selection to a single commit (or none).</summary>
    private void SetCommitSelection(string? hash)
    {
        _selectedCommitHash = hash;
        _selectionAnchorHash = hash;
        _selectedCommitHashes.Clear();
        if (hash != null) _selectedCommitHashes.Add(hash);
        EndBranchCompare();   // any collapse (History click, repo switch) ends a branch compare
        // Start on the commit's files and diff at the selection, not at the Commit tab.
        // History's build asks too, but the selection can land while another tab is
        // showing — a preloaded log picking its first commit — and this covers that.
        PrefetchCommitDetail(hash);
    }

    /// <summary>Clears the Branches-tab compare selection. Called when a History
    /// selection edit takes over so the two range sources never both drive the diff.</summary>
    private void EndBranchCompare()
    {
        _selectedBranches.Clear();
        _branchCompare = null;
    }

    /// <summary>True while a two-(or-more-)branch compare is selected in the Branches tab.</summary>
    private bool BranchCompareActive() => _branchCompare is not null;

    // Selection and focus are separate, like a Windows list: _selectedCommitHash
    // is the focus caret and may sit outside (or alongside an empty) selection
    // after Ctrl+arrows / Ctrl+Space / Ctrl+click toggles.
    private bool SelectionContains(string hash) => _selectedCommitHashes.Contains(hash);

    /// <summary>Base (oldest) and head (newest) of the multi-selection compare, or
    /// null when fewer than two selected commits exist in the loaded history.</summary>
    private (string BaseHash, string HeadHash)? SelectedRange()
    {
        // A merge request under review is compared between its own base and head — the
        // refs GitLab computed its diff from, so every position sent back matches.
        if (_review is { Ready: true } review)
            return (review.BaseSha, review.HeadSha);

        // A branch-tab compare resolves its two tips directly, so the diff works
        // even when a tip isn't in the loaded (filtered) history list.
        if (BranchCompareActive())
            return (_branchCompare!.Value.BaseHash, _branchCompare!.Value.HeadHash);

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
        _review = null;             // a review belongs to the repository it was opened in
        SetCommitSelection(null);   // reset selection; History reloads and picks first
        _settings.ActiveRepoPath = path;
        Persist();
        RequestPreload(path, full: true);
        // Hosted data is per repository too, and reloads on a switch only when it has
        // gone quiet for a while — flipping between two repos is not a poll.
        RefreshHostedIfStale(path, RefreshOnSwitchAfter);
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
            RequestPreload(_activeRepoPath, full: true);
        }
        Persist();
        Rerender();
    }

    // ── Keyboard navigation across repos / workspaces ────────────────────

    /// <summary>Step the active repo within the current workspace's ordered list.
    /// With no active repo, lands on the first (delta &gt; 0) or last entry.</summary>
    private void NavigateRepo(int delta)
    {
        // SidebarRepos, not the workspace's raw list: a repository's linked worktrees are
        // nested under it in the sidebar, and stepping through a list that included them
        // would stop on rows the eye reads as belonging to the row above. Standing on a
        // worktree, the step starts from the repository it hangs under.
        var repos = SidebarRepos;
        if (repos.Count == 0) return;
        var idx = IndexOfPath(repos, _activeRepoPath);
        if (idx < 0) idx = IndexOfPath(repos, SidebarRepoOf(_activeRepoPath));
        var next = idx < 0
            ? (delta > 0 ? 0 : repos.Count - 1)
            : Math.Clamp(idx + delta, 0, repos.Count - 1);
        SelectRepo(repos[next]);
    }

    private void SelectRepoByIndex(int index)
    {
        var repos = SidebarRepos;
        if (index < 0 || index >= repos.Count) return;
        SelectRepo(repos[index]);
    }

    /// <summary>The sidebar entry a path sits under — itself when it is one, otherwise the
    /// repository whose worktree it is. Empty when nothing in the sidebar claims it.</summary>
    private string SidebarRepoOf(string path)
    {
        if (string.IsNullOrEmpty(path)) return "";
        foreach (var group in SidebarRepoGroups())
            if (PathEquals(group.Path, path) || group.Worktrees.Any(w => PathEquals(w.Path, path)))
                return group.Path;
        return "";
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
        _settings.DiffWrap = _diffWrap;
        _settings.DiffSplit = _diffSplit;
        _settings.RefTreeView = _refTreeView;
        _settings.CommitMessageExpanded = _commitMsgExpanded;
        _settings.CommitFileListWidth = _commitSplit.Width;
        _settings.ChangesFileListWidth = _changesSplit.Width;
        JunctionSettings.Save(_settings);
    }

    // ── Toast ────────────────────────────────────────────────────────────

    /// <summary>The one verb an actionable toast offers, e.g. "Create merge request".</summary>
    private (string Label, Action Run)? _toastAction;

    /// <summary>A transient confirmation bottom-centre. With an <paramref name="action"/> it
    /// stays 4.5 s instead of 1.7 — it is asking for a decision rather than reporting one.</summary>
    private async void Flash(string message, (string Label, Action Run)? action = null)
    {
        _toastCts?.Cancel();
        _toast = message;
        _toastAction = action;
        _toastCts = new CancellationTokenSource();
        var token = _toastCts.Token;
        MarkDirty();
        try
        {
            await Task.Delay(action is null ? 1700 : 4500, token);
            _toast = null;
            _toastAction = null;
            MarkDirty();
        }
        catch (OperationCanceledException) { }
    }

    private void DismissToast()
    {
        _toastCts?.Cancel();
        _toast = null;
        _toastAction = null;
        MarkDirty();
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

        // The create-merge-request card: Esc cancels, Ctrl+Enter submits, typing flows to
        // its fields. Checked before the popover's own Esc so a picker closes first.
        if (_createMr != null && _popover == null)
        {
            if (e.Key == KeyCode.Escape) { CloseCreateMr(); e.StopPropagation = true; }
            else if (ctrl && e.Key == KeyCode.Enter) { SubmitCreateMr(); e.StopPropagation = true; }
            return;
        }

        // A modal dialog swallows every global shortcut; only Esc (cancel) acts,
        // and typing flows straight to the focused field.
        if (_dialog is { } dialog)
        {
            if (e.Key == KeyCode.Escape) { CloseDialog(); e.StopPropagation = true; }
            // A confirm-only prompt has no field to take the Enter (the field's own
            // handler is what confirms the others), so it is accepted here.
            else if (e.Key == KeyCode.Enter && dialog.Placeholder == null)
            { ConfirmDialog(); e.StopPropagation = true; }
            return;
        }

        // Options owns the keyboard while open — after the dialog, since a sign-out confirm
        // asks on top of it. Esc backs out one step: the add-host card first, then the
        // dialog. Typing flows to whichever field has focus.
        if (_optionsOpen)
        {
            if (e.Key == KeyCode.Escape) { OptionsEscape(); e.StopPropagation = true; }
            return;
        }

        // ⌘P / Ctrl+P toggles the palette from anywhere — the same key Vesper uses, so the
        // shortcut does not change meaning between the two apps. Ctrl+K, which Junction used
        // before, still works: it was the advertised key for ten releases.
        //
        // Both are caught here, ahead of the per-view handlers, and that is load-bearing rather
        // than tidy: those switch on the bare key and ignore modifiers, so Ctrl+P reaching the
        // Branches view would push the selected branch (KeyCode.P is Push there). Any new
        // Ctrl+<letter> shortcut has to be intercepted at this level for the same reason.
        if (ctrl && e.Key is KeyCode.P or KeyCode.K)
        {
            TogglePalette();
            e.StopPropagation = true;
            return;
        }

        // Ctrl+, opens Options — the key every editor uses for its settings. There is no
        // gear in the chrome by design; this and the palette are the two ways in.
        if (ctrl && e.Key == KeyCode.Comma)
        {
            OpenOptions();
            e.StopPropagation = true;
            return;
        }

        // ⌘+ / ⌘- / ⌘0 — the zoom keys every browser and editor has, driving the same
        // interface scale the Appearance overlay offers in steps. Handled here, above the
        // typing guard, because zooming is not something to lose because a field has focus.
        if (ctrl && e.Key is KeyCode.Equals or KeyCode.Minus or KeyCode.Digit0)
        {
            NudgeUiScale(e.Key switch
            {
                KeyCode.Equals => 1,
                KeyCode.Minus => -1,
                _ => 0,
            });
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

        // ⌘O / Ctrl+O opens the selected file itself with the desktop's own handler — the
        // other half of Ctrl+D, for when the answer is "edit this" rather than "compare it".
        // Intercepted here for the same reason as the two above: the per-view handlers switch
        // on the bare key, and KeyCode.O is Open in the Files tree.
        if (ctrl && e.Key == KeyCode.O && View is JunctionView.Commit or JunctionView.Changes)
        {
            OpenSelectedFile();
            e.StopPropagation = true;
            return;
        }

        if (_paletteOpen)
        {
            if (e.Key == KeyCode.Escape) { ClosePalette(); e.StopPropagation = true; }
            return;
        }

        // The worktrees panel is modal while open: Esc closes it, everything else
        // is swallowed (its own buttons/rows drive the actions).
        if (_worktreesOpen)
        {
            if (e.Key == KeyCode.Escape) { CloseWorktrees(); e.StopPropagation = true; }
            return;
        }

        if (_reflogOpen)
        {
            if (e.Key == KeyCode.Escape) { CloseReflog(); e.StopPropagation = true; }
            return;
        }

        if (_submodulesOpen)
        {
            if (e.Key == KeyCode.Escape) { CloseSubmodules(); e.StopPropagation = true; }
            return;
        }

        if (_remotesOpen)
        {
            if (e.Key == KeyCode.Escape) { CloseRemotes(); e.StopPropagation = true; }
            return;
        }

        // The merge-request panel has keys of its own (list navigation, checkout, open in
        // browser); it decides what it took. Typing in its search box flows to the field.
        if (_mrPanelOpen)
        {
            if (HandleMergeRequestsKey(e, typing)) e.StopPropagation = true;
            return;
        }

        // Find-in-text, before the typing guard and before Esc leaves fullscreen: the bar's
        // own keys have to reach it while its field has the keyboard, and Esc backs out of
        // the search before it backs out of the view.
        if (ActiveFindBar() is { } findBar && HandleFindBarKey(e, findBar))
        {
            e.StopPropagation = true;
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

        // How the diff is shown — unified/split, wrapped, whitespace, fullscreen — wherever
        // one is on screen: the docked pane in Commit and Changes, and the fullscreen
        // overlay. Ahead of the per-view keys below, so the diff answers for s / w / i / F11
        // whether or not its body holds the focus (the pane's own handler passes them up for
        // exactly this).
        if (HandleDiffViewKey(e)) { e.StopPropagation = true; return; }

        // m folds the commit message away. Behind the typing guard above, like f — an m
        // typed into a commit summary has to stay an m — and gated on the Commit tab
        // actually showing a message with a body, so the letter is free elsewhere.
        if (e.Key == KeyCode.M && CommitMessageHasBody)
        {
            ToggleCommitMessage();
            e.StopPropagation = true;
            return;
        }

        // f opens the find bar over whatever text is on screen — the bare-letter twin of the
        // Ctrl+F handled further up, which has to run before the typing guard so it works
        // from inside a field. This one is deliberately after it: an f typed into the commit
        // message is an f. ActiveFindBar answers null where there is no text to search, so
        // the letter stays free in the views that have none.
        if (e.Key == KeyCode.F && ActiveFindBar() is { } findTarget)
        {
            OpenFindBar(findTarget);
            e.StopPropagation = true;
            return;
        }

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
            KeyCode.Digit6 => JunctionView.Tags,
            KeyCode.Digit7 => JunctionView.Files,
            _ => (JunctionView?)null,
        };
        if (viewForKey is { } v) { SetView(v); e.StopPropagation = true; return; }

        // Commit view: arrows/page/home/end move the file selection — unless the
        // diff pane is focused, which consumes those keys to scroll (StopPropagation).
        // Ctrl/Cmd+↑/↓ steps to the previous/next commit without going back to
        // History — from the Commit view (which re-renders for it) and the Files
        // view (which re-roots its tree at the new commit). The list is
        // newest-first, so Up = newer, Down = older. Same path as the ▲/▼ buttons
        // in both headers, so keyboard and mouse stay in step.
        if (ctrl && e.Key is KeyCode.Up or KeyCode.Down
            && View is JunctionView.Commit or JunctionView.Files)
        {
            MoveCommitSelection(e.Key == KeyCode.Up ? -1 : 1);
            e.StopPropagation = true;
            return;
        }

        // Reviewing a merge request: the arrows are the caret's, c n p e act on the threads,
        // and Esc backs out one layer at a time — ahead of the file-tree keys below.
        if (HandleReviewKey(e))
        {
            e.StopPropagation = true;
            return;
        }

        if (View == JunctionView.Commit)
        {
            if (MoveFileByKey(e))
                e.StopPropagation = true;
            return;
        }

        // Files view: arrows/page/home/end + left/right + Enter navigate the file tree.
        // Only fires when the content body isn't focused — a focused body consumes these
        // keys to scroll and stops propagation before they reach this global handler.
        if (View == JunctionView.Files)
        {
            // The same key History, Branches and Tags use to reach their search box. Here it
            // finds a file in the browsed tree; Ctrl+F still finds text inside the open one.
            if (e.Key == KeyCode.Slash)
            {
                _pendingFilesSearchFocus = true;
                MarkDirty();
                e.StopPropagation = true;
                return;
            }
            if (HandleFilesTreeKey(e))
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

        if (View == JunctionView.Tags)
        {
            if (e.Key == KeyCode.Slash)
            {
                _pendingTagSearchFocus = true;
                MarkDirty();
                e.StopPropagation = true;
                return;
            }
            if (HandleTagKey(e))
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

        // The file list. Reached only when nothing is typing (the composer's fields sit
        // in this view) and the diff pane didn't consume the key to scroll itself.
        if (View == JunctionView.Changes)
        {
            if (HandleChangesNavKey(e))
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

    /// <summary>True when a key event should stay with a focused single-line search
    /// field instead of driving its results list. Home/End (± Shift) move the caret
    /// or select within the query, so they always belong to the field. Plain
    /// PageUp/PageDown carry no single-line text meaning and page the list, but with
    /// Shift held they're kept out of the list too (no accidental range-extend while
    /// typing). ↑/↓, plain PageUp/PageDown, and Enter fall through to the list.</summary>
    private static bool IsFieldEditKey(KeyDownEvent e) =>
        e.Key is KeyCode.Home or KeyCode.End
        || (e.Key is KeyCode.PageUp or KeyCode.PageDown && e.Modifiers.HasFlag(KeyModifiers.Shift));

    public void Dispose()
    {
        _window.Resized -= OnWindowResize;
        _toastCts?.Cancel();
        _toastCts?.Dispose();
        _autoFetchCts?.Cancel();
        _autoFetchCts?.Dispose();
        // AnimationManager's tween list is process-global, and a looping tween holds a
        // callback into this app: leaving one running would tick a disposed window's
        // MarkDirty for the rest of the process.
        _remoteOpTween.Cancel();
        foreach (var w in _watchers.Values) w.Dispose();
        _watchers.Clear();
        _retryPending.Clear();   // a pending retry finds nothing to do
    }
}
