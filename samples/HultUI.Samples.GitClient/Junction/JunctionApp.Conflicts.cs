using HultUI;
using HultUI.Core.Builders;
using HultUI.Core.Elements;
using HultUI.Core.Events;
using HultUI.Core.Layout;
using HultUI.Core.Primitives;
using HultUI.Core.Reactivity;
using HultUI.Core.Styling;
using GitClient.Git;
using GitClient.Theme;

namespace GitClient.Junction;

public partial class JunctionApp
{
    // ── Merge-conflict resolution (Changes view) ─────────────────────────────
    //
    // Conflicts appear in the file list like any other change; selecting one swaps
    // the diff pane for a resolution view. The conflicted working file is parsed
    // into its marked regions, and each is resolved independently — ours / theirs /
    // both / hand-edited — then written back and staged. Whole-file escapes (checkout
    // --ours/--theirs, open in VS Code) remain for binary or already-merged files.
    // The identity of each side (branch · hash · subject · author) is shown below the
    // regions; merge-level Abort / Continue live in a pinned banner above the list.

    // Pinned above the file list while a merge is in progress.
    private Element BuildMergeBanner(ChangesData data)
    {
        int left = data.Conflicted.Count;
        var bar = Flex.Row(gap: 8).FlexShrink(0).AlignItems(AlignItems.Center)
            .Padding(new Thickness(9, 12, 9, 12))
            .Background(Nocturne.Surface)
            .BorderColor(Nocturne.Divider).BorderWidth(new Thickness(0, 0, 1, 0));

        bar.AddChild(Ui.Icon(Icons.GitMerge, 14f, Nocturne.DangerIcon).FlexShrink(0));
        var label = left > 0
            ? $"Merge · {left} conflict{(left == 1 ? "" : "s")} left"
            : "Merge · all resolved";
        bar.AddChild(Text.New(label).FontSize(12f).FontWeight(FontWeight.Medium)
            .TextColor(Nocturne.Text).FlexGrow(1).FlexShrink(1)
            .OverflowMode(TextOverflow.Ellipsis).MaxLines(1));

        bar.AddChild(ConflictButton("Abort", Icons.X, DoMergeAbort, Nocturne.DangerText));
        bool canContinue = left == 0;
        var cont = ConflictButton("Continue", Icons.Play,
            canContinue ? DoMergeContinue : null, canContinue ? Nocturne.Accent : Nocturne.On(0.3));
        cont.Key = "merge-continue-" + (canContinue ? "on" : "off");
        bar.AddChild(cont);
        return bar;
    }

    // A conflict entry in the file list (no checkbox — you resolve via the pane).
    private Element ConflictListRow(GitFileChange f)
    {
        var selected = _changeSelConflicted && f.Path == _changeSelPath;
        var name = System.IO.Path.GetFileName(f.Path);
        var dir = f.Path.Length > name.Length ? f.Path[..^name.Length] : "";

        var row = Flex.Row(gap: 8).AlignItems(AlignItems.Center)
            .Padding(new Thickness(6, 8, 6, 8)).CornerRadius(Nocturne.RadiusRow)
            .Cursor(CursorType.Pointer).Tooltip(f.Path);
        row.Key = "c:" + f.Path + (selected ? "#s" : "");
        if (selected) row.Background(Nocturne.Accent900);

        row.AddChild(Box.New().Width(14).FlexShrink(0)
            .Children(Ui.Icon(Icons.Warning, 12f, Nocturne.DangerIcon)));

        var text = Flex.Column(gap: 0).FlexGrow(1).FlexShrink(1);
        text.AddChild(Text.New(name).FontSize(12f)
            .OverflowMode(TextOverflow.Ellipsis).MaxLines(1));
        if (dir.Length > 0)
            text.AddChild(Text.New(dir.TrimEnd('/', '\\')).FontSize(9.5f).TextColor(Nocturne.On(0.40))
                .OverflowMode(TextOverflow.Ellipsis).MaxLines(1));
        row.AddChild(text);

        row.AddChild(Ui.Tag("conflict", Nocturne.Alpha(Nocturne.DangerText, 0.16), Nocturne.DangerText, 9.5f).FlexShrink(0));

        row.OnClick(_ => SelectConflict(f.Path));
        row.On<PointerDownEvent>(e => { if (e.Button == 2) SelectConflict(f.Path); });
        row.ContextMenu(() => ConflictFileMenuItems(f));
        if (!selected) row.HoverBg(Nocturne.Neutral900, Color.Transparent);
        return row;
    }

    private IReadOnlyList<ContextMenuItem> ConflictFileMenuItems(GitFileChange f) =>
    [
        Item(Icons.ArrowFatRight, "Use ours (current)", () => ResolveConflict(f.Path, ours: true)),
        Item(Icons.Cloud, "Use theirs (incoming)", () => ResolveConflict(f.Path, ours: false)),
        ContextMenuItem.Separator(),
        Item(Icons.FileCode, "Open in VS Code", () => OpenConflictInVsCode(f.Path)),
    ];

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
    private Element BuildConflictResolutionPane(string path, MergeSides? merge)
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
        header.AddChild(Ui.Tag("merge conflict", Nocturne.Alpha(Nocturne.DangerText, 0.16), Nocturne.DangerText, 10f)
            .FlexShrink(0));
        col.AddChild(header);

        if (doc is { HasConflicts: true })
            col.AddChild(BuildConflictActionBar(path, doc));

        var body = ScrollView.New().FlexGrow(1).Padding(new Thickness(14));
        var inner = Flex.Column(gap: 12);

        // Which side is which comes first — you need to know what "ours" and
        // "theirs" mean before choosing between them.
        if (merge is { } sides)
        {
            inner.AddChild(MergeSideCard("Incoming · theirs", sides.TheirsBranch, sides.Theirs,
                Nocturne.Accent2_400, path));
            inner.AddChild(MergeSideCard("Current · ours", sides.OursBranch, sides.Ours,
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
        _changeSelConflicted = false;
        _changeSelStaged = true;
        Flash($"Resolved {System.IO.Path.GetFileName(path)}");
        RefreshAll();
    }

    // ── Actions ──────────────────────────────────────────────────────────

    private void ResolveConflict(string path, bool ours)
    {
        if (ActiveRepo is not { } repo) return;
        var res = ours ? repo.CheckoutOurs(path) : repo.CheckoutTheirs(path);
        if (res.Success)
        {
            // Resolved files are staged; keep this one selected as a normal change so
            // its (now-merged) diff shows, and the next conflict is a click away.
            _changeSelConflicted = false;
            _changeSelStaged = true;
        }
        Flash(res.Success
            ? $"Used {(ours ? "ours" : "theirs")} for {System.IO.Path.GetFileName(path)}"
            : FirstLine(res.Stderr, "Resolve failed"));
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

    private void DoMergeContinue()
    {
        if (ActiveRepo is not { } repo) return;
        var res = repo.MergeContinue();
        Flash(res.Success ? "Merge completed" : FirstLine(res.Stderr, "Resolve every conflict first"));
        RefreshAll();
    }

    private void DoMergeAbort()
    {
        if (ActiveRepo is not { } repo) return;
        var res = repo.MergeAbort();
        Flash(res.Success ? "Merge aborted" : FirstLine(res.Stderr, "Abort failed"));
        RefreshAll();
    }
}
