using Junction.Git;
using Xunit;

namespace Junction.Tests;

/// <summary>
/// Binary files in a diff. git refuses to produce a patch for one — it emits a single
/// "Binary files a/x and b/x differ" line and no hunks — and Junction used to drop that
/// line on the floor, leaving a pane that said "No textual changes" about a file that had
/// very much changed. These pin the line being read, and read into the change kind, the
/// blob hashes and the byte sizes the summary card is built from.
/// </summary>
public class BinaryDiffParsingTests
{
    private const string ModifiedBinary =
        "diff --git a/assets/logo.pdf b/assets/logo.pdf\n" +
        "index e0d84f4..9114530 100644\n" +
        "Binary files a/assets/logo.pdf and b/assets/logo.pdf differ\n";

    private const string AddedBinary =
        "diff --git a/vendor/lib.dll b/vendor/lib.dll\n" +
        "new file mode 100644\n" +
        "index 0000000..25880a1\n" +
        "Binary files /dev/null and b/vendor/lib.dll differ\n";

    private const string DeletedBinary =
        "diff --git a/old.zip b/old.zip\n" +
        "deleted file mode 100644\n" +
        "index 3185dbb..0000000\n" +
        "Binary files a/old.zip and /dev/null differ\n";

    [Fact]
    public void AModifiedBinaryFileCarriesBothBlobHashes()
    {
        var file = Assert.Single(GitRepository.ParseUnifiedDiff(ModifiedBinary));

        Assert.True(file.IsBinary);
        Assert.Equal(FileChangeKind.Modified, file.Binary!.Kind);
        Assert.Equal("e0d84f4", file.Binary.OldBlob);
        Assert.Equal("9114530", file.Binary.NewBlob);
    }

    [Fact]
    public void AnAddedBinaryFileHasNoOldBlob()
    {
        // git writes an all-zero hash for the side the file was not on. That is an absence,
        // not a hash, and showing "0000000" as one would be a lie the card repeats.
        var file = Assert.Single(GitRepository.ParseUnifiedDiff(AddedBinary));

        Assert.Equal(FileChangeKind.Added, file.Binary!.Kind);
        Assert.Null(file.Binary.OldBlob);
        Assert.Equal("25880a1", file.Binary.NewBlob);
    }

    [Fact]
    public void ADeletedBinaryFileHasNoNewBlob()
    {
        var file = Assert.Single(GitRepository.ParseUnifiedDiff(DeletedBinary));

        Assert.Equal(FileChangeKind.Deleted, file.Binary!.Kind);
        Assert.Equal("3185dbb", file.Binary.OldBlob);
        Assert.Null(file.Binary.NewBlob);
    }

    [Fact]
    public void ABinaryFileHasNothingToRender()
    {
        // The point of the whole exercise: no hunks means no rows, no measuring and no
        // tokenizing. If anything ever synthesizes content for these, this fails first.
        var file = Assert.Single(GitRepository.ParseUnifiedDiff(ModifiedBinary));

        Assert.Empty(file.Hunks);
    }

    [Fact]
    public void ARenamedBinaryFileKeepsItsRenameKind()
    {
        const string renamed =
            "diff --git a/a.png b/b.png\n" +
            "similarity index 60%\n" +
            "rename from a.png\n" +
            "rename to b.png\n" +
            "index e0d84f4..9114530 100644\n" +
            "Binary files a/a.png and b/b.png differ\n";

        var file = Assert.Single(GitRepository.ParseUnifiedDiff(renamed));

        Assert.Equal(FileChangeKind.Renamed, file.Binary!.Kind);
        // "similarity index 60%" must not be mistaken for the blob line beneath it.
        Assert.Equal("e0d84f4", file.Binary.OldBlob);
    }

    [Fact]
    public void AnOrdinaryTextDiffIsNotBinary()
    {
        const string text =
            "diff --git a/src/app.cs b/src/app.cs\n" +
            "index 1111111..2222222 100644\n" +
            "--- a/src/app.cs\n" +
            "+++ b/src/app.cs\n" +
            "@@ -1,1 +1,2 @@\n" +
            " keep\n" +
            "+add\n";

        var file = Assert.Single(GitRepository.ParseUnifiedDiff(text));

        Assert.False(file.IsBinary);
        Assert.Null(file.Binary);
        Assert.Single(file.Hunks);
    }

    [Fact]
    public void HeaderStateDoesNotLeakBetweenFiles()
    {
        // The extended headers are per-file state in a single-pass parser, so the file
        // after a deleted binary must not inherit either its kind or its blob hashes.
        var files = GitRepository.ParseUnifiedDiff(
            DeletedBinary +
            "diff --git a/keep.txt b/keep.txt\n" +
            "index aaaaaaa..bbbbbbb 100644\n" +
            "--- a/keep.txt\n" +
            "+++ b/keep.txt\n" +
            "@@ -1,1 +1,1 @@\n" +
            "-old\n" +
            "+new\n" +
            ModifiedBinary);

        Assert.Equal(3, files.Count);
        Assert.False(files[1].IsBinary);
        Assert.Equal(FileChangeKind.Modified, files[2].Binary!.Kind);
        Assert.Equal("e0d84f4", files[2].Binary!.OldBlob);
    }

    [Fact]
    public void ABinaryLineInsideAHunkIsStillJustALine()
    {
        // A text file whose content happens to contain git's own wording. The header
        // branches only run before the first hunk, so this stays a plain addition.
        const string text =
            "diff --git a/notes.md b/notes.md\n" +
            "index 1111111..2222222 100644\n" +
            "--- a/notes.md\n" +
            "+++ b/notes.md\n" +
            "@@ -1,0 +1,1 @@\n" +
            "+Binary files a/x and b/x differ\n";

        var file = Assert.Single(GitRepository.ParseUnifiedDiff(text));

        Assert.False(file.IsBinary);
        Assert.Equal("Binary files a/x and b/x differ", file.Hunks[0].Lines[1].Content);
    }
}

/// <summary>
/// The same behaviour against a real repository, which is the only place git's actual
/// wording is on trial rather than a literal copied into a test.
/// </summary>
public class BinaryDiffRepositoryTests
{
    private static byte[] Blob(int length, byte seed) =>
        Enumerable.Range(0, length).Select(i => (byte)((i * 7 + seed) % 251)).ToArray();

    /// <summary>A repository with one committed binary file, modified in the working tree.</summary>
    private static TempRepo WithBinary()
    {
        var repo = TempRepo.CreateWithInitialCommit();
        repo.WriteBytes("assets/logo.pdf", Blob(600, 0));
        repo.Git("add", "-A");
        repo.Git("commit", "-m", "add a binary asset");
        repo.WriteBytes("assets/logo.pdf", Blob(1400, 3));
        return repo;
    }

    [Fact]
    public void TheWorkingDiffReportsAModifiedBinaryFileAsBinary()
    {
        using var repo = WithBinary();

        var file = Assert.Single(repo.Repo.GetWorkingDiff(staged: false));

        Assert.Equal("assets/logo.pdf", file.Path);
        Assert.True(file.IsBinary, "git said the file differs; the parse lost it");
        Assert.Equal(FileChangeKind.Modified, file.Binary!.Kind);
        Assert.NotNull(file.Binary.OldBlob);
        Assert.NotNull(file.Binary.NewBlob);
        Assert.Empty(file.Hunks);
    }

    [Fact]
    public void ACommitDiffReportsAnAddedBinaryFileAsBinary()
    {
        using var repo = WithBinary();

        var diff = repo.Repo.GetCommitDiff(repo.Head);
        var file = Assert.Single(diff, d => d.Path == "assets/logo.pdf");

        Assert.Equal(FileChangeKind.Added, file.Binary!.Kind);
        Assert.Null(file.Binary.OldBlob);
    }

    [Fact]
    public void BothSidesSizesComeBackWithoutFetchingTheBlobs()
    {
        using var repo = WithBinary();

        Assert.Equal(600, repo.Repo.GetBlobSize("HEAD", "assets/logo.pdf"));
        Assert.Equal(600, repo.Repo.GetIndexBlobSize("assets/logo.pdf"));
        Assert.Equal(1400, repo.Repo.GetWorkingFileSize("assets/logo.pdf"));

        // Absent on that side reads as null, which is how the card knows to say "—".
        Assert.Null(repo.Repo.GetBlobSize("HEAD", "assets/never-existed.pdf"));
        Assert.Null(repo.Repo.GetWorkingFileSize("assets/never-existed.pdf"));
    }

    [Fact]
    public void AnUntrackedBinaryFileIsNotReadAsText()
    {
        // Untracked files never go through `git diff`, so nothing else would have judged
        // this one. Read as text it arrives as hundreds of mojibake additions.
        using var repo = TempRepo.CreateWithInitialCommit();
        repo.WriteBytes("dropped.bin", Blob(4096, 11));

        var file = Assert.Single(repo.Repo.GetUntrackedFileDiff("dropped.bin"));

        Assert.True(file.IsBinary);
        Assert.Equal(FileChangeKind.Added, file.Binary!.Kind);
        Assert.Empty(file.Hunks);
    }

    [Fact]
    public void AnUntrackedTextFileStillGetsItsAdditions()
    {
        using var repo = TempRepo.CreateWithInitialCommit();
        repo.Write("dropped.txt", "one\ntwo\nthree\n");

        var file = Assert.Single(repo.Repo.GetUntrackedFileDiff("dropped.txt"));

        Assert.False(file.IsBinary);
        // Header line plus the three additions.
        Assert.Equal(4, Assert.Single(file.Hunks).Lines.Count);
    }
}
