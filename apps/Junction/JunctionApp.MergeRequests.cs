using HultUI;
using HultUI.Core.Builders;
using HultUI.Core.Components;
using HultUI.Core.Reactivity;
using HultUI.Core.Elements;
using HultUI.Core.Events;
using HultUI.Core.Layout;
using HultUI.Core.Primitives;
using HultUI.Core.Styling;
using Junction.Git;
using Junction.GitLab;
using Junction.Theme;

namespace Junction;

/// <summary>
/// The merge-request panel — the design's two pages in one 760-wide card: a list with
/// filter chips and a scope switch, and a detail page with the status, pipeline and
/// approvals cards, the description, the changed files and the discussions. Plus the
/// small pieces other views borrow: the <c>!iid</c> chip, the toolbar's head glyph, the
/// Commit header's pipeline line and the Overview card's hosted footer.
/// </summary>
/// <remarks>
/// Read side and <i>Checkout locally</i> only: approve, merge, rebase and draft toggling
/// come with the phase that owns the action row, and the review entry with the diff
/// gutter. Everything shown comes from <see cref="MergeRequestsFor"/> and friends, so a
/// failed refresh keeps the rows and adds a line saying how old they are.
/// </remarks>
public partial class JunctionApp
{
    internal enum MrFilter { AssignedToMe, Reviewing, Mine, AllOpen }
    private enum MrScope { Repo, Workspace }

    private sealed record MrRow(string Path, GlMergeRequest Mr);

    private bool _mrPanelOpen;
    private MrFilter _mrFilter = MrFilter.AssignedToMe;
    private MrScope _mrScope = MrScope.Repo;
    private string _mrQuery = "";
    private int _mrSel;
    private (string Path, long Iid)? _mrOpen;
    private string? _mrBusy;
    private readonly Ref<TextInputElement> _mrSearchRef = new();
    private bool _pendingMrSearchFocus;
    private List<MrRow> _mrRows = [];

    private readonly KeyedAsyncLoader<(string Path, long Iid), Hosted<IReadOnlyList<GlDiff>>> _mrDiffLoaders =
        new(Hosted<IReadOnlyList<GlDiff>>.Empty([]), capacity: 16);

    internal bool MergeRequestsOpen => _mrPanelOpen;

    // ── Open / close ─────────────────────────────────────────────────────────────────

    internal void OpenMergeRequests(MrFilter? filter = null)
    {
        if (GitLabRemoteFor(_activeRepoPath) is not { } remote) { Flash("This repository is not on GitLab"); return; }
        if (!_gitlab.HasAccount(remote.Host)) { OpenOptions(OptionsSection.GitLabAccounts); return; }
        if (filter is { } f) { _mrFilter = f; _mrSel = 0; }
        _mrPanelOpen = true;
        _mrOpen = null;
        ClosePalette();
        _popover = null;
        RefreshHostedIfStale(_activeRepoPath, RefreshOnSwitchAfter);
        // Rerender, not MarkDirty: opening activates this repo's hosted loaders for the first
        // time, and an untracked rebuild would leave the Effect unsubscribed from them.
        Rerender();
    }

    /// <summary>Straight to one merge request's detail — from a chip, the toolbar, a todo.</summary>
    internal void OpenMergeRequest(string path, long iid)
    {
        if (!PathEquals(path, _activeRepoPath)) SelectRepo(path);
        if (GitLabRemoteFor(_activeRepoPath) is not { } remote || !_gitlab.HasAccount(remote.Host)) return;
        _mrPanelOpen = true;
        _mrOpen = (_activeRepoPath, iid);
        ClosePalette();
        _popover = null;
        Rerender();
    }

    private void CloseMergeRequests()
    {
        _mrPanelOpen = false;
        _mrOpen = null;
        MarkDirty();
    }

    // ── Rows ─────────────────────────────────────────────────────────────────────────

    private IEnumerable<string> ScopeRepos() => _mrScope == MrScope.Workspace && ActiveWorkspace is { } ws
        ? ws.RepoPaths.Where(GitLabActive)
        : [_activeRepoPath];

    private List<MrRow> PoolRows()
    {
        var rows = new List<MrRow>();
        foreach (var path in ScopeRepos())
            foreach (var mr in MergeRequestsFor(path).Value)
                if (mr.IsOpen) rows.Add(new MrRow(path, mr));
        rows.Sort((a, b) => Nullable.Compare(b.Mr.UpdatedAt, a.Mr.UpdatedAt));
        return rows;
    }

    private long? MyIdOn(string path) =>
        GitLabRemoteFor(path) is { } r && _gitlab.For(r.Host) is { } a ? a.UserId : null;

    private bool Passes(MrRow row, MrFilter filter)
    {
        var me = MyIdOn(row.Path);
        return filter switch
        {
            MrFilter.AssignedToMe => me is { } id && row.Mr.Assignees.Any(u => u.Id == id),
            MrFilter.Reviewing => me is { } id && row.Mr.Reviewers.Any(u => u.Id == id),
            MrFilter.Mine => me is { } id && row.Mr.Author?.Id == id,
            _ => true,
        };
    }

    private bool MatchesQuery(MrRow row)
    {
        var q = _mrQuery.Trim();
        if (q.Length == 0) return true;
        var hay = $"{row.Mr.Title} !{row.Mr.Iid} {row.Mr.SourceBranch} {row.Mr.Author?.Name} {row.Mr.Author?.Username}";
        return hay.Contains(q, StringComparison.OrdinalIgnoreCase);
    }

    private static string FilterLabel(MrFilter f) => f switch
    {
        MrFilter.AssignedToMe => "Assigned to me",
        MrFilter.Reviewing => "Reviewing",
        MrFilter.Mine => "Mine",
        _ => "All open",
    };

    // ── Status words ─────────────────────────────────────────────────────────────────

    private static (string Word, Color Color) StatusWord(GlMergeRequest mr)
    {
        if (mr.Draft && mr.DetailedMergeStatus is null or "draft_status") return ("Draft", Nocturne.On(0.62));
        return mr.DetailedMergeStatus switch
        {
            "mergeable" => ("Mergeable", Nocturne.DiffAddSign),
            "conflict" => ("Conflicts", Nocturne.DiffDelSign),
            "need_rebase" => ("Needs rebase", Nocturne.On(0.62)),
            "ci_still_running" => ("Pipeline running", Nocturne.Accent300),
            "ci_must_pass" => ("Pipeline must pass", Nocturne.On(0.62)),
            "not_approved" => ("Needs approval", Nocturne.On(0.62)),
            "discussions_not_resolved" => ("Unresolved threads", Nocturne.On(0.62)),
            "draft_status" => ("Draft", Nocturne.On(0.62)),
            "checking" or "unchecked" or "preparing" or "approvals_syncing" => ("Checking…", Nocturne.On(0.62)),
            "requested_changes" => ("Changes requested", Nocturne.On(0.62)),
            "not_open" => ("Closed", Nocturne.On(0.62)),
            null or "" => ("", Nocturne.On(0.62)),
            var other => (other.Replace('_', ' '), Nocturne.On(0.62)),
        };
    }

    private static string StatusReason(GlMergeRequest mr, GlApprovals? approvals) => mr.DetailedMergeStatus switch
    {
        "mergeable" => "No conflicts, approvals met, pipeline passed.",
        "conflict" => $"{mr.TargetBranch} has moved on; the branch conflicts with it.",
        "need_rebase" => $"Fast-forward only — rebase onto {mr.TargetBranch} first.",
        "ci_still_running" => mr.HeadPipeline is { } p ? $"Waiting for pipeline #{p.Id} to finish." : "Waiting for the pipeline to finish.",
        "ci_must_pass" => "The pipeline must pass before merging.",
        "not_approved" => approvals?.ApprovalsLeft is { } left and > 0
            ? $"{left} more approval{(left == 1 ? "" : "s")} required."
            : "Approval required.",
        "discussions_not_resolved" => "Threads are still open.",
        "draft_status" => "Mark as ready to allow merging.",
        "checking" or "unchecked" or "preparing" => "GitLab is still checking whether this can merge.",
        _ => "",
    };

    // ── Panel ────────────────────────────────────────────────────────────────────────

    private Element BuildMergeRequestsPanel()
    {
        var backdrop = Box.New()
            .Width(Dimension.Percent(100)).Height(Dimension.Percent(100))
            .Background(new Color(0, 0, 0, 140))
            .JustifyContent(JustifyContent.FlexStart).AlignItems(AlignItems.Center)
            .Padding(new Thickness(48, 24, 24, 24))
            .ZIndex(42);
        backdrop.Position.Type = PositionType.Absolute;
        backdrop.Position.Top = Dimension.Px(0);
        backdrop.Position.Left = Dimension.Px(0);
        backdrop.On<ClickEvent>(e => { e.StopPropagation = true; CloseMergeRequests(); });

        var card = Flex.Column(gap: 0)
            .Width(760).MaxWidth(Dimension.Percent(94)).MaxHeight(740)
            .Background(Nocturne.Surface)
            .BorderColor(Nocturne.Neutral700).BorderWidth(1f)
            .CornerRadius(Nocturne.RadiusLg)
            .Overflow(Overflow.Hidden);
        card.On<ClickEvent>(e => e.StopPropagation = true);
        card.AutomationId = "merge-requests";

        if (_mrOpen is { } open) BuildMrDetailPage(card, open.Path, open.Iid);
        else BuildMrListPage(card);
        // Keyed by page so the list's fields never patch onto the detail's.
        card.Key = _mrOpen is null ? "mr-list" : "mr-detail";

        backdrop.AddChild(card);
        return backdrop;
    }

    private Element MrCloseButton()
    {
        var b = Ui.IconButton(Icons.X, CloseMergeRequests, "Close (Esc)", 28f).FlexShrink(0);
        b.AccessibleName = "Close merge requests";
        return b;
    }

    // ── List page ────────────────────────────────────────────────────────────────────

    private void BuildMrListPage(BoxElement card)
    {
        var remote = GitLabRemoteFor(_activeRepoPath)!;
        var hosted = MergeRequestsFor(_activeRepoPath);
        var pool = PoolRows();
        _mrRows = pool.Where(r => Passes(r, _mrFilter) && MatchesQuery(r)).ToList();
        if (_mrSel >= _mrRows.Count) _mrSel = Math.Max(0, _mrRows.Count - 1);

        // Header.
        var header = Flex.Row(gap: 12).AlignItems(AlignItems.Center).FlexShrink(0)
            .Padding(new Thickness(14, 18, 12, 18))
            .BorderColor(Nocturne.Divider).BorderWidth(new Thickness(0, 0, 1, 0));
        header.AddChild(Box.New().Width(34).Height(34).FlexShrink(0).CornerRadius(Nocturne.RadiusMd)
            .Background(Nocturne.Accent800).JustifyContent(JustifyContent.Center).AlignItems(AlignItems.Center)
            .Children(Ui.Icon(Icons.GitPullRequest, 17f, Nocturne.Accent200)));
        var titleCol = Flex.Column(gap: 1).FlexGrow(1).FlexShrink(1);
        titleCol.AddChild(Text.New("Merge requests").FontSize(16f).FontWeight(FontWeight.Medium));
        string subtitle = _mrScope == MrScope.Workspace && ActiveWorkspace is { } ws
            ? $"{ws.Name} · {ScopeRepos().Count()} GitLab repositories"
            : $"{remote.ProjectPath} on {remote.Host}";
        titleCol.AddChild(Ui.Mono(subtitle, 11.5f, Nocturne.On(0.52)).OverflowMode(TextOverflow.Ellipsis).MaxLines(1));
        header.AddChild(titleCol);
        if (hosted.Loaded)
            header.AddChild(Text.New(AsOfText(hosted.AsOf)).FontSize(11f)
                .TextColor(hosted.Stale ? Nocturne.MarkAmber : Nocturne.On(0.42)).FlexShrink(0));
        header.AddChild(MrCloseButton());
        card.AddChild(header);

        // Filter row.
        var filters = Flex.Row(gap: 6).AlignItems(AlignItems.Center).FlexShrink(0)
            .Padding(new Thickness(10, 18, 10, 18))
            .BorderColor(Nocturne.Divider).BorderWidth(new Thickness(0, 0, 1, 0));
        foreach (var f in new[] { MrFilter.AssignedToMe, MrFilter.Reviewing, MrFilter.Mine, MrFilter.AllOpen })
        {
            int count = pool.Count(r => Passes(r, f));
            filters.AddChild(MrFilterChip(FilterLabel(f), count, f == _mrFilter, () => { _mrFilter = f; _mrSel = 0; MarkDirty(); }));
        }
        filters.AddChild(Box.New().FlexGrow(1));
        filters.AddChild(MrScopeSegment());
        filters.AddChild(MrSearchBox());
        card.AddChild(filters);

        // Provenance line when the last refresh failed.
        if (hosted.Failure is { } failure)
            card.AddChild(MrFailureLine(remote.Host, hosted, failure));

        // Rows.
        var scroll = ScrollView.New().FlexGrow(1).FlexShrink(1).MinHeight(220)
            .Padding(new Thickness(6, 8, 6, 8));
        if (MergeRequestsFirstLoading(_activeRepoPath))
        {
            for (int i = 0; i < 4; i++) scroll.AddChild(MrSkeletonRow(i));
        }
        else if (_mrRows.Count == 0)
        {
            scroll.AddChild(Box.New().Padding(new Thickness(40, 24, 40, 24)).AlignItems(AlignItems.Center)
                .Children(Text.New(MrEmptyCopy()).FontSize(13f).TextColor(Nocturne.On(0.48))));
        }
        else
        {
            for (int i = 0; i < _mrRows.Count; i++)
                scroll.AddChild(MrListRow(_mrRows[i], i, hosted.Failure != null));
        }
        card.AddChild(scroll);

        // Legend.
        var legend = Flex.Row(gap: 14).AlignItems(AlignItems.Center).FlexShrink(0)
            .Padding(new Thickness(8, 18, 8, 18))
            .BorderColor(Nocturne.Divider).BorderWidth(new Thickness(1, 0, 0, 0));
        foreach (var (key, what) in Shortcuts.Legend("Merge requests", "mr.back", "mr.review", "mr.approve"))
            legend.AddChild(Flex.Row(gap: 4).AlignItems(AlignItems.Center).Children(
                Text.New(key).FontSize(10.5f).FontWeight(FontWeight.SemiBold).TextColor(Nocturne.On(0.68)),
                Text.New(what).FontSize(10.5f).TextColor(Nocturne.On(0.45))));
        legend.AddChild(Box.New().FlexGrow(1));
        legend.AddChild(Text.New($"{_mrRows.Count} open").FontSize(10.5f).TextColor(Nocturne.On(0.45)));
        card.AddChild(legend);
    }

    private string MrEmptyCopy()
    {
        var where = _mrScope == MrScope.Workspace && ActiveWorkspace is { } ws
            ? $"across {ws.Name}"
            : $"for {System.IO.Path.GetFileName(_activeRepoPath.TrimEnd('/', '\\'))}";
        if (_mrQuery.Trim().Length > 0) return $"Nothing matches \"{_mrQuery.Trim()}\" {where}.";
        return _mrFilter switch
        {
            MrFilter.AssignedToMe => $"No merge requests assigned to you {where}.",
            MrFilter.Reviewing => $"No merge requests waiting on your review {where}.",
            MrFilter.Mine => $"No open merge requests of yours {where}.",
            _ => $"No open merge requests {where}. Push a branch and the toast offers to create one.",
        };
    }

    private static Element MrFilterChip(string label, int count, bool on, Action onClick)
    {
        var chip = Flex.Row(gap: 5).AlignItems(AlignItems.Center)
            .Padding(new Thickness(3, 9, 3, 9)).CornerRadius(999f)
            .Cursor(CursorType.Pointer)
            .Background(on ? Nocturne.Alpha(Nocturne.Accent, 0.14) : Color.Transparent)
            .Outline(1f, on ? Nocturne.Accent : Nocturne.Divider);
        chip.AddChild(Text.New(label).FontSize(11f).TextColor(on ? Nocturne.Accent200 : Nocturne.On(0.62)));
        chip.AddChild(Text.New(count.ToString()).FontSize(10.5f).TextColor(on ? Nocturne.Alpha(Nocturne.Accent200, 0.6) : Nocturne.On(0.4)));
        chip.OnClick(_ => onClick());
        chip.Role = HultUI.Core.Accessibility.AccessibilityRole.Button;
        chip.AccessibleName = label;
        chip.Key = "mr-filter-" + label + (on ? "-on" : "-off");
        return chip;
    }

    private Element MrScopeSegment()
    {
        var seg = Flex.Row(gap: 2).AlignItems(AlignItems.Center).Padding(new Thickness(2))
            .Background(Nocturne.Bg).BorderColor(Nocturne.Divider).BorderWidth(1f).CornerRadius(6f);
        seg.AddChild(MrScopeButton("This repo", Icons.GitRepository, MrScope.Repo));
        seg.AddChild(MrScopeButton("Workspace", Icons.Stack, MrScope.Workspace));
        return seg;
    }

    private Element MrScopeButton(string label, string glyph, MrScope scope)
    {
        bool on = _mrScope == scope;
        var b = Flex.Row(gap: 5).AlignItems(AlignItems.Center)
            .Padding(new Thickness(3, 9, 3, 9)).CornerRadius(5f).Cursor(CursorType.Pointer)
            .Background(on ? Nocturne.Surface : Color.Transparent)
            .Outline(on ? 1f : 0f, on ? Nocturne.Accent : Color.Transparent);
        b.AddChild(Ui.Icon(glyph, 12f, on ? Nocturne.Accent : Nocturne.On(0.58)));
        b.AddChild(Text.New(label).FontSize(11f).FontWeight(FontWeight.Medium).TextColor(on ? Nocturne.Accent : Nocturne.On(0.58)));
        b.OnClick(_ => { _mrScope = scope; _mrSel = 0; Rerender(); });
        b.Role = HultUI.Core.Accessibility.AccessibilityRole.Button;
        b.AccessibleName = label;
        b.Key = "mr-scope-" + label + (on ? "-on" : "-off");
        return b;
    }

    private Element MrSearchBox()
    {
        var box = Flex.Row(gap: 6).AlignItems(AlignItems.Center)
            .Width(190).Height(30).Padding(new Thickness(0, 10, 0, 10))
            .Background(Nocturne.Bg).BorderColor(Nocturne.Divider).BorderWidth(1f).CornerRadius(Nocturne.RadiusMd);
        box.AddChild(Ui.Icon(Icons.MagnifyingGlass, 12f, Nocturne.On(0.45)));
        var input = TextInput.New(_mrQuery)
            .PlaceholderText("Press / to search…")
            .OnTextChanged(v => { _mrQuery = v; _mrSel = 0; MarkDirty(); })
            // The field fills the box's 28px of content: at 24 the theme's own padding left
            // the glyphs' descenders under the bottom edge.
            .Height(Dimension.Px(28)).FlexGrow(1).FlexShrink(1).FlexBasis(Dimension.Px(0))
            .FontSize(12f).TextColor(Nocturne.Text)
            .WithRef(_mrSearchRef);
        input.AutomationId = "mr-search";
        box.AddChild(input);
        return box;
    }

    private Element MrFailureLine(string host, Hosted<IReadOnlyList<GlMergeRequest>> hosted, GitLabFailure failure)
    {
        var line = Flex.Row(gap: 8).AlignItems(AlignItems.Center).FlexShrink(0)
            .Padding(new Thickness(8, 12, 8, 12)).CornerRadius(Nocturne.RadiusSm)
            .Background(Nocturne.Bg).BorderColor(Nocturne.Divider).BorderWidth(1f);
        line.AddChild(Ui.Icon(failure.Kind == GitLabFailureKind.RateLimited ? Icons.Clock : Icons.Warning, 14f, Nocturne.MarkAmber));
        var text = Flex.Column(gap: 1).FlexGrow(1).FlexShrink(1);
        var head = failure.Kind switch
        {
            GitLabFailureKind.RateLimited => failure.UserMessage(host) + ".",
            GitLabFailureKind.Unauthorized => $"Your {host} session was refused — sign in again.",
            _ => hosted.Loaded ? $"Showing what was loaded {AsOfText(hosted.AsOf)[6..]}." : $"{host} could not be reached.",
        };
        text.AddChild(Text.New(head).FontSize(12f));
        text.AddChild(Text.New(failure.Kind == GitLabFailureKind.RateLimited
                ? "Cached rows stay. Retry-After is honoured."
                : $"{failure.Message}. Nothing else changes.")
            .FontSize(11f).TextColor(Nocturne.On(0.55)));
        line.AddChild(text);
        if (failure.Kind != GitLabFailureKind.RateLimited)
            line.AddChild(Ui.GhostButton("Retry", null, () => RefreshHosted(_activeRepoPath)));
        line.AutomationId = "mr-failure";
        return Flex.Column(gap: 0).Padding(new Thickness(8, 18, 0, 18)).Children(line);
    }

    private static Element MrSkeletonRow(int i)
    {
        float[] widths = [0.46f, 0.58f, 0.38f, 0.52f];
        var row = Flex.Row(gap: 12).AlignItems(AlignItems.Center)
            .Padding(new Thickness(8, 10, 8, 10)).CornerRadius(Nocturne.RadiusMd).Background(Nocturne.Bg);
        row.AddChild(Box.New().Width(34).Height(10).CornerRadius(5f).Background(Nocturne.Neutral800));
        row.AddChild(Box.New().Width(Dimension.Percent(widths[i % widths.Length] * 100f)).Height(10).CornerRadius(5f).Background(Nocturne.Neutral800));
        row.AddChild(Box.New().FlexGrow(1));
        row.AddChild(Box.New().Width(14).Height(14).CornerRadius(7f).Background(Nocturne.Neutral800));
        row.AddChild(Box.New().Width(70).Height(10).CornerRadius(5f).Background(Nocturne.Neutral800));
        row.AutomationId = "mr-skeleton";
        return row;
    }

    private Element MrListRow(MrRow r, int index, bool stale)
    {
        var mr = r.Mr;
        bool selected = index == _mrSel;
        var dim = stale ? 0.75 : 1.0;

        var row = Flex.Row(gap: 12).AlignItems(AlignItems.Center)
            .Padding(new Thickness(9, 12, 9, 12)).CornerRadius(Nocturne.RadiusMd)
            .Cursor(CursorType.Pointer)
            .Background(selected ? Nocturne.Accent900 : Color.Transparent);
        row.Key = $"{r.Path}|{mr.Iid}" + (selected ? "#s" : "");
        row.Role = HultUI.Core.Accessibility.AccessibilityRole.ListItem;
        row.AccessibleName = $"!{mr.Iid} {mr.Title}";

        row.AddChild(Ui.Mono($"!{mr.Iid}", 11.5f, Nocturne.Alpha(Nocturne.Accent300, dim)).Width(40).FlexShrink(0));

        var mid = Flex.Column(gap: 3).FlexGrow(1).FlexShrink(1).FlexBasis(Dimension.Px(0));
        var line1 = Flex.Row(gap: 6).AlignItems(AlignItems.Center);
        if (mr.Draft) line1.AddChild(Ui.TagOutline("Draft", Nocturne.On(0.6), 9f));
        line1.AddChild(Text.New(mr.Title).FontSize(13f).TextColor(Nocturne.Alpha(Nocturne.Text, dim))
            .FlexShrink(1).OverflowMode(TextOverflow.Ellipsis).MaxLines(1));
        mid.AddChild(line1);
        var line2 = Flex.Row(gap: 7).AlignItems(AlignItems.Center);
        if (_mrScope == MrScope.Workspace)
        {
            line2.AddChild(Text.New(System.IO.Path.GetFileName(r.Path.TrimEnd('/', '\\'))).FontSize(10.5f).TextColor(Nocturne.Accent300));
            line2.AddChild(Text.New("·").FontSize(10.5f).TextColor(Nocturne.On(0.46)));
        }
        AddBranchPair(line2, mr, 10.5f);
        line2.AddChild(Text.New("·").FontSize(10.5f).TextColor(Nocturne.On(0.46)));
        line2.AddChild(Text.New(mr.Author?.Name ?? "").FontSize(10.5f).TextColor(Nocturne.On(0.46)));
        line2.AddChild(Text.New("·").FontSize(10.5f).TextColor(Nocturne.On(0.46)));
        line2.AddChild(Text.New(RelativeTime(mr.UpdatedAt)).FontSize(10.5f).TextColor(Nocturne.On(0.46)));
        mid.AddChild(line2);
        row.AddChild(mid);

        var right = Flex.Row(gap: 12).AlignItems(AlignItems.Center).FlexShrink(0);
        if (MrPipeline(r.Path, mr) is { } pipe && Ui.PipelineGlyph(pipe.Status, 14f) is { } glyph)
            right.AddChild(glyph.Tooltip($"Pipeline #{pipe.Id} · {Ui.PipelineWord(pipe.Status)}"));
        if (ApprovalsFor(r.Path, mr.Iid).Value is { } approvals && approvals.ApprovalsRequired is { } need && need > 0)
        {
            bool met = approvals.ApprovedCount >= need;
            var color = met ? Nocturne.DiffAddSign : Nocturne.On(0.55);
            right.AddChild(Flex.Row(gap: 4).AlignItems(AlignItems.Center).Children(
                Ui.Icon(Icons.SealCheck, 13f, color),
                Text.New($"{approvals.ApprovedCount}/{need}").FontSize(11f).TextColor(color)));
        }
        var (word, wordColor) = StatusWord(mr);
        right.AddChild(Box.New().Width(112).AlignItems(AlignItems.FlexEnd)
            .Children(Text.New(word).FontSize(11f).TextColor(wordColor).MaxLines(1).OverflowMode(TextOverflow.Ellipsis)));
        row.AddChild(right);

        row.OnClick(_ =>
        {
            if (_mrSel == index) { _mrOpen = (r.Path, mr.Iid); Rerender(); }
            else { _mrSel = index; MarkDirty(); }
        });
        row.On<PointerDownEvent>(e => { if (e.Button == 2 && _mrSel != index) { _mrSel = index; MarkDirty(); } });
        row.ContextMenu(() => MrMenuItems(r));
        if (!selected) row.HoverBg(Nocturne.Neutral900, Color.Transparent);
        return row;
    }

    private IReadOnlyList<ContextMenuItem> MrMenuItems(MrRow r) =>
    [
        Item(Icons.BendDownRight, "Checkout locally", () => CheckoutMergeRequest(r.Path, r.Mr)),
        Item(Icons.Eyes, "Review", () => StartReview(r.Path, r.Mr.Iid)),
        Item(Icons.SealCheck, ApprovalsFor(r.Path, r.Mr.Iid).Value?.UserHasApproved == true ? "Revoke approval" : "Approve",
            () => ToggleApproval(r.Path, r.Mr, ApprovalsFor(r.Path, r.Mr.Iid).Value)),
        ContextMenuItem.Separator(),
        Item(Icons.ArrowSquareOut, "Open in browser", () => OpenUrl(r.Mr.WebUrl)),
        Item(Icons.Copy, $"Copy !{r.Mr.Iid} link", () => CopyToClipboard(r.Mr.WebUrl ?? "")),
    ];

    /// <summary>The merge request's pipeline: the one GitLab attached to it, or — for a
    /// branch pipeline GitLab did not link — the latest one on its head commit.</summary>
    private GlPipeline? MrPipeline(string path, GlMergeRequest mr) =>
        mr.HeadPipeline ?? PipelineForSha(path, mr.Sha);

    /// <summary>source prefix chip + mono leaf → mono target.</summary>
    private static void AddBranchPair(BoxElement line, GlMergeRequest mr, float fontSize)
    {
        var pb = BranchName.Parse(mr.SourceBranch);
        var (chipBg, chipFg) = pb.ChipLabel switch
        {
            "FEAT" => (Nocturne.Accent800, Nocturne.Accent100),
            "WIP" => (Nocturne.Accent2_800, Nocturne.Accent2_100),
            _ => (Nocturne.Neutral800, Nocturne.Neutral100),
        };
        line.AddChild(Ui.Tag(pb.ChipLabel, chipBg, chipFg, 9f));
        line.AddChild(Ui.Mono(pb.Title, fontSize, Nocturne.On(0.7)).FlexShrink(1).OverflowMode(TextOverflow.Ellipsis).MaxLines(1));
        line.AddChild(Ui.Icon(Icons.ArrowRight, fontSize, Nocturne.On(0.4)));
        line.AddChild(Ui.Mono(mr.TargetBranch, fontSize, Nocturne.On(0.7)).FlexShrink(1).OverflowMode(TextOverflow.Ellipsis).MaxLines(1));
    }

    // ── Detail page ──────────────────────────────────────────────────────────────────

    private void BuildMrDetailPage(BoxElement card, string path, long iid)
    {
        var mr = MergeRequestsFor(path).Value.FirstOrDefault(m => m.Iid == iid);
        var header = Flex.Column(gap: 8).FlexShrink(0)
            .Padding(new Thickness(12, 18, 10, 18))
            .BorderColor(Nocturne.Divider).BorderWidth(new Thickness(0, 0, 1, 0));
        var row1 = Flex.Row(gap: 10).AlignItems(AlignItems.Center);
        var back = Ui.GhostButton("Merge requests", Icons.ArrowLeft, BackToMrList);
        back.AccessibleName = "Back to merge requests";
        row1.AddChild(back);
        row1.AddChild(Box.New().Padding(new Thickness(2, 8, 2, 8)).CornerRadius(999f).Background(Nocturne.Accent800)
            .Children(Ui.Mono($"!{iid}", 12f, Nocturne.Accent100)));
        if (mr != null)
        {
            row1.AddChild(Ui.TagOutline(mr.Draft ? "Draft" : mr.MergeWhenPipelineSucceeds ? "Auto-merge set" : "Open",
                Nocturne.On(0.6), 9.5f));
            row1.AddChild(Text.New(mr.Title).FontSize(14.5f).FontWeight(FontWeight.Medium)
                .FlexGrow(1).FlexShrink(1).OverflowMode(TextOverflow.Ellipsis).MaxLines(1));
            row1.AddChild(Ui.GhostButton("Open in browser", Icons.ArrowSquareOut, () => OpenUrl(mr.WebUrl)));
        }
        else
        {
            // The list is loaded and the request is not in it: it was merged or closed since —
            // by this window's own merge, or by someone else. Say so rather than wait forever.
            var hostedList = MergeRequestsFor(path);
            bool gone = hostedList.Loaded && !_mrLoaders.For(path).IsLoading;
            var word = Text.New(gone ? $"!{iid} is no longer open — it was merged or closed." : "Loading…")
                .FontSize(14.5f).TextColor(Nocturne.On(0.5)).FlexGrow(1);
            if (gone) word.AutomationId = "mr-gone";
            row1.AddChild(word);
        }
        row1.AddChild(MrCloseButton());
        header.AddChild(row1);

        if (mr != null)
        {
            var meta = Flex.Row(gap: 10).AlignItems(AlignItems.Center);
            AddBranchPair(meta, mr, 11.5f);
            meta.AddChild(Text.New("·").FontSize(11.5f).TextColor(Nocturne.On(0.35)));
            meta.AddChild(Flex.Row(gap: 5).AlignItems(AlignItems.Center).Children(
                Box.New().Width(18).Height(18).CornerRadius(9f).Background(Nocturne.Accent800)
                    .JustifyContent(JustifyContent.Center).AlignItems(AlignItems.Center)
                    .Children(Text.New(Initials(mr.Author?.Name ?? "?")).FontSize(9f).TextColor(Nocturne.Accent100)),
                Text.New(mr.Author?.Name ?? "").FontSize(11.5f).TextColor(Nocturne.On(0.55))));
            meta.AddChild(Text.New("·").FontSize(11.5f).TextColor(Nocturne.On(0.35)));
            meta.AddChild(Text.New($"updated {RelativeTime(mr.UpdatedAt)}").FontSize(11.5f).TextColor(Nocturne.On(0.55)));
            meta.AddChild(Box.New().FlexGrow(1));
            var hosted = MergeRequestsFor(path);
            if (hosted.Loaded)
                meta.AddChild(Text.New(AsOfText(hosted.AsOf)).FontSize(10.5f)
                    .TextColor(hosted.Stale ? Nocturne.MarkAmber : Nocturne.On(0.4)));
            header.AddChild(meta);
        }
        card.AddChild(header);

        var body = ScrollView.New().FlexGrow(1).FlexShrink(1);
        var col = Flex.Column(gap: 16).Padding(new Thickness(14, 18, 14, 18));
        if (mr != null)
        {
            var approvals = ApprovalsFor(path, iid).Value;
            var discussions = DiscussionsFor(path, iid).Value;
            col.AddChild(MrCards(path, mr, approvals));
            col.AddChild(MrDescription(mr.Description));
            col.AddChild(MrChanges(path, iid, mr, discussions));
            col.AddChild(MrDiscussions(path, iid, discussions));
        }
        body.AddChild(col);
        card.AddChild(body);

        // Actions: checkout and review, then the request's own verbs — see
        // JunctionApp.MergeRequestActions. A refusal stays under the row.
        if (mr != null)
        {
            card.AddChild(MrActionRow(path, iid, mr, ApprovalsFor(path, iid).Value));
            if (MrActionFailureLine() is { } failed) card.AddChild(failed);
        }
        else
        {
            card.AddChild(Flex.Row(gap: 8).FlexShrink(0).Padding(new Thickness(12, 18, 12, 18))
                .BorderColor(Nocturne.Divider).BorderWidth(new Thickness(1, 0, 0, 0)));
        }

        var legend = Flex.Row(gap: 14).AlignItems(AlignItems.Center).FlexShrink(0)
            .Padding(new Thickness(7, 18, 7, 18));
        foreach (var (key, what) in Shortcuts.Legend("Merge requests", "mr.search", "mr.move"))
            legend.AddChild(Flex.Row(gap: 4).AlignItems(AlignItems.Center).Children(
                Text.New(key).FontSize(10.5f).FontWeight(FontWeight.SemiBold).TextColor(Nocturne.On(0.68)),
                Text.New(what).FontSize(10.5f).TextColor(Nocturne.On(0.45))));
        card.AddChild(legend);
    }

    private void BackToMrList()
    {
        _mrOpen = null;
        _mrActionFailure = null;
        MarkDirty();
    }

    private static Element MrCard(string kicker, Element content)
    {
        var box = Flex.Column(gap: 5).Padding(new Thickness(10, 12, 10, 12)).CornerRadius(Nocturne.RadiusMd)
            .Background(Nocturne.Bg).BorderColor(Nocturne.Divider).BorderWidth(1f)
            .FlexShrink(1).FlexBasis(Dimension.Px(0));
        box.AddChild(Text.New(kicker.ToUpperInvariant()).FontSize(10f).TextColor(Nocturne.On(0.42)));
        box.AddChild(content);
        return box;
    }

    private Element MrCards(string path, GlMergeRequest mr, GlApprovals? approvals)
    {
        var grid = Flex.Row(gap: 10).AlignItems(AlignItems.Stretch);

        var (word, wordColor) = StatusWord(mr);
        var status = Flex.Column(gap: 3);
        status.AddChild(Text.New(word.Length > 0 ? word : "—").FontSize(14f).FontWeight(FontWeight.Medium).TextColor(wordColor));
        var reason = StatusReason(mr, approvals);
        if (reason.Length > 0) status.AddChild(Text.New(reason).FontSize(11f).TextColor(Nocturne.On(0.55)));
        grid.AddChild(MrCard("Status", status).FlexGrow(1.2f));

        var pipeline = Flex.Column(gap: 3);
        if (MrPipeline(path, mr) is { } p && Ui.PipelineMark(p.Status) is { } mark)
        {
            var line = Flex.Row(gap: 6).AlignItems(AlignItems.Center).Cursor(CursorType.Pointer)
                .Tooltip("Open pipeline in the browser");
            line.AddChild(Ui.Icon(mark.Glyph, 15f, mark.Color));
            line.AddChild(Text.New($"#{p.Id} · {Ui.PipelineWord(p.Status)}").FontSize(13f).FontWeight(FontWeight.Medium).TextColor(mark.Color));
            line.OnClick(_ => OpenUrl(p.WebUrl));
            pipeline.AddChild(line);
            pipeline.AddChild(Text.New(RelativeTime(p.UpdatedAt ?? p.CreatedAt)).FontSize(10.5f).TextColor(Nocturne.On(0.45)));
        }
        else
        {
            pipeline.AddChild(Text.New("No pipeline").FontSize(13f).TextColor(Nocturne.On(0.45)));
        }
        grid.AddChild(MrCard("Pipeline", pipeline).FlexGrow(1f));

        var appr = Flex.Column(gap: 4);
        if (approvals is { ApprovalsRequired: { } need } && need > 0)
        {
            bool met = approvals.ApprovedCount >= need;
            appr.AddChild(Text.New($"{approvals.ApprovedCount} of {need}{(met ? " · met" : "")}").FontSize(14f)
                .FontWeight(FontWeight.Medium).TextColor(met ? Nocturne.DiffAddSign : Nocturne.Text));
        }
        else
        {
            appr.AddChild(Text.New(approvals is null ? "…" : approvals.ApprovedCount > 0 ? $"{approvals.ApprovedCount} approved" : "Optional")
                .FontSize(14f).FontWeight(FontWeight.Medium));
        }
        var who = Flex.Row(gap: 4).AlignItems(AlignItems.Center).MinHeight(18);
        if (approvals is { ApprovedBy.Count: > 0 })
            foreach (var by in approvals.ApprovedBy)
                who.AddChild(Box.New().Width(18).Height(18).CornerRadius(9f).Background(Nocturne.Accent800)
                    .JustifyContent(JustifyContent.Center).AlignItems(AlignItems.Center)
                    .Tooltip(by.User?.Name ?? "")
                    .Children(Text.New(Initials(by.User?.Name ?? "?")).FontSize(8.5f).TextColor(Nocturne.Accent100)));
        else
            who.AddChild(Text.New("Nobody yet").FontSize(11f).TextColor(Nocturne.On(0.45)));
        appr.AddChild(who);
        grid.AddChild(MrCard("Approvals", appr).FlexGrow(1f));
        return grid;
    }

    /// <summary>The description, drawn as markdown — see <c>JunctionApp.Markdown</c>. One
    /// that is empty, or only a template's comments, says so.</summary>
    private Element MrDescription(string? description)
    {
        var doc = MarkdownReader.Read(description);
        return doc.IsEmpty
            ? Text.New("No description.").FontSize(12.5f).TextColor(Nocturne.On(0.45))
            : MarkdownView(doc, new MdLook(12.5f, Nocturne.On(0.84)));
    }

    private Element MrChanges(string path, long iid, GlMergeRequest mr, IReadOnlyList<GlDiscussion> discussions)
    {
        var diffs = MrDiffsFor(path, iid);
        var col = Flex.Column(gap: 6);
        var head = Flex.Row(gap: 8).AlignItems(AlignItems.Center);
        head.AddChild(Text.New($"Changes · {(diffs.Loaded ? diffs.Value.Count.ToString() : mr.ChangesCount ?? "…")} files")
            .FontSize(12.5f).FontWeight(FontWeight.Medium).FlexGrow(1));
        col.AddChild(head);
        var rows = Flex.Column(gap: 1);
        foreach (var d in diffs.Value)
        {
            var row = Flex.Row(gap: 8).AlignItems(AlignItems.Center)
                .Padding(new Thickness(5, 8, 5, 8)).CornerRadius(6f);
            row.AddChild(Ui.Mono(d.StatusLetter.ToString(), 11f, Nocturne.StatusLetter(d.StatusLetter)).Width(14).FlexShrink(0));
            row.AddChild(Ui.Mono(d.Path, 12f, Nocturne.Text).FlexShrink(1).FlexGrow(1).OverflowMode(TextOverflow.Ellipsis).MaxLines(1));
            int threads = discussions.Count(t => !t.IndividualNote && t.Position?.NewPath == d.Path);
            if (threads > 0)
                row.AddChild(Flex.Row(gap: 3).AlignItems(AlignItems.Center).Padding(new Thickness(0, 6, 0, 6)).CornerRadius(999f)
                    .Background(Nocturne.Neutral800).Children(
                        Ui.Icon(Icons.ChatCircle, 10f, Nocturne.Neutral100),
                        Text.New(threads.ToString()).FontSize(10f).TextColor(Nocturne.Neutral100)));
            row.HoverBg(Nocturne.Neutral900, Color.Transparent);
            row.Cursor(CursorType.Pointer).Tooltip("Review this file");
            var file = d.Path;
            row.OnClick(_ => StartReview(path, iid, openFile: file));
            rows.AddChild(row);
        }
        if (diffs.Value.Count == 0)
            rows.AddChild(Text.New(diffs.Loaded ? "No changed files." : "Loading files…").FontSize(12f).TextColor(Nocturne.On(0.45))
                .Padding(new Thickness(4, 8, 4, 8)));
        col.AddChild(rows);
        return col;
    }

    private Element MrDiscussions(string path, long iid, IReadOnlyList<GlDiscussion> discussions)
    {
        var threads = discussions.Where(t => t.First is { System: false }).ToList();
        var col = Flex.Column(gap: 6);
        col.AddChild(Text.New($"Discussions · {threads.Count}").FontSize(12.5f).FontWeight(FontWeight.Medium));
        var rows = Flex.Column(gap: 2);
        foreach (var t in threads)
        {
            var first = t.First!;
            var row = Flex.Row(gap: 9).AlignItems(AlignItems.Center).Padding(new Thickness(6, 8, 6, 8)).CornerRadius(6f);
            bool resolved = t.IsResolved;
            row.AddChild(Ui.Icon(resolved ? Icons.CheckCircle : Icons.ChatCircleFill, 14f,
                resolved ? Nocturne.On(0.4) : Nocturne.Accent300).FlexShrink(0));
            var mid = Flex.Column(gap: 2).FlexGrow(1).FlexShrink(1).FlexBasis(Dimension.Px(0));
            mid.AddChild(Text.New(FirstTextLine(first.Body)).FontSize(12.5f).OverflowMode(TextOverflow.Ellipsis).MaxLines(1));
            if (first.Position is { } pos && (pos.NewPath ?? pos.OldPath) is { } file)
            {
                string where = pos.LineRange is { Start: { } s, End: { } e } && (s.NewLine ?? s.OldLine) != (e.NewLine ?? e.OldLine)
                    ? $"lines {s.NewLine ?? s.OldLine}–{e.NewLine ?? e.OldLine}"
                    : $"line {pos.NewLine ?? pos.OldLine}";
                mid.AddChild(Ui.Mono($"{file} · {where}", 10.5f, Nocturne.On(0.45)).OverflowMode(TextOverflow.Ellipsis).MaxLines(1));
            }
            row.AddChild(mid);
            row.AddChild(Text.New($"{first.Author?.Name ?? ""} · {RelativeTime(first.CreatedAt)}").FontSize(11f)
                .TextColor(Nocturne.On(0.45)).FlexShrink(0));
            // A thread on the diff opens the review on it; one on the request itself has
            // no line to go to.
            if (first.Position is { } p && (p.NewPath ?? p.OldPath) is { } threadFile)
            {
                var id = t.Id;
                row.Cursor(CursorType.Pointer).Tooltip("Open this thread in the diff");
                row.HoverBg(Nocturne.Neutral900, Color.Transparent);
                row.OnClick(_ => StartReview(path, iid, openFile: threadFile, focusThread: id));
            }
            rows.AddChild(row);
        }
        if (threads.Count == 0)
            rows.AddChild(Text.New("No discussions yet.").FontSize(12f).TextColor(Nocturne.On(0.45)).Padding(new Thickness(4, 8, 4, 8)));
        col.AddChild(rows);
        return col;
    }

    private static string FirstTextLine(string body) => MarkdownReader.FirstLine(body);

    private Hosted<IReadOnlyList<GlDiff>> MrDiffsFor(string path, long iid)
    {
        if (GitLabRemoteFor(path) is not { } remote || _gitlab.ClientFor(remote.Host) is not { } client)
            return Hosted<IReadOnlyList<GlDiff>>.Empty([]);
        var loader = _mrDiffLoaders.For((path, iid));
        var prev = loader.Value;
        loader.Load($"{path}#{iid}#{HostedGen(path)}", () =>
        {
            var id = ResolveProjectId(client, remote);
            if (id is null) return prev;
            var d = Sync(client.GetMergeRequestDiffsAsync(id.Value, iid));
            return d.Ok ? new Hosted<IReadOnlyList<GlDiff>>(d.Value, DateTimeOffset.UtcNow, null, true) : Keep(prev, d.Failure!);
        });
        return loader.Value;
    }

    // ── Checkout locally ─────────────────────────────────────────────────────────────

    /// <summary>The remote the project was detected from: the chosen one, else origin.</summary>
    private string HostingRemoteName(string path) =>
        _settings.RepoHostingRemote.GetValueOrDefault(NormPath(path)) ?? "origin";

    /// <summary>
    /// Brings the merge request's head down as <c>refs/remotes/&lt;remote&gt;/merge-requests/N</c>
    /// and puts the source branch on it: created when there is none, checked out and
    /// fast-forwarded when there is. A local branch that has diverged is checked out as it
    /// is and said so — nothing here rewrites work.
    /// </summary>
    private async void CheckoutMergeRequest(string path, GlMergeRequest mr)
    {
        if (_mrBusy != null || RemoteOpRunning) return;
        // The branch is checked out in that repository, and History then shows it there.
        if (!PathEquals(path, _activeRepoPath)) SelectRepo(path);
        var repo = RepoFor(path);
        var remoteName = HostingRemoteName(path);
        var fetched = $"refs/remotes/{remoteName}/merge-requests/{mr.Iid}";
        _mrBusy = $"Fetching refs/merge-requests/{mr.Iid}/head…";
        _mrBusyAction = "checkout";
        MarkDirty();

        var fetch = await repo.FetchRefspecAsync(remoteName, $"+refs/merge-requests/{mr.Iid}/head:{fetched}", NoAskPass);
        if (!fetch.Success)
        {
            _mrBusy = null;
            _mrBusyAction = null;
            Flash(FirstLine(fetch.Stderr, "Could not fetch the merge request"));
            MarkDirty();
            return;
        }

        var branch = mr.SourceBranch;
        var (result, note) = await Task.Run(() =>
        {
            if (repo.LocalBranchExists(branch))
            {
                var co = repo.Checkout(branch);
                if (!co.Success) return (co, "");
                var ff = repo.MergeFastForwardOnly(fetched);
                return (co, ff.Success ? "" : " — your local branch has diverged, left as it is");
            }
            var create = repo.CreateBranch(branch, fetched);
            if (!create.Success) return (create, "");
            return (repo.Checkout(branch), "");
        });

        _mrBusy = null;
        _mrBusyAction = null;
        if (!result.Success)
        {
            Flash(FirstLine(result.Stderr, "Checkout failed"));
            MarkDirty();
            return;
        }
        Flash($"Checked out {branch} at !{mr.Iid}{note}");
        BumpDataGen(path);
        CloseMergeRequests();
        SetView(JunctionView.History);
        Rerender();
    }

    // ── Keys ─────────────────────────────────────────────────────────────────────────

    private bool HandleMergeRequestsKey(KeyDownEvent e, bool typing)
    {
        bool inSearch = typing && e.Target is TextInputElement { AutomationId: "mr-search" };

        if (e.Key == KeyCode.Escape)
        {
            if (_mrOpen != null) { BackToMrList(); return true; }
            if (inSearch && _mrQuery.Length > 0) { _mrQuery = ""; if (_mrSearchRef.Current is { } field) field.Text = ""; _mrSel = 0; MarkDirty(); return true; }
            CloseMergeRequests();
            return true;
        }
        if (typing && e.Key is not (KeyCode.Up or KeyCode.Down or KeyCode.Enter)) return false;

        var open = _mrOpen is { } o ? MergeRequestsFor(o.Path).Value.FirstOrDefault(m => m.Iid == o.Iid) : null;
        var openPath = _mrOpen?.Path;
        var selected = _mrOpen is null && _mrSel >= 0 && _mrSel < _mrRows.Count ? _mrRows[_mrSel] : null;

        if (_mrOpen is null && Shortcuts.Pressed("mr.search", e))
        {
            _pendingMrSearchFocus = true;
            MarkDirty();
            return true;
        }
        if (Shortcuts.Pressed("mr.checkout", e))
        {
            if (open != null && openPath != null) CheckoutMergeRequest(openPath, open);
            else if (selected != null) CheckoutMergeRequest(selected.Path, selected.Mr);
            return true;
        }
        if (Shortcuts.Pressed("mr.review", e))
        {
            if (open != null && openPath != null) StartReview(openPath, open.Iid);
            else if (selected != null) StartReview(selected.Path, selected.Mr.Iid);
            return true;
        }
        if (Shortcuts.Pressed("mr.approve", e))
        {
            if (open != null && openPath != null) ToggleApproval(openPath, open, ApprovalsFor(openPath, open.Iid).Value);
            else if (selected != null) ToggleApproval(selected.Path, selected.Mr, ApprovalsFor(selected.Path, selected.Mr.Iid).Value);
            return true;
        }
        if (Shortcuts.Pressed("mr.browser", e))
        {
            OpenUrl((open ?? selected?.Mr)?.WebUrl);
            return true;
        }

        switch (e.Key)
        {
            case KeyCode.Up when _mrOpen is null:
                if (_mrSel > 0) { _mrSel--; MarkDirty(); }
                return true;
            case KeyCode.Down when _mrOpen is null:
                if (_mrSel < _mrRows.Count - 1) { _mrSel++; MarkDirty(); }
                return true;
            case KeyCode.Enter when _mrOpen is null:
                if (selected != null) { _mrOpen = (selected.Path, selected.Mr.Iid); Rerender(); }
                return true;
        }
        return false;
    }

    private void OpenUrl(string? url)
    {
        if (string.IsNullOrEmpty(url)) return;
        if (!Platform.ShellLauncher.Open(url, out var error)) Flash($"Could not open {url}: {error}");
    }

    // ── Pieces other views borrow ────────────────────────────────────────────────────

    /// <summary>The <c>!412</c> chip a branch row, a History row or a header carries;
    /// clicking it opens the merge request.</summary>
    private BoxElement MergeRequestChip(GlMergeRequest mr)
    {
        var chip = Box.New().Padding(new Thickness(0, 6, 0, 6)).CornerRadius(999f)
            .Outline(1f, Nocturne.Accent700).Cursor(CursorType.Pointer)
            .Tooltip($"!{mr.Iid} {mr.Title}")
            .Children(Ui.Mono($"!{mr.Iid}", 10f, Nocturne.Accent200));
        chip.On<ClickEvent>(e => { e.StopPropagation = true; OpenMergeRequest(_activeRepoPath, mr.Iid); });
        chip.AutomationId = "mr-chip";
        return chip;
    }

    private static string PipelineSummary(GlPipeline p)
    {
        var started = p.StartedAt ?? p.CreatedAt;
        return started is { } t
            ? $"Pipeline #{p.Id} · {Ui.PipelineWord(p.Status)} · started {RelativeTime(t)}"
            : $"Pipeline #{p.Id} · {Ui.PipelineWord(p.Status)}";
    }

    private static string ProtectionSummary(GlProtectedBranch rule)
    {
        var who = rule.PushLevel switch
        {
            0 => "no one",
            30 => "developers and above",
            40 => "maintainers",
            60 => "admins",
            var n => $"access level {n}",
        };
        return $"{rule.Name} is protected — {who} can push, force push {(rule.AllowForcePush ? "allowed" : "not allowed")}";
    }

    /// <summary>The toolbar's glyph for the current branch's head: its pipeline, and the
    /// merge request it is the source of. Null when there is nothing to say.</summary>
    private BoxElement? HeadPipelineButton(bool compact)
    {
        if (!HasActiveRepo || !GitLabActive(_activeRepoPath)) return null;
        // The toolbar is built on every view, so this is where the project — and with it the
        // protected-branch rules a push confirm quotes — starts loading, before anything asks.
        _ = ProjectFor(_activeRepoPath);
        var sum = SummaryFor(_activeRepoPath);
        var current = sum.Branches.FirstOrDefault(b => b.IsCurrent);
        var pipe = PipelineForSha(_activeRepoPath, current?.Hash);
        var mr = MergeRequestForBranch(_activeRepoPath, sum.Branch);
        if (pipe is null && mr is null) return null;

        var b = Flex.Row(gap: 5).Height(30).AlignItems(AlignItems.Center)
            .Padding(new Thickness(0, 7, 0, 7)).CornerRadius(Nocturne.RadiusMd)
            .BorderColor(Nocturne.Divider).BorderWidth(1f).Cursor(CursorType.Pointer).FlexShrink(0);
        var tip = new List<string>();
        if (pipe != null && Ui.PipelineMark(pipe.Status) is { } mark)
        {
            b.AddChild(Ui.Icon(mark.Glyph, 14f, mark.Color));
            tip.Add(PipelineSummary(pipe));
        }
        else
        {
            b.AddChild(Ui.Icon(Icons.GitPullRequest, 14f, Nocturne.Accent300));
        }
        if (mr != null)
        {
            b.AddChild(Box.New().Padding(new Thickness(1, 6, 1, 6)).CornerRadius(999f).Background(Nocturne.Accent800)
                .Children(Ui.Mono($"!{mr.Iid}", 10.5f, Nocturne.Accent100)));
            tip.Add($"!{mr.Iid} {mr.Title}");
        }
        b.Tooltip(string.Join(" · ", tip));
        b.AccessibleName = string.Join(" · ", tip);
        b.OnClick(_ => { if (mr != null) OpenMergeRequest(_activeRepoPath, mr.Iid); else OpenUrl(pipe!.WebUrl); });
        b.HoverBg(Nocturne.Alpha(Nocturne.Accent, 0.08), Color.Transparent);
        return b;
    }

    /// <summary>The Commit header's line: the pipeline on this commit, and the merge request
    /// it heads. Null unless the repository is GitLab-backed and there is something to say.</summary>
    private Element? BuildCommitHostedLine(string hash)
    {
        if (!GitLabActive(_activeRepoPath)) return null;
        var pipe = PipelineForSha(_activeRepoPath, hash);
        var mr = MergeRequestForSha(_activeRepoPath, hash);
        if (pipe is null && mr is null) return null;
        var row = Flex.Row(gap: 10).AlignItems(AlignItems.Center);
        if (pipe != null && Ui.PipelineMark(pipe.Status) is { } mark)
        {
            var started = pipe.StartedAt ?? pipe.CreatedAt;
            var line = Flex.Row(gap: 5).AlignItems(AlignItems.Center).Cursor(CursorType.Pointer)
                .Tooltip("Open pipeline in the browser");
            line.AddChild(Ui.Icon(mark.Glyph, 13f, mark.Color));
            line.AddChild(Text.New($"Pipeline #{pipe.Id} · {Ui.PipelineWord(pipe.Status)}{(started is { } t ? $" · started {RelativeTime(t)}" : "")}")
                .FontSize(11.5f).TextColor(Nocturne.On(0.55)));
            line.OnClick(_ => OpenUrl(pipe.WebUrl));
            row.AddChild(line);
        }
        if (mr != null) row.AddChild(MergeRequestChip(mr));
        return row;
    }

    /// <summary>The Overview card's hosted footer: default-branch pipeline · N MRs · M need you.</summary>
    /// <summary>The Overview's strip: how many open requests across the workspace name the
    /// signed-in user as reviewer, and the way to them. Absent when no repository in the
    /// workspace is on a GitLab with an account.</summary>
    private Element? BuildOverviewReviewStrip(IReadOnlyList<string> repos)
    {
        var hosted = repos.Where(GitLabActive).ToList();
        if (hosted.Count == 0) return null;
        int count = 0, across = 0;
        DateTimeOffset? asOf = null;
        foreach (var path in hosted)
        {
            var mrs = MergeRequestsFor(path);
            if (mrs.Loaded) asOf = asOf is null || mrs.AsOf < asOf ? mrs.AsOf : asOf;
            var me = MyIdOn(path);
            int mine = me is { } id ? mrs.Value.Count(m => m.IsOpen && m.Reviewers.Any(u => u.Id == id)) : 0;
            if (mine > 0) { count += mine; across++; }
        }

        var strip = Flex.Row(gap: 10).AlignItems(AlignItems.Center).FlexShrink(0)
            .Padding(new Thickness(12, 16, 12, 16)).CornerRadius(Nocturne.RadiusMd)
            .Background(Nocturne.Surface).BorderColor(Nocturne.Neutral700).BorderWidth(1f);
        strip.AutomationId = "overview-review-strip";
        strip.AddChild(Ui.Icon(Icons.GitLabLogo, 15f, Nocturne.Accent300).FlexShrink(0));
        var text = count == 0
            ? "Nothing is waiting for your review."
            : $"You have {count} merge request{(count == 1 ? "" : "s")} to review across {across} repositor{(across == 1 ? "y" : "ies")}.";
        strip.AddChild(Text.New(text).FontSize(13f).FlexShrink(1).MaxLines(1).OverflowMode(TextOverflow.Ellipsis));
        if (asOf is { } at)
            strip.AddChild(Text.New(AsOfText(at)).FontSize(11f).TextColor(Nocturne.On(0.42)).FlexShrink(0));
        strip.AddChild(Box.New().FlexGrow(1));
        var open = Ui.GhostButton(count == 0 ? "Open merge requests" : "Review them", Icons.ArrowRight,
            () => OpenWorkspaceReviews(hosted), 12.5f);
        open.AccessibleName = count == 0 ? "Open merge requests" : "Review them";
        strip.AddChild(open.FlexShrink(0));
        return strip;
    }

    /// <summary>The panel in workspace scope on Reviewing. The panel keys off the active
    /// repository, so a workspace whose active one is not on GitLab moves to one that is.</summary>
    private void OpenWorkspaceReviews(IReadOnlyList<string> hosted)
    {
        if (!GitLabActive(_activeRepoPath) && hosted.Count > 0) SelectRepo(hosted[0]);
        _mrScope = MrScope.Workspace;
        OpenMergeRequests(MrFilter.Reviewing);
    }

    private Element? BuildOverviewHostedLine(string path)
    {
        if (!GitLabActive(path)) return null;
        var sum = SummaryFor(path);
        var defaultBranch = ProjectFor(path).Value?.Project.DefaultBranch ?? "main";
        var tip = sum.Branches.FirstOrDefault(b => !b.IsRemote && b.Name == defaultBranch)
                  ?? sum.Branches.FirstOrDefault(b => b.IsRemote && b.Name.EndsWith("/" + defaultBranch, StringComparison.Ordinal));
        var pipe = PipelineForSha(path, tip?.Hash);
        var mrs = MergeRequestsFor(path).Value.Where(m => m.IsOpen).ToList();
        var me = MyIdOn(path);
        int needMe = me is { } id
            ? mrs.Count(m => m.Assignees.Any(u => u.Id == id) || m.Reviewers.Any(u => u.Id == id))
            : 0;

        var line = Flex.Row(gap: 6).AlignItems(AlignItems.Center)
            .Padding(new Thickness(10, 0, 0, 0))
            .BorderColor(Nocturne.Divider).BorderWidth(new Thickness(1, 0, 0, 0));
        if (pipe != null && Ui.PipelineMark(pipe.Status) is { } mark)
        {
            line.AddChild(Ui.Icon(mark.Glyph, 13f, mark.Color));
            line.AddChild(Text.New($"{defaultBranch} {Ui.PipelineWord(pipe.Status)}").FontSize(11.5f).TextColor(Nocturne.On(0.55)));
        }
        else
        {
            line.AddChild(Ui.Icon(Icons.Minus, 13f, Nocturne.On(0.35)));
            line.AddChild(Text.New("no pipeline").FontSize(11.5f).TextColor(Nocturne.On(0.55)));
        }
        line.AddChild(Text.New("·").FontSize(11.5f).TextColor(Nocturne.On(0.5)));
        var count = Text.New(mrs.Count == 1 ? "1 MR" : $"{mrs.Count} MRs").FontSize(11.5f).TextColor(Nocturne.Accent).Cursor(CursorType.Pointer)
            .Tooltip("Open merge requests");
        count.On<ClickEvent>(e => { e.StopPropagation = true; SelectRepo(path); OpenMergeRequests(MrFilter.AllOpen); });
        line.AddChild(count);
        if (needMe > 0)
            line.AddChild(Text.New($"{needMe} need you").FontSize(11.5f).TextColor(Nocturne.Accent300));
        line.AutomationId = "overview-hosted";
        return line;
    }
}
