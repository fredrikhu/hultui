using System.Collections;
using System.Text.RegularExpressions;
using HultUI.Core.Rendering;
using HultUI.Syntax;
using TextMateSharp.Grammars;
using Xunit;

namespace HultUI.Syntax.Tests;

/// <summary>
/// A Markdown text is tokenized with the grammars it can reach, not with all 49 that Markdown names
/// (plan 17, round 10): the first Markdown file kept 29.4 MB of grammars until the theme changed, and
/// one with no fences keeps Markdown's 1.3 MB now. Its colours must not change: a grammar is left out
/// only where no rule could have entered it.
///
/// <para>Run alone: a test starts from no registries (two theme changes), which would drop the
/// grammars of tests running beside it.</para>
/// </summary>
[Collection(nameof(EmbeddedLanguageTests))]
public class EmbeddedLanguageTests
{
    private const string Markdown = EmbeddedLanguages.Markdown;
    private static readonly RegistryOptions Bundled = new(ThemeName.DarkPlus);

    // A fence rule's start in the grammar, around its alternation of info-string terms.
    private const string Prefix = @"(^|\G)(\s*)(`{3,}|~{3,})\s*(?i:(";
    private const string Suffix = @")((\s+|:|\{|\?)[^`~]*)?$)";

    private static void Fresh()
    {
        SyntaxHighlighter.SetTheme("Light");
        SyntaxHighlighter.SetTheme("DarkPlus");
    }

    private static IDictionary<string, object> MarkdownRepository() =>
        (IDictionary<string, object>)((IDictionary<string, object>)Bundled.GetGrammar(Markdown))["repository"];

    /// <summary>The fence rules of the grammar file, in its order: name, terms, grammars included.</summary>
    private static List<(string Rule, string[] Terms, string[] Scopes, string Begin)> GrammarFences()
    {
        var repository = MarkdownRepository();
        var fences = new List<(string, string[], string[], string)>();
        foreach (var pattern in ((IEnumerable)((IDictionary<string, object>)repository["fenced_code_block"])["patterns"])
                 .Cast<IDictionary<string, object>>())
        {
            var name = ((string)pattern["include"])[1..];
            var rule = (IDictionary<string, object>)repository[name];
            var scopes = EmbeddedLanguages.NamesIn(rule, "");
            if (scopes.Length == 0) continue;   // the rule for a language it does not know
            var begin = (string)rule["begin"];
            Assert.StartsWith(Prefix, begin);
            Assert.EndsWith(Suffix, begin);
            fences.Add((name["fenced_code_block_".Length..], begin[Prefix.Length..^Suffix.Length].Split('|'), scopes, begin));
        }
        return fences;
    }

    [Fact]
    public void TheFenceTable_IsTheGrammarFiles()
    {
        var file = GrammarFences();
        Assert.Equal(file.Count, EmbeddedLanguages.MarkdownFences.Length);
        for (int i = 0; i < file.Count; i++)
        {
            var table = EmbeddedLanguages.MarkdownFences[i];
            Assert.Equal(file[i].Rule, table.Rule);
            Assert.Equal(file[i].Terms, table.Terms);
            Assert.Equal(file[i].Scopes, table.Scopes);
            // A term holds nothing the scan does not read: letters, \x, '.' and '.+?'.
            foreach (var term in table.Terms)
                Assert.Matches(@"^([A-Za-z0-9_#\-]|\\[+{}.]|\.\+\?|\.)+$", term);
        }
    }

    /// <summary>Every grammar Markdown names is one a scan decides: a fence's, HTML's or YAML's.</summary>
    [Fact]
    public void Markdown_NamesNoGrammarItsScanDoesNotDecide()
    {
        var names = EmbeddedLanguages.NamesIn(Bundled.GetGrammar(Markdown), Markdown);
        Assert.Equal(EmbeddedLanguages.MarkdownCovers.Where(s => s != Markdown).Order(), names.Order());
    }

    /// <summary>
    /// The scan finds a fence rule exactly where the rule's own expression matches, starting anywhere in
    /// the line (where a block quote or a list item leaves off, `\G`). .NET reads these expressions as
    /// Oniguruma does on ASCII text; past ASCII, a character that folds to letters makes the scan answer
    /// every rule.
    /// </summary>
    [Fact]
    public void TheScan_FindsTheFenceRulesTheirExpressionsMatch()
    {
        var fences = GrammarFences();
        var rules = fences.Select(f => (f.Scopes, Start: new Regex(f.Begin[@"(^|\G)".Length..]))).ToList();
        var infos = new List<string> { "", " ", "x", "hs", "{.x}", "?", "`js", "js`", " js ~", "js,x", "jsx2" };
        foreach (var term in fences.SelectMany(f => f.Terms))
        {
            var plain = term.Replace(@".+?", "ab").Replace(@"\", "");
            foreach (var word in new[] { plain, plain.Replace('.', 'x'), plain.ToUpperInvariant(), plain[..^1], plain + "s" })
                infos.AddRange([word, " " + word, word + " ", word + " title=\"a b\"", word + ":src/a", word + "{1,3}",
                    word + "?x", word + ",x", word + " `x`", word + " ~", "\t" + word + "\t"]);
        }
        int lines = 0;
        foreach (var run in new[] { "```", "~~~", "````", "``", "~~", "`~~~" })
            foreach (var lead in new[] { "", "  ", "> ", "- x " })
                foreach (var info in infos)
                {
                    string line = lead + run + info;
                    var expected = rules.Where(r => r.Start.IsMatch(line)).SelectMany(r => r.Scopes).ToHashSet();
                    var found = new HashSet<string>();
                    Assert.False(EmbeddedLanguages.FencesIn(line, found), line);
                    Assert.True(expected.SetEquals(found),
                        $"'{line}': the grammar starts {string.Join(' ', expected.Order())}; the scan found {string.Join(' ', found.Order())}");
                    lines++;
                }
        Assert.True(lines > 20_000, $"{lines} lines");

        foreach (var folds in new[] { "```ſh", "```Kotlin", "```c ß", "``` ﬁsh" })
            Assert.True(EmbeddedLanguages.FencesIn(folds, []), folds);
    }

    [Fact]
    public void AMarkdownText_WithNoFences_LoadsNoOtherGrammar()
    {
        Fresh();
        Assert.Equal([Markdown], SyntaxHighlighter.LoadedFor("README.md",
            "# Title\n\nSome *text*, **bold** and `code`.\n\n- a [link](https://example.com)\n\n> a quote\n"));
    }

    [Fact]
    public void AFence_LoadsItsLanguage_AndHtmlAndFrontMatterTheirs()
    {
        Loads([Markdown, "source.json"], "# T\n\n```json\n{\"a\": 1}\n```\n");
        Loads([Markdown, "source.yaml"], "---\ntitle: x\n---\n\n# T\n");
        Loads([Markdown, EmbeddedLanguages.HtmlDerivative, "text.html.basic", "source.css", "source.js"], "# T\n\nOne<br>two\n");
        // CoffeeScript names JavaScript in a capture only, which loads with it all the same.
        Loads([Markdown, "source.coffee", "source.js"], "```coffee\nx = `1`\n```\n");
    }

    private static void Loads(string[] grammars, string text)
    {
        Fresh();
        Assert.Equal(grammars.Order(), SyntaxHighlighter.LoadedFor("a.md", text)!.Order());
    }

    /// <summary>A text one of whose registries holds its grammars uses it; a new set of grammars
    /// replaces the sets it holds.</summary>
    [Fact]
    public void ATextUsesARegistryThatHoldsItsGrammars()
    {
        Fresh();
        var json = SyntaxHighlighter.LoadedFor("a.md", "```json\n{}\n```\n");
        Assert.Same(json, SyntaxHighlighter.LoadedFor("b.md", "plain\n"));
        var both = SyntaxHighlighter.LoadedFor("a.md", "```json\n{}\n```\n```sql\nselect 1\n```\n");
        Assert.NotSame(json, both);
        Assert.Same(both, SyntaxHighlighter.LoadedFor("c.md", "```json\n[]\n```\n"));
    }

    /// <summary>
    /// What a Markdown text with every fence language, HTML and a dashed line ends in: its registry
    /// has every grammar Markdown names, as the shared one did, so the lines above it colour as they
    /// did before.
    /// </summary>
    private static string Everything() =>
        "\n\n" + string.Concat(EmbeddedLanguages.MarkdownFences.Select(f => $"```{f.Terms[0]}\nx = 1\n```\n\n"))
        + "<br> &amp;\n\n---\n";

    private static readonly string[] Texts =
    [
        "# T\n\n```bash\nls -la | grep x && echo \"$HOME\"\n```\n",
        "```json\n{\"a\": [1, true, null]}\n```\n",
        "```csharp\nvar x = new List<int> { 1 }; // c\n```\n",
        "```python\ndef f(a: int) -> str:\n    return f'{a}'\n```\n",
        "```coffee\nx = `var y = 1`\n```\n",
        "```php\n<?php echo $x; ?>\n<b>hi</b>\n```\n",
        "```html\n<div onclick=\"go()\" style=\"color: red\"><script>let a = 1;</script></div>\n```\n",
        "---\ntitle: x\ntags: [a, b]\n---\n\n# T\n",
        "<details>\n<summary>S</summary>\n\n```js\nconst a = 1;\n```\n\n</details>\n\nFish &amp; chips <kbd>K</kbd>\n",
        "> ```ruby\n> puts 1\n> ```\n\n- item\n  ```go\n  func main() {}\n  ```\n",
        "````markdown\n```sh\necho inner\n```\n````\n\n~~~yaml\nk: v\n~~~\n",
        "```{.python title=\"x\"}\nprint(1)\n```\n\n```C++\nint main() {}\n```\n\n```rust,ignore\nfn main() {}\n```\n",
        "````textmate_init\necho \"$HOME\" # a shell fence: '.' takes the fourth backtick\n````\n\n```hs\nmain = print 1\n```\n",
    ];

    [Fact]
    public void AText_ColoursAsWithEveryGrammar()
    {
        Assert.Equal(49, SyntaxHighlighter.LoadedFor("a.md", Everything())!.Count);
        foreach (var text in Texts)
        {
            Fresh();   // else the text would take the registry of every grammar, which holds its own
            var alone = Tokenize(text);
            var withEverything = Tokenize(text + Everything());
            for (int i = 0; i < alone.Length; i++)
                Assert.True(alone[i].SequenceEqual(withEverything[i]), $"line {i + 1} of:\n{text}");
            Assert.True(alone.Any(line => line.Count > 1), $"nothing coloured in:\n{text}");
        }
    }

    [Fact]
    public void ADiff_ColoursBothVersionsAsAWholeTokenizeDoes()
    {
        const string old = "# T\n\nplain\n";
        const string @new = "# T\n\n```json\n{\"a\": 1}\n```\n\nplain\n";
        LineRange[] all = [new(1, 100)];
        var diff = SyntaxHighlighter.TokenizeDiff("a.md", old, all, @new, all, SyntaxHighlighter.DefaultMaxLines,
            CancellationToken.None, SteppingClock.Standing);
        var whole = Tokenize(@new);
        for (int i = 0; i < diff.New!.Length; i++) Assert.Equal(whole[i], diff.New[i]);
        Assert.True(diff.New[3].Count > 1);   // the JSON line, coloured
    }

    private static IReadOnlyList<TextSpan>[] Tokenize(string text) =>
        SyntaxHighlighter.TokenizeLines("a.md", text, SyntaxHighlighter.DefaultMaxLines, CancellationToken.None,
            SteppingClock.Standing)!;
}

[CollectionDefinition(nameof(EmbeddedLanguageTests), DisableParallelization = true)]
public class EmbeddedLanguageCollection;
