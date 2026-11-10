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
        Loads([Markdown, EmbeddedLanguages.HtmlDerivative, "text.html.basic"], "# T\n\nOne<br>two\n");
        Loads([Markdown, EmbeddedLanguages.HtmlDerivative, "text.html.basic", "source.css", "source.js"],
            "<STYLE>a {}</STYLE>\n\n<b onclick=\"f()\">x</b>\n");
        Loads([Markdown, EmbeddedLanguages.HtmlDerivative, "text.html.basic", "source.js"], "<script>let a;</script>\n");
        // CoffeeScript names JavaScript in a capture only, which loads with it all the same.
        Loads([Markdown, "source.coffee", "source.js"], "```coffee\nx = `1`\n```\n");
        // A bash fence: the shell grammar alone, unless a heredoc names a language (and its '<' may be HTML's).
        Loads([Markdown, "source.shell"], "```bash\necho \"$HOME\" | tr a b\n```\n");
        Loads([Markdown, "source.shell", "source.python", EmbeddedLanguages.HtmlDerivative, "text.html.basic"],
            "```bash\npython3 - <<PYTHON\nprint(1)\nPYTHON\n```\n");
    }

    [Fact]
    public void AShellRubyOrFSharpFile_LoadsWhatItsHeredocsAndDocCommentsName()
    {
        Loads(["source.shell"], "#!/bin/sh\nfor f in *; do echo \"$f\"; done\ncat <<EOF\nhi\nEOF\n", "a.sh");
        Loads(["source.shell", "source.ruby"], "ruby <<- 'RUBY'\nputs 1\nRUBY\n", "a.sh");
        Loads(["source.ruby"], "def f = <<~EOS\n  hi\nEOS\nputs f\n", "a.rb");
        Loads(["source.ruby", "source.sql", "source.c"], "q = <<~SQL\n  select 1\nSQL\nc = <<-\"MY_C\"\n int x;\nMY_C\n", "a.rb");
        Loads(["source.ruby", "text.html.basic"], "puts DATA.read\n__END__\n<html></html>\n", "a.rb");
        Loads(["source.fsharp"], "let f x = x + 1 // no doc\n(* a comment *)\n", "a.fs");
        Loads(["source.fsharp", Markdown], "/// Adds one.\nlet f x = x + 1\n", "a.fs");
        Loads(["source.fsharp", Markdown, EmbeddedLanguages.HtmlDerivative, "text.html.basic", "source.cs"],
            "/// <summary>Adds one.</summary>\n/// ```cs\n/// var y = 1;\n/// ```\nlet f x = x + 1\n", "a.fs");
    }

    private static void Loads(string[] grammars, string text, string path = "a.md")
    {
        Fresh();
        Assert.Equal(grammars.Order(), SyntaxHighlighter.LoadedFor(path, text)!.Order());
    }

    /// <summary>
    /// Every rule of the shell, Ruby, F# and HTML grammars that includes another grammar, by the start
    /// of the outermost rule around the include: the scans look for exactly these starts.
    /// </summary>
    [Fact]
    public void TheHeredocDocCommentAndTagTables_AreTheGrammarFiles()
    {
        var shell = Outermost(EmbeddedLanguages.Shell);
        Assert.Equal(EmbeddedLanguages.ShellHeredocs.Length * 2, shell.Count);   // with and without '-'
        foreach (var (begin, scopes) in shell)
        {
            var m = Regex.Match(begin, @"^\(<<\)(-?)\\s\*\(""\|'\|\)\\s\*\(([A-Z]+)\)\(\?=");
            Assert.True(m.Success, begin);
            Assert.Equal([EmbeddedLanguages.ShellHeredocs.Single(h => h.Name == m.Groups[2].Value).Scope], scopes);
        }

        var ruby = Outermost(EmbeddedLanguages.Ruby);
        Assert.Equal(["text.html.basic"], ruby[@"^" + EmbeddedLanguages.RubyDataSection + @"\n"]);
        var heredocs = ruby.Where(r => r.Key != @"^__END__\n").SelectMany(r =>
        {
            var m = Regex.Match(r.Key, @"^\(\?=\(\?><<\[-~\]\(""\?\)\(\(\?:\[_\\w\]\+_\|\)(?:\(\?:)?([A-Z|]+)\)?\)\\b\\1\)\)$");
            Assert.True(m.Success, r.Key);
            return m.Groups[1].Value.Split('|').Select(name => (name, Assert.Single(r.Value)));
        });
        Assert.Equal(EmbeddedLanguages.RubyHeredocs.Order(), heredocs.Order());

        var fsharp = Outermost(EmbeddedLanguages.FSharp);
        Assert.Equal(new[] { @"^\s*(\(\*\*(?!\)))((?!\*\)).)*$", "///" }.Order(StringComparer.Ordinal),
            fsharp.Keys.Order(StringComparer.Ordinal));
        Assert.All(fsharp.Values, scopes => Assert.Equal([Markdown], scopes));

        var html = Outermost(EmbeddedLanguages.Html);
        Assert.Equal(3, html.Count);
        Assert.Equal(["source.js"], html[EmbeddedLanguages.HtmlEventHandlers + @"(?![\w:-])"]);
        Assert.Equal(["source.css"], html[@"(^[ \t]+)?(?=<(?i:style)\b(?!-))"]);
        Assert.Equal(["source.js", "text.html.basic"], html[@"(^[ \t]+)?(?=<(?i:script)\b(?!-))"].Order());

        // And the grammars each names are the ones its scan decides.
        foreach (var (scope, covers) in new[] { (EmbeddedLanguages.Shell, EmbeddedLanguages.ShellCovers),
                     (EmbeddedLanguages.Ruby, EmbeddedLanguages.RubyCovers), (EmbeddedLanguages.FSharp, EmbeddedLanguages.FSharpCovers),
                     (EmbeddedLanguages.Html, EmbeddedLanguages.HtmlCovers) })
            Assert.Equal(covers.Where(s => s != scope).Order(), EmbeddedLanguages.NamesIn(Bundled.GetGrammar(scope), scope).Order());
    }

    /// <summary>The grammars a grammar's rules include, by the start (begin or match) of the outermost
    /// rule around each include; "" for an include no rule's start guards.</summary>
    private static Dictionary<string, List<string>> Outermost(string scope)
    {
        var found = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        var grammar = (IDictionary<string, object>)Bundled.GetGrammar(scope);
        Walk(grammar["patterns"], null);
        if (grammar.TryGetValue("repository", out var repository))
            foreach (var rule in ((IDictionary<string, object>)repository).Values) Walk(rule, null);
        return found;

        void Walk(object node, string? start)
        {
            if (node is IDictionary<string, object> rule)
            {
                start ??= (rule.TryGetValue("begin", out var b) ? b : rule.TryGetValue("match", out var m) ? m : null) as string;
                if (rule.TryGetValue("include", out var i) && i is string include && include[0] != '#' && include is not ("$self" or "$base"))
                {
                    var list = found.TryGetValue(start ?? "", out var l) ? l : found[start ?? ""] = [];
                    var name = include.Split('#')[0];
                    if (!list.Contains(name)) list.Add(name);
                }
                foreach (var (key, value) in rule)
                    if (key != "include" && value is not string) Walk(value, start);
            }
            else if (node is IEnumerable items and not string)
                foreach (var item in items) if (item is not null) Walk(item, start);
        }
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
    /// What a text that reaches every grammar ends in: every heredoc, doc comment and tag a scan looks
    /// for, a fence in every language, HTML and a dashed line. Its registry has every grammar Markdown
    /// names, as the shared one did, so the lines above it colour as they did before.
    /// </summary>
    private static string Everything() =>
        "\n\n# <<RUBY <<PYTHON <<APPLESCRIPT <<HTML <<MARKDOWN <<TEXTILE <<SHELL\n"
        + "# <<~HTML <<~XML <<~SQL <<~CSS <<~CPP <<~C <<~JS <<~JQUERY <<~SH <<~LUA <<~RUBY\n"
        + "# __END__ /// (** <script> <style> onclick\n\n"
        + string.Concat(EmbeddedLanguages.MarkdownFences.Select(f => $"```{f.Terms[0]}\nx = 1\n```\n\n"))
        + "<br> &amp;\n\n---\n";

    private static readonly (string Path, string Text)[] Texts =
    [
        ("a.md", "# T\n\n```bash\nls -la | grep x && echo \"$HOME\"\n```\n"),
        ("a.md", "```json\n{\"a\": [1, true, null]}\n```\n"),
        ("a.md", "```csharp\nvar x = new List<int> { 1 }; // c\n```\n"),
        ("a.md", "```python\ndef f(a: int) -> str:\n    return f'{a}'\n```\n"),
        ("a.md", "```coffee\nx = `var y = 1`\n```\n"),
        ("a.md", "```php\n<?php echo $x; ?>\n<b>hi</b>\n```\n"),
        ("a.md", "```html\n<div onclick=\"go()\" style=\"color: red\"><script>let a = 1;</script></div>\n```\n"),
        ("a.md", "<p align=\"center\"><img src=\"a.png\"><br><b>bold</b> &copy;</p>\n\n<style>p { margin: 0 }</style>\n"),
        ("a.md", "---\ntitle: x\ntags: [a, b]\n---\n\n# T\n"),
        ("a.md", "<details>\n<summary>S</summary>\n\n```js\nconst a = 1;\n```\n\n</details>\n\nFish &amp; chips <kbd>K</kbd>\n"),
        ("a.md", "> ```ruby\n> puts 1\n> ```\n\n- item\n  ```go\n  func main() {}\n  ```\n"),
        ("a.md", "````markdown\n```sh\necho inner\n```\n````\n\n~~~yaml\nk: v\n~~~\n"),
        ("a.md", "```{.python title=\"x\"}\nprint(1)\n```\n\n```C++\nint main() {}\n```\n\n```rust,ignore\nfn main() {}\n```\n"),
        ("a.md", "````textmate_init\necho \"$HOME\" # a shell fence: '.' takes the fourth backtick\n````\n\n```hs\nmain = print 1\n```\n"),
        ("a.md", "```bash\npython3 - <<'PYTHON'\nimport sys; print(sys.argv)\nPYTHON\ncat <<HTML\n<i onclick=\"f()\">x</i>\nHTML\n```\n"),
        ("a.sh", "#!/bin/bash\nset -e\nfor f in *.txt; do echo \"$f\" | tee -a \"$HOME/log\"; done\ncat <<EOF\nhi $USER\nEOF\n"),
        ("a.sh", "ruby <<-'RUBY'\n\tputs \"#{1 + 2}\"\n\tRUBY\ncat << \"MARKDOWN\"\n# T\n```json\n{\"a\": 1}\n```\nMARKDOWN\n"),
        ("a.rb", "class G\n  def greet(n = 1)\n    m = <<~EOS\n      Hi #{n}\n    EOS\n    { k: :v, \"s\" => 1 }\n  end\nend\n"),
        ("a.rb", "q = <<~SQL\n  SELECT id FROM t WHERE id = 1\nSQL\nh = <<-HTML\n  <b onclick=\"f()\">x</b>\nHTML\nc = <<~MY_C\n  int x = 0;\nMY_C\n"),
        ("a.rb", "puts DATA.read\n__END__\n<!DOCTYPE html>\n<html><body><p>x</p></body></html>\n"),
        ("a.fs", "module M\n// plain\nlet area r = System.Math.PI * r * r\n(* block *)\nprintfn \"%f\" (area 1.0)\n"),
        ("a.fs", "/// Adds **one** to `x`:\n/// ```json\n/// {\"a\": 1}\n/// ```\nlet f x = x + 1\n"),
        ("a.fs", "/// <summary>Adds `a` and **b**.</summary>\nlet add a b = a + b\n(**\n```fsharp\nlet x = add 1 2\n```\n*)\nlet c a b = a < b && b > 0\n"),
    ];

    [Fact]
    public void AText_ColoursAsWithEveryGrammar()
    {
        foreach (var (path, text) in Texts)
        {
            Fresh();   // else the text would take the registry of every grammar, which holds its own
            var alone = Tokenize(text, path);
            var withEverything = Tokenize(text + Everything(), path);
            for (int i = 0; i < alone.Length; i++)
                Assert.True(alone[i].SequenceEqual(withEverything[i]), $"line {i + 1} of {path}:\n{text}");
            Assert.True(alone.Any(line => line.Count > 1), $"nothing coloured in {path}:\n{text}");
        }
        foreach (var path in new[] { "a.md", "a.sh", "a.rb", "a.fs" })
        {
            Fresh();
            Assert.Equal(49, SyntaxHighlighter.LoadedFor(path, Everything())!.Count);
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

    private static IReadOnlyList<TextSpan>[] Tokenize(string text, string path = "a.md") =>
        SyntaxHighlighter.TokenizeLines(path, text, SyntaxHighlighter.DefaultMaxLines, CancellationToken.None,
            SteppingClock.Standing)!;
}

[CollectionDefinition(nameof(EmbeddedLanguageTests), DisableParallelization = true)]
public class EmbeddedLanguageCollection;
