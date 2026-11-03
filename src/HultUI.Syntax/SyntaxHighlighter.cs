using System.Diagnostics;
using HultUI.Core.Primitives;
using HultUI.Core.Rendering;
using TextMateSharp.Grammars;
using TextMateSharp.Registry;
using TextMateSharp.Themes;
using TmTheme = TextMateSharp.Themes.Theme;   // "Theme" alone collides with HultUI.Core.Styling.Theme

namespace HultUI.Syntax;

/// <summary>Lines of a text, 1-based, both ends included.</summary>
internal readonly record struct LineRange(int First, int Last);

/// <summary>What <see cref="SyntaxHighlighter.TokenizeDiff"/> made of two versions of a file: each
/// side's spans by line, null when it has none, and how many lines it walked.</summary>
internal readonly record struct TokenizedDiff(IReadOnlyList<TextSpan>[]? Old, IReadOnlyList<TextSpan>[]? New,
    int LinesWalked);

/// <summary>
/// Syntax tokenizer backed by TextMateSharp (VS Code's TextMate grammars), shared by
/// every HultUI app that shows code — Vesper Commander's file preview and Junction's
/// diff both run through here, so a file colours the same way in both. Which theme
/// colours it is process-wide and starts as Dark+; see <see cref="SetTheme"/>.
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

    /// <summary>How many times this thread has needed the registry, loaded or not. A test
    /// reads it to show that a question answered from a table never touched it.</summary>
    [ThreadStatic] internal static int RegistryUsesOnThisThread;

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
    internal static readonly TimeSpan LineBudget = TimeSpan.FromMilliseconds(250);
    private static readonly TimeSpan FileBudget = TimeSpan.FromSeconds(3);

    // Tokenizing one line costs the square of its length (each search measures the whole
    // line): with no budget a 32,000-character line took 17 s of JavaScript and 27 s of C#. A
    // line longer than LongLine is coloured on its first LongLine characters only, and the rest
    // draws plain; 2,000 dense characters take 80 ms of JavaScript, 130 ms of C#. JSON and XML
    // tokenize about 17 times cheaper, and 8,000 of theirs take 70 and 40 ms. Like a line
    // stopped at its budget, a cut line leaves the next one the state it started in.
    internal const int LongLine = 2_000;
    internal const int LongDataLine = 8_000;

    /// <summary>
    /// The most lines <see cref="TokenizeLines(string, string)"/> colours: a text with more
    /// gets no spans at all. A caller that tokenizes on a worker and can stop a tokenize it
    /// no longer wants may pass its own cap to
    /// <see cref="TokenizeLines(string, string, int, CancellationToken, TimeProvider?)"/>; texts
    /// are still held to 2,000,000 characters and three seconds, after which the remaining lines
    /// stay uncoloured. A line longer than 2,000 characters (8,000 in JSON and XML) is coloured
    /// on those first characters only and the rest of it draws plain: a line's tokenize costs the
    /// square of its length.
    /// </summary>
    public const int DefaultMaxLines = 20_000;

    /// <summary>
    /// Whether a grammar exists for <paramref name="path"/> — i.e. whether tokenizing
    /// it could produce colour at all.
    ///
    /// <para>Callers that need to decide up front whether to take the highlighting
    /// path should ask this rather than keep a private list of "code" extensions:
    /// such a list silently diverges from the grammar set, which is exactly how
    /// <c>.slnx</c>, <c>.csproj</c>, and <c>.ps1</c> ended up highlighting in one app
    /// and not the other.</para>
    ///
    /// <para>A file list asks once per visible row per frame, so this is answered from a
    /// table of the bundled grammars' extensions (<see cref="BundledGrammarExtensions"/>):
    /// it neither loads the registry — 5 MB and up to 10 ms on the UI thread, the first
    /// time — nor takes the gate a running tokenize holds.</para>
    /// </summary>
    public static bool CanHighlight(string path) =>
        ResolveExtension(path) is { } ext && BundledGrammarExtensions.All.Contains(ext);

    /// <summary>
    /// Tokenizes <paramref name="text"/> as the language implied by
    /// <paramref name="path"/>'s extension. Returns one span list per line
    /// (empty for lines with nothing to color; offsets are line-relative), or
    /// null when the extension has no grammar or the file is too large.
    /// </summary>
    public static IReadOnlyList<TextSpan>[]? TokenizeLines(string path, string text) =>
        TokenizeLines(path, text, DefaultMaxLines, CancellationToken.None);

    /// <summary>
    /// <see cref="TokenizeLines(string, string)"/> for a caller on a worker: a text of more
    /// than <paramref name="maxLines"/> lines gets null, and <paramref name="cancellationToken"/>
    /// is checked before every line, so a tokenize nobody wants any more — a newer response,
    /// a closed tab — throws <see cref="OperationCanceledException"/> within a line and lets
    /// the next one have the gate. Tokenizes run one at a time.
    ///
    /// <para><paramref name="clock"/> is what the three-second budget is measured on, the system
    /// clock when null. An app passes its <c>Application.Time</c>: under a headless session that
    /// clock stands still inside a frame, so no budget runs out there, and a test sees the whole
    /// text's colour on every run rather than however much of it a busy machine got through.
    /// TextMateSharp times each line on the wall clock itself, so on any clock but the system's
    /// a line has no budget of its own either.</para>
    /// </summary>
    public static IReadOnlyList<TextSpan>[]? TokenizeLines(string path, string text, int maxLines,
        CancellationToken cancellationToken, TimeProvider? clock = null) =>
        TokenizeLines(path, text, maxLines, cancellationToken, clock, wrap: null);

    /// <summary><see cref="TokenizeLines(string, string, int, CancellationToken, TimeProvider?)"/>
    /// through a wrapper of the grammar, which a test uses to make a line run out of its
    /// budget.</summary>
    internal static IReadOnlyList<TextSpan>[]? TokenizeLines(string path, string text, int maxLines,
        CancellationToken cancellationToken, TimeProvider? clock, Func<IGrammar, IGrammar>? wrap)
    {
        if (text.Length > MaxChars) return null;
        cancellationToken.ThrowIfCancellationRequested();
        lock (Gate)
        {
            var grammar = GrammarFor(path);
            if (grammar == null) return null;
            return Tokenize(wrap is null ? grammar : wrap(grammar), text, maxLines,
                clock ?? TimeProvider.System, cancellationToken);
        }
    }

    /// <summary>
    /// Two versions of one file tokenized for a diff, which shows a few lines of each. A line
    /// needs every line above it, for the state they leave (a block comment opened 900 lines
    /// up), but a diff needs nothing below its last line, and of the lines above it only that
    /// state. So each side stops at its last shown line; a line nobody shows is walked for its
    /// state, which saves about a fifth of its bytes and leaves no spans to keep (a diff's lines
    /// are a few in a hundred); and the lines both versions start with are walked once. On the
    /// spans of a line shown this is <see cref="TokenizeLines(string, string, int, CancellationToken, TimeProvider?)"/>,
    /// and it holds a side to the same limits: a text too long for that gets none here.
    ///
    /// <para>A side's array ends at its last shown line, and holds spans only for lines shown.
    /// A side with no text, or nothing shown, is null.</para>
    /// </summary>
    /// <param name="oldShown">The old side's lines to colour, in rising order.</param>
    /// <param name="newShown">The new side's, likewise.</param>
    internal static TokenizedDiff TokenizeDiff(string path, string? oldText, IReadOnlyList<LineRange> oldShown,
        string? newText, IReadOnlyList<LineRange> newShown, int maxLines, CancellationToken cancellationToken,
        TimeProvider clock)
    {
        if (oldShown.Count == 0 || oldText is not null && TooLong(oldText, maxLines)) oldText = null;
        if (newShown.Count == 0 || newText is not null && TooLong(newText, maxLines)) newText = null;
        if (oldText is null && newText is null) return default;
        cancellationToken.ThrowIfCancellationRequested();
        lock (Gate)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var grammar = GrammarFor(path);
            if (grammar == null) return default;

            var oldLines = oldText is null ? [] : SplitLines(oldText, oldShown[^1].Last);
            var newLines = newText is null ? [] : SplitLines(newText, newShown[^1].Last);
            var oldSide = new Side(oldText is null ? null : Uncoloured(oldLines.Count), oldShown);
            var newSide = new Side(newText is null ? null : Uncoloured(newLines.Count), newShown);
            var longer = newLines.Count >= oldLines.Count ? newLines : oldLines;
            int shared = oldText is null || newText is null ? 0 : Math.Min(SharedLines(oldText, newText), longer.Count);

            var walk = new LineWalk(grammar, clock, cancellationToken);
            IStateStack? state = null;
            walk.Run(longer, 0, shared, ref state, ref oldSide, ref newSide);
            var elapsed = clock.GetElapsedTime(walk.Started);
            var sharedState = state;
            walk.Run(newLines, shared, newLines.Count, ref state, ref newSide, ref Side.None);
            // Each version has its whole budget, as it would alone, less the walk they shared.
            walk.Started = clock.GetTimestamp() - (long)(elapsed.TotalSeconds * clock.TimestampFrequency);
            walk.Run(oldLines, shared, oldLines.Count, ref sharedState, ref oldSide, ref Side.None);
            return new TokenizedDiff(oldSide.Result, newSide.Result, walk.Lines);
        }
    }

    private static bool TooLong(string text, int maxLines) =>
        text.Length > MaxChars
        || text.AsSpan().Count('\n') + (text.Length > 0 && text[^1] != '\n' ? 1 : 0) > maxLines;

    /// <summary>How many whole lines two texts start with in common.</summary>
    private static int SharedLines(string a, string b) =>
        a.AsSpan(0, a.AsSpan().CommonPrefixLength(b)).Count('\n');

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

    private static readonly Dictionary<string, string?> ExtensionByLanguage = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// <see cref="SpansForText"/> for text whose language is named rather than implied by a
    /// file — a markdown fence's info string. The name is tried as a grammar's id, then as
    /// one of its aliases, then as an extension, so <c>csharp</c>, <c>C#</c> and <c>cs</c> all
    /// find C#, and <c>bash</c> finds the shell grammar. Empty when nothing matches.
    /// </summary>
    public static IReadOnlyList<TextSpan> SpansForLanguage(string language, string text) =>
        ExtensionForLanguage(language) is { } ext ? SpansForText("code" + ext, text) : Array.Empty<TextSpan>();

    private static string? ExtensionForLanguage(string language)
    {
        language = language.Trim();
        if (language.Length == 0 || language.IndexOfAny(['/', '\\', '.']) >= 0) return null;
        lock (Gate)
        {
            if (ExtensionByLanguage.TryGetValue(language, out var known)) return known;
            EnsureRegistry();
            string? ext = null;
            foreach (var lang in _options!.GetAvailableLanguages())
            {
                if (lang.Extensions is not { Count: > 0 } extensions) continue;
                if (string.Equals(lang.Id, language, StringComparison.OrdinalIgnoreCase)
                    || lang.Aliases?.Exists(a => string.Equals(a, language, StringComparison.OrdinalIgnoreCase)) == true)
                {
                    ext = extensions[0];
                    break;
                }
            }
            // Not a language's name, but maybe its extension spelled without the dot.
            if (ext is null && ResolveExtension("code." + language) is { } byExt && _options.GetScopeByExtension(byExt) != null)
                ext = "." + language;
            ExtensionByLanguage[language] = ext;
            return ext;
        }
    }

    // ── Registry / grammar resolution (all under Gate) ───────────────────

    private static ThemeName _themeName = ThemeName.DarkPlus;

    /// <summary>
    /// Which of TextMateSharp's bundled themes colours code from here on. Named rather than
    /// typed so callers do not need a TextMateSharp reference of their own; an unrecognised
    /// name leaves the current theme alone and answers false.
    ///
    /// <para>Process-wide, like everything else on this type. An app calls it when its own
    /// palette changes — a warm chrome around VS Code Dark+ code reads as two apps stacked
    /// on each other — and then rebuilds, since spans already handed out keep their old
    /// colours.</para>
    /// </summary>
    public static bool SetTheme(string name)
    {
        if (!Enum.TryParse<ThemeName>(name, ignoreCase: true, out var parsed)) return false;

        lock (Gate)
        {
            if (parsed == _themeName && _registry != null) return true;
            _themeName = parsed;
            // Drop the registry rather than re-theming it in place: the colour ids a rule
            // carries are indices into the theme's own colour table, so ColorById and every
            // grammar's cached tokenization belong to the theme that produced them.
            _registry = null;
            _options = null;
            _theme = null;
            _defaultFg = null;
            GrammarByExt.Clear();
            ColorById.Clear();
        }
        // Which extensions are highlightable does not depend on the theme: CanHighlight answers
        // from a fixed table.
        return true;
    }

    private static void EnsureRegistry()
    {
        RegistryUsesOnThisThread++;
        if (_registry != null) return;
        _options = new RegistryOptions(_themeName);
        _registry = new Registry(_options);
        _theme = _registry.GetTheme();
        _defaultFg = _theme.GetGuiColorDictionary().TryGetValue("editor.foreground", out var fg)
            ? ParseHex(fg)
            : null;
    }

    /// <summary>
    /// The canonical extension to look a grammar up by: the file's own extension, its
    /// alias, or — for extension-less files — the one its name implies. Null when
    /// nothing maps. Pure string work, so both the predicate and the loader share it
    /// without either touching the registry.
    /// </summary>
    internal static string? ResolveExtension(string path)
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

    private static IReadOnlyList<TextSpan>[]? Tokenize(IGrammar grammar, string text, int maxLines,
        TimeProvider clock, CancellationToken cancellationToken)
    {
        var lines = SplitLines(text);
        if (lines.Count > maxLines) return null;

        var side = new Side(Uncoloured(lines.Count), EveryLine);
        IStateStack? state = null;
        new LineWalk(grammar, clock, cancellationToken).Run(lines, 0, lines.Count, ref state, ref side, ref Side.None);
        return side.Result;
    }

    private static readonly LineRange[] EveryLine = [new(1, int.MaxValue)];

    private static IReadOnlyList<TextSpan>[] Uncoloured(int lines)
    {
        var result = new IReadOnlyList<TextSpan>[lines];
        Array.Fill(result, Array.Empty<TextSpan>());
        return result;
    }

    /// <summary>What a walk fills in: one version's result, for the lines it shows.</summary>
    private struct Side(IReadOnlyList<TextSpan>[]? result, IReadOnlyList<LineRange> shown)
    {
        public static Side None = new(null, []);

        public readonly IReadOnlyList<TextSpan>[]? Result = result;
        private int _range;

        /// <summary>Whether this side shows a line (0-based). Asked in rising order.</summary>
        public bool Shows(int line)
        {
            if (Result is null) return false;
            while (_range < shown.Count && shown[_range].Last <= line) _range++;
            return _range < shown.Count && shown[_range].First - 1 <= line;
        }
    }

    /// <summary>A walk down lines, each from the state the one above left.</summary>
    private sealed class LineWalk(IGrammar grammar, TimeProvider clock, CancellationToken cancellationToken)
    {
        /// <summary>What the budget of three seconds runs from.</summary>
        public long Started = clock.GetTimestamp();

        /// <summary>Lines walked so far.</summary>
        public int Lines;

        // TextMateSharp measures a line's budget on the wall clock only; MaxValue is none.
        private readonly TimeSpan _lineBudget = clock == TimeProvider.System ? LineBudget : TimeSpan.MaxValue;
        private readonly int _longLine = LongLineFor(grammar);
        private readonly List<TextSpan> _spans = new(32);

        /// <summary>Walks lines <paramref name="from"/> to <paramref name="to"/> from
        /// <paramref name="state"/>. The spans of a line go to the sides that show it; a line
        /// neither shows is walked for its state.</summary>
        public void Run(List<ReadOnlyMemory<char>> lines, int from, int to, ref IStateStack? state,
            ref Side first, ref Side second)
        {
            for (int i = from; i < to; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (clock.GetElapsedTime(Started) > FileBudget) continue;
                Lines++;

                bool inFirst = first.Shows(i), inSecond = second.Shows(i);
                var line = lines[i];
                // A long line is tokenized on its first characters only and, like a line
                // stopped at its budget, leaves the state it started in. Walked for that state
                // alone, it needs no tokenize at all.
                bool whole = line.Length <= _longLine;
                if (!whole)
                {
                    if (!inFirst && !inSecond) continue;
                    line = line[..(char.IsHighSurrogate(line.Span[_longLine - 1]) ? _longLine - 1 : _longLine)];
                }
                long lineStarted = Stopwatch.GetTimestamp();
                if (!inFirst && !inSecond)
                {
                    IStateStack next;
                    try { next = grammar.TokenizeLine2(new LineText(line), state, _lineBudget).RuleStack; }
                    catch { continue; }   // a broken grammar rule shouldn't kill the whole file
                    if (Finished(lineStarted)) state = next;
                    continue;
                }

                ITokenizeLineResult tokenized;
                try { tokenized = grammar.TokenizeLine(new LineText(line), state, _lineBudget); }
                catch { continue; }   // a broken grammar rule shouldn't kill the whole file
                if (whole && Finished(lineStarted)) state = tokenized.RuleStack;

                int lineLength = line.Length;
                _spans.Clear();
                foreach (var token in tokenized.Tokens)
                {
                    int s = Math.Clamp(token.StartIndex, 0, lineLength);
                    int e = Math.Clamp(token.EndIndex, s, lineLength);
                    if (e <= s) continue;
                    if (ColorFor(token.Scopes) is not { } color) continue;

                    // Merge contiguous same-color tokens so the renderer draws fewer segments.
                    if (_spans.Count > 0 && _spans[^1].End == s && _spans[^1].Color == color)
                        _spans[^1] = _spans[^1] with { Length = _spans[^1].Length + (e - s) };
                    else
                        _spans.Add(new TextSpan(s, e - s, color));
                }
                if (_spans.Count == 0) continue;
                var spans = _spans.ToArray();
                if (inFirst) first.Result![i] = spans;
                if (inSecond) second.Result![i] = spans;
            }
        }

        // A line stopped at its budget hands back the state it stopped in, part-way through
        // the line, inside a string say, and every line after it used to be tokenized from
        // there: a JSON body's keys and values swapped colours to its last line. The result
        // does not say it stopped (StoppedEarly is on TextMateSharp's internal type), so a
        // line that took its whole budget is taken to have, and the next line starts from the
        // state this one started in, as VS Code does. The stopped line keeps the colour found
        // before the stop.
        private bool Finished(long lineStarted) =>
            _lineBudget == TimeSpan.MaxValue || Stopwatch.GetElapsedTime(lineStarted) < _lineBudget;

        /// <summary>How many characters of a line <paramref name="grammar"/> colours.</summary>
        private static int LongLineFor(IGrammar grammar) =>
            grammar.GetScopeName() is "source.json" or "source.json.comments" or "text.xml" ? LongDataLine : LongLine;
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
    // stripped so offsets line up with the raw line content. At most `limit` of them.
    private static List<ReadOnlyMemory<char>> SplitLines(string text, int limit = int.MaxValue)
    {
        var lines = new List<ReadOnlyMemory<char>>(Math.Min(limit, 256));
        int start = 0;
        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] != '\n') continue;
            int end = i > start && text[i - 1] == '\r' ? i - 1 : i;
            lines.Add(text.AsMemory(start, end - start));
            if (lines.Count == limit) return lines;
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
