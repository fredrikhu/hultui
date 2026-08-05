using VesperCommander;
using Xunit;

namespace VesperCommander.Tests;

public class FindEngineTests
{
    private static FindEngine.Options Opt(string pattern, bool matchCase = false, bool regex = false) =>
        new(pattern, null, Recursive: false, matchCase, regex, Hidden: false);

    [Theory]
    [InlineData("list", "listing.rs", true)]
    [InlineData("list", "main.rs", false)]
    [InlineData("LIST", "listing.rs", true)]     // case-insensitive by default
    public void Substring_Matches(string pattern, string name, bool expected) =>
        Assert.Equal(expected, FindEngine.NameMatches(name, Opt(pattern)));

    [Fact]
    public void Substring_CaseSensitive_WhenRequested()
    {
        Assert.False(FindEngine.NameMatches("listing.rs", Opt("LIST", matchCase: true)));
        Assert.True(FindEngine.NameMatches("listing.rs", Opt("list", matchCase: true)));
    }

    [Theory]
    [InlineData("*.rs", "main.rs", true)]
    [InlineData("*.rs", "main.toml", false)]
    [InlineData("main.*", "main.rs", true)]
    [InlineData("m?in.rs", "main.rs", true)]
    [InlineData("m?in.rs", "mtin.rs", true)]
    [InlineData("m?in.rs", "moain.rs", false)]   // ? is exactly one char
    public void Glob_Matches(string pattern, string name, bool expected) =>
        Assert.Equal(expected, FindEngine.NameMatches(name, Opt(pattern)));

    [Theory]
    [InlineData(@"^main\.\w+$", "main.rs", true)]
    [InlineData(@"^main\.\w+$", "xmain.rs", false)]
    public void Regex_Matches(string pattern, string name, bool expected) =>
        Assert.Equal(expected, FindEngine.NameMatches(name, Opt(pattern, regex: true)));

    [Fact]
    public void InvalidRegex_MatchesNothing() =>
        Assert.False(FindEngine.NameMatches("main.rs", Opt("(unclosed", regex: true)));

    [Fact]
    public void EmptyPattern_MatchesEverything() =>
        Assert.True(FindEngine.NameMatches("anything.xyz", Opt("")));

    [Theory]
    [InlineData(".zip", "archive.zip", true)]
    [InlineData(".zip", "archive.ZIP", true)]      // case-insensitive by default
    [InlineData(".zip", "archive.zip.bak", false)] // must be the actual extension
    [InlineData(".zip", "gzip.txt", false)]        // not a substring match
    [InlineData(".tar.gz", "backup.tar.gz", true)] // compound extension
    [InlineData(".rs", "main.rs", true)]
    [InlineData(".rs", "main.rst", false)]
    public void LeadingDot_MatchesByExtension(string pattern, string name, bool expected) =>
        Assert.Equal(expected, FindEngine.NameMatches(name, Opt(pattern)));

    [Fact]
    public void ContentHits_CountsOccurrences()
    {
        var tmp = Path.GetTempFileName();
        try
        {
            File.WriteAllText(tmp, "panic here\nno match\npanic again\n");
            var o = new FindEngine.Options("", "panic", false, false, false, false);
            Assert.Equal(2, FindEngine.ContentHits(tmp, o));
        }
        finally { File.Delete(tmp); }
    }

    [Fact]
    public void ContentHits_ZeroForBinary()
    {
        var tmp = Path.GetTempFileName();
        try
        {
            File.WriteAllBytes(tmp, new byte[] { (byte)'a', 0, (byte)'b', (byte)'a' });   // NUL ⇒ binary
            var o = new FindEngine.Options("", "a", false, false, false, false);
            Assert.Equal(0, FindEngine.ContentHits(tmp, o));
        }
        finally { File.Delete(tmp); }
    }
}
