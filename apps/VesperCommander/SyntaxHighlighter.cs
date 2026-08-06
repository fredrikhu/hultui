using System.Diagnostics;
using HultUI.Core.Primitives;
using HultUI.Core.Rendering;
using TextMateSharp.Grammars;
using TextMateSharp.Registry;
using TextMateSharp.Themes;
using TmTheme = TextMateSharp.Themes.Theme;

namespace VesperCommander;

/// <summary>
/// Syntax tokenizer backed by TextMateSharp (VS Code's TextMate grammars, Dark+
/// theme) — the same engine and palette the GitClient/Junction diff uses, so code
/// previews read with real editor colors rather than a heuristic tint.
///
/// <para>Text is tokenized in full (per-line state carries block comments and
/// multi-line strings across line breaks) and returned as per-line colored spans.
/// Tokens the theme leaves at the default editor foreground produce no span, so
/// plain identifiers keep the element's own text color.</para>
///
/// <para>Thread-safe via a single gate; the preview excerpt is small (≤500 short
/// lines) so a synchronous tokenize is cheap, but callers still memoize per file.</para>
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

    // Guards against pathological inputs (minified bundles, generated code):
    // oversized files skip highlighting entirely, and a file that tokenizes
    // slowly gets whatever lines fit in the budget (the rest stay uncolored).
    private const int MaxChars = 2_000_000;
    private const int MaxLineCount = 20_000;
    private static readonly TimeSpan LineBudget = TimeSpan.FromMilliseconds(250);
    private static readonly TimeSpan FileBudget = TimeSpan.FromSeconds(3);

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
            var grammar = GrammarFor(System.IO.Path.GetExtension(path));
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

    private static IGrammar? GrammarFor(string ext)
    {
        if (string.IsNullOrEmpty(ext)) return null;
        EnsureRegistry();
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
