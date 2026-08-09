using Junction.Git;
using Xunit;

namespace Junction.Tests;

/// <summary>
/// Word-level highlight inside a paired deletion/addition. The interesting behaviour is
/// what it declines to do: when a pair shares nothing, whole-line colouring reads better
/// than painting both lines end to end, so it returns nothing at all.
/// </summary>
public class IntralineDiffTests
{
    [Fact]
    public void OnlyTheChangedTokenIsHighlighted()
    {
        var (old, changed) = IntralineDiff.Compute("int x = 1;", "int x = 2;");

        // Offset 8 is the literal; everything around it matched.
        Assert.Equal((8, 9), Assert.Single(old!));
        Assert.Equal((8, 9), Assert.Single(changed!));
    }

    [Fact]
    public void IdenticalLinesHighlightNothing()
    {
        var (old, changed) = IntralineDiff.Compute("same", "same");
        Assert.Null(old);
        Assert.Null(changed);
    }

    [Fact]
    public void LinesSharingNoTokenFallBackToWholeLineColour()
    {
        // No shared non-whitespace token means every token differs; highlighting all of
        // both sides is noise, so the caller is told to colour the lines instead.
        var (old, changed) = IntralineDiff.Compute("alpha", "bravo");
        Assert.Null(old);
        Assert.Null(changed);
    }

    [Fact]
    public void SharedWhitespaceAloneIsNotAnAnchor()
    {
        // These two lines share exactly one token: the space. A space is not evidence
        // that the lines are related, so this must still decline.
        var (old, changed) = IntralineDiff.Compute("alpha bravo", "charlie delta");
        Assert.Null(old);
        Assert.Null(changed);
    }

    [Fact]
    public void ShortUnchangedGapsAreAbsorbedIntoOneRange()
    {
        // "a,b" → "x,y" changes both operands with a single matched comma between them.
        // Two one-character highlights either side of a comma read as visual noise, so
        // gaps of up to two characters merge into the surrounding range.
        var (old, changed) = IntralineDiff.Compute("a,b", "x,y");
        Assert.Equal((0, 3), Assert.Single(old!));
        Assert.Equal((0, 3), Assert.Single(changed!));
    }

    [Fact]
    public void DistantChangesStayAsSeparateRanges()
    {
        var (old, _) = IntralineDiff.Compute(
            "one and two and three",
            "ONE and two and THREE");

        Assert.NotNull(old);
        Assert.Equal(2, old!.Count);
        Assert.Equal((0, 3), old[0]);
        Assert.Equal((16, 21), old[1]);
    }

    [Fact]
    public void PathologicallyLongLinesAreSkipped()
    {
        // The matcher is O(n·m) over tokens. Past the cap it declines rather than
        // stalling the diff viewer on a minified bundle that arrived as one line.
        var a = string.Join(" ", Enumerable.Range(0, 400).Select(i => $"w{i}"));

        var (old, changed) = IntralineDiff.Compute(a, a + " tail");
        Assert.Null(old);
        Assert.Null(changed);
    }

    [Fact]
    public void AnEmptySideHighlightsNothing()
    {
        var (addedOld, addedNew) = IntralineDiff.Compute("", "added");
        Assert.Null(addedOld);
        Assert.Null(addedNew);

        var (removedOld, removedNew) = IntralineDiff.Compute("removed", "");
        Assert.Null(removedOld);
        Assert.Null(removedNew);
    }
}
