using HultUI.Core.Builders;
using HultUI.Core.Elements;
using HultUI.Core.Styling;
using Junction.Git;
using Junction.Theme;

namespace Junction;

/// <summary>
/// Issue-tracker links: the repository's <c>.issuetracker</c> rules, and turning the
/// references a commit message makes into things you can click. Shared by the History list
/// (subjects) and the Commit tab (the whole message).
/// </summary>
public partial class JunctionApp
{
    private (string Repo, DateTime Stamp, string Host, IReadOnlyList<IssueTrackerRule> Rules) _issueRules = ("", default, "", []);

    // Whether this build has looked at the rules file yet. Build clears it; the first asker
    // looks, and the rest of the build — and the rows a list realizes until the next one —
    // take the answer. Looking is two calls to the file system, 26 µs, and it was made for
    // every rendered line: 18 History rows on each move of the selection, most of the cost
    // of building them.
    private bool _issueRulesLooked;

    /// <summary>How many times the rules were asked for, and how many of those looked at the
    /// file, for tests.</summary>
    internal int IssueRulesAsks { get; private set; }
    internal int IssueRulesLooks { get; private set; }

    /// <summary>
    /// The active repository's rules, re-read when the file changes. Cached because it is
    /// asked once per rendered line — every visible History row, every line of a message —
    /// and the answer costs a subprocess. Whether the file changed is asked once a build: a
    /// change is seen by the next one, and the repository watcher rebuilds on any change in
    /// the working tree.
    /// <para>A repository on GitLab gets two rules of its own after the file's: <c>#123</c>
    /// is an issue and <c>!123</c> a merge request on that project. The file's rules come
    /// first so a project that maps <c>#</c> elsewhere keeps its say.</para>
    /// </summary>
    private IReadOnlyList<IssueTrackerRule> IssueRules()
    {
        if (!HasActiveRepo) return [];
        IssueRulesAsks++;
        if (_issueRulesLooked && _issueRules.Repo == _activeRepoPath) return _issueRules.Rules;
        _issueRulesLooked = true;
        IssueRulesLooks++;
        var file = System.IO.Path.Combine(_activeRepoPath, IssueTrackerFile.FileName);
        var stamp = System.IO.File.Exists(file) ? System.IO.File.GetLastWriteTimeUtc(file) : default;
        var remote = GitLabRemoteFor(_activeRepoPath);
        var hostKey = remote is null ? "" : remote.WebBaseUrl + "/" + remote.ProjectPath;
        if (_issueRules.Repo != _activeRepoPath || _issueRules.Stamp != stamp || _issueRules.Host != hostKey)
        {
            var rules = new List<IssueTrackerRule>(IssueTrackerFile.Load(_activeRepoPath));
            if (remote is not null)
            {
                var project = $"{remote.WebBaseUrl}/{remote.ProjectPath}";
                rules.Add(new IssueTrackerRule("GitLab issue",
                    new System.Text.RegularExpressions.Regex(@"(?<![\w!])#(\d+)\b", System.Text.RegularExpressions.RegexOptions.None, TimeSpan.FromMilliseconds(100)),
                    project + "/-/issues/$1"));
                rules.Add(new IssueTrackerRule("GitLab merge request",
                    new System.Text.RegularExpressions.Regex(@"(?<!\w)!(\d+)\b", System.Text.RegularExpressions.RegexOptions.None, TimeSpan.FromMilliseconds(100)),
                    project + "/-/merge_requests/$1"));
            }
            _issueRules = (_activeRepoPath, stamp, hostKey, rules);
        }
        return _issueRules.Rules;
    }

    /// <summary>
    /// Marks up the issue references in <paramref name="text"/> on a label already showing
    /// it, and hands the label back so the call can be chained. A label with no references —
    /// which is most of them — is returned untouched, so the ranges cost nothing where there
    /// are none.
    /// </summary>
    private TextElement IssueLinked(TextElement label, string text)
    {
        var refs = IssueTrackerFile.Find(text, IssueRules());
        if (refs.Count == 0) return label;

        // Underlined and in the accent, so a link reads as one before it is hovered — inside
        // a run of text there is nothing else to discover it by.
        label.Spans([.. refs.Select(r => new HultUI.Core.Rendering.TextSpan(
            r.Start, r.Length, Nocturne.Accent300, Decoration: TextDecoration.Underline))]);
        label.Links([.. refs.Select(r => new TextLink(r.Start, r.Length, r.Url))], OpenIssue);
        label.Tooltip(refs.Count == 1 ? $"{refs[0].Label}: {refs[0].Url}" : "Issue links");
        return label;
    }

    private void OpenIssue(string url)
    {
        if (!Platform.ShellLauncher.Open(url, out var error))
            Flash($"Could not open {url}: {error}");
    }
}
