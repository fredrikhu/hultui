using HultUI;
using HultUI.Core.Builders;
using HultUI.Core.Elements;
using HultUI.Core.Events;
using HultUI.Core.Layout;
using HultUI.Core.Primitives;
using HultUI.Core.Reactivity;
using HultUI.Core.Styling;
using Junction.Git;
using Junction.Theme;

namespace Junction;

public partial class JunctionApp
{
    // ── Conflict resolution (Changes view) ───────────────────────────────────
    //
    // Conflicts appear in the file list like any other change; selecting one swaps
    // the diff pane for a resolution view. The conflicted working file is parsed
    // into its marked regions, and each is resolved independently — ours / theirs /
    // both / hand-edited — then written back and staged. Whole-file escapes (checkout
    // --ours/--theirs, open in VS Code) remain for binary or already-merged files.
    // The identity of each side (branch · hash · subject · author) is shown below the
    // regions; Abort / Skip / Continue live in a pinned banner above the list.
    //
    // All four sequencer operations land here — merge, rebase, cherry-pick, revert —
    // because they leave the working tree in the same shape and differ only in what
    // resumes them and in which side is which (see SequencerState). Rebase in
    // particular inverts ours/theirs, which is why nothing below hard-codes the names.

    // Pinned above the file list while an operation is half-finished.
    private Element BuildSequencerBanner(ChangesData data, SequencerState state)
    {
        int left = data.Conflicted.Count;
        // Two rows, not one. Rebase adds a Skip button that merge has no use for, and
        // three buttons plus a status line do not fit the file-list column at its default
        // width — the status was the half that lost, which is the half that says what is
        // happening. Stacking keeps both legible however narrow the column is dragged.
        var bar = Flex.Column(gap: 7).FlexShrink(0)
            .Padding(new Thickness(9, 12, 9, 12))
            .Background(Nocturne.Surface)
            .BorderColor(Nocturne.Divider).BorderWidth(new Thickness(0, 0, 1, 0));

        var status = Flex.Row(gap: 8).AlignItems(AlignItems.Center);
        status.AddChild(Ui.Icon(SequencerGlyph(state.Op), 14f,
            left > 0 ? Nocturne.DangerIcon : Nocturne.Accent).FlexShrink(0));

        // "Rebase 3 of 7 · 2 conflicts left". The progress half only exists for a
        // replay of several commits, and the conflict half only while some are left —
        // a paused-but-clean rebase (an `edit` stop) is a real state and says so.
        var bits = new List<string> { state.Name };
        if (state.Progress.Length > 0) bits.Add(state.Progress);
        var label = string.Join(" ", bits) + " · " + (left > 0
            ? $"{left} conflict{(left == 1 ? "" : "s")} left"
            : "all resolved");
        status.AddChild(Text.New(label).FontSize(12f).FontWeight(FontWeight.Medium)
            .TextColor(Nocturne.Text).FlexGrow(1).FlexShrink(1)
            .OverflowMode(TextOverflow.Ellipsis).MaxLines(1)
            .Tooltip(SequencerTooltip(state)));
        bar.AddChild(status);

        var op = state.Op;
        var buttons = Flex.Row(gap: 6).AlignItems(AlignItems.Center);
        buttons.AddChild(ConflictButton("Abort", Icons.X, () => DoSequencerAbort(op), Nocturne.DangerText));
        if (state.CanSkip)
        {
            var skip = ConflictButton("Skip", Icons.BendDownRight, () => DoSequencerSkip(op), Nocturne.On(0.7));
            skip.Tooltip($"Drop this commit and carry on with the rest of the {state.Name.ToLowerInvariant()}");
            buttons.AddChild(skip);
        }
        buttons.AddChild(Box.New().FlexGrow(1));
        bool canContinue = left == 0;
        var cont = ConflictButton("Continue", Icons.Play,
            canContinue ? () => DoSequencerContinue(op) : null,
            canContinue ? Nocturne.Accent : Nocturne.On(0.3));
        cont.Key = "sequencer-continue-" + (canContinue ? "on" : "off");
        if (!canContinue) cont.Tooltip("Resolve every conflict first");
        buttons.AddChild(cont);
        bar.AddChild(buttons);
        return bar;
    }

    private static string SequencerGlyph(SequencerOp op) => op switch
    {
        SequencerOp.Rebase => Icons.ArrowsDownUp,
        SequencerOp.CherryPick => Icons.GitCommit,
        SequencerOp.Revert => Icons.CounterClockwise,
        _ => Icons.GitMerge,
    };

    private static string SequencerTooltip(SequencerState state) => state.Op switch
    {
        SequencerOp.Rebase =>
            $"Replaying {state.TheirsLabel} onto {state.OursLabel}. "
            + "Your own commit is the \"theirs\" side of every conflict.",
        SequencerOp.CherryPick => $"Picking onto {state.OursLabel}.",
        SequencerOp.Revert => $"Reverting on {state.OursLabel}.",
        _ => $"Merging {state.TheirsLabel} into {state.OursLabel}.",
    };

    // A conflict entry in the file list (no checkbox — you resolve via the pane).
    private Element ConflictListRow(TreeNode<GitFileChange> node)
    {
        // Conflicts are the third section of the same file list, so they select like
        // the other two (and never alongside them — see the one-section rule) and group
        // into the same folder tree.
        var f = node.Item;
        var self = new ChangeRef(f.Path, ChangeSection.Conflicted);
        var selected = _changeSelection.Contains(self);
        var focused = ChangeFocus() == self;
        var bg = selected ? Nocturne.Accent900
            : focused ? Nocturne.Neutral900
            : Color.Transparent;

        var row = Flex.Row(gap: 8).AlignItems(AlignItems.Center)
            .Padding(ChangeRowPadding(node.Depth)).CornerRadius(Nocturne.RadiusRow)
            .Cursor(CursorType.Pointer).Tooltip(f.Path);
        row.Key = "c:" + f.Path + (selected ? "#s" : "") + (focused ? "#f" : "");
        if (bg != Color.Transparent) row.Background(bg);
        if (focused) row.WithRef(_changeFocusRef);

        row.AddChild(Box.New().Width(14).FlexShrink(0)
            .Children(Ui.Icon(Icons.Warning, 12f, Nocturne.DangerIcon)));

        row.AddChild(Text.New(node.Segment).FontSize(12f).FlexGrow(1).FlexShrink(1)
            .OverflowMode(TextOverflow.Ellipsis).MaxLines(1));

        row.AddChild(Ui.Tag("conflict", Nocturne.Alpha(Nocturne.DangerText, 0.16), Nocturne.DangerText, 9.5f).FlexShrink(0));

        row.OnClick(e => OnChangeRowClick(self, e.Modifiers));
        row.On<PointerDownEvent>(e =>
        {
            if (e.Button == 2 && !_changeSelection.Contains(self))
            {
                SetChangeSelection(self);
                Rerender();
            }
        });
        row.ContextMenu(() => ConflictFileMenuItems(f));
        if (!selected) row.HoverBg(Nocturne.Neutral900, bg);
        return row;
    }

    private IReadOnlyList<ContextMenuItem> ConflictFileMenuItems(GitFileChange f)
    {
        // Whole-file resolution generalizes to a selection; the hand resolution in the
        // right pane doesn't, so "Open in VS Code" stays on the clicked file.
        var sel = ChangeActionPaths(new ChangeRef(f.Path, ChangeSection.Conflicted));
        var n = sel.Count;
        // Named after the operation in progress, because "theirs" during a rebase is the
        // user's own commit — a menu that called it "incoming" would be inviting them to
        // throw their work away.
        var op = _changes.Value.Sequencer?.State.Op ?? SequencerOp.Merge;
        var ours = $"Use ours ({OursRole(op).ToLowerInvariant()})";
        var theirs = $"Use theirs ({TheirsRole(op).ToLowerInvariant()})";
        return
        [
            Item(Icons.ArrowFatRight, n > 1 ? $"{ours} for {n} files" : ours,
                () => ResolveConflicts(sel, ours: true)),
            Item(Icons.Cloud, n > 1 ? $"{theirs} for {n} files" : theirs,
                () => ResolveConflicts(sel, ours: false)),
            ContextMenuItem.Separator(),
            Item(Icons.FileCode, "Open in VS Code", () => OpenConflictInVsCode(f.Path)),
        ];
    }

    // ── Per-hunk resolution state ────────────────────────────────────────
    //
    // The conflicted working file is parsed into context runs + conflict regions
    // (ConflictDocument). Each region gets its own choice — ours / theirs / both /
    // hand-edited — and "Mark resolved" renders the document back out and stages it.
    // Regions left unresolved re-render with their original markers, so a partial
    // save is still a valid conflicted file.

    // Bounded: one slot per conflicted file version; a big merge would otherwise
    // pin every parsed document for the session.
    private readonly KeyedAsyncLoader<string, ConflictDocument?> _conflictDocLoaders =
        new(null, capacity: 16, comparer: StringComparer.Ordinal);

    private string? _conflictKey;                                        // "repo|path|gen" the choices belong to
    private readonly Dictionary<int, ConflictChoice> _conflictChoices = new();
    private readonly Dictionary<int, string> _conflictManual = new();
    private int? _conflictEditing;                                       // hunk being hand-edited
    private string _conflictEditText = "";
    private readonly Ref<TextAreaElement> _conflictEditRef = new();
    private bool _pendingConflictEditFocus;

    // Runs off the UI thread.
    private ConflictDocument? LoadConflictDoc(string repoPath, string path)
    {
        try
        {
            var full = System.IO.Path.Combine(repoPath, path);
            return File.Exists(full) ? ConflictDocument.Parse(File.ReadAllText(full)) : null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Drops choices when the file (or its on-disk generation) changes, so a
    /// previous file's picks can't leak into this one.</summary>
    private void SyncConflictChoices(string key)
    {
        if (_conflictKey == key) return;
        _conflictKey = key;
        _conflictChoices.Clear();
        _conflictManual.Clear();
        _conflictEditing = null;
    }

    // The right pane for a selected conflicted file: per-region resolution plus the
    // identity of each side (what "ours" and "theirs" actually are).
    private Element BuildConflictResolutionPane(string path, ConflictSides? sides)
    {
        var key = $"{_activeRepoPath}|{path}|{DataGen(_activeRepoPath)}";
        SyncConflictChoices(key);
        var loader = _conflictDocLoaders.For(key);
        var repoPath = _activeRepoPath;
        loader.Load(key, () => LoadConflictDoc(repoPath, path));
        var doc = loader.Value;

        var col = Flex.Column(gap: 0).FlexGrow(1);

        var header = Flex.Row(gap: 10).AlignItems(AlignItems.Center).FlexShrink(0)
            .Padding(new Thickness(8, 12, 8, 12))
            .BorderColor(Nocturne.Divider).BorderWidth(new Thickness(0, 0, 1, 0));
        header.AddChild(Ui.Mono(path.Length > 0 ? path : "—", 12f, Nocturne.Text)
            .FlexGrow(1).FlexShrink(1).OverflowMode(TextOverflow.Ellipsis).MaxLines(1));
        header.AddChild(Ui.Tag(
                sides is { } s ? s.State.Name.ToLowerInvariant() + " conflict" : "conflict",
                Nocturne.Alpha(Nocturne.DangerText, 0.16), Nocturne.DangerText, 10f)
            .FlexShrink(0));
        col.AddChild(header);

        if (doc is { HasConflicts: true })
            col.AddChild(BuildConflictActionBar(path, doc));

        var body = ScrollView.New().FlexGrow(1).Padding(new Thickness(14));
        var inner = Flex.Column(gap: 12);

        // Which side is which comes first — you need to know what "ours" and
        // "theirs" mean before choosing between them, and during a rebase they are the
        // opposite way round from everyone's intuition.
        if (sides is { } sd)
        {
            var st = sd.State;
            inner.AddChild(MergeSideCard($"{TheirsRole(st.Op)} · theirs", st.TheirsLabel, sd.Theirs,
                Nocturne.Accent2_400, path));
            inner.AddChild(MergeSideCard($"{OursRole(st.Op)} · ours", st.OursLabel, sd.Ours,
                Nocturne.Accent400, path));
        }

        if (doc == null)
        {
            inner.AddChild(Text.New("Reading file…").FontSize(12.5f).TextColor(Nocturne.On(0.5)));
        }
        else if (!doc.HasConflicts)
        {
            // Binary file, or markers already removed by hand.
            inner.AddChild(Text.New("No conflict markers found in this file — it may be binary, or already merged by hand.")
                .FontSize(12.5f).TextColor(Nocturne.On(0.7)));
            var btns = Flex.Row(gap: 8).AlignItems(AlignItems.Center);
            btns.AddChild(ConflictButton("Use ours", null, () => ResolveConflict(path, ours: true), Nocturne.Accent300));
            btns.AddChild(ConflictButton("Use theirs", null, () => ResolveConflict(path, ours: false), Nocturne.Accent2_300));
            btns.AddChild(ConflictButton("Mark resolved as-is", Icons.Check, () => StageResolved(path), Nocturne.Accent));
            btns.AddChild(ConflictButton("Open in VS Code", Icons.FileCode, () => OpenConflictInVsCode(path), Nocturne.On(0.75)));
            inner.AddChild(btns);
        }
        else
        {
            foreach (var h in doc.Hunks)
                inner.AddChild(ConflictHunkCard(h, doc.Hunks.Count));
        }

        body.AddChild(inner);
        col.AddChild(body);
        return col;
    }

    // Sticky summary + whole-file escapes above the regions.
    /// <summary>Hands the file to the chosen merge tool and reloads it once the tool closes,
    /// so what the tool wrote is what the pane shows.</summary>
    private async void OpenConflictInMergeTool(string path)
    {
        if (ActiveRepo is not { } repo) return;
        var tools = _settings.ExternalTools;
        Flash($"Opening {path} in {ExternalMergeLauncher.Name(tools)}…");
        var (ok, err) = await Task.Run(() => (ExternalMergeLauncher.TryRun(tools, repo, path, out var e), e));
        if (!ok) { Flash(string.IsNullOrEmpty(err) ? "Could not open the merge tool" : err); return; }
        RefreshAll();
    }

    private Element BuildConflictActionBar(string path, ConflictDocument doc)
    {
        int total = doc.Hunks.Count;
        int done = doc.Hunks.Count(h => _conflictChoices.GetValueOrDefault(h.Index) != ConflictChoice.Unresolved);
        bool all = done == total;

        var bar = Flex.Row(gap: 8).FlexShrink(0).AlignItems(AlignItems.Center)
            .Padding(new Thickness(7, 12, 7, 12))
            .Background(Nocturne.Alpha(all ? Nocturne.Accent : Nocturne.DangerText, 0.07))
            .BorderColor(Nocturne.Divider).BorderWidth(new Thickness(0, 0, 1, 0));

        bar.AddChild(Text.New($"{done} of {total} resolved").FontSize(12f)
            .TextColor(all ? Nocturne.Accent300 : Nocturne.On(0.7)).FlexGrow(1));

        bar.AddChild(ConflictButton("All ours", null, () => ChooseAll(doc, ConflictChoice.Ours), Nocturne.Accent300));
        bar.AddChild(ConflictButton("All theirs", null, () => ChooseAll(doc, ConflictChoice.Theirs), Nocturne.Accent2_300));
        bar.AddChild(ConflictButton("Open in VS Code", Icons.FileCode, () => OpenConflictInVsCode(path), Nocturne.On(0.7)));
        // The three-way tool from Options › External tools, when one is chosen.
        if (ExternalMergeLauncher.Command(_settings.ExternalTools) is not null)
            bar.AddChild(ConflictButton($"Open in {ExternalMergeLauncher.Name(_settings.ExternalTools)}", Icons.GitMerge,
                () => OpenConflictInMergeTool(path), Nocturne.On(0.7)));

        var save = ConflictButton(all ? "Mark resolved" : $"Resolve {total - done} more",
            Icons.Check, all ? () => ApplyConflictResolution(path, doc) : null,
            all ? Nocturne.Accent : Nocturne.On(0.3));
        save.Key = "conflict-save-" + (all ? "on" : "off");
        bar.AddChild(save);
        return bar;
    }

    private Element ConflictHunkCard(ConflictHunk h, int total)
    {
        var choice = _conflictChoices.GetValueOrDefault(h.Index, ConflictChoice.Unresolved);
        bool resolved = choice != ConflictChoice.Unresolved;
        bool editing = _conflictEditing == h.Index;

        var card = Flex.Column(gap: 8)
            .Padding(new Thickness(10, 12, 10, 12))
            .Background(Nocturne.Surface)
            .BorderColor(resolved ? Nocturne.Alpha(Nocturne.Accent, 0.5) : Nocturne.Divider).BorderWidth(1f)
            .CornerRadius(Nocturne.RadiusSm);
        // Re-key on state so handlers refresh with the rendered variant.
        card.Key = $"chunk:{h.Index}:{choice}:{(editing ? "e" : "")}";

        var top = Flex.Row(gap: 8).AlignItems(AlignItems.Center);
        top.AddChild(Text.New($"Conflict {h.Index + 1} of {total}").FontSize(11f)
            .FontWeight(FontWeight.Medium).TextColor(Nocturne.On(0.6)).FlexGrow(1));
        if (resolved)
            top.AddChild(Ui.Tag(ChoiceLabel(choice), Nocturne.Alpha(Nocturne.Accent, 0.16), Nocturne.Accent300, 9.5f));
        card.AddChild(top);

        if (editing)
        {
            var editBox = Flex.Column(gap: 0)
                .Padding(new Thickness(6, 8, 6, 8))
                .Background(Nocturne.Bg).BorderColor(Nocturne.Accent).BorderWidth(1f)
                .CornerRadius(Nocturne.RadiusSm);
            var ta = TextArea.New(_conflictEditText);
            ta.OnTextChanged(v => { _conflictEditText = v; })
              .Height(Dimension.Px(120)).TextColor(Nocturne.Text).WithRef(_conflictEditRef);
            editBox.AddChild(ta);
            card.AddChild(editBox);

            var eb = Flex.Row(gap: 6).AlignItems(AlignItems.Center);
            eb.AddChild(ConflictButton("Save", Icons.Check, () => SaveManualEdit(h.Index), Nocturne.Accent));
            eb.AddChild(ConflictButton("Cancel", Icons.X, CancelManualEdit, Nocturne.On(0.6)));
            card.AddChild(eb);
            return card;
        }

        var actions = Flex.Row(gap: 6).AlignItems(AlignItems.Center);
        actions.AddChild(ChoiceButton("Ours", h.Index, ConflictChoice.Ours, choice, Nocturne.Accent300));
        actions.AddChild(ChoiceButton("Theirs", h.Index, ConflictChoice.Theirs, choice, Nocturne.Accent2_300));
        actions.AddChild(ChoiceButton("Both", h.Index, ConflictChoice.Both, choice, Nocturne.On(0.75)));
        actions.AddChild(ConflictButton("Edit", Icons.PencilSimple, () => BeginManualEdit(h), Nocturne.On(0.7)));
        if (resolved)
            actions.AddChild(ConflictButton("Reset", null, () => ClearChoice(h.Index), Nocturne.On(0.55)));
        card.AddChild(actions);

        if (choice == ConflictChoice.Manual)
        {
            card.AddChild(SideBlock("RESULT (edited)", SplitManual(h.Index), Nocturne.Accent, resolved));
        }
        else
        {
            card.AddChild(SideBlock($"OURS · {h.OursLabel}", h.Ours, Nocturne.Accent400,
                choice is ConflictChoice.Ours or ConflictChoice.Both));
            if (h.HasBase)
                card.AddChild(SideBlock($"BASE · {h.BaseLabel}", h.Base, Nocturne.On(0.4), false));
            card.AddChild(SideBlock($"THEIRS · {h.TheirsLabel}", h.Theirs, Nocturne.Accent2_400,
                choice is ConflictChoice.Theirs or ConflictChoice.Both));
        }
        return card;
    }

    // A labelled block of code lines; `kept` tints it as part of the resolution.
    private const int MaxConflictLinesShown = 40;

    private static Element SideBlock(string label, IReadOnlyList<string> lines, Color accent, bool kept)
    {
        var box = Flex.Column(gap: 2)
            .Padding(new Thickness(5, 8, 5, 8))
            .Background(kept ? Nocturne.Alpha(accent, 0.12) : Nocturne.Alpha(Nocturne.Neutral900, 0.6))
            .BorderColor(kept ? Nocturne.Alpha(accent, 0.5) : Nocturne.Divider).BorderWidth(1f)
            .CornerRadius(Nocturne.RadiusSm);

        var head = Flex.Row(gap: 6).AlignItems(AlignItems.Center);
        head.AddChild(Text.New(label).FontSize(9.5f).FontWeight(FontWeight.Medium)
            .TextColor(accent).FlexGrow(1).OverflowMode(TextOverflow.Ellipsis).MaxLines(1));
        if (kept) head.AddChild(Ui.Icon(Icons.Check, 10f, accent).FlexShrink(0));
        box.AddChild(head);

        if (lines.Count == 0)
        {
            box.AddChild(Ui.Mono("(empty)", 11f, Nocturne.On(0.35)));
            return box;
        }
        foreach (var line in lines.Take(MaxConflictLinesShown))
            box.AddChild(Ui.Mono(line.Length == 0 ? " " : line, 11.5f, Nocturne.On(0.85))
                .Selectable()
                .MaxLines(1).OverflowMode(TextOverflow.Ellipsis));
        if (lines.Count > MaxConflictLinesShown)
            box.AddChild(Text.New($"… {lines.Count - MaxConflictLinesShown} more lines")
                .FontSize(10f).TextColor(Nocturne.On(0.4)));
        return box;
    }

    private Element ChoiceButton(string label, int index, ConflictChoice value, ConflictChoice current, Color fg)
    {
        bool on = current == value;
        var b = Flex.Row(gap: 0).FlexShrink(0).AlignItems(AlignItems.Center).JustifyContent(JustifyContent.Center)
            .Padding(new Thickness(4, 10, 4, 10)).CornerRadius(Nocturne.RadiusSm)
            .BorderColor(on ? fg : Nocturne.Divider).BorderWidth(1f)
            .Cursor(CursorType.Pointer);
        if (on) b.Background(Nocturne.Alpha(fg, 0.16));
        b.AddChild(Text.New(label).FontSize(11f).FontWeight(FontWeight.Medium)
            .TextColor(on ? fg : Nocturne.On(0.65)));
        b.Key = $"cc:{index}:{value}:{(on ? "on" : "off")}";
        b.OnClick(e => { e.StopPropagation = true; SetChoice(index, on ? ConflictChoice.Unresolved : value); });
        if (!on) b.HoverBg(Nocturne.Alpha(fg, 0.08), Color.Transparent);
        return b;
    }

    private static string ChoiceLabel(ConflictChoice c) => c switch
    {
        ConflictChoice.Ours => "ours",
        ConflictChoice.Theirs => "theirs",
        ConflictChoice.Both => "both",
        ConflictChoice.BothReversed => "both (theirs first)",
        ConflictChoice.Manual => "edited",
        _ => "",
    };

    private IReadOnlyList<string> SplitManual(int index) =>
        _conflictManual.TryGetValue(index, out var t)
            ? t.Replace("\r\n", "\n").TrimEnd('\n').Split('\n')
            : [];

    /// <param name="filePath">The conflicted file, so clicking the hash opens that
    /// side's commit with this file already selected.</param>
    private Element MergeSideCard(string role, string? branch, GitCommit? commit, Color accent,
        string? filePath = null)
    {
        var card = Flex.Column(gap: 4)
            .Padding(new Thickness(9, 11, 9, 11))
            .Background(Nocturne.Surface)
            .BorderColor(Nocturne.Divider).BorderWidth(1f)
            .CornerRadius(Nocturne.RadiusSm);

        var top = Flex.Row(gap: 6).AlignItems(AlignItems.Center);
        top.AddChild(Text.New(role.ToUpperInvariant()).FontSize(9.5f)
            .FontWeight(FontWeight.Medium).TextColor(accent).FlexGrow(1));
        if (!string.IsNullOrEmpty(branch))
            top.AddChild(Ui.Chip(Icons.GitBranch, branch, Nocturne.Neutral800, Nocturne.Neutral100).FlexShrink(0));
        card.AddChild(top);

        if (commit is { } c)
        {
            var line = Flex.Row(gap: 8).AlignItems(AlignItems.Center);
            line.AddChild(CommitHashLink(c, accent, filePath));
            line.AddChild(Text.New(c.Subject).FontSize(12f).TextColor(Nocturne.Text)
                .FlexGrow(1).FlexShrink(1).OverflowMode(TextOverflow.Ellipsis).MaxLines(1));
            card.AddChild(line);
            card.AddChild(Text.New(c.AuthorName).FontSize(11f).TextColor(Nocturne.On(0.5)));
        }
        else
        {
            card.AddChild(Text.New("(commit unavailable)").FontSize(11f).TextColor(Nocturne.On(0.4)));
        }
        return card;
    }

    /// <summary>The short hash as a link: opens that side's commit in the Commit view
    /// with <paramref name="filePath"/> selected, so you can read what that side
    /// actually changed before deciding. (If the commit doesn't touch that file, the
    /// Commit view falls back to its first file.)</summary>
    private Element CommitHashLink(GitCommit c, Color accent, string? filePath)
    {
        var box = Flex.Row(gap: 4).FlexShrink(0).AlignItems(AlignItems.Center)
            .Padding(new Thickness(1, 5, 1, 5)).CornerRadius(Nocturne.RadiusSm)
            .Cursor(CursorType.Pointer)
            .Tooltip($"Open {c.ShortHash} in the Commit view");
        box.AddChild(Ui.Mono(c.ShortHash, 11f, accent));
        box.AddChild(Ui.Icon(Icons.ArrowRight, 9f, Nocturne.Alpha(accent, 0.7)));
        box.Key = "hashlink:" + c.Hash + ":" + (filePath ?? "");
        box.OnClick(e => { e.StopPropagation = true; OpenCommitForSide(c.Hash, filePath); });
        box.HoverBg(Nocturne.Alpha(accent, 0.16), Color.Transparent);
        return box;
    }

    private void OpenCommitForSide(string hash, string? filePath)
    {
        SetCommitSelection(hash);                  // collapses any range compare too
        if (!string.IsNullOrEmpty(filePath)) _selectedFilePath = filePath;
        PrefetchCommitDetail(hash);
        SetView(JunctionView.Commit);              // reactive → tracked rebuild
    }

    // A small bordered action button; a null onClick renders it disabled (used for
    // Continue while conflicts remain).
    private static BoxElement ConflictButton(string label, string? glyph, Action? onClick, Color fg)
    {
        var b = Flex.Row(gap: 5).FlexShrink(0).AlignItems(AlignItems.Center).JustifyContent(JustifyContent.Center)
            .Padding(new Thickness(4, 9, 4, 9)).CornerRadius(Nocturne.RadiusSm)
            .BorderColor(Nocturne.Divider).BorderWidth(1f);
        if (glyph != null) b.AddChild(Ui.Icon(glyph, 12f, fg));
        b.AddChild(Text.New(label).FontSize(11f).FontWeight(FontWeight.Medium).TextColor(fg));
        if (onClick != null)
        {
            b.Cursor(CursorType.Pointer);
            b.OnClick(e => { e.StopPropagation = true; onClick(); });
            b.HoverBg(Nocturne.Alpha(fg, 0.10), Color.Transparent);
        }
        return b;
    }

    // ── Per-hunk resolution actions ──────────────────────────────────────

    private void SetChoice(int index, ConflictChoice choice)
    {
        if (choice == ConflictChoice.Unresolved) _conflictChoices.Remove(index);
        else _conflictChoices[index] = choice;
        MarkDirty();
    }

    private void ClearChoice(int index)
    {
        _conflictChoices.Remove(index);
        _conflictManual.Remove(index);
        MarkDirty();
    }

    private void ChooseAll(ConflictDocument doc, ConflictChoice choice)
    {
        foreach (var h in doc.Hunks) _conflictChoices[h.Index] = choice;
        MarkDirty();
    }

    private void BeginManualEdit(ConflictHunk h)
    {
        // Seed with the current resolution (or ours) so editing starts from something.
        var choice = _conflictChoices.GetValueOrDefault(h.Index, ConflictChoice.Unresolved);
        if (choice == ConflictChoice.Manual && _conflictManual.TryGetValue(h.Index, out var existing))
        {
            _conflictEditText = existing;
        }
        else
        {
            var seed = ConflictDocument.Resolve(h,
                choice == ConflictChoice.Unresolved ? ConflictChoice.Ours : choice, null);
            _conflictEditText = seed.Count > 0 ? string.Join("\n", seed) + "\n" : "";
        }
        _conflictEditing = h.Index;
        _pendingConflictEditFocus = true;
        MarkDirty();
    }

    private void SaveManualEdit(int index)
    {
        _conflictManual[index] = _conflictEditText;
        _conflictChoices[index] = ConflictChoice.Manual;
        _conflictEditing = null;
        MarkDirty();
    }

    private void CancelManualEdit()
    {
        _conflictEditing = null;
        MarkDirty();
    }

    /// <summary>Renders the document with the chosen resolutions, writes it back, and
    /// stages the file (which is what clears it from git's unmerged list).</summary>
    private void ApplyConflictResolution(string path, ConflictDocument doc)
    {
        if (ActiveRepo is not { } repo) return;
        try
        {
            var text = doc.Render(_conflictChoices, _conflictManual);
            File.WriteAllText(System.IO.Path.Combine(repo.Path, path), text);
        }
        catch (Exception ex)
        {
            Flash("Could not write the file: " + ex.Message);
            return;
        }
        StageResolved(path);
    }

    /// <summary>Stages a now-merged file, clearing it from git's unmerged list.</summary>
    private void StageResolved(string path)
    {
        if (ActiveRepo is not { } repo) return;
        if (!repo.Stage(path)) { Flash("Could not stage " + System.IO.Path.GetFileName(path)); return; }
        _conflictChoices.Clear();
        _conflictManual.Clear();
        _conflictEditing = null;
        // Keep the file selected as a normal staged change so its merged diff shows.
        RetargetChangeSelection([path], ChangeSection.Staged);
        Flash($"Resolved {System.IO.Path.GetFileName(path)}");
        RefreshAll();
    }

    // ── Actions ──────────────────────────────────────────────────────────

    private void ResolveConflict(string path, bool ours) => ResolveConflicts([path], ours);

    /// <summary>Takes one whole side for every given conflicted file. Each file is its
    /// own checkout, so a failure on one leaves the others resolved — the toast names
    /// the first error rather than pretending the batch was atomic.</summary>
    private void ResolveConflicts(IReadOnlyList<string> paths, bool ours)
    {
        if (ActiveRepo is not { } repo || paths.Count == 0) return;
        string? error = null;
        var done = 0;
        foreach (var path in paths)
        {
            var res = ours ? repo.CheckoutOurs(path) : repo.CheckoutTheirs(path);
            if (res.Success) done++;
            else error ??= FirstLine(res.Stderr, "Resolve failed");
        }
        if (done > 0)
        {
            // Resolved files are staged; keep them selected as normal changes so the
            // (now-merged) diff shows, and the next conflict is a click away.
            RetargetChangeSelection(paths, ChangeSection.Staged);
        }
        Flash(error ?? (paths.Count == 1
            ? $"Used {(ours ? "ours" : "theirs")} for {System.IO.Path.GetFileName(paths[0])}"
            : $"Used {(ours ? "ours" : "theirs")} for {paths.Count} files"));
        RefreshAll();
    }

    private async void OpenConflictInVsCode(string path)
    {
        if (ActiveRepo is not { } repo) return;
        Flash("Opening in VS Code…");
        var full = System.IO.Path.Combine(repo.Path, path);
        var (ok, err) = await Task.Run(() =>
        {
            bool o = ExternalDiffLauncher.TryOpen(repo.Path, "code", [full], out var e);
            return (o, e);
        });
        if (!ok) Flash(string.IsNullOrEmpty(err) ? "Could not open VS Code" : err);
    }

    /// <summary>What the checked-out side is, in each operation's own words. "Current"
    /// is right for three of the four; during a rebase the checked-out side is the branch
    /// being replayed onto, which is anything but the user's current work.</summary>
    private static string OursRole(SequencerOp op) => op switch
    {
        SequencerOp.Rebase => "New base",
        _ => "Current",
    };

    private static string TheirsRole(SequencerOp op) => op switch
    {
        SequencerOp.Rebase => "Your commit",
        SequencerOp.CherryPick => "Picked commit",
        SequencerOp.Revert => "Reverted commit",
        _ => "Incoming",
    };

    private void DoSequencerContinue(SequencerOp op)
    {
        if (ActiveRepo is not { } repo) return;
        var res = repo.SequencerContinue(op);
        // A rebase that continues into the *next* conflict exits non-zero too, so the
        // failure message is only a failure when the operation actually ended.
        if (res.Success)
            Flash(repo.GetSequencerOp() == SequencerOp.None
                ? $"{OpName(op)} completed"
                : $"{OpName(op)} — next conflict");
        else
            Flash(FirstLine(res.Stderr, "Resolve every conflict first"));
        RefreshAll();
    }

    private void DoSequencerAbort(SequencerOp op)
    {
        if (ActiveRepo is not { } repo) return;
        var res = repo.SequencerAbort(op);
        Flash(res.Success ? $"{OpName(op)} aborted" : FirstLine(res.Stderr, "Abort failed"));
        RefreshAll();
    }

    private void DoSequencerSkip(SequencerOp op)
    {
        if (ActiveRepo is not { } repo) return;
        var res = repo.SequencerSkip(op);
        if (res.Success)
            Flash(repo.GetSequencerOp() == SequencerOp.None
                ? $"{OpName(op)} completed — commit skipped"
                : "Commit skipped");
        else
            Flash(FirstLine(res.Stderr, "Skip failed"));
        RefreshAll();
    }

    private static string OpName(SequencerOp op) => op switch
    {
        SequencerOp.Merge => "Merge",
        SequencerOp.Rebase => "Rebase",
        SequencerOp.CherryPick => "Cherry-pick",
        SequencerOp.Revert => "Revert",
        _ => "Operation",
    };

    // ── Repository-level markers ─────────────────────────────────────────
    //
    // The banner above only exists inside the Changes view, which is no use at all to
    // someone whose pull stopped on conflicts in the third repository of a workspace:
    // every other surface showed the same accent dot it shows for an unsaved edit. These
    // two are what the sidebar row, the collapsed rail and the Overview card wear instead.

    /// <summary>The chip a repository row wears while git is half way through an
    /// operation: the operation's own glyph and how many conflicts are still unresolved.
    /// Danger-coloured, because unlike a dirty working tree this is a state the repository
    /// cannot be left in.</summary>
    private static Element SequencerBadge(RepoSummary sum)
    {
        var chip = Flex.Row(gap: 3).AlignItems(AlignItems.Center).FlexShrink(0)
            .Padding(new Thickness(2, 6, 2, 6)).CornerRadius(Nocturne.RadiusMd * 0.75f)
            .Background(Nocturne.Alpha(Nocturne.DangerText, 0.16))
            .Tooltip(SequencerSummary(sum));
        chip.AddChild(Ui.Icon(SequencerGlyph(sum.Op), 10f, Nocturne.DangerText));
        if (sum.Conflicts > 0)
            chip.AddChild(Text.New(sum.Conflicts.ToString()).FontSize(9.5f)
                .FontWeight(FontWeight.Medium).TextColor(Nocturne.DangerText));
        return chip;
    }

    /// <summary>One line naming the state and what it is waiting for. A sequencer with
    /// nothing left to resolve is a real state — the work is done and git wants the
    /// continue — and says so rather than reading as "nothing to see here".</summary>
    private static string SequencerSummary(RepoSummary sum) => sum.Conflicts > 0
        ? $"{OpName(sum.Op)} stopped on {sum.Conflicts} conflict{(sum.Conflicts == 1 ? "" : "s")} — resolve them in Changes"
        : $"{OpName(sum.Op)} in progress — every conflict resolved, waiting to continue";

    /// <summary>The same state as a tag, for the Overview card, where it takes the place of
    /// the "N changed" count: the count is true but it is not the thing to act on.</summary>
    private static Element SequencerTag(RepoSummary sum) =>
        Ui.Tag(sum.Conflicts > 0
                    ? $"{OpName(sum.Op)} · {sum.Conflicts} conflict{(sum.Conflicts == 1 ? "" : "s")}"
                    : $"{OpName(sum.Op)} · ready to continue",
                Nocturne.Alpha(Nocturne.DangerText, 0.16), Nocturne.DangerText, 10f)
            .Tooltip(SequencerSummary(sum));
}
