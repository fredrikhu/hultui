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
    private (string Repo, DateTime Stamp, IReadOnlyList<IssueTrackerRule> Rules) _issueRules = ("", default, []);

    /// <summary>
    /// The active repository's rules, re-read when the file changes. Cached because it is
    /// asked once per rendered line — every visible History row, every line of a message —
    /// and the answer costs a subprocess.
    /// </summary>
    private IReadOnlyList<IssueTrackerRule> IssueRules()
    {
        if (!HasActiveRepo) return [];
        var file = System.IO.Path.Combine(_activeRepoPath, IssueTrackerFile.FileName);
        var stamp = System.IO.File.Exists(file) ? System.IO.File.GetLastWriteTimeUtc(file) : default;
        if (_issueRules.Repo != _activeRepoPath || _issueRules.Stamp != stamp)
            _issueRules = (_activeRepoPath, stamp, IssueTrackerFile.Load(_activeRepoPath));
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
