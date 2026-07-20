using HultUI.Core.Builders;
using HultUI.Core.Elements;
using HultUI.Core.Events;
using HultUI.Core.Layout;
using HultUI.Core.Primitives;
using HultUI.Core.Styling;
using HultUI.Core.Rendering;
using GitClient.Git;
using GitClient.Theme;

namespace GitClient.Junction;

public partial class JunctionApp
{
    private const float LaneWidth = 18f;
    private const float DotRadius = 5.5f;
    private const float RowHeight = 54f;

    private static readonly Color[] LanePalette =
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

        var include = IncludeSet(_activeRepoPath);
        var includeList = include.OrderBy(x => x, StringComparer.Ordinal).ToList();
        var key = $"{_activeRepoPath}|{string.Join(",", includeList)}|{DataGen(_activeRepoPath)}";
        _history.Load(key, () => LoadHistory(_activeRepoPath, includeList), onLoaded: PickInitialCommit);
        var data = _history.Value;
        var filtered = FilteredCommits(data);

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
        col.AddChild(BuildIncludeBar(includeList, filtered.Count));
        col.AddChild(BuildCommitList(data, filtered));
        col.AddChild(BuildHistoryFooter());
        return col;
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
    private HistoryData LoadHistory(string path, List<string> includeList)
    {
        var repo = RepoFor(path);
        var refs = includeList.Count > 0 ? includeList : null;
        var commits = repo.GetLog(includeRefs: refs);

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
        var stats = repo.GetLogStats(refs: refs);
        var remotes = new HashSet<string>(repo.GetRemotes(), StringComparer.Ordinal);
        return new HistoryData(commits, graph, stats, remotes);
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
        MarkDirty();
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

    private Element BuildIncludeBar(List<string> includeList, int commitCount)
    {
        var bar = Flex.Row(gap: 10)
            .FlexShrink(0).AlignItems(AlignItems.Center)
            .Padding(new Thickness(9, 16, 9, 16))
            .BorderColor(Nocturne.Divider).BorderWidth(new Thickness(0, 0, 1, 0));

        bar.AddChild(Flex.Row(gap: 6).AlignItems(AlignItems.Center).Children(
            Ui.Icon(Icons.Funnel, 13f, Nocturne.On(0.55)),
            Text.New("Included").FontSize(11f).TextColor(Nocturne.On(0.55))));

        if (includeList.Count == 0)
        {
            bar.AddChild(Ui.TagOutline("All branches", Nocturne.Accent, 10f));
        }
        else
        {
            var chips = Flex.Row(gap: 6).AlignItems(AlignItems.Center);
            foreach (var name in includeList)
            {
                var pb = BranchName.Parse(name);
                chips.AddChild(Ui.Tag(pb.Title, pb.ChipBg, pb.ChipFg, 10f));
            }
            chips.AddChild(Ui.GhostButton("Clear", Icons.X, ClearInclude, 11f));
            bar.AddChild(chips);
        }

        bar.AddChild(Box.New().FlexGrow(1));
        bar.AddChild(BuildHistorySearch());
        bar.AddChild(Text.New($"{commitCount} commits").FontSize(11f).TextColor(Nocturne.On(0.40)));
        return bar;
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

    // ── Commit list ──────────────────────────────────────────────────────

    private Element BuildCommitList(HistoryData data, List<GitCommit> commits)
    {
        if (commits.Count == 0)
            return Box.New().FlexGrow(1).JustifyContent(JustifyContent.Center).AlignItems(AlignItems.Center)
                .Children(Text.New(_historyQuery.Trim().Length > 0 ? "No matching commits" : "No commits yet")
                    .FontSize(14f).TextColor(Nocturne.On(0.45)));

        var selectedIndex = _selectedCommitHash == null
            ? -1
            : IndexOfHash(commits, _selectedCommitHash);

        return VirtualListElement<GitCommit>.New()
            .ItemHeight((int)RowHeight)
            .Items(commits)
            .RenderWith(c => HistoryRow(c,
                data.Graph.TryGetValue(c.Hash, out var g) ? g : default,
                data.Stats.TryGetValue(c.Hash, out var s) ? s : (0, 0),
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
        line1.AddChild(Text.New(commit.Subject).FontSize(13f).TextColor(Nocturne.Text)
            .FlexGrow(1).FlexShrink(1).OverflowMode(TextOverflow.Ellipsis).MaxLines(1));
        body.AddChild(line1);

        // Line 2: sha · author · time · +adds −dels.
        var line2 = Flex.Row(gap: 9).AlignItems(AlignItems.Center);
        line2.AddChild(Ui.Mono(commit.ShortHash, 11f, Nocturne.On(0.46)).Tooltip(commit.Hash));
        line2.AddChild(Text.New(commit.AuthorName).FontSize(11f).TextColor(Nocturne.On(0.46)));
        line2.AddChild(Text.New("·").FontSize(11f).TextColor(Nocturne.On(0.46)));
        line2.AddChild(Text.New(RelativeTime(commit.AuthorDate)).FontSize(11f).TextColor(Nocturne.On(0.46))
            .Tooltip(commit.AuthorDate.ToString("yyyy-MM-dd HH:mm:ss")));
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
        bar.AddChild(Hint("↑ ↓", "select commit"));
        bar.AddChild(Hint("Ctrl/Shift + click/↑↓", "select commits to compare"));
        bar.AddChild(Hint("Ctrl+Space", "toggle commit"));
        bar.AddChild(Hint("↵", comparing ? "compare selected" : "open commit details"));
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

    private static string RelativeTime(DateTimeOffset date)
    {
        var span = DateTimeOffset.Now - date;
        if (span.TotalMinutes < 1) return "just now";
        if (span.TotalMinutes < 60) return $"{(int)span.TotalMinutes}m ago";
        if (span.TotalHours < 24) return $"{(int)span.TotalHours}h ago";
        if (span.TotalDays < 30) return $"{(int)span.TotalDays}d ago";
        if (span.TotalDays < 365) return $"{(int)(span.TotalDays / 30)}mo ago";
        return $"{(int)(span.TotalDays / 365)}y ago";
    }
}
