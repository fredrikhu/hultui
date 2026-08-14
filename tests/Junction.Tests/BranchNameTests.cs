using Xunit;

namespace Junction.Tests;

/// <summary>
/// Splitting a branch ref into the chip / title / subtitle the sidebar shows. Real
/// repositories are full of names that ignore the convention, so the parser's job is as
/// much about degrading gracefully as about recognising the scheme.
/// </summary>
public class BranchNameTests
{
    [Fact]
    public void AFullyQualifiedFeatureBranchSplitsIntoAllThreeParts()
    {
        var b = BranchName.Parse("feature/24.1/payments/PC-1043-refund-flow");

        Assert.Equal("FEAT", b.ChipLabel);
        Assert.Equal("PC-1043-refund-flow", b.Title);
        Assert.Equal("24.1  ·  payments", b.Sub);
        Assert.True(b.HasSub);
        Assert.Equal("feature/24.1/payments/PC-1043-refund-flow", b.Short);
    }

    [Theory]
    [InlineData("main", "MAIN")]
    [InlineData("master", "MAIN")]
    [InlineData("develop", "DEV")]
    [InlineData("release/24.1", "REL")]
    [InlineData("wip/24.1/core/spike", "WIP")]
    public void KnownPrefixesGetTheirOwnChip(string name, string chip)
    {
        Assert.Equal(chip, BranchName.Parse(name).ChipLabel);
    }

    [Fact]
    public void AnUnknownPrefixBecomesItsOwnChip()
    {
        // No mapping, no fallback label — the prefix the repo actually uses is more
        // informative than a generic one.
        var b = BranchName.Parse("hotfix/urgent-thing");

        Assert.Equal("HOTFIX", b.ChipLabel);
        Assert.Equal("urgent-thing", b.Title);
        Assert.Equal("urgent-thing", b.Sub);
    }

    [Fact]
    public void AFlatNameHasNoSubtitle()
    {
        var b = BranchName.Parse("main");

        Assert.Equal("main", b.Title);
        Assert.Equal("", b.Sub);
        Assert.False(b.HasSub);
    }

    [Fact]
    public void ADeeplyNestedNameKeepsTheLeafAsTheTitle()
    {
        // Only the first two segments after the prefix form the subtitle; the title is
        // always the leaf, however deep the name goes.
        var b = BranchName.Parse("feature/24.1/payments/sub/team/PC-9-thing");

        Assert.Equal("PC-9-thing", b.Title);
        Assert.Equal("24.1  ·  payments", b.Sub);
    }

    [Fact]
    public void AnEmptyNameDoesNotThrow()
    {
        var b = BranchName.Parse("");

        Assert.Equal("", b.ChipLabel);
        Assert.Equal("", b.Title);
        Assert.Equal("", b.Short);
    }

    // ── Validation ───────────────────────────────────────────────────────
    // The new-worktree prompt names the branch after the folder, so these names are
    // *derived* rather than typed and reach git without anyone reading them first.

    [Theory]
    [InlineData("main")]
    [InlineData("feature/24.1/payments/PC-1043-refund-flow")]
    [InlineData("release-24.1")]
    [InlineData("fix.a.thing")]
    [InlineData("junction-revamped-wt")]   // what SuggestWorktreePath proposes
    public void ValidateAcceptsNamesGitAccepts(string name)
    {
        Assert.Null(BranchName.Validate(name));
    }

    [Theory]
    [InlineData("", "Enter")]
    [InlineData("my work", "spaces")]
    [InlineData("wip~1", "'~'")]
    [InlineData("a^b", "'^'")]
    [InlineData("a:b", "':'")]
    [InlineData("a?b", "'?'")]
    [InlineData("a*b", "'*'")]
    [InlineData("a[b", "'['")]
    [InlineData("a\\b", "'\\'")]
    [InlineData("a..b", "'..'")]
    [InlineData("HEAD@{0}", "'@{'")]
    [InlineData("a//b", "empty path component")]
    [InlineData("@", "reserved")]
    [InlineData("/leading", "'/'")]
    [InlineData("trailing/", "'/'")]
    [InlineData("-dashed", "'-'")]
    [InlineData("dotted.", "'.'")]
    [InlineData("thing.lock", "'.lock'")]
    [InlineData(".hidden", "'.'")]
    [InlineData("feature/.hidden", "start with '.'")]
    [InlineData("feature/thing.lock", "'.lock'")]
    public void ValidateExplainsWhatIsWrongRatherThanJustSayingNo(string name, string expected)
    {
        var error = BranchName.Validate(name);

        Assert.NotNull(error);
        // The point of validating up front is the sentence: git's own answer is
        // "fatal: '<name>' is not a valid branch name", which names no character.
        Assert.Contains(expected, error, StringComparison.Ordinal);
    }

    [Fact]
    public void ValidateRejectsSurroundingWhitespaceBeforeItRejectsTheSpaceItself()
    {
        // A folder picked up with a trailing space is the likeliest way this fires, and
        // "can't start or end with a space" is a more actionable sentence than the
        // generic no-spaces rule that would otherwise catch it.
        Assert.Contains("start or end with a space", BranchName.Validate("feature/x ")!, StringComparison.Ordinal);
    }

    [Fact]
    public void ValidateTreatsANullNameAsEmpty()
    {
        Assert.Equal("Enter a branch name", BranchName.Validate(null));
    }
}
