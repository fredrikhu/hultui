namespace GitClient.Git;

/// <summary>
/// One entry from <c>git worktree list --porcelain</c>. A repository always has
/// at least the main worktree (its primary working tree); linked worktrees are
/// separate directories checking out other branches/commits of the same repo.
/// </summary>
public record GitWorktree(
    string Path,
    string Head,          // commit the worktree is at ("" for a bare main worktree)
    string? Branch,       // short branch name, or null when detached or bare
    bool IsBare,
    bool IsDetached,
    bool IsLocked,
    string? LockReason,   // git's reason text, when the lock has one
    bool IsMain,          // the primary worktree (first entry git reports)
    bool Prunable)        // git flagged it prunable (its working dir is gone)
{
    /// <summary>A short human label: the branch name, "(detached)", or "(bare)".</summary>
    public string RefLabel => IsBare ? "(bare)" : Branch ?? "(detached)";
}
