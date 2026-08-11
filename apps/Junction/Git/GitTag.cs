namespace Junction.Git;

/// <summary>
/// One entry under <c>refs/tags</c>.
/// <para>Git has two kinds. A lightweight tag is a ref pointing straight at a commit; an
/// annotated tag is its own object, with a tagger, a date and a message, that in turn points
/// at the commit. <paramref name="Hash"/> is always the commit either way — the tag object's
/// own sha is of no use to anyone reading a list — and <paramref name="Message"/> is empty
/// for a lightweight tag, which has nowhere to keep one.</para>
/// </summary>
/// <param name="Date">Tagger date for an annotated tag, committer date for a lightweight
/// one: in both cases when the tag came to point where it points.</param>
public record GitTag(
    string Name,
    string Hash,
    string Subject = "",
    DateTimeOffset Date = default,
    bool Annotated = false,
    string Message = ""
);
