using HultUI.Core.Primitives;
using HultUI.Core.Rendering;
using HultUI.Syntax;
using TextMateSharp.Grammars;
using TextMateSharp.Internal.Grammars;
using TextMateSharp.Registry;
using Xunit;

namespace HultUI.Syntax.Tests;

/// <summary>
/// The colours Dark+ (the default theme) gives the tokens whose colour changed when the highlighter
/// took them from TextMateSharp's encoded tokens (plan 17 round 9, approved by the user): each is
/// VS Code's. Before, the first rule with a colour from any of a token's scopes won, the theme's own
/// file before the one it includes, and a rule's parent scopes were ignored. Then those whose colour
/// changed when the highlighter merged Dark+ with dark_vs, the theme it includes, into one (round 11,
/// approved by the user), each VS Code's too.
/// </summary>
public class ThemeColourTests
{
    private static readonly Color String = Color.FromHex("#CE9178");
    private static readonly Color Keyword = Color.FromHex("#569CD6");
    private static readonly Color Variable = Color.FromHex("#9CDCFE");
    private static readonly Color Number = Color.FromHex("#B5CEA8");
    private static readonly Color Function = Color.FromHex("#DCDCAA");

    /// <summary>The colour of the characters <paramref name="token"/> covers where it first appears
    /// after <paramref name="after"/> on the last line of <paramref name="text"/>; null when they draw
    /// plain, throws when they are not one colour.</summary>
    internal static Color? ColourOf(string path, string text, string token, string after = "")
    {
        var spans = SyntaxHighlighter.TokenizeLines(path, text, SyntaxHighlighter.DefaultMaxLines,
            CancellationToken.None, SteppingClock.Standing)!;
        string line = text.Split('\n')[^1];
        int start = line.IndexOf(token, line.IndexOf(after, StringComparison.Ordinal) + after.Length, StringComparison.Ordinal);
        Assert.True(start >= 0, $"'{token}' not in '{line}'");
        var colours = Enumerable.Range(start, token.Length)
            .Select(i => spans[^1].FirstOrDefault(s => s.Start <= i && i < s.End) is { Length: > 0 } s ? s.Color : (Color?)null)
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

    /// <summary>In a PowerShell file a member is a function's yellow, as in VS Code: Dark+'s rule
    /// "source.powershell variable.other.member" applies inside the file's root scope. TextMateSharp
    /// applies no rule that names parent scopes, so a PowerShell file gets a theme with that rule's
    /// parent dropped. A PowerShell fence in Markdown has no source.powershell scope, and there the
    /// member is a variable, as in VS Code too.</summary>
    [Fact]
    public void APowerShellMemberIsAFunctionInAPowerShellFile()
    {
        const string line = "$n = $items.Count + $list.Keys.Length";
        Assert.Equal(Function, ColourOf("a.ps1", line, ".Count"));
        Assert.Equal(Function, ColourOf("a.psm1", line, ".Keys"));
        Assert.Equal(Variable, ColourOf("a.ps1", line, "$items"));
        Assert.Equal(Variable, ColourOf("a.md", "```powershell\n" + line, ".Count"));
    }

    /// <summary>A JSON key is a property name's light blue, as in VS Code: it was teal. Dark+'s own
    /// "support.type" beat "support.type.property-name" of dark_vs, the theme it includes, while
    /// TextMateSharp kept the two files apart; the highlighter now merges them as VS Code does (plan 17
    /// round 11, approved by the user). Strings and numbers keep their colours.</summary>
    [Theory]
    [InlineData("a.json")]
    [InlineData("a.jsonc")]
    public void AJsonKeyIsAPropertyName(string path)
    {
        const string line = "{ \"name\": \"hultui\", \"stars\": 128 }";
        Assert.Equal(Variable, ColourOf(path, line, "\"name\""));
        Assert.Equal(Variable, ColourOf(path, line, "\"stars\""));
        Assert.Equal(String, ColourOf(path, line, "\"hultui\""));
        Assert.Equal(Number, ColourOf(path, line, "128"));
    }

    /// <summary><c>this</c> is a keyword's blue, as in VS Code: it was a variable's light blue, Dark+'s
    /// own "variable" before dark_vs's "variable.language". The member after it stays a variable.</summary>
    [Theory]
    [InlineData("a.ts", "const n = this.items.size;")]
    [InlineData("a.js", "const n = this.items.size;")]
    [InlineData("a.cs", "class C { int F() => this.items.Count; }")]
    public void ThisIsAKeyword(string path, string line)
    {
        Assert.Equal(Keyword, ColourOf(path, line, "this"));
        Assert.Equal(Variable, ColourOf(path, line, "items"));
    }
}

/// <summary>
/// The themes other than Dark+ (the default). Run alone: each test changes the process-wide theme,
/// which would recolour the tests running beside it.
/// </summary>
[Collection(nameof(OtherThemeColourTests))]
public class OtherThemeColourTests
{
    /// <summary>Light+ includes light_vs as Dark+ includes dark_vs, and merges it the same way: a JSON
    /// key is light_vs's property-name blue (it was Light+'s "support.type" teal, #267F99) and
    /// <c>this</c> light_vs's "variable.language" blue (it was Light+'s "variable", #001080).</summary>
    [Fact]
    public void LightPlusMergesTheThemeItIncludes()
    {
        SyntaxHighlighter.SetTheme("LightPlus");
        try
        {
            Assert.Equal(Color.FromHex("#0451A5"), ThemeColourTests.ColourOf("a.json", "{ \"name\": 1 }", "\"name\""));
            Assert.Equal(Color.FromHex("#0000FF"), ThemeColourTests.ColourOf("a.ts", "const n = this.items.size;", "this"));
            Assert.Equal(Color.FromHex("#001080"), ThemeColourTests.ColourOf("a.ts", "const n = this.items.size;", "items"));
        }
        finally { SyntaxHighlighter.SetTheme("DarkPlus"); }
    }

    private static readonly (string Path, string Scope, string Text)[] Samples =
    [
        ("a.json", "source.json", "{\n  \"name\": \"hultui\",\n  \"tags\": [\"ui\", 2, true, null],\n  \"owner\": { \"login\": \"f\" }\n}"),
        ("a.ts", "source.ts", "export class Store {\n  private items = new Map<string, number>();\n  get size(): number { return this.items.size; }\n}\nconst o = { 'quoted': 1, plain: 2, 7: 3 };"),
        ("a.cs", "source.cs", "namespace N;\n/// <summary>Doc.</summary>\npublic sealed class C(int x)\n{\n    public int F() => this.G($\"{x} items\", _ => 1) + 0x1F;\n}"),
    ];

    /// <summary>A theme that includes no other is handed to TextMateSharp as it is: in every such
    /// bundled theme each character of a few languages has the colour TextMateSharp's own registry
    /// gives it, unwrapped (a token of the default foreground, or of the editor's, draws plain).</summary>
    [Fact]
    public void AThemeThatIncludesNoneColoursAsTextMateSharpAlone()
    {
        int themes = 0;
        try
        {
            foreach (var name in Enum.GetValues<ThemeName>())
            {
                var options = new RegistryOptions(name);
                if (options.GetDefaultTheme().GetInclude() is { Length: > 0 }) continue;
                themes++;
                Assert.True(SyntaxHighlighter.SetTheme(name.ToString()));
                var registry = new Registry(options);
                var theme = registry.GetTheme();
                Color? editor = theme.GetGuiColorDictionary().TryGetValue("editor.foreground", out var fg) ? Parse(fg) : null;
                foreach (var (path, scope, text) in Samples)
                {
                    var spans = SyntaxHighlighter.TokenizeLines(path, text, SyntaxHighlighter.DefaultMaxLines,
                        CancellationToken.None, SteppingClock.Standing)!;
                    var grammar = registry.LoadGrammar(scope);
                    IStateStack? state = null;
                    var lines = text.Split('\n');
                    for (int i = 0; i < lines.Length; i++)
                    {
                        var result = grammar.TokenizeLine2(new LineText(lines[i].AsMemory()), state, TimeSpan.MaxValue);
                        state = result.RuleStack;
                        var expected = new Color?[lines[i].Length];
                        for (int k = 0; k < result.Tokens.Length / 2; k++)
                        {
                            int id = EncodedTokenAttributes.GetForeground(result.Tokens[2 * k + 1]);
                            Color? colour = id <= 1 ? null : Parse(theme.GetColor(id));
                            if (colour == editor) colour = null;
                            int end = k + 1 < result.Tokens.Length / 2 ? result.Tokens[2 * k + 2] : lines[i].Length;
                            for (int c = result.Tokens[2 * k]; c < end; c++) expected[c] = colour;
                        }
                        var actual = new Color?[lines[i].Length];
                        foreach (var span in spans[i])
                            for (int c = span.Start; c < span.End; c++) actual[c] = span.Color;
                        Assert.True(expected.SequenceEqual(actual), $"{name} {path} line {i + 1}: '{lines[i]}'");
                    }
                }
            }
        }
        finally { SyntaxHighlighter.SetTheme("DarkPlus"); }
        Assert.Equal(18, themes);
    }

    private static Color? Parse(string? hex) => hex is { Length: 7 or 9 } && hex[0] == '#' ? Color.FromHex(hex) : null;
}

[CollectionDefinition(nameof(OtherThemeColourTests), DisableParallelization = true)]
public class OtherThemeColourCollection;
