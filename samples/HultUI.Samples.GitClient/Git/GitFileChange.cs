namespace GitClient.Git;

public enum FileChangeKind
{
    Added,
    Modified,
    Deleted,
    Renamed,
    Copied,
    Untracked,
    Conflicted,
}

public record GitFileChange(
    string Path,
    string? OldPath,
    FileChangeKind Kind,
    int Additions,
    int Deletions
);
