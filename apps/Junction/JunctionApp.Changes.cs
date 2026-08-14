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

        // The ignore-whitespace flag is part of the key so toggling it reloads the diffs.
        bool ws = _settings.Git.IgnoreWhitespace;
        _changes.Load($"{_activeRepoPath}|{DataGen(_activeRepoPath)}|w{ws}",
            () => LoadChanges(ws), onLoaded: PickInitialChange);
        var data = _changes.Value;

        // During a merge the right pane is always shown — it hosts the per-file
        // conflict resolution — even on narrow windows where diffs normally hide.
        bool showPane = !_hideDiff || data.IsMerging;

        var row = Flex.Row(gap: 0).FlexGrow(1).AlignItems(AlignItems.Stretch);
        row.AddChild(BuildChangesColumn(data, fill: !showPane));
        if (showPane)
            row.AddChild(BuildChangesRightPane(data));
        return row;
    }

    // The right pane: a conflicted file shows the ours/theirs resolution UI; any
    // other selection shows the normal diff (open in VS Code with Ctrl+D).
    private Element BuildChangesRightPane(ChangesData data)
    {
        if (data.IsMerging && _changeSelConflicted && _changeSelPath != null
            && data.Conflicted.Any(f => f.Path == _changeSelPath))
            return BuildConflictResolutionPane(_changeSelPath, data.Merge);

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
        bool merging = repo.IsMerging();
        IReadOnlyList<GitFileChange> conflicted = merging ? repo.GetConflictedFiles() : [];
        MergeSides? merge = null;
        if (merging)
        {
            var conflictPaths = conflicted.Select(f => f.Path).ToHashSet(StringComparer.Ordinal);
            unstaged = unstaged.Where(f => !conflictPaths.Contains(f.Path)).ToList();
            staged = staged.Where(f => !conflictPaths.Contains(f.Path)).ToList();
            merge = new MergeSides(
                repo.GetCurrentBranch(), repo.GetCommit("HEAD"),
                repo.GetMergingBranchName(), repo.GetCommit("MERGE_HEAD"));
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
        return new ChangesData(staged, unstaged, stagedDiffs, unstagedDiffs, merging, conflicted, merge, assumeUnchanged);
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

    /// <summary>Every selectable file row in display order — conflicts, then staged,
    /// then unstaged, exactly as <see cref="BuildChangesColumn"/> lays them out.
    /// Keyboard navigation and Shift ranges index into this list.</summary>
    private static List<ChangeRef> ChangeRows(ChangesData data)
    {
        var rows = new List<ChangeRef>(data.Conflicted.Count + data.Staged.Count + data.Unstaged.Count);
        foreach (var f in data.Conflicted) rows.Add(new ChangeRef(f.Path, ChangeSection.Conflicted));
        foreach (var f in data.Staged) rows.Add(new ChangeRef(f.Path, ChangeSection.Staged));
        foreach (var f in data.Unstaged) rows.Add(new ChangeRef(f.Path, ChangeSection.Unstaged));
        return rows;
    }

    private static HashSet<ChangeRef> ChangeRowSet(ChangesData data) => [.. ChangeRows(data)];

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

    /// <summary>The focus caret as a row, or null when no file is selected.</summary>
    private ChangeRef? ChangeFocus() => _changeSelPath is { } p
        ? new ChangeRef(p, _changeSelConflicted ? ChangeSection.Conflicted
            : _changeSelStaged ? ChangeSection.Staged : ChangeSection.Unstaged)
        : null;

    /// <summary>Moves the focus caret — the file the diff pane, Ctrl+D and the
    /// line-level staging selection all follow — without touching the selection set.</summary>
    private void SetChangeFocus(ChangeRef row)
    {
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
            default: return false;
        }
    }

    // Rows here are variable height (a file with a directory prefix is two lines), so
    // a page is a count rather than a measured band — the same one the Commit view's
    // file list uses.
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

    /// <summary>How many files a row action targets: the whole selection when the row
    /// is inside a multi-selection, otherwise just that row. O(1), so rows can label
    /// themselves with it.</summary>
    private int ChangeActionCount(ChangeRef row) =>
        _changeSelection.Count > 1 && _changeSelection.Contains(row) ? _changeSelection.Count : 1;

    /// <summary>The files a row action acts on, in display order. Mirrors the History
    /// list's right-click rule: inside a multi-selection the action takes the
    /// selection, outside it takes the clicked row.</summary>
    private List<string> ChangeActionPaths(ChangeRef row)
    {
        if (ChangeActionCount(row) == 1) return [row.Path];
        return ChangeRows(_changes.Value)
            .Where(_changeSelection.Contains)
            .Select(r => r.Path)
            .ToList();
    }

    /// <summary>Moves selected rows (and the caret and anchor) into another section,
    /// so a selection keeps hold of its files as they move between the lists.</summary>
    private void RetargetChangeSelection(IReadOnlyList<string> paths, ChangeSection to)
    {
        var moved = new HashSet<string>(paths, StringComparer.Ordinal);
        foreach (var r in _changeSelection.Where(r => moved.Contains(r.Path)).ToList())
        {
            _changeSelection.Remove(r);
            _changeSelection.Add(r with { Section = to });
        }
        if (_changeAnchor is { } a && moved.Contains(a.Path)) _changeAnchor = a with { Section = to };
        if (_changeSelPath is { } p && moved.Contains(p)) SetChangeFocus(new ChangeRef(p, to));
    }

    // ── Left column: file lists + composer ───────────────────────────────

    private Element BuildChangesColumn(ChangesData data, bool fill = false)
    {
        // When the diff pane is hidden the column fills the view; otherwise it's a
        // fixed-width sidebar next to the diff.
        var col = Flex.Column(gap: 0)
            .BorderColor(Nocturne.Divider).BorderWidth(new Thickness(0, 1, 0, 0));
        if (fill) col.FlexGrow(1);
        else col.Width(330).FlexShrink(0);

        // Merge state gets a pinned banner (status + Abort / Continue) above the list.
        if (data.IsMerging)
            col.AddChild(BuildMergeBanner(data));

        var scroll = ScrollView.New().FlexGrow(1).Padding(new Thickness(8, 8, 4, 8))
            .WithRef(_changeScrollRef);

        // Conflicts first — selecting one opens its resolution pane on the right.
        if (data.Conflicted.Count > 0)
        {
            scroll.AddChild(SectionHeader($"Conflicts · {data.Conflicted.Count}", Nocturne.DangerText, "", null));
            foreach (var f in data.Conflicted)
                scroll.AddChild(ConflictListRow(f));
        }

        if (data.Staged.Count == 0 && data.Unstaged.Count == 0)
        {
            if (!data.IsMerging && data.Conflicted.Count == 0)
                scroll.AddChild(Box.New().Padding(new Thickness(26)).JustifyContent(JustifyContent.Center)
                    .AlignItems(AlignItems.Center)
                    .Children(Text.New("No changes").FontSize(13f).TextColor(Nocturne.On(0.45))));
        }
        else
        {
            scroll.AddChild(SectionHeader($"Staged · {data.Staged.Count}", Nocturne.Accent300,
                "Unstage all", data.Staged.Count > 0 ? UnstageAll : null, topGap: data.Conflicted.Count > 0));
            foreach (var f in data.Staged)
                scroll.AddChild(ChangeRow(f, isStaged: true));

            scroll.AddChild(SectionHeader($"Changes · {data.Unstaged.Count}", Nocturne.On(0.42),
                "Stage all", data.Unstaged.Count > 0 ? StageAll : null, topGap: true));
            foreach (var f in data.Unstaged)
                scroll.AddChild(ChangeRow(f, isStaged: false));
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
        col.AddChild(BuildComposer(data.Staged.Count));
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

    private Element ChangeRow(GitFileChange f, bool isStaged)
    {
        var self = new ChangeRef(f.Path, isStaged ? ChangeSection.Staged : ChangeSection.Unstaged);
        var selected = _changeSelection.Contains(self);
        // The focus caret (Ctrl+arrows) can sit outside the selection; give it a
        // hover-strength tint so keyboard navigation stays visible.
        var focused = ChangeFocus() == self;
        var bg = selected ? Nocturne.Accent900
            : focused ? Nocturne.Neutral900
            : Color.Transparent;
        var n = ChangeActionCount(self);
        var (letter, color) = StatusLetter(f.Kind);
        var name = System.IO.Path.GetFileName(f.Path);
        var dir = f.Path.Length > name.Length ? f.Path[..^name.Length] : "";

        var row = Flex.Row(gap: 8).AlignItems(AlignItems.Center)
            .Padding(new Thickness(6, 8, 6, 8)).CornerRadius(Nocturne.RadiusRow)
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

        var text = Flex.Column(gap: 0).FlexGrow(1);
        text.AddChild(Text.New(name).FontSize(12f).OverflowMode(TextOverflow.Ellipsis).MaxLines(1));
        if (dir.Length > 0)
            text.AddChild(Text.New(dir).FontSize(9.5f).TextColor(Nocturne.On(0.40))
                .OverflowMode(TextOverflow.Ellipsis).MaxLines(1));
        row.AddChild(text);

        if (f.Additions > 0 || f.Deletions > 0)
            row.AddChild(Flex.Row(gap: 4).FlexShrink(0).Children(
                Text.New($"+{f.Additions}").FontSize(10f).TextColor(Nocturne.AddCount),
                Text.New($"−{f.Deletions}").FontSize(10f).TextColor(Nocturne.DelCount)));

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
        items.Add(Item(Icons.FileCode, "Open diff in VS Code", () =>
        {
            SelectChange(f.Path, isStaged);
            OpenSelectedDiffExternally();
        }));
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

    private Element BuildComposer(int stagedCount)
    {
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
        box.AddChild(descBox);

        // Sign toggle: bound to the persisted default; flipping it sticks.
        var sign = Checkbox.New(_settings.Git.SignCommits, "Sign commit (GPG/SSH)")
            .OnCheckedChanged(v => { _settings.Git.SignCommits = v; Persist(); MarkDirty(); });
        sign.LabelGap = 8f;
        sign.TextColor(Nocturne.On(0.7)).FontSize(11.5f);
        box.AddChild(sign);

        var label = $"Commit {stagedCount} staged file{(stagedCount == 1 ? "" : "s")}"
            + (_settings.Git.SignCommits ? " (signed)" : "");
        box.AddChild(Ui.PrimaryButton(label, Icons.GitCommit, DoCommit).Width(Dimension.Percent(100)));
        return box;
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
        if (_changes.Value.Staged.Count == 0) { Flash("Nothing staged to commit"); return; }
        if (string.IsNullOrWhiteSpace(_commitSummary)) { Flash("Enter a commit summary"); return; }

        var message = _commitSummary.Trim();
        if (!string.IsNullOrWhiteSpace(_commitDescription))
            message += "\n\n" + _commitDescription.Trim();

        var result = repo.Commit(message, sign: _settings.Git.SignCommits);
        if (result.Success)
        {
            _commitSummary = "";
            _commitDescription = "";
            Flash(_settings.Git.SignCommits ? "Committed (signed)" : "Committed");
            RefreshAll();
        }
        else
        {
            Flash(FirstLine(result.Stderr, "Commit failed"));
        }
    }
}
