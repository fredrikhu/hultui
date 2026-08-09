namespace Junction.Git;

/// <summary>
/// One entry from <c>git reflog</c> — a point HEAD used to be. The reflog is the
/// recovery net: after a bad reset/rebase/checkout the "lost" commit still has a
/// reflog entry, so branching or resetting back to <see cref="Hash"/> restores it.
/// </summary>
public record GitReflogEntry(
    string Hash,
    string ShortHash,
    string Selector,   // e.g. "HEAD@{2}"
    string Action,     // reflog subject: "reset: moving to HEAD~1", "commit: fix bug", "checkout: …"
    DateTimeOffset Date,
    string Subject)    // the commit's own subject at that point
{
    /// <summary>The operation verb ("reset", "commit", "rebase", "checkout", …),
    /// parsed from the reflog subject's leading token — drives the row's chip.</summary>
    public string Verb
    {
        get
        {
            var colon = Action.IndexOf(':');
            var head = colon > 0 ? Action[..colon] : Action;
            var space = head.IndexOf(' ');
            return (space > 0 ? head[..space] : head).Trim();
        }
    }
}
