namespace GitClient.Git;

/// <summary>
/// Actions available on each commit during interactive rebase. Pick/squash/fixup/drop
/// map 1:1 to git's own todo keywords. Reword is special: git's built-in <c>reword</c>
/// opens <c>$GIT_EDITOR</c> for the new message, which we don't wire up — instead the
/// dialog collects the message up front and we emit <c>pick</c> + <c>exec git commit
/// --amend -m …</c> to rewrite the message non-interactively.
/// </summary>
public enum RebaseOp
{
    Pick,
    Reword,
    Squash,
    Fixup,
    Drop,
}

public static class RebaseOpExtensions
{
    /// <summary>The git-rebase-todo keyword for this op. Reword has no direct keyword
    /// in our todo (see <see cref="RebaseOp"/>); callers emit <c>pick</c> plus an
    /// <c>exec</c> line instead.</summary>
    public static string ToKeyword(this RebaseOp op) => op switch
    {
        RebaseOp.Pick => "pick",
        RebaseOp.Reword => "pick",
        RebaseOp.Squash => "squash",
        RebaseOp.Fixup => "fixup",
        RebaseOp.Drop => "drop",
        _ => "pick",
    };
}

/// <summary>
/// A single line in the interactive rebase plan — the commit, its short hash and
/// subject for display, and the user-selected op. Order within the list controls
/// the final commit order. <see cref="NewMessage"/> is only read when
/// <see cref="Op"/> is <see cref="RebaseOp.Reword"/>.
/// </summary>
public sealed class RebaseEntry
{
    public required string Hash { get; init; }
    public required string ShortHash { get; init; }
    public required string Subject { get; init; }
    public RebaseOp Op { get; set; } = RebaseOp.Pick;
    public string? NewMessage { get; set; }
}
