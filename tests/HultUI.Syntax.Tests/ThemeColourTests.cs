using HultUI.Core.Primitives;
using HultUI.Core.Rendering;
using HultUI.Syntax;
using Xunit;

namespace HultUI.Syntax.Tests;

/// <summary>
/// The colours Dark+ (the default theme) gives the tokens whose colour changed when the highlighter
/// took them from TextMateSharp's encoded tokens (plan 17 round 9, approved by the user): each is
/// VS Code's. Before, the first rule with a colour from any of a token's scopes won, the theme's own
/// file before the one it includes, and a rule's parent scopes were ignored.
/// </summary>
public class ThemeColourTests
{
    private static readonly Color String = Color.FromHex("#CE9178");
    private static readonly Color Keyword = Color.FromHex("#569CD6");
    private static readonly Color Variable = Color.FromHex("#9CDCFE");
    private static readonly Color Number = Color.FromHex("#B5CEA8");

    /// <summary>The colour of the characters <paramref name="token"/> covers where it first appears
    /// after <paramref name="after"/>; null when they draw plain, throws when they are not one colour.</summary>
    private static Color? ColourOf(string path, string line, string token, string after = "")
    {
        var spans = SyntaxHighlighter.TokenizeLines(path, line, SyntaxHighlighter.DefaultMaxLines,
            CancellationToken.None, SteppingClock.Standing)!;
        int start = line.IndexOf(token, line.IndexOf(after, StringComparison.Ordinal) + after.Length, StringComparison.Ordinal);
        Assert.True(start >= 0, $"'{token}' not in '{line}'");
        var colours = Enumerable.Range(start, token.Length)
            .Select(i => spans[0].FirstOrDefault(s => s.Start <= i && i < s.End) is { Length: > 0 } s ? s.Color : (Color?)null)
            .Distinct().ToList();
        return Assert.Single(colours);
    }

    /// <summary>A quoted object key is a string, as in VS Code: it was the key's light blue (Dark+'s
    /// "meta.object-literal.key" before dark_vs's "string"). An unquoted key keeps that blue, and a
    /// numeric key is a number.</summary>
    [Theory]
    [InlineData("a.ts")]
    [InlineData("a.js")]
    public void AQuotedObjectKeyIsAString(string path)
    {
        const string line = "const o = { 'quoted': 1, plain: 2, 7: 3 };";
        Assert.Equal(String, ColourOf(path, line, "'quoted'"));
        Assert.Equal(Variable, ColourOf(path, line, "plain"));
        Assert.Equal(Number, ColourOf(path, line, "7", after: "plain"));
    }

    /// <summary><c>new</c> is a keyword, as in VS Code: it was purple, from Dark+'s rule for C++'s
    /// <c>new</c> ("source.cpp keyword.operator.new"), whose parent scope was ignored.</summary>
    [Theory]
    [InlineData("a.ts")]
    [InlineData("a.js")]
    public void NewIsAKeyword(string path) =>
        Assert.Equal(Keyword, ColourOf(path, "const d = new Date();", "new"));

    /// <summary>A C member after <c>.</c> or <c>-&gt;</c> is a variable, as in VS Code: it was yellow,
    /// from Dark+'s rule for PowerShell's members ("source.powershell variable.other.member"), whose
    /// parent scope was ignored.</summary>
    [Fact]
    public void ACMemberIsAVariable()
    {
        const string line = "int f(struct s *p) { return p->left + p->right.width; }";
        Assert.Equal(Variable, ColourOf("a.c", line, "left"));
        Assert.Equal(Variable, ColourOf("a.c", line, "width"));
    }
}
