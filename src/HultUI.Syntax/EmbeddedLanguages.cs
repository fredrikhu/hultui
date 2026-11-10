using System.Buffers;
using System.Collections;

namespace HultUI.Syntax;

/// <summary>
/// Which of the grammars that a text's grammar embeds the text can reach.
///
/// <para>TextMateSharp loads with a grammar every grammar it names, and builds the rules of all of them
/// at the first line. Markdown names a grammar for each of its 52 fence rules, and HTML and YAML, and
/// those name more: 49 bundled grammars, so the first Markdown file allocated 82.9 MB and kept 29.4 MB
/// until the theme changed (plan 17, round 10). A grammar only enters a language it embeds through a
/// rule whose start matches somewhere in the text, though: a fence in that language, an HTML tag. So
/// such a text is tokenized by a registry that hands out only the grammars a rule could start in it,
/// and to it any other is missing, as an embedded language with no grammar is to TextMateSharp: a rule
/// that includes it loses that pattern, or drops out when it was its only one. That changes nothing
/// where the rule could not have matched anyway, so the text colours as it did with every grammar.</para>
///
/// <para>A scan may answer too much, never too little: a grammar it cannot rule out is handed out. A
/// grammar with no scan here reaches every grammar it names.</para>
/// </summary>
internal static class EmbeddedLanguages
{
    internal const string Markdown = "text.html.markdown";
    internal const string HtmlDerivative = "text.html.derivative";
    internal const string Yaml = "source.yaml";

    /// <summary>Whether a text in <paramref name="scope"/> gets a registry of the grammars it reaches,
    /// rather than the one every other grammar shares.</summary>
    public static bool Scans(string scope) => scope == Markdown;

    /// <summary>
    /// Every grammar <paramref name="text"/> (and <paramref name="other"/>, a diff's second version) can
    /// reach from <paramref name="root"/>, root included. <paramref name="namesOf"/> gives the scopes a
    /// grammar names (<see cref="NamesIn"/>), null for a scope with no grammar, which is left out.
    /// <paramref name="longLine"/> is how many characters of a line the root's tokenize reads.
    /// </summary>
    public static HashSet<string> Reach(string root, string text, string? other, int longLine,
        Func<string, string[]?> namesOf)
    {
        var reached = new HashSet<string>(StringComparer.Ordinal);
        if (namesOf(root) is null) return reached;
        var queue = new Queue<string>();
        reached.Add(root);
        queue.Enqueue(root);
        while (queue.TryDequeue(out var scope))
        {
            var fired = scope == Markdown ? MarkdownReaches(text, other, longLine) : null;
            foreach (var name in namesOf(scope)!)
            {
                if (fired is not null && MarkdownCovers.Contains(name) && !fired.Contains(name)) continue;
                if (namesOf(name) is not null && reached.Add(name)) queue.Enqueue(name);
            }
        }
        return reached;
    }

    /// <summary>The scopes of the other grammars a grammar includes, anywhere in it: in its patterns,
    /// its repository, its captures. TextMateSharp loads those its patterns and repository name; a
    /// capture's are looked up when its rules are built.</summary>
    public static string[] NamesIn(object grammar, string self)
    {
        var names = new List<string>();
        Walk(grammar);
        names.Remove(self);
        return [.. names];

        void Walk(object node)
        {
            if (node is IDictionary<string, object> rule)
            {
                foreach (var (key, value) in rule)
                {
                    if (key == "include" && value is string include)
                    {
                        if (include.Length == 0 || include[0] == '#' || include is "$self" or "$base") continue;
                        int hash = include.IndexOf('#');
                        var scope = hash < 0 ? include : include[..hash];
                        if (!names.Contains(scope)) names.Add(scope);
                    }
                    else if (value is not null and not string) Walk(value);
                }
            }
            else if (node is IEnumerable items)
                foreach (var item in items)
                    if (item is not null and not string) Walk(item);
        }
    }

    // ── Markdown ──────────────────────────────────────────────────────────
    //
    // Markdown reaches a fence language through its fence rule, whose start names it; HTML (and with it
    // JavaScript and CSS) through a tag, a comment or an entity, each of which starts with '<' or '&';
    // and YAML through front matter, `\A-{3}\s*$`, which opens a text's first line. \A matches on any
    // line the tokenize starts from no state, and a line too long to keep its state leaves the next one
    // none, so any line that starts with three dashes counts.

    /// <summary>
    /// The fence rules of the Markdown grammar, in its order: each start's info-string terms, spelled as
    /// in the grammar (a regular expression's alternatives, which <see cref="TermAt"/> reads), and the
    /// grammars the rule includes. A test holds this to the grammar file.
    /// </summary>
    internal static readonly FenceRule[] MarkdownFences =
    [
        new("css", [@"css", @"css.erb"], [@"source.css"]),
        new("basic", [@"html", @"htm", @"shtml", @"xhtml", @"inc", @"tmpl", @"tpl"], [@"text.html.basic"]),
        new("ini", [@"ini", @"conf"], [@"source.ini"]),
        new("java", [@"java", @"bsh"], [@"source.java"]),
        new("lua", [@"lua"], [@"source.lua"]),
        new("makefile", [@"Makefile", @"makefile", @"GNUmakefile", @"OCamlMakefile"], [@"source.makefile"]),
        new("perl", [@"perl", @"pl", @"pm", @"pod", @"t", @"PL", @"psgi", @"vcl"], [@"source.perl"]),
        new("r", [@"R", @"r", @"s", @"S", @"Rprofile", @"\{\.r.+?\}"], [@"source.r"]),
        new("ruby", [@"ruby", @"rb", @"rbx", @"rjs", @"Rakefile", @"rake", @"cgi", @"fcgi", @"gemspec", @"irbrc",
            @"Capfile", @"ru", @"prawn", @"Cheffile", @"Gemfile", @"Guardfile", @"Hobofile", @"Vagrantfile",
            @"Appraisals", @"Rantfile", @"Berksfile", @"Berksfile.lock", @"Thorfile", @"Puppetfile"], [@"source.ruby"]),
        new("php", [@"php", @"php3", @"php4", @"php5", @"phpt", @"phtml", @"aw", @"ctp"], [@"text.html.basic", @"source.php"]),
        new("sql", [@"sql", @"ddl", @"dml"], [@"source.sql"]),
        new("vs_net", [@"vb"], [@"source.asp.vb.net"]),
        new("xml", [@"xml", @"xsd", @"tld", @"jsp", @"pt", @"cpt", @"dtml", @"rss", @"opml"], [@"text.xml"]),
        new("xsl", [@"xsl", @"xslt"], [@"text.xml.xsl"]),
        new("yaml", [@"yaml", @"yml"], [@"source.yaml"]),
        new("dosbatch", [@"bat", @"batch"], [@"source.batchfile"]),
        new("clojure", [@"clj", @"cljs", @"clojure"], [@"source.clojure"]),
        new("coffee", [@"coffee", @"Cakefile", @"coffee.erb"], [@"source.coffee"]),
        new("c", [@"c", @"h"], [@"source.c"]),
        new("cpp", [@"cpp", @"c\+\+", @"cxx"], [@"source.cpp"]),
        new("diff", [@"patch", @"diff", @"rej"], [@"source.diff"]),
        new("dockerfile", [@"dockerfile", @"Dockerfile"], [@"source.dockerfile"]),
        new("git_commit", [@"COMMIT_EDITMSG", @"MERGE_MSG"], [@"text.git-commit"]),
        new("git_rebase", [@"git-rebase-todo"], [@"text.git-rebase"]),
        new("go", [@"go", @"golang"], [@"source.go"]),
        new("groovy", [@"groovy", @"gvy"], [@"source.groovy"]),
        new("pug", [@"jade", @"pug"], [@"text.pug"]),
        new("js", [@"js", @"jsx", @"javascript", @"es6", @"mjs", @"cjs", @"\{\.js.+?\}"], [@"source.js"]),
        new("js_regexp", [@"regexp"], [@"source.js.regexp"]),
        new("json", [@"json", @"json5", @"sublime-settings", @"sublime-menu", @"sublime-keymap", @"sublime-mousemap",
            @"sublime-theme", @"sublime-build", @"sublime-project", @"sublime-completions"], [@"source.json"]),
        new("jsonc", [@"jsonc"], [@"source.json.comments"]),
        new("less", [@"less"], [@"source.css.less"]),
        new("objc", [@"objectivec", @"objective-c", @"mm", @"objc", @"obj-c", @"m", @"h"], [@"source.objc"]),
        new("swift", [@"swift"], [@"source.swift"]),
        new("scss", [@"scss"], [@"source.css.scss"]),
        new("perl6", [@"perl6", @"p6", @"pl6", @"pm6", @"nqp"], [@"source.perl.6"]),
        new("powershell", [@"powershell", @"ps1", @"psm1", @"psd1"], [@"source.powershell"]),
        new("python", [@"python", @"py", @"py3", @"rpy", @"pyw", @"cpy", @"SConstruct", @"Sconstruct", @"sconstruct",
            @"SConscript", @"gyp", @"gypi", @"\{\.python.+?\}"], [@"source.python"]),
        new("regexp_python", [@"re"], [@"source.regexp.python"]),
        new("rust", [@"rust", @"rs", @"\{\.rust.+?\}"], [@"source.rust"]),
        new("scala", [@"scala", @"sbt"], [@"source.scala"]),
        new("shell", [@"shell", @"sh", @"bash", @"zsh", @"bashrc", @"bash_profile", @"bash_login", @"profile",
            @"bash_logout", @".textmate_init", @"\{\.bash.+?\}"], [@"source.shell"]),
        new("ts", [@"typescript", @"ts"], [@"source.ts"]),
        new("tsx", [@"tsx"], [@"source.tsx"]),
        new("csharp", [@"cs", @"csharp", @"c#"], [@"source.cs"]),
        new("fsharp", [@"fs", @"fsharp", @"f#"], [@"source.fsharp"]),
        new("dart", [@"dart"], [@"source.dart"]),
        new("handlebars", [@"handlebars", @"hbs"], [@"text.html.handlebars"]),
        new("markdown", [@"markdown", @"md"], [@"text.html.markdown"]),
        new("log", [@"log"], [@"text.log"]),
        new("erlang", [@"erlang"], [@"source.erlang"]),
        new("elixir", [@"elixir"], [@"source.elixir"]),
    ];

    /// <summary>One fence rule: <c>fenced_code_block_</c><see cref="Rule"/>.</summary>
    internal sealed record FenceRule(string Rule, string[] Terms, string[] Scopes);

    /// <summary>The grammars Markdown reaches only through a scan; any other it names, it always reaches.</summary>
    internal static readonly HashSet<string> MarkdownCovers =
        [.. MarkdownFences.SelectMany(rule => rule.Scopes), HtmlDerivative, Yaml];

    /// <summary>The grammars of the fence rules that could start in <paramref name="text"/>, read as
    /// Markdown's tokenize reads it; true when any might. A test reads it.</summary>
    internal static bool FencesIn(string text, HashSet<string> found) => Fences(text, SyntaxHighlighter.LongLine, found);

    /// <summary>Of the grammars Markdown names, those a rule could enter in these texts.</summary>
    private static HashSet<string> MarkdownReaches(string text, string? other, int longLine)
    {
        var fired = new HashSet<string>(StringComparer.Ordinal);
        if (Fences(text, longLine, fired) | (other is not null && Fences(other, longLine, fired)))
            foreach (var rule in MarkdownFences) fired.UnionWith(rule.Scopes);
        if (MayHoldHtml(text) || other is not null && MayHoldHtml(other)) fired.Add(HtmlDerivative);
        if (MayOpenFrontMatter(text) || other is not null && MayOpenFrontMatter(other)) fired.Add(Yaml);
        return fired;
    }

    private static bool MayHoldHtml(string text) => text.AsSpan().IndexOfAny('<', '&') >= 0;

    private static bool MayOpenFrontMatter(string text) =>
        text.StartsWith("---", StringComparison.Ordinal) || text.Contains("\n---", StringComparison.Ordinal);

    /// <summary>
    /// Adds to <paramref name="found"/> the grammars of the fence rules that could start in
    /// <paramref name="text"/>. A fence rule starts at a run of three or more backticks or tildes, at a
    /// line's start or wherever a block quote or a list leaves off, so every run anywhere is read. True
    /// when a run is followed by a character the scan cannot read, and any fence rule may start there.
    /// </summary>
    private static bool Fences(string text, int longLine, HashSet<string> found)
    {
        var all = text.AsSpan();
        bool unsure = false;
        int lineStart = 0, from = 0;
        while (from < all.Length)
        {
            int hit = all[from..].IndexOfAny('`', '~');
            if (hit < 0) break;
            int start = from + hit, end = start + 1;
            while (end < all.Length && all[end] == all[start]) end++;
            if (all[from..start].LastIndexOf('\n') is int nl and >= 0) lineStart = from + nl + 1;
            from = end;
            if (end - start < 3) continue;

            // The line as the tokenize reads it: no '\r' before its '\n', and a long one cut short.
            int lineEnd = all[end..].IndexOf('\n') is int next and >= 0 ? end + next : all.Length;
            if (lineEnd > end && all[lineEnd - 1] == '\r') lineEnd--;
            var line = all[lineStart..lineEnd];
            unsure |= FencesAt(line, start - lineStart, end - lineStart, found);
            if (line.Length > longLine)
            {
                var cut = line[..(char.IsHighSurrogate(line[longLine - 1]) ? longLine - 1 : longLine)];
                int runEnd = Math.Min(end - lineStart, cut.Length);
                if (runEnd - (start - lineStart) >= 3) unsure |= FencesAt(cut, start - lineStart, runEnd, found);
            }
        }
        return unsure;
    }

    // Under (?i) Oniguruma folds these to ASCII letters ('ſ' to s, the Kelvin sign to k, 'ß' to ss, the
    // ligatures to ff, fi, fl, ffi, ffl, st), so one may stand for letters of a term. No other character
    // folds to ASCII alone.
    private static readonly SearchValues<char> FoldToAscii =
        SearchValues.Create("ſKßẞﬀﬁﬂﬃﬄﬅﬆ");

    /// <summary>
    /// The fence rules that could start at the run <c>[start, end)</c> of <paramref name="line"/>: the
    /// rule's start, <c>(`{3,}|~{3,})\s*(?i:(terms)((\s+|:|\{|\?)[^`~]*)?$)</c>, read from every place a
    /// term could begin, since the run and the spaces after it may give characters back to it. True when
    /// the rest of the line holds a character that folds to ASCII letters.
    /// </summary>
    private static bool FencesAt(ReadOnlySpan<char> line, int start, int end, HashSet<string> found)
    {
        if (line[end..].IndexOfAny(FoldToAscii) >= 0) return true;
        int first = end;
        while (first < line.Length && char.IsWhiteSpace(line[first])) first++;
        foreach (var rule in MarkdownFences)
        {
            if (Holds(found, rule.Scopes)) continue;
            foreach (var term in rule.Terms)
            {
                bool named = false;
                for (int at = start + 3; at <= first && !named; at++) named = TermAt(line, at, term);
                if (!named) continue;
                foreach (var scope in rule.Scopes) found.Add(scope);
                break;
            }
        }
        return false;
    }

    private static bool Holds(HashSet<string> set, string[] items)
    {
        foreach (var item in items)
            if (!set.Contains(item)) return false;
        return true;
    }

    /// <summary>
    /// Whether <paramref name="term"/> matches at <paramref name="at"/> and the rest of the line can end
    /// a fence's info string: <c>((\s+|:|\{|\?)[^`~]*)?$</c>. A term is a fence rule's alternative as the
    /// grammar spells it: letters (any case), <c>\</c> before a character meant as itself, <c>.</c> for
    /// any one character and <c>.+?</c> for one or more.
    /// </summary>
    internal static bool TermAt(ReadOnlySpan<char> line, int at, ReadOnlySpan<char> term)
    {
        while (!term.IsEmpty)
        {
            if (term[0] == '.')
            {
                if (at >= line.Length) return false;
                at += CharacterAt(line, at);
                if (!term.StartsWith(".+?")) { term = term[1..]; continue; }
                for (term = term[3..]; ; at += CharacterAt(line, at))
                {
                    if (TermAt(line, at, term)) return true;
                    if (at >= line.Length) return false;
                }
            }
            int length = term[0] == '\\' ? 2 : 1;
            char want = term[length - 1];
            term = term[length..];
            if (at >= line.Length || !SameLetter(line[at], want)) return false;
            at++;
        }
        var rest = line[at..];
        return rest.IsEmpty
            || (rest[0] is ':' or '{' or '?' || char.IsWhiteSpace(rest[0])) && rest[1..].IndexOfAny('`', '~') < 0;
    }

    private static int CharacterAt(ReadOnlySpan<char> line, int at) =>
        char.IsHighSurrogate(line[at]) && at + 1 < line.Length && char.IsLowSurrogate(line[at + 1]) ? 2 : 1;

    private static bool SameLetter(char c, char want) =>
        c == want || char.IsAsciiLetter(want) && char.IsAsciiLetter(c) && (c | 0x20) == (want | 0x20);
}
