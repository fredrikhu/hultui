namespace GitClient.Git;

public record GitSubmodule(
    string Path,
    string Hash,
    string? Describe,
    bool Dirty,
    bool Uninitialized
);
