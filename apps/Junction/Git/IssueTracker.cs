using System.Text.RegularExpressions;

namespace Junction.Git;

/// <summary>One rule from a repository's <c>.issuetracker</c>: a pattern that recognises an
/// issue reference in a commit message, and the URL that reference points at.</summary>
/// <param name="Label">The section name, shown as the link's tooltip so several trackers in
/// one repository can be told apart.</param>
/// <param name="Pattern">Compiled from the rule's <c>regex</c>.</param>
/// <param name="UrlTemplate">The rule's <c>url</c>, with <c>$1</c>…<c>$9</c> standing for the
/// pattern's capture groups.</param>
public sealed record IssueTrackerRule(string Label, Regex Pattern, string UrlTemplate)
{
    /// <summary>The URL a particular match points at, with its capture groups filled in.</summary>
    public string UrlFor(Match match) => match.Result(UrlTemplate);
}

/// <summary>An issue reference found in a piece of text.</summary>
public readonly record struct IssueReference(int Start, int Length, string Url, string Label)
{
    public int End => Start + Length;
}

/// <summary>
/// Reads a repository's <c>.issuetracker</c> — the convention Fork established — and finds
/// the issue references a commit message mentions.
///
/// <para>The file is git-config syntax, one section per tracker:</para>
/// <code>
/// [issuetracker "Jira"]
///     regex = "ABCD-(\\d+)"
///     url = "https://example.atlassian.net/browse/ABCD-$1"
/// </code>
/// </summary>
/// <remarks>
/// Parsed by <c>git config -f</c> rather than by hand. The format's escaping is git's, not
/// INI's in general — <c>"ABCD-(\\d+)"</c> has to arrive as <c>ABCD-(\d+)</c>, and a regex
/// mangled by a home-grown unescaper is a rule that silently matches nothing. Git already
/// reads this exactly right, and Junction is already talking to git.
/// </remarks>
public static class IssueTrackerFile
{
    public const string FileName = ".issuetracker";

    /// <summary>Ceiling on one pattern's work. The patterns come from a file in the
    /// repository, so a pathological one is a thing a clone can carry — it should cost a
    /// link, not the frame.</summary>
    private static readonly TimeSpan MatchTimeout = TimeSpan.FromMilliseconds(100);

    /// <summary>
    /// The rules in <paramref name="repoPath"/>'s <c>.issuetracker</c>, or an empty list when
    /// the file is absent, unreadable, or has no usable rule. A rule missing either key, or
    /// carrying a regex git accepts but .NET cannot compile, is skipped — one bad section
    /// does not cost the others.
    /// </summary>
    public static IReadOnlyList<IssueTrackerRule> Load(string repoPath)
    {
        if (string.IsNullOrEmpty(repoPath)) return [];
        var file = System.IO.Path.Combine(repoPath, FileName);
        if (!System.IO.File.Exists(file)) return [];

        // -z: NUL between entries, newline between key and value. Values may contain
        // anything, so this is the only listing format that cannot be mis-split.
        var result = GitProcess.Run(repoPath, "config", "-f", file, "--list", "-z");
        if (!result.Success) return [];

        var byLabel = new Dictionary<string, (string? Regex, string? Url)>(StringComparer.Ordinal);
        var order = new List<string>();
        foreach (var entry in result.Stdout.Split('\0', StringSplitOptions.RemoveEmptyEntries))
        {
            int split = entry.IndexOf('\n');
            if (split < 0) continue;                       // a valueless key ("[section] key")
            var key = entry[..split];
            var value = entry[(split + 1)..];

            // issuetracker.<label>.<field> — the label is whatever is between, dots and all.
            if (!key.StartsWith("issuetracker.", StringComparison.Ordinal)) continue;
            int lastDot = key.LastIndexOf('.');
            if (lastDot <= "issuetracker".Length) continue;
            var label = key["issuetracker.".Length..lastDot];
            var field = key[(lastDot + 1)..];

            if (!byLabel.TryGetValue(label, out var pair)) order.Add(label);
            byLabel[label] = field switch
            {
                "regex" => (value, pair.Url),
                "url" => (pair.Regex, value),
                _ => pair,
            };
        }

        var rules = new List<IssueTrackerRule>();
        foreach (var label in order)
        {
            var (pattern, url) = byLabel[label];
            if (string.IsNullOrEmpty(pattern) || string.IsNullOrEmpty(url)) continue;
            try
            {
                rules.Add(new IssueTrackerRule(label, new Regex(pattern, RegexOptions.None, MatchTimeout), url));
            }
            catch (ArgumentException)
            {
                // A pattern .NET will not compile. Skipping is the only sane answer: the file
                // belongs to the repository, and one project's syntax is not Junction's bug.
            }
        }
        return rules;
    }

    /// <summary>
    /// Every issue reference in <paramref name="text"/>, in the order they appear. Where two
    /// rules claim overlapping text the earlier rule in the file wins, so a repository can
    /// put its more specific tracker first and have it take precedence.
    /// </summary>
    public static IReadOnlyList<IssueReference> Find(string text, IReadOnlyList<IssueTrackerRule> rules)
    {
        if (string.IsNullOrEmpty(text) || rules.Count == 0) return [];

        var found = new List<IssueReference>();
        foreach (var rule in rules)
        {
            try
            {
                foreach (Match m in rule.Pattern.Matches(text))
                {
                    if (m.Length == 0) continue;
                    if (found.Any(f => m.Index < f.End && f.Start < m.Index + m.Length)) continue;
                    found.Add(new IssueReference(m.Index, m.Length, rule.UrlFor(m), rule.Label));
                }
            }
            catch (RegexMatchTimeoutException)
            {
                // This rule gives up on this message; the rest still get their links.
            }
        }
        found.Sort((a, b) => a.Start.CompareTo(b.Start));
        return found;
    }
}
