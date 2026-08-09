using System.Diagnostics;
using HultUI.Core.Primitives;
using HultUI.Core.Rendering;
using TextMateSharp.Grammars;
using TextMateSharp.Registry;
using TextMateSharp.Themes;
using TmTheme = TextMateSharp.Themes.Theme;   // "Theme" alone collides with HultUI.Core.Styling.Theme

namespace HultUI.Syntax;

/// <summary>
/// Syntax tokenizer backed by TextMateSharp (VS Code's TextMate grammars, Dark+
/// theme), shared by every HultUI app that shows code — Vesper Commander's file
/// preview and Junction's diff both run through here, so a file colours the same way
/// in both.
///
/// <para>Text is tokenized in full — per-line state carries block comments and
/// multi-line strings across line breaks, so a diff hunk alone would mis-colour — and
/// returned as per-line coloured spans ready for <c>TextElement.Spans</c>. Tokens the
/// theme leaves at the default editor foreground produce no span, so plain
/// identifiers keep the element's own text colour (a diff row's add/delete tint stays
/// visible between keywords).</para>
///
/// <para>Thread-safe via a single gate. Call it from a background thread: a large
/// file tokenizes in the low hundreds of milliseconds, and callers memoize per file.</para>
/// </summary>
public static class SyntaxHighlighter
{
    private static readonly object Gate = new();
    private static RegistryOptions? _options;
    private static Registry? _registry;
    private static TmTheme? _theme;
    private static Color? _defaultFg;
    private static readonly Dictionary<string, IGrammar?> GrammarByExt = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<int, Color?> ColorById = new();
    private static readonly Dictionary<string, bool> CanHighlightByExt = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Extensions absent from TextMateSharp's bundled language table that are
    /// nonetheless plain instances of a language it does carry. VS Code resolves most
    /// of these through language contributions the bundle doesn't ship, so they arrive
    /// here unhighlighted; pointing them at the grammar that genuinely parses them
    /// costs one dictionary hit.
    ///
    /// <para>Only exact aliases belong here. A format that merely <em>resembles</em> a
    /// bundled language — TOML against INI, say — would mis-colour more than it
    /// coloured, so it stays plain instead. Note that <c>.csproj</c>, <c>.targets</c>,
    /// <c>.props</c>, and the PowerShell extensions need no entry: the bundle already
    /// lists them.</para>
    /// </summary>
    private static readonly Dictionary<string, string> ExtensionAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        // XML dialects
        [".slnx"] = ".xml",            // the XML solution format
        [".config"] = ".xml",          // app/web/packages/NuGet.config
        [".resx"] = ".xml",
        [".manifest"] = ".xml",
        [".runsettings"] = ".xml",
        [".ruleset"] = ".xml",
        [".natvis"] = ".xml",
        [".vsixmanifest"] = ".xml",
        [".plist"] = ".xml",
        [".xliff"] = ".xml",

        // JSON dialects
        [".slnf"] = ".json",           // solution filter
        [".jsonl"] = ".json",
        [".ndjson"] = ".json",
        [".code-workspace"] = ".jsonc",   // comments permitted
    };

    /// <summary>
    /// Files that carry their language in the name rather than an extension. Without
    /// this a file manager shows <c>Dockerfile</c> and <c>Makefile</c> — two of the
    /// most-opened files in any repository — as flat grey.
    /// </summary>
    private static readonly Dictionary<string, string> FileNameAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Dockerfile"] = ".dockerfile",
        ["Containerfile"] = ".containerfile",
        ["Makefile"] = ".mak",
        ["GNUmakefile"] = ".mak",
        ["Jenkinsfile"] = ".jenkinsfile",
        ["Rakefile"] = ".rb",
        ["Gemfile"] = ".rb",
        ["Podfile"] = ".rb",
        ["Vagrantfile"] = ".rb",
    };

    // Guards against pathological inputs (minified bundles, generated code):
    // oversized files skip highlighting entirely, and a file that tokenizes
    // slowly gets whatever lines fit in the budget (the rest stay uncolored).
    private const int MaxChars = 2_000_000;
    private const int MaxLineCount = 20_000;
    private static readonly TimeSpan LineBudget = TimeSpan.FromMilliseconds(250);
    private static readonly TimeSpan FileBudget = TimeSpan.FromSeconds(3);

    /// <summary>
    /// Whether a grammar exists for <paramref name="path"/> — i.e. whether tokenizing
    /// it could produce colour at all.
    ///
    /// <para>Callers that need to decide up front whether to take the highlighting
    /// path should ask this rather than keep a private list of "code" extensions:
    /// such a list silently diverges from the grammar set, which is exactly how
    /// <c>.slnx</c>, <c>.csproj</c>, and <c>.ps1</c> ended up highlighting in one app
    /// and not the other.</para>
    /// </summary>
    public static bool CanHighlight(string path)
    {
        var ext = ResolveExtension(path);
        if (ext is null) return false;
        lock (Gate)
        {
            if (CanHighlightByExt.TryGetValue(ext, out var known)) return known;
            EnsureRegistry();
            // Scope lookup only — deliberately NOT LoadGrammar. A file list asks this
            // once per visible row per frame, and loading a grammar means parsing a
            // sizeable JSON on the UI thread for a language that may never be shown.
            // The real load stays deferred to the first tokenize.
            bool can = _options!.GetScopeByExtension(ext) != null;
            CanHighlightByExt[ext] = can;
            return can;
        }
    }

    /// <summary>
    /// Tokenizes <paramref name="text"/> as the language implied by
    /// <paramref name="path"/>'s extension. Returns one span list per line
    /// (empty for lines with nothing to color; offsets are line-relative), or
    /// null when the extension has no grammar or the file is too large.
    /// </summary>
    public static IReadOnlyList<TextSpan>[]? TokenizeLines(string path, string text)
    {
        if (text.Length > MaxChars) return null;
        lock (Gate)
        {
            var grammar = GrammarFor(path);
            return grammar == null ? null : Tokenize(grammar, text);
        }
    }

    /// <summary>
    /// Flatten the per-line tokenization into a single span list whose offsets are
    /// absolute within <paramref name="text"/> — a drop-in for a Text/TextArea that
    /// holds the whole excerpt. Assumes '\n' line breaks (the preview loader
    /// normalizes to that). Returns an empty list when the language is unsupported.
    /// </summary>
    public static IReadOnlyList<TextSpan> SpansForText(string path, string text)
    {
        var perLine = TokenizeLines(path, text);
        if (perLine == null) return Array.Empty<TextSpan>();

        var flat = new List<TextSpan>();
        int lineStart = 0, li = 0, n = text.Length;
        while (li < perLine.Length)
        {
            int nl = text.IndexOf('\n', lineStart);
            int lineEnd = nl < 0 ? n : nl;
            foreach (var s in perLine[li])
            {
                int abs = lineStart + s.Start;
                if (abs >= lineStart && abs + s.Length <= lineEnd)   // stay within the line
                    flat.Add(new TextSpan(abs, s.Length, s.Color, s.Background));
            }
            if (nl < 0) break;
            lineStart = nl + 1;
            li++;
        }
        return flat;
    }

    // ── Registry / grammar resolution (all under Gate) ───────────────────

    private static void EnsureRegistry()
    {
        if (_registry != null) return;
        _options = new RegistryOptions(ThemeName.DarkPlus);
        _registry = new Registry(_options);
        _theme = _registry.GetTheme();
        if (_theme.GetGuiColorDictionary().TryGetValue("editor.foreground", out var fg))
            _defaultFg = ParseHex(fg);
    }

    /// <summary>
    /// The canonical extension to look a grammar up by: the file's own extension, its
    /// alias, or — for extension-less files — the one its name implies. Null when
    /// nothing maps. Pure string work, so both the predicate and the loader share it
    /// without either touching the registry.
    /// </summary>
    private static string? ResolveExtension(string path)
    {
        var ext = System.IO.Path.GetExtension(path);
        if (ext.Length == 0)
            return FileNameAliases.TryGetValue(System.IO.Path.GetFileName(path), out var byName) ? byName : null;
        return ExtensionAliases.TryGetValue(ext, out var alias) ? alias : ext;
    }

    /// <summary>
    /// The grammar for <paramref name="path"/>, resolved from its extension, then
    /// from an alias, then — for extension-less files — from its name. Null when
    /// nothing bundled parses it.
    /// </summary>
    private static IGrammar? GrammarFor(string path)
    {
        if (ResolveExtension(path) is not { } ext) return null;
        EnsureRegistry();
        // Keyed by the resolved extension, so every alias shares one cached grammar.
        if (GrammarByExt.TryGetValue(ext, out var grammar)) return grammar;
        var scope = _options!.GetScopeByExtension(ext);
        grammar = scope == null ? null : _registry!.LoadGrammar(scope);
        GrammarByExt[ext] = grammar;
        return grammar;
    }

    // ── Tokenization ─────────────────────────────────────────────────────

    private static IReadOnlyList<TextSpan>[]? Tokenize(IGrammar grammar, string text)
    {
        var lines = SplitLines(text);
        if (lines.Count > MaxLineCount) return null;

        var result = new IReadOnlyList<TextSpan>[lines.Count];
        IStateStack? state = null;
        var sw = Stopwatch.StartNew();
        var spans = new List<TextSpan>(32);

        for (int i = 0; i < lines.Count; i++)
        {
            result[i] = [];
            if (sw.Elapsed > FileBudget) continue;

            ITokenizeLineResult tokenized;
            try { tokenized = grammar.TokenizeLine(new LineText(lines[i]), state, LineBudget); }
            catch { continue; }   // a broken grammar rule shouldn't kill the whole file
            state = tokenized.RuleStack;

            int lineLength = lines[i].Length;
            spans.Clear();
            foreach (var token in tokenized.Tokens)
            {
                int s = Math.Clamp(token.StartIndex, 0, lineLength);
                int e = Math.Clamp(token.EndIndex, s, lineLength);
                if (e <= s) continue;
                if (ColorFor(token.Scopes) is not { } color) continue;

                // Merge contiguous same-color tokens so the renderer draws fewer segments.
                if (spans.Count > 0 && spans[^1].End == s && spans[^1].Color == color)
                    spans[^1] = spans[^1] with { Length = spans[^1].Length + (e - s) };
                else
                    spans.Add(new TextSpan(s, e - s, color));
            }
            if (spans.Count > 0) result[i] = spans.ToArray();
        }
        return result;
    }

    private static Color? ColorFor(List<string> scopes)
    {
        foreach (var rule in _theme!.Match(scopes))
        {
            if (rule.foreground <= 0) continue;
            if (!ColorById.TryGetValue(rule.foreground, out var color))
            {
                color = ParseHex(_theme.GetColor(rule.foreground));
                ColorById[rule.foreground] = color;
            }
            if (color is not { } c) continue;
            // The default editor foreground carries no information — let the
            // element's own text color show through instead.
            return c == _defaultFg ? null : c;
        }
        return null;
    }

    // Lines as slices of the source (no per-line string allocs); trailing '\r' is
    // stripped so offsets line up with the raw line content.
    private static List<ReadOnlyMemory<char>> SplitLines(string text)
    {
        var lines = new List<ReadOnlyMemory<char>>(256);
        int start = 0;
        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] != '\n') continue;
            int end = i > start && text[i - 1] == '\r' ? i - 1 : i;
            lines.Add(text.AsMemory(start, end - start));
            start = i + 1;
        }
        if (start < text.Length)
        {
            int end = text.Length;
            if (end > start && text[end - 1] == '\r') end--;
            lines.Add(text.AsMemory(start, end - start));
        }
        return lines;
    }

    private static Color? ParseHex(string? hex)
    {
        if (hex == null || hex.Length < 7 || hex[0] != '#') return null;
        try
        {
            byte r = Convert.ToByte(hex.Substring(1, 2), 16);
            byte g = Convert.ToByte(hex.Substring(3, 2), 16);
            byte b = Convert.ToByte(hex.Substring(5, 2), 16);
            byte a = hex.Length >= 9 ? Convert.ToByte(hex.Substring(7, 2), 16) : (byte)255;
            return new Color(r, g, b, a);
        }
        catch (FormatException) { return null; }
    }
}
