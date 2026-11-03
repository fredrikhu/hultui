using HultUI;
using HultUI.Core.Accessibility;
using HultUI.Core.Builders;
using HultUI.Core.Elements;
using HultUI.Core.Events;
using HultUI.Core.Layout;
using HultUI.Core.Primitives;
using HultUI.Core.Styling;
using Junction.Git;
using Junction.Theme;

namespace Junction;

public partial class JunctionApp
{
    private Element BuildChangesView()
    {
        if (!HasActiveRepo)
            return BuildStubView("Changes", "Open a repository first.");

        SyncChangeTrees(_activeRepoPath);
        EnsureChangesLoaded(_activeRepoPath);
        var data = ChangesView;

        // Mid-operation the right pane is always shown — it hosts the per-file
        // conflict resolution — even on narrow windows where diffs normally hide.
        bool showPane = !_hideDiff || data.InSequencer;

        var row = Flex.Row(gap: 0).FlexGrow(1).AlignItems(AlignItems.Stretch);
        row.AddChild(BuildChangesColumn(data, fill: !showPane));
        if (showPane)
        {
            row.AddChild(_changesSplit.Handle(Nocturne.Alpha(Nocturne.Accent, 0.18), Nocturne.Alpha(Nocturne.Accent, 0.40)));
            row.AddChild(BuildChangesRightPane(data));
        }
        return row;
    }

    // The right pane: a conflicted file shows the ours/theirs resolution UI; any
    // other selection shows the normal diff (open in VS Code with Ctrl+D).
    private Element BuildChangesRightPane(ChangesData data)
    {
        if (data.InSequencer && _changeSelConflicted && _changeSelPath != null
            && data.Conflicted.Any(f => f.Path == _changeSelPath))
            return BuildConflictResolutionPane(_changeSelPath, data.Sequencer);

        GitFileDiff? diff = null;
        if (_changeSelPath != null && !_changeSelConflicted)
        {
            var dict = _changeSelStaged ? data.StagedDiffs : data.UnstagedDiffs;
            dict.TryGetValue(_changeSelPath, out diff);
        }
        // Untracked files have a synthesized all-additions diff with no real index
        // header, so line-level patch application can't target them — stage the whole
        // file from the list instead. Line staging is for tracked modifications.
        bool untracked = diff != null && _changeSelPath != null && !_changeSelStaged
            && data.Unstaged.Any(f => f.Path == _changeSelPath && f.Kind == FileChangeKind.Untracked);
        var interact = untracked ? null : ChangesDiffInteract(diff);
        return BuildDiffPane(_changeSelPath ?? "", diff, interact);
    }

    // Runs off the UI thread.
    /// <summary>Starts (or reuses) the working-tree load for <paramref name="path"/>. The view
    /// calls it as it builds and the preload calls it from another tab; same key both ways.
    /// The selection bookkeeping in <see cref="OnChangesLoaded"/> is active-repo state, and
    /// there is no self-heal in the build for it, so the callback rides along here too —
    /// a preloaded Changes tab would otherwise open with nothing selected.</summary>
    private void EnsureChangesLoaded(string path)
    {
        // The ignore-whitespace flag is part of the key so toggling it reloads the diffs.
        bool ws = _settings.Git.IgnoreWhitespace;
        _changesLoaders.For(path).Load($"{path}|{DataGen(path)}|w{ws}",
            () => LoadOrRetry(path, () => LoadChanges(path, ws)),
            onLoaded: data => { if (PathEquals(path, _activeRepoPath)) OnChangesLoaded(data); });
    }

    // Runs off the UI thread.
    private ChangesData LoadChanges(string path, bool ignoreWhitespace)
    {
        var repo = RepoFor(path);
        var staged = repo.GetStagedChanges();
        var unstaged = repo.GetUnstagedChanges();

        // Conflict state. Unmerged files leak into `git diff` as plain "Modified",
        // so pull them from status (GetConflictedFiles) and remove them from the
        // ordinary lists — they get their own resolution panel instead.
        //
        // Any of the four sequencer operations can leave them: a rebase stopped on a
        // conflict looked like a working tree full of ordinary modifications until this
        // stopped asking specifically about merges.
        var state = repo.GetSequencerState();
        IReadOnlyList<GitFileChange> conflicted = state != null ? repo.GetConflictedFiles() : [];
        ConflictSides? sequencer = null;
        if (state != null)
        {
            var conflictPaths = conflicted.Select(f => f.Path).ToHashSet(StringComparer.Ordinal);
            unstaged = unstaged.Where(f => !conflictPaths.Contains(f.Path)).ToList();
            staged = staged.Where(f => !conflictPaths.Contains(f.Path)).ToList();
            sequencer = new ConflictSides(state,
                state.OursRef != null ? repo.GetCommit(state.OursRef) : null,
                state.TheirsRef != null ? repo.GetCommit(state.TheirsRef) : null);
        }

        var stagedDiffs = repo.GetWorkingDiff(staged: true, ignoreWhitespace: ignoreWhitespace)
            .GroupBy(d => d.Path).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        var unstagedDiffs = repo.GetWorkingDiff(staged: false, ignoreWhitespace: ignoreWhitespace)
            .GroupBy(d => d.Path).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        // Untracked files aren't in `git diff`; synthesize an all-additions diff.
        foreach (var f in unstaged.Where(f => f.Kind == FileChangeKind.Untracked))
        {
            if (unstagedDiffs.ContainsKey(f.Path)) continue;
            var ud = repo.GetUntrackedFileDiff(f.Path).FirstOrDefault();
            if (ud != null) unstagedDiffs[f.Path] = ud;
        }

        // Every listed file gets an entry, even an empty one. A file git status calls
        // modified while git diff has nothing to show for it (see AddStatusOnlyChanges) is
        // a row like any other, and the pane beside it has to read "No textual changes" —
        // a missing entry renders as "Select a file", next to the file just selected.
        foreach (var f in staged)
            if (!stagedDiffs.ContainsKey(f.Path)) stagedDiffs[f.Path] = new GitFileDiff(f.Path, null, []);
        foreach (var f in unstaged)
            if (!unstagedDiffs.ContainsKey(f.Path)) unstagedDiffs[f.Path] = new GitFileDiff(f.Path, null, []);

        var assumeUnchanged = repo.GetAssumeUnchangedFiles();
        return new ChangesData(staged, unstaged, stagedDiffs, unstagedDiffs, conflicted, sequencer,
            assumeUnchanged, repo.GetLastCommitMessage());
    }

    private void PickInitialChange(ChangesData data)
    {
        PruneChangeSelection(data);
        if (ChangeFocus() is { } focus && ChangeRowSet(data).Contains(focus)) return;

        // Prefer an unresolved conflict so its resolution pane shows straight away.
        if (data.Conflicted.Count > 0)
            SetChangeSelection(new ChangeRef(data.Conflicted[0].Path, ChangeSection.Conflicted));
        else if (data.Staged.Count > 0)
            SetChangeSelection(new ChangeRef(data.Staged[0].Path, ChangeSection.Staged));
        else if (data.Unstaged.Count > 0)
            SetChangeSelection(new ChangeRef(data.Unstaged[0].Path, ChangeSection.Unstaged));
        else
            SetChangeSelection(null);
    }

    // ── Selection model (Windows-list semantics, as in the History list) ──
    //
    // The three _changeSel* fields are the focus caret and _changeSelection the
    // selection set; see the field comments in JunctionApp.cs for the split and for
    // the one-section rule this list adds.

    /// <summary>Every row in display order — conflicts, then staged, then unstaged, each
    /// grouped into its folder tree exactly as the list lays them out
    /// (<see cref="SyncChangeListRows"/>). Folder rows are rows like any other here: keyboard
    /// navigation and Shift ranges index into this list, so a folder is somewhere the caret
    /// lands and something a range picks up.</summary>
    private List<ChangeRef> ChangeRows(ChangesData data)
    {
        SyncChangeListRows(data);
        return _changeWalk;
    }

    /// <summary>The three lists in display order, so the row walk and the build agree
    /// about what follows what.</summary>
    private static (ChangeSection Section, IReadOnlyList<GitFileChange> Files)[] ChangeSections(ChangesData data) =>
    [
        (ChangeSection.Conflicted, data.Conflicted),
        (ChangeSection.Staged, data.Staged),
        (ChangeSection.Unstaged, data.Unstaged),
    ];

    /// <summary>
    /// Every row a selection may still name, regardless of what is expanded. Visibility is
    /// the wrong question for pruning: collapsing a folder must not quietly drop the files
    /// under it from the selection, and the files are still there to stage.
    ///
    /// <para>Folder rows are derived from the paths rather than from the trees for the
    /// same reason — a folder's row exists whether or not its parent is open.</para>
    /// </summary>
    private static HashSet<ChangeRef> ChangeRowSet(ChangesData data)
    {
        var set = new HashSet<ChangeRef>();
        foreach (var (section, files) in ChangeSections(data))
            foreach (var f in files)
            {
                set.Add(new ChangeRef(f.Path, section));
                for (var i = f.Path.IndexOf('/'); i >= 0; i = f.Path.IndexOf('/', i + 1))
                    set.Add(new ChangeRef(f.Path[..i], section, IsFolder: true));
            }
        return set;
    }

    /// <summary>Drops selected rows (and an anchor) that no longer exist.
    ///
    /// <para>Runs when a load lands, and only then. Between a staging action and the
    /// reload that follows it, <c>_changes.Value</c> still describes the world before
    /// the action — pruning against that would delete the very selection the action
    /// just moved into the staged list (it looked like three rows that had vanished).
    /// Stale rows in the set are harmless in the meantime: rows render from the set by
    /// lookup, and the action paths are filtered through the live rows.</para></summary>
    private void PruneChangeSelection(ChangesData data)
    {
        if (_changeSelection.Count == 0 && _changeAnchor == null) return;
        var live = ChangeRowSet(data);
        _changeSelection.RemoveWhere(r => !live.Contains(r));
        if (_changeAnchor is { } a && !live.Contains(a)) _changeAnchor = null;
    }

    /// <summary>The focus caret as a row, or null when nothing is selected.</summary>
    private ChangeRef? ChangeFocus() => _changeCaret;

    /// <summary>Moves the focus caret without touching the selection set.
    ///
    /// <para>The three <c>_changeSel*</c> fields — the file the diff pane, Ctrl+D and the
    /// line-level staging selection all follow — move with it only for a *file* row. A
    /// caret on a folder leaves the open file where it is, so arrowing through the list
    /// doesn't blank the right-hand pane every time it crosses a folder (the Files tab
    /// behaves the same way).</para></summary>
    private void SetChangeFocus(ChangeRef row)
    {
        _changeCaret = row;
        if (row.IsFolder) return;
        _changeSelPath = row.Path;
        _changeSelStaged = row.Section == ChangeSection.Staged;
        _changeSelConflicted = row.Section == ChangeSection.Conflicted;
    }

    /// <summary>Collapses the file selection to a single row (or clears it), moving the
    /// caret and the Shift anchor with it. The only way the selection collapses.</summary>
    private void SetChangeSelection(ChangeRef? row)
    {
        _changeSelection.Clear();
        _changeAnchor = row;
        if (row is { } r)
        {
            SetChangeFocus(r);
            _changeSelection.Add(r);
        }
        else
        {
            _changeCaret = null;
            _changeSelPath = null;
            _changeSelStaged = false;
            _changeSelConflicted = false;
        }
    }

    /// <summary>True when this row can join the selection without making it span two
    /// sections (an empty selection can start anywhere).</summary>
    private bool SameSectionAsSelection(ChangeRef row) =>
        _changeSelection.Count == 0 || _changeSelection.First().Section == row.Section;

    /// <summary>Adds the contiguous run of rows between two rows (inclusive, either
    /// order) to the selection, keeping only those in <paramref name="b"/>'s section.</summary>
    private void AddChangeRangeToSelection(ChangeRef a, ChangeRef b)
    {
        var rows = ChangeRows(ChangesView);
        var i = rows.IndexOf(a);
        var j = rows.IndexOf(b);
        if (i < 0 || j < 0) { _changeSelection.Add(b); return; }
        if (i > j) (i, j) = (j, i);
        for (var k = i; k <= j; k++)
            if (rows[k].Section == b.Section) _changeSelection.Add(rows[k]);
    }

    /// <summary>File-row click with Windows list semantics: plain click selects, Ctrl
    /// toggles, Shift selects the anchor→clicked range, Ctrl+Shift adds that range. A
    /// modifier click that would carry the selection into another section starts a
    /// fresh single selection there instead — a set spanning Staged and Changes has no
    /// one staging direction.</summary>
    private void OnChangeRowClick(ChangeRef row, KeyModifiers mods)
    {
        var ctrl = mods.HasFlag(KeyModifiers.Ctrl);
        var shift = mods.HasFlag(KeyModifiers.Shift);
        var anchor = _changeAnchor ?? ChangeFocus();

        if (shift && anchor is { } a && a.Section == row.Section)
        {
            // Ctrl+Shift adds the range to what's already selected — unless that
            // selection sits in another section, which the range can't join.
            if (!ctrl || !SameSectionAsSelection(row)) _changeSelection.Clear();
            AddChangeRangeToSelection(a, row);
            SetChangeFocus(row);
            _changeAnchor = a;
        }
        else if (ctrl && !shift && SameSectionAsSelection(row))
        {
            // Toggle; focus follows the click either way (the selection may go empty).
            if (!_changeSelection.Add(row)) _changeSelection.Remove(row);
            SetChangeFocus(row);
            _changeAnchor = row;
        }
        else
        {
            SetChangeSelection(row);
        }
        // RerenderView, not MarkDirty: the caret drives the right pane, and a conflicted or
        // image file there activates a NEW per-file loader — see SelectChange.
        RerenderView();
    }

    /// <summary>Changes-list navigation, the same keys as the History list. Shift
    /// extends from the anchor, Ctrl moves the caret only, Ctrl+Shift adds the range
    /// without clearing, Ctrl+Space toggles the focused row.</summary>
    private bool HandleChangesNavKey(KeyDownEvent e)
    {
        var shift = e.Modifiers.HasFlag(KeyModifiers.Shift);
        var ctrl = e.Modifiers.HasFlag(KeyModifiers.Ctrl);
        var focusOnly = ctrl && !shift;
        switch (e.Key)
        {
            case KeyCode.Down: MoveChangeSelection(1, shift, focusOnly, ctrl); return true;
            case KeyCode.Up: MoveChangeSelection(-1, shift, focusOnly, ctrl); return true;
            case KeyCode.PageDown: MoveChangeSelection(ChangesPageSize, shift, focusOnly, ctrl); return true;
            case KeyCode.PageUp: MoveChangeSelection(-ChangesPageSize, shift, focusOnly, ctrl); return true;
            case KeyCode.Home: MoveChangeSelection(int.MinValue / 2, shift, focusOnly, ctrl); return true;
            case KeyCode.End: MoveChangeSelection(int.MaxValue / 2, shift, focusOnly, ctrl); return true;
            case KeyCode.Space when ctrl: ToggleFocusedChangeSelection(); return true;
            case KeyCode.Delete when !ctrl && !shift: return DiscardByKey();
            // The conventional folder keys, as in the Files tab and the ref tables.
            case KeyCode.Right: return ChangeTreeStep(right: true);
            case KeyCode.Left: return ChangeTreeStep(right: false);
            default: return false;
        }
    }

    /// <summary>Delete on the Changes list discards what the caret's row covers — the
    /// same action, and the same confirm, the row's context menu offers. It aims at the
    /// whole selection when the caret sits inside one, exactly as the menu does.
    ///
    /// <para>Unstaged rows only. Discard means <c>git restore</c> / <c>git clean</c>, which
    /// are defined against the worktree; a staged row has to be unstaged first, and saying
    /// so beats a Delete that silently does nothing.</para></summary>
    private bool DiscardByKey()
    {
        if (ChangeFocus() is not { } focus) return false;
        if (focus.Section != ChangeSection.Unstaged)
        {
            Flash(focus.Section == ChangeSection.Staged
                ? "Unstage it first — discard works on the worktree"
                : "Resolve the conflict first");
            return true;
        }
        PromptDiscardPaths(ChangeActionPaths(focus));
        return true;
    }

    /// <summary>←/→ on the caret's row via the tree model: right opens a closed folder and
    /// otherwise descends, left closes an open one and otherwise steps out to the parent.
    /// A step that lands elsewhere selects that row, exactly as ↑/↓ would — which is also
    /// why stepping out of a folder never leaves the caret hidden inside a collapse.</summary>
    private bool ChangeTreeStep(bool right)
    {
        if (ChangeFocus() is not { } focus) return true;
        var data = ChangesView;
        var nodes = ChangeTreeRows(focus.Section, SectionFiles(data, focus.Section));
        var i = nodes.FindIndex(n => n.IsFolder == focus.IsFolder && n.Path == focus.Path);
        if (i < 0) return true;

        var landed = ChangeTreeFor(focus.Section).Step(nodes, i, right);
        if (landed == i) return true;   // it expanded or collapsed instead, which says so itself
        var node = nodes[landed];
        SetChangeSelection(new ChangeRef(node.Path, focus.Section, node.IsFolder));
        _pendingScrollToSelected = true;
        RerenderView();
        return true;
    }

    // One line per row now that the folder above carries the directory, so a page could
    // be measured — but the count is what the Commit view's list uses and what the
    // History list's page maths degrades to, and eight rows is a page either way.
    private const int ChangesPageSize = 8;

    private void MoveChangeSelection(int delta, bool extend = false, bool focusOnly = false, bool additive = false)
    {
        var rows = ChangeRows(ChangesView);
        if (rows.Count == 0) return;
        var i = ChangeFocus() is { } f ? rows.IndexOf(f) : 0;
        if (i < 0) i = 0;
        i = Math.Clamp(i + delta, 0, rows.Count - 1);

        if (extend)
        {
            var anchor = _changeAnchor ?? ChangeFocus() ?? rows[i];
            // A range can't span two sections, so an extend that runs past the anchor's
            // section stops at its edge rather than dragging in rows staged the other way.
            i = ClampToSection(rows, i, anchor.Section);
            // Ctrl+Shift keeps the rest of the selection — but only while that keeps it
            // inside one section (the anchor can have been pruned out from under it).
            if (!additive || !SameSectionAsSelection(rows[i])) _changeSelection.Clear();
            AddChangeRangeToSelection(anchor, rows[i]);
            SetChangeFocus(rows[i]);
            _changeAnchor = anchor;
        }
        else if (focusOnly)
        {
            // Ctrl+arrows: move the focus caret only; selection and anchor stay.
            SetChangeFocus(rows[i]);
        }
        else
        {
            SetChangeSelection(rows[i]);
        }
        _pendingScrollToSelected = true;
        // RerenderView for the same reason as OnChangeRowClick: the caret changed, so the
        // right pane may now read a different per-file loader.
        RerenderView();
    }

    private static int ClampToSection(List<ChangeRef> rows, int index, ChangeSection section)
    {
        var first = rows.FindIndex(r => r.Section == section);
        if (first < 0) return index;
        return Math.Clamp(index, first, rows.FindLastIndex(r => r.Section == section));
    }

    /// <summary>Ctrl+Space: toggles the focused file in/out of the selection (may empty
    /// it entirely, like a Windows list).</summary>
    private void ToggleFocusedChangeSelection()
    {
        if (ChangeFocus() is not { } row) return;
        if (!SameSectionAsSelection(row)) _changeSelection.Clear();
        if (!_changeSelection.Add(row)) _changeSelection.Remove(row);
        _changeAnchor = row;
        MarkDirty();   // the caret didn't move, so no new loader can be activated
    }

    /// <summary>The files a row action acts on, in list order. Mirrors the History list's
    /// right-click rule: inside a multi-file selection the action takes the selection,
    /// outside it takes the clicked row — and a folder row is the files under it.</summary>
    private List<string> ChangeActionPaths(ChangeRef row)
    {
        var data = ChangesView;
        if (_changeSelection.Contains(row))
        {
            var selected = ChangeSelectionPaths(data);
            if (selected.Count > 1) return selected;
        }
        if (!row.IsFolder) return [row.Path];
        return FilesUnder(SectionFiles(data, row.Section), row.Path).Select(f => f.Path).ToList();
    }

    /// <summary>Every file the selection covers, in list order and named once each: a
    /// folder row stands for its files, and a range that swept up both a folder and the
    /// files under it must not stage any of them twice.</summary>
    private List<string> ChangeSelectionPaths(ChangesData data)
    {
        var paths = new List<string>();
        if (_changeSelection.Count == 0) return paths;
        foreach (var (section, files) in ChangeSections(data))
            foreach (var f in files)
                if (ChangeSelectionCovers(section, f.Path))
                    paths.Add(f.Path);
        return paths;
    }

    /// <summary>True when the selection holds this file's own row, or any folder row
    /// above it.</summary>
    private bool ChangeSelectionCovers(ChangeSection section, string path)
    {
        if (_changeSelection.Contains(new ChangeRef(path, section))) return true;
        for (var i = path.IndexOf('/'); i >= 0; i = path.IndexOf('/', i + 1))
            if (_changeSelection.Contains(new ChangeRef(path[..i], section, IsFolder: true)))
                return true;
        return false;
    }

    /// <summary>Moves selected rows (and the caret and anchor) into another section,
    /// so a selection keeps hold of its files as they move between the lists.</summary>
    private void RetargetChangeSelection(IReadOnlyList<string> paths, ChangeSection to)
    {
        var moved = new HashSet<string>(paths, StringComparer.Ordinal);
        // Read once: the view is derived (it applies the pending staging over git's answer),
        // so asking for it per row would rebuild it per row.
        var data = ChangesView;
        foreach (var r in _changeSelection.Where(Moved).ToList())
        {
            _changeSelection.Remove(r);
            _changeSelection.Add(r with { Section = to });
        }
        if (_changeAnchor is { } a && Moved(a)) _changeAnchor = a with { Section = to };
        if (_changeCaret is { } c && Moved(c)) _changeCaret = c with { Section = to };
        // The diff pane's file follows the move even when the caret sits on a folder
        // above it — otherwise the pane would keep reading the section it just left.
        if (_changeSelPath is { } p && moved.Contains(p))
        {
            _changeSelStaged = to == ChangeSection.Staged;
            _changeSelConflicted = to == ChangeSection.Conflicted;
        }
        return;

        // A folder row follows its files: it moves when everything under it did.
        bool Moved(ChangeRef r) =>
            !r.IsFolder
                ? moved.Contains(r.Path)
                : FilesUnder(SectionFiles(data, r.Section), r.Path) is { Count: > 0 } under
                  && under.All(f => moved.Contains(f.Path));
    }

    // ── Left column: file lists + composer ───────────────────────────────

    private Element BuildChangesColumn(ChangesData data, bool fill = false)
    {
        // When the diff pane is hidden the column fills the view; otherwise it's a
        // fixed-width sidebar next to the diff.
        var col = Flex.Column(gap: 0)
            .BorderColor(Nocturne.Divider).BorderWidth(new Thickness(0, 1, 0, 0));
        if (fill) col.FlexGrow(1);
        else col.Width(_changesSplit.Size).FlexShrink(0);

        // A half-finished operation gets a pinned banner (status + Abort / Skip /
        // Continue) above the list.
        if (data.Sequencer is { } seq)
        {
            var banner = BuildSequencerBanner(data, seq.State);
            banner.Key = "sequencer-banner";
            col.AddChild(banner);
        }

        col.AddChild(BuildChangesList(data));
        // The host takes the composer's place in the column: it never shrinks.
        col.AddChild(PlaceComposer(data).FlexShrink(0));
        return col;
    }

    // ── The list: a virtual list of item rows ───────────────────────────
    //
    // It was a scroll view of a column per section holding a row for every file and folder, so
    // every key built all of them again: 3 MB a key with 300 changed files, 3,065 elements resolved
    // and 2,883 patched. It is the Commit list's shape now: one virtual list of tagged rows, so only
    // the rows on screen are elements and a key makes the rows whose item changed: none, since the
    // list draws the caret and the pointer's tint (it made the two the caret left and reached, 2.2
    // a key at 300 files). A section's header is a row of the list, held at the top while its
    // section scrolls under it — Stage all / Unstage all stay in reach in a long list — until the
    // next section's header pushes it off (StickyRows), as Sticky() did in the columns. The rows
    // keep the heights they had (ChangeRowHeights), so it looks and scrolls as it did.

    private Element BuildChangesList(ChangesData data)
    {
        // How many files the current selection covers, counted once for the whole build: every
        // row in a selection of several labels itself with it ("Stage 3 files"). One row, the
        // selection of every key, is its own count: the walk over every file is for the others.
        _changeSelFileCount = _changeSelection.Count > 1 ? ChangeSelectionPaths(data).Count : _changeSelection.Count;
        SyncChangeListRows(data);

        // The list draws the caret: the tint under it says whether it is on a pick, a row of the
        // selection, or alone, which a plain row's hover tint matches so a keyboard walk stays
        // visible. A selection that is the caret's row alone, every plain key's, is that tint; the
        // rows of any other draw their own fill. So a key makes no row: the tint moves.
        var caret = ChangeFocus();
        _changeSelIsCaret = _changeSelection.Count == 1 && caret is { } c && _changeSelection.Contains(c);
        bool onPick = caret is { } k && _changeSelection.Contains(k);
        int caretIndex = caret is { } at ? ChangeListIndex(at) : -1;
        // On a row of a wider selection the row's own fill is the tint: drawn twice, its rounded
        // corners' edge pixels would come out a shade darker.
        var caretTint = !onPick ? Nocturne.Neutral900 : _changeSelIsCaret ? Nocturne.Accent900 : Color.Transparent;

        // Everything else a row shows is the colour scheme and the scale its glyphs are cut at.
        var shape = new ChangeListShape(Nocturne.Theme, Ui.DisplayScale);
        if (shape != _changeListShape)
        {
            _changeListShape = shape;
            _changeScrollRef.Current?.InvalidateRows();
        }

        var list = VirtualListElement<ChangeListItem>.New()
            .Items(new ChangeListItems(this, _changeListRows))
            .RenderWith((item, _) => ChangeListRow(item))
            .RowHeights(_changeRowHeight ??= (item, _) => ChangeRowHeight(item))
            .StickyRows(ChangeRowIsHeld)
            .SelectionHighlight(caretTint, Nocturne.RadiusRow)
            .HoverHighlight(Nocturne.Neutral900, Nocturne.RadiusRow, rows: ChangeRowTakesHover)
            .SelectedIndex(caretIndex);
        list.FlexGrow(1).Padding(new Thickness(8, 8, 4, 8));
        // The rows are what a reader walks — tree items for the folders, the buttons in them — in
        // a container that says it is a scroll view, as it did. A row off screen is not an
        // element, so a reader no longer finds it there (the Commit list's trade). A row the
        // caret can reach says it is selected or not (AccessibleSelected), and the list tells it
        // which: the caret's row is the selected one.
        list.RowsAreComposite = true;
        list.Role = AccessibilityRole.ScrollView;
        list.WithRef(_changeScrollRef);
        // Keyed, as the banner is, so the composer's kept host goes back where it was unmarked.
        list.Key = "change-list";
        return list;
    }

    private readonly record struct ChangeListShape(object Theme, float Scale);

    private ChangeListShape _changeListShape;
    private Func<ChangeListItem, int, float>? _changeRowHeight;

    /// <summary>What a row of the list is.</summary>
    private enum ChangeListKind : byte
    {
        ConflictsHeader, StagedHeader, ChangesHeader, HiddenHeader,
        Folder, File, Conflict, Hidden,
        /// <summary>"No changes".</summary>
        Empty,
    }

    /// <summary>
    /// One row of the list as its list sees it: what it is, and what its look takes from the rest
    /// of the view. A header carries its section's count and whether another section precedes it;
    /// a hidden file its path; a folder or a file row its node (a folder's totals with it), and
    /// whether it is an open folder, draws a pick's fill (a row of a selection that is not the
    /// caret's row alone; the list draws the caret), and how many files a row action takes while it
    /// is part of a selection of several. A key that moves the caret changes no item.
    /// <para>The node is compared by what a row shows of it and acts on (its path), not by which
    /// object it is: a stage or an unstage makes every node again, and a row whose file, place and
    /// counts are the same is the same row, which the list moves to where its item now stands
    /// instead of making it again. No index is in it, for the same reason.</para>
    /// </summary>
    private readonly record struct ChangeListItem(ChangeListKind Kind, ChangeSection Section = default,
        TreeNode<GitFileChange>? Node = null, string? Path = null, int Count = 0, bool TopGap = false,
        bool Expanded = false, bool Selected = false, int SelectedFiles = 0, int Additions = 0, int Deletions = 0)
    {
        /// <summary>How many files a row action targets: every file the selection covers when the
        /// row is inside a selection of several, otherwise the row's own.</summary>
        public int ActionCount(int own) => SelectedFiles > 0 ? SelectedFiles : own;

        public bool Equals(ChangeListItem other) =>
            Kind == other.Kind && Section == other.Section && Path == other.Path && Count == other.Count
            && TopGap == other.TopGap && Expanded == other.Expanded && Selected == other.Selected
            && SelectedFiles == other.SelectedFiles && Additions == other.Additions && Deletions == other.Deletions
            && SameRow(Node, other.Node);

        public override int GetHashCode() => HashCode.Combine(Kind, Section, Node?.Path, Path, Count);

        private static bool SameRow(TreeNode<GitFileChange>? a, TreeNode<GitFileChange>? b) =>
            ReferenceEquals(a, b)
            || a is not null && b is not null && a.IsFolder == b.IsFolder && a.Depth == b.Depth
               && a.LeafCount == b.LeafCount && a.Path == b.Path && a.Segment == b.Segment
               && EqualityComparer<GitFileChange>.Default.Equals(a.Item, b.Item);
    }

    private static readonly Func<ChangeListItem, int, bool> ChangeRowIsHeld = static (item, _) =>
        item.Kind is ChangeListKind.ConflictsHeader or ChangeListKind.StagedHeader or ChangeListKind.ChangesHeader
            or ChangeListKind.HiddenHeader;

    /// <summary>The rows the pointer tints: the ones that tinted themselves, not a header or "No changes",
    /// nor a pick, whose fill covers the tint but for its rounded corners' edge pixels.</summary>
    private static readonly Func<ChangeListItem, int, bool> ChangeRowTakesHover = static (item, _) =>
        !item.Selected
        && item.Kind is ChangeListKind.Folder or ChangeListKind.File or ChangeListKind.Conflict or ChangeListKind.Hidden;

    /// <summary>The selection is the caret's row alone, as every plain key leaves it: the list draws
    /// it, and no row draws a pick's fill. Read as the build reads the selection.</summary>
    private bool _changeSelIsCaret;

    /// <summary>The rows as the list's items, each made when the list asks for it, the selection
    /// read then.</summary>
    private sealed class ChangeListItems(JunctionApp app, List<ChangeListItem> rows) : IReadOnlyList<ChangeListItem>
    {
        public int Count => rows.Count;

        public ChangeListItem this[int index]
        {
            get
            {
                var row = rows[index];
                if (row.Node is not { } node) return row;
                if (app._changeSelection.Count == 0) return row;
                var self = new ChangeRef(node.Path, row.Section, node.IsFolder);
                bool selected = app._changeSelection.Contains(self);
                return row with
                {
                    Selected = selected && !app._changeSelIsCaret,
                    SelectedFiles = selected && app._changeSelFileCount > 1 ? app._changeSelFileCount : 0,
                };
            }
        }

        public IEnumerator<ChangeListItem> GetEnumerator()
        {
            for (var i = 0; i < Count; i++) yield return this[i];
        }

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }

    // The rows, kept while the lists and the open folders are what they were: flattened again on
    // every key, they were a list of every row and a walk of each tree a key. The walk is the
    // folder and file rows alone, which is what the keyboard and a Shift range index into.
    private readonly record struct ChangeListKey(object? Conflicted, object? Staged, object? Unstaged,
        object? Hidden, bool InSequencer, int TreeGen);

    private ChangeListKey _changeListKey;
    private List<ChangeListItem> _changeListRows = [];
    private List<ChangeRef> _changeWalk = [];
    private Dictionary<ChangeRef, int>? _changeRowIndex;
    private int _changeTreeGen;

    private void SyncChangeListRows(ChangesData data)
    {
        var key = new ChangeListKey(data.Conflicted, data.Staged, data.Unstaged, data.AssumeUnchanged,
            data.InSequencer, _changeTreeGen);
        if (key == _changeListKey) return;
        _changeListKey = key;

        // New lists: the items a mounted list holds go on reading the rows they were made over.
        var rows = _changeListRows = [];
        var walk = _changeWalk = [];
        _changeRowIndex = null;

        // Conflicts first — selecting one opens its resolution pane on the right.
        if (data.Conflicted.Count > 0)
        {
            rows.Add(new ChangeListItem(ChangeListKind.ConflictsHeader, Count: data.Conflicted.Count));
            AddChangeRows(ChangeSection.Conflicted, data.Conflicted);
        }

        if (data.Staged.Count == 0 && data.Unstaged.Count == 0)
        {
            if (!data.InSequencer && data.Conflicted.Count == 0)
                rows.Add(new ChangeListItem(ChangeListKind.Empty));
        }
        else
        {
            rows.Add(new ChangeListItem(ChangeListKind.StagedHeader, Count: data.Staged.Count, TopGap: data.Conflicted.Count > 0));
            AddChangeRows(ChangeSection.Staged, data.Staged);
            rows.Add(new ChangeListItem(ChangeListKind.ChangesHeader, Count: data.Unstaged.Count, TopGap: true));
            AddChangeRows(ChangeSection.Unstaged, data.Unstaged);
        }

        // Assume-unchanged files are hidden from status, so surface them here with a
        // way to start watching them again (otherwise the flag is a trap — set once,
        // never seen again).
        if (data.AssumeUnchanged.Count > 0)
        {
            rows.Add(new ChangeListItem(ChangeListKind.HiddenHeader, Count: data.AssumeUnchanged.Count,
                TopGap: data.Staged.Count > 0 || data.Unstaged.Count > 0 || data.Conflicted.Count > 0));
            foreach (var path in data.AssumeUnchanged)
                rows.Add(new ChangeListItem(ChangeListKind.Hidden, Path: path));
        }
        return;

        // One section's tree: folder rows and file rows in one flat run.
        void AddChangeRows(ChangeSection section, IReadOnlyList<GitFileChange> files)
        {
            var tree = ChangeTreeFor(section);
            foreach (var node in ChangeTreeRows(section, files))
            {
                var kind = node.IsFolder ? ChangeListKind.Folder
                    : section == ChangeSection.Conflicted ? ChangeListKind.Conflict
                    : ChangeListKind.File;
                var (additions, deletions) = node.IsFolder ? FolderTotals(node) : default;
                rows.Add(new ChangeListItem(kind, section, node, Expanded: node.IsFolder && tree.IsExpanded(node),
                    Additions: additions, Deletions: deletions));
                walk.Add(new ChangeRef(node.Path, section, node.IsFolder));
            }
        }
    }

    /// <summary>Where a row is among the list's rows, or -1.</summary>
    private int ChangeListIndex(ChangeRef row)
    {
        if (_changeRowIndex is null)
        {
            _changeRowIndex = new Dictionary<ChangeRef, int>(_changeWalk.Count);
            for (var i = 0; i < _changeListRows.Count; i++)
                if (_changeListRows[i].Node is { } node)
                    _changeRowIndex[new ChangeRef(node.Path, _changeListRows[i].Section, node.IsFolder)] = i;
        }
        return _changeRowIndex.TryGetValue(row, out var index) ? index : -1;
    }

    /// <summary>Brings the caret's row on screen. The list may not have realized it, so its band
    /// comes from the row heights, summed in the order the list sums them.</summary>
    private void ScrollChangeCaretIntoView()
    {
        if (_changeScrollRef.Current is not { } list || ChangeFocus() is not { } caret) return;
        int index = ChangeListIndex(caret);
        if (index < 0) return;
        float top = 0f;
        for (int i = 0; i < index; i++) top += ChangeRowHeight(_changeListRows[i]);
        list.ScrollRangeIntoView(top, ChangeRowHeight(_changeListRows[index]));
    }

    /// <summary>Rows the list has made, for tests.</summary>
    internal int ChangeRowsMade { get; private set; }

    private Element ChangeListRow(ChangeListItem item)
    {
        ChangeRowsMade++;
        switch (item.Kind)
        {
            case ChangeListKind.ConflictsHeader:
                return SectionHeader("conflicts", $"Conflicts · {item.Count}", Nocturne.DangerText, "", null, item.TopGap);
            case ChangeListKind.StagedHeader:
                return SectionHeader("staged", $"Staged · {item.Count}", Nocturne.Accent300, "Unstage all",
                    item.Count > 0 ? UnstageAll : null, item.TopGap);
            case ChangeListKind.ChangesHeader:
                return SectionHeader("changes", $"Changes · {item.Count}", Nocturne.On(0.42), "Stage all",
                    item.Count > 0 ? StageAll : null, item.TopGap);
            case ChangeListKind.HiddenHeader:
                return SectionHeader("hidden", $"Hidden · {item.Count}", Nocturne.On(0.42), "", null, item.TopGap);
            case ChangeListKind.Folder:
                return ChangesFolderRow(item);
            case ChangeListKind.File:
                return ChangeRow(item);
            case ChangeListKind.Conflict:
                return ConflictListRow(item);
            case ChangeListKind.Hidden:
                return HiddenFileRow(item.Path!);
            default:
                return Box.New().Padding(new Thickness(26)).JustifyContent(JustifyContent.Center)
                    .AlignItems(AlignItems.Center)
                    .Children(Text.New("No changes").FontSize(13f).TextColor(Nocturne.On(0.45)));
        }
    }

    // The heights the rows had in the scroll view, which a virtual list has to be told: each is
    // its padding and its tallest child, measured from the faces as layout measures a line, once
    // per display scale.
    private readonly record struct ChangeRowSizes(float Scale, float Header, float Action, float Folder,
        float ConflictFolder, float File, float Conflict, float Hidden, float HiddenDirectory, float Empty);

    private ChangeRowSizes _changeRowSizes;

    private float ChangeRowHeight(ChangeListItem item)
    {
        var h = ChangeRowHeights();
        switch (item.Kind)
        {
            case ChangeListKind.ConflictsHeader or ChangeListKind.HiddenHeader:
                return (item.TopGap ? 14f : 7f) + 5f + h.Header;
            case ChangeListKind.StagedHeader or ChangeListKind.ChangesHeader:
                return (item.TopGap ? 14f : 7f) + 5f + (item.Count > 0 ? h.Action : h.Header);
            case ChangeListKind.Folder:
                return item.Section == ChangeSection.Conflicted ? h.ConflictFolder : h.Folder;
            case ChangeListKind.File:
                return h.File;
            case ChangeListKind.Conflict:
                return h.Conflict;
            case ChangeListKind.Hidden:
                return System.IO.Path.GetFileName(item.Path!).Length < item.Path!.Length ? h.HiddenDirectory : h.Hidden;
            default:
                return h.Empty;
        }
    }

    private ChangeRowSizes ChangeRowHeights()
    {
        float scale = Ui.DisplayScale;
        if (_changeRowSizes.File > 0 && _changeRowSizes.Scale == scale) return _changeRowSizes;
        const float RowPadding = 12f, IconButton = 28f, TagPadding = 6f, GhostChrome = 8f, EmptyPadding = 52f;
        float file = MathF.Max(MathF.Max(LineHeight(12f), LineHeight(11f, Fonts.Mono, FontWeight.Bold)), Ui.IconBox(14f));
        // No measure context yet (nothing has been drawn): what the default face gives, until a
        // later build measures.
        if (file <= 0)
            return new ChangeRowSizes(scale, 13.3f, 22.6f, 27.3f, 27.3f, 28f, 30.6f, 40f, 40.6f, 69.3f);
        float folder = MathF.Max(LineHeight(11.5f, weight: FontWeight.Medium), LineHeight(10f));
        return _changeRowSizes = new ChangeRowSizes(scale,
            Header: LineHeight(10f),
            Action: MathF.Max(LineHeight(10f), GhostChrome + LineHeight(11f)),
            Folder: RowPadding + MathF.Max(folder, Ui.IconBox(14f)),
            ConflictFolder: RowPadding + MathF.Max(folder, Ui.IconBox(12f)),
            File: RowPadding + file,
            Conflict: RowPadding + MathF.Max(LineHeight(12f), MathF.Max(Ui.IconBox(12f), TagPadding + LineHeight(9.5f))),
            Hidden: RowPadding + MathF.Max(IconButton, LineHeight(12f)),
            HiddenDirectory: RowPadding + MathF.Max(IconButton, LineHeight(12f) + LineHeight(9.5f)),
            Empty: EmptyPadding + LineHeight(13f));
    }

    private Element SectionHeader(string section, string label, Color labelColor, string action, Action? onAction, bool topGap = false)
    {
        // Held at the top of the list while its section scrolls under it, on the list's own
        // fill so the rows going by do not show through. Keyed, so a new count patches the row
        // on screen: its button holds the focus of whoever is staging from the keyboard.
        var row = Flex.Row(gap: 6).AlignItems(AlignItems.Center)
            .Padding(new Thickness(topGap ? 14 : 7, 8, 5, 8))
            .Background(Nocturne.Bg);
        row.Key = section;
        row.AddChild(Text.New(label.ToUpperInvariant()).FontSize(10f).TextColor(labelColor).FlexGrow(1));
        if (onAction != null)
            row.AddChild(Ui.GhostButton(action, null, onAction, 11f));
        return row;
    }

    /// <summary>A folder row in one of the three lists. It selects, stages and unstages as
    /// everything below it — one checkbox for a directory is the shortcut the flat list
    /// could never offer — and its counts are the totals of the files it hides.</summary>
    private Element ChangesFolderRow(ChangeListItem item)
    {
        var node = item.Node!;
        var section = item.Section;
        var tree = ChangeTreeFor(section);
        var self = new ChangeRef(node.Path, section, IsFolder: true);
        var (selected, expanded) = (item.Selected, item.Expanded);
        var n = item.ActionCount(node.LeafCount);
        var (additions, deletions) = (item.Additions, item.Deletions);

        // Conflicts have no staging checkbox — their resolution is per file — so that
        // section's folders carry the twisty alone.
        Element? check = null;
        if (section != ChangeSection.Conflicted)
        {
            var isStaged = section == ChangeSection.Staged;
            check = Box.New().FlexShrink(0).Cursor(CursorType.Pointer)
                .Tooltip(isStaged ? $"Unstage {n} file{(n == 1 ? "" : "s")}"
                                  : $"Stage {n} file{(n == 1 ? "" : "s")}")
                .Children(Ui.Icon(isStaged ? Icons.CheckSquare : Icons.Square, 14f,
                    isStaged ? Nocturne.Accent : Nocturne.On(0.38)));
            check.OnClick(e =>
            {
                e.StopPropagation = true;
                var paths = ChangeActionPaths(self);
                if (isStaged) UnstagePaths(paths); else StagePaths(paths);
            });
        }

        var row = ChangeFolderRow(node, expanded, lead: check, trail: ChangeCounts(additions, deletions));
        // A pick and the open state change how the row looks, and a collapse moves every row
        // below it — see ChangeRow for why a patched row would otherwise keep the previous
        // occupant's handlers. The caret's tint and the pointer's are the list's.
        row.Key = $"{section}:dir:{node.Path}{(selected ? "#s" : "")}{(expanded ? "#o" : "")}";
        if (selected) row.Background(Nocturne.Accent900);
        row.AccessibleSelected = false;

        row.OnClick(e =>
        {
            OnChangeRowClick(self, e.Modifiers);
            // A plain click on a folder opens or closes it, the way every tree behaves.
            // A modifier click is building a selection and must leave the shape alone —
            // collapsing under a Shift range would take rows out from under it.
            if (e.Modifiers == KeyModifiers.None) tree.Toggle(node);
        });
        row.On<PointerDownEvent>(e =>
        {
            if (e.Button == 2 && !_changeSelection.Contains(self))
            {
                SetChangeSelection(self);
                RerenderView();
            }
        });
        row.ContextMenu(() => ChangeFolderMenuItems(section, node));
        return row;
    }

    private Element ChangeRow(ChangeListItem item)
    {
        var node = item.Node!;
        var f = node.Item;
        bool isStaged = item.Section == ChangeSection.Staged;
        var self = new ChangeRef(f.Path, item.Section);
        // A pick's fill; the focus caret's tint (a pick's, or a hover-strength one off the
        // selection, so keyboard navigation stays visible) is the list's.
        var selected = item.Selected;
        var n = item.ActionCount(1);
        var (letter, color) = StatusLetter(f.Kind);

        // Checkbox · status letter · name · counts. The letter slot is a fixed track so
        // the names start on one column whatever the letter's width.
        var row = Grid.Of(ChangeRowColumns, gap: 8).AlignItems(AlignItems.Center)
            .Padding(ChangeRowPadding(node.Depth)).CornerRadius(Nocturne.RadiusRow)
            .Cursor(CursorType.Pointer).Tooltip(f.Path);
        // A pick in the key so a change of it gives a fresh element (see FileTreeRow).
        row.Key = (isStaged ? "s:" : "u:") + f.Path + (selected ? "#s" : "");
        if (selected) row.Background(Nocturne.Accent900);
        row.AccessibleSelected = false;

        // Checkbox toggles staging without moving the selection — and acts on the whole
        // selection when this row is part of one, the same set the context menu takes.
        var check = Box.New().GridColumn(0).Cursor(CursorType.Pointer)
            .Tooltip(n > 1 ? (isStaged ? $"Unstage {n} files" : $"Stage {n} files")
                           : isStaged ? "Unstage" : "Stage")
            .Children(Ui.Icon(isStaged ? Icons.CheckSquare : Icons.Square, 14f,
                isStaged ? Nocturne.Accent : Nocturne.On(0.38)));
        // Recomputed at click time, not captured: the row keeps its key while another
        // row joins or leaves the selection, and a patched element keeps this handler.
        check.OnClick(e =>
        {
            e.StopPropagation = true;
            var paths = ChangeActionPaths(self);
            if (isStaged) UnstagePaths(paths); else StagePaths(paths);
        });
        row.AddChild(check);

        row.AddChild(Box.New().GridColumn(1)
            .Children(Ui.Mono(letter, 11f, color).FontWeight(FontWeight.Bold)));

        // The name alone: the folder row above it carries the directory.
        row.AddChild(Text.New(node.Segment).FontSize(12f).GridColumn(2)
            .OverflowMode(TextOverflow.Ellipsis).MaxLines(1));

        if (ChangeCounts(f.Additions, f.Deletions) is { } counts)
            row.AddChild(counts.GridColumn(3));

        row.OnClick(e => OnChangeRowClick(self, e.Modifiers));
        // Right-click outside the selection collapses to that row first, then the
        // framework opens the menu; inside it the selection is left alone.
        row.On<PointerDownEvent>(e =>
        {
            if (e.Button == 2 && !_changeSelection.Contains(self))
            {
                SetChangeSelection(self);
                RerenderView();
            }
        });
        row.ContextMenu(() => ChangeFileMenuItems(f, isStaged));
        return row;
    }

    // A row in the "Hidden" (assume-unchanged) section: the path plus a button to
    // start watching it again.
    private Element HiddenFileRow(string path)
    {
        var name = System.IO.Path.GetFileName(path);
        var dir = path.Length > name.Length ? path[..^name.Length] : "";
        var row = Flex.Row(gap: 8).AlignItems(AlignItems.Center)
            .Padding(new Thickness(6, 8, 6, 8)).CornerRadius(Nocturne.RadiusRow).Tooltip(path);
        row.Key = "hidden:" + path;
        row.AddChild(Ui.Icon(Icons.EyeSlash, 13f, Nocturne.On(0.4)).FlexShrink(0));

        var text = Flex.Column(gap: 0).FlexGrow(1);
        text.AddChild(Text.New(name).FontSize(12f).TextColor(Nocturne.On(0.6))
            .OverflowMode(TextOverflow.Ellipsis).MaxLines(1));
        if (dir.Length > 0)
            text.AddChild(Text.New(dir).FontSize(9.5f).TextColor(Nocturne.On(0.35))
                .OverflowMode(TextOverflow.Ellipsis).MaxLines(1));
        row.AddChild(text);

        var watch = Ui.IconButton(Icons.Eye, () => SetAssumeUnchanged(path, false), "Watch again", 28f);
        watch.OnClick(e => e.StopPropagation = true);
        row.AddChild(watch.FlexShrink(0));
        // Its hover tint is the list's, with the rows'.
        return row;
    }

    // ── File context menu ────────────────────────────────────────────────

    private IReadOnlyList<ContextMenuItem> ChangeFileMenuItems(GitFileChange f, bool isStaged)
    {
        // Right-clicking inside a multi-selection targets the whole selection for the
        // operations that generalize (stage / unstage / discard); the rest act on the
        // clicked file, which is the one the diff pane is showing.
        var self = new ChangeRef(f.Path, isStaged ? ChangeSection.Staged : ChangeSection.Unstaged);
        var sel = ChangeActionPaths(self);
        var n = sel.Count;

        var items = new List<ContextMenuItem>();
        if (isStaged)
        {
            items.Add(Item(Icons.MinusCircle, n > 1 ? $"Unstage {n} files" : "Unstage",
                () => UnstagePaths(sel)));
            items.Add(Item(Icons.Square, "Unstage all", UnstageAll));
        }
        else
        {
            items.Add(Item(Icons.PlusCircle, n > 1 ? $"Stage {n} files" : "Stage",
                () => StagePaths(sel)));
            items.Add(Item(Icons.CheckSquare, "Stage all", StageAll));
        }
        items.Add(ContextMenuItem.Separator());
        // The file itself, with whatever the desktop opens it with. Selecting first so the
        // keyboard shortcut and the menu act on the same file — right-clicking a row the
        // caret is not on would otherwise open the previously selected one.
        items.Add(Item(Icons.RocketLaunch, "Open file", () =>
        {
            SelectChange(f.Path, isStaged);
            LaunchFilesPath(f.Path);
        }, Shortcuts.Keys("diff.open")));
        // Named for the editor Options sets, as the diff header's button is.
        items.Add(Item(Icons.FileCode, $"Open diff in {EditorName(_settings.ExternalTools)}", () =>
        {
            SelectChange(f.Path, isStaged);
            OpenSelectedDiffExternally();
        }, Shortcuts.Keys("diff.editor")));
        items.Add(Item(Icons.Copy, "Copy path", () => CopyToClipboard(f.Path)));
        if (_settings.ExternalTools.CustomCommand.Trim().Length > 0)
            items.Add(Item(Icons.TerminalWindow, "Run custom command",
                () => RunCustomCommandOn(System.IO.Path.Combine(_activeRepoPath, f.Path.Replace('/', System.IO.Path.DirectorySeparatorChar)), null)));

        // Ignore / assume-unchanged apply to the worktree (unstaged) list.
        if (!isStaged)
        {
            items.Add(ContextMenuItem.Separator());
            if (f.Kind == FileChangeKind.Untracked)
            {
                // Ignoring only untracks files that git isn't already tracking.
                items.Add(Item(Icons.EyeSlash, "Add to .gitignore", () => IgnorePath(f.Path)));
                var ext = System.IO.Path.GetExtension(f.Path);
                if (ext.Length > 1)
                    items.Add(Item(Icons.EyeSlash, $"Ignore all *{ext}", () => IgnorePattern("*" + ext)));
            }
            else
            {
                // Tracked file: assume-unchanged hides future edits from status.
                items.Add(Item(Icons.EyeSlash, "Assume unchanged (hide from status)",
                    () => SetAssumeUnchanged(f.Path, true)));
            }
        }

        // Discard only from the worktree (unstaged) list, where `git restore`
        // / `git clean` are well-defined; staged rows should be unstaged first.
        if (!isStaged)
        {
            items.Add(ContextMenuItem.Separator());
            items.Add(Danger(Icons.CounterClockwise,
                n > 1 ? $"Discard changes in {n} files" : "Discard changes",
                () => PromptDiscardPaths(sel)));
        }
        return items;
    }

    /// <summary>A folder row's menu: the tree commands first, then the same staging
    /// actions its files offer, aimed at everything below it.</summary>
    private IReadOnlyList<ContextMenuItem> ChangeFolderMenuItems(ChangeSection section, TreeNode<GitFileChange> node)
    {
        var tree = ChangeTreeFor(section);
        var expanded = tree.IsExpanded(node);
        var self = new ChangeRef(node.Path, section, IsFolder: true);
        var sel = ChangeActionPaths(self);
        var n = sel.Count;

        var items = new List<ContextMenuItem>
        {
            Item(expanded ? Icons.CaretRight : Icons.CaretDown, expanded ? "Collapse" : "Expand",
                () => tree.Toggle(node)),
            Item(Icons.FolderOpen, "Expand everything below", () => tree.SetSubtreeExpanded(node, true)),
            Item(Icons.Folders, "Collapse everything below", () => tree.SetSubtreeExpanded(node, false)),
            ContextMenuItem.Separator(),
        };
        if (section == ChangeSection.Staged)
            items.Add(Item(Icons.MinusCircle, $"Unstage {n} file{(n == 1 ? "" : "s")}", () => UnstagePaths(sel)));
        else if (section == ChangeSection.Unstaged)
            items.Add(Item(Icons.PlusCircle, $"Stage {n} file{(n == 1 ? "" : "s")}", () => StagePaths(sel)));
        items.Add(Item(Icons.Copy, "Copy folder path", () => CopyToClipboard(node.Path)));

        if (section == ChangeSection.Unstaged)
        {
            items.Add(ContextMenuItem.Separator());
            items.Add(Danger(Icons.CounterClockwise, $"Discard changes in {n} file{(n == 1 ? "" : "s")}",
                () => PromptDiscardPaths(sel)));
        }
        return items;
    }

    /// <summary>Discard is the one action here that destroys work `git` cannot give
    /// back, so it confirms first and names how much is going — a mis-aimed discard of
    /// a whole multi-selection is a far worse accident than a mis-aimed stage.</summary>
    private void PromptDiscardPaths(IReadOnlyList<string> paths)
    {
        var wanted = new HashSet<string>(paths, StringComparer.Ordinal);
        var files = ChangesView.Unstaged.Where(f => wanted.Contains(f.Path)).ToList();
        if (files.Count == 0) return;

        var untracked = files.Count(f => f.Kind == FileChangeKind.Untracked);
        var body = files.Count == 1
            ? $"Discards the working-tree changes in {System.IO.Path.GetFileName(files[0].Path)}."
            : $"Discards the working-tree changes in {files.Count} files.";
        if (untracked > 0)
            body += untracked == 1
                ? " One of them is untracked and is deleted from disk."
                : $" {untracked} of them are untracked and are deleted from disk.";
        // Options › General can turn the question off; the discard itself is the same.
        if (!_settings.General.ConfirmDestructive) { DiscardChanges(files); return; }
        OpenConfirm("Discard changes", body + " This cannot be undone.",
            files.Count == 1 ? "Discard" : $"Discard {files.Count} files",
            () => DiscardChanges(files));
    }

    private void DiscardChanges(IReadOnlyList<GitFileChange> files)
    {
        if (ActiveRepo is not { } repo) return;
        var ok = repo.DiscardChanges(files);
        Flash(ok
            ? files.Count == 1
                ? $"Discarded {System.IO.Path.GetFileName(files[0].Path)}"
                : $"Discarded {files.Count} files"
            : "Discard failed");
        // The rows are gone (restored or deleted), so nothing survives them: clear the
        // selection and let the reload pick the next file.
        SetChangeSelection(null);
        RefreshAll();
    }

    // ── Ignore / assume-unchanged actions ─────────────────────────────────

    private void IgnorePath(string path)
    {
        if (ActiveRepo is not { } repo) return;
        Flash(repo.AddToGitignore(path) ? $"Ignored {System.IO.Path.GetFileName(path)}" : "Couldn't write .gitignore");
        RefreshAll();
    }

    private void IgnorePattern(string pattern)
    {
        if (ActiveRepo is not { } repo) return;
        Flash(repo.AddToGitignore(pattern) ? $"Ignoring {pattern}" : "Couldn't write .gitignore");
        RefreshAll();
    }

    private void SetAssumeUnchanged(string path, bool assume)
    {
        if (ActiveRepo is not { } repo) return;
        var res = repo.SetAssumeUnchanged(path, assume);
        Flash(res.Success
            ? (assume ? $"Hiding changes to {System.IO.Path.GetFileName(path)}" : $"Watching {System.IO.Path.GetFileName(path)} again")
            : FirstLine(res.Stderr, "update-index failed"));
        RefreshAll();
    }

    // ── Composer ─────────────────────────────────────────────────────────
    //
    // A child component, as the chrome is (see JunctionApp.Chrome.cs): a character in the summary built the
    // whole view, every row of the lists with it, for fields that hold their own text. 76 KB a
    // key with one changed file, 1.3 MB with 200. Nothing outside the composer shows what is typed,
    // so a character builds the composer alone (_composerRevision), and only when the issue list or
    // the closing hint moves (ComposerTyped): the fields hold their own text. It builds with the view when
    // a full rebuild asked (_chromeRevision, safe by default) or when what it shows of the
    // working copy moved, which the view's build compares (Place); a view-only build (a row
    // selected, a splitter dragged) leaves it as it is. ComposerPartTests holds it to what a full
    // build of it makes.

    private readonly HultUI.Core.Reactivity.Signal<int> _composerRevision = new(0);
    private readonly HultUI.Core.Components.PartGroup _composerParts = new();
    private HultUI.Core.Components.Part? _composerPart;
    private ComposerCounts _composerCounts, _composerBuiltCounts;
    private int _composerBuiltRevision = -1;

    /// <summary>Builds of the composer so far, for tests.</summary>
    internal int ComposerBuilds => _composerParts.Builds;

    /// <summary>All the composer shows of the working copy: what the commit button says it will
    /// do, and the message Amend pours in.</summary>
    private readonly record struct ComposerCounts(int Staged, int Unstaged, int Conflicted, string? HeadMessage);

    /// <summary>The composer's host, for the view's build to place: built again first when a full
    /// rebuild came since it last built, or what it shows of <paramref name="data"/> moved.</summary>
    private HultUI.Core.Components.ComponentHost PlaceComposer(ChangesData data)
    {
        _composerCounts = new ComposerCounts(data.Staged.Count, data.Unstaged.Count, data.Conflicted.Count, data.HeadMessage);
        var part = _composerPart ??= new(_composerParts, () =>
        {
            _composerBuiltCounts = _composerCounts;
            _composerBuiltRevision = _chromeRevision.Peek();
            var built = BuildComposer(_composerCounts);
            _composerBuiltText = ComposerTextShown();   // after the build, which clamps the list's row
            return built;
        }, own: _composerRevision);
        part.Mount();   // the first build, before asking what it was built from
        return part.Place(stale: _composerCounts != _composerBuiltCounts || _composerBuiltRevision != _chromeRevision.Peek());
    }

    /// <summary>All the composer shows of the text typed into it, on GitLab: the issue list's
    /// token and highlighted row, and the closing hint's reference. Off GitLab it shows nothing of
    /// it (the default). The fields hold their own text.</summary>
    private readonly record struct ComposerText((int Start, string Query)? Token, int Row, string? Closing);

    private ComposerText _composerBuiltText;

    private ComposerText ComposerTextShown()
    {
        // Both the token and a closing reference have a '#', and most messages have none: the
        // common character asks nothing more, not even whether the repository is on GitLab.
        bool inSummary = _commitSummary.Contains('#'), inBody = _commitDescription.Contains('#');
        if (!(inSummary || inBody) || !GitLabActive(_activeRepoPath)) return default;
        var text = _issueInSummary ? _commitSummary : _commitDescription;
        var token = (_issueInSummary ? inSummary : inBody) ? IssueTokenAt(text, _issueCaret) : null;
        string? closing = null;
        if (inSummary && ClosingRef.Match(_commitSummary) is { Success: true } s) closing = s.Value;
        else if (inBody && ClosingRef.Match(_commitDescription) is { Success: true } b) closing = b.Value;
        return new ComposerText(token, token is null ? 0 : _issueSel, closing);
    }

    /// <summary>A character in one of the composer's fields. The fields hold their own text, so
    /// the composer builds again, alone, only when what it shows of the text moves: the issue
    /// list or the closing hint (<see cref="ComposerText"/>). It built on every character
    /// (13.5 KB a character off GitLab, 15.4 on it with no issue being typed).</summary>
    private void ComposerTyped(bool summary, int caret)
    {
        NoteComposerTyping(summary, caret);
        if (ComposerTextShown() != _composerBuiltText) _composerRevision.Value++;
    }

    private Element BuildComposer(ComposerCounts counts)
    {
        int stagedCount = counts.Staged;
        // Nothing staged but plenty changed is the ordinary case for someone who never
        // stages, so the commit button and Ctrl+Enter take the whole worktree rather than
        // refusing. Conflicts are the exception: staging one is how you declare it resolved,
        // and doing that to a file nobody looked at is not something a commit should decide.
        bool commitAll = stagedCount == 0 && counts.Unstaged > 0 && counts.Conflicted == 0;

        var box = Flex.Column(gap: 8).FlexShrink(0)
            .Padding(new Thickness(12))
            .BorderColor(Nocturne.Divider).BorderWidth(new Thickness(1, 0, 0, 0));

        // Ctrl/Cmd+Enter commits from either the summary or description field.
        // Handled in the capture phase (on the composer, an ancestor of both
        // fields) so it fires before the text area's own Enter handler inserts
        // a newline.
        box.On<KeyDownEvent>(EventPhase.Capture, e =>
        {
            var mod = e.Modifiers.HasFlag(KeyModifiers.Ctrl) || e.Modifiers.HasFlag(KeyModifiers.Super);
            if (mod && e.Key == KeyCode.Enter)
            {
                e.StopPropagation = true;
                DoCommit();
                return;
            }
            // While the issue list is open its keys come first — Enter picks rather than
            // inserting a newline, Esc closes the list before it does anything else.
            if (HandleIssueSuggestionKey(e)) e.StopPropagation = true;
        });

        // The open issues a typed "#" offers, above the field it is typed in — see
        // JunctionApp.IssueAutocomplete. Absent on a repository that is not on GitLab.
        if (BuildIssueSuggestions() is { } suggestions)
        {
            suggestions.Key = "composer-issues";
            box.AddChild(suggestions);
        }

        // Summary
        var summary = TextInput.New(_commitSummary).Style(Ui.Field());
        summary.PlaceholderText("Summary (required)")
            .OnTextChanged(q => { _commitSummary = q; ComposerTyped(summary: true, _summaryRef.Current?.CursorPosition ?? q.Length); })
            .Height(Dimension.Px(34)).FlexShrink(0)
            .TextColor(Nocturne.Text).WithRef(_summaryRef);
        // Every child of the composer is keyed, and that is all-or-nothing: a keyed child
        // among unkeyed siblings makes the reconciler match the whole list by key, and the
        // unkeyed ones are then *replaced* rather than patched — which threw away the field
        // you were typing in after the first character.
        summary.Key = "composer-summary";
        box.AddChild(summary);

        // Description
        // The wrapper's 6px and the theme's 6px of vertical inset, and its 56px of lines.
        var desc = TextArea.New(_commitDescription).Style(Ui.Field(verticalPadding: 12f));
        desc.PlaceholderText("Description")
            .OnTextChanged(q => { _commitDescription = q; ComposerTyped(summary: false, _descRef.Current?.CursorPosition ?? q.Length); })
            .Height(Dimension.Px(82))
            .TextColor(Nocturne.Text).WithRef(_descRef);
        desc.Key = "composer-description";
        box.AddChild(desc);

        // "Closes #1042 will close the issue when this reaches main." — one line, only when
        // the message says so and the repository is on GitLab.
        if (BuildClosingHint() is { } hint)
        {
            hint.Key = "composer-closing-hint";
            box.AddChild(hint);
        }

        // Amend, then sign. Amend is offered only where there is a commit to rewrite — a
        // repository with no commits yet has nothing behind HEAD, and `git commit --amend`
        // there fails with a message about an unborn branch.
        if (counts.HeadMessage is { Length: > 0 })
        {
            var amend = Checkbox.New(_amendCommit, "Amend last commit")
                .OnCheckedChanged(v => ToggleAmend(v, counts.HeadMessage));
            amend.LabelGap = 8f;
            amend.TextColor(Nocturne.On(0.7)).FontSize(11.5f);
            // Checked and unchecked are different rows to the reconciler; without this the
            // patched checkbox keeps the previous handler and its captured HEAD message.
            box.AddChild(amend);
        }

        // Sign toggle: bound to the persisted default; flipping it sticks.
        var sign = Checkbox.New(_settings.Git.SignCommits, "Sign commit (GPG/SSH)")
            .OnCheckedChanged(v => { _settings.Git.SignCommits = v; Persist(); MarkDirty(); });
        sign.LabelGap = 8f;
        sign.TextColor(Nocturne.On(0.7)).FontSize(11.5f);
        sign.Key = "composer-sign";
        box.AddChild(sign);

        // The button says what the click will actually do, including the two cases where
        // that is not "commit what is staged" — there is no other place to learn either.
        var label = _amendCommit
            ? (stagedCount > 0
                ? $"Amend last commit with {stagedCount} staged file{(stagedCount == 1 ? "" : "s")}"
                : commitAll
                    ? $"Amend last commit with {counts.Unstaged} change{(counts.Unstaged == 1 ? "" : "s")}"
                    : "Amend last commit (message only)")
            : commitAll
                ? $"Commit all {counts.Unstaged} change{(counts.Unstaged == 1 ? "" : "s")}"
                : $"Commit {stagedCount} staged file{(stagedCount == 1 ? "" : "s")}";
        if (_settings.Git.SignCommits) label += " (signed)";
        var button = Ui.PrimaryButton(label, _amendCommit ? Icons.PencilSimple : Icons.GitCommit, DoCommit)
            .Width(Dimension.Percent(100));
        button.Key = "composer-commit";
        box.AddChild(button);
        return box;
    }

    /// <summary>
    /// Turns amend on or off, carrying HEAD's message in and back out with it.
    ///
    /// <para>Switching on fills empty fields from HEAD, which is the whole point: an amend
    /// almost always keeps the message and adds to the tree, and retyping it is how a
    /// message gets accidentally rewritten. Switching off takes that text back out — but
    /// only while it is still <i>exactly</i> what was poured in, so an edit made in between
    /// survives the toggle instead of being thrown away.</para>
    /// </summary>
    private void ToggleAmend(bool on, string? headMessage)
    {
        _amendCommit = on;
        if (on)
        {
            if (!string.IsNullOrWhiteSpace(_commitSummary) || !string.IsNullOrWhiteSpace(_commitDescription))
            {
                _amendPrefill = null;   // there was already a message; leave it alone
            }
            else if (headMessage is { Length: > 0 })
            {
                var (sum, body) = SplitCommitMessage(headMessage);
                _commitSummary = sum;
                _commitDescription = body;
                _amendPrefill = headMessage;
            }
        }
        else if (_amendPrefill is { } prefill)
        {
            var (sum, body) = SplitCommitMessage(prefill);
            if (_commitSummary == sum && _commitDescription == body)
            {
                _commitSummary = "";
                _commitDescription = "";
            }
            _amendPrefill = null;
        }
        MarkDirty();
    }

    /// <summary>A commit message split the way the two fields hold it: the first line is the
    /// summary, everything after the blank line that follows it is the description.</summary>
    private static (string Summary, string Body) SplitCommitMessage(string message)
    {
        var text = message.ReplaceLineEndings("\n").TrimEnd();
        var nl = text.IndexOf('\n');
        if (nl < 0) return (text, "");
        return (text[..nl], text[(nl + 1)..].TrimStart('\n'));
    }

    // ── Actions ──────────────────────────────────────────────────────────

    // Both selections can activate a NEW per-file AsyncLoader (image diff / conflict
    // document), so they must go through the Effect — a MarkDirty rebuild is untracked
    // and it would never subscribe to the new loader's signals. RerenderView: the chrome
    // shows nothing of the selection, and neither does the composer.
    private void SelectChange(string path, bool staged)
    {
        SetChangeSelection(new ChangeRef(path, staged ? ChangeSection.Staged : ChangeSection.Unstaged));
        RerenderView();
    }

    private void SelectConflict(string path)
    {
        SetChangeSelection(new ChangeRef(path, ChangeSection.Conflicted));
        RerenderView();
    }

    // ── Optimistic staging ───────────────────────────────────────────────
    //
    // git add / git reset on a large repository takes long enough to feel, and until it had
    // returned and the status had been read again the row stayed exactly where it was — so
    // a click on a file did nothing visible for a beat, and the second click (on what was
    // by then a different row) did something else. The move is made in the list first and
    // the command run behind it: staging a file git has just listed succeeds essentially
    // always, and the rare failure is corrected out loud — the row springs back and the
    // error git gave is on screen — instead of being paid for by every click.

    /// <summary>A file whose staging has been asked for but is not yet in git's index — or
    /// is not yet out of it. Keyed by repository too: the overlay outlives a click, and the
    /// same path exists in more than one open repository.</summary>
    private readonly record struct StageKey(string Repo, string Path);

    /// <summary>True: on its way into the staged list; false: on its way out of it.</summary>
    private readonly Dictionary<StageKey, bool> _pendingStage = new();

    /// <summary>Moves whose git command has come back successfully. They stay on screen
    /// until the reload behind them lands, or the row would flick back to where it started
    /// for the frames in between.</summary>
    private readonly HashSet<StageKey> _settledStage = new();

    /// <summary>The working copy as the user should see it: what git last told us, with
    /// every staging that has been asked for already applied.
    ///
    /// <para>Kept while git's answer, the stagings in flight and the repository are what they
    /// were: made on every call, a staging in flight gave each call new lists, so the rows were
    /// flattened again and every row on screen made again on every frame until git answered
    /// (~130 rows a call at 300 files).</para></summary>
    private ChangesData ChangesView
    {
        get
        {
            var data = _changes.Value;
            if (_pendingStage.Count == 0) return data;
            // A load's lists are new objects, and the record compares them as objects.
            if (_changesViewOf is { } of && of == data && _changesViewGen == _pendingStageGen
                && _changesViewRepo == _activeRepoPath)
                return _changesView;
            _changesView = ApplyPendingStaging(data);
            (_changesViewOf, _changesViewGen, _changesViewRepo) = (data, _pendingStageGen, _activeRepoPath);
            return _changesView;
        }
    }

    private ChangesData _changesView;
    private ChangesData? _changesViewOf;
    private int _changesViewGen;
    private string? _changesViewRepo;

    /// <summary>Counts the changes of <see cref="_pendingStage"/>, which <see cref="ChangesView"/> is
    /// kept across.</summary>
    private int _pendingStageGen;

    /// <summary>Moves the pending files between the two lists. The file's row is carried
    /// across unchanged — it is the same file with the same counts — and so is its diff,
    /// which is what the pane beside the list would otherwise come up empty for.</summary>
    private ChangesData ApplyPendingStaging(ChangesData data)
    {
        if (_pendingStage.Count == 0) return data;
        var mine = _pendingStage.Where(kv => PathComparer.Equals(kv.Key.Repo, _activeRepoPath)).ToList();
        if (mine.Count == 0) return data;

        var staged = data.Staged.ToList();
        var unstaged = data.Unstaged.ToList();
        var stagedDiffs = new Dictionary<string, GitFileDiff>(data.StagedDiffs, StringComparer.Ordinal);
        var unstagedDiffs = new Dictionary<string, GitFileDiff>(data.UnstagedDiffs, StringComparer.Ordinal);

        foreach (var (key, toStaged) in mine)
        {
            var from = toStaged ? unstaged : staged;
            var to = toStaged ? staged : unstaged;
            var fromDiffs = toStaged ? unstagedDiffs : stagedDiffs;
            var toDiffs = toStaged ? stagedDiffs : unstagedDiffs;

            int i = from.FindIndex(f => f.Path == key.Path);
            if (i < 0) continue;               // git already agrees, or the file is gone
            var entry = from[i];
            from.RemoveAt(i);
            if (!to.Any(f => f.Path == key.Path))
            {
                to.Add(entry);
                // Keep the lists in the path order git hands them over in, so a row does
                // not jump to the bottom of its new list and then up again on the reload.
                to.Sort((a, b) => string.CompareOrdinal(a.Path, b.Path));
            }
            if (fromDiffs.Remove(key.Path, out var diff)) toDiffs[key.Path] = diff;
        }
        return data with
        {
            Staged = staged,
            Unstaged = unstaged,
            StagedDiffs = stagedDiffs,
            UnstagedDiffs = unstagedDiffs,
        };
    }

    /// <summary>Runs when a reload lands: retires the moves git has confirmed, then picks
    /// the selection against the world as it now stands (moves still in flight included —
    /// pruning against the raw data would delete the very rows they are moving).</summary>
    private void OnChangesLoaded(ChangesData data)
    {
        foreach (var key in _settledStage) _pendingStage.Remove(key);
        _settledStage.Clear();
        _pendingStageGen++;
        PickInitialChange(ApplyPendingStaging(data));
    }

    /// <summary>Stages every given file and keeps them selected as they move into the
    /// staged list. The move shows immediately; git is asked behind it.</summary>
    private void StagePaths(IReadOnlyList<string> paths)
    {
        if (ActiveRepo is not { } repo || paths.Count == 0) return;
        RetargetChangeSelection(paths, ChangeSection.Staged);
        RunStaging(paths, toStaged: true, () => repo.Stage(paths),
            ok: paths.Count > 1 ? $"Staged {paths.Count} files" : null, fail: "Stage failed");
    }

    private void UnstagePaths(IReadOnlyList<string> paths)
    {
        if (ActiveRepo is not { } repo || paths.Count == 0) return;
        RetargetChangeSelection(paths, ChangeSection.Unstaged);
        RunStaging(paths, toStaged: false, () => repo.Unstage(paths),
            ok: paths.Count > 1 ? $"Unstaged {paths.Count} files" : null, fail: "Unstage failed");
    }

    private void StageAll()
    {
        if (ActiveRepo is not { } repo) return;
        var paths = ChangesView.Unstaged.Select(f => f.Path).ToList();
        if (paths.Count == 0) return;
        RunStaging(paths, toStaged: true, repo.StageAll, ok: null, fail: "Stage failed");
    }

    private void UnstageAll()
    {
        if (ActiveRepo is not { } repo) return;
        var paths = ChangesView.Staged.Select(f => f.Path).ToList();
        if (paths.Count == 0) return;
        RunStaging(paths, toStaged: false, repo.UnstageAll, ok: null, fail: "Unstage failed");
    }

    /// <summary>
    /// Staging commands run one after another, not all at once: each takes git's index lock,
    /// and two racing for it makes one fail for a reason the user did nothing to cause — which
    /// under an optimistic list means a row springing back for no visible reason. Anything
    /// that has to see the index settled waits on this same chain; see <see cref="DoCommit"/>.
    /// </summary>
    private Task _stagingChain = Task.CompletedTask;

    /// <summary>
    /// Shows the move, then runs <paramref name="command"/> off the UI thread and reconciles
    /// with what it says. A failure clears the overlay before the toast, so the rows are back
    /// where git has them by the time the message is read.
    /// </summary>
    private void RunStaging(IReadOnlyList<string> paths, bool toStaged, Func<bool> command,
        string? ok, string fail)
    {
        var repoPath = _activeRepoPath;
        var keys = paths.Select(p => new StageKey(repoPath, p)).ToList();
        foreach (var key in keys) _pendingStage[key] = toStaged;
        _pendingStageGen++;
        MarkDirty();
        _stagingChain = RunStagingAsync(keys, command, ok, fail, repoPath);
    }

    private async Task RunStagingAsync(List<StageKey> keys, Func<bool> command,
        string? ok, string fail, string repoPath)
    {
        // Read before the first await, so this is the chain as it stood when the click
        // happened rather than the one this call is about to become.
        var previous = _stagingChain;
        try { await previous.ConfigureAwait(false); }
        catch { /* the earlier command reported its own failure */ }

        bool success;
        string? error = null;
        try { success = await Task.Run(command).ConfigureAwait(false); }
        catch (Exception ex) { success = false; error = ex.Message; }

        Application.InvokeOnMainThread(() =>
        {
            if (success)
            {
                foreach (var key in keys)
                    if (_pendingStage.ContainsKey(key)) _settledStage.Add(key);
                if (ok != null) Flash(ok);
            }
            else
            {
                foreach (var key in keys) { _pendingStage.Remove(key); _settledStage.Remove(key); }
                _pendingStageGen++;
                Flash(error is null ? fail : FirstLine(error, fail));
            }
            if (PathComparer.Equals(repoPath, _activeRepoPath)) RefreshAll();
            else MarkDirty();
        });
    }

    /// <summary>
    /// Runs a git command off the UI thread and applies its result on the main thread once
    /// it has finished. <paramref name="command"/> must do all of the talking to git —
    /// including any follow-up query the result needs, such as asking what state the
    /// sequencer is in — and <paramref name="apply"/> must do none of it.
    ///
    /// <para>These all used to run inline in the click handler that started them. One git
    /// command costs a few hundred milliseconds here and several of them chain, so the
    /// window stopped painting — no hover, no motion, nothing — until the last one returned.
    /// Only the result touches the UI, so only the result needs the UI thread.</para>
    ///
    /// <para>Joins <see cref="_stagingChain"/> rather than running free: every one of these
    /// takes git's index lock, and two racing for it makes one fail for a reason the user
    /// did nothing to cause. Staging is optimistic on top of that, so a checkout fired
    /// straight after a stage has to see the index the list is already showing.</para>
    /// </summary>
    private void RunGitCommand<T>(Func<T> command, Action<T> apply)
        => _stagingChain = RunGitCommandAsync(command, apply);

    private async Task RunGitCommandAsync<T>(Func<T> command, Action<T> apply)
    {
        // Read before the first await, so this is the chain as it stood when the click
        // happened rather than the one this call is about to become.
        var previous = _stagingChain;
        try { await previous.ConfigureAwait(false); }
        catch { /* the earlier command reported its own failure */ }

        T result;
        try { result = await Task.Run(command).ConfigureAwait(false); }
        catch (Exception ex)
        {
            // Git throwing rather than failing is the unexpected case — a missing binary, a
            // path that vanished. Say so instead of leaving the click looking ignored.
            var message = ex.Message;
            Application.InvokeOnMainThread(() => Flash(FirstLine(message, "Git command failed")));
            return;
        }

        Application.InvokeOnMainThread(() => apply(result));
    }

    private async void DoCommit()
    {
        // Staging is optimistic: the rows move before git has been asked. A commit pressed
        // straight after one must therefore wait for the index to actually hold what the
        // list is already showing — otherwise it commits whatever `git add` had reached.
        try { await _stagingChain; }
        catch { /* the staging call reported its own failure */ }

        if (ActiveRepo is not { } repo) return;
        var data = ChangesView;

        // Nothing staged: take the whole worktree, the way `git commit -a` does — except it
        // also picks up untracked files, because "commit all my changes" from a list that
        // shows untracked files alongside the rest can only mean the list. An amend with
        // nothing staged is still a commit (of the message alone), so it is left to proceed.
        bool stageEverything = false;
        if (data.Staged.Count == 0)
        {
            if (data.Conflicted.Count > 0)
            {
                Flash("Resolve the conflicts first");
                return;
            }
            if (data.Unstaged.Count > 0) stageEverything = true;
            else if (!_amendCommit) { Flash("Nothing staged to commit"); return; }
        }
        if (string.IsNullOrWhiteSpace(_commitSummary)) { Flash("Enter a commit summary"); return; }

        var message = _commitSummary.Trim();
        if (!string.IsNullOrWhiteSpace(_commitDescription))
            message += "\n\n" + _commitDescription.Trim();

        bool sign = _settings.Git.SignCommits;
        bool amend = _amendCommit;
        bool stageAll = stageEverything;

        // The await above does not get this off the UI thread: awaiting an already-completed
        // task continues synchronously, and the chain is complete whenever nothing was staged
        // in the last moment — which is most of the time. So the commit itself has to be
        // handed off explicitly. It is the slowest of these commands, too: hooks run inside
        // it, and a signed commit waits on gpg.
        RunGitCommand(
            () => stageAll && !repo.StageAll()
                ? new CommitOutcome(Staged: false, null)
                : new CommitOutcome(true, amend ? repo.CommitAmend(message, sign) : repo.Commit(message, sign)),
            o =>
            {
                if (!o.Staged) { Flash("Stage failed"); return; }
                var result = o.Result!;
                if (result.Success)
                {
                    _commitSummary = "";
                    _commitDescription = "";
                    // The next commit is a new one unless it is asked for again — see _amendCommit.
                    _amendCommit = false;
                    _amendPrefill = null;
                    Flash((amend ? "Amended" : "Committed") + (sign ? " (signed)" : ""));
                    RefreshAll();
                }
                else
                {
                    Flash(FirstLine(result.Stderr, amend ? "Amend failed" : "Commit failed"));
                }
            });
    }

    /// <summary>Whether the implicit "stage everything first" step got through, and the
    /// commit that followed it — null when it did not.</summary>
    private readonly record struct CommitOutcome(bool Staged, GitResult? Result);
}
