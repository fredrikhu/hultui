namespace Junction.Git;

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

/// <summary>
/// What git said about a file it would not diff textually. Instead of hunks it emits a
/// single "Binary files a/x and b/x differ" line, so there is no content to show and
/// none is invented: the blob hashes come from the same file's <c>index</c> header and
/// the sizes are looked up separately.
/// </summary>
/// <param name="Kind">Add / delete / rename, read from the file's mode headers.</param>
/// <param name="OldBlob">Abbreviated blob hash before the change, or null if absent there.</param>
/// <param name="NewBlob">Abbreviated blob hash after the change, or null if absent there.</param>
public record GitBinaryDiff(FileChangeKind Kind, string? OldBlob, string? NewBlob);

public record GitFileDiff(string Path, string? OldPath, List<GitDiffHunk> Hunks)
{
    /// <summary>Non-null when git reported this file as binary rather than emitting hunks.
    /// The viewer shows a summary card for these and never tokenizes them.</summary>
    public GitBinaryDiff? Binary { get; set; }

    /// <summary>True when there is no textual diff to render — see <see cref="Binary"/>.</summary>
    public bool IsBinary => Binary != null;

    /// <summary>Raw "--- a/path" header line from the diff output. Needed for patch reconstruction.</summary>
    public string? OldPathHeader { get; set; }

    /// <summary>Raw "+++ b/path" header line from the diff output. Needed for patch reconstruction.</summary>
    public string? NewPathHeader { get; set; }
}
