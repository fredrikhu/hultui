using System.Text;
using HultUI.Core.Elements;
using Junction.Git;
using Junction.GitLab;

namespace Junction;

/// <summary>
/// Branch from issue — design §I's second half. GitLab has no API for it; the web UI names
/// the branch from the project's template, <c>%{id}-%{title}</c> slugified, and starts it
/// at the default branch. This does the same locally: pick an open issue, get
/// <c>1042-split-refunds-across-original-tenders</c> checked out from the remote's default
/// branch, and the merge request the branch is pushed into later pre-fills
/// <c>Closes #1042</c> from the leaf (see JunctionApp.CreateMergeRequest).
/// </summary>
public partial class JunctionApp
{
    private const int IssueSlugMaxLength = 60;

    /// <summary>Set when the picker was asked for before the issues had loaded; the build
    /// after they land opens it (see <see cref="ServiceBranchFromIssue"/>).</summary>
    private bool _pendingIssuePicker;

    /// <summary>The picker: the project's open issues as a popover under the toolbar.</summary>
    internal void OpenBranchFromIssue()
    {
        if (!GitLabActive(_activeRepoPath)) { Flash("This repository is not on a GitLab with an account"); return; }
        var issues = IssuesFor(_activeRepoPath);
        var open = issues.Value.Where(i => i.State == "opened").Take(20).ToList();
        if (open.Count == 0)
        {
            if (issues.Loaded) { Flash("No open issues on this project"); return; }
            // A first look activates the loader; the picker opens when the issues land.
            Flash("Loading the project's issues…");
            _pendingIssuePicker = true;
            Rerender();
            return;
        }
        _pendingIssuePicker = false;
        var items = new List<ContextMenuItem>();
        foreach (var issue in open)
        {
            var i = issue;
            items.Add(Item(Icons.GitBranch, $"#{i.Iid}  {i.Title}", () => CreateBranchFromIssue(i.Iid)));
        }
        _popoverX = 300f;
        _popoverY = 96f;
        _popover = items;
        MarkDirty();
    }

    /// <summary>After a build: opens the picker that was asked for while the issues were
    /// still loading, once they have.</summary>
    private void ServiceBranchFromIssue()
    {
        if (!_pendingIssuePicker || !HasActiveRepo) return;
        var issues = IssuesFor(_activeRepoPath);
        if (!issues.Loaded && issues.Failure is null) return;
        _pendingIssuePicker = false;
        if (issues.Failure is { } failure) { Flash("Could not load the issues: " + failure.UserMessage(GitLabRemoteFor(_activeRepoPath)?.Host ?? "GitLab", "Project")); return; }
        _window.RequestPostFrameCallback(OpenBranchFromIssue);
    }

    /// <summary>Creates and checks out <c>&lt;iid&gt;-&lt;slug&gt;</c> from the default
    /// branch — the remote's copy when the clone has one, since that is what the merge
    /// request will target.</summary>
    internal void CreateBranchFromIssue(long iid)
    {
        if (ActiveRepo is not { } repo) return;
        var issue = IssuesFor(_activeRepoPath).Value.FirstOrDefault(i => i.Iid == iid);
        if (issue is null) { Flash($"Issue #{iid} is not among the loaded issues"); return; }
        var name = BranchNameForIssue(issue.Iid, issue.Title);

        // Read the loaders here, on the UI thread that owns them; the background half below
        // must touch nothing but git.
        var defaultBranch = ProjectFor(_activeRepoPath).Value?.Project.DefaultBranch ?? "main";
        var remoteRef = $"{HostingRemoteName(_activeRepoPath)}/{defaultBranch}";

        // Four git commands at worst, one after another. Run inline they were four process
        // launches deep in a click handler, and the window painted nothing until the last
        // one came back.
        RunGitCommand(
            () =>
            {
                if (repo.LocalBranchExists(name))
                    return new BranchFromIssueOutcome(Existed: true, Created: false, "", repo.Checkout(name));

                var start = repo.GetCommit(remoteRef) is not null ? remoteRef : defaultBranch;
                var created = repo.CreateBranch(name, start);
                return created.Success
                    ? new BranchFromIssueOutcome(false, Created: true, start, repo.Checkout(name))
                    : new BranchFromIssueOutcome(false, Created: false, start, created);
            },
            o =>
            {
                if (o.Existed)
                    Flash(o.Result.Success
                        ? $"Checked out {name}, which already existed"
                        : FirstLine(o.Result.Stderr, "Checkout failed"));
                else if (!o.Created)
                {
                    // Nothing changed, so nothing to reload — as before.
                    Flash(FirstLine(o.Result.Stderr, "Create branch failed"));
                    return;
                }
                else
                    Flash(o.Result.Success
                        ? $"Created {name} from {o.Start}"
                        : FirstLine(o.Result.Stderr, $"Created {name}, checkout failed"));
                RefreshAll();
            });
    }

    /// <summary>Which of the three paths through <see cref="CreateBranchFromIssue"/>'s git
    /// work was taken, and how it ended — so the main thread can report it without asking
    /// git anything further.</summary>
    private readonly record struct BranchFromIssueOutcome(
        bool Existed, bool Created, string Start, GitResult Result);

    /// <summary>GitLab's default branch-name template, <c>%{id}-%{title}</c>: the title
    /// lowercased, runs of anything but letters and digits as one dash, trimmed, cut to
    /// sixty characters at a dash.</summary>
    internal static string BranchNameForIssue(long iid, string title)
    {
        var sb = new StringBuilder();
        bool dash = false;
        foreach (var ch in title.ToLowerInvariant())
        {
            if (char.IsAsciiLetterOrDigit(ch)) { sb.Append(ch); dash = false; }
            else if (!dash && sb.Length > 0) { sb.Append('-'); dash = true; }
        }
        var slug = sb.ToString().TrimEnd('-');
        if (slug.Length > IssueSlugMaxLength)
        {
            slug = slug[..IssueSlugMaxLength];
            int cut = slug.LastIndexOf('-');
            if (cut > 0) slug = slug[..cut];
        }
        return slug.Length == 0 ? iid.ToString() : $"{iid}-{slug}";
    }
}
