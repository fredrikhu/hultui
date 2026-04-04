namespace GitClient.Git;

public record GitBranch(
    string Name,
    string FullName,
    bool IsCurrent,
    bool IsRemote,
    string? TrackingBranch,
    int AheadCount,
    int BehindCount
);
