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
}
