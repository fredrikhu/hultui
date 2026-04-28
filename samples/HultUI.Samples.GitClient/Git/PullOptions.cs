using GitClient;

namespace GitClient.Git;

public sealed class PullOptions
{
    public PullStrategy Strategy { get; init; } = PullStrategy.Rebase;
    public bool AutoStash { get; init; } = true;
    public bool Prune { get; init; } = true;
}
