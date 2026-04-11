namespace GitClient.Git;

/// <summary>
/// A single stash entry. <see cref="Ref"/> is the addressable form
/// (e.g. "stash@{0}") used for apply / pop / drop. <see cref="Subject"/>
/// is what the user typed (or git's WIP default).
/// </summary>
public record GitStash(string Ref, string Branch, string Subject);
