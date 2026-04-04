namespace GitClient.Git;

public enum DiffLineKind
{
    Context,
    Addition,
    Deletion,
    HunkHeader,
    FileHeader,
}

public record GitDiffLine(DiffLineKind Kind, string Content, int? OldLineNumber, int? NewLineNumber);

public record GitDiffHunk(string Header, List<GitDiffLine> Lines);

public record GitFileDiff(string Path, string? OldPath, List<GitDiffHunk> Hunks);
