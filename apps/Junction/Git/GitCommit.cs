namespace Junction.Git;

public record GitCommit(
    string Hash,
    string ShortHash,
    string AuthorName,
    string AuthorEmail,
    DateTimeOffset AuthorDate,
    string Subject,
    string[] ParentHashes,
    string[] Refs
);
