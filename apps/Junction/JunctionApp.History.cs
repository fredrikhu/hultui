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

    // A property, not a static readonly field: the ramp it reads changes with the theme, and
    // a field would hold whichever one was current when this type was first touched.
    private static Color[] LanePalette =>
    [
        Nocturne.Accent400,
        Nocturne.Neutral300,
        Nocturne.Accent2_400,
        Nocturne.Accent300,
        Nocturne.Neutral400,
        Nocturne.Accent500,
    ];

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

        var col = Flex.Column(gap: 0).FlexGrow(1);
        col.AddChild(BuildIncludeBar(
            IncludeSet(_activeRepoPath).OrderBy(x => x, StringComparer.Ordinal).ToList(), filtered.Count));
        if (_contentSearchOpen)
            col.AddChild(BuildContentSearchBar(filtered.Count));
        col.AddChild(BuildCommitList(data, stats, filtered));
        col.AddChild(BuildHistoryFooter());
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
        var key = $"{path}|{string.Join(",", includeList)}|{DataGen(path)}"
                + (content.Length > 0 ? $"|{(contentRegex ? "G" : "S")}:{content}" : "");
        // The initial selection is active-repo state; a preload of some other repo (none
        // today, but the guard is cheap) must not pick a commit in this one.
        _historyLoaders.For(path).Load(key, () => LoadHistory(path, includeList, content, contentRegex),
            onLoaded: data => { if (PathEquals(path, _activeRepoPath)) PickInitialCommit(data); });
        if (stats)
            _historyStatLoaders.For(path).Load(key,
                () => RepoFor(path).GetLogStats(refs: includeList.Count > 0 ? includeList : null,
                    pickaxe: content, pickaxeRegex: contentRegex));
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
        string? content = null, bool contentRegex = false)
    {
        var repo = RepoFor(path);
        var refs = includeList.Count > 0 ? includeList : null;
        var commits = repo.GetLog(includeRefs: refs, pickaxe: content, pickaxeRegex: contentRegex);

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
        return new HistoryData(commits, graph, remotes);
    }

    private void PickInitialCommit(HistoryData data)
    {
        if (data.Commits.Count == 0) { SetCommitSelection(null); return; }
        if (_selectedCommitHash == null || data.Commits.All(c => c.Hash != _selectedCommitHash))
            SetCommitSelection(data.Commits[0].Hash);
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
        // Rerender, not MarkDirty: changing the selected commit changes the Files
        // view's per-commit tree/content loader KEYS, so the next build activates new
        // AsyncLoader instances — an untracked rebuild would leave them unsubscribed
        // and the browser stuck on "Loading…" (see OnExternalChange).
        Rerender();
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
        _pendingRevealHash = null;

        if (IndexOfHash(filtered, hash) >= 0)
        {
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
    private Element BuildIncludeBar(List<string> includeList, int commitCount)
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

        var count = Text.New($"{commitCount} commits").FontSize(11f).TextColor(Nocturne.On(0.40));
        count.Key = "include-count";
        bar.AddChild(count);
        return bar;
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
        var box = Flex.Row(gap: 6).AlignItems(AlignItems.Center)
            .Height(32).FlexGrow(1).MaxWidth(460)
            .Padding(new Thickness(0, 10, 0, 10))
            .Background(Nocturne.Bg).BorderColor(Nocturne.Divider).BorderWidth(1f)
            .CornerRadius(Nocturne.RadiusMd);
        var input = TextInput.New(_contentQuery);
        input.PlaceholderText(_contentRegex
                ? "Find commits whose diff matches this regex… (Enter)"
                : "Find commits that added or removed this text… (Enter)")
            .OnTextChanged(q => { _contentQuery = q; MarkDirty(); })
            .Height(Dimension.Px(28)).FlexGrow(1).FlexShrink(1).FlexBasis(Dimension.Px(0))
            .TextColor(Nocturne.Text).WithRef(_contentSearchRef);
        input.On<KeyDownEvent>(ContentSearchKey);
        box.AddChild(input);
        bar.AddChild(box);

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
        MarkDirty();
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
            var empty = _contentApplied.Length > 0
                ? $"No commits {(_contentRegex ? "whose diff matches" : "that changed")} \"{_contentApplied}\""
                : _historyQuery.Trim().Length > 0 ? "No matching commits" : "No commits yet";
            return Box.New().FlexGrow(1).JustifyContent(JustifyContent.Center).AlignItems(AlignItems.Center)
                .Children(Text.New(empty).FontSize(14f).TextColor(Nocturne.On(0.45)));
        }

        var selectedIndex = _selectedCommitHash == null
            ? -1
            : IndexOfHash(commits, _selectedCommitHash);

        return VirtualListElement<GitCommit>.New()
            .ItemHeight((int)RowHeight)
            .Items(commits)
            .RenderWith(c => HistoryRow(c,
                data.Graph.TryGetValue(c.Hash, out var g) ? g : default,
                stats.TryGetValue(c.Hash, out var s) ? s : (0, 0),
                data.Remotes,
                SelectionContains(c.Hash)))
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

    private static int IndexOfHash(IReadOnlyList<GitCommit> commits, string hash)
    {
        for (var i = 0; i < commits.Count; i++)
            if (commits[i].Hash == hash) return i;
        return -1;
    }

    private Element HistoryRow(GitCommit commit, GitGraphRow graph, (int Added, int Deleted) stat,
        HashSet<string> remotes, bool selected)
    {
        // The focus caret (Ctrl+arrows) can sit outside the selection; give it a
        // hover-strength tint so keyboard navigation stays visible.
        var focused = commit.Hash == _selectedCommitHash;
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

    private Element BuildHistoryFooter()
    {
        var bar = Flex.Row(gap: 16)
            .FlexShrink(0).AlignItems(AlignItems.Center)
            .Padding(new Thickness(8, 16, 8, 16))
            .BorderColor(Nocturne.Divider).BorderWidth(new Thickness(1, 0, 0, 0));

        var comparing = SelectedRange() != null;
        bar.AddChild(Box.New().FlexGrow(1));
        bar.AddChild(Ui.SecondaryButton(
            comparing ? $"Compare {_selectedCommitHashes.Count} commits" : "Open commit details",
            Icons.ArrowRight, () => SetView(JunctionView.Commit), 11.5f));
        return bar;
    }

    private static Element Hint(string keys, string label)
    {
        return Flex.Row(gap: 5).AlignItems(AlignItems.Center).Children(
            Text.New(keys).FontSize(11f).FontWeight(FontWeight.SemiBold).TextColor(Nocturne.On(0.68)),
            Text.New(label).FontSize(11f).TextColor(Nocturne.On(0.45)));
    }

    // ── Graph rendering ──────────────────────────────────────────────────

    private static void DrawGraph(ICanvasDrawingContext ctx, GitGraphRow row)
    {
        var h = ctx.Bounds.Height;
        var centerY = h / 2f;

        foreach (var e in row.EdgesFromPrev ?? [])
            ctx.DrawBezier(new Point(LaneX(e.FromLane), 0), new Point(LaneX(e.ToLane), centerY),
                LaneColor(e.ColorIndex), 2f);
        foreach (var e in row.EdgesToNext ?? [])
            ctx.DrawBezier(new Point(LaneX(e.FromLane), centerY), new Point(LaneX(e.ToLane), h),
                LaneColor(e.ColorIndex), 2f);

        var center = new Point(LaneX(row.Lane), centerY);
        var color = LaneColor(ColorIndexForLane(row));
        ctx.DrawCircle(center, DotRadius + 2.5f, Nocturne.Bg);   // 3px ring in the ground colour
        ctx.DrawCircle(center, DotRadius, color);
    }

    private static int ColorIndexForLane(GitGraphRow row)
    {
        foreach (var e in row.EdgesToNext ?? [])
            if (e.FromLane == row.Lane) return e.ColorIndex;
        foreach (var e in row.EdgesFromPrev ?? [])
            if (e.ToLane == row.Lane) return e.ColorIndex;
        return row.Lane;
    }

    private static float LaneX(int lane) => lane * LaneWidth + 12f;

    private static Color LaneColor(int i) =>
        LanePalette[((i % LanePalette.Length) + LanePalette.Length) % LanePalette.Length];
}
