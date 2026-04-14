namespace GitClient.Git;

/// <summary>
/// Actions available on each commit during interactive rebase. Maps 1:1 to the
/// keywords git writes into <c>.git/rebase-merge/git-rebase-todo</c>. Reword and
/// edit are omitted from this first pass because they require mid-rebase editor
/// interception; squash/fixup accept the default combined message.
/// </summary>
public enum RebaseOp
{
    Pick,
    Squash,
    Fixup,
    Drop,
}

public static class RebaseOpExtensions
{
    /// <summary>The git-rebase-todo keyword for this op.</summary>
    public static string ToKeyword(this RebaseOp op) => op switch
    {
        RebaseOp.Pick => "pick",
        RebaseOp.Squash => "squash",
        RebaseOp.Fixup => "fixup",
        RebaseOp.Drop => "drop",
        _ => "pick",
    };
}

/// <summary>
/// A single line in the interactive rebase plan — the commit, its short hash and
/// subject for display, and the user-selected op. Order within the list controls
/// the final commit order.
/// </summary>
public sealed class RebaseEntry
{
    public required string Hash { get; init; }
    public required string ShortHash { get; init; }
    public required string Subject { get; init; }
    public RebaseOp Op { get; set; } = RebaseOp.Pick;
}
