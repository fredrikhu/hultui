using System.Diagnostics;
using System.Runtime.CompilerServices;
using HultUI.Core.Primitives;
using HultUI.Core.Rendering;
using TextMateSharp.Grammars;
using TextMateSharp.Internal.Grammars;
using TextMateSharp.Internal.Types;
using TextMateSharp.Registry;
using TextMateSharp.Themes;
using TmTheme = TextMateSharp.Themes.Theme;   // "Theme" alone collides with HultUI.Core.Styling.Theme

namespace HultUI.Syntax;

/// <summary>
/// Lines of a text, numbered from 1, with both ends included: <c>new LineRange(3, 5)</c> is lines
/// 3, 4 and 5, and <c>new LineRange(7, 7)</c> is line 7 alone. This is how a diff's hunk header
/// numbers its lines.
///
/// <para><see cref="SyntaxHighlighter.TokenizeDiff"/> takes each side's lines as a list of these,
/// and checks it: <see cref="First"/> at least 1, <see cref="Last"/> no less than
/// <see cref="First"/>, and each range starting after the one before it ends (adjacent ranges are
/// fine, overlapping or falling ones are not).</para>
/// </summary>
/// <param name="First">The first line of the range, numbered from 1.</param>
/// <param name="Last">The last line of the range, which is included.</param>
public readonly record struct LineRange(int First, int Last);

/// <summary>
/// What <see cref="SyntaxHighlighter.TokenizeDiff"/> made of two versions of a file. Each side is
/// an array with one span list per line, line 1 at index 0, and offsets within the line, as
/// <see cref="SyntaxHighlighter.TokenizeLines(string, string)"/> gives them.
///
/// <para>A side's array ends at its last shown line, or at the end of its text when that comes
/// first. It holds spans only for the lines shown: every other line's list is empty, and so is a
/// shown line with nothing to colour. A side is null when it had no text, nothing shown, or a
/// text too long to colour, and both are null when the path has no grammar. <c>default</c> is
/// the result with nothing coloured.</para>
/// </summary>
/// <param name="Old">The old version's spans by line, or null when it has none.</param>
/// <param name="New">The new version's spans by line, or null when it has none.</param>
/// <param name="LinesWalked">How many lines were tokenized, those both versions start with counted
/// once: the work the call did, for a caller that counts it.</param>
public readonly record struct TokenizedDiff(IReadOnlyList<TextSpan>[]? Old, IReadOnlyList<TextSpan>[]? New,
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
    /// <summary>What every registry is built over: <see cref="_options"/> with the theme merged
    /// with the one it includes (<see cref="MergedOptions"/>).</summary>
    private static MergedOptions? _themed;
    private static Registry? _registry;
    private static Palette? _palette;
    private static readonly Dictionary<string, Language?> LanguageByExt = new(StringComparer.OrdinalIgnoreCase);

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
            if (LanguageFor(path, text) is not { } language) return null;
            return Tokenize(wrap is null ? language.Grammar : wrap(language.Grammar), language.Colours, text, maxLines,
                clock ?? TimeProvider.System, cancellationToken);
        }
    }

    /// <summary>
    /// Two versions of one file tokenized for a diff, which shows a few lines of each. A line
    /// needs every line above it, for the state they leave (a block comment opened 900 lines
    /// up), but a diff needs nothing below its last line, and of the lines above it only that
    /// state. So each side stops at its last shown line; a line nobody shows is walked for its
    /// state and leaves no spans to keep (a diff's lines are a few in a hundred); and the lines
    /// both versions start with are walked once. So are the stretches they share between hunks:
    /// a line of the old version with the text of one of the new version's, starting in the state
    /// that one started in, takes the state it left and its spans. A line shown gets the spans
    /// <see cref="TokenizeLines(string, string, int, CancellationToken, TimeProvider?)"/> gives it,
    /// and each side is held to the same limits: a text too long for that gets none here.
    ///
    /// <para>A side's array ends at its last shown line, and holds spans only for lines shown.
    /// A side with no text, or nothing shown, is null; see <see cref="TokenizedDiff"/>.</para>
    ///
    /// <para>Call it from a worker, like the other tokenizes: it takes the same gate, and a change
    /// near the end of a large file walks every line above it.</para>
    ///
    /// <para>A call cancelled part-way leaves where it got to with its texts: a later call handed
    /// the same string instances, the same lines shown and the same clock, under the same theme,
    /// goes on from there instead of walking from the first line again, and gives the spans an
    /// uncancelled call gives, within the budget one walk has. That is kept only as long as the
    /// texts are: a caller that reads a file afresh for each call gets a fresh walk each time, and
    /// keeps nothing.</para>
    /// </summary>
    /// <param name="path">The file's path or name; its extension picks the grammar, as for
    /// <see cref="TokenizeLines(string, string)"/>. Both versions are coloured as that language.</param>
    /// <param name="oldText">The old version's text, or null when there is none (a file the change
    /// added).</param>
    /// <param name="oldShown">The old version's lines to colour, numbered from 1 with both ends
    /// included, in rising order and none overlapping another. Empty for none.</param>
    /// <param name="newText">The new version's text, or null when there is none (a file the change
    /// deleted).</param>
    /// <param name="newShown">The new version's lines to colour, likewise.</param>
    /// <param name="maxLines">The most lines a version may have: one with more gets no spans, as in
    /// <see cref="TokenizeLines(string, string, int, CancellationToken, TimeProvider?)"/>. Pass
    /// <see cref="DefaultMaxLines"/> for its usual cap.</param>
    /// <param name="cancellationToken">Checked before every line; a cancelled tokenize throws
    /// <see cref="OperationCanceledException"/> within a line.</param>
    /// <param name="clock">What the three-second budget is measured on, the system clock when null;
    /// see <see cref="TokenizeLines(string, string, int, CancellationToken, TimeProvider?)"/>.</param>
    /// <returns>Each version's spans by line, and how many lines this call walked to find them
    /// (fewer when it went on from a cancelled one).</returns>
    /// <exception cref="ArgumentNullException"><paramref name="oldShown"/> or
    /// <paramref name="newShown"/> is null.</exception>
    /// <exception cref="ArgumentException">A range in <paramref name="oldShown"/> or
    /// <paramref name="newShown"/> starts before line 1, ends before it starts, or does not start
    /// after the range before it ends. Checked whether or not that version has text.</exception>
    public static TokenizedDiff TokenizeDiff(string path, string? oldText, IReadOnlyList<LineRange> oldShown,
        string? newText, IReadOnlyList<LineRange> newShown, int maxLines, CancellationToken cancellationToken,
        TimeProvider? clock = null) =>
        TokenizeDiff(path, oldText, oldShown, newText, newShown, maxLines, cancellationToken, clock, wrap: null);

    /// <summary><see cref="TokenizeDiff(string, string?, IReadOnlyList{LineRange}, string?, IReadOnlyList{LineRange}, int, CancellationToken, TimeProvider?)"/>
    /// through a wrapper of the grammar, which a test uses to cancel a walk at a line of its
    /// choosing. A walk goes on with the grammar, whatever wrapped it before.</summary>
    internal static TokenizedDiff TokenizeDiff(string path, string? oldText, IReadOnlyList<LineRange> oldShown,
        string? newText, IReadOnlyList<LineRange> newShown, int maxLines, CancellationToken cancellationToken,
        TimeProvider? clock, Func<IGrammar, IGrammar>? wrap)
    {
        CheckShown(oldShown, nameof(oldShown));
        CheckShown(newShown, nameof(newShown));
        clock ??= TimeProvider.System;
        if (oldShown.Count == 0 || oldText is not null && TooLong(oldText, maxLines)) oldText = null;
        if (newShown.Count == 0 || newText is not null && TooLong(newText, maxLines)) newText = null;
        if (oldText is null && newText is null) return default;
        cancellationToken.ThrowIfCancellationRequested();
        lock (Gate)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (LanguageFor(path, newText ?? oldText!, newText is null ? null : oldText) is not { } language) return default;

            var oldLines = oldText is null ? [] : SplitLines(oldText, oldShown[^1].Last);
            var newLines = newText is null ? [] : SplitLines(newText, newShown[^1].Last);
            var longer = newLines.Count >= oldLines.Count ? newLines : oldLines;
            int shared = oldText is null || newText is null ? 0 : Math.Min(SharedLines(oldText, newText), longer.Count);

            // The texts are what a walk is kept with: the longer, which an empty one never is.
            string held = (oldText?.Length ?? -1) > (newText?.Length ?? -1) ? oldText! : newText!;
            var w = DiffWalks.TryGetValue(held, out var kept) && kept.Continues(language, oldText, oldShown, newText, newShown, clock)
                ? kept
                : new DiffWalk(language, oldText, oldShown, newText, newShown, clock,
                    oldText is null ? null : Uncoloured(oldLines.Count), newText is null ? null : Uncoloured(newLines.Count));
            var oldSide = new Side(w.OldResult, oldShown);
            var newSide = new Side(w.NewResult, newShown);
            var walk = new LineWalk(wrap is null ? language.Grammar : wrap(language.Grammar), language.Colours, clock,
                cancellationToken);
            if (w.Spent > TimeSpan.Zero) walk.Started = clock.GetTimestamp() - Ticks(w.Spent, clock);   // the budget goes on
            try
            {
                if (w.Stage == 0)
                {
                    walk.Run(longer, w.At, shared, ref w.State, ref oldSide, ref newSide);
                    w.SharedSpent = clock.GetElapsedTime(walk.Started);
                    (w.Stage, w.At, w.SharedState) = (1, shared, w.State);
                }
                if (w.Stage == 1)
                {
                    // What the new version's own lines leave, for the old version's to take; only
                    // when both have lines of their own.
                    if (w.NewLeft is null && newLines.Count > shared && oldLines.Count > shared)
                        w.NewLeft = new LinesLeft(shared, newLines.Count - shared);
                    walk.Record = w.NewLeft;
                    walk.Run(newLines, w.At, newLines.Count, ref w.State, ref newSide, ref Side.None);
                    walk.Record = null;
                    // Each version has its whole budget, as it would alone, less the walk they shared.
                    walk.Started = clock.GetTimestamp() - Ticks(w.SharedSpent, clock);
                    (w.Stage, w.At, w.State) = (2, shared, w.SharedState);
                }
                if (w.NewLeft is not null) walk.Take = new LineTaker(newLines, w.SharedState, w.NewLeft, w.NewResult!);
                walk.Run(oldLines, w.At, oldLines.Count, ref w.State, ref oldSide, ref Side.None);
            }
            catch (OperationCanceledException)
            {
                // Keep where it got to, for a call with these texts. The lines before walk.At are
                // done, and w.State is the state the last of them left. The taker's index of line
                // texts is not kept: a walk that goes on makes it again.
                (w.At, w.Spent) = (walk.At, clock.GetElapsedTime(walk.Started));
                if (w.Stage > 0 || w.At > 0) DiffWalks.AddOrUpdate(held, w);
                throw;
            }
            if (ReferenceEquals(w, kept)) DiffWalks.Remove(held);   // its arrays are the caller's now
            return new TokenizedDiff(w.OldResult, w.NewResult, walk.Lines);
        }
    }

    private static long Ticks(TimeSpan span, TimeProvider clock) => (long)(span.TotalSeconds * clock.TimestampFrequency);

    /// <summary>The walks <see cref="TokenizeDiff(string, string?, IReadOnlyList{LineRange}, string?, IReadOnlyList{LineRange}, int, CancellationToken, TimeProvider?)"/>
    /// was cancelled out of, by the longer of their texts: each is kept while that string is alive,
    /// and goes with it. Under <see cref="Gate"/>.</summary>
    private static readonly ConditionalWeakTable<string, DiffWalk> DiffWalks = new();

    /// <summary>Where a diff's walk has got to: the lines both versions start with (stage 0),
    /// then the new version's own (1), then the old version's (2). A call goes on with it only
    /// for the same texts, lines shown and clock, and the same grammar, which a theme change
    /// replaces; anything else would colour from a state the lines above it did not leave.</summary>
    private sealed class DiffWalk(Language language, string? oldText, IReadOnlyList<LineRange> oldShown,
        string? newText, IReadOnlyList<LineRange> newShown, TimeProvider clock,
        IReadOnlyList<TextSpan>[]? oldResult, IReadOnlyList<TextSpan>[]? newResult)
    {
        private readonly LineRange[] _oldShown = [.. oldShown], _newShown = [.. newShown];

        public readonly IReadOnlyList<TextSpan>[]? OldResult = oldResult, NewResult = newResult;

        /// <summary>The stage, and the next of its lines to walk.</summary>
        public int Stage, At;

        /// <summary>The state the lines walked so far left; the one the shared lines left.</summary>
        public IStateStack? State, SharedState;

        /// <summary>The stage's budget spent so far, and the shared lines' (both zero on a clock
        /// but the system's).</summary>
        public TimeSpan Spent, SharedSpent;

        /// <summary>What the new version's own lines left, once its walk has started on them;
        /// null when either version has no lines of its own.</summary>
        public LinesLeft? NewLeft;

        public bool Continues(Language l, string? o, IReadOnlyList<LineRange> os, string? n, IReadOnlyList<LineRange> ns,
            TimeProvider c) =>
            ReferenceEquals(l, language) && ReferenceEquals(o, oldText) && ReferenceEquals(n, newText)
            && ReferenceEquals(c, clock) && Same(os, _oldShown) && Same(ns, _newShown);

        private static bool Same(IReadOnlyList<LineRange> a, LineRange[] b)
        {
            if (a.Count != b.Length) return false;
            for (int i = 0; i < b.Length; i++)
                if (a[i] != b[i]) return false;
            return true;
        }
    }

    /// <summary>The new version's own lines as its walk left them, for the old version's walk to
    /// take (see <see cref="LineTaker"/>): the state each line left and what it can give, 9 B a
    /// line, kept with a cancelled walk.</summary>
    private sealed class LinesLeft(int first, int count)
    {
        /// <summary>The first of the lines: the first the two versions do not share.</summary>
        public readonly int First = first;

        /// <summary>The state line <see cref="First"/> + k left.</summary>
        public readonly IStateStack?[] State = new IStateStack?[count];

        /// <summary>What line <see cref="First"/> + k can give: <see cref="LineWalk.Nothing"/>,
        /// <see cref="LineWalk.StateOnly"/> or <see cref="LineWalk.StateAndSpans"/>.</summary>
        public readonly byte[] Gives = new byte[count];

        /// <summary>The line the old version's next line is expected to take: the one after the
        /// last taken.</summary>
        public int Expected = first;
    }

    /// <summary>
    /// For a line of the old version, finds a line of the new version's own that it can take: one
    /// with the same text that started in the state the old line starts in. A line's tokens and the
    /// state it leaves are made of nothing else, so the old line leaves the state the new one left
    /// and has its spans, without a tokenize. Two versions share the stretches between their hunks,
    /// and the old version's walk used to tokenize each of them again (plan 17 round 17).
    ///
    /// <para>It tries the line after the last one taken first, the next of a stretch; then the
    /// lines of the same text nearest to that one, after it and then before, at most
    /// <see cref="Tries"/> of them, since a lone brace or a blank line has hundreds. The index it
    /// finds them in, 8 B a line, is made at the first line the expected one does not give, and
    /// goes with the call.</para>
    /// </summary>
    private sealed class LineTaker(List<ReadOnlyMemory<char>> lines, IStateStack? firstState, LinesLeft left,
        IReadOnlyList<TextSpan>[] spans)
    {
        private const int Tries = 16;

        // The lines that can give anything, by the hash of their text and then by number:
        // hash << 32 | line.
        private long[]? _index;
        private int _indexed;

        /// <summary>Takes a line for one of <paramref name="text"/> that starts in
        /// <paramref name="state"/>: sets the state it left, and its spans when
        /// <paramref name="needSpans"/>.</summary>
        public bool TryTake(ReadOnlySpan<char> text, bool needSpans, ref IStateStack? state,
            out IReadOnlyList<TextSpan>? taken)
        {
            int expected = left.Expected;
            if (expected < lines.Count && Try(expected, text, needSpans, ref state, out taken)) return true;

            taken = null;
            var index = _index ??= Index();
            int hash = string.GetHashCode(text), tries = 0;
            int at = Array.BinarySearch(index, 0, _indexed, (long)hash << 32 | (uint)expected);
            if (at < 0) at = ~at;
            for (int k = at; k < _indexed && tries < Tries && (int)(index[k] >> 32) == hash; k++)
            {
                if ((int)index[k] == expected) continue;   // tried
                tries++;
                if (Try((int)index[k], text, needSpans, ref state, out taken)) return true;
            }
            for (int k = at - 1; k >= 0 && tries < Tries && (int)(index[k] >> 32) == hash; k--)
            {
                tries++;
                if (Try((int)index[k], text, needSpans, ref state, out taken)) return true;
            }
            return false;
        }

        private bool Try(int line, ReadOnlySpan<char> text, bool needSpans, ref IStateStack? state,
            out IReadOnlyList<TextSpan>? taken)
        {
            taken = null;
            int k = line - left.First;
            byte gives = left.Gives[k];
            if (gives == LineWalk.Nothing || needSpans && gives != LineWalk.StateAndSpans) return false;
            if (!lines[line].Span.SequenceEqual(text) || !SameState(k == 0 ? firstState : left.State[k - 1], state))
                return false;
            state = left.State[k];
            if (needSpans) taken = spans[line];
            left.Expected = line + 1;
            return true;
        }

        private long[] Index()
        {
            var index = new long[left.Gives.Length];
            for (int k = 0; k < left.Gives.Length; k++)
                if (left.Gives[k] != LineWalk.Nothing)
                    index[_indexed++] = (long)string.GetHashCode(lines[left.First + k].Span) << 32 | (uint)(left.First + k);
            Array.Sort(index, 0, _indexed);
            return index;
        }
    }

    /// <summary>Whether a line started in <paramref name="a"/> is tokenized as one started in
    /// <paramref name="b"/>. TextMateSharp's <c>Equals</c> compares the rules open, what their
    /// ends captured and their scopes, but not whether each rule's begin matched to the end of its
    /// line, which decides whether <c>\G</c> matches at the start of the next: that is compared
    /// here (VS Code's tokenizer, whose comparison this is, leaves it out). States of another kind
    /// are compared by their own <c>Equals</c>.</summary>
    internal static bool SameState(IStateStack? a, IStateStack? b)
    {
        if (ReferenceEquals(a, b)) return true;
        if (a is null || b is null || !a.Equals(b)) return false;
        for (StateStack? x = a as StateStack, y = b as StateStack; x is not null && y is not null; x = x.Parent, y = y.Parent)
        {
            if (ReferenceEquals(x, y)) return true;
            if (BeginRuleCapturedEol(x) != BeginRuleCapturedEol(y)) return false;
        }
        return true;
    }

    // Internal in TextMateSharp 2.0.5; a test pins it.
    [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "get_BeginRuleCapturedEOL")]
    private static extern bool BeginRuleCapturedEol(StateStack state);

    /// <summary>Throws unless <paramref name="shown"/> is what <see cref="LineRange"/> asks of a
    /// side's lines. The walk reads a side's last line from its last range, and steps through the
    /// ranges once, in order.</summary>
    private static void CheckShown(IReadOnlyList<LineRange> shown, string paramName)
    {
        ArgumentNullException.ThrowIfNull(shown, paramName);
        int previousLast = 0;
        for (int i = 0; i < shown.Count; i++)
        {
            var (first, last) = shown[i];
            string? wrong =
                first < 1 ? "starts before line 1; lines are numbered from 1"
                : last < first ? "ends before it starts; both ends are included, so one line is (n, n)"
                : first <= previousLast ? $"does not start after range {i - 1}, which ends at line {previousLast}; "
                    + "ranges go in rising order and do not overlap"
                : null;
            if (wrong is not null)
                throw new ArgumentException($"Range {i} of the lines shown, ({first}, {last}), {wrong}.", paramName);
            previousLast = last;
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
            // carries are indices into the theme's own colour table, so a palette and every
            // grammar's cached tokenization belong to the theme that produced them.
            _registry = null;
            _options = null;
            _themed = null;
            _palette = null;
            _rootParents = null;
            LanguageByExt.Clear();
            LanguageByRoot.Clear();
            Reachings.Clear();
            DiffWalks.Clear();   // their states and spans are the old theme's
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
        _themed = new MergedOptions(_options);
        _registry = new Registry(_themed);
        _palette = new Palette(_registry.GetTheme());
        _rootParents = RootedOptions.ParentsNamed(_themed.GetDefaultTheme());
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

    /// <summary>A grammar and the palette its tokens' colour ids index.</summary>
    private sealed record Language(IGrammar Grammar, Palette Colours);

    /// <summary>
    /// The grammar for <paramref name="path"/>, resolved from its extension, then
    /// from an alias, then — for extension-less files — from its name, to tokenize
    /// <paramref name="text"/> (and <paramref name="other"/>, a diff's second version)
    /// with. Null when nothing bundled parses it.
    /// </summary>
    private static Language? LanguageFor(string path, string text, string? other = null)
    {
        if (ResolveExtension(path) is not { } ext) return null;
        EnsureRegistry();
        // Keyed by the resolved extension, so every alias shares one cached grammar.
        if (LanguageByExt.TryGetValue(ext, out var language)) return language;
        if (ScannedByExt.TryGetValue(ext, out var scanned)) return LanguageReaching(scanned, text, other);
        var scope = _options!.GetScopeByExtension(ext);
        if (scope != null && EmbeddedLanguages.Scans(scope))
        {
            ScannedByExt[ext] = scope;
            return LanguageReaching(scope, text, other);
        }
        language = scope == null ? null : LanguageForRoot(scope);
        LanguageByExt[ext] = language;
        return language;
    }

    // ── A text that embeds other languages ───────────────────────────────
    //
    // A grammar that embeds others (Markdown) tokenizes each text with a registry of only the grammars
    // the text can reach (EmbeddedLanguages), not the shared one: a Markdown file with no fences builds
    // Markdown's rules, not those of 49 grammars. Registries are kept for the last few sets of grammars,
    // and a text uses any whose set holds its own, since a grammar it cannot reach changes nothing; a new
    // set replaces the sets it holds.

    /// <summary>The extensions whose grammar scans its texts, and that grammar's scope.</summary>
    private static readonly Dictionary<string, string> ScannedByExt = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>A registry for texts of <see cref="Root"/> that reach no grammar outside
    /// <see cref="Scopes"/>, and the grammars it has <see cref="Loaded"/>.</summary>
    private sealed record Reaching(string Root, HashSet<string> Scopes, Language Language, IReadOnlyList<string> Loaded);

    /// <summary>The grammars TextMateSharp loaded into the registry that colours <paramref name="text"/>
    /// as <paramref name="path"/>, or null when that is the shared registry. A test reads it.</summary>
    internal static IReadOnlyList<string>? LoadedFor(string path, string text)
    {
        lock (Gate)
        {
            var language = LanguageFor(path, text);
            return Reachings.Find(r => r.Language == language)?.Loaded;
        }
    }

    /// <summary>The registries of texts that embed languages, the last used first.</summary>
    private static readonly List<Reaching> Reachings = [];
    internal const int MaxReachings = 4;

    /// <summary>The scopes each grammar names, by scope; null for a scope with no grammar. Read from the
    /// grammar files, so it holds across themes, and has an entry per scope any grammar names.</summary>
    private static readonly Dictionary<string, string[]?> NamesByScope = new(StringComparer.Ordinal);

    private static Language? LanguageReaching(string root, string text, string? other)
    {
        var parsed = new Dictionary<string, IRawGrammar>(StringComparer.Ordinal);
        var reached = EmbeddedLanguages.Reach(root, text, other, LongLineFor(root), scope => NamesOf(scope, parsed));
        if (reached.Count == 0) return null;
        for (int i = 0; i < Reachings.Count; i++)
        {
            var known = Reachings[i];
            if (known.Root != root || !known.Scopes.IsSupersetOf(reached)) continue;
            Reachings.RemoveAt(i);
            Reachings.Insert(0, known);
            return known.Language;
        }

        IRegistryOptions options = _rootParents!.Any(parent => RootedOptions.Names(root, parent))
            ? new RootedOptions(_themed!, root)
            : _themed!;
        var reach = new ReachOptions(options, root, reached, parsed);
        var registry = new Registry(reach);
        if (registry.LoadGrammar(root) is not { } grammar) return null;
        var language = new Language(grammar, new Palette(registry.GetTheme()));
        Reachings.RemoveAll(r => r.Root == root && reached.IsSupersetOf(r.Scopes));
        Reachings.Insert(0, new Reaching(root, reached, language, reach.HandedOut));
        if (Reachings.Count > MaxReachings) Reachings.RemoveAt(MaxReachings);
        return language;
    }

    /// <summary>The scopes <paramref name="scope"/>'s grammar names, reading the grammar the first time
    /// (into <paramref name="parsed"/>, for the registry to take rather than read it again).</summary>
    private static string[]? NamesOf(string scope, Dictionary<string, IRawGrammar> parsed)
    {
        if (NamesByScope.TryGetValue(scope, out var names)) return names;
        IRawGrammar? grammar = null;
        try { grammar = _options!.GetGrammar(scope); }
        catch { }   // a grammar that does not read is one TextMateSharp skips too
        if (grammar is not null) parsed[scope] = grammar;
        return NamesByScope[scope] = grammar is null ? null : EmbeddedLanguages.NamesIn(grammar, scope);
    }

    /// <summary>
    /// The registry's options for texts that reach only <paramref name="reached"/>: to it any other
    /// grammar is missing. A grammar read while finding them is handed out from <paramref name="parsed"/>.
    /// </summary>
    private sealed class ReachOptions(IRegistryOptions inner, string root, HashSet<string> reached,
        Dictionary<string, IRawGrammar> parsed) : IRegistryOptions
    {
        /// <summary>The scopes of the grammars handed out, in order.</summary>
        public readonly List<string> HandedOut = [];

        public IRawTheme GetDefaultTheme() => inner.GetDefaultTheme();
        public IRawTheme GetTheme(string scopeName) => inner.GetTheme(scopeName);

        public IRawGrammar GetGrammar(string scopeName)
        {
            if (!reached.Contains(scopeName)) return null!;
            var grammar = parsed.Remove(scopeName, out var read) ? read : inner.GetGrammar(scopeName);
            if (grammar is not null) HandedOut.Add(scopeName);
            return grammar!;
        }

        // TextMateSharp loads with a grammar the grammars its patterns and repository name, not those only a
        // capture names (CoffeeScript's `...` names JavaScript); in the shared registry those were there
        // anyway. Named here as the grammar's injections, they load with it. A registry applies only its
        // root grammar's injections, and the root has none.
        public ICollection<string> GetInjections(string scopeName)
        {
            if (scopeName == root || !NamesByScope.TryGetValue(scopeName, out var names) || names is null) return null!;
            var reachable = names.Where(reached.Contains).ToList();
            return reachable.Count == 0 ? null! : reachable;
        }
    }

    // ── A theme that includes another ────────────────────────────────────
    //
    // A VS Code theme file can include another: Dark+ includes dark_vs, and Light+ and High Contrast
    // Light include light_vs. VS Code reads the two as one list of rules, the included file's first,
    // so for each scope the most specific rule of either file wins, and of two as specific the theme's
    // own. TextMateSharp keeps the included file in a second lookup that it reads only when no rule of
    // the theme's own applies to the scope, however broad, so a broader rule of the theme's beats a
    // narrower one of the file it includes: in Dark+ a JSON key took dark_plus's "support.type" teal
    // over dark_vs's "support.type.property-name" light blue, and `this` dark_plus's "variable" light
    // blue over dark_vs's "variable.language" blue. So every registry gets the theme merged as VS Code
    // merges it (MergedOptions), and TextMateSharp, seeing no include, has one lookup. A theme that
    // includes nothing is handed over as it is.

    /// <summary>The registry's options with the theme merged with the one it includes, merged once.</summary>
    private sealed class MergedOptions(RegistryOptions inner) : IRegistryOptions
    {
        private readonly IRawTheme _theme = MergedTheme.Of(inner.GetDefaultTheme(), inner);

        public IRawTheme GetDefaultTheme() => _theme;
        public IRawTheme GetTheme(string scopeName) => inner.GetTheme(scopeName);
        public IRawGrammar GetGrammar(string scopeName) => inner.GetGrammar(scopeName);
        public ICollection<string> GetInjections(string scopeName) => inner.GetInjections(scopeName);
    }

    /// <summary>
    /// A theme and the chain of themes it includes as one theme that includes none: the rules of the
    /// last theme included first and the theme's own last, in their files' order, and the colours of
    /// the editor's parts (<c>editor.foreground</c>) of each theme over those of the one it includes.
    /// </summary>
    private sealed class MergedTheme(IRawTheme theme, List<IRawThemeSetting> rules,
        List<KeyValuePair<string, object>> guiColors) : IRawTheme
    {
        public static IRawTheme Of(IRawTheme theme, IRegistryOptions options)
        {
            var chain = new List<IRawTheme> { theme };
            var seen = new HashSet<string>(StringComparer.Ordinal);
            while (chain[^1].GetInclude() is { Length: > 0 } include && seen.Add(include)
                   && options.GetTheme(include) is { } included)
                chain.Add(included);
            if (chain.Count == 1) return theme;

            var rules = new List<IRawThemeSetting>();
            var guiColors = new List<KeyValuePair<string, object>>();
            var at = new Dictionary<string, int>(StringComparer.Ordinal);
            for (int i = chain.Count - 1; i >= 0; i--)
            {
                rules.AddRange(chain[i].GetSettings() ?? []);
                rules.AddRange(chain[i].GetTokenColors() ?? []);
                foreach (var color in chain[i].GetGuiColors() ?? [])
                {
                    if (at.TryGetValue(color.Key, out int k)) guiColors[k] = color;
                    else { at[color.Key] = guiColors.Count; guiColors.Add(color); }
                }
            }
            return new MergedTheme(theme, rules, guiColors);
        }

        public string GetName() => theme.GetName();
        public string GetInclude() => null!;
        // One list, so a rule's place in it (which TextMateSharp breaks ties of specificity by) runs
        // across both files.
        public ICollection<IRawThemeSetting> GetSettings() => null!;
        public ICollection<IRawThemeSetting> GetTokenColors() => rules;
        public ICollection<KeyValuePair<string, object>> GetGuiColors() => guiColors;
    }

    // ── A theme rule that names the root scope ───────────────────────────
    //
    // TextMateSharp never colours an encoded token by a theme rule that names parent scopes: it
    // looks for the rule's own scope among the token's parents as well. VS Code applies such a rule
    // inside its parents. In Dark+ and Light+ the one that matters is "source.powershell
    // variable.other.member", a member's yellow in a PowerShell file, where source.powershell is
    // the root of every token: without it, `$x.Count` would draw Count light blue as a variable. So
    // a grammar whose root scope a rule's one parent names gets a registry of its own, whose theme
    // has that rule without the parent (RootedOptions): inside such a file it always applies, as in
    // VS Code. Everywhere else the rule still applies nowhere, also as in VS Code (a C member is not
    // a PowerShell one, and a PowerShell fence in Markdown has no source.powershell scope). Of the
    // bundled grammars that is PowerShell, and Less for dark_vs's and light_vs's
    // "source.css.less entity.other.attribute-name.id".
    private static HashSet<string>? _rootParents;
    private static readonly Dictionary<string, Language> LanguageByRoot = new(StringComparer.Ordinal);

    private static Language? LanguageForRoot(string scope)
    {
        if (!_rootParents!.Any(parent => RootedOptions.Names(scope, parent)))
            return _registry!.LoadGrammar(scope) is { } grammar ? new Language(grammar, _palette!) : null;
        if (LanguageByRoot.TryGetValue(scope, out var known)) return known;
        var registry = new Registry(new RootedOptions(_themed!, scope));
        if (registry.LoadGrammar(scope) is not { } rooted) return null;
        return LanguageByRoot[scope] = new Language(rooted, new Palette(registry.GetTheme()));
    }

    /// <summary>
    /// The registry's options with the theme as TextMateSharp should see it for a grammar whose
    /// root scope is <paramref name="root"/>: a rule whose one parent names the root, "source.powershell
    /// variable.other.member" for <c>source.powershell</c>, without its parent, and last in the theme
    /// (the merged one, so after the rules of both files), as VS Code prefers a rule with parents to
    /// one without for the same scope.
    /// </summary>
    private sealed class RootedOptions(IRegistryOptions inner, string root) : IRegistryOptions
    {
        public IRawTheme GetDefaultTheme() => new RootedTheme(inner.GetDefaultTheme(), root);
        public IRawTheme GetTheme(string scopeName) => inner.GetTheme(scopeName) is { } theme ? new RootedTheme(theme, root) : null!;
        public IRawGrammar GetGrammar(string scopeName) => inner.GetGrammar(scopeName);
        public ICollection<string> GetInjections(string scopeName) => inner.GetInjections(scopeName);

        /// <summary>Whether <paramref name="selector"/> is <paramref name="scope"/> or a prefix of it at
        /// a dot, as a theme matches scopes.</summary>
        public static bool Names(string scope, string selector) =>
            scope.StartsWith(selector, StringComparison.Ordinal)
            && (scope.Length == selector.Length || scope[selector.Length] == '.');

        /// <summary>The parent scope of every rule of <paramref name="theme"/> (merged, so the rules of
        /// the one it includes are among them) that names one: the roots a grammar may need its own
        /// registry for.</summary>
        public static HashSet<string> ParentsNamed(IRawTheme theme)
        {
            var parents = new HashSet<string>(StringComparer.Ordinal);
            foreach (var settings in new[] { theme.GetSettings(), theme.GetTokenColors() })
                foreach (var setting in settings ?? [])
                    foreach (var selector in Selectors(setting.GetScope()))
                        if (ParentOf(selector) is { } parent) parents.Add(parent);
            return parents;
        }

        /// <summary>The parent a selector of two scopes names ("source.powershell" of
        /// "source.powershell variable.other.member"); null for any other.</summary>
        private static string? ParentOf(string selector)
        {
            var parts = selector.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            return parts.Length == 2 ? parts[0] : null;
        }

        private static List<string> Selectors(object? scope) => scope switch
        {
            string list => [.. list.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)],
            IList<object> items => [.. items.OfType<string>().Select(s => s.Trim())],
            _ => [],
        };

        private sealed class RootedTheme(IRawTheme inner, string root) : IRawTheme
        {
            public string GetName() => inner.GetName();
            public string GetInclude() => inner.GetInclude();
            public ICollection<IRawThemeSetting> GetSettings() => Rooted(inner.GetSettings())!;
            public ICollection<IRawThemeSetting> GetTokenColors() => Rooted(inner.GetTokenColors())!;
            public ICollection<KeyValuePair<string, object>> GetGuiColors() => inner.GetGuiColors();

            private ICollection<IRawThemeSetting>? Rooted(ICollection<IRawThemeSetting>? settings)
            {
                if (settings is null) return null;
                var kept = new List<IRawThemeSetting>(settings.Count);
                var moved = new List<IRawThemeSetting>();
                foreach (var setting in settings)
                {
                    var selectors = Selectors(setting.GetScope());
                    if (!selectors.Exists(IsRooted)) { kept.Add(setting); continue; }
                    if (selectors.Where(s => !IsRooted(s)).ToList() is { Count: > 0 } stay)
                        kept.Add(new Setting(setting, string.Join(',', stay)));
                    moved.Add(new Setting(setting, string.Join(',', selectors.Where(IsRooted)
                        .Select(s => s.Split(' ', StringSplitOptions.RemoveEmptyEntries)[1]))));
                }
                kept.AddRange(moved);
                return kept;
            }

            private bool IsRooted(string selector) => ParentOf(selector) is { } parent && Names(root, parent);
        }

        private sealed class Setting(IRawThemeSetting inner, string scope) : IRawThemeSetting
        {
            public string GetName() => inner.GetName();
            public object GetScope() => scope;
            public IThemeSetting GetSetting() => inner.GetSetting();
        }
    }

    // ── Tokenization ─────────────────────────────────────────────────────

    private static IReadOnlyList<TextSpan>[]? Tokenize(IGrammar grammar, Palette colours, string text, int maxLines,
        TimeProvider clock, CancellationToken cancellationToken)
    {
        var lines = SplitLines(text);
        if (lines.Count > maxLines) return null;

        var side = new Side(Uncoloured(lines.Count), EveryLine);
        IStateStack? state = null;
        new LineWalk(grammar, colours, clock, cancellationToken).Run(lines, 0, lines.Count, ref state, ref side, ref Side.None);
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
    private sealed class LineWalk(IGrammar grammar, Palette colours, TimeProvider clock,
        CancellationToken cancellationToken)
    {
        /// <summary>What the budget of three seconds runs from.</summary>
        public long Started = clock.GetTimestamp();

        /// <summary>Lines walked so far.</summary>
        public int Lines;

        /// <summary>The line <see cref="Run"/> is on: when a cancel stops it, the first not walked.</summary>
        public int At;

        /// <summary>Where <see cref="Run"/> records what each line left, or null.</summary>
        public LinesLeft? Record;

        /// <summary>Where <see cref="Run"/> looks for a line to take before it tokenizes one, or null.</summary>
        public LineTaker? Take;

        /// <summary>What a line walked can give a line of another version with its text that
        /// starts in an equal state (see <see cref="LineTaker"/>): nothing, when what it left is not
        /// made of its text and state alone (its budget or the file's ran out, a rule broke); the
        /// state it left; or that and its spans, when it is shown.</summary>
        public const byte Nothing = 0, StateOnly = 1, StateAndSpans = 2;

        // TextMateSharp measures a line's budget on the wall clock only; MaxValue is none.
        private readonly TimeSpan _lineBudget = clock == TimeProvider.System ? LineBudget : TimeSpan.MaxValue;
        private readonly int _longLine = LongLineFor(grammar.GetScopeName());
        private readonly List<TextSpan> _spans = new(32);

        /// <summary>Walks lines <paramref name="from"/> to <paramref name="to"/> from
        /// <paramref name="state"/>. The spans of a line go to the sides that show it; a line
        /// neither shows is walked for its state.</summary>
        public void Run(List<ReadOnlyMemory<char>> lines, int from, int to, ref IStateStack? state,
            ref Side first, ref Side second)
        {
            for (int i = from; i < to; i++)
            {
                At = i;
                cancellationToken.ThrowIfCancellationRequested();
                byte gives = Line(lines[i], i, ref state, ref first, ref second);
                if (Record is { } record) (record.State[i - record.First], record.Gives[i - record.First]) = (state, gives);
            }
        }

        /// <summary>Walks line <paramref name="i"/>, or takes it; says what it can give.</summary>
        private byte Line(ReadOnlyMemory<char> line, int i, ref IStateStack? state, ref Side first, ref Side second)
        {
            if (clock.GetElapsedTime(Started) > FileBudget) return Nothing;

            bool inFirst = first.Shows(i), inSecond = second.Shows(i);
            if (Take is not null && Take.TryTake(line.Span, inFirst || inSecond, ref state, out var taken))
            {
                if (inFirst) first.Result![i] = taken!;
                if (inSecond) second.Result![i] = taken!;
                return inFirst || inSecond ? StateAndSpans : StateOnly;
            }
            Lines++;

            // A long line is tokenized on its first characters only and, like a line stopped at
            // its budget, leaves the state it started in. Walked for that state alone, it needs no
            // tokenize at all.
            bool whole = line.Length <= _longLine;
            if (!whole)
            {
                if (!inFirst && !inSecond) return StateOnly;
                line = line[..(char.IsHighSurrogate(line.Span[_longLine - 1]) ? _longLine - 1 : _longLine)];
            }
            long lineStarted = Stopwatch.GetTimestamp();
            ITokenizeLineResult2 tokenized;
            try { tokenized = grammar.TokenizeLine2(new LineText(line), state, _lineBudget); }
            catch { return Nothing; }   // a broken grammar rule shouldn't kill the whole file
            bool finished = Finished(lineStarted);
            if (whole && finished) state = tokenized.RuleStack;
            // A line stopped at its budget has the colours it found before the stop, and, when it
            // is whole, the state it started in for want of the one it would leave.
            byte gives = finished ? StateAndSpans : whole ? Nothing : StateOnly;
            if (!inFirst && !inSecond) return Math.Min(gives, StateOnly);   // walked for its state alone

            // Two numbers a token: where it starts, and its attributes, which carry the colour
            // the theme gives it (see Palette). A token ends where the next one starts, the
            // last at the end of the line.
            var tokens = tokenized.Tokens;
            int count = tokens.Length / 2, lineLength = line.Length;
            _spans.Clear();
            for (int k = 0; k < count; k++)
            {
                int s = Math.Clamp(tokens[2 * k], 0, lineLength);
                int e = Math.Clamp(k + 1 < count ? tokens[2 * k + 2] : lineLength, s, lineLength);
                if (e <= s) continue;
                if (colours.For(EncodedTokenAttributes.GetForeground(tokens[2 * k + 1])) is not { } color) continue;

                // Merge contiguous same-color tokens so the renderer draws fewer segments.
                if (_spans.Count > 0 && _spans[^1].End == s && _spans[^1].Color == color)
                    _spans[^1] = _spans[^1] with { Length = _spans[^1].Length + (e - s) };
                else
                    _spans.Add(new TextSpan(s, e - s, color));
            }
            if (_spans.Count == 0) return gives;
            var spans = _spans.ToArray();
            if (inFirst) first.Result![i] = spans;
            if (inSecond) second.Result![i] = spans;
            return gives;
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

    }

    /// <summary>How many characters of a line the grammar of root scope <paramref name="scope"/> colours.</summary>
    private static int LongLineFor(string scope) =>
        scope is "source.json" or "source.json.comments" or "text.xml" ? LongDataLine : LongLine;

    // TextMateSharp encodes in each token the colour the theme gives it, matched as the scopes were
    // pushed, so a line comes back as numbers: no token objects and no lists of scope names, 28 % of
    // a JSON body's tokenize bytes and 18 % of an XML body's. Each scope takes its own most specific
    // rule and the innermost scope with a colour wins, as in VS Code; a rule naming parent scopes
    // ("source.cpp keyword.operator.new") no longer colours a token outside them. In Dark+ and
    // Light+, the two bundled themes that include another file, that differs from taking the first
    // rule with a colour from any scope of the token, the theme's own file before the included one:
    // a quoted object key in TypeScript is a string now, `new` a keyword, a C member a variable.
    // Only the colour is taken, as before; a theme's bold and italic stay unused, since they would
    // change how wide a line is.
    /// <summary>A theme's colours by the ids one registry's tokens carry.</summary>
    private sealed class Palette(TmTheme theme)
    {
        private readonly Color? _editorForeground =
            theme.GetGuiColorDictionary().TryGetValue("editor.foreground", out var fg) ? ParseHex(fg) : null;
        private readonly Dictionary<int, Color?> _byId = new();

        /// <summary>The colour of a token's foreground id. Id 1 is the theme's default foreground,
        /// which a token no rule colours carries; it draws in the element's own text colour, and
        /// so does a rule's colour that is the editor's foreground.</summary>
        public Color? For(int id)
        {
            if (id <= 1) return null;
            if (!_byId.TryGetValue(id, out var color))
            {
                color = ParseHex(theme.GetColor(id));
                if (color == _editorForeground) color = null;
                _byId[id] = color;
            }
            return color;
        }
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
