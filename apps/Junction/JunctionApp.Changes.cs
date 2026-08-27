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
        // The ignore-whitespace flag is part of the key so toggling it reloads the diffs.
        bool ws = _settings.Git.IgnoreWhitespace;
        _changes.Load($"{_activeRepoPath}|{DataGen(_activeRepoPath)}|w{ws}",
            () => LoadChanges(ws), onLoaded: PickInitialChange);
        var data = _changes.Value;

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
    private ChangesData LoadChanges(bool ignoreWhitespace = false)
    {
        var repo = RepoFor(_activeRepoPath);
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

    /// <summary>Every row on screen in display order — conflicts, then staged, then
    /// unstaged, each grouped into its folder tree exactly as <see cref="BuildChangesColumn"/>
    /// lays them out. Folder rows are rows like any other here: keyboard navigation and
    /// Shift ranges index into this list, so a folder is somewhere the caret lands and
    /// something a range picks up.</summary>
    private List<ChangeRef> ChangeRows(ChangesData data)
    {
        var rows = new List<ChangeRef>();
        foreach (var (section, files) in ChangeSections(data))
            foreach (var node in ChangeTreeRows(section, files))
                rows.Add(new ChangeRef(node.Path, section, node.IsFolder));
        return rows;
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
        var rows = ChangeRows(_changes.Value);
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
        // Rerender, not MarkDirty: the caret drives the right pane, and a conflicted or
        // image file there activates a NEW per-file loader — see SelectChange.
        Rerender();
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
        var data = _changes.Value;
        var nodes = ChangeTreeRows(focus.Section, SectionFiles(data, focus.Section));
        var i = nodes.FindIndex(n => n.IsFolder == focus.IsFolder && n.Path == focus.Path);
        if (i < 0) return true;

        var landed = ChangeTreeFor(focus.Section).Step(nodes, i, right);
        if (landed == i) { MarkDirty(); return true; }   // it expanded or collapsed instead
        var node = nodes[landed];
        SetChangeSelection(new ChangeRef(node.Path, focus.Section, node.IsFolder));
        _pendingScrollToSelected = true;
        Rerender();
        return true;
    }

    // One line per row now that the folder above carries the directory, so a page could
    // be measured — but the count is what the Commit view's list uses and what the
    // History list's page maths degrades to, and eight rows is a page either way.
    private const int ChangesPageSize = 8;

    private void MoveChangeSelection(int delta, bool extend = false, bool focusOnly = false, bool additive = false)
    {
        var rows = ChangeRows(_changes.Value);
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
        // Rerender for the same reason as OnChangeRowClick: the caret changed, so the
        // right pane may now read a different per-file loader.
        Rerender();
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

    /// <summary>How many files a row action targets: every file the selection covers when
    /// the row is inside it, otherwise the row's own — one for a file, all of them for a
    /// folder. <paramref name="ownCount"/> comes from the caller's node, so labelling a
    /// row stays O(1) and the whole list stays O(n).</summary>
    private int ChangeActionCount(ChangeRef row, int ownCount) =>
        _changeSelection.Contains(row) && _changeSelFileCount > 1 ? _changeSelFileCount : ownCount;

    /// <summary>The files a row action acts on, in list order. Mirrors the History list's
    /// right-click rule: inside a multi-file selection the action takes the selection,
    /// outside it takes the clicked row — and a folder row is the files under it.</summary>
    private List<string> ChangeActionPaths(ChangeRef row)
    {
        var data = _changes.Value;
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
                : FilesUnder(SectionFiles(_changes.Value, r.Section), r.Path) is { Count: > 0 } under
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
        else col.Width(_changesSplit.Width).FlexShrink(0);

        // A half-finished operation gets a pinned banner (status + Abort / Skip /
        // Continue) above the list.
        if (data.Sequencer is { } seq)
            col.AddChild(BuildSequencerBanner(data, seq.State));

        var scroll = ScrollView.New().FlexGrow(1).Padding(new Thickness(8, 8, 4, 8))
            .WithRef(_changeScrollRef);

        // How many files the current selection covers, counted once for the whole build:
        // every row that offers an action labels itself with it ("Stage 3 files").
        _changeSelFileCount = ChangeSelectionPaths(data).Count;

        // Conflicts first — selecting one opens its resolution pane on the right.
        if (data.Conflicted.Count > 0)
        {
            scroll.AddChild(SectionHeader($"Conflicts · {data.Conflicted.Count}", Nocturne.DangerText, "", null));
            AddChangeRows(scroll, ChangeSection.Conflicted, data.Conflicted);
        }

        if (data.Staged.Count == 0 && data.Unstaged.Count == 0)
        {
            if (!data.InSequencer && data.Conflicted.Count == 0)
                scroll.AddChild(Box.New().Padding(new Thickness(26)).JustifyContent(JustifyContent.Center)
                    .AlignItems(AlignItems.Center)
                    .Children(Text.New("No changes").FontSize(13f).TextColor(Nocturne.On(0.45))));
        }
        else
        {
            scroll.AddChild(SectionHeader($"Staged · {data.Staged.Count}", Nocturne.Accent300,
                "Unstage all", data.Staged.Count > 0 ? UnstageAll : null, topGap: data.Conflicted.Count > 0));
            AddChangeRows(scroll, ChangeSection.Staged, data.Staged);

            scroll.AddChild(SectionHeader($"Changes · {data.Unstaged.Count}", Nocturne.On(0.42),
                "Stage all", data.Unstaged.Count > 0 ? StageAll : null, topGap: true));
            AddChangeRows(scroll, ChangeSection.Unstaged, data.Unstaged);
        }

        // Assume-unchanged files are hidden from status, so surface them here with a
        // way to start watching them again (otherwise the flag is a trap — set once,
        // never seen again).
        if (data.AssumeUnchanged.Count > 0)
        {
            scroll.AddChild(SectionHeader($"Hidden · {data.AssumeUnchanged.Count}", Nocturne.On(0.42),
                "", null, topGap: data.Staged.Count > 0 || data.Unstaged.Count > 0 || data.Conflicted.Count > 0));
            foreach (var path in data.AssumeUnchanged)
                scroll.AddChild(HiddenFileRow(path));
        }

        col.AddChild(scroll);
        col.AddChild(BuildComposer(data));
        return col;
    }

    private Element SectionHeader(string label, Color labelColor, string action, Action? onAction, bool topGap = false)
    {
        var row = Flex.Row(gap: 6).AlignItems(AlignItems.Center)
            .Padding(new Thickness(topGap ? 14 : 7, 8, 5, 8));
        row.AddChild(Text.New(label.ToUpperInvariant()).FontSize(10f).TextColor(labelColor).FlexGrow(1));
        if (onAction != null)
            row.AddChild(Ui.GhostButton(action, null, onAction, 11f));
        return row;
    }

    /// <summary>Lays one section's tree into the column: folder rows and file rows in one
    /// flat run, the same order <see cref="ChangeRows"/> walks for the keyboard.</summary>
    private void AddChangeRows(Element scroll, ChangeSection section,
        IReadOnlyList<GitFileChange> files)
    {
        foreach (var node in ChangeTreeRows(section, files))
            scroll.AddChild(node.IsFolder
                ? ChangesFolderRow(section, node, files)
                : section == ChangeSection.Conflicted
                    ? ConflictListRow(node)
                    : ChangeRow(node, isStaged: section == ChangeSection.Staged));
    }

    /// <summary>A folder row in one of the three lists. It selects, stages and unstages as
    /// everything below it — one checkbox for a directory is the shortcut the flat list
    /// could never offer — and its counts are the totals of the files it hides.</summary>
    private Element ChangesFolderRow(ChangeSection section, TreeNode<GitFileChange> node,
        IReadOnlyList<GitFileChange> files)
    {
        var tree = ChangeTreeFor(section);
        var self = new ChangeRef(node.Path, section, IsFolder: true);
        var selected = _changeSelection.Contains(self);
        var focused = ChangeFocus() == self;
        var expanded = tree.IsExpanded(node);
        var bg = selected ? Nocturne.Accent900
            : focused ? Nocturne.Neutral900
            : Color.Transparent;
        var n = ChangeActionCount(self, node.LeafCount);
        var under = FilesUnder(files, node.Path);
        var (additions, deletions) = FolderTotals(under);

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
        // Selection, focus and open state all change how the row looks, and a collapse
        // moves every row below it — see ChangeRow for why a patched row would otherwise
        // keep the previous occupant's handlers.
        row.Key = $"{section}:dir:{node.Path}{(selected ? "#s" : "")}{(focused ? "#f" : "")}{(expanded ? "#o" : "")}";
        if (bg != Color.Transparent) row.Background(bg);
        if (focused) row.WithRef(_changeFocusRef);

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
                Rerender();
            }
        });
        row.ContextMenu(() => ChangeFolderMenuItems(section, node));
        if (!selected) row.HoverBg(Nocturne.Neutral900, bg);
        return row;
    }

    private Element ChangeRow(TreeNode<GitFileChange> node, bool isStaged)
    {
        var f = node.Item;
        var self = new ChangeRef(f.Path, isStaged ? ChangeSection.Staged : ChangeSection.Unstaged);
        var selected = _changeSelection.Contains(self);
        // The focus caret (Ctrl+arrows) can sit outside the selection; give it a
        // hover-strength tint so keyboard navigation stays visible.
        var focused = ChangeFocus() == self;
        var bg = selected ? Nocturne.Accent900
            : focused ? Nocturne.Neutral900
            : Color.Transparent;
        var n = ChangeActionCount(self, 1);
        var (letter, color) = StatusLetter(f.Kind);

        var row = Flex.Row(gap: 8).AlignItems(AlignItems.Center)
            .Padding(ChangeRowPadding(node.Depth)).CornerRadius(Nocturne.RadiusRow)
            .Cursor(CursorType.Pointer).Tooltip(f.Path);
        // Selection and focus in the key so either change gives a fresh element (see
        // FileTreeRow — the stale hover handler would otherwise clobber the highlight).
        row.Key = (isStaged ? "s:" : "u:") + f.Path + (selected ? "#s" : "") + (focused ? "#f" : "");
        if (bg != Color.Transparent) row.Background(bg);
        if (focused) row.WithRef(_changeFocusRef);

        // Checkbox toggles staging without moving the selection — and acts on the whole
        // selection when this row is part of one, the same set the context menu takes.
        var check = Box.New().FlexShrink(0).Cursor(CursorType.Pointer)
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

        row.AddChild(Box.New().Width(14).FlexShrink(0)
            .Children(Ui.Mono(letter, 11f, color).FontWeight(FontWeight.Bold)));

        // The name alone: the folder row above it carries the directory.
        row.AddChild(Text.New(node.Segment).FontSize(12f).FlexGrow(1).FlexShrink(1)
            .OverflowMode(TextOverflow.Ellipsis).MaxLines(1));

        if (ChangeCounts(f.Additions, f.Deletions) is { } counts)
            row.AddChild(counts);

        row.OnClick(e => OnChangeRowClick(self, e.Modifiers));
        // Right-click outside the selection collapses to that row first, then the
        // framework opens the menu; inside it the selection is left alone.
        row.On<PointerDownEvent>(e =>
        {
            if (e.Button == 2 && !_changeSelection.Contains(self))
            {
                SetChangeSelection(self);
                Rerender();
            }
        });
        row.ContextMenu(() => ChangeFileMenuItems(f, isStaged));
        if (!selected) row.HoverBg(Nocturne.Neutral900, bg);   // restore the focus tint on leave
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
        row.HoverBg(Nocturne.Neutral900, Color.Transparent);
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
        }, "Ctrl O"));
        items.Add(Item(Icons.FileCode, "Open diff in VS Code", () =>
        {
            SelectChange(f.Path, isStaged);
            OpenSelectedDiffExternally();
        }, "Ctrl D"));
        items.Add(Item(Icons.Copy, "Copy path", () => CopyToClipboard(f.Path)));

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
        var files = _changes.Value.Unstaged.Where(f => wanted.Contains(f.Path)).ToList();
        if (files.Count == 0) return;

        var untracked = files.Count(f => f.Kind == FileChangeKind.Untracked);
        var body = files.Count == 1
            ? $"Discards the working-tree changes in {System.IO.Path.GetFileName(files[0].Path)}."
            : $"Discards the working-tree changes in {files.Count} files.";
        if (untracked > 0)
            body += untracked == 1
                ? " One of them is untracked and is deleted from disk."
                : $" {untracked} of them are untracked and are deleted from disk.";
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

    private Element BuildComposer(ChangesData data)
    {
        int stagedCount = data.Staged.Count;
        // Nothing staged but plenty changed is the ordinary case for someone who never
        // stages, so the commit button and Ctrl+Enter take the whole worktree rather than
        // refusing. Conflicts are the exception: staging one is how you declare it resolved,
        // and doing that to a file nobody looked at is not something a commit should decide.
        bool commitAll = stagedCount == 0 && data.Unstaged.Count > 0 && data.Conflicted.Count == 0;

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
            }
        });

        // Summary
        var sumBox = Flex.Row(gap: 0).AlignItems(AlignItems.Center)
            .Height(34).Padding(new Thickness(0, 10, 0, 10))
            .Background(Nocturne.Bg).BorderColor(Nocturne.Divider).BorderWidth(1f).CornerRadius(Nocturne.RadiusMd);
        var summary = TextInput.New(_commitSummary);
        summary.PlaceholderText("Summary (required)")
            .OnTextChanged(q => { _commitSummary = q; MarkDirty(); })
            .Height(Dimension.Px(30)).FlexGrow(1).FlexShrink(1).FlexBasis(Dimension.Px(0))
            .TextColor(Nocturne.Text).WithRef(_summaryRef);
        sumBox.AddChild(summary);
        // Every child of the composer is keyed, and that is all-or-nothing: a keyed child
        // among unkeyed siblings makes the reconciler match the whole list by key, and the
        // unkeyed ones are then *replaced* rather than patched — which threw away the field
        // you were typing in after the first character.
        sumBox.Key = "composer-summary";
        box.AddChild(sumBox);

        // Description
        var descBox = Flex.Column(gap: 0)
            .Padding(new Thickness(6, 10, 6, 10))
            .Background(Nocturne.Bg).BorderColor(Nocturne.Divider).BorderWidth(1f).CornerRadius(Nocturne.RadiusMd);
        var desc = TextArea.New(_commitDescription);
        desc.PlaceholderText("Description")
            .OnTextChanged(q => { _commitDescription = q; MarkDirty(); })
            .Height(Dimension.Px(56))
            .TextColor(Nocturne.Text).WithRef(_descRef);
        descBox.AddChild(desc);
        descBox.Key = "composer-description";
        box.AddChild(descBox);

        // Amend, then sign. Amend is offered only where there is a commit to rewrite — a
        // repository with no commits yet has nothing behind HEAD, and `git commit --amend`
        // there fails with a message about an unborn branch.
        if (data.HeadMessage is { Length: > 0 })
        {
            var amend = Checkbox.New(_amendCommit, "Amend last commit")
                .OnCheckedChanged(v => ToggleAmend(v, data.HeadMessage));
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
                    ? $"Amend last commit with {data.Unstaged.Count} change{(data.Unstaged.Count == 1 ? "" : "s")}"
                    : "Amend last commit (message only)")
            : commitAll
                ? $"Commit all {data.Unstaged.Count} change{(data.Unstaged.Count == 1 ? "" : "s")}"
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
    // document), so they must go through Rerender — a MarkDirty rebuild is untracked
    // and the Effect would never subscribe to the new loader's signals.
    private void SelectChange(string path, bool staged)
    {
        SetChangeSelection(new ChangeRef(path, staged ? ChangeSection.Staged : ChangeSection.Unstaged));
        Rerender();
    }

    private void SelectConflict(string path)
    {
        SetChangeSelection(new ChangeRef(path, ChangeSection.Conflicted));
        Rerender();
    }

    /// <summary>Stages every given file in one git call and keeps them selected as they
    /// move into the staged list.</summary>
    private void StagePaths(IReadOnlyList<string> paths)
    {
        if (ActiveRepo is not { } repo || paths.Count == 0) return;
        var ok = repo.Stage(paths);
        if (!ok) Flash("Stage failed");
        else if (paths.Count > 1) Flash($"Staged {paths.Count} files");
        RetargetChangeSelection(paths, ChangeSection.Staged);
        RefreshAll();
    }

    private void UnstagePaths(IReadOnlyList<string> paths)
    {
        if (ActiveRepo is not { } repo || paths.Count == 0) return;
        var ok = repo.Unstage(paths);
        if (!ok) Flash("Unstage failed");
        else if (paths.Count > 1) Flash($"Unstaged {paths.Count} files");
        RetargetChangeSelection(paths, ChangeSection.Unstaged);
        RefreshAll();
    }

    private void StageAll()
    {
        ActiveRepo?.StageAll();
        RefreshAll();
    }

    private void UnstageAll()
    {
        ActiveRepo?.UnstageAll();
        RefreshAll();
    }

    private void DoCommit()
    {
        if (ActiveRepo is not { } repo) return;
        var data = _changes.Value;

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

        if (stageEverything && !repo.StageAll())
        {
            Flash("Stage failed");
            return;
        }

        bool sign = _settings.Git.SignCommits;
        bool amend = _amendCommit;
        var result = amend ? repo.CommitAmend(message, sign) : repo.Commit(message, sign);
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
    }
}
