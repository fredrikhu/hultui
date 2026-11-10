using System.Buffers;
using System.Collections;
using System.Text.RegularExpressions;

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
/// <para>Markdown, HTML, the shell, Ruby and F# have scans; the last three reach Markdown (a heredoc, a
/// doc comment) and with it every other grammar, so they scan their own texts too. A scan reads the
/// whole text, wherever the grammar is embedded in it, and may answer too much, never too little: a
/// grammar it cannot rule out is handed out. A grammar with no scan reaches every grammar it names.</para>
/// </summary>
internal static partial class EmbeddedLanguages
{
    internal const string Markdown = "text.html.markdown";
    internal const string HtmlDerivative = "text.html.derivative";
    internal const string Html = "text.html.basic";
    internal const string Yaml = "source.yaml";
    internal const string Shell = "source.shell";
    internal const string Ruby = "source.ruby";
    internal const string FSharp = "source.fsharp";
    internal const string Css = "source.css";
    internal const string JavaScript = "source.js";

    /// <summary>Whether a text in <paramref name="scope"/> gets a registry of the grammars it reaches,
    /// rather than the one every other grammar shares: the grammars that reach Markdown, and with it
    /// every other, through a fence, a heredoc or a doc comment.</summary>
    public static bool Scans(string scope) => scope is Markdown or Shell or Ruby or FSharp;

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
            var (covers, fired) = Decided(scope, text, other, longLine);
            foreach (var name in namesOf(scope)!)
            {
                if (covers is not null && covers.Contains(name) && !fired!.Contains(name)) continue;
                if (namesOf(name) is not null && reached.Add(name)) queue.Enqueue(name);
            }
        }
        return reached;
    }

    /// <summary>For a grammar with a scan, the grammars it decides and, of those, the ones it found a
    /// way into in these texts; nulls for a grammar with none, which reaches all it names.</summary>
    private static (HashSet<string>? Covers, HashSet<string>? Fired) Decided(string scope, string text, string? other,
        int longLine) => scope switch
    {
        Markdown => (MarkdownCovers, MarkdownReaches(text, other, longLine)),
        Shell => (ShellCovers, Both(text, other, ShellReaches)),
        Ruby => (RubyCovers, Both(text, other, RubyReaches)),
        FSharp => (FSharpCovers, Both(text, other, FSharpReaches)),
        Html => (HtmlCovers, Both(text, other, HtmlReaches)),
        _ => (null, null),
    };

    private static HashSet<string> Both(string text, string? other, Action<string, HashSet<string>> scan)
    {
        var fired = new HashSet<string>(StringComparer.Ordinal);
        scan(text, fired);
        if (other is not null) scan(other, fired);
        return fired;
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

        // A raw grammar is TextMateSharp's Raw, a Dictionary<string, object>, and lists of them: walked
        // with their own enumerators, a node costs nothing (through the interfaces, 3.6 MB for all 78).
        void Walk(object node)
        {
            switch (node)
            {
                case Dictionary<string, object> rule:
                    foreach (var (key, value) in rule) Visit(key, value);
                    break;
                case IDictionary<string, object> rule:
                    foreach (var (key, value) in rule) Visit(key, value);
                    break;
                case List<object> items:
                    foreach (var item in items) Visit("", item);
                    break;
                case IEnumerable items:
                    foreach (var item in items) Visit("", item);
                    break;
            }
        }

        void Visit(string key, object? value)
        {
            if (key == "include" && value is string include)
            {
                if (include.Length == 0 || include[0] == '#' || include is "$self" or "$base") return;
                int hash = include.IndexOf('#');
                var scope = hash < 0 ? include : include[..hash];
                if (!names.Contains(scope)) names.Add(scope);
            }
            else if (value is not null and not string) Walk(value);
        }
    }

    // ── Markdown ──────────────────────────────────────────────────────────
    //
    // Markdown reaches a fence language through its fence rule, whose start names it; HTML through a
    // tag, a comment or an entity, each of which starts with '<' or '&';
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

    // ── Shell, Ruby, F#, HTML ─────────────────────────────────────────────
    //
    // Each enters the languages it embeds through rules that a short literal starts, so its scan looks
    // for that literal. A test holds each table to the rules of its grammar file.

    /// <summary>The shell grammar's heredocs that embed a language, <c>(&lt;&lt;)-?\s*("|'|)\s*(NAME)</c>
    /// and a delimiter after it, by NAME (in capitals, as it must be written), and the grammar each
    /// includes.</summary>
    internal static readonly (string Name, string Scope)[] ShellHeredocs =
    [
        ("RUBY", Ruby), ("PYTHON", "source.python"), ("APPLESCRIPT", "source.applescript"), ("HTML", Html),
        ("MARKDOWN", Markdown), ("TEXTILE", "text.html.textile"), ("SHELL", Shell),
    ];

    internal static readonly HashSet<string> ShellCovers = [.. ShellHeredocs.Select(h => h.Scope)];

    private static void ShellReaches(string text, HashSet<string> fired)
    {
        var all = text.AsSpan();
        for (int at = all.IndexOf("<<"); at >= 0; at = Next(all, at + 1, "<<"))
        {
            int i = at + 2;
            if (i < all.Length && all[i] == '-') i++;
            i = SkipSpaces(all, i);
            if (i < all.Length && all[i] is '"' or '\'') i = SkipSpaces(all, i + 1);
            foreach (var (name, scope) in ShellHeredocs)
                if (all[i..].StartsWith(name, StringComparison.Ordinal)) fired.Add(scope);
        }
    }

    /// <summary>Ruby's heredocs that embed a language,
    /// <c>(?=(?&gt;&lt;&lt;[-~]("?)((?:[_\w]+_|)NAME)\b\1))</c>, by NAME (JavaScript and the shell have
    /// two), and the grammar each includes.</summary>
    internal static readonly (string Name, string Scope)[] RubyHeredocs =
    [
        ("HTML", Html), ("XML", "text.xml"), ("SQL", "source.sql"), ("CSS", Css), ("CPP", "source.c++"),
        ("C", "source.c"), ("JS", JavaScript), ("JAVASCRIPT", JavaScript), ("JQUERY", "source.js.jquery"),
        ("SH", Shell), ("SHELL", Shell), ("LUA", "source.lua"), ("RUBY", Ruby),
    ];

    /// <summary>Ruby's data section, <c>^__END__\n</c>, takes HTML when markup follows.</summary>
    internal const string RubyDataSection = "__END__";

    internal static readonly HashSet<string> RubyCovers = [.. RubyHeredocs.Select(h => h.Scope), Html];

    private static void RubyReaches(string text, HashSet<string> fired)
    {
        var all = text.AsSpan();
        if (all.IndexOf(RubyDataSection) >= 0) fired.Add(Html);
        for (int at = all.IndexOf("<<"); at >= 0; at = Next(all, at + 1, "<<"))
        {
            int i = at + 2;
            if (i >= all.Length || all[i] is not ('-' or '~')) continue;
            if (++i < all.Length && all[i] == '"') i++;
            // The heredoc's word, to the first ASCII character no word holds: past ASCII any may be one.
            int end = i;
            while (end < all.Length && (all[end] > 0x7F || char.IsAsciiLetterOrDigit(all[end]) || all[end] == '_')) end++;
            foreach (var (name, scope) in RubyHeredocs)
                if (EndsAWord(all[i..end], name)) fired.Add(scope);
        }
    }

    /// <summary>Whether <paramref name="name"/> stands in <paramref name="word"/> at its start or after
    /// '_', and ends it, or ends its ASCII where a character past ASCII may end the word.</summary>
    private static bool EndsAWord(ReadOnlySpan<char> word, string name)
    {
        for (int at = word.IndexOf(name); at >= 0; at = Next(word, at + 1, name))
            if ((at == 0 || word[at - 1] == '_') && (at + name.Length == word.Length || word[at + name.Length] > 0x7F))
                return true;
        return false;
    }

    /// <summary>F#'s doc comments, a <c>///</c> line and a <c>(**</c> block, take Markdown.</summary>
    internal static readonly string[] FSharpDocComments = ["///", "(**"];

    internal static readonly HashSet<string> FSharpCovers = [Markdown];

    private static void FSharpReaches(string text, HashSet<string> fired)
    {
        foreach (var opens in FSharpDocComments)
            if (text.Contains(opens, StringComparison.Ordinal)) fired.Add(Markdown);
    }

    // HTML takes CSS in a <style> element, and JavaScript in a <script> element or an event handler's
    // attribute (onclick="..."). The two tag names match in any case, as (?i:style) and (?i:script).

    /// <summary>The event handlers' attribute names, as the HTML grammar's rule for them starts, less its
    /// closing <c>(?![\w:-])</c>. Letters as written: the rule ignores no case.</summary>
    internal const string HtmlEventHandlers =
        "on(s(croll|t(orage|alled)|u(spend|bmit)|e(curitypolicyviolation|ek(ing|ed)|lect))|hashchange|"
        + "c(hange|o(ntextmenu|py)|u(t|echange)|l(ick|ose)|an(cel|play(through)?))|t(imeupdate|oggle)|"
        + "in(put|valid)|o(nline|ffline)|d(urationchange|r(op|ag(start|over|e(n(ter|d)|xit)|leave)?)|blclick)|"
        + "un(handledrejection|load)|p(opstate|lay(ing)?|a(ste|use|ge(show|hide))|rogress)|e(nded|rror|mptied)|"
        + "volumechange|key(down|up|press)|focus|w(heel|aiting)|l(oad(start|e(nd|d(data|metadata)))?|anguagechange)|"
        + "a(uxclick|fterprint|bort)|r(e(s(ize|et)|jectionhandled)|atechange)|"
        + "m(ouse(o(ut|ver)|down|up|enter|leave|move)|essage(error)?)|b(efore(unload|print)|lur))";

    [GeneratedRegex(HtmlEventHandlers, RegexOptions.CultureInvariant)]
    private static partial Regex EventHandler();

    internal static readonly HashSet<string> HtmlCovers = [Css, JavaScript];

    private static void HtmlReaches(string text, HashSet<string> fired)
    {
        if (OpensTag(text, "style")) fired.Add(Css);
        if (OpensTag(text, "script") || EventHandler().IsMatch(text)) fired.Add(JavaScript);
    }

    /// <summary>Whether a '&lt;' is followed by <paramref name="tag"/> in any case, or by a character past
    /// ASCII where one could stand for its letters (Oniguruma folds 'ſ' to s, 'ﬆ' to st).</summary>
    private static bool OpensTag(string text, string tag)
    {
        var all = text.AsSpan();
        for (int at = all.IndexOf('<'); at >= 0; at = Next(all, at + 1, '<'))
        {
            var after = all[(at + 1)..Math.Min(all.Length, at + 1 + tag.Length)];
            foreach (var c in after)
                if (c > 0x7F) return true;
            if (after.Equals(tag, StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }

    private static int Next(ReadOnlySpan<char> text, int from, ReadOnlySpan<char> what) =>
        from > text.Length ? -1 : text[from..].IndexOf(what) is int at and >= 0 ? from + at : -1;

    private static int Next(ReadOnlySpan<char> text, int from, char what) =>
        from > text.Length ? -1 : text[from..].IndexOf(what) is int at and >= 0 ? from + at : -1;

    private static int SkipSpaces(ReadOnlySpan<char> text, int at)
    {
        while (at < text.Length && char.IsWhiteSpace(text[at])) at++;
        return at;
    }
}
