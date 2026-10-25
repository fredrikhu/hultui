using HultUI.Core.Builders;
using HultUI.Core.Elements;
using HultUI.Core.Events;
using HultUI.Core.Layout;
using HultUI.Core.Primitives;
using HultUI.Core.Styling;
using HultUI.Core.Rendering;
using Junction.Git;
using Junction.Theme;

namespace Junction;

public partial class JunctionApp
{
    private const float LaneWidth = 18f;
    private const float DotRadius = 5.5f;
    private const float RowHeight = 54f;

    // Not a static readonly field: the ramp it reads changes with the theme, and a field would
    // hold whichever one was current when this type was first touched. Not a new array per
    // read either: the graph reads it for every edge and dot of every row on screen, every
    // frame (a new Color[6] per read was 1.97 KB a frame over a screen of History). Built once
    // per theme instead, which is all Nocturne's ramps follow.
    private static Color[] s_lanePalette = [];
    private static HultUI.Themes.NamedTheme? s_lanePaletteTheme;

    private static Color[] LanePalette
    {
        get
        {
            var theme = Nocturne.Theme;
            if (!ReferenceEquals(theme, s_lanePaletteTheme))
            {
                s_lanePalette =
                [
                    Nocturne.Accent400,
                    Nocturne.Neutral300,
                    Nocturne.Accent2_400,
                    Nocturne.Accent300,
                    Nocturne.Neutral400,
                    Nocturne.Accent500,
                ];
                s_lanePaletteTheme = theme;
            }
            return s_lanePalette;
        }
    }

    private Element BuildHistoryView()
    {
        if (!HasActiveRepo)
            return BuildStubView("History", "Open a repository to see its history.");

        EnsureHistoryLoaded(_activeRepoPath, stats: true);
        var data = _history.Value;
        var stats = _historyStats.Value;
        var filtered = FilteredCommits(data);

        // A reveal requested from another view settles here rather than at the click: the
        // switch into History is usually what starts this repo's log loading, so there is
        // nothing to look the hash up in at the moment the menu item runs.
        ResolvePendingReveal(filtered);

        // Keep the selection valid within the filtered view (e.g. after a search
        // narrows the list past the current selection).
        if (filtered.Count > 0 && (_selectedCommitHash == null || filtered.All(c => c.Hash != _selectedCommitHash)))
            SetCommitSelection(filtered[0].Hash);

        // Eagerly load the selected commit's (or multi-select compare's) detail so
        // opening the Commit view is instant. Any selection change rebuilds History,
        // so this covers keyboard, click and auto-select alike; a repeat with the
        // same key is a cache hit.
        if (SelectedRange() is { } range) PrefetchRangeDetail(range);
        else PrefetchCommitDetail(_selectedCommitHash);

        // The rows whose realization asks for the next page: the last tenth of a page, which
        // is a few screens — enough lead for the page to land before the scroll reaches the
        // end. Not while a search is on, because a search narrows the list until its tail is
        // on screen from the start, and that would load the whole history a page at a time
        // without anyone asking for it; "Search all history" is the ask.
        _historyTail.Clear();
        if (!data.Exhausted && _historyQuery.Trim().Length == 0)
            for (var i = Math.Max(0, filtered.Count - Math.Max(2, _historyPageCommits / 10)); i < filtered.Count; i++)
                _historyTail.Add(filtered[i].Hash);

        var col = Flex.Column(gap: 0).FlexGrow(1);
        // A GitLab-backed repository with no account gets one line offering to connect,
        // above everything else and dismissable for good — see JunctionApp.GitLabMarks.
        if (BuildFirstRunStrip() is { } firstRun)
            col.AddChild(firstRun);
        col.AddChild(BuildIncludeBar(
            IncludeSet(_activeRepoPath).OrderBy(x => x, StringComparer.Ordinal).ToList(), filtered.Count, data));
        if (_contentSearchOpen)
            col.AddChild(BuildContentSearchBar(filtered.Count));
        col.AddChild(BuildCommitList(data, stats, filtered));
        col.AddChild(BuildHistoryFooter(data));
        return col;
    }

    /// <summary>
    /// Starts (or reuses) the loads behind the History view for <paramref name="path"/>:
    /// the log with its graph, and — with <paramref name="stats"/> — the +/- counts that
    /// come from the far slower numstat pass. The view calls this as it builds; the
    /// preload calls it while another tab is showing, and the key is the same either way,
    /// so whichever comes second is a cache hit.
    /// </summary>
    private void EnsureHistoryLoaded(string path, bool stats)
    {
        var includeList = IncludeSet(path).OrderBy(x => x, StringComparer.Ordinal).ToList();
        // Snapshot the pickaxe query/mode so the off-thread load uses a stable value.
        var content = _contentApplied;
        var contentRegex = _contentRegex;
        var scope = $"{path}|{string.Join(",", includeList)}"
                  + (content.Length > 0 ? $"|{(contentRegex ? "G" : "S")}:{content}" : "");
        var key = $"{scope}|{DataGen(path)}";

        // How far back is remembered per scope, not per data generation: a commit or a fetch
        // reloads the log from the top, but at the depth that had been scrolled to, so the
        // selection five thousand commits down is still in the list afterwards. A different
        // include set or content search is a different log, and starts at one page again.
        var window = WindowFor(path);
        if (window.Scope != scope)
        {
            window.Scope = scope;
            window.Want = _historyPageCommits;
            window.LoadAll = false;
        }
        var depth = window.Want;

        var loader = _historyLoaders.For(path);
        // The next page is appended onto what is loaded — when that is the same log. Across a
        // generation the hashes may have moved, so the pages are fetched from the top again.
        var from = loader.Value.Key == key ? loader.Value : HistoryData.Empty;
        // The initial selection is active-repo state; a preload of some other repo (none
        // today, but the guard is cheap) must not pick a commit in this one.
        loader.Load($"{key}|{depth}",
            () => LoadOrRetry(path, () => LoadHistory(path, includeList, content, contentRegex, key, from, depth)),
            onLoaded: data =>
            {
                if (PathEquals(path, _activeRepoPath)) PickInitialCommit(data);
                OnHistoryPageLoaded(path, data);
                // The counts need the hashes, so a preload — which asks while the log is
                // still empty and is not asked again — starts them from the landing.
                if (stats) EnsureHistoryStatsLoaded(path, data);
            });
        if (stats) EnsureHistoryStatsLoaded(path, loader.Value);
    }

    // ── Paging the log ───────────────────────────────────────────────────
    //
    // The log used to stop at 500 commits, and a repository's history does not. A page at a
    // time is the shape that stays cheap: git itself is fast — walking fifty thousand commits
    // takes a quarter of a second, and a page of a thousand a few milliseconds over the
    // process start — but the +/- counts beside each row come from a numstat pass that costs
    // a few milliseconds per commit and would take minutes over the whole of a large
    // repository. So the list grows a page at a time as it is scrolled, and the counts follow
    // each page in chunks, cached by hash so a reload after a commit computes only what is new.

    /// <summary>How many commits a page of the log holds. A field rather than a constant so
    /// a test can shrink it to a handful and page through a small repository — through the
    /// constructor, since the first page loads there.</summary>
    private readonly int _historyPageCommits;
    private const int DefaultHistoryPageCommits = 1000;

    /// <summary>How many commits' counts one numstat pass computes. Small enough that the
    /// first rows of a page show their counts while the rest are still being computed.</summary>
    private const int HistoryStatsChunk = 250;

    /// <summary>How much further the log is asked to reach, per repository.</summary>
    private sealed class HistoryWindow
    {
        public string Scope = "";
        public int Want;
        /// <summary>Keep asking for the next page as each lands, until the log runs out —
        /// "Load all", or a reveal of a commit that is not in the pages loaded so far.</summary>
        public bool LoadAll;
    }

    private readonly Dictionary<string, HistoryWindow> _historyWindows = new(PathComparer);

    private HistoryWindow WindowFor(string path)
    {
        if (!_historyWindows.TryGetValue(path, out var w)) _historyWindows[path] = w = new HistoryWindow();
        return w;
    }

    /// <summary>The hashes near the loaded end of the list on screen. A row for one of these
    /// being realized is what asks for the next page — that is the list being scrolled to
    /// within a few screens of its end, whatever did the scrolling.</summary>
    private HashSet<string> _historyTail = [];
    private bool _historyMoreQueued;

    /// <summary>Asks for the next page of the active repository's log, or — with
    /// <paramref name="all"/> — for every page after it, one landing after the other.</summary>
    private void RequestMoreHistory(bool all)
    {
        if (!HasActiveRepo) return;
        var data = _history.Value;
        var window = WindowFor(_activeRepoPath);
        if (all) window.LoadAll = true;
        if (data.Exhausted || _history.IsLoading) return;
        window.Want = NextHistoryDepth(data.Commits.Count);
        MarkDirty();   // the loader key now differs → the next page loads
    }

    private void StopLoadingAllHistory()
    {
        if (!HasActiveRepo) return;
        WindowFor(_activeRepoPath).LoadAll = false;
        MarkDirty();
    }

    /// <summary>
    /// The depth to ask for after <paramref name="loaded"/> commits. Scrolling adds a page;
    /// "Load all" doubles what there is, up to twenty pages at a time, because git re-walks
    /// the commits it skips and a hundred pages of a thousand would walk the first ones a
    /// hundred times over. Doubling keeps the total walk within twice the log's length.
    /// </summary>
    private int NextHistoryDepth(int loaded)
    {
        var page = _historyPageCommits;
        if (HasActiveRepo && WindowFor(_activeRepoPath).LoadAll)
            page = Math.Clamp(loaded, page, 20 * page);
        return loaded + page;
    }

    private void OnHistoryPageLoaded(string path, HistoryData data)
    {
        var window = WindowFor(path);
        if (!window.LoadAll) return;
        // A reveal is what set LoadAll when its commit was not in the pages loaded; found, the
        // loading stops there rather than fetching the rest of the history behind it.
        if (_pendingRevealHash is { } hash && data.Commits.Any(c => c.Hash == hash))
            window.LoadAll = false;
        if (data.Exhausted) { window.LoadAll = false; return; }
        window.Want = NextHistoryDepth(data.Commits.Count);
        MarkDirty();
    }

    /// <summary>
    /// Starts the numstat pass for the commits on screen that have no counts yet — one chunk
    /// per call, the next asked for by the rebuild the landing triggers. The counts are kept
    /// by hash for as long as the repository's loader lives, so a reload after a fetch or a
    /// commit finds nearly all of them already there.
    /// </summary>
    private void EnsureHistoryStatsLoaded(string path, HistoryData data)
    {
        if (data.Commits.Count == 0) return;
        var stats = _historyStatLoaders.For(path);
        var known = stats.Value;
        var missing = new List<string>(HistoryStatsChunk);
        int missingCount = 0;
        foreach (var c in data.Commits)
        {
            if (known.ContainsKey(c.Hash)) continue;
            missingCount++;
            if (missing.Count < HistoryStatsChunk) missing.Add(c.Hash);
        }
        if (missingCount == 0) return;
        // The key is what is still owed under this log: each chunk landing lowers it, which is
        // what makes the next call a new load rather than a cache hit.
        stats.Load($"{data.Key}|{data.Depth}|{missingCount}",
            () =>
            {
                var fresh = RepoFor(path).GetStatsFor(missing);
                var merged = new Dictionary<string, (int Added, int Deleted)>(known);
                foreach (var (hash, stat) in fresh) merged[hash] = stat;
                return merged;
            },
            // More chunks owed: a rebuild asks for the next. Only the tracked reads in a build
            // would otherwise, and a preload's are not.
            onLoaded: _ => { if (missingCount > missing.Count) MarkDirty(); });
    }

    private List<GitCommit> FilteredCommits(HistoryData data)
    {
        var q = _historyQuery.Trim();
        if (q.Length == 0) return data.Commits.ToList();
        return data.Commits.Where(c =>
            c.Subject.Contains(q, StringComparison.OrdinalIgnoreCase) ||
            c.AuthorName.Contains(q, StringComparison.OrdinalIgnoreCase) ||
            c.ShortHash.Contains(q, StringComparison.OrdinalIgnoreCase) ||
            c.Hash.Contains(q, StringComparison.OrdinalIgnoreCase)).ToList();
    }

    // Runs off the UI thread.
    private HistoryData LoadHistory(string path, List<string> includeList,
        string? content, bool contentRegex, string key, HistoryData from, int depth)
    {
        var repo = RepoFor(path);
        var refs = includeList.Count > 0 ? includeList : null;

        // Only the page after what is loaded, when there is something to append to; a log
        // asked for from nothing, or shallower than what there is, is fetched from the top.
        List<GitCommit> commits;
        if (from.Commits.Count > 0 && from.Commits.Count < depth && !from.Exhausted)
        {
            var more = repo.GetLog(maxCount: depth - from.Commits.Count, skip: from.Commits.Count,
                includeRefs: refs, pickaxe: content, pickaxeRegex: contentRegex);
            commits = new List<GitCommit>(from.Commits.Count + more.Count);
            commits.AddRange(from.Commits);
            commits.AddRange(more);
        }
        else if (from.Commits.Count > 0 && from.Exhausted)
        {
            commits = from.Commits.ToList();
        }
        else
        {
            commits = repo.GetLog(maxCount: depth, includeRefs: refs, pickaxe: content, pickaxeRegex: contentRegex);
        }
        var exhausted = commits.Count < depth;

        // With an explicit include set, parents can reference commits outside the
        // reachable window — strip those so the graph doesn't open phantom lanes.
        var graphInput = commits;
        if (refs != null)
        {
            var present = new HashSet<string>(commits.Select(c => c.Hash));
            graphInput = commits.Select(c => c with
            {
                ParentHashes = c.ParentHashes.Where(present.Contains).ToArray()
            }).ToList();
        }

        var graph = GitGraphLayout.Compute(graphInput);
        var remotes = new HashSet<string>(repo.GetRemotes(), StringComparer.Ordinal);
        return new HistoryData(commits, graph, remotes, key, depth, exhausted);
    }


    /// <summary>The log as it was before the one on screen — what a selected hash that the
    /// new log no longer has can be looked up in, to follow it to its rewritten copy.</summary>
    private HistoryData _previousHistory = HistoryData.Empty;

    private void PickInitialCommit(HistoryData data)
    {
        var previous = _previousHistory;
        _previousHistory = data;
        if (data.Commits.Count == 0) { SetCommitSelection(null); return; }
        if (_selectedCommitHash != null && data.Commits.Any(c => c.Hash == _selectedCommitHash)) return;
        if (_selectedCommitHash != null && FollowRewrite(previous, data)) return;
        SetCommitSelection(data.Commits[0].Hash);
    }

    // ── Following a rewrite ──────────────────────────────────────────────
    //
    // A rebase, amend or cherry-pick gives a commit a new hash, and the selection is a
    // hash. Rather than fall back to the top of the list, the selection follows the
    // commit: a rewrite keeps the author and the author timestamp to the second, so a
    // commit that just appeared with the same author, second and subject is the same
    // commit; when two appeared in the same second, the one whose change has the same
    // patch id is. Nothing matches a dropped or squashed-away commit, and the top of the
    // list is still where the selection lands then.

    /// <summary>Moves the selection — lead, members and Shift anchor — to the rewritten
    /// copies of the commits it named. False when the lead commit has no copy.</summary>
    private bool FollowRewrite(HistoryData previous, HistoryData data)
    {
        if (previous.Commits.Count == 0) return false;
        var before = new Dictionary<string, GitCommit>(StringComparer.Ordinal);
        foreach (var c in previous.Commits) before.TryAdd(c.Hash, c);
        var present = new HashSet<string>(data.Commits.Select(c => c.Hash), StringComparer.Ordinal);
        var appeared = data.Commits.Where(c => !before.ContainsKey(c.Hash)).ToList();
        if (appeared.Count == 0) return false;

        string? Follow(string? hash)
        {
            if (hash == null) return null;
            if (present.Contains(hash)) return hash;
            return before.TryGetValue(hash, out var was) ? RewrittenAs(was, appeared) : null;
        }

        var lead = Follow(_selectedCommitHash);
        if (lead == null) return false;
        var members = _selectedCommitHashes.Select(Follow).OfType<string>().ToList();
        _selectedCommitHashes.Clear();
        _selectedCommitHashes.UnionWith(members);
        _selectedCommitHashes.Add(lead);
        _selectionAnchorHash = Follow(_selectionAnchorHash) ?? lead;
        _selectedCommitHash = lead;
        PrefetchCommitDetail(lead);
        return true;
    }

    /// <summary>The commit among <paramref name="appeared"/> that <paramref name="was"/>
    /// became, or null when none — or more than one — can be it.</summary>
    private string? RewrittenAs(GitCommit was, List<GitCommit> appeared)
    {
        var stamped = appeared.Where(c =>
            c.AuthorEmail == was.AuthorEmail && c.AuthorDate == was.AuthorDate).ToList();
        if (stamped.Count == 0) return null;
        if (stamped.Count == 1) return stamped[0].Hash;

        // Same second twice — scripted commits do that. The subject usually settles it
        // (unless the rewrite was a reword, which is why it is not the first test).
        var titled = stamped.Where(c => c.Subject == was.Subject).ToList();
        if (titled.Count == 1) return titled[0].Hash;
        if (titled.Count > 1) stamped = titled;

        // Still tied: by content. The original still exists after a rebase (the reflog
        // keeps it), so its patch id can be compared with each candidate's. A few short git
        // calls on the UI thread, on a path this rare.
        if (ActiveRepo is not { } repo) return null;
        if (repo.GetPatchId(was.Hash) is not { } id) return null;
        var same = stamped.Where(c => repo.GetPatchId(c.Hash) == id).ToList();
        return same.Count == 1 ? same[0].Hash : null;
    }

    private void MoveCommitSelection(int delta, bool extend = false, bool focusOnly = false, bool additive = false)
    {
        EndBranchCompare();   // a History selection edit takes over the compare range
        var commits = FilteredCommits(_history.Value);
        if (commits.Count == 0) return;
        var i = _selectedCommitHash == null ? 0 : IndexOfHash(commits, _selectedCommitHash);
        if (i < 0) i = 0;
        i = Math.Clamp(i + delta, 0, commits.Count - 1);
        if (extend)
        {
            // Shift+arrows: selection becomes the contiguous anchor→focus range
            // (Ctrl+Shift keeps the rest of the selection and adds the range).
            var anchor = _selectionAnchorHash ?? _selectedCommitHash ?? commits[i].Hash;
            if (!additive) _selectedCommitHashes.Clear();
            AddRangeToSelection(commits, anchor, commits[i].Hash);
            _selectedCommitHash = commits[i].Hash;
            _selectionAnchorHash = anchor;
        }
        else if (focusOnly)
        {
            // Ctrl+arrows: move the focus caret only; selection and anchor stay.
            _selectedCommitHash = commits[i].Hash;
        }
        else
        {
            SetCommitSelection(commits[i].Hash);
        }
        _pendingScrollToSelected = true;
        // A tracked rebuild, not MarkDirty: changing the selected commit changes the Files
        // view's per-commit tree/content loader KEYS, so the next build activates new
        // AsyncLoader instances — an untracked rebuild would leave them unsubscribed
        // and the browser stuck on "Loading…" (see OnExternalChange). The view's alone: a
        // selection is nothing the chrome shows.
        RerenderView();
    }

    /// <summary>Ctrl+Space: toggles the focused commit in/out of the selection
    /// (may empty it entirely, like a Windows list).</summary>
    private void ToggleFocusedCommitSelection()
    {
        EndBranchCompare();
        if (_selectedCommitHash is not { } hash) return;
        if (!_selectedCommitHashes.Add(hash)) _selectedCommitHashes.Remove(hash);
        _selectionAnchorHash = hash;
        MarkDirty();
    }

    /// <summary>Adds the contiguous run of commits between two hashes (inclusive,
    /// either order) to the selection set.</summary>
    private void AddRangeToSelection(List<GitCommit> commits, string aHash, string bHash)
    {
        var i = IndexOfHash(commits, aHash);
        var j = IndexOfHash(commits, bHash);
        if (i < 0 || j < 0) { _selectedCommitHashes.Add(bHash); return; }
        if (i > j) (i, j) = (j, i);
        for (var k = i; k <= j; k++)
            _selectedCommitHashes.Add(commits[k].Hash);
    }

    /// <summary>Commit-row click with Windows list semantics: plain click selects
    /// (and opens details when already the single selection), Ctrl toggles,
    /// Shift selects the anchor→clicked range, Ctrl+Shift adds that range.</summary>
    private void OnCommitRowClick(GitCommit commit, KeyModifiers mods)
    {
        EndBranchCompare();
        var ctrl = mods.HasFlag(KeyModifiers.Ctrl);
        var shift = mods.HasFlag(KeyModifiers.Shift);

        if (shift)
        {
            var commits = FilteredCommits(_history.Value);
            var anchor = _selectionAnchorHash ?? _selectedCommitHash ?? commit.Hash;
            if (!ctrl) _selectedCommitHashes.Clear();
            AddRangeToSelection(commits, anchor, commit.Hash);
            _selectedCommitHash = commit.Hash;
            _selectionAnchorHash = anchor;
            MarkDirty();
            return;
        }
        if (ctrl)
        {
            // Toggle; focus follows the click either way (selection may go empty).
            if (!_selectedCommitHashes.Add(commit.Hash)) _selectedCommitHashes.Remove(commit.Hash);
            _selectedCommitHash = commit.Hash;
            _selectionAnchorHash = commit.Hash;
            MarkDirty();
            return;
        }

        // Re-clicking the commit that is already the single selection opens it;
        // any other plain click collapses the selection to the clicked commit.
        if (commit.Hash == _selectedCommitHash
            && _selectedCommitHashes.Count == 1 && _selectedCommitHashes.Contains(commit.Hash))
        {
            SetView(JunctionView.Commit);
        }
        else
        {
            SetCommitSelection(commit.Hash);
            MarkDirty();
        }
    }

    /// <summary>
    /// Asks History to select a commit and scroll to it. Used by the blame gutter, where
    /// "which commit wrote this line" and "where does it sit in the history" are two
    /// different questions — the first is the Commit view, this is the second.
    /// </summary>
    private void RevealCommitInHistory(string hash)
    {
        if (!IsBlamableCommit(hash)) return;

        SetCommitSelection(hash);
        PrefetchCommitDetail(hash);   // the reader is one keystroke from opening it anyway

        // The History search box filters the loaded log client-side, so a query left in it
        // can hide the very row being revealed. Clear it: a reveal that lands on nothing
        // reads as a broken menu item, and retyping a search is cheap.
        _historyQuery = "";

        _pendingRevealHash = hash;
        SetView(JunctionView.History);
        Rerender();   // the switch activates this repo's history loader
    }

    /// <summary>
    /// Settles a pending reveal once the log is actually loaded: scrolls to the commit if
    /// the list holds it, and says why if it does not.
    ///
    /// <para>The include-branches set and the content (pickaxe) search both narrow the log
    /// <i>server-side</i>, so a commit that certainly exists in the repository can equally
    /// certainly be absent from the list on screen. Doing nothing in that case would be
    /// indistinguishable from a dead menu item.</para>
    /// </summary>
    private void ResolvePendingReveal(List<GitCommit> filtered)
    {
        if (_pendingRevealHash is not { } hash || _history.IsLoading) return;

        // Not in the pages loaded, with older ones still to come: the commit may well be
        // further back — blame reaches any commit in the file's past — so the log keeps
        // loading until it holds the hash or runs out, and the reveal stays owed meanwhile.
        var data = _history.Value;
        if (IndexOfHash(filtered, hash) < 0 && !data.Exhausted && data.Commits.Count > 0
            && !data.Commits.Any(c => c.Hash == hash))
        {
            // This runs inside Build, so the request — which marks the tree dirty — waits
            // for the frame rather than asking for a rebuild from within one.
            if (!_historyMoreQueued)
            {
                _historyMoreQueued = true;
                _window.RequestPostFrameCallback(() =>
                {
                    _historyMoreQueued = false;
                    if (_pendingRevealHash != null) RequestMoreHistory(all: true);
                });
            }
            return;
        }
        _pendingRevealHash = null;

        if (IndexOfHash(filtered, hash) >= 0)
        {
            // The click selected the commit before the log held it, and each build meanwhile
            // moved a selection it could not find back to the top; now that it is here, it is
            // the selection again.
            if (_selectedCommitHash != hash) SetCommitSelection(hash);
            // Not _pendingScrollToSelected: that is consumed in OnAfterBuild, which runs
            // *before* the layout pass. The other setters get away with it because they
            // scroll a list that earlier frames already laid out, but a reveal arrives with
            // History being built for the first time — the list's viewport and content
            // heights are both still 0, so ScrollRangeIntoView clamps the offset to 0 and
            // the commit is selected but left off screen. Scroll once the frame is laid out.
            _window.RequestPostFrameCallback(EnsureSelectedCommitVisible);
            return;
        }

        var why = _contentApplied.Length > 0
            ? "the Included branches filter or the content search"
            : "the Included branches filter";
        Flash($"{hash[..Math.Min(7, hash.Length)]} isn't in the history shown here — check {why}");
    }

    /// <summary>Scrolls the commit list so the keyboard-selected row is on screen
    /// (the list isn't focused, so it won't auto-scroll on its own).</summary>
    private void EnsureSelectedCommitVisible()
    {
        var list = _commitListRef.Current;
        if (list == null || _selectedCommitHash == null) return;

        var idx = IndexOfHash(FilteredCommits(_history.Value), _selectedCommitHash);
        if (idx < 0) return;

        // Virtualised list with a fixed row height: the selected row may not be
        // materialised, so locate its band by index rather than element bounds.
        list.ScrollRangeIntoView(idx * RowHeight, RowHeight);
    }

    // ── Include-in-history bar ───────────────────────────────────────────

    /// <remarks>Every child here carries a key, and that is not decoration. Two of them
    /// already needed one — the branch-filter and pickaxe buttons, whose lit and unlit
    /// states have to be replaced rather than patched — and a child with a key makes the
    /// reconciler match this bar's children <i>only</i> by key, so every unkeyed sibling is
    /// a new element on every build. The search box is one of those siblings: rebuilt from
    /// scratch, it dropped the keyboard on each keystroke, which is what the report "the
    /// filter text box loses focus for each keystroke" was. Key all of them or none.</remarks>
    private Element BuildIncludeBar(List<string> includeList, int commitCount, HistoryData data)
    {
        var bar = Flex.Row(gap: 10)
            .FlexShrink(0).AlignItems(AlignItems.Center)
            .Padding(new Thickness(9, 16, 9, 16))
            .BorderColor(Nocturne.Divider).BorderWidth(new Thickness(0, 0, 1, 0));

        var label = Flex.Row(gap: 6).AlignItems(AlignItems.Center).Children(
            Ui.Icon(Icons.Funnel, 13f, Nocturne.On(0.55)),
            Text.New("Included").FontSize(11f).TextColor(Nocturne.On(0.55)));
        label.Key = "include-label";
        bar.AddChild(label);

        var current = HasActiveRepo ? SummaryFor(_activeRepoPath).Branch : "";
        var onlyCurrent = includeList is [var single] && single == current;
        if (CurrentBranchFilterButton(current, onlyCurrent) is { } button)
            bar.AddChild(button);

        if (includeList.Count == 0)
        {
            var all = Ui.TagOutline("All branches", Nocturne.Accent, 10f);
            all.Key = "include-scope";
            bar.AddChild(all);
        }
        else if (!onlyCurrent)
        {
            var chips = Flex.Row(gap: 6).AlignItems(AlignItems.Center);
            foreach (var name in includeList)
            {
                var pb = BranchName.Parse(name);
                chips.AddChild(Ui.Tag(pb.Title, pb.ChipBg, pb.ChipFg, 10f));
            }
            chips.AddChild(Ui.GhostButton("Clear", Icons.X, ClearInclude, 11f));
            // Same key as the "All branches" tag it stands in for: the two are alternatives
            // for one slot, and they are different element types, so the reconciler swaps
            // rather than patches either way.
            chips.Key = "include-scope";
            bar.AddChild(chips);
        }

        var spacer = Box.New().FlexGrow(1);
        spacer.Key = "include-spacer";
        bar.AddChild(spacer);

        var search = BuildHistorySearch();
        search.Key = "history-search";
        bar.AddChild(search);

        bar.AddChild(ContentSearchToggle());

        var count = Text.New(HistoryCountLabel(commitCount, data)).FontSize(11f).TextColor(Nocturne.On(0.40));
        count.Key = "include-count";
        bar.AddChild(count);
        return bar;
    }

    /// <summary>
    /// "786 commits" once the whole log is in; "newest 1,000" while there is more behind it,
    /// so a count never reads as the size of a history it has only the top of. With a search
    /// on, how many of those match.
    /// </summary>
    private string HistoryCountLabel(int shown, HistoryData data)
    {
        var loaded = data.Commits.Count;
        var all = data.Exhausted ? $"{loaded:N0} commits" : $"newest {loaded:N0}";
        if (_historyQuery.Trim().Length == 0) return data.Exhausted ? all : $"{all} commits";
        return $"{shown:N0} of {all}";
    }

    /// <summary>
    /// One click for the filter people actually want: just the branch that is checked out.
    /// Setting it a branch at a time from the Branches tab's eye toggles is the general
    /// case, and the general case is three tab switches away from the common one.
    ///
    /// <para>It is a toggle — pressed again the log goes back to all branches — and it is
    /// lit while the include set is exactly this branch, at which point it stands in for
    /// the chip and the "Clear" beside it: one control saying one thing. Null on a detached
    /// HEAD, which names no branch to filter on, and while the summary is still loading.</para>
    /// </summary>
    private Element? CurrentBranchFilterButton(string branch, bool active)
    {
        if (branch.Length == 0 || branch == "HEAD") return null;

        var fg = active ? Nocturne.Accent : Nocturne.On(0.6);
        var title = BranchName.Parse(branch).Title;

        var b = Flex.Row(gap: 5).FlexShrink(0).AlignItems(AlignItems.Center)
            .Padding(new Thickness(4, 9, 4, 9))
            .CornerRadius(Nocturne.RadiusMd)
            .BorderColor(active ? Nocturne.Accent : Nocturne.Divider).BorderWidth(1f)
            .Cursor(CursorType.Pointer)
            .Tooltip(active
                ? $"Showing only {branch} — click for every branch again"
                : $"Show only the commits on {branch}");
        if (active) b.Background(Nocturne.Alpha(Nocturne.Accent, 0.12));
        b.AddChild(Ui.Icon(Icons.GitBranch, 12f, fg));
        if (!_compactChrome)
            b.AddChild(Text.New($"Only {title}").FontSize(11f).FontWeight(FontWeight.Medium)
                .TextColor(fg).OverflowMode(TextOverflow.Ellipsis).MaxLines(1));
        // Lit and unlit differ in fill and border, so the flip has to replace the element
        // rather than patch over the other state's background; the branch is in the key
        // because checking another one out changes what this button means.
        b.Key = "onlycurrent:" + branch + (active ? "#a" : "");
        b.OnClick(e => { e.StopPropagation = true; ToggleCurrentBranchOnly(); });
        if (!active) b.HoverBg(Nocturne.Alpha(Nocturne.Text, 0.07), Color.Transparent);
        return b;
    }

    /// <summary>Narrows the log to the checked-out branch, or widens it back to every
    /// branch when it is already the only one included. Behind both the include bar's
    /// button and the palette's "History: only the current branch".</summary>
    private void ToggleCurrentBranchOnly()
    {
        if (!HasActiveRepo) return;
        var branch = SummaryFor(_activeRepoPath).Branch;
        if (branch.Length == 0 || branch == "HEAD") { Flash("HEAD is detached — no branch to filter on"); return; }

        var set = IncludeSet(_activeRepoPath);
        var only = set.Count == 1 && set.Contains(branch);
        set.Clear();
        if (!only) set.Add(branch);
        BumpDataGen(_activeRepoPath);   // the log reloads with the new include set
        MarkDirty();
    }

    // Toggles the content-search (pickaxe) bar. Lit while a query is applied so the
    // active filter is visible even with the bar collapsed.
    private Element ContentSearchToggle()
    {
        var active = _contentApplied.Length > 0;
        var b = Flex.Row(gap: 0).Width(34).Height(34).FlexShrink(0)
            .AlignItems(AlignItems.Center).JustifyContent(JustifyContent.Center)
            .CornerRadius(Nocturne.RadiusMd)
            .BorderColor(active ? Nocturne.Accent : Nocturne.Divider).BorderWidth(1f)
            .Cursor(CursorType.Pointer).Tooltip("Search file contents (pickaxe)");
        if (active) b.Background(Nocturne.Alpha(Nocturne.Accent, 0.12));
        b.AddChild(Ui.Icon(Icons.FileMagnifier, 15f, active ? Nocturne.Accent300 : Nocturne.On(0.6)));
        b.Key = "content-toggle" + (active ? "#a" : "") + (_contentSearchOpen ? "#o" : "");
        b.OnClick(_ => ToggleContentSearch());
        if (!active) b.HoverBg(Nocturne.Alpha(Nocturne.Text, 0.07), Color.Transparent);
        return b;
    }

    // ── Content search (pickaxe) bar ─────────────────────────────────────

    private Element BuildContentSearchBar(int matchCount)
    {
        var bar = Flex.Row(gap: 8).FlexShrink(0).AlignItems(AlignItems.Center)
            .Padding(new Thickness(8, 16, 8, 16))
            .Background(Nocturne.Alpha(Nocturne.Accent, 0.05))
            .BorderColor(Nocturne.Divider).BorderWidth(new Thickness(0, 0, 1, 0));

        bar.AddChild(Ui.Icon(Icons.FileMagnifier, 13f, Nocturne.Accent300).FlexShrink(0));

        // -S / -G mode segment.
        var seg = Flex.Row(gap: 2).FlexShrink(0).Padding(new Thickness(2))
            .Background(Nocturne.Surface).BorderColor(Nocturne.Divider).BorderWidth(1f)
            .CornerRadius(Nocturne.RadiusRow);
        seg.AddChild(ContentModeButton("Text", regex: false));
        seg.AddChild(ContentModeButton("Regex", regex: true));
        bar.AddChild(seg);

        // Input.
        var input = TextInput.New(_contentQuery).Style(Ui.Field());
        input.PlaceholderText(_contentRegex
                ? "Find commits whose diff matches this regex… (Enter)"
                : "Find commits that added or removed this text… (Enter)")
            .OnTextChanged(q => { _contentQuery = q; MarkViewDirty(); })
            .Height(Dimension.Px(32)).FlexGrow(1).MaxWidth(460)
            .TextColor(Nocturne.Text).WithRef(_contentSearchRef);
        input.On<KeyDownEvent>(ContentSearchKey);
        bar.AddChild(input);

        bar.AddChild(Ui.PrimaryButton("Search", Icons.MagnifyingGlass, ApplyContentSearch, 11.5f).FlexShrink(0));

        if (_contentApplied.Length > 0)
        {
            bar.AddChild(Text.New($"{matchCount} match{(matchCount == 1 ? "" : "es")}")
                .FontSize(11f).TextColor(Nocturne.On(0.5)).FlexShrink(0));
            bar.AddChild(Ui.GhostButton("Clear", Icons.X, ClearContentSearch, 11f).FlexShrink(0));
        }
        return bar;
    }

    private Element ContentModeButton(string label, bool regex)
    {
        var on = _contentRegex == regex;
        var fg = on ? Nocturne.Accent : Nocturne.On(0.55);
        var b = Flex.Row(gap: 0).FlexShrink(0).AlignItems(AlignItems.Center)
            .Padding(new Thickness(4, 9, 4, 9)).CornerRadius(5f).Cursor(CursorType.Pointer)
            .Tooltip(regex ? "-G: diff matches a regex" : "-S: literal string occurrences changed");
        // An outline, not a border: these size to their content, so a border on the active
        // one would make it 2px wider and slide every button beside it. Layout never reads
        // an outline.
        if (on) b.Background(Nocturne.Bg).Outline(1f, Nocturne.Accent);
        b.AddChild(Text.New(label).FontSize(11f).FontWeight(FontWeight.Medium).TextColor(fg));
        b.Key = "cmode:" + label + (on ? "#on" : "");
        b.OnClick(e => { e.StopPropagation = true; SetContentMode(regex); });
        return b;
    }

    private Element BuildHistorySearch()
    {
        var box = Flex.Row(gap: 6).AlignItems(AlignItems.Center)
            .Height(34).MaxWidth(300).FlexShrink(1)
            .Padding(new Thickness(0, 10, 0, 10))
            .Background(Nocturne.Bg)
            .BorderColor(Nocturne.Divider).BorderWidth(1f)
            .CornerRadius(Nocturne.RadiusMd);
        box.AddChild(Ui.Icon(Icons.MagnifyingGlass, 12f, Nocturne.On(0.45)).FlexShrink(0));

        // Borderless field (theme.TextInput); this wrapper box supplies the border.
        // Same height as the palette field so one theme padding centres both.
        var input = TextInput.New(_historyQuery);
        input.PlaceholderText("Press / to search commits…")
            .OnTextChanged(OnHistoryQueryChanged)
            .Height(Dimension.Px(30))
            .FlexGrow(1).FlexShrink(1).FlexBasis(Dimension.Px(0))
            .TextColor(Nocturne.Text)
            .WithRef(_historySearchRef);
        input.On<KeyDownEvent>(HistorySearchKey);
        box.AddChild(input);

        if (_historyQuery.Length > 0)
        {
            var clear = Box.New().Width(18).Height(18).FlexShrink(0).CornerRadius(9f)
                .JustifyContent(JustifyContent.Center).AlignItems(AlignItems.Center)
                .Cursor(CursorType.Pointer)
                .Children(Ui.Icon(Icons.X, 10f, Nocturne.On(0.5)));
            clear.OnClick(_ => OnHistoryQueryChanged(""));
            clear.HoverBg(Nocturne.Neutral800, Color.Transparent);
            box.AddChild(clear);
        }
        return box;
    }

    private void OnHistoryQueryChanged(string q)
    {
        _historyQuery = q;
        _pendingScrollToSelected = true;
        MarkViewDirty();
    }

    private void HistorySearchKey(KeyDownEvent e)
    {
        // Let Alt-modified keys bubble to the global repo/workspace shortcuts.
        // Ctrl bubbles too, except the Windows-list keys (Ctrl+↑/↓ moves the
        // focus caret, Ctrl+Space toggles) which the list handles even while
        // the search box is focused.
        if (e.Modifiers.HasFlag(KeyModifiers.Alt)) return;
        if (e.Modifiers.HasFlag(KeyModifiers.Ctrl)
            && e.Key is not (KeyCode.Up or KeyCode.Down or KeyCode.Space))
            return;
        if (e.Key == KeyCode.Escape)
        {
            _historyQuery = "";
            MarkDirty();
            e.StopPropagation = true;
            return;
        }
        // Home/End (± Shift) and Shift+PageUp/Down belong to the text box — they move
        // the caret or select within the query. Plain PageUp/Down page the list.
        if (IsFieldEditKey(e)) return;
        if (HandleHistoryNavKey(e))
            e.StopPropagation = true;
    }

    private void ClearInclude()
    {
        IncludeSet(_activeRepoPath).Clear();
        BumpDataGen(_activeRepoPath);
        MarkDirty();
    }

    // ── Content search actions ───────────────────────────────────────────

    private void ToggleContentSearch()
    {
        _contentSearchOpen = !_contentSearchOpen;
        if (_contentSearchOpen) _pendingContentSearchFocus = true;
        MarkDirty();
    }

    /// <summary>Palette entry point: switch to History, reveal the content-search
    /// bar and focus it.</summary>
    private void OpenContentSearch()
    {
        SetView(JunctionView.History);
        _contentSearchOpen = true;
        _pendingContentSearchFocus = true;
        MarkDirty();
    }

    /// <summary>Runs the pickaxe query (re-loads history). Empty query clears it.</summary>
    private void ApplyContentSearch()
    {
        var q = _contentQuery.Trim();
        if (q == _contentApplied) return;   // no change → no reload
        _contentApplied = q;
        _pendingScrollToSelected = true;
        MarkDirty();   // the loader key now differs → History reloads with -S/-G
    }

    private void SetContentMode(bool regex)
    {
        if (_contentRegex == regex) return;
        _contentRegex = regex;
        // The loader key embeds the mode, so if a query is applied this rebuild
        // re-runs the load with the new -S/-G flag; the same loader instance is
        // already subscribed, so its completion is observed.
        MarkDirty();
    }

    private void ClearContentSearch()
    {
        _contentQuery = "";
        _contentApplied = "";
        _pendingScrollToSelected = true;
        MarkDirty();
    }

    private void ContentSearchKey(KeyDownEvent e)
    {
        if (e.Modifiers.HasFlag(KeyModifiers.Alt)) return;
        if (e.Key == KeyCode.Enter) { ApplyContentSearch(); e.StopPropagation = true; return; }
        if (e.Key == KeyCode.Escape)
        {
            // First Esc clears an applied query; a second closes the bar.
            if (_contentApplied.Length > 0 || _contentQuery.Length > 0) ClearContentSearch();
            else { _contentSearchOpen = false; MarkDirty(); }
            e.StopPropagation = true;
        }
    }

    // ── Commit list ──────────────────────────────────────────────────────

    private Element BuildCommitList(HistoryData data,
        Dictionary<string, (int Added, int Deleted)> stats, List<GitCommit> commits)
    {
        if (commits.Count == 0)
        {
            var searching = _historyQuery.Trim().Length > 0;
            var empty = _contentApplied.Length > 0
                ? $"No commits {(_contentRegex ? "whose diff matches" : "that changed")} \"{_contentApplied}\""
                : searching ? "No matching commits" : "No commits yet";
            var box = Box.New().FlexGrow(1).Gap(12).JustifyContent(JustifyContent.Center).AlignItems(AlignItems.Center)
                .Children(Text.New(empty).FontSize(14f).TextColor(Nocturne.On(0.45)));
            // A search that found nothing in the pages loaded has not searched the history —
            // say so, and offer the rest, or the empty list reads as "not in this repository".
            if (searching && data.Commits.Count > 0 && !data.Exhausted)
            {
                box.AddChild(Text.New($"in the newest {data.Commits.Count:N0} commits")
                    .FontSize(12f).TextColor(Nocturne.On(0.35)));
                box.AddChild(HistoryLoadAllButton());
            }
            return box;
        }

        var selectedIndex = _selectedCommitHash == null
            ? -1
            : IndexOfHash(commits, _selectedCommitHash);

        // The list keeps a row until its commit or its focus changes, so a move makes two rows
        // instead of every row on screen. Everything else a row shows is told here: when any of
        // it changes, the rows on screen are made again.
        var shape = new HistoryRowShape(data.Graph, data.Remotes, stats, IssueRules(), _compactChrome,
            _historyQuery.Trim(), data.Exhausted, GitLabRowData(), Nocturne.Theme, Ui.DisplayScale);
        // `|`, not `||`: the selection's snapshot is brought up to date either way.
        if (HistoryRowSelectionChanged() | shape != _historyRowShape)
        {
            _commitListRef.Current?.InvalidateRows();
            _historyRowShape = shape;
        }

        return VirtualListElement<GitCommit>.New()
            .ItemHeight((int)RowHeight)
            .Items(commits)
            // `focused` is the list's selected index — the focus caret, which a plain move and
            // Ctrl+arrows move. Membership of a wider selection is read live; HistoryRowSelection-
            // Changed above remakes the rows when it is anything but the focused commit alone.
            .RenderWith((c, focused) => HistoryRow(c,
                data.Graph.TryGetValue(c.Hash, out var g) ? g : default,
                stats.TryGetValue(c.Hash, out var s) ? s : (0, 0),
                data.Remotes,
                SelectionContains(c.Hash), focused))
            .AccessibleItemLabel(c => $"{c.Subject}, {c.AuthorName}, {c.ShortHash}")
            .OnActivateItem(c =>
            {
                // Activating a commit inside the multi-selection opens the compare;
                // activating an unselected one collapses the selection to it first.
                if (!SelectionContains(c.Hash)) SetCommitSelection(c.Hash);
                SetView(JunctionView.Commit);
            })
            .SelectedIndex(selectedIndex)
            .FlexGrow(1)
            .WithRef(_commitListRef);
    }

    /// <summary>
    /// What a History row shows besides its commit and whether it has the focus — the two
    /// things its list compares. References are compared by identity: the graph, remotes and
    /// counts are new objects when a page or a chunk of counts lands, the issue rules when the
    /// rules file changes, and GitLab's pipelines and merge requests when a refresh lands. The
    /// query and the end of the log decide which rows ask for the next page. The chips' glyphs
    /// are cut at the display scale, and a change of scale retires the old rasters (the next
    /// frees them), so the scale is here too.
    /// </summary>
    private readonly record struct HistoryRowShape(
        object Graph, object Remotes, object Stats, object IssueRules, bool Compact,
        string Query, bool Exhausted, (object? Pipelines, object? MergeRequests) GitLab, object Theme,
        float Scale);

    private HistoryRowShape _historyRowShape;

    /// <summary>Rows History's template has made, for tests.</summary>
    internal int HistoryRowsMade { get; private set; }

    /// <summary>GitLab's data the rows draw from (pipeline glyphs, merge request chips), or
    /// nothing when the repository is not on GitLab with an account — the same reads the rows
    /// make, once a build.</summary>
    private (object?, object?) GitLabRowData() =>
        HasActiveRepo && GitLabActive(_activeRepoPath)
            ? (PipelinesFor(_activeRepoPath).Value, MergeRequestsFor(_activeRepoPath).Value)
            : (null, null);

    // The multi-selection as the rows on screen last drew it; empty while the selection is the
    // focused commit alone.
    private readonly HashSet<string> _historyRowSelection = new(StringComparer.Ordinal);
    private bool _historyRowSelectionSingle = true;

    /// <summary>
    /// Whether the selection changed in a way the list cannot see. The list is told the focused
    /// commit; while the selection is that commit alone, a row is selected exactly when it has
    /// the focus, and a move needs nothing more. A wider selection (Shift, Ctrl), or one the
    /// focus is not in, colours rows by membership and names Compare on the focused row, so any
    /// change to it makes the rows again.
    /// </summary>
    private bool HistoryRowSelectionChanged()
    {
        bool single = _selectedCommitHashes.Count == 1
            && _selectedCommitHash is { } focus && _selectedCommitHashes.Contains(focus);
        if (single)
        {
            if (_historyRowSelectionSingle) return false;
            _historyRowSelectionSingle = true;
            _historyRowSelection.Clear();
            return true;
        }
        if (!_historyRowSelectionSingle && _historyRowSelection.SetEquals(_selectedCommitHashes)) return false;
        _historyRowSelectionSingle = false;
        _historyRowSelection.Clear();
        _historyRowSelection.UnionWith(_selectedCommitHashes);
        return true;
    }

    private static int IndexOfHash(IReadOnlyList<GitCommit> commits, string hash)
    {
        for (var i = 0; i < commits.Count; i++)
            if (commits[i].Hash == hash) return i;
        return -1;
    }

    private Element HistoryRow(GitCommit commit, GitGraphRow graph, (int Added, int Deleted) stat,
        HashSet<string> remotes, bool selected, bool focused)
    {
        HistoryRowsMade++;
        // A row near the loaded end coming on screen is the list being scrolled towards it —
        // by wheel, keyboard, scrollbar or autoscroll alike, since every one of them ends in
        // the list realizing the rows it now shows. This runs inside the render pass, so the
        // request is deferred to after the frame rather than marking the tree dirty mid-paint.
        if (_historyTail.Contains(commit.Hash) && !_historyMoreQueued)
        {
            _historyMoreQueued = true;
            _window.RequestPostFrameCallback(() =>
            {
                _historyMoreQueued = false;
                if (View == JunctionView.History) RequestMoreHistory(all: false);
            });
        }

        // The focus caret (Ctrl+arrows) can sit outside the selection; give it a
        // hover-strength tint so keyboard navigation stays visible.
        var bg = selected ? Nocturne.Accent900
            : focused ? Nocturne.Neutral900
            : Color.Transparent;
        var row = Flex.Row(gap: 0)
            .AlignItems(AlignItems.Stretch)
            .Height(RowHeight)
            .Background(bg)
            .BorderColor(Nocturne.On(0.05)).BorderWidth(new Thickness(0, 0, 1, 0))
            .Cursor(CursorType.Pointer);

        // Graph gutter.
        var columns = Math.Max(graph.LaneCount, 1);
        var gutterWidth = Math.Max(56f, columns * LaneWidth + 10f);
        row.AddChild(Canvas.New(ctx => DrawGraph(ctx, graph)).Width(gutterWidth).FlexShrink(0));

        // Body.
        var body = Flex.Column(gap: 3).FlexGrow(1).JustifyContent(JustifyContent.Center)
            .Padding(new Thickness(0, 8, 0, 0));

        // Line 1: ref chips + message. Narrow windows drop the chips (HEAD / branch
        // / remote markers) so they don't wrap or crowd the subject; FlexShrink(0)
        // keeps a shown chip from compressing and wrapping its own label.
        var line1 = Flex.Row(gap: 6).AlignItems(AlignItems.Center);
        if (!_compactChrome)
            foreach (var chip in RefChips(commit.Refs, remotes))
                line1.AddChild(chip.FlexShrink(0));
        // The subject carries issue links too — the log is where you go looking for the
        // commit that mentions a ticket, so making you open it first to reach the ticket is
        // one step too many. Clicking a link opens the issue and leaves the row alone; the
        // link handler stops the click before the row's own selection sees it.
        line1.AddChild(IssueLinked(Text.New(commit.Subject).FontSize(13f).TextColor(Nocturne.Text), commit.Subject)
            .FlexGrow(1).FlexShrink(1).OverflowMode(TextOverflow.Ellipsis).MaxLines(1));
        body.AddChild(line1);

        // Line 2: sha · author · time · +adds −dels.
        var line2 = Flex.Row(gap: 9).AlignItems(AlignItems.Center);
        line2.AddChild(Ui.Mono(commit.ShortHash, 11f, Nocturne.On(0.46)).Tooltip(commit.Hash));
        line2.AddChild(Text.New(commit.AuthorName).FontSize(11f).TextColor(Nocturne.On(0.46)));
        line2.AddChild(Text.New("·").FontSize(11f).TextColor(Nocturne.On(0.46)));
        // The label is the author's own wall clock: git records the offset a commit was
        // written at, and "g" renders in that offset — to the minute, and without saying
        // which offset it is. So the tooltip carries what the label leaves out, the seconds
        // and the offset, rather than restating the same instant in a second format.
        line2.AddChild(Text.New(commit.AuthorDate.ToString("g")).FontSize(11f).TextColor(Nocturne.On(0.46))
            .Tooltip(commit.AuthorDate.ToString("yyyy-MM-dd HH:mm:ss zzz")));
        if (stat.Added > 0)
            line2.AddChild(Text.New($"+{stat.Added}").FontSize(11f).TextColor(Nocturne.AddCount));
        if (stat.Deleted > 0)
            line2.AddChild(Text.New($"−{stat.Deleted}").FontSize(11f).TextColor(Nocturne.DelCount));
        // What GitLab knows about this commit, after the stats: its latest pipeline, and
        // the merge request it is the head of. Most rows have neither and show nothing.
        if (GitLabActive(_activeRepoPath))
        {
            if (PipelineForSha(_activeRepoPath, commit.Hash) is { } pipe && Ui.PipelineGlyph(pipe.Status, 12f) is { } glyph)
                line2.AddChild(glyph.FlexShrink(0).Tooltip(PipelineSummary(pipe)));
            if (MergeRequestForSha(_activeRepoPath, commit.Hash) is { } mr)
                line2.AddChild(MergeRequestChip(mr).FlexShrink(0));
        }
        body.AddChild(line2);

        row.AddChild(body);

        // Affordance follows the focus caret; it names what Enter will do
        // (a multi-selection compares, otherwise the focused commit opens).
        if (focused)
            row.AddChild(Flex.Row(gap: 4).AlignItems(AlignItems.Center).FlexShrink(0)
                .Padding(new Thickness(0, 12, 0, 8))
                .Children(
                    Text.New(_selectedCommitHashes.Count > 1 ? "Compare" : "Details")
                        .FontSize(11f).TextColor(Nocturne.Accent300),
                    Ui.Icon(Icons.ArrowRight, 12f, Nocturne.Accent300)));

        row.OnClick(e => OnCommitRowClick(commit, e.Modifiers));
        // Right-click outside the selection collapses to that row first, then the
        // framework opens the menu; inside it the selection is left alone.
        row.On<PointerDownEvent>(e =>
        {
            if (e.Button == 2 && !SelectionContains(commit.Hash))
            {
                SetCommitSelection(commit.Hash);
                MarkDirty();
            }
        });
        row.ContextMenu(() => CommitMenuItems(commit));
        if (!selected)
            row.HoverBg(Nocturne.Neutral900, bg);   // restore the focus tint on leave

        return row;
    }

    // How many ref chips a commit row shows before collapsing the rest into a
    // "+N" overflow chip. A branch tip can carry many refs (local + its remote,
    // plus origin/HEAD); this keeps the row from pushing the subject off-screen.
    private const int MaxRefChips = 4;

    private static IEnumerable<Element> RefChips(string[] refs, HashSet<string> remotes)
    {
        // Parse the decorations into their kinds so we can collapse the noise:
        //  - local branches carry no icon (a bare name reads as "local");
        //  - a remote-only branch (not checked out here) gets the cloud icon, and its
        //    remote prefix is dropped when there's only one remote (origin/main → main);
        //  - a local branch that also sits on the remote here merges into ONE chip with
        //    a small green ✓ sync badge (main + origin/main → main ✓) — so the cloud
        //    always means "remote-only", never "in sync".
        string? head = null;                     // branch HEAD points at ("HEAD -> X")
        var detached = false;                    // bare "HEAD"
        var locals = new List<string>();
        var remoteRefs = new List<(string Remote, string Leaf)>();
        var tags = new List<string>();

        foreach (var raw in refs)
        {
            var d = raw.Trim();
            if (d.Length == 0) continue;
            if (d.StartsWith("HEAD -> ", StringComparison.Ordinal)) { head = d["HEAD -> ".Length..]; locals.Add(head); continue; }
            if (d == "HEAD") { detached = true; continue; }
            if (d.StartsWith("tag: ", StringComparison.Ordinal)) { tags.Add(d[5..]); continue; }
            var seg = d.Split('/', 2);
            if (seg.Length == 2 && remotes.Contains(seg[0]))
            {
                if (d.EndsWith("/HEAD", StringComparison.Ordinal)) continue;  // symbolic origin/HEAD
                remoteRefs.Add((seg[0], seg[1]));
                continue;
            }
            locals.Add(d);
        }

        var singleRemote = remotes.Count <= 1;
        var consumed = new bool[remoteRefs.Count];
        var chips = new List<Element>();

        if (head != null || detached)
            chips.Add(Ui.Chip(Icons.ArrowFatRight, "HEAD", Color.Transparent, Nocturne.Accent, outline: true));

        // Local branches first; merge a matching remote-tracking ref on this commit.
        foreach (var name in locals)
        {
            var paired = false;
            for (var i = 0; i < remoteRefs.Count; i++)
                if (!consumed[i] && remoteRefs[i].Leaf == name) { consumed[i] = true; paired = true; }
            chips.Add(BranchChip(name, onRemote: paired));
        }

        // Remote-only branches: cloud + name (with the remote prefix only if ambiguous).
        for (var i = 0; i < remoteRefs.Count; i++)
        {
            if (consumed[i]) continue;
            var (remote, leaf) = remoteRefs[i];
            chips.Add(Ui.Chip(Icons.Cloud, singleRemote ? leaf : $"{remote}/{leaf}",
                Nocturne.Neutral800, Nocturne.Neutral100));
        }

        foreach (var t in tags)
            chips.Add(Ui.Chip(Icons.Tag, t, Nocturne.Neutral800, Nocturne.Neutral100));

        if (chips.Count <= MaxRefChips)
        {
            foreach (var chip in chips) yield return chip;
            yield break;
        }
        // Reserve the last slot for the overflow count so nothing vanishes silently.
        for (var i = 0; i < MaxRefChips - 1; i++) yield return chips[i];
        yield return Ui.Chip(Icons.GitBranch, $"+{chips.Count - (MaxRefChips - 1)}",
            Nocturne.Neutral800, Nocturne.Neutral100);
    }

    /// <summary>A branch chip: local branches are text-only (no icon); a local branch
    /// that's also in sync with its remote here gets a leading 🤝 badge (🤝 main).
    /// The cloud icon is reserved for remote-only refs.</summary>
    private static Element BranchChip(string name, bool onRemote)
    {
        var pb = BranchName.Parse(name);
        var (bg, fg) = pb.ChipLabel switch
        {
            "FEAT" => (Nocturne.Accent800, Nocturne.Accent100),
            "WIP" => (Nocturne.Accent2_800, Nocturne.Accent2_100),
            _ => (Nocturne.Neutral800, Nocturne.Neutral100),
        };
        var chip = Ui.Chip(onRemote ? Icons.Handshake : null, pb.Title, bg, fg);
        if (onRemote) chip.Tooltip("Local and remote in sync");
        return chip;
    }

    // ── Footer ───────────────────────────────────────────────────────────

    private Element BuildHistoryFooter(HistoryData data)
    {
        var bar = Flex.Row(gap: 16)
            .FlexShrink(0).AlignItems(AlignItems.Center)
            .Padding(new Thickness(8, 16, 8, 16))
            .BorderColor(Nocturne.Divider).BorderWidth(new Thickness(1, 0, 0, 0));

        if (HistoryPagingControls(data) is { } paging) bar.AddChild(paging);
        var comparing = SelectedRange() != null;
        bar.AddChild(Box.New().FlexGrow(1));
        bar.AddChild(Ui.SecondaryButton(
            comparing ? $"Compare {_selectedCommitHashes.Count} commits" : "Open commit details",
            Icons.ArrowRight, () => SetView(JunctionView.Commit), 11.5f));
        return bar;
    }

    /// <summary>
    /// The left of the footer while there is history behind what is loaded: how far the list
    /// reaches, and the two ways to reach further — a page, or all of it. Nothing once the log
    /// is exhausted; the count in the include bar says the whole of it is there.
    /// </summary>
    private Element? HistoryPagingControls(HistoryData data)
    {
        if (data.Commits.Count == 0 || data.Exhausted) return null;
        var row = Flex.Row(gap: 10).AlignItems(AlignItems.Center).FlexShrink(0);
        var loaded = data.Commits.Count;

        if (WindowFor(_activeRepoPath).LoadAll)
        {
            row.AddChild(Text.New($"Loading every commit… {loaded:N0} so far")
                .FontSize(11f).TextColor(Nocturne.On(0.55)));
            row.AddChild(Ui.GhostButton("Stop", Icons.X, StopLoadingAllHistory, 11f));
            row.Key = "paging#all";
            return row;
        }
        if (_history.IsLoading)
        {
            row.AddChild(Text.New("Loading older commits…").FontSize(11f).TextColor(Nocturne.On(0.55)));
            row.Key = "paging#more";
            return row;
        }
        row.AddChild(Text.New($"The newest {loaded:N0} commits").FontSize(11f).TextColor(Nocturne.On(0.55)));
        row.AddChild(Ui.GhostButton($"Load {_historyPageCommits:N0} more", Icons.ArrowDown,
            () => RequestMoreHistory(all: false), 11f));
        row.AddChild(HistoryLoadAllButton());
        // Three states of different shape in one slot: a key per state, so a change swaps the
        // row rather than patching one state's children over another's.
        row.Key = "paging#idle";
        return row;
    }

    /// <summary>"Load all", or with a search on "Search all history" — the same load, named
    /// for what the person is doing.</summary>
    private Element HistoryLoadAllButton() =>
        Ui.GhostButton(_historyQuery.Trim().Length > 0 ? "Search all history" : "Load all",
            Icons.TrayArrowDown, () => RequestMoreHistory(all: true), 11f);

    private static Element Hint(string keys, string label)
    {
        return Flex.Row(gap: 5).AlignItems(AlignItems.Center).Children(
            Text.New(keys).FontSize(11f).FontWeight(FontWeight.SemiBold).TextColor(Nocturne.On(0.68)),
            Text.New(label).FontSize(11f).TextColor(Nocturne.On(0.45)));
    }

    // ── Graph rendering ──────────────────────────────────────────────────

    // Runs every frame for every row on screen, so it loops by index: a foreach over an
    // IReadOnlyList boxes an enumerator each time.
    private static void DrawGraph(ICanvasDrawingContext ctx, GitGraphRow row)
    {
        var h = ctx.Bounds.Height;
        var centerY = h / 2f;

        if (row.EdgesFromPrev is { } fromPrev)
            for (int i = 0; i < fromPrev.Count; i++)
            {
                var e = fromPrev[i];
                ctx.DrawBezier(new Point(LaneX(e.FromLane), 0), new Point(LaneX(e.ToLane), centerY),
                    LaneColor(e.ColorIndex), 2f);
            }
        if (row.EdgesToNext is { } toNext)
            for (int i = 0; i < toNext.Count; i++)
            {
                var e = toNext[i];
                ctx.DrawBezier(new Point(LaneX(e.FromLane), centerY), new Point(LaneX(e.ToLane), h),
                    LaneColor(e.ColorIndex), 2f);
            }

        var center = new Point(LaneX(row.Lane), centerY);
        var color = LaneColor(ColorIndexForLane(row));
        ctx.DrawCircle(center, DotRadius + 2.5f, Nocturne.Bg);   // 3px ring in the ground colour
        ctx.DrawCircle(center, DotRadius, color);
    }

    private static int ColorIndexForLane(GitGraphRow row)
    {
        if (row.EdgesToNext is { } toNext)
            for (int i = 0; i < toNext.Count; i++)
                if (toNext[i].FromLane == row.Lane) return toNext[i].ColorIndex;
        if (row.EdgesFromPrev is { } fromPrev)
            for (int i = 0; i < fromPrev.Count; i++)
                if (fromPrev[i].ToLane == row.Lane) return fromPrev[i].ColorIndex;
        return row.Lane;
    }

    private static float LaneX(int lane) => lane * LaneWidth + 12f;

    private static Color LaneColor(int i)
    {
        var palette = LanePalette;
        int n = palette.Length;
        return palette[((i % n) + n) % n];
    }
}
