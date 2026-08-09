using Junction.Git;
using Xunit;

namespace Junction.Tests;

/// <summary>
/// Turning git's unified-diff output into the model the viewer and the staging code
/// share. Line numbers computed here are what partial staging later writes into hunk
/// headers, so an off-by-one is not a display bug — it is a patch git refuses.
/// </summary>
public class DiffParsingTests
{
    private const string OneFile =
        "diff --git a/src/app.cs b/src/app.cs\n" +
        "index 1111111..2222222 100644\n" +
        "--- a/src/app.cs\n" +
        "+++ b/src/app.cs\n" +
        "@@ -10,4 +10,5 @@ void Main()\n" +
        " keep one\n" +
        "-drop me\n" +
        "+add me\n" +
        "+add me too\n" +
        " keep two\n";

    [Fact]
    public void AFileHeaderStartsANewFileAndKeepsBothPathLines()
    {
        var file = Assert.Single(GitRepository.ParseUnifiedDiff(OneFile));

        Assert.Equal("src/app.cs", file.Path);
        // Kept verbatim: patch synthesis re-emits them, and a rename's two sides differ.
        Assert.Equal("--- a/src/app.cs", file.OldPathHeader);
        Assert.Equal("+++ b/src/app.cs", file.NewPathHeader);
    }

    [Fact]
    public void LineNumbersAdvancePerSide()
    {
        var hunk = Assert.Single(Assert.Single(GitRepository.ParseUnifiedDiff(OneFile)).Hunks);

        // The header line is kept in the list, so the body starts at index 1.
        var body = hunk.Lines.Skip(1).ToList();

        Assert.Equal(DiffLineKind.Context, body[0].Kind);
        Assert.Equal(10, body[0].OldLineNumber);
        Assert.Equal(10, body[0].NewLineNumber);

        // A deletion advances only the old side …
        Assert.Equal(DiffLineKind.Deletion, body[1].Kind);
        Assert.Equal(11, body[1].OldLineNumber);
        Assert.Null(body[1].NewLineNumber);

        // … and an addition only the new side.
        Assert.Equal(DiffLineKind.Addition, body[2].Kind);
        Assert.Null(body[2].OldLineNumber);
        Assert.Equal(11, body[2].NewLineNumber);
        Assert.Equal(12, body[3].NewLineNumber);

        // The trailing context has absorbed one deletion and two additions.
        Assert.Equal(12, body[4].OldLineNumber);
        Assert.Equal(13, body[4].NewLineNumber);
    }

    [Fact]
    public void TheTrailingNewlineDoesNotBecomeAPhantomContextLine()
    {
        // Splitting on '\n' leaves an empty final element. Treating it as context added
        // a blank row that was invisible in the viewer and fatal to patch synthesis —
        // it inflated every last hunk's line counts by one.
        var hunk = Assert.Single(Assert.Single(GitRepository.ParseUnifiedDiff(OneFile)).Hunks);

        Assert.Equal(6, hunk.Lines.Count);   // header + 5 body lines, nothing more
        Assert.Equal("keep two", hunk.Lines[^1].Content);
    }

    [Fact]
    public void ABlankContextLineIsKept()
    {
        // A blank context line is a single space, not an empty string — the distinction
        // the phantom-row fix turns on. Dropping it would shift every line after it.
        var diff =
            "diff --git a/a b/a\n" +
            "--- a/a\n" +
            "+++ b/a\n" +
            "@@ -1,3 +1,3 @@\n" +
            " one\n" +
            " \n" +
            "-two\n" +
            "+TWO\n";

        var hunk = Assert.Single(Assert.Single(GitRepository.ParseUnifiedDiff(diff)).Hunks);
        var blank = hunk.Lines[2];

        Assert.Equal(DiffLineKind.Context, blank.Kind);
        Assert.Equal("", blank.Content);
        Assert.Equal(2, blank.OldLineNumber);
    }

    [Fact]
    public void TheNoNewlineMarkerAttachesToTheLineBeforeIt()
    {
        var diff =
            "diff --git a/a b/a\n" +
            "--- a/a\n" +
            "+++ b/a\n" +
            "@@ -1,1 +1,1 @@\n" +
            "-old\n" +
            "\\ No newline at end of file\n" +
            "+new\n" +
            "\\ No newline at end of file\n";

        var hunk = Assert.Single(Assert.Single(GitRepository.ParseUnifiedDiff(diff)).Hunks);

        // The marker is a property of its neighbour, not a line of its own.
        Assert.Equal(3, hunk.Lines.Count);
        Assert.True(hunk.Lines[1].NoNewlineAfter);
        Assert.True(hunk.Lines[2].NoNewlineAfter);
    }

    [Fact]
    public void MultipleFilesAndHunksAreSeparated()
    {
        var diff = OneFile +
            "diff --git a/b.txt b/b.txt\n" +
            "--- a/b.txt\n" +
            "+++ b/b.txt\n" +
            "@@ -1,1 +1,1 @@\n" +
            "-x\n" +
            "+y\n" +
            "@@ -50,1 +50,1 @@\n" +
            "-p\n" +
            "+q\n";

        var files = GitRepository.ParseUnifiedDiff(diff);

        Assert.Equal(2, files.Count);
        Assert.Single(files[0].Hunks);
        Assert.Equal(2, files[1].Hunks.Count);
        Assert.Equal(50, files[1].Hunks[1].Lines[1].OldLineNumber);
    }

    [Fact]
    public void AHunkHeaderWithoutCountsDefaultsToOne()
    {
        // "@@ -1 +1 @@" is valid when the range is a single line.
        var diff =
            "diff --git a/a b/a\n" +
            "--- a/a\n" +
            "+++ b/a\n" +
            "@@ -7 +7 @@\n" +
            "-x\n" +
            "+y\n";

        var hunk = Assert.Single(Assert.Single(GitRepository.ParseUnifiedDiff(diff)).Hunks);
        Assert.Equal(7, hunk.Lines[1].OldLineNumber);
        Assert.Equal(7, hunk.Lines[2].NewLineNumber);
    }

    [Fact]
    public void AnEmptyDiffYieldsNoFiles()
    {
        Assert.Empty(GitRepository.ParseUnifiedDiff(""));
    }

    [Fact]
    public void BuildHunkPatchReconstructsAnApplyableSingleHunkPatch()
    {
        var file = Assert.Single(GitRepository.ParseUnifiedDiff(OneFile));
        var patch = GitRepository.BuildHunkPatch(file, file.Hunks[0]);

        Assert.Equal(
            "diff --git a/src/app.cs b/src/app.cs\n" +
            "--- a/src/app.cs\n" +
            "+++ b/src/app.cs\n" +
            "@@ -10,4 +10,5 @@ void Main()\n" +
            " keep one\n" +
            "-drop me\n" +
            "+add me\n" +
            "+add me too\n" +
            " keep two\n",
            patch);
    }
}

/// <summary>
/// The two-character porcelain status code and the ahead/behind suffix. Both come from
/// <c>git status --porcelain=v1 --branch</c>, where the difference between "staged for
/// deletion" and "deleted in the working tree" is one column.
/// </summary>
public class StatusParsingTests
{
    [Theory]
    [InlineData("A ", FileChangeKind.Added)]
    [InlineData("M ", FileChangeKind.Modified)]
    [InlineData(" M", FileChangeKind.Modified)]
    [InlineData("D ", FileChangeKind.Deleted)]
    [InlineData(" D", FileChangeKind.Deleted)]
    [InlineData("R ", FileChangeKind.Renamed)]
    [InlineData("C ", FileChangeKind.Copied)]
    [InlineData("??", FileChangeKind.Untracked)]
    [InlineData("MM", FileChangeKind.Modified)]
    public void TheIndexColumnWinsWhenBothAreSet(string code, FileChangeKind expected)
    {
        Assert.Equal(expected, GitRepository.ParseStatusCode(code));
    }

    [Theory]
    [InlineData("UU")]   // both modified
    [InlineData("AA")]   // both added
    [InlineData("DD")]   // both deleted
    [InlineData("AU")]
    [InlineData("UA")]
    [InlineData("DU")]
    [InlineData("UD")]
    public void EveryUnmergedCodeIsAConflict(string code)
    {
        // These must not fall through to Added/Deleted: a conflicted file that reads as
        // "added" would be offered a plain stage button instead of the merge tool.
        Assert.Equal(FileChangeKind.Conflicted, GitRepository.ParseStatusCode(code));
    }

    [Theory]
    [InlineData("", 0, 0)]
    [InlineData("ahead 3", 3, 0)]
    [InlineData("behind 2", 0, 2)]
    [InlineData("ahead 3, behind 2", 3, 2)]
    [InlineData("gone", 0, 0)]
    public void AheadBehindIsReadOffTheBranchLine(string track, int ahead, int behind)
    {
        Assert.Equal((ahead, behind), GitRepository.ParseAheadBehind(track));
    }
}
