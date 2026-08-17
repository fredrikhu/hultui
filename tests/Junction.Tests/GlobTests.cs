using Xunit;

namespace Junction.Tests;

/// <summary>
/// The wildcard half of the branch search box. The promise is that a pattern which works
/// in <c>git branch --list</c> works here: anchored at both ends, <c>*</c> crossing the
/// slashes, <c>?</c> standing for exactly one character.
/// </summary>
public class GlobTests
{
    [Theory]
    [InlineData("feature/login", "feature/*")]
    [InlineData("feature/24.1/payments/PC-1", "feature/*")]      // '*' crosses separators
    [InlineData("release-24.1", "*-24.1")]
    [InlineData("PC-104-refund", "PC-1?4-*")]
    [InlineData("main", "*")]
    [InlineData("main", "main")]                                  // no wildcard: still exact
    [InlineData("FEATURE/Login", "feature/log*")]                 // case-insensitive
    [InlineData("a", "?")]
    [InlineData("feature/x", "*/x")]
    [InlineData("", "*")]
    public void MatchesWhatTheShellWould(string text, string pattern)
    {
        Assert.True(Glob.IsMatch(text, pattern), $"\"{pattern}\" should match \"{text}\"");
    }

    [Theory]
    [InlineData("feature/login", "login")]        // anchored: a bare word is not "contains"
    [InlineData("feature/login", "feature/l?")]   // '?' is exactly one character
    [InlineData("main", "main?")]
    [InlineData("release/24.1", "feature/*")]
    [InlineData("", "?")]
    public void RefusesWhatTheShellWould(string text, string pattern)
    {
        Assert.False(Glob.IsMatch(text, pattern), $"\"{pattern}\" should not match \"{text}\"");
    }

    [Fact]
    public void BacktracksThroughRepeatedStarsWithoutGivingUpEarly()
    {
        // The greedy first pass over-consumes and has to fall back to the last star; the
        // naive single-pass matcher answers false here.
        Assert.True(Glob.IsMatch("feature/payments/refund-flow", "*pay*refund*"));
        Assert.False(Glob.IsMatch("feature/payments/refund-flow", "*refund*pay*"));
    }

    [Fact]
    public void APathologicalPatternStillTerminates()
    {
        // Quadratic, not exponential, and it never recurses — the point of the two-pointer
        // form. A second here would mean the search box hangs on a typo.
        Assert.False(Glob.IsMatch(new string('a', 4000) + "b", "*a*a*a*a*a*c"));
    }

    [Theory]
    [InlineData("feature/*", true)]
    [InlineData("PC-1?4", true)]
    [InlineData("feature/login", false)]
    [InlineData("", false)]
    public void OnlyAQueryCarryingAWildcardCountsAsAPattern(string query, bool expected)
    {
        // Typing a plain name must stay a substring search — an anchored match on
        // "login" would find nothing at all in a repo full of feature/login branches.
        Assert.Equal(expected, Glob.HasWildcard(query));
    }
}
