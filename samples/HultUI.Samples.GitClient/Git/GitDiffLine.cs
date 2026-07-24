namespace GitClient.Git;

public enum DiffLineKind
{
    Context,
    Addition,
    Deletion,
    HunkHeader,
    FileHeader,
}

/// <param name="NoNewlineAfter">True when git emitted "\ No newline at end of file"
/// right after this line — i.e. this line is the last on its side and its file has no
/// trailing newline. Preserved so partial-staging patch synthesis can reproduce it.</param>
public record GitDiffLine(DiffLineKind Kind, string Content, int? OldLineNumber, int? NewLineNumber,
    bool NoNewlineAfter = false);

public record GitDiffHunk(string Header, List<GitDiffLine> Lines);

public record GitFileDiff(string Path, string? OldPath, List<GitDiffHunk> Hunks)
{
    /// <summary>Raw "--- a/path" header line from the diff output. Needed for patch reconstruction.</summary>
    public string? OldPathHeader { get; set; }

    /// <summary>Raw "+++ b/path" header line from the diff output. Needed for patch reconstruction.</summary>
    public string? NewPathHeader { get; set; }
}
