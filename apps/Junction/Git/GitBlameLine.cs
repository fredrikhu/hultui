namespace Junction.Git;

public record GitBlameLine(
    string CommitHash,
    string ShortHash,
    string AuthorName,
    DateTimeOffset AuthorDate,
    string Summary,
    int LineNumber,
    string Content
);
