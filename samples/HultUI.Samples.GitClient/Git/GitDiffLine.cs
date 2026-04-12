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

public record GitFileDiff(string Path, string? OldPath, List<GitDiffHunk> Hunks)
{
    /// <summary>Raw "--- a/path" header line from the diff output. Needed for patch reconstruction.</summary>
    public string? OldPathHeader { get; set; }

    /// <summary>Raw "+++ b/path" header line from the diff output. Needed for patch reconstruction.</summary>
    public string? NewPathHeader { get; set; }
}
