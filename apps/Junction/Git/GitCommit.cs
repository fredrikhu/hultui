namespace Junction.Git;

public record GitCommit(
    string Hash,
    string ShortHash,
    string AuthorName,
    string AuthorEmail,
    DateTimeOffset AuthorDate,
    string Subject,
    string[] ParentHashes,
    string[] Refs,
    /// <summary>The full commit message — subject, blank line and body — as git stores it.
    /// Only the single-commit lookup fills this in; the log listing leaves it empty, because
    /// a body per row is a lot of text to carry for a column that shows one line of it.</summary>
    string Message = ""
);
