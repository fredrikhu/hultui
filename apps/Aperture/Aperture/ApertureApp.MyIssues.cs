using Aperture.Core;
using Aperture.Core.Requests;
using HultUI;
using HultUI.Core.Elements;

namespace Aperture;

/// <summary>
/// A reporter's side of the issues: someone who files problems from the player but may not
/// read everyone's gets a "Your reports" door instead — their own reports, the answers to
/// them, and a badge for the ones with news since they last read them.
///
/// <para>"News" is a report with more messages than when its reporter last read it here, or
/// resolved (or reopened) since. The server keeps no read state, so this is kept in the
/// settings file, per request server, as <see cref="SeenIssue"/>s. A report nobody has read
/// here counts as read at one message and open — which is how it was filed. The first look
/// records everything as read, as the arrival toast's does, so connecting for the first time
/// does not call last year's answers new.</para>
///
/// <para>Looked for on the arrival watch's timer, since the server has no push. The server's
/// own count (<c>/issue/count</c>) is everyone's, whoever asks, so the badge comes from this
/// user's list instead.</para>
/// </summary>
public sealed partial class ApertureApp
{
    /// <summary>Files reports but may not read everyone's: the door shows their own.</summary>
    internal bool IsReporterOnly => _requestServer?.User is { CanSeeIssues: false } && CanReportProblems;

    private readonly AsyncLoader<IReadOnlyList<MediaIssue>?> _myIssues = new(null);
    private IReadOnlyList<MediaIssue> _myIssueList = [];
    private int _myIssuesTick;

    /// <summary>What has been read, loaded from the account once per session. Kept here
    /// alone when there is no account row (the screenshot driver and the tests).</summary>
    private Dictionary<int, SeenIssue>? _seenIssues;
    private bool _seenIssuesLoaded;

    /// <summary>News already toasted this session, as (issue, messages, resolved), so each
    /// change is said once, not every five minutes until it is read.</summary>
    private readonly HashSet<(int, int, bool)> _issueNewsSaid = [];

    private void EnsureMyIssues()
    {
        if (!IsReporterOnly || _requestServer is not { } server) return;
        _myIssues.Load(("my-issues", _requestGeneration, _dataGeneration, _issueGeneration, _myIssuesTick), () =>
        {
            // Quietly, as the arrival watch is: a server that is off is not worth a toast
            // every five minutes, and the pages that need it say so themselves.
            var result = server.GetIssuesAsync(IssueFilter.All, 0, 50).GetAwaiter().GetResult();
            return result.Ok ? result.Value.Items : null;
        }, list =>
        {
            if (list is null || _requestServer != server) return;
            _myIssueList = list;
            NoteIssueNews(list);
        });
    }

    /// <summary>The watch's tick: look again.</summary>
    internal void CheckMyIssues()
    {
        if (!IsReporterOnly) return;
        _myIssuesTick++;
        Rerender();
    }

    /// <summary>Reports of this user's with news — the badge on "Your reports".</summary>
    internal int MyIssueNewsCount => IsReporterOnly ? _myIssueList.Count(HasNews) : 0;

    /// <summary>An answer or a change of status since this user last read their report.
    /// Never for someone else's report, nor for anyone who reads everyone's.</summary>
    internal bool HasNews(MediaIssue issue)
    {
        if (!IsReporterOnly || issue.ReportedById != _requestServer!.User.Id) return false;
        if (SeenIssueMap() is not { } seen) return false;   // before the first look
        var was = seen.GetValueOrDefault(issue.Id) ?? AsFiled(issue.Id);
        return issue.Comments.Count > was.Comments || (issue.Status == IssueStatus.Resolved) != was.Resolved;
    }

    /// <summary>A report reads as read at the state it was filed in.</summary>
    private static SeenIssue AsFiled(int id) => new() { Id = id, Comments = 1, Resolved = false };

    private static SeenIssue Marker(MediaIssue issue) => new()
    {
        Id = issue.Id,
        Comments = issue.Comments.Count,
        Resolved = issue.Status == IssueStatus.Resolved,
    };

    private Dictionary<int, SeenIssue>? SeenIssueMap()
    {
        if (!_seenIssuesLoaded)
        {
            _seenIssuesLoaded = true;
            if (IssueAccount()?.SeenIssues is { } stored) _seenIssues = stored.ToDictionary(s => s.Id);
        }
        return _seenIssues;
    }

    private RequestServerAccountData? IssueAccount() =>
        _library is { } lib ? _settings.RequestServerFor(lib.Server.Id) : null;

    /// <summary>Records these reports as read now. Only a reporter's own — a manager
    /// reading someone's report has nothing to mark.</summary>
    private void MarkIssuesRead(IEnumerable<MediaIssue> issues)
    {
        if (!IsReporterOnly || _requestServer is not { } server) return;
        var map = SeenIssueMap();
        bool changed = map is null;   // the first look records, even when there is nothing
        map ??= [];
        foreach (var issue in issues.Where(i => i.ReportedById == server.User.Id))
        {
            var now = Marker(issue);
            if (map.TryGetValue(issue.Id, out var old) && old.Comments == now.Comments && old.Resolved == now.Resolved)
                continue;
            map[issue.Id] = now;
            changed = true;
        }
        _seenIssues = map;
        if (!changed || IssueAccount() is not { } account) return;
        // The newest few hundred are plenty; the list is only ever asked for the latest fifty.
        account.SeenIssues = map.Values.OrderByDescending(s => s.Id).Take(200).ToList();
        _settings.Save();
    }

    /// <summary>After a look: the first one records everything as read and says nothing;
    /// later ones say what changed, once each.</summary>
    private void NoteIssueNews(IReadOnlyList<MediaIssue> issues)
    {
        if (SeenIssueMap() is null)
        {
            MarkIssuesRead(issues);
            return;
        }
        var seen = _seenIssues!;
        var news = issues.Where(HasNews)
            .Where(i => _issueNewsSaid.Add((i.Id, i.Comments.Count, i.Status == IssueStatus.Resolved)))
            // The page open on it already shows it — or will, once it is read again.
            .Where(i => !(Current.View == ApertureView.Issue && Current.Id == IssueRouteId(i)))
            .ToList();

        if (news.Count == 1)
        {
            var issue = news[0];
            var was = seen.GetValueOrDefault(issue.Id) ?? AsFiled(issue.Id);
            Flash(NewsPhrase(issue, was), ToastSeverity.Success, ("Open", () => OpenIssue(issue)));
        }
        else if (news.Count > 1)
        {
            Flash($"{news.Count} of your reports have news", ToastSeverity.Success, ("Show", () =>
            {
                SetIssueListFilter(IssueFilter.All);
                Navigate(new Route(ApertureView.Issues));
            }));
        }
    }

    /// <summary>"Your report on Stalker has an answer" — or was resolved, or both.</summary>
    internal static string NewsPhrase(MediaIssue issue, SeenIssue was)
    {
        var title = issue.Title ?? "a title";
        bool answered = issue.Comments.Count > was.Comments;
        bool resolved = issue.Status == IssueStatus.Resolved;
        return (answered, resolved != was.Resolved, resolved) switch
        {
            (true, true, true) => $"Your report on {title} was answered and resolved",
            (true, true, false) => $"Your report on {title} was answered and reopened",
            (false, true, true) => $"Your report on {title} was resolved",
            (false, true, false) => $"Your report on {title} was reopened",
            _ => $"Your report on {title} has an answer",
        };
    }

    private void ForgetMyIssues()
    {
        _myIssueList = [];
        _seenIssues = null;
        _seenIssuesLoaded = false;
        _issueNewsSaid.Clear();
    }
}
