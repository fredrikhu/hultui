using Meridian.Core;
using Xunit;

namespace Meridian.Tests;

/// <summary>The one set of rules for a name the app makes a file or folder from.</summary>
public class CollectionNamesTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("meridian-names-").FullName;

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Theory]
    [InlineData("list-users")]
    [InlineData("List users")]
    [InlineData("v2.charges")]
    [InlineData("_private")]
    [InlineData("console")]       // a device name only when it is the whole stem
    [InlineData("com10")]
    [InlineData("ünïcödé")]
    public void AnOrdinaryNameIsFine(string name) => Assert.Null(CollectionNames.Problem(name, "request"));

    [Theory]
    [InlineData("", "Give the request a name.")]
    [InlineData("a/b", "can't contain /")]
    [InlineData("a\\b", "can't contain /")]
    [InlineData("..", "can't contain /")]
    [InlineData("a..b", "can't contain /")]
    [InlineData("a:b", "can't contain <")]
    [InlineData("a*", "can't contain <")]
    [InlineData("tab\there", "can't contain <")]
    [InlineData(".hidden", "can't start with a dot")]
    [InlineData("trailing.", "can't end with a dot")]
    [InlineData("CON", "for a device; pick another name")]
    [InlineData("aux.req", "for a device; pick another name")]
    [InlineData("Lpt9", "for a device; pick another name")]
    public void ANameNoOsTakesIsRefused(string name, string reason) =>
        Assert.Contains(reason, CollectionNames.Problem(name, "request"));

    [Fact]
    public void TakenFindsTheEntryInAnyLetterCase()
    {
        File.WriteAllText(Path.Combine(_root, "Login.req"), "");
        Directory.CreateDirectory(Path.Combine(_root, "Users"));

        Assert.Equal("Login.req", CollectionNames.Taken(_root, "login.req"));
        Assert.Equal("Users", CollectionNames.Taken(_root, "USERS"));
        Assert.Null(CollectionNames.Taken(_root, "users.req"));
        Assert.Null(CollectionNames.Taken(Path.Combine(_root, "missing"), "x"));
    }

    /// <summary>A rename is no clash with itself, so it may change only the letter case — but
    /// only itself: another entry in another case still clashes.</summary>
    [Fact]
    public void TakenIgnoresTheEntryBeingRenamed()
    {
        File.WriteAllText(Path.Combine(_root, "Login.req"), "");
        File.WriteAllText(Path.Combine(_root, "logout.req"), "");

        Assert.Null(CollectionNames.Taken(_root, "LOGIN.req", except: "Login.req"));
        Assert.Equal("logout.req", CollectionNames.Taken(_root, "Logout.req", except: "Login.req"));
        Assert.Equal("Login.req", CollectionNames.Taken(_root, "login.req", except: "login.req"));   // exact name only
    }

    [Theory]
    [InlineData(" local.env ", ".env", "local")]
    [InlineData("local.ENV", ".env", "local")]
    [InlineData("get-user .req", ".req", "get-user")]
    [InlineData(".req", ".req", "")]
    [InlineData("plain", ".req", "plain")]
    public void StemDropsTheSuffixAndTheSpaces(string input, string suffix, string expected) =>
        Assert.Equal(expected, CollectionNames.Stem(input, suffix));
}
