using Aperture.Core;
using Aperture.Core.Requests;
using Aperture.Theme;
using HultUI;
using HultUI.Core.Accessibility;
using HultUI.Core.Builders;
using HultUI.Core.Elements;
using HultUI.Core.Layout;
using HultUI.Core.Primitives;
using HultUI.Core.Styling;

namespace Aperture;

/// <summary>
/// The problem reports, for whoever looks after the library: a list of what has been
/// reported (open ones first — the list starts there), and one report's page with its
/// conversation, a reply box and Resolve.
///
/// <para>A rail door of its own, shown only to someone the server lets read everyone's
/// reports (Manage issues or View issues). Answering and resolving take Manage issues —
/// or being the person who reported it, which the server allows too; a reader without
/// either sees the conversation and no reply box.</para>
/// </summary>
public sealed partial class ApertureApp
{
    /// <summary>Bumped by a reply or a change of status. Apart from the request
    /// generation so that answering a report does not re-read Discover.</summary>
    private int _issueGeneration;

    private static readonly (IssueFilter Filter, string Label)[] IssueFilters =
    [
        (IssueFilter.Open, "Open"),
        (IssueFilter.Resolved, "Resolved"),
        (IssueFilter.All, "All"),
    ];

    // ── The list ─────────────────────────────────────────────────────────

    private readonly AsyncLoader<IssuePage> _issuePage = new(IssuePage.Empty);
    private readonly List<MediaIssue> _issueListItems = [];
    private string? _issueListKey;
    private int _issueListTotal;

    private IssueFilter? _issueListFilter;

    /// <summary>Open reports for someone looking after the library; all of them for a
    /// reporter, to whom a resolved report is news rather than something done with.</summary>
    internal IssueFilter IssueListFilter => _issueListFilter ?? (IsReporterOnly ? IssueFilter.All : IssueFilter.Open);
    internal IReadOnlyList<MediaIssue> IssueList => _issueListItems;
    internal int IssueListTotal => _issueListTotal;
    internal bool IssueListLoading => _issuePage.IsLoading;
    internal bool IssueListHasMore => _issueListItems.Count < _issueListTotal;

    private void EnsureIssueList(bool more = false)
    {
        if (_requestServer is not { } server) return;
        var filter = IssueListFilter;
        var key = $"{_requestGeneration}|{_dataGeneration}|{_issueGeneration}|{filter}";
        if (_issueListKey != key)
        {
            _issueListKey = key;
            _issueListItems.Clear();
            _issueListTotal = 0;
        }
        else if (!more || !IssueListHasMore)
        {
            if (_issueListItems.Count > 0 || _issuePage.IsLoading) return;
        }

        int skip = _issueListItems.Count;
        _issuePage.Load((key, skip), () => FetchRequest(
            () => server.GetIssuesAsync(filter, skip, RequestPageSize), IssuePage.Empty, "The reports"),
            page =>
            {
                if (_issueListKey != key || page.Skip != _issueListItems.Count) return;
                _issueListItems.AddRange(page.Items);
                _issueListTotal = page.Total;
            });
    }

    internal void ShowMoreIssues()
    {
        EnsureIssueList(more: true);
        Rerender();
    }

    internal void SetIssueListFilter(IssueFilter filter)
    {
        if (IssueListFilter == filter) return;
        _issueListFilter = filter;
        Rerender();
    }

    // ── The badge ────────────────────────────────────────────────────────

    private readonly AsyncLoader<IssueCounts?> _issueCounts = new(null);

    private void EnsureIssueCounts()
    {
        if (_requestServer is not { } server || !server.User.CanSeeIssues) return;
        _issueCounts.Load(("issue-counts", _requestGeneration, _dataGeneration, _issueGeneration), () =>
            FetchRequest<IssueCounts?>(async () =>
            {
                var counts = await server.GetIssueCountsAsync().ConfigureAwait(false);
                return counts.Ok ? ApertureResult<IssueCounts?>.Success(counts.Value) : counts.As<IssueCounts?>();
            }, null, "The report count"));
    }

    /// <summary>Open reports, for the rail's badge — everyone's, which is what the server counts.</summary>
    internal int OpenIssueCount => _requestServer is null ? 0 : _issueCounts.Value?.Open ?? 0;

    // ── One report ───────────────────────────────────────────────────────

    /// <summary>A report as loaded — or, with no issue, the one that would not load.</summary>
    internal sealed record IssueLoad(int Id, MediaIssue? Issue);

    private readonly AsyncLoader<IssueLoad?> _issue = new(null);
    private int _issueReplyFor;
    private string _issueReply = "";
    private bool _issueSending;

    private void EnsureIssue(int id)
    {
        if (_requestServer is not { } server) return;
        _issue.Load(("issue", _requestGeneration, _dataGeneration, _issueGeneration, id), () =>
        {
            var loaded = FetchRequest<MediaIssue?>(async () =>
            {
                var issue = await server.GetIssueAsync(id).ConfigureAwait(false);
                return issue.Ok ? ApertureResult<MediaIssue?>.Success(issue.Value) : issue.As<MediaIssue?>();
            }, null, "The report");
            return (IssueLoad?)new IssueLoad(id, loaded);
        }, load =>
        {
            if (load?.Issue is { } issue) MarkIssuesRead([issue]);
        });
    }

    /// <summary>May answer, resolve and reopen this report: a manager, or its reporter.</summary>
    private bool CanAnswer(MediaIssue issue) =>
        _requestServer?.User is { } user && (user.CanManageIssues || issue.ReportedById == user.Id);

    // ── The verbs ────────────────────────────────────────────────────────

    /// <summary>Answers a report, and — when <paramref name="status"/> is given — resolves
    /// or reopens it in the same go. The reply goes first, so a resolve that fails still
    /// leaves the answer said.</summary>
    internal void AnswerIssue(MediaIssue issue, string reply, IssueStatus? status)
    {
        if (_requestServer is not { } server || _issueSending) return;
        reply = reply.Trim();
        if (reply.Length == 0 && status is null) return;
        _issueSending = true;
        var name = issue.Title ?? "the report";
        _ = ApplyIssueAsync(async () =>
        {
            if (reply.Length > 0)
            {
                var answered = await server.CommentOnIssueAsync(issue.Id, reply).ConfigureAwait(false);
                if (!answered.Ok || status is null) return answered;
            }
            return await server.SetIssueStatusAsync(issue.Id, status!.Value).ConfigureAwait(false);
        }, (status, reply.Length > 0) switch
        {
            (IssueStatus.Resolved, true) => $"Answered and resolved {name}",
            (IssueStatus.Resolved, false) => $"Resolved {name}",
            (IssueStatus.Open, true) => $"Answered and reopened {name}",
            (IssueStatus.Open, false) => $"Reopened {name}",
            _ => $"Answered {name}",
        }, clearReply: reply.Length > 0);
    }

    private async Task ApplyIssueAsync(Func<Task<ApertureResult<MediaIssue>>> call, string done, bool clearReply)
    {
        var result = await call().ConfigureAwait(false);
        Application.InvokeOnMainThread(() =>
        {
            _issueSending = false;
            if (!result.Ok)
            {
                FlashRequestFailure(result.Failure!, "The report");
                Rerender();
                return;
            }
            Flash(done, ToastSeverity.Success);
            // What the reporter has now seen — marked before the lists read again, so their
            // own reply is never their own news.
            MarkIssuesRead([result.Value]);
            if (clearReply) _issueReply = "";
            _issueGeneration++;
            Rerender();
        });
    }

    // ── The list view ────────────────────────────────────────────────────

    private Element BuildIssuesView()
    {
        EnsureIssueList();

        var column = Flex.Column(gap: 0).FlexGrow(1).AutomationId("issues-view");
        var bar = Flex.Row(gap: Halide.S3).FlexShrink(0)
            .Height(56).AlignItems(AlignItems.Center)
            .Padding(new Thickness(0, Halide.S5, 0, Halide.S5))
            .BorderColor(Halide.Border).BorderWidth(new Thickness(0, 0, 1, 0));
        bool own = IsReporterOnly;
        bar.AddChild(Text.New(own ? "Your reports" : "Issues").FontSize(18f).FontWeight(FontWeight.SemiBold)
            .TextColor(Halide.Text));
        if (IssueListTotal > 0) bar.AddChild(Ui.Mono(IssueListTotal.ToString(), 11.5f, Halide.TextFaint));
        bar.AddChild(Box.New().Width(Halide.S3));
        foreach (var (filter, label) in IssueFilters)
        {
            var f = filter;
            bar.AddChild(FilterChip(label, IssueListFilter == f, () => SetIssueListFilter(f),
                "issues-filter-" + f.ToString().ToLowerInvariant()));
        }
        column.AddChild(bar);

        var items = IssueList;
        if (items.Count == 0)
        {
            SetCursorList("issues:empty", [], 1);
            column.AddChild(EmptyState(IssueListLoading ? Icons.Loading : Icons.Report,
                IssueListLoading ? $"Asking {RequestServerTitle}…"
                    : own && IssueListFilter == IssueFilter.All ? "You haven't reported anything"
                    : IssueListFilter == IssueFilter.Open ? "No open reports" : "No reports here",
                IssueListLoading ? null
                    : own && IssueListFilter == IssueFilter.All
                        ? "Something wrong with what's playing? Report it from the player — the answer comes back here."
                    : IssueListFilter == IssueFilter.Open ? "A problem reported from the player lands here."
                    : "Nothing matches that filter.",
                "issues-empty"));
            return column;
        }

        var scroll = ScrollView.Vertical().FlexGrow(1);
        var list = Flex.Column(gap: Halide.S1).Padding(new Thickness(Halide.S3, Halide.S5, Halide.S5, Halide.S5));
        SetCursorList($"issues:{IssueListFilter}", items.Cast<object>().ToList(), 1);
        for (int i = 0; i < items.Count; i++) list.AddChild(IssueRow(items[i], i));

        if (IssueListHasMore)
        {
            var more = Flex.Row(gap: Halide.S2).AlignItems(AlignItems.Center).Padding(new Thickness(Halide.S3, 0, 0, 0));
            more.AddChild(Ui.Button(ButtonVariant.Secondary,
                IssueListLoading ? "Loading…" : $"Show more ({IssueListTotal - items.Count} left)",
                IssueListLoading ? null : ShowMoreIssues, "issues-more"));
            list.AddChild(more);
        }
        scroll.AddChild(list);
        column.AddChild(scroll);
        return column;
    }

    /// <summary>One report: the poster, the title, what kind of problem and where, who and
    /// when, the first line of what they said, and where it stands. The row opens the
    /// report; Resolve is a button of its own.</summary>
    private Element IssueRow(MediaIssue issue, int index)
    {
        var row = Flex.Row(gap: Halide.S3).AlignItems(AlignItems.Center)
            .Padding(new Thickness(Halide.S2))
            .CornerRadius(Halide.Radius2)
            .Cursor(CursorType.Pointer)
            .AccessibleRole(AccessibilityRole.Button)
            .AccessibleName(IssueAccessibleName(issue) + (HasNews(issue) ? ", new" : ""))
            .AutomationId("issue-" + issue.Id);
        row.IsFocusable = true;
        TrackCursor(row, index);
        if (IsCursor(index)) row.Outline(2f, Halide.Accent, offset: 1f);
        row.HoverBg(Halide.Raised);

        row.AddChild(IssuePoster(issue, 46, 69));

        bool news = HasNews(issue);
        var text = Flex.Column(gap: 3).FlexGrow(1).FlexShrink(1);
        var titleLine = Flex.Row(gap: Halide.S2).AlignItems(AlignItems.Center);
        if (news)
            titleLine.AddChild(Box.New().Width(7).Height(7).FlexShrink(0).CornerRadius(3.5f).Background(Halide.Accent));
        titleLine.AddChild(Ui.Line(IssueTitle(issue), 13f, Halide.Text).FlexShrink(1));
        text.AddChild(titleLine);
        var what = new List<string> { IssueTypeLabel(issue.Type) };
        if (issue.Where is { Length: > 0 } where) what.Add(where);
        // A reporter's list is all their own: their name on every row says nothing.
        if (!IsReporterOnly) what.Add(issue.ReportedBy);
        what.Add(Ui.Ago(issue.CreatedAt));
        text.AddChild(Ui.Line(string.Join(" · ", what), 11.5f, Halide.TextMuted));
        if (issue.Comments.FirstOrDefault() is { } first)
            text.AddChild(Ui.Line(first.Message.ReplaceLineEndings(" "), 12f, Halide.TextDim));
        row.AddChild(text);

        int replies = Math.Max(0, issue.Comments.Count - 1);
        if (replies > 0)
            row.AddChild(Ui.Line(replies == 1 ? "1 reply" : $"{replies} replies", 11f,
                news ? Halide.Accent : Halide.TextFaint).FlexShrink(0));
        row.AddChild(IssueStatusChip(issue));

        var verbs = Flex.Row(gap: Halide.S1).AlignItems(AlignItems.Center).FlexShrink(0);
        if (issue.Status == IssueStatus.Open && _requestServer?.User.CanManageIssues == true)
            verbs.AddChild(Ui.IconButton(Icons.Approve, "Resolve", () => AnswerIssue(issue, "", IssueStatus.Resolved),
                15f, Halide.StatusOk, $"issue-{issue.Id}-resolve"));
        verbs.OnClick(e => e.StopPropagation = true);
        row.AddChild(verbs);

        row.OnClick(_ => OpenIssue(issue));
        return row;
    }

    internal void OpenIssue(MediaIssue issue) => Navigate(new Route(ApertureView.Issue, IssueRouteId(issue), issue.Title));

    private static string IssueRouteId(MediaIssue issue) =>
        issue.Id.ToString(System.Globalization.CultureInfo.InvariantCulture);

    // ── One report's page ────────────────────────────────────────────────

    private Element BuildIssueView(Route route)
    {
        int id = int.TryParse(route.Id, System.Globalization.NumberStyles.Integer,
            System.Globalization.CultureInfo.InvariantCulture, out var parsed) ? parsed : 0;
        EnsureIssue(id);
        if (_issueReplyFor != id)
        {
            _issueReplyFor = id;
            _issueReply = "";
        }

        var column = Flex.Column(gap: 0).FlexGrow(1).AutomationId("issue-view");
        // The loader keeps the last report while the next one loads; only this one's counts.
        var load = _issue.Value is { } l && l.Id == id ? l : null;
        column.AddChild(ViewBar(load?.Issue is { } named ? IssueTitle(named) : route.Title ?? "Report", 0, "issue"));

        if (load?.Issue is not { } issue)
        {
            SetCursorList("issue:empty", [], 1);
            column.AddChild(load is null
                ? EmptyState(Icons.Loading, $"Asking {RequestServerTitle}…", null, "issue-loading")
                : EmptyState(Icons.Report, "This report didn't load",
                    $"{RequestServerTitle} may have deleted it, or be busy — try again later.", "issue-missing"));
            return column;
        }
        SetCursorList("issue:" + id, [], 1);

        var scroll = ScrollView.Vertical().FlexGrow(1);
        var page = Flex.Column(gap: Halide.S5).Padding(new Thickness(Halide.S5)).MaxWidth(Dimension.Px(860));

        // The title, what is wrong, where it stands.
        var head = Flex.Row(gap: Halide.S4).AlignItems(AlignItems.FlexStart);
        head.AddChild(IssuePoster(issue, 80, 120));
        var facts = Flex.Column(gap: Halide.S2).FlexGrow(1).FlexShrink(1);
        var chips = Flex.Row(gap: Halide.S2).AlignItems(AlignItems.Center);
        chips.AddChild(IssueStatusChip(issue));
        chips.AddChild(Ui.Chip(IssueTypeLabel(issue.Type)));
        if (issue.Where is { Length: > 0 } where) chips.AddChild(Ui.Chip(where));
        facts.AddChild(chips);
        bool yours = issue.ReportedById == _requestServer?.User.Id;
        facts.AddChild(Text.New($"{(yours ? "You reported this" : "Reported by " + issue.ReportedBy)} · {Ui.Ago(issue.CreatedAt)}")
            .FontSize(12.5f).TextColor(Halide.TextMuted));

        var open = Flex.Row(gap: Halide.S2).AlignItems(AlignItems.Center);
        var item = IssueAsItem(issue);
        if (item.JellyfinItemId is { Length: > 0 } && _library is not null)
            open.AddChild(Ui.Button(ButtonVariant.Secondary, "Open in your library",
                () => Navigate(new Route(ApertureView.Detail, item.JellyfinItemId, item.Title)), "issue-open-library"));
        if (issue.TmdbId > 0)
            open.AddChild(Ui.Button(ButtonVariant.Ghost, $"Open on {RequestServerTitle}",
                () => Navigate(new Route(ApertureView.DiscoverTitle, item.Key, item.Title)), "issue-open-title"));
        facts.AddChild(open);
        head.AddChild(facts);
        page.AddChild(head);

        // The conversation, oldest first — the report itself, then the answers.
        var talk = Flex.Column(gap: Halide.S3).AutomationId("issue-conversation");
        foreach (var comment in issue.Comments) talk.AddChild(IssueCommentBubble(issue, comment));
        page.AddChild(talk);

        page.AddChild(CanAnswer(issue)
            ? IssueReplyBox(issue)
            : Text.New($"Only someone who manages issues on {RequestServerTitle} can answer this.")
                .FontSize(12f).TextColor(Halide.TextFaint).AutomationId("issue-read-only"));

        scroll.AddChild(page);
        column.AddChild(scroll);
        return column;
    }

    private Element IssueCommentBubble(MediaIssue issue, IssueComment comment)
    {
        bool mine = comment.AuthorId != 0 && comment.AuthorId == _requestServer?.User.Id;
        bool reporter = comment.AuthorId != 0 && comment.AuthorId == issue.ReportedById;
        var bubble = Flex.Column(gap: Halide.S1)
            .Padding(new Thickness(Halide.S3, Halide.S4, Halide.S3, Halide.S4))
            .CornerRadius(Halide.Radius2)
            .Background(mine ? Halide.Tint(Halide.Accent) : Halide.Surface)
            .BorderColor(Halide.Border).BorderWidth(1f)
            .AccessibleRole(AccessibilityRole.Group)
            .AccessibleName($"{comment.Author}, {Ui.Ago(comment.CreatedAt)}: {comment.Message}")
            .AutomationId("issue-comment-" + comment.Id);
        var who = Flex.Row(gap: Halide.S2).AlignItems(AlignItems.Center);
        who.AddChild(Ui.Line(mine ? $"{comment.Author} (you)" : comment.Author, 12.5f, Halide.Text)
            .FontWeight(FontWeight.SemiBold));
        if (reporter) who.AddChild(Ui.Chip("Reporter"));
        who.AddChild(Ui.Line(Ui.Ago(comment.CreatedAt), 11f, Halide.TextFaint));
        bubble.AddChild(who);
        bubble.AddChild(Text.New(comment.Message).FontSize(13f).TextColor(Halide.TextDim));
        return bubble;
    }

    private Element IssueReplyBox(MediaIssue issue)
    {
        var reply = Flex.Column(gap: Halide.S2);
        // Bare: the theme's field style draws the border, as it does for every text field.
        var message = TextArea.New(_issueReply)
            .PlaceholderText(issue.ReportedById == _requestServer?.User.Id
                ? "Add to your report — more detail, or whether it's fixed now"
                : issue.Status == IssueStatus.Open
                    ? "Answer — what you found, or what you changed"
                    : "Add to the conversation")
            .OnTextChanged(v => { _issueReply = v; MarkDirty(); })
            .Height(Dimension.Px(90)).TextColor(Halide.Text);
        message.AutomationId = "issue-reply";
        message.AccessibleName = "Reply";
        reply.AddChild(message);

        bool said = _issueReply.Trim().Length > 0;
        var actions = Flex.Row(gap: Halide.S2).AlignItems(AlignItems.Center);
        actions.AddChild(Box.New().FlexGrow(1));
        var (flipLabel, flipTo) = issue.Status == IssueStatus.Open
            ? (said ? "Reply and resolve" : "Resolve", IssueStatus.Resolved)
            : (said ? "Reply and reopen" : "Reopen", IssueStatus.Open);
        var flip = Ui.Button(ButtonVariant.Secondary, _issueSending ? "Sending…" : flipLabel,
            _issueSending ? null : () => AnswerIssue(issue, _issueReply, flipTo),
            issue.Status == IssueStatus.Open ? "issue-resolve" : "issue-reopen",
            issue.Status == IssueStatus.Open ? Icons.Approve : null);
        flip.IsEnabled = !_issueSending;
        actions.AddChild(flip);
        var send = Ui.Button(ButtonVariant.Primary, "Reply",
            said && !_issueSending ? () => AnswerIssue(issue, _issueReply, null) : null, "issue-send");
        send.IsEnabled = said && !_issueSending;
        actions.AddChild(send);
        reply.AddChild(actions);
        return reply;
    }

    // ── Pieces ───────────────────────────────────────────────────────────

    private Element IssuePoster(MediaIssue issue, float width, float height)
    {
        var thumb = Box.New().Width(width).Height(height).FlexShrink(0)
            .CornerRadius(Halide.Radius1).Background(Halide.Raised)
            .AlignItems(AlignItems.Center).JustifyContent(JustifyContent.Center);
        thumb.Box.OverflowX = Overflow.Hidden;
        thumb.Box.OverflowY = Overflow.Hidden;
        var (image, status) = RequestArtwork(RequestImages.Poster, issue.PosterPath, width);
        if (status == PosterCache.Status.Ready && image is { IsLoaded: true })
        {
            var picture = Image.New(image);
            picture.Fit = ImageFit.Cover;
            thumb.AddChild(picture.Size(Dimension.Percent(100), Dimension.Percent(100)));
        }
        else thumb.AddChild(Ui.Icon(issue.Kind == DiscoverKind.Movie ? Icons.Movies : Icons.Shows, 16f, Halide.TextFaint));
        return thumb;
    }

    private static string IssueTitle(MediaIssue issue)
    {
        var title = issue.Title ?? $"TMDB {issue.TmdbId}";
        return issue.Year is { } year ? $"{title} ({year})" : title;
    }

    /// <summary>The report panel's own words for the kinds, so a report reads back the way
    /// it was filed.</summary>
    private static string IssueTypeLabel(IssueType type) => type switch
    {
        IssueType.Video => "Picture",
        IssueType.Audio => "Sound",
        IssueType.Subtitles => "Subtitles",
        _ => "Other",
    };

    private static Element IssueStatusChip(MediaIssue issue) => issue.Status == IssueStatus.Open
        ? Ui.Chip("Open", Halide.Accent)
        : Ui.Chip("Resolved", Halide.StatusOk);

    /// <summary>The title a report is about, as a discovery item — so it opens the way a
    /// card for it would.</summary>
    private static DiscoverItem IssueAsItem(MediaIssue issue) => new()
    {
        TmdbId = issue.TmdbId,
        Kind = issue.Kind,
        Title = issue.Title ?? "",
        Year = issue.Year,
        PosterPath = issue.PosterPath,
        JellyfinItemId = issue.JellyfinItemId,
    };

    private static string IssueAccessibleName(MediaIssue issue)
    {
        var parts = new List<string> { issue.Title ?? $"TMDB {issue.TmdbId}", IssueTypeLabel(issue.Type).ToLowerInvariant() + " problem" };
        if (issue.Where is { Length: > 0 } where) parts.Add(where.Replace(" · ", " "));
        parts.Add($"reported by {issue.ReportedBy}");
        parts.Add(issue.Status == IssueStatus.Open ? "open" : "resolved");
        int replies = Math.Max(0, issue.Comments.Count - 1);
        if (replies > 0) parts.Add(replies == 1 ? "1 reply" : $"{replies} replies");
        return string.Join(", ", parts);
    }
}
