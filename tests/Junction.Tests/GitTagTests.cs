using Junction.Git;
using Xunit;

namespace Junction.Tests;

/// <summary>
/// Reading <c>refs/tags</c>. The trap here is the two kinds of tag: an annotated tag is an
/// object of its own that points at the commit, so the ref's own sha is the tag object and
/// the commit is one dereference away — a list that showed the ref sha would print a hash
/// that appears nowhere in the history.
/// </summary>
public class GitTagTests
{
    [Fact]
    public void ALightweightTagPointsAtItsCommit()
    {
        using var repo = TempRepo.CreateWithInitialCommit();
        repo.Git("tag", "v1.0.0");

        var tag = Assert.Single(repo.Repo.GetTags());

        Assert.Equal("v1.0.0", tag.Name);
        Assert.Equal(repo.Head, tag.Hash);
        Assert.False(tag.Annotated);
        Assert.Equal("", tag.Message);
        Assert.Equal("initial", tag.Subject);
    }

    [Fact]
    public void AnAnnotatedTagAlsoPointsAtItsCommitAndNotAtTheTagObject()
    {
        using var repo = TempRepo.CreateWithInitialCommit();
        repo.Git("tag", "-a", "v2.0.0", "-m", "the second one");

        var tag = Assert.Single(repo.Repo.GetTags());

        Assert.True(tag.Annotated);
        Assert.Equal(repo.Head, tag.Hash);
        // Which is the whole point: the ref itself resolves to something else.
        Assert.NotEqual(repo.Head, repo.Git("rev-parse", "v2.0.0").Stdout.Trim());
        // The tag's own message, and separately the commit it points at.
        Assert.Equal("the second one", tag.Message);
        Assert.Equal("initial", tag.Subject);
    }

    /// <summary>Tagger dates are pinned rather than taken from the clock: git sorts
    /// creatordate to the second, so two tags made in the same second tie and the order
    /// falls back to the ref name — which had this test passing alone and failing in a
    /// full run.</summary>
    [Fact]
    public void TagsComeBackNewestFirst()
    {
        using var repo = TempRepo.CreateWithInitialCommit();
        TagAt(repo, "v1.1.0", "2020-01-02T00:00:00+00:00");
        TagAt(repo, "v1.0.0", "2020-01-01T00:00:00+00:00");

        var names = repo.Repo.GetTags().Select(t => t.Name).ToList();

        Assert.Equal(["v1.1.0", "v1.0.0"], names);
    }

    /// <summary>An annotated tag with a tagger date of its own. <c>git tag -a</c> takes it
    /// from GIT_COMMITTER_DATE, and child processes inherit this one.</summary>
    private static void TagAt(TempRepo repo, string name, string isoDate)
    {
        Environment.SetEnvironmentVariable("GIT_COMMITTER_DATE", isoDate);
        try { repo.Git("tag", "-a", name, "-m", name); }
        finally { Environment.SetEnvironmentVariable("GIT_COMMITTER_DATE", null); }
    }

    [Fact]
    public void ATagOnAnEarlierCommitReportsThatCommitsSubject()
    {
        using var repo = TempRepo.CreateWithInitialCommit();
        var first = repo.Head;
        repo.Write("a.txt", "one\n");
        repo.Git("add", "-A");
        repo.Git("commit", "-m", "second");
        repo.Git("tag", "release", first);

        var tag = Assert.Single(repo.Repo.GetTags());

        Assert.Equal(first, tag.Hash);
        Assert.Equal("initial", tag.Subject);
    }

    [Fact]
    public void ARepositoryWithNoTagsListsNone()
    {
        using var repo = TempRepo.CreateWithInitialCommit();
        Assert.Empty(repo.Repo.GetTags());
    }

    [Fact]
    public void CreatingAndDeletingATagIsVisibleInTheListing()
    {
        using var repo = TempRepo.CreateWithInitialCommit();

        Assert.True(repo.Repo.CreateTag("v3.0.0", commit: null, message: "annotated").Success);
        Assert.Contains(repo.Repo.GetTags(), t => t.Name == "v3.0.0" && t.Annotated);

        Assert.True(repo.Repo.DeleteTag("v3.0.0").Success);
        Assert.Empty(repo.Repo.GetTags());
    }

    /// <summary>A tag message can run to paragraphs; the listing keeps the subject line so
    /// a row stays a row.</summary>
    [Fact]
    public void OnlyTheFirstLineOfAnAnnotatedMessageIsKept()
    {
        using var repo = TempRepo.CreateWithInitialCommit();
        repo.Git("tag", "-a", "v4.0.0", "-m", "headline\n\nthe body, which is longer");

        var tag = Assert.Single(repo.Repo.GetTags());

        Assert.Equal("headline", tag.Message);
    }
}
