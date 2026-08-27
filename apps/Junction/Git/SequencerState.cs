namespace Junction.Git;

/// <summary>
/// The half-finished git operation a repository is sitting in, if any.
///
/// <para>Git calls these the "sequencer" operations: four commands that replay someone
/// else's work onto yours, stop on the first conflict, and wait. They differ in the
/// command that resumes them and in what the two sides of a conflict mean — nothing
/// else — so the resolution UI is written once against this rather than four times.</para>
/// </summary>
public enum SequencerOp
{
    None,
    Merge,
    Rebase,
    CherryPick,
    Revert,
}

/// <summary>
/// An in-progress <see cref="SequencerOp"/> and what its two conflict sides are.
///
/// <para><see cref="OursRef"/> and <see cref="TheirsRef"/> are the commits behind index
/// stages 2 and 3 — which is to say, exactly what <c>git checkout --ours/--theirs</c>
/// would give you. During a merge that reads the obvious way round (ours = the branch
/// you are on). During a <b>rebase it is inverted</b>: your commits are the ones being
/// replayed, so they arrive as "theirs", and "ours" is the branch you are rebasing onto.
/// Getting this backwards silently discards work, so the labels are carried here rather
/// than hard-coded next to the buttons.</para>
/// </summary>
/// <param name="Op">Which operation is in progress.</param>
/// <param name="OursRef">Rev for stage 2, or null when it cannot be resolved.</param>
/// <param name="TheirsRef">Rev for stage 3, or null when it cannot be resolved.</param>
/// <param name="OursLabel">Human name for the "ours" side (a branch, or a description).</param>
/// <param name="TheirsLabel">Human name for the "theirs" side.</param>
/// <param name="Branch">The branch the operation is being applied to, when it has a name.</param>
/// <param name="Step">1-based position in a multi-commit replay; 0 when unknown.</param>
/// <param name="Total">How many commits the replay covers; 0 when unknown.</param>
public sealed record SequencerState(
    SequencerOp Op,
    string? OursRef,
    string? TheirsRef,
    string OursLabel,
    string TheirsLabel,
    string? Branch,
    int Step,
    int Total)
{
    /// <summary>The operation's name as the user would say it ("Rebase", "Cherry-pick").</summary>
    public string Name => Op switch
    {
        SequencerOp.Merge => "Merge",
        SequencerOp.Rebase => "Rebase",
        SequencerOp.CherryPick => "Cherry-pick",
        SequencerOp.Revert => "Revert",
        _ => "",
    };

    /// <summary>Whether git can drop the current commit and carry on. A merge has only the
    /// one commit to apply, so there is nothing to skip past.</summary>
    public bool CanSkip => Op is SequencerOp.Rebase or SequencerOp.CherryPick or SequencerOp.Revert;

    /// <summary>"3 of 7" while replaying a run of commits, else empty.</summary>
    public string Progress => Total > 0 && Step > 0 ? $"{Step} of {Total}" : "";
}
