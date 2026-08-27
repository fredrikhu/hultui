namespace Junction.Git;

/// <summary>
/// What a push is about to do, decided before it runs.
///
/// <para>A branch that exists only locally has no upstream, and <c>git push</c> refuses
/// it with a fatal error and a command to run by hand. Junction publishes it instead:
/// there is no case where someone pressed Push and wanted nothing to happen. Separating
/// the decision from the act keeps the git-config lookups off the UI thread and lets the
/// toast say which of the two things happened.</para>
/// </summary>
/// <param name="Remote">Where to publish, when <paramref name="SetUpstream"/>. Null for
/// an ordinary push, which follows the upstream the branch already has.</param>
/// <param name="Branch">The branch being pushed.</param>
/// <param name="SetUpstream">True when the branch has no upstream and this push creates one.</param>
/// <param name="Problem">Why the push cannot happen at all (detached HEAD, no remotes);
/// null when it can.</param>
public sealed record PushPlan(string? Remote, string Branch, bool SetUpstream, string? Problem);
