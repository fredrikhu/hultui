using Junction.Git;
using Xunit;

namespace Junction.Tests;

/// <summary>
/// Partial-staging patch synthesis. <c>git apply</c> rejects a patch whose source side
/// does not match the file it is applied against, and the source side flips with the
/// direction: staging applies forward onto the index, unstaging and discarding apply in
/// reverse onto the index / working tree. Getting that backwards produces a patch that
/// looks right and fails to apply, so the two directions are pinned separately.
/// </summary>
public class GitPatchBuilderTests
{
    // one          context
    // +two         addition
    // +three       addition
    // four         context
    private static (GitFileDiff File, GitDiffLine Add1, GitDiffLine Add2) TwoAdditions()
    {
        var add1 = new GitDiffLine(DiffLineKind.Addition, "two", null, 2);
        var add2 = new GitDiffLine(DiffLineKind.Addition, "three", null, 3);
        var hunk = new GitDiffHunk("@@ -1,2 +1,4 @@",
        [
            new GitDiffLine(DiffLineKind.HunkHeader, "@@ -1,2 +1,4 @@", null, null),
            new GitDiffLine(DiffLineKind.Context, "one", 1, 1),
            add1,
            add2,
            new GitDiffLine(DiffLineKind.Context, "four", 2, 4),
        ]);
        return (new GitFileDiff("a.txt", null, [hunk]), add1, add2);
    }

    // one          context
    // -two         deletion
    // -three       deletion
    // four         context
    private static (GitFileDiff File, GitDiffLine Del1, GitDiffLine Del2) TwoDeletions()
    {
        var del1 = new GitDiffLine(DiffLineKind.Deletion, "two", 2, null);
        var del2 = new GitDiffLine(DiffLineKind.Deletion, "three", 3, null);
        var hunk = new GitDiffHunk("@@ -1,4 +1,2 @@",
        [
            new GitDiffLine(DiffLineKind.HunkHeader, "@@ -1,4 +1,2 @@", null, null),
            new GitDiffLine(DiffLineKind.Context, "one", 1, 1),
            del1,
            del2,
            new GitDiffLine(DiffLineKind.Context, "four", 4, 2),
        ]);
        return (new GitFileDiff("a.txt", null, [hunk]), del1, del2);
    }

    [Fact]
    public void StagingDropsAnUnselectedAdditionBecauseTheIndexHasNotSeenIt()
    {
        var (file, add1, _) = TwoAdditions();

        var patch = GitPatchBuilder.BuildSelectionPatch(file, StageOp.Stage, new HashSet<GitDiffLine> { add1 });

        Assert.Equal(
            "diff --git a/a.txt b/a.txt\n" +
            "--- a/a.txt\n" +
            "+++ b/a.txt\n" +
            "@@ -1,2 +1,3 @@\n" +
            " one\n" +
            "+two\n" +
            " four\n",
            patch);
    }

    [Fact]
    public void UnstagingKeepsAnUnselectedAdditionAsContextBecauseTheIndexHasIt()
    {
        // Same file, same selection, opposite direction — and the unselected addition
        // has to appear, as context, or the reverse apply will not line up.
        var (file, add1, _) = TwoAdditions();

        var patch = GitPatchBuilder.BuildSelectionPatch(file, StageOp.Unstage, new HashSet<GitDiffLine> { add1 });

        Assert.Equal(
            "diff --git a/a.txt b/a.txt\n" +
            "--- a/a.txt\n" +
            "+++ b/a.txt\n" +
            "@@ -1,3 +1,4 @@\n" +
            " one\n" +
            "+two\n" +
            " three\n" +
            " four\n",
            patch);
    }

    [Fact]
    public void StagingKeepsAnUnselectedDeletionAsContextBecauseTheIndexStillHasIt()
    {
        var (file, del1, _) = TwoDeletions();

        var patch = GitPatchBuilder.BuildSelectionPatch(file, StageOp.Stage, new HashSet<GitDiffLine> { del1 });

        Assert.Equal(
            "diff --git a/a.txt b/a.txt\n" +
            "--- a/a.txt\n" +
            "+++ b/a.txt\n" +
            "@@ -1,4 +1,3 @@\n" +
            " one\n" +
            "-two\n" +
            " three\n" +
            " four\n",
            patch);
    }

    [Fact]
    public void DiscardingDropsAnUnselectedDeletionBecauseTheWorkingTreeLacksIt()
    {
        var (file, del1, _) = TwoDeletions();

        var patch = GitPatchBuilder.BuildSelectionPatch(file, StageOp.Discard, new HashSet<GitDiffLine> { del1 });

        Assert.Equal(
            "diff --git a/a.txt b/a.txt\n" +
            "--- a/a.txt\n" +
            "+++ b/a.txt\n" +
            "@@ -1,3 +1,2 @@\n" +
            " one\n" +
            "-two\n" +
            " four\n",
            patch);
    }

    [Fact]
    public void HunksWithNothingSelectedAreOmittedAndLaterHunksShiftToCompensate()
    {
        // Skipping a hunk changes how many lines precede the ones that follow. The
        // new-side start of every later hunk has to absorb that, or git apply rejects
        // the patch at the second hunk.
        var selected = new GitDiffLine(DiffLineKind.Addition, "added later", null, 21);
        var file = new GitFileDiff("a.txt", null,
        [
            new GitDiffHunk("@@ -1,2 +1,3 @@",
            [
                new GitDiffLine(DiffLineKind.HunkHeader, "@@ -1,2 +1,3 @@", null, null),
                new GitDiffLine(DiffLineKind.Context, "one", 1, 1),
                new GitDiffLine(DiffLineKind.Addition, "skipped", null, 2),
                new GitDiffLine(DiffLineKind.Context, "two", 2, 3),
            ]),
            new GitDiffHunk("@@ -20,1 +21,2 @@",
            [
                new GitDiffLine(DiffLineKind.HunkHeader, "@@ -20,1 +21,2 @@", null, null),
                selected,
                new GitDiffLine(DiffLineKind.Context, "tail", 20, 22),
            ]),
        ]);

        var patch = GitPatchBuilder.BuildSelectionPatch(file, StageOp.Stage, new HashSet<GitDiffLine> { selected })!;

        Assert.DoesNotContain("skipped", patch);
        // The first hunk contributed nothing, so the second one's new-side start is
        // still the old-side start — not the 21 the original header carried.
        Assert.Contains("@@ -20,1 +20,2 @@", patch);
    }

    [Fact]
    public void RunningDeltaAccumulatesAcrossKeptHunks()
    {
        var first = new GitDiffLine(DiffLineKind.Addition, "first", null, 2);
        var second = new GitDiffLine(DiffLineKind.Addition, "second", null, 21);
        var file = new GitFileDiff("a.txt", null,
        [
            new GitDiffHunk("@@ -1,1 +1,2 @@",
            [
                new GitDiffLine(DiffLineKind.HunkHeader, "@@ -1,1 +1,2 @@", null, null),
                new GitDiffLine(DiffLineKind.Context, "one", 1, 1),
                first,
            ]),
            new GitDiffHunk("@@ -20,1 +21,2 @@",
            [
                new GitDiffLine(DiffLineKind.HunkHeader, "@@ -20,1 +21,2 @@", null, null),
                new GitDiffLine(DiffLineKind.Context, "tail", 20, 22),
                second,
            ]),
        ]);

        var patch = GitPatchBuilder.BuildSelectionPatch(file, StageOp.Stage,
            new HashSet<GitDiffLine> { first, second })!;

        Assert.Contains("@@ -1,1 +1,2 @@", patch);
        // One line added by the first hunk pushes the second hunk's new side down by one.
        Assert.Contains("@@ -20,1 +21,2 @@", patch);
    }

    [Fact]
    public void ASelectionThatMatchesNoHunkProducesNoPatch()
    {
        var (file, _, _) = TwoAdditions();

        Assert.Null(GitPatchBuilder.BuildSelectionPatch(file, StageOp.Stage, new HashSet<GitDiffLine>()));
    }

    [Fact]
    public void TheNoNewlineMarkerIsReproduced()
    {
        // Dropping the marker turns "no trailing newline" into "trailing newline", which
        // git applies as a real one-character content change to the last line.
        var add = new GitDiffLine(DiffLineKind.Addition, "last", null, 2, NoNewlineAfter: true);
        var file = new GitFileDiff("a.txt", null,
        [
            new GitDiffHunk("@@ -1,1 +1,2 @@",
            [
                new GitDiffLine(DiffLineKind.HunkHeader, "@@ -1,1 +1,2 @@", null, null),
                new GitDiffLine(DiffLineKind.Context, "one", 1, 1),
                add,
            ]),
        ]);

        var patch = GitPatchBuilder.BuildSelectionPatch(file, StageOp.Stage, new HashSet<GitDiffLine> { add })!;

        Assert.Contains("+last\n\\ No newline at end of file\n", patch);
    }

    [Fact]
    public void RenameHeadersFromTheOriginalDiffAreCarriedThrough()
    {
        // A rename's --- / +++ lines name different paths. Synthesising them from
        // file.Path would name the new path on both sides and the apply would fail.
        var (file, add1, _) = TwoAdditions();
        file.OldPathHeader = "--- a/old-name.txt";
        file.NewPathHeader = "+++ b/a.txt";

        var patch = GitPatchBuilder.BuildSelectionPatch(file, StageOp.Stage, new HashSet<GitDiffLine> { add1 })!;

        Assert.Contains("--- a/old-name.txt\n+++ b/a.txt\n", patch);
    }
}
