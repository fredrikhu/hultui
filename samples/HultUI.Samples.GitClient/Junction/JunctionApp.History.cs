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
        var key = $"{_activeRepoPath}|{string.Join(",", includeList)}|{_historyGen}";
        _history.Load(key, () => LoadHistory(_activeRepoPath, includeList), onLoaded: PickInitialCommit);
        var data = _history.Value;
        var filtered = FilteredCommits(data);

        // Keep the selection valid within the filtered view (e.g. after a search
        // narrows the list past the current selection).
        if (filtered.Count > 0 && (_selectedCommitHash == null || filtered.All(c => c.Hash != _selectedCommitHash)))
            _selectedCommitHash = filtered[0].Hash;

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
        if (data.Commits.Count == 0) { _selectedCommitHash = null; return; }
        if (_selectedCommitHash == null || data.Commits.All(c => c.Hash != _selectedCommitHash))
            _selectedCommitHash = data.Commits[0].Hash;
    }

    private void MoveCommitSelection(int delta)
    {
        var commits = FilteredCommits(_history.Value);
        if (commits.Count == 0) return;
        var i = _selectedCommitHash == null ? 0 : IndexOfHash(commits, _selectedCommitHash);
        if (i < 0) i = 0;
        i = Math.Clamp(i + delta, 0, commits.Count - 1);
        _selectedCommitHash = commits[i].Hash;
        _pendingScrollToSelected = true;
        MarkDirty();
    }

    /// <summary>Scrolls the commit list so the keyboard-selected row is on screen
    /// (the list isn't focused, so it won't auto-scroll on its own).</summary>
    private void EnsureSelectedCommitVisible()
    {
        var list = _commitListRef.Current;
        if (list == null || _selectedCommitHash == null) return;

        var idx = IndexOfHash(FilteredCommits(_history.Value), _selectedCommitHash);
        if (idx < 0) return;

        var top = idx * RowHeight;
        var bottom = top + RowHeight;
        var viewH = list.Layout.AbsoluteContentBox.Height;
        var cur = list.ScrollOffsetY;

        if (top < cur) list.ScrollOffsetY = top;
        else if (bottom > cur + viewH) list.ScrollOffsetY = bottom - viewH;
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
            .Height(30).MaxWidth(300).FlexShrink(1)
            .Padding(new Thickness(0, 10, 0, 10))
            .Background(Nocturne.Bg)
            .BorderColor(Nocturne.Divider).BorderWidth(1f)
            .CornerRadius(Nocturne.RadiusMd);
        box.AddChild(Ui.Icon(Icons.MagnifyingGlass, 12f, Nocturne.On(0.45)).FlexShrink(0));

        // Borderless field (theme.TextInput); this wrapper box supplies the border.
        var input = TextInput.New(_historyQuery);
        input.PlaceholderText("Press / to search commits…")
            .OnTextChanged(OnHistoryQueryChanged)
            .Height(Dimension.Px(28))
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
        if (e.Key == KeyCode.Escape)
        {
            _historyQuery = "";
            MarkDirty();
            e.StopPropagation = true;
            return;
        }
        if (HandleHistoryNavKey(e))
            e.StopPropagation = true;
    }

    private void ClearInclude()
    {
        IncludeSet(_activeRepoPath).Clear();
        _historyGen++;
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
                c.Hash == _selectedCommitHash))
            .AccessibleItemLabel(c => $"{c.Subject}, {c.AuthorName}, {c.ShortHash}")
            .OnActivateItem(c => { _selectedCommitHash = c.Hash; SetView(JunctionView.Commit); })
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
        var row = Flex.Row(gap: 0)
            .AlignItems(AlignItems.Stretch)
            .Height(RowHeight)
            .Background(selected ? Nocturne.Accent900 : Color.Transparent)
            .BorderColor(Nocturne.On(0.05)).BorderWidth(new Thickness(0, 0, 1, 0))
            .Cursor(CursorType.Pointer);

        // Graph gutter.
        var columns = Math.Max(graph.LaneCount, 1);
        var gutterWidth = Math.Max(56f, columns * LaneWidth + 10f);
        row.AddChild(Canvas.New(ctx => DrawGraph(ctx, graph)).Width(gutterWidth).FlexShrink(0));

        // Body.
        var body = Flex.Column(gap: 3).FlexGrow(1).JustifyContent(JustifyContent.Center)
            .Padding(new Thickness(0, 8, 0, 0));

        // Line 1: ref chips + message.
        var line1 = Flex.Row(gap: 6).AlignItems(AlignItems.Center);
        foreach (var chip in RefChips(commit.Refs, remotes))
            line1.AddChild(chip);
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

        // Selected affordance.
        if (selected)
            row.AddChild(Flex.Row(gap: 4).AlignItems(AlignItems.Center).FlexShrink(0)
                .Padding(new Thickness(0, 12, 0, 8))
                .Children(
                    Text.New("Details").FontSize(11f).TextColor(Nocturne.Accent300),
                    Ui.Icon(Icons.ArrowRight, 12f, Nocturne.Accent300)));

        row.OnClick(_ =>
        {
            if (commit.Hash == _selectedCommitHash) SetView(JunctionView.Commit);
            else { _selectedCommitHash = commit.Hash; MarkDirty(); }
        });
        if (!selected)
            row.HoverBg(Nocturne.Neutral900, Color.Transparent);

        return row;
    }

    private static IEnumerable<Element> RefChips(string[] refs, HashSet<string> remotes)
    {
        var emitted = 0;
        foreach (var raw in refs)
        {
            if (emitted >= 3) yield break;
            var d = raw.Trim();
            if (d.Length == 0) continue;

            // "HEAD -> main": a HEAD marker plus the branch it points at.
            if (d.StartsWith("HEAD -> ", StringComparison.Ordinal))
            {
                yield return Ui.Chip(Icons.ArrowFatRight, "HEAD", Color.Transparent, Nocturne.Accent, outline: true);
                if (++emitted >= 3) yield break;
                foreach (var e in LocalBranchChip(d["HEAD -> ".Length..])) { yield return e; emitted++; }
                continue;
            }
            if (d == "HEAD")
            {
                yield return Ui.Chip(Icons.ArrowFatRight, "HEAD", Color.Transparent, Nocturne.Accent, outline: true);
                emitted++;
                continue;
            }
            if (d.StartsWith("tag: ", StringComparison.Ordinal))
            {
                yield return Ui.Chip(Icons.Tag, d[5..], Nocturne.Neutral800, Nocturne.Neutral100);
                emitted++;
                continue;
            }

            // Remote-tracking ref ("origin/main"): cloud chip, keep the prefix.
            var firstSeg = d.Split('/', 2)[0];
            if (remotes.Contains(firstSeg))
            {
                if (d.EndsWith("/HEAD", StringComparison.Ordinal)) continue;  // symbolic origin/HEAD — skip
                yield return Ui.Chip(Icons.Cloud, d, Nocturne.Neutral800, Nocturne.Neutral100);
                emitted++;
                continue;
            }

            foreach (var e in LocalBranchChip(d)) { yield return e; emitted++; }
        }
    }

    private static IEnumerable<Element> LocalBranchChip(string name)
    {
        var pb = BranchName.Parse(name);
        var (glyph, bg, fg) = pb.ChipLabel switch
        {
            "FEAT" => (Icons.GitBranch, Nocturne.Accent800, Nocturne.Accent100),
            "WIP" => (Icons.Flask, Nocturne.Accent2_800, Nocturne.Accent2_100),
            _ => (Icons.GitBranch, Nocturne.Neutral800, Nocturne.Neutral100),
        };
        yield return Ui.Chip(glyph, pb.Title, bg, fg);
    }

    // ── Footer ───────────────────────────────────────────────────────────

    private Element BuildHistoryFooter()
    {
        var bar = Flex.Row(gap: 16)
            .FlexShrink(0).AlignItems(AlignItems.Center)
            .Padding(new Thickness(8, 16, 8, 16))
            .BorderColor(Nocturne.Divider).BorderWidth(new Thickness(1, 0, 0, 0));

        bar.AddChild(Hint("↑ ↓", "select commit"));
        bar.AddChild(Hint("↵", "open commit details"));
        bar.AddChild(Box.New().FlexGrow(1));
        bar.AddChild(Ui.SecondaryButton("Open commit details", Icons.ArrowRight,
            () => SetView(JunctionView.Commit), 11.5f));
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
