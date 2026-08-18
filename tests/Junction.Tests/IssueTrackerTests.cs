using Junction.Git;
using Xunit;

namespace Junction.Tests;

/// <summary>
/// Reading a repository's <c>.issuetracker</c> and finding what a commit message refers to.
/// The file is git-config syntax and is read with git, so the escaping cases here are the
/// ones a hand-rolled INI parser gets wrong: <c>"\\d"</c> has to arrive as <c>\d</c>.
/// </summary>
public class IssueTrackerTests
{
    private static TempRepo RepoWith(string issueTrackerContents)
    {
        var repo = TempRepo.CreateWithInitialCommit();
        repo.Write(IssueTrackerFile.FileName, issueTrackerContents);
        return repo;
    }

    [Fact]
    public void ARuleTurnsAReferenceIntoAUrl()
    {
        using var repo = RepoWith("""
            [issuetracker "Jira"]
                regex = "ABCD-(\\d+)"
                url = "https://example.atlassian.net/browse/ABCD-$1"
            """);

        var rules = IssueTrackerFile.Load(repo.Path);
        var found = IssueTrackerFile.Find("Fix ABCD-4291 before the release", rules);

        var one = Assert.Single(found);
        Assert.Equal("ABCD-4291", "Fix ABCD-4291 before the release".Substring(one.Start, one.Length));
        Assert.Equal("https://example.atlassian.net/browse/ABCD-4291", one.Url);
        Assert.Equal("Jira", one.Label);
    }

    [Fact]
    public void SeveralTrackersCoexistAndBothMatch()
    {
        using var repo = RepoWith("""
            [issuetracker "GitHub"]
                regex = "#(\\d+)"
                url = "https://github.com/acme/widget/issues/$1"
            [issuetracker "Jira"]
                regex = "OPS-(\\d+)"
                url = "https://example.atlassian.net/browse/OPS-$1"
            """);

        var found = IssueTrackerFile.Find("Closes #12, follows OPS-3", IssueTrackerFile.Load(repo.Path));

        Assert.Equal(2, found.Count);
        // In the order they appear in the message, not the order the rules are declared.
        Assert.Equal("https://github.com/acme/widget/issues/12", found[0].Url);
        Assert.Equal("https://example.atlassian.net/browse/OPS-3", found[1].Url);
    }

    // Whichever rule is declared first owns the text, so a repository can put its more
    // specific tracker above the catch-all and have it win.
    [Fact]
    public void OverlappingMatchesGoToTheRuleDeclaredFirst()
    {
        using var repo = RepoWith("""
            [issuetracker "Specific"]
                regex = "PROJ-[0-9]+"
                url = "https://specific.example/$0"
            [issuetracker "Catch all"]
                regex = "[A-Z]+-[0-9]+"
                url = "https://catchall.example/$0"
            """);

        var one = Assert.Single(IssueTrackerFile.Find("see PROJ-7", IssueTrackerFile.Load(repo.Path)));
        Assert.Equal("Specific", one.Label);
    }

    [Fact]
    public void AnIncompleteOrUnreadableRuleIsSkippedWithoutLosingTheOthers()
    {
        using var repo = RepoWith("""
            [issuetracker "No url"]
                regex = "X-(\\d+)"
            [issuetracker "Bad regex"]
                regex = "([unclosed"
                url = "https://example/$1"
            [issuetracker "Fine"]
                regex = "Y-(\\d+)"
                url = "https://example/y/$1"
            """);

        var rules = IssueTrackerFile.Load(repo.Path);
        var fine = Assert.Single(rules);
        Assert.Equal("Fine", fine.Label);
        Assert.Equal("https://example/y/9", Assert.Single(IssueTrackerFile.Find("Y-9", rules)).Url);
    }

    [Fact]
    public void ARepositoryWithNoFileHasNoRules()
    {
        using var repo = TempRepo.CreateWithInitialCommit();
        Assert.Empty(IssueTrackerFile.Load(repo.Path));
        Assert.Empty(IssueTrackerFile.Find("ABCD-1", IssueTrackerFile.Load(repo.Path)));
    }

    // The message is what carries the references, so it has to survive the round trip
    // through git in one piece — blank line, body and all.
    [Fact]
    public void TheFullCommitMessageComesBackFromGit()
    {
        using var repo = TempRepo.CreateWithInitialCommit();
        repo.Write("a.txt", "a\n");
        repo.Git("add", "-A");
        repo.Git("commit", "-m", "Subject line", "-m", "Body mentioning ABCD-7\nand a second line");

        var commit = repo.Repo.GetCommit("HEAD")!;
        Assert.Equal("Subject line", commit.Subject);
        Assert.Contains("ABCD-7", commit.Message);
        Assert.StartsWith("Subject line", commit.Message);
        Assert.Contains("and a second line", commit.Message);
    }
}
