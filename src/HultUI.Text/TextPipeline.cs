using System.Globalization;
using HultUI.Core.Layout;
using HultUI.Core.Primitives;
using HultUI.Core.Rendering;
using HultUI.Core.Styling;

namespace HultUI.Text;

public sealed class TextPipeline
{
    private readonly IFontCollection _fonts;
    private readonly ITextShaper _shaper;
    // Fully laid-out results, keyed on width (used only when the text must wrap).
    private readonly LruCache<MeasurementKey, TextLayout> _cache = new(capacity: 2048, maxWeight: LayoutCacheBytes);
    // Width-independent shaping, so the same string measured at different
    // available widths is shaped once. Covers the common single-line case fully.
    // Sized to hold the working set of a large list laid out in one frame (the
    // measure pass touches every row, then the arrange pass touches them again),
    // so shaping survives between the two passes instead of being evicted. Shaped
    // entries are small and shaping is expensive, so a generous bound is worth it.
    private readonly LruCache<ShapeKey, ShapedText> _shapeCache = new(capacity: 16384, maxWeight: ShapeCacheBytes);

    // Word-level shape cache. Raw shaping results keyed by space-delimited
    // segment + font instance + size, so distinct strings built from a
    // recurring vocabulary shape each word once instead of missing the
    // whole-string cache every time. Font instances are cache-stable in
    // IFontCollection implementations, so reference keys hold.
    // Known fidelity tradeoff (the same one browser word caches accept):
    // kerning across a segment boundary is lost — a font that kerns
    // letter↔space pairs would measure differently. UI fonts essentially
    // never do, and WordShapeCacheTests verifies equivalence on this system.
    // Looked up by a span of the text: a segment becomes a string of its own only on a miss, to
    // be the entry's key.
    private readonly WordCache _wordShapeCache = new(capacity: 8192, maxWeight: WordCacheBytes);

    // Widths of whole strings on one unbounded line, for MeasureWidth (Widths, below). Made at
    // the first width asked for: a pipeline that measures none — no TextArea — has none.
    private Widths? _widths;

    // The prefix measurer of each style its paragraphs asked for (PrefixMeasurer, below). Made at
    // the first: only a line that merged clusters asks.
    private Dictionary<ResolvedInheritedStyle, TextPrefixMeasurer>? _prefixMeasurers;
    private const int PrefixMeasurerCacheMaxSize = 64;

    // ── How much the caches above may hold ──
    //
    // An application keeps one pipeline for its whole life, so what these caches hold stays
    // after the text that filled them is gone, and an entry count bounds little: a label's
    // shaping keeps ~0.5 KB alive, a long line's 16 bytes a glyph more. Once closed, a CSV
    // file of 1,000-character lines without spaces in a TextArea left 23 MB of "words"
    // here, and a 5,000-block document in the rich text editor 8 MB of laid-out paragraphs.
    // So each cache is also bounded by an estimate of the bytes its entries keep alive
    // (ShapedBytes, WordBytes, LayoutBytes, WidthBytes), least recently used out first.
    //
    // The shape cache holds what a frame draws and lays out twice: a list or grid of labels,
    // measured and then arranged, and the lines a TextArea shows. 8 MB holds 5,000 labels of a
    // few words; at 4 MB the perf suite's 5,000-block rich text document no longer fitted and
    // took twice as long to open. (It was 24 MB while a TextArea found each of its lines' widths
    // here on every keystroke — 8 MB made typing in a 10,000-line JSON document go from 2 to
    // 26 ms — until the TextArea kept its own widths.) The word and layout bounds hold all that
    // the perf suite, Junction and Vesper Commander find in them again: ordinary words fill
    // the word cache's 8,192 entries with ~2 MB, and no scenario reuses more than ~1.5 MB of
    // laid-out text. The width cache holds the distinct lines of a 19,000-line JSON response
    // (~7,000 of them, ~1.8 MB) with room for the next one's.
    internal const long ShapeCacheBytes = 8L << 20;
    internal const long WordCacheBytes = 4L << 20;
    internal const long LayoutCacheBytes = 4L << 20;
    internal const long WidthCacheBytes = 4L << 20;

    // What each holds now, for the tests that hold them to their bounds.
    internal (int Entries, long Bytes) ShapeCacheUse => (_shapeCache.Count, _shapeCache.Weight);
    internal (int Entries, long Bytes) WordCacheUse => (_wordShapeCache.Count, _wordShapeCache.Weight);
    internal (int Entries, long Bytes) LayoutCacheUse => (_cache.Count, _cache.Weight);
    internal (int Entries, long Bytes) WidthCacheUse => _widths is { } w ? (w.Cache.Count, w.Cache.Weight) : (0, 0);

    // Roughly the bytes a cached result keeps alive: object headers and fields, the key's
    // string, 16 bytes a glyph. Measured with full collections (a label's shaping is ~420 bytes
    // plus 2 a character and 16 a glyph; a word's ~170 plus the same), and rounded up where a
    // result gets more later (the prefix widths of a truncated string, the break segments of
    // a wrapped one: a few bytes a character).
    private static int ShapedBytes(string text, ShapedText shaped) =>
        400 + 4 * text.Length + (shaped.SingleLine is { } line ? GlyphBytes(line) : 0);

    private static int WordBytes(string word, ShapedRun run) => 200 + 2 * word.Length + 16 * run.Glyphs.Length;

    // A width's entry: its key's string, the cache's node and its slot (measured: 215 bytes for a
    // line of 20 characters, 494 for one of 160).
    private static int WidthBytes(string text) => 180 + 2 * text.Length;

    // A layout weighs what it would with every line's runs made, read or not: lines made later
    // cannot add to a weight already in the cache, so a weight of what the lines hold now would
    // stay short of what they hold once drawn, for as long as the entry lives. A line not made yet
    // counts a glyph a character and hands its runs in through _runsNotMade.
    private int LayoutBytes(string text, TextLayout layout)
    {
        int bytes = 300 + 4 * text.Length + GlyphBytes(layout) + 80 * _runsNotMade;
        _runsNotMade = 0;
        var lines = layout.Lines;
        for (int i = 0; i < lines.Count; i++)
            bytes += 120 + (lines[i].RunsMade ? 80 * lines[i].Runs.Count : 0);
        return bytes;
    }

    // How many runs the lines not made since the last LayoutBytes will make.
    private int _runsNotMade;

    private static int GlyphBytes(TextLayout layout)
    {
        int glyphs = 0;
        var lines = layout.Lines;
        for (int i = 0; i < lines.Count; i++)
        {
            var line = lines[i];
            if (!line.RunsMade)
            {
                glyphs += line.DrawnLength;
                continue;
            }
            var runs = line.Runs;
            for (int r = 0; r < runs.Count; r++)
                glyphs += runs[r].GlyphCount;
        }
        return 16 * glyphs;
    }

    // Digit fast path. All-digit segments (row ids, counters) are each unique,
    // so they miss every string-level cache forever and pay a full shaper call.
    // For fonts that provably shape digits context-free, compose them from a
    // per-(font,size) advance table instead — and skip the word cache entirely,
    // so unique ids stop churning its LRU.
    // Prove-then-enable: at first use the font+size shapes every digit and all
    // 100 digit pairs once; any multi-glyph result, nonzero Y, kerning
    // adjustment, or contextual substitution disqualifies it (entry = null) and
    // digits take the normal shaper path. Never enabled on assumption.
    private readonly Dictionary<DigitKey, DigitTable?> _digitTables = new();

    private readonly record struct DigitKey(IResolvedFont Font, float FontSize);

    private sealed class DigitTable
    {
        public readonly uint[] GlyphIds = new uint[10];
        public readonly float[] Advances = new float[10];
    }

    public IFontCollection Fonts => _fonts;
    public ITextShaper Shaper => _shaper;

    public TextPipeline(IFontCollection fonts, ITextShaper shaper)
    {
        _fonts = fonts;
        _shaper = shaper;
        _cutShaper = shaper as IShapesWithCuts;
    }

    // The shaper, when it can say where a run can be cut (RunCuts): a long word is shaped with its
    // cuts, and a prefix table reads that word cut short from them.
    private readonly IShapesWithCuts? _cutShaper;

    /// <summary>
    /// When false, a prefix of a word that a prefix table cuts short is shaped as a string of its
    /// own, always: the reference. When true (the default), it is read off the whole word's run
    /// where the shaper said a cut there is safe (<see cref="RunCuts"/>), the same width and glyph
    /// count to the bit, and shaped only where it is not.
    /// </summary>
    internal bool PrefixesFromCuts { get; init; } = true;

    private bool ReadsCuts => _cutShaper is not null && PrefixesFromCuts;

    // The shortest word a prefix table shapes with its cuts (FillPrefixTable: only its words are cut
    // short). A table cuts long words (text without spaces: a row of CJK is one); a short one costs
    // the cuts' bytes for few prefixes.
    internal const int MinCutWord = 8;

    // How many words cut short were read off cuts, and how many had to be shaped there (a word cache
    // miss the cuts refused), for the tests; the reference path counts neither.
    internal int PrefixesCut { get; private set; }
    internal int PrefixesShaped { get; private set; }

    /// <summary>
    /// The metrics of the face <paramref name="style"/> resolves to, at its size. The vertical
    /// metrics are the ones a line of that face is laid out with; the character width is the
    /// shaped advance of <c>"0"</c>, so a grid that steps by it stays on the glyphs this pipeline
    /// draws. Decoration lines the font does not give fall back to the renderer's estimates.
    /// </summary>
    public FontMetrics MeasureFont(ResolvedInheritedStyle style)
    {
        var font = _fonts.Resolve(style.FontFamily, style.FontWeight, style.FontSlant);
        float size = style.FontSize;
        var zero = Layout("0", style, float.PositiveInfinity, 1);
        var estimate = FontMetrics.Estimate(style, zero.Size);
        return new FontMetrics(
            FamilyName: font.FamilyName,
            Ascent: MathF.Abs(font.Ascent) * size,
            Descent: font.Descent * size,
            LineGap: font.Leading * size,
            CharacterWidth: zero.Size.Width,
            IsFixedPitch: font.IsFixedPitch,
            UnderlinePosition: font.Underline is { } u ? u.Position * size : estimate.UnderlinePosition,
            UnderlineThickness: font.Underline is { } ut && ut.Thickness > 0 ? ut.Thickness * size : estimate.UnderlineThickness,
            StrikeoutPosition: font.Strikeout is { } s ? s.Position * size : estimate.StrikeoutPosition,
            StrikeoutThickness: font.Strikeout is { } st && st.Thickness > 0 ? st.Thickness * size : estimate.StrikeoutThickness);
    }

    /// <summary>Whether the face <paramref name="style"/> resolves to has its own glyph for
    /// <paramref name="codePoint"/>: true means no fallback font draws it.</summary>
    public bool HasGlyph(ResolvedInheritedStyle style, int codePoint) =>
        _fonts.Resolve(style.FontFamily, style.FontWeight, style.FontSlant).HasGlyph(codePoint);

    // ── The paragraph's CJK context ──
    //
    // What the whole text says its Han characters are (kana → ja, Hangul → ko), for font
    // fallback. It has to be the whole text, not what Itemize is handed: wrapping itemizes
    // line-break segments to measure them — in CJK often a single ideograph with no kana in
    // sight — and a segment must be measured in the face its finished line is drawn in.
    // The public entry points record the paragraph, and the scan runs only when a cache miss
    // actually itemizes, at most once per call. Every cache stays correct without a key
    // change: the text-keyed ones because the context is a function of the text, the word
    // and digit ones because they key on the font instance, which carries the choice.
    private string _paragraph = "";
    private string? _hanContext;
    private bool _hanContextKnown;

    // The UI culture the text-keyed caches were filled under. Han without kana follows it
    // (ScriptLanguage), and those caches key on the text alone, so a language switch at
    // runtime would otherwise keep serving layouts in the old culture's glyphs. The word
    // and digit caches key on the font instance and stay valid.
    private System.Globalization.CultureInfo? _culture;

    private void BeginParagraph(string text)
    {
        _paragraph = text;
        _hanContextKnown = false;
        FollowCulture();
        // Every public call that measures or lays text out begins here: the view of a range's
        // prefix widths (MeasurePrefixes with a range) is good until the next one, and lets go
        // of its text — a text area's whole document — here.
        if (_prefixView is { Holding: true } view)
            view.Release();
        _lineSource = null;
    }

    private void FollowCulture()
    {
        var culture = System.Globalization.CultureInfo.CurrentUICulture;
        if (!ReferenceEquals(culture, _culture))
        {
            if (_culture is not null)
            {
                _cache.Clear();
                _shapeCache.Clear();
                _widths?.Cache.Clear();
                _minContentCache.Clear();
                _memos.Clear();
                _memoWidths.Clear();
                _memoLines.Clear();
                _lineStyles?.Clear();
                _lineStyle = null;
                Array.Clear(_seen);
            }
            _culture = culture;
        }
    }

    // Into the pipeline's kept list (_fontRuns), good until the next call: every
    // caller reads the runs before it itemizes again, and nothing it calls while it reads them
    // itemizes into this list (a foreach over it would throw if anything did). A new list a call
    // was 14–27 % of what styled, BiDi and fallback text allocated per layout (plan 17, round 5).
    private List<FontRun> Itemize(string text, ResolvedInheritedStyle style) =>
        FontItemizer.Itemize(text, _fonts, style.FontFamily, style.FontWeight, style.FontSlant, ParagraphHanContext(),
            Kept(_fontRuns));

    private string? ParagraphHanContext()
    {
        if (!_hanContextKnown)
        {
            _hanContext = ScriptLanguage.CjkContextOf(_paragraph);
            _hanContextKnown = true;
        }
        return _hanContext;
    }

    private readonly List<FontRun> _fontRuns = new();

    // The most items a kept scratch list holds room for between uses: a line of a few thousand
    // runs or BiDi pieces. One that grew past it for a long paragraph is let go of at its next use.
    private const int KeptScratchItems = 4096;

    private static List<T> Kept<T>(List<T> scratch)
    {
        scratch.Clear();
        if (scratch.Capacity > KeptScratchItems) scratch.TrimExcess();
        return scratch;
    }

    // The font runs of text[from..end), itemized as a string of those characters would be, into
    // `into` (cleared first) as ranges of `text`: no string per run. Lines and measures itemize
    // this way, so a line cuts out only the strings its shaped runs keep, and a measure none.
    private void ItemizeRange(string text, int from, int end, ResolvedInheritedStyle style, List<RunRange> into)
    {
        string? hanContext = ParagraphHanContext();
        into.Clear();
        var sink = new RangeRuns(from, into);
        FontItemizer.Itemize(text.AsSpan(from, end - from), _fonts, style.FontFamily, style.FontWeight, style.FontSlant,
            hanContext, ref sink);
    }

    // The same for characters that may be no string's, in the Han context given: their runs,
    // offset by `offset`.
    private void ItemizeChars(ReadOnlySpan<char> text, int offset, ResolvedInheritedStyle style, string? hanContext,
        List<RunRange> into)
    {
        into.Clear();
        var sink = new RangeRuns(offset, into);
        FontItemizer.Itemize(text, _fonts, style.FontFamily, style.FontWeight, style.FontSlant, hanContext, ref sink);
    }

    // A font run as where it starts and ends in the text it was itemized from.
    private readonly record struct RunRange(int Start, int End, IResolvedFont Font);

    private readonly struct RangeRuns(int offset, List<RunRange> runs) : FontItemizer.IRunSink
    {
        public void Add(int start, int end, IResolvedFont font) => runs.Add(new RunRange(offset + start, offset + end, font));
    }

    // ── Metric-affecting spans (stage 2 of docs/plans/12-rich-text-spans.md) ──
    //
    // A span carrying Weight/Slant/FontSize changes shaping, so the layout must see
    // it. Style segmentation happens BEFORE font itemization: the string is split at
    // metric-span boundaries, each segment itemized with its effective weight/slant
    // and shaped at its effective size. Line breaking stays one pass — UAX #14 break
    // opportunities are style-independent. Plain and color-only-span text keeps
    // today's paths, caches and keys untouched.

    /// <summary>True when any span would change shaping or measurement.</summary>
    public static bool AnyMetricAffecting(IReadOnlyList<Core.Rendering.TextSpan>? spans)
    {
        if (spans is null) return false;
        for (int i = 0; i < spans.Count; i++)
            if (spans[i].IsMetricAffecting)
                return true;
        return false;
    }

    // Layout-relevant digest of a span list: offsets and metric fields only. Colors
    // and decorations are paint-only and must not fragment the layout caches.
    //
    // The nullable fields go in as plain values, an unset one as a value no set one has (-1 for
    // the enums, NaN for the size): HashCode.Add of a Nullable<T> boxed the value on .NET 11's JIT
    // inside this loop, two boxes (48 B) for each metric span of every styled layout, cache hits
    // included (text/styled wrap 150 paragraphs resize 74.8 -> 225.7 KB a step). .NET 10's JIT and
    // NativeAOT on both did not box it.
    private static int MetricDigest(IReadOnlyList<Core.Rendering.TextSpan> spans)
    {
        var hash = new HashCode();
        for (int i = 0; i < spans.Count; i++)
        {
            var s = spans[i];
            if (!s.IsMetricAffecting) continue;
            hash.Add(s.Start); hash.Add(s.Length);
            hash.Add(s.Weight is { } weight ? (int)weight : -1);
            hash.Add(s.Slant is { } slant ? (int)slant : -1);
            hash.Add(s.FontSize is { } size ? size : float.NaN);
            hash.Add(s.FontFamily);
        }
        return hash.ToHashCode();
    }

    /// <summary>Span-aware layout. Falls back to the plain path unless some span is
    /// metric-affecting; spans are absolute offsets into <paramref name="text"/>.</summary>
    public TextLayout Layout(string text, ResolvedInheritedStyle style, float maxWidth, int? maxLines,
        TextOverflow overflow, IReadOnlyList<Core.Rendering.TextSpan>? spans)
        => Layout(text, style, maxWidth, maxLines, overflow, spans, out _);

    /// <summary>Span-aware layout, and the least <paramref name="maxWidth"/> from which the same
    /// layout comes back for every greater one (NaN when that is not known): see
    /// <see cref="Layout(string, ResolvedInheritedStyle, float, int?, TextOverflow, out float)"/>.
    /// Styled text has no single-line fast path, so it never knows.</summary>
    public TextLayout Layout(string text, ResolvedInheritedStyle style, float maxWidth, int? maxLines,
        TextOverflow overflow, IReadOnlyList<Core.Rendering.TextSpan>? spans, out float sameFromWidth)
    {
        if (!AnyMetricAffecting(spans))
            return Layout(text, style, maxWidth, maxLines, overflow, out sameFromWidth);
        sameFromWidth = float.NaN;
        BeginParagraph(text);
        if (string.IsNullOrEmpty(text))
            return TextLayout.Empty;

        // Right-to-left script, or an Rtl paragraph, takes the direction-aware path with its
        // spans: direction runs, then style runs, then font runs (docs/plans/14-bidi.md). The
        // styled path below is left-to-right only — it used to get this text too, and one
        // bold word was enough to lay a Hebrew paragraph out left to right.
        bool needsBidi = NeedsBidi(text, style, PlainPathForLtr);
        int digest = MetricDigest(spans!);

        // Styled text skips the width-independent single-line fast path for now and
        // goes straight to the width-keyed cache; the digest keeps styled and plain
        // results apart without touching plain keys, and the direction keeps an Rtl
        // and an Auto layout of the same text apart, as on the plain BiDi path.
        var key = new MeasurementKey(text, style.FontFamily, style.FontSize, style.FontWeight,
            style.FontSlant, maxWidth, maxLines ?? -1, overflow, style.LineHeight,
            style.LetterSpacing, digest, needsBidi ? style.TextDirection : TextDirection.Ltr);
        if (_cache.TryGet(key, out var cached))
            return cached;

        var memo = MemoOf(text, style, spans, digest, bidi: needsBidi);
        var layout = needsBidi
            ? ComputeWrappedBidi(text, style, maxWidth, maxLines, overflow, memo, spans)
            : ComputeWrappedStyled(text, style, spans!, maxWidth, maxLines, overflow, memo);
        (_laidOutMemo, _laidOutKey) = (null, default);
        _cache.Put(key, layout, LayoutBytes(text, layout));
        return layout;
    }

    public TextLayout Layout(string text, ResolvedInheritedStyle style, float maxWidth, int? maxLines,
        TextOverflow overflow = TextOverflow.Clip)
        => Layout(text, style, maxWidth, maxLines, overflow, out _);

    /// <summary>Layout, and the least <paramref name="maxWidth"/> from which the same layout comes
    /// back for every greater one: for one line that fits, its own width — the fast path below
    /// returns the same single line for any width it fits in. NaN otherwise: wrapped and truncated
    /// text, and text on the BiDi path, are laid out again at each width.</summary>
    public TextLayout Layout(string text, ResolvedInheritedStyle style, float maxWidth, int? maxLines,
        TextOverflow overflow, out float sameFromWidth)
    {
        sameFromWidth = float.NaN;
        BeginParagraph(text);
        if (string.IsNullOrEmpty(text))
            return TextLayout.Empty;

        // Text that can contain right-to-left script — or an Rtl paragraph style —
        // takes the direction-aware path (docs/plans/14-bidi.md), cached under the
        // same width-keyed cache as wrapped text, with Direction in the key.
        //
        // The classifier deliberately stays *before* the caches, running on every
        // call rather than once per string. Consulting the shape cache first would
        // answer the same question for free on a hit (the BiDi path never populates
        // that cache, so a hit proves the string is RTL-free) — measured, it buys
        // 0.16 µs on a 4,700-character warm layout and costs every warm BiDi lookup
        // ~45%, for a more tangled Layout. Not worth it.
        if (NeedsBidi(text, style, PlainPathForLtr))
        {
            var bidiKey = new MeasurementKey(text, style.FontFamily, style.FontSize, style.FontWeight,
                style.FontSlant, maxWidth, maxLines ?? -1, overflow, style.LineHeight,
                style.LetterSpacing, 0, style.TextDirection);
            if (_cache.TryGet(bidiKey, out var cachedBidi))
                return cachedBidi;
            var bidiLayout = ComputeWrappedBidi(text, style, maxWidth, maxLines, overflow,
                MemoOf(text, style, spans: null, spanDigest: 0, bidi: true));
            (_laidOutMemo, _laidOutKey) = (null, default);
            _cache.Put(bidiKey, bidiLayout, LayoutBytes(text, bidiLayout));
            return bidiLayout;
        }

        var shapeKey = new ShapeKey(text, style.FontFamily, style.FontSize, style.FontWeight,
            style.FontSlant, style.LineHeight, style.LetterSpacing);
        if (!_shapeCache.TryGet(shapeKey, out var shaped))
        {
            shaped = ComputeShaping(text, style);
            _shapeCache.Put(shapeKey, shaped, ShapedBytes(text, shaped));
        }

        // Single line that fits: the whole layout is width-independent, so the
        // shaped result is returned directly for any width that accommodates it.
        // Nothing is truncated, so the overflow mode is irrelevant here.
        if (!shaped.HasNewline && shaped.TotalWidth <= maxWidth)
        {
            sameFromWidth = shaped.TotalWidth;
            return shaped.SingleLine!;
        }

        // Explicit newlines or overflow: line breaking depends on the width, so
        // fall back to the width-keyed cache. Overflow is part of the key because
        // Ellipsis rewrites the truncated line, producing a different layout.
        var key = new MeasurementKey(text, style.FontFamily, style.FontSize, style.FontWeight,
            style.FontSlant, maxWidth, maxLines ?? -1, overflow, style.LineHeight, style.LetterSpacing);
        if (_cache.TryGet(key, out var cached))
            return cached;

        TextLayout layout;
        if (maxLines == 1 && overflow == TextOverflow.Clip && !shaped.HasNewline)
        {
            // One clipped line is the longest prefix that fits, cut at a character.
            // The wrap path would break it at the last *word* boundary before maxWidth,
            // which for text with few break opportunities — a path, a URL, a minified
            // JSON line — ends the line far short of the box and reads as though the
            // text simply stopped. Clipping is what the caller asked for; breaking is
            // what Ellipsis does, minus the ellipsis.
            layout = ClipToWidth(text, style, maxWidth, shaped);
        }
        else if (maxLines == 1 && overflow == TextOverflow.Ellipsis && !shaped.HasNewline
            && (EllipsizeOneLine(text, style, shaped, maxWidth)
                ?? EllipsizeMeasuredLine(text, style, shaped, maxWidth)) is { } oneLine)
        {
            layout = oneLine;
        }
        else
            layout = ComputeWrapped(text, style, maxWidth, maxLines, overflow, shaped);
        _cache.Put(key, layout, LayoutBytes(text, layout));
        return layout;
    }

    /// <summary>
    /// <paramref name="text"/> as one paragraph at <paramref name="maxWidth"/>, kept for caret,
    /// hit-test and selection questions
    /// (docs/plans/16-rich-text-editor.md). The layout is the one <see cref="Layout(string,
    /// ResolvedInheritedStyle, float, int?, TextOverflow, IReadOnlyList{Core.Rendering.TextSpan}?)"/>
    /// returns and caches; only the empty line after a final newline is new.
    /// </summary>
    public PipelineTextParagraph LayoutParagraph(string text, ResolvedInheritedStyle style, float maxWidth,
        IReadOnlyList<Core.Rendering.TextSpan>? spans, int? maxLines = null, TextOverflow overflow = TextOverflow.Clip)
    {
        var layout = Layout(text, style, maxWidth, maxLines, overflow, spans);
        return new PipelineTextParagraph(this, text, style, maxWidth, spans, maxLines, overflow, layout);
    }

    /// <summary>A line with no text at <paramref name="start"/>, as tall as the style's font
    /// makes one: where a caret goes in an empty paragraph or after its final newline.</summary>
    internal TextLine EmptyLine(ResolvedInheritedStyle style, int start) => ShapeLineText("", 0, 0, style, start);

    // The longest prefix of <paramref name="text"/> that fits <paramref name="maxWidth"/>.
    // Only whole characters are kept: DrawText does not clip to its bounds, so emitting the
    // glyph that straddles the edge would let it spill out of the box.
    private TextLayout ClipToWidth(string text, ResolvedInheritedStyle style, float maxWidth, ShapedText shaped)
    {
        // Not even one character fits. The line is still a line: it occupies its font's
        // height, and an empty layout here would tell MeasureContent that a non-empty
        // string needs no vertical space. Flex measures children against a zero basis as a
        // matter of course, so this runs during ordinary layout, and a zero height there
        // collapses whatever contains the text.
        if (maxWidth <= 0) return EmptyLineOfHeight(style);

        int best = FitPrefix(text, 0, text.Length, style, maxWidth, PrefixWidthsOf(shaped, text, style));
        if (best == 0) return EmptyLineOfHeight(style);

        var line = ShapeLineText(text, 0, best, style, start: 0);
        return new TextLayout([line], new Size(line.Width, line.Height));
    }

    /// <summary>
    /// The largest <c>i</c> in <c>[from, to]</c> whose slice <c>text[from..i]</c> still fits
    /// <paramref name="maxWidth"/>. Returns <paramref name="from"/> when not even one
    /// character does; callers decide what that means.
    /// </summary>
    /// <remarks>
    /// Gallops out from a short slice before bisecting, so every string measured stays within
    /// twice the answer. Bisecting the whole range instead would start by shaping half of what
    /// may be a 25,000-character line to discover that ~200 of them fit.
    /// <para><paramref name="prefixes"/>, when given, is the prefix widths of
    /// <c>text[from..to]</c> (or of more of the text from <paramref name="from"/> on);
    /// <paramref name="keptTable"/> says the kept lists hold those of <c>text[from..to]</c>
    /// (<see cref="FillPrefixTable"/>, a wrap's last line).</para>
    /// </remarks>
    private int FitPrefix(string text, int from, int to, ResolvedInheritedStyle style, float maxWidth,
        PrefixWidths? prefixes = null, bool keptTable = false)
    {
        int span = 1;
        while (from + span < to && Measure(from + span) <= maxWidth)
            span <<= 1;

        int lo = from + Math.Min(span >> 1, to - from);
        int hi = from + Math.Min(span, to - from);
        int best = lo;
        while (lo <= hi)
        {
            int mid = (lo + hi) >> 1;
            if (Measure(mid) <= maxWidth) { best = mid; lo = mid + 1; }
            else hi = mid - 1;
        }
        return SnapPrefix(text, best);   // never split a surrogate pair

        float Measure(int end) => keptTable
            ? PrefixWidth(text, from, to - from, System.Runtime.InteropServices.CollectionsMarshal.AsSpan(_prefixRuns),
                System.Runtime.InteropServices.CollectionsMarshal.AsSpan(_prefixSegments), style.FontSize,
                style.LetterSpacing, end - from, style)
            : prefixes is null
                ? MeasureRange(text, from, end, style)
                : PrefixWidth(prefixes, end - from, style);
    }

    /// <summary>
    /// Cuts <c>text[from..to]</c> into lines at character boundaries, appending all but the
    /// final remainder to <paramref name="lines"/> and returning where that remainder starts.
    ///
    /// <para>For a run with no break opportunity in it — a hash, a base64 blob, a path with
    /// no separators — this is the only thing that can make it fit. Without it the run is
    /// emitted as a single line wider than the box, and simply runs out of it.</para>
    ///
    /// <para><paramref name="keptTable"/> says the kept lists hold the prefix widths of
    /// <c>text[from..to]</c>, for the first cut only.</para>
    /// </summary>
    private int SplitByCharacter(string text, int from, int to, ResolvedInheritedStyle style,
        float maxWidth, List<TextLine> lines, int? maxLines, bool keptTable = false,
        PrefixWidths? measureOnly = null)
    {
        while (from < to && (!maxLines.HasValue || lines.Count < maxLines.Value))
        {
            int fit = FitPrefix(text, from, to, style, maxWidth, keptTable: keptTable);
            keptTable = false;
            // A box too narrow for even one character would otherwise never advance. One
            // character per line is the sane floor, and what a browser does.
            if (fit <= from) fit = NextCodePoint(text, from);
            if (fit >= to) break;             // what is left fits a line of its own
            lines.Add(measureOnly is null
                ? ShapeLineText(text, from, fit, style, start: from)
                : MeasureLineText(text, from, fit, style, measureOnly));
            from = fit;
        }
        return from;
    }

    // A line with no glyphs but the full height of one, taken from the primary font.
    private TextLayout EmptyLineOfHeight(ResolvedInheritedStyle style)
    {
        var line = ShapeLineText("", 0, 0, style, start: 0);
        return new TextLayout([line], new Size(0, line.Height));
    }

    // Width-independent: itemize + shape the whole string, record its natural
    // width, and (when there is no explicit newline) prebuild the single-line
    // layout that any sufficiently wide measurement will reuse.
    private ShapedText ComputeShaping(string text, ResolvedInheritedStyle style)
    {
        var fontRuns = Itemize(text, style);
        if (fontRuns.Count == 0)
            return new ShapedText(0, HasNewline: false, TextLayout.Empty);

        var shapedRuns = new List<ShapedRun>(fontRuns.Count);
        float totalWidth = 0;
        foreach (var run in fontRuns)
        {
            var shaped = ShapeWithSpacing(run.Text, run.Font, style.FontSize, style.LetterSpacing);
            shapedRuns.Add(shaped);
            totalWidth += shaped.Width;
        }

        bool hasNewline = text.Contains('\n');
        var singleLine = hasNewline ? null : BuildSingleLineLayout(shapedRuns, totalWidth, text.Length, style.LineHeight);
        return new ShapedText(totalWidth, hasNewline, singleLine);
    }

    // Multi-line path: the text either contains explicit newlines or is wider
    // than the available width. BreakIntoLines measures segment by segment and
    // shapes each line it keeps; nothing here is shaped as a whole first, because
    // nothing would read it. The segments and their widths come from the string's
    // shaping-cache entry, so only the lines kept are new work at a new width.
    //
    // With Ellipsis, the last line of the clipped lines is rewritten with a trailing
    // ellipsis when content was truncated. Truncation happens either because a maxLines
    // limit dropped trailing text, or because a single unbreakable token is wider than the
    // available width. Only the final line is touched; earlier lines keep their exact
    // shaping and positions.
    private TextLayout ComputeWrapped(string text, ResolvedInheritedStyle style, float maxWidth, int? maxLines,
        TextOverflow overflow, ShapedText shaped)
    {
        var lines = RentLines();
        var size = BreakIntoLines(lines, text, style, maxWidth, maxLines, BreakSegmentsOf(shaped, text));
        if (overflow == TextOverflow.Ellipsis && TruncatedTail(text, lines, maxWidth, out int start, out int tailEnd))
        {
            string tail = text[start..tailEnd];
            float width = WithLastLine(lines, BuildEllipsizedLine(text, tail, start, style, maxWidth,
                ReuseAcrossWidths ? BuildPrefixWidths(tail, style) : null, MeasureString(EllipsisString, style)));
            size = new Size(width, size.Height);
        }
        return new TextLayout(LayoutLines(lines), size);
    }

    // ── Styled layout internals ──
    // A styled line is segments cut at metric-span boundaries, each itemized with its
    // effective weight/slant and shaped at its effective size. Offsets are absolute
    // into the source string throughout, because span ranges are.

    // Both lookups below binary-search the spans, which TextSpan requires sorted by start and
    // apart. They are asked once per piece of every line, and a scan of every span for each
    // made a long paragraph's layout quadratic: 600 spans, 1,300 pieces, a million steps a
    // keystroke in a rich text editor.

    private ResolvedInheritedStyle EffectiveStyleAt(
        ResolvedInheritedStyle style, IReadOnlyList<Core.Rendering.TextSpan> spans, int offset) =>
        EffectiveStyleAt(style, spans, offset, FirstSpanEndingAfter(spans, offset));

    // i is FirstSpanEndingAfter(spans, offset), which a piece of a line asks for twice over.
    private static ResolvedInheritedStyle EffectiveStyleAt(
        ResolvedInheritedStyle style, IReadOnlyList<Core.Rendering.TextSpan> spans, int offset, int i)
    {
        if (i >= spans.Count) return style;
        var s = spans[i];
        if (!s.IsMetricAffecting || offset < s.Start) return style;
        return style with
        {
            FontWeight = s.Weight ?? style.FontWeight,
            FontSlant = s.Slant ?? style.FontSlant,
            FontSize = s.FontSize ?? style.FontSize,
            FontFamily = s.FontFamily ?? style.FontFamily,
        };
    }

    // The nearest metric-span edge after <paramref name="offset"/>, clamped to limit: the end of
    // the metric span holding it, else the start of the first one after it.
    private static int NextStyleBoundary(IReadOnlyList<Core.Rendering.TextSpan> spans, int offset, int limit) =>
        NextStyleBoundary(spans, offset, limit, FirstSpanEndingAfter(spans, offset));

    private static int NextStyleBoundary(IReadOnlyList<Core.Rendering.TextSpan> spans, int offset, int limit, int first)
    {
        for (int i = first; i < spans.Count; i++)
        {
            var s = spans[i];
            if (s.Start >= limit) break;
            if (!s.IsMetricAffecting) continue;
            return Math.Min(limit, s.Start > offset ? s.Start : s.End);
        }
        return limit;
    }

    private static int FirstSpanEndingAfter(IReadOnlyList<Core.Rendering.TextSpan> spans, int offset)
    {
        int lo = 0, hi = spans.Count;
        while (lo < hi)
        {
            int mid = (lo + hi) >> 1;
            if (spans[mid].End <= offset) lo = mid + 1;
            else hi = mid;
        }
        return lo;
    }

    private float MeasureStyled(string text, int from, int to,
        ResolvedInheritedStyle style, IReadOnlyList<Core.Rendering.TextSpan> spans)
    {
        float width = 0;
        int i = from;
        while (i < to)
        {
            int first = FirstSpanEndingAfter(spans, i);
            int b = NextStyleBoundary(spans, i, to, first);
            width += MeasureRange(text, i, b, EffectiveStyleAt(style, spans, i, first));
            i = b;
        }
        return width;
    }

    private TextLine ShapeStyledLine(string text, int from, int to,
        ResolvedInheritedStyle style, IReadOnlyList<Core.Rendering.TextSpan> spans, TextMemo? memo,
        bool appendEllipsis = false)
    {
        int sourceLength = to - from + (appendEllipsis ? 1 : 0);
        // Trailing whitespace is trimmed for width, as in ShapeLineText.
        while (to > from && char.IsWhiteSpace(text[to - 1])) to--;

        // A first layout with many spans makes its runs now (MostSpansCopied).
        var runs = ComposeRunsWhenRead && (memo is not null || spans.Count <= MostSpansCopied) ? null : new List<ShapedRun>();
        var extent = StyledRuns(text, from, to, style, spans, appendEllipsis, ParagraphHanContext(), _lineRuns, runs);

        if (extent.Runs == 0)
        {
            var primary = _fonts.Resolve(style.FontFamily, style.FontWeight, style.FontSlant);
            extent.Ascent = Math.Abs(primary.Ascent) * style.FontSize;
            extent.Descent = primary.Descent * style.FontSize;
            extent.Leading = primary.Leading * style.FontSize;
        }

        var (ascent, leading) = ApplyLineHeight(extent.Ascent, extent.Descent, extent.Leading, style.LineHeight);
        TextLine line;
        if (runs is not null)
            line = new TextLine(runs, extent.Width, ascent, extent.Descent, leading);
        else if (extent.Runs == 0)
            line = new TextLine([], extent.Width, ascent, extent.Descent, leading);
        else
        {
            _runsNotMade += extent.Runs;
            line = new TextLine(SourceOf(text, style, spans, memo, appendEllipsis), to - from + (appendEllipsis ? 1 : 0),
                extent.Width, ascent, extent.Descent, leading);
        }
        line.Start = from;
        line.Length = sourceLength;
        return line;
    }

    // What walking a styled line's pieces comes to: how many runs, their width side by side, and
    // the largest of each metric over them.
    private struct StyledExtent
    {
        public int Runs;
        public float Width, Ascent, Descent, Leading;
    }

    // The runs of the styled line text[from..to], in order, and what they come to. With `into`,
    // shaped and added to it; without, measured (MeasureRun): the same widths added in the same
    // order, so a line's width and metrics are the same to the bit whether its runs are made with
    // it or when read (ComposeRuns, which calls this with the Han context the line was laid out
    // in and a list of its own).
    private StyledExtent StyledRuns(string text, int from, int to, ResolvedInheritedStyle style,
        IReadOnlyList<Core.Rendering.TextSpan> spans, bool appendEllipsis, string? hanContext, List<RunRange> fontRuns,
        List<ShapedRun>? into)
    {
        var extent = new StyledExtent();
        for (int i = from; i < to;)
        {
            int first = FirstSpanEndingAfter(spans, i);
            int b = NextStyleBoundary(spans, i, to, first);
            var eff = EffectiveStyleAt(style, spans, i, first);
            ItemizeChars(text.AsSpan(i, b - i), i, eff, hanContext, Kept(fontRuns));
            foreach (var (runStart, runEnd, font) in fontRuns)
                AddStyledRun(ref extent, text, runStart, runEnd, font, eff.FontSize, eff.LetterSpacing, into);
            i = b;
        }
        fontRuns.Clear();   // the runs' faces are not kept alive by the scratch list

        // The ellipsis is the element's own text, not the span's: a truncation mark
        // rendered bold because the last word happened to be bold reads as content.
        if (appendEllipsis)
        {
            ItemizeChars(EllipsisString, 0, style, hanContext, Kept(fontRuns));
            foreach (var (runStart, runEnd, font) in fontRuns)
                AddStyledRun(ref extent, EllipsisString, runStart, runEnd, font, style.FontSize, style.LetterSpacing, into);
            fontRuns.Clear();
        }
        return extent;
    }

    private void AddStyledRun(ref StyledExtent extent, string text, int start, int end, IResolvedFont font,
        float fontSize, float letterSpacing, List<ShapedRun>? into)
    {
        if (into is null)
            extent.Width += MeasureRun(text, start, end, font, fontSize, letterSpacing);
        else
        {
            var shaped = ShapeWithSpacing(text[start..end], font, fontSize, letterSpacing);
            shaped.OffsetX = extent.Width;
            extent.Width += shaped.Width;
            into.Add(shaped);
        }
        extent.Runs++;
        // A larger-size run raises its whole line, the same max-across-runs
        // rule font fallback already applies.
        extent.Ascent = Math.Max(extent.Ascent, Math.Abs(font.Ascent) * fontSize);
        extent.Descent = Math.Max(extent.Descent, font.Descent * fontSize);
        extent.Leading = Math.Max(extent.Leading, font.Leading * fontSize);
    }

    // Styled twin of BuildEllipsizedLine: the longest styled prefix that leaves room
    // for the ellipsis, plus the ellipsis. Width is monotonic in prefix length, so
    // this converges in O(log n) measurements however long the tail is.
    private TextLine BuildStyledEllipsizedLine(string text, int start, int tailEnd,
        ResolvedInheritedStyle style, IReadOnlyList<Core.Rendering.TextSpan> spans, float maxWidth, TextMemo? memo)
    {
        float ellipsisWidth = MeasureString(EllipsisString, style);
        int lo = start, hi = tailEnd, best = start;
        while (lo <= hi)
        {
            int mid = (lo + hi) >> 1;
            if (MeasureStyled(text, start, mid, style, spans, memo) + ellipsisWidth <= maxWidth)
            {
                best = mid;
                lo = mid + 1;
            }
            else hi = mid - 1;
        }
        return StyledLine(text, start, SnapPrefix(text, best), style, spans, memo, appendEllipsis: true);
    }

    // Styled twin of FitPrefix: gallop out, then bisect, measuring with spans.
    private int StyledFitPrefix(string text, int from, int to,
        ResolvedInheritedStyle style, IReadOnlyList<Core.Rendering.TextSpan> spans, float maxWidth, TextMemo? memo)
    {
        int span = 1;
        while (from + span < to && MeasureStyled(text, from, from + span, style, spans, memo) <= maxWidth)
            span <<= 1;

        int lo = from + Math.Min(span >> 1, to - from);
        int hi = from + Math.Min(span, to - from);
        int best = lo;
        while (lo <= hi)
        {
            int mid = (lo + hi) >> 1;
            if (MeasureStyled(text, from, mid, style, spans, memo) <= maxWidth) { best = mid; lo = mid + 1; }
            else hi = mid - 1;
        }
        return SnapPrefix(text, best);
    }

    // The styled twin of ComputeWrapped + BreakIntoLines: same greedy break logic,
    // measurement and line shaping made span-aware, same truncation policy.
    private TextLayout ComputeWrappedStyled(string text, ResolvedInheritedStyle style,
        IReadOnlyList<Core.Rendering.TextSpan> spans, float maxWidth, int? maxLines,
        TextOverflow overflow, TextMemo? memo)
    {
        var allBreaks = memo?.Breaks ?? BreakOffsets(text);

        var lines = RentLines();
        int textOffset = 0;
        float lineWidth = 0;

        for (int s = 1; s < allBreaks.Count; s++)
        {
            int segStart = allBreaks[s - 1];
            int segEnd = allBreaks[s];

            if (segEnd - segStart == 1 && text[segStart] == '\n')
            {
                lines.Add(StyledLine(text, textOffset, segStart, style, spans, memo));
                textOffset = segEnd;
                lineWidth = 0;
                if (maxLines.HasValue && lines.Count >= maxLines.Value) break;
                continue;
            }

            float segWidth = StyledSegmentWidth(text, allBreaks, s, style, spans, memo);

            if (lineWidth + segWidth > maxWidth && lineWidth > 0)
            {
                lines.Add(StyledLine(text, textOffset, segStart, style, spans, memo));
                textOffset = segStart;
                lineWidth = 0;
                if (maxLines.HasValue && lines.Count >= maxLines.Value) break;
            }

            if (lineWidth == 0 && segWidth > maxWidth)
            {
                // Unbreakable overlong segment: cut by character, span-aware.
                while (textOffset < segEnd && (!maxLines.HasValue || lines.Count < maxLines.Value))
                {
                    int fit = StyledFitPrefix(text, textOffset, segEnd, style, spans, maxWidth, memo);
                    if (fit <= textOffset) fit = NextCodePoint(text, textOffset);
                    if (fit >= segEnd) break;
                    lines.Add(StyledLine(text, textOffset, fit, style, spans, memo));
                    textOffset = fit;
                }
                if (maxLines.HasValue && lines.Count >= maxLines.Value) break;
                lineWidth = MeasureStyled(text, textOffset, segEnd, style, spans, memo);
                continue;
            }

            lineWidth += segWidth;
        }

        if (textOffset < text.Length && (!maxLines.HasValue || lines.Count < maxLines.Value))
            lines.Add(StyledLine(text, textOffset, text.Length, style, spans, memo));

        float totalHeight = 0, maxLineWidth = 0;
        foreach (var line in lines)
        {
            line.OffsetY = totalHeight;
            totalHeight += line.Height;
            maxLineWidth = Math.Max(maxLineWidth, line.Width);
        }
        if (overflow == TextOverflow.Ellipsis && TruncatedTail(text, lines, maxWidth, out int start, out int tailEnd))
            maxLineWidth = WithLastLine(lines, BuildStyledEllipsizedLine(text, start, tailEnd, style, spans, maxWidth, memo));
        return new TextLayout(LayoutLines(lines), new Size(maxLineWidth, totalHeight));
    }

    // ── BiDi internals (docs/plans/14-bidi.md) ──

    /// <summary>Cheap classifier: does this text need UAX #9 analysis at all?</summary>
    /// <remarks>
    /// Every LTR layout runs this, on every call, before the cache is even
    /// consulted — it is the one place BiDi taxes text that has no RTL in it. So
    /// text with nothing at or above U+0590 is answered by one vectorized pass; only
    /// past the first such char is each one asked whether it can need the BiDi path
    /// (<see cref="MayNeedBidi"/>). Any char at or above U+0590 used to be enough:
    /// the em dash in "Item 4 — 7 units" took that label to the BiDi path, at twice
    /// the time and memory of "Item 4 - 7 units".
    /// </remarks>
    private static bool NeedsBidi(ReadOnlySpan<char> text, ResolvedInheritedStyle style, bool plainPathForLtr)
    {
        // Auto needs no special case: with nothing right-to-left in the text,
        // first-strong detection can only answer left-to-right, which is what the
        // plain path already does.
        if (style.TextDirection == TextDirection.Rtl) return true;
        // Cast to ushort rather than calling the char overload directly: both are
        // the same vectorized range search, but IndexOfAnyExceptInRange<char>
        // boxes its two bounds on every call (96 B, measured — see
        // samples/HultUI.Samples.TextBench) and the ushort one allocates nothing.
        var chars = System.Runtime.InteropServices.MemoryMarshal.Cast<char, ushort>(text);
        int first = chars.IndexOfAnyExceptInRange((ushort)0, (ushort)0x058F);
        if (first < 0) return false;
        if (!plainPathForLtr) return true;
        for (int i = first; i < text.Length; i++)
            if (MayNeedBidi(text[i])) return true;
        return false;
    }

    /// <summary>
    /// Whether a char at or above U+0590 keeps its text on the BiDi path: one that can be
    /// right-to-left or steers direction, or one that can join its neighbours into a
    /// single user-perceived character — a surrogate, a mark, a joiner or variation
    /// selector, a Hangul jamo. The plain path cuts lines and truncates between chars,
    /// and has never been given such text; the BiDi path cuts between code points and
    /// clusters. Everything else — dashes, quotes, arrows, symbols, box drawing, CJK,
    /// kana, Hangul syllables, the private use area — is left-to-right, one char to a
    /// character: the plain path breaks it into lines exactly as the BiDi path did, and
    /// truncates it the way it truncates Latin text (PlainPathEquivalenceTests).
    /// </summary>
    internal static bool MayNeedBidi(char c)
    {
        if (c < '\u0590') return false;
        if (c <= '\u08FF') return true;                                    // Hebrew to Arabic Extended-A
        if (c >= '\uFB1D' && c <= '\uFDFF') return true;                 // Hebrew and Arabic presentation forms
        if (c >= '\uFE70' && c <= '\uFEFF') return true;                 // Arabic presentation forms B, BOM
        if (char.IsSurrogate(c)) return true;
        if ((c >= '\u1100' && c <= '\u11FF') || (c >= '\uA960' && c <= '\uA97F') || (c >= '\uD7B0' && c <= '\uD7FF'))
            return true;                                                     // Hangul jamo
        if (c == '\u0D4E') return true;                                     // Malayalam dot reph: a prepend
        return char.GetUnicodeCategory(c) switch
        {
            UnicodeCategory.NonSpacingMark or UnicodeCategory.SpacingCombiningMark or UnicodeCategory.EnclosingMark
                or UnicodeCategory.Format or UnicodeCategory.LineSeparator or UnicodeCategory.ParagraphSeparator
                or UnicodeCategory.OtherNotAssigned => true,
            _ => false,
        };
    }

    /// <summary>
    /// Contiguous same-embedding-level segments within <c>[from, to)</c> of an
    /// already-resolved level array.
    /// </summary>
    /// <remarks>
    /// The levels are resolved ONCE for the whole paragraph and sliced here.
    /// Re-running UAX #9 on a substring is not the same computation: resolution is
    /// paragraph-scoped, so a neutral character next to a cut would resolve
    /// against the cut instead of against the text that actually surrounds it —
    /// and it also meant one ICU round trip per break candidate per line.
    /// <para>Into <paramref name="segments"/>, cleared first and returned: the pipeline's kept
    /// list, which MeasureBidi and ShapeBidiLine each read before anything fills it again.</para>
    /// </remarks>
    private static List<(int Start, int End, byte Level)> SegmentsIn(byte[] levels, int from, int to,
        List<(int Start, int End, byte Level)> segments)
    {
        Kept(segments);
        int i = from;
        while (i < to)
        {
            byte level = levels[i];
            int j = i + 1;
            while (j < to && levels[j] == level) j++;
            segments.Add((i, j, level));
            i = j;
        }
        return segments;
    }

    /// <summary>
    /// UAX #9 rule L2: from the highest embedding level down to the lowest odd one,
    /// reverse every contiguous run at that level or higher. Returns the visual
    /// sequence of logical segment indices. Implemented directly — it is a dozen
    /// lines — so the reorder does not depend on a second ICU API surface. Into
    /// <paramref name="order"/>, cleared first and returned (ShapeBidiLine's kept list).
    /// </summary>
    private static List<int> VisualOrder(List<byte> levels, List<int> order)
    {
        int n = levels.Count;
        Kept(order);
        for (int i = 0; i < n; i++) order.Add(i);

        byte max = 0, minOdd = byte.MaxValue;
        for (int i = 0; i < n; i++)
        {
            byte l = levels[i];
            if (l > max) max = l;
            if ((l & 1) == 1 && l < minOdd) minOdd = l;
        }
        if (minOdd == byte.MaxValue) return order;   // all even: already visual

        for (int level = max; level >= minOdd; level--)
        {
            int i = 0;
            while (i < n)
            {
                if (levels[order[i]] >= level)
                {
                    int j = i;
                    while (j < n && levels[order[j]] >= level) j++;
                    order.Reverse(i, j - i);
                    i = j;
                }
                else i++;
            }
        }
        return order;
    }

    // ── What a string keeps between widths: the BiDi and the styled paths ──
    //
    // Right-to-left runs bypass the word cache, so every measure and every line on the BiDi
    // path is HarfBuzz shaping from scratch: ~4 µs for one Hebrew word, ~25 µs for a
    // 75-character label (measured; an LTR word is under 1 µs). The styled path (metric
    // spans) is word-cached, but still cuts every measure at the spans' edges and itemizes
    // each piece. A resize drag paid all of it at each width — the UAX #9 levels, the break
    // offsets, every segment's width, every ellipsis probe and every line — though each is a
    // function of the text, its style, its metric spans and the range, and the same ranges
    // come back width after width: a segment always, a line or a probe whenever the width
    // moves less than a word. So they are memoized — the code that computes them unchanged,
    // the output the same. (The plain path has its own: BreakSegments, PrefixWidths.)
    //
    // What every layout of a string needs lives on its entry: levels (BiDi only), breaks,
    // segment widths and the line of the whole text, which is all a label that fits ever
    // asks for. Probes and partial lines go to caches shared by all strings and both paths,
    // bounded, and sized for two frames of 1,000 ellipsized labels (~8 probes and 2 lines
    // each). A first cut with half the lines, and segment widths in the shared cache too,
    // thrashed (111 ms a frame against 17); twice these sizes was no faster. Full, they hold
    // ~7 MB more than the reference path does for such a list (measured: 60 widths of 1,000
    // BiDi labels, 4.9 against 11.6 MB); for wrapped paragraphs less, since copies share
    // their runs with the remembered lines.
    //
    // A string gets its entry at its second layout, not its first. Most text on the BiDi path
    // is not right-to-left at all — an em dash, an arrow or an emoji is enough to send a label
    // there — and most is laid out at one width, once; the entry's lookup, insert and
    // eviction cost such a label ~0.3 µs, measured at +10 % on 2,000 new labels. So the first
    // layout only notes its key's hash in _seen. The note can be wrong both ways — a slot
    // taken by another string, two keys with one hash — which moves when reuse starts, never
    // what a layout returns. An entry, once made, is used whatever the note says.
    //
    // Like the caches above, the entries and the remembered lines are bounded by the bytes they
    // keep alive as well as by their counts (MemoBytes, LineBytes): by count alone, 4,096
    // paragraphs of 2,000 characters, Hebrew mixed with Latin, kept ~58 MB. And a range's key
    // used to hold its string's entry, so an entry the cache had let go stayed alive — levels,
    // breaks, widths, its whole line — as long as any of its widths or lines did. A range now
    // names its entry by a number (TextMemo.Id) that no other entry has, and keeps nothing of
    // it. A width is a fixed ~100 bytes with its key, ~1.6 MB at the count bound, no more.
    //
    // The bounds sit above what the largest real use fills, because what is kept is used again,
    // across widths and across opens of the same document: the perf suite's 5,000-block rich text
    // document, opened twice and resized, fills the entries' count (4,096, 7.0 MB as weighed)
    // and the lines' (4,096, 8.4 MB), and `richtext/open` finds most of it again; at 8 MB the
    // lines' bound already let 337 of them go. Long paragraphs weigh more and stop short of the
    // counts: the worst case is now the bounds, 22 MB, where it was ~58.

    private readonly LruCache<MemoKey, TextMemo> _memos = new(capacity: 4096, maxWeight: MemoCacheBytes);
    private readonly LruCache<MemoRange, float> _memoWidths = new(capacity: 16384);
    private readonly LruCache<MemoRange, TextLine> _memoLines = new(capacity: 4096, maxWeight: MemoLineCacheBytes);
    private readonly int[] _seen = new int[16384];

    internal const long MemoCacheBytes = 10L << 20;
    internal const long MemoLineCacheBytes = 12L << 20;

    // What each holds now, for the tests that hold them to their bounds.
    internal (int Entries, long Bytes) MemoCacheUse => (_memos.Count, _memos.Weight);
    internal (int Entries, long Bytes) MemoLineCacheUse => (_memoLines.Count, _memoLines.Weight);

    // What an entry keeps alive: its key's string, the levels (one byte a character, BiDi only),
    // the break offsets (a list, room and all) and segment widths, the metric spans, the line of
    // the whole text once it has one (LineBytes), and the objects and cache bookkeeping around
    // them. Measured with full collections, emptying the cache: within 3 % for paragraphs of 400
    // and 2,000 characters, BiDi or styled, and labels of 60 or 80.
    private const int SpanBytes = 56;

    private static int MemoBytes(TextMemo memo, int textLength, int wholeLineRuns = 0)
    {
        int bytes = 300 + 2 * textLength + 4 * memo.Breaks.Capacity + 4 * memo.SegmentWidths.Length;
        if (memo.Levels is { } levels) bytes += levels.Length;
        if (memo.Spans is { } spans) bytes += SpanBytes * spans.Length;
        if (memo.WholeLine is { } line) bytes += LineBytes(line, wholeLineRuns);
        return bytes;
    }

    // A remembered line: each run's object, text and 16 bytes a glyph, and the line's entry.
    // Within 5 % of what full collections measure for lines of one run to thirty (a line of
    // Hebrew mixed with Latin is a run a word or two), ellipsized or not. A line whose runs are not
    // made yet weighs what it will once they are (LayoutBytes says why), its runsNotMade runs and a
    // glyph a character.
    private static int LineBytes(TextLine line, int runsNotMade)
    {
        if (!line.RunsMade)
            return 180 + 104 * runsNotMade + 18 * line.DrawnLength;
        int bytes = 180;
        var runs = line.Runs;
        for (int i = 0; i < runs.Count; i++)
            bytes += 104 + 2 * runs[i].Text.Length + 16 * runs[i].GlyphCount;
        return bytes;
    }

    // Whether a key with this hash has been laid out before; notes it if not. Two slots per
    // hash, and a note goes where it displaces nothing: with one, two strings on the same
    // slot took it from each other at every layout and neither ever got an entry (measured:
    // 137 of 1,000 labels, laid out from scratch every frame).
    private bool SeenBefore(int hash)
    {
        int mask = _seen.Length - 1;
        ref int first = ref _seen[hash & mask];
        ref int second = ref _seen[(int)((uint)(hash * -1640531535) >> 18) & mask];
        if (first == hash || second == hash) return true;
        if (second == 0 && first != 0) second = hash;
        else first = hash;
        return false;
    }

    // SpanDigest is MetricDigest's hash of the metric spans, 0 without spans; the entry
    // keeps the spans themselves, so two span lists with one digest never share it.
    private readonly record struct MemoKey(string Text, string FontFamily, float FontSize, FontWeight FontWeight,
        FontSlant FontSlant, float LineHeight, float LetterSpacing, TextDirection Direction, int SpanDigest);

    // A range of one string's text; Owner is its entry's Id, a number no other entry has, so a
    // string laid out again after its entry was evicted starts with nothing remembered, and the
    // key keeps nothing of the entry alive. Ellipsis tells a line with the mark from the same
    // range without it, and is false for widths.
    private readonly record struct MemoRange(int Owner, int From, int To, bool Ellipsis);

    private sealed class TextMemo
    {
        // What its ranges are keyed by (MemoRange.Owner).
        public readonly int Id;
        // Null on the styled path, which is left-to-right only.
        public readonly byte[]? Levels;
        public readonly bool RtlParagraph;
        public readonly List<int> Breaks;
        // The metric spans this entry was made for, in order (MetricOnly); null without spans.
        public readonly Core.Rendering.TextSpan[]? Spans;
        // The width of segment s (Breaks[s - 1]..Breaks[s]); NaN until a pass reaches it.
        public readonly float[] SegmentWidths;
        // The line of the whole text: the one line of every width it fits.
        public TextLine? WholeLine;
        // What its styled lines make their runs from, at every width: without and with the mark.
        public LineSource? Lines, EllipsizedLines;
        // Whether a line remembered without its runs has counted for the text it keeps.
        public bool TextWeighed;

        public TextMemo(int id, byte[]? levels, bool rtlParagraph, List<int> breaks, Core.Rendering.TextSpan[]? spans)
        {
            Id = id;
            Levels = levels;
            RtlParagraph = rtlParagraph;
            Breaks = breaks;
            Spans = spans;
            SegmentWidths = new float[breaks.Count];
            Array.Fill(SegmentWidths, float.NaN);
        }
    }

    // A metric span as layout sees it: what EffectiveStyleAt and NextStyleBoundary read, nothing a
    // paint-only change can move.
    private static Core.Rendering.TextSpan MetricOnly(Core.Rendering.TextSpan span) =>
        new(span.Start, span.Length, default, default, TextDecoration.None, span.Weight, span.Slant, span.FontSize,
            span.FontFamily);

    // The metric spans in order, MetricOnly: sorted and apart, the two lookups give on them what
    // they give on the whole list.
    private static Core.Rendering.TextSpan[]? MetricSpansOf(IReadOnlyList<Core.Rendering.TextSpan>? spans)
    {
        if (spans is null) return null;
        int count = 0;
        for (int i = 0; i < spans.Count; i++)
            if (spans[i].IsMetricAffecting)
                count++;
        if (count == 0) return null;
        var kept = new Core.Rendering.TextSpan[count];
        for (int i = 0, k = 0; k < count; i++)
            if (spans[i].IsMetricAffecting)
                kept[k++] = MetricOnly(spans[i]);
        return kept;
    }

    private static bool SameMetricSpans(Core.Rendering.TextSpan[]? kept, IReadOnlyList<Core.Rendering.TextSpan>? spans)
    {
        int k = 0;
        if (spans is not null)
            for (int i = 0; i < spans.Count; i++)
            {
                var span = spans[i];
                if (!span.IsMetricAffecting) continue;
                if (kept is null || k >= kept.Length || kept[k] != MetricOnly(span))
                    return false;
                k++;
            }
        return (kept?.Length ?? 0) == k;
    }

    // The string's entry for the BiDi path (bidi) or the styled one. Null on the reference
    // path, where every layout works its levels, breaks, widths and lines out afresh, and at
    // a string's first layout.
    private TextMemo? MemoOf(string text, ResolvedInheritedStyle style, IReadOnlyList<Core.Rendering.TextSpan>? spans, int spanDigest, bool bidi)
    {
        if (!ReuseAcrossWidths) return null;
        var key = new MemoKey(text, style.FontFamily, style.FontSize, style.FontWeight, style.FontSlant,
            style.LineHeight, style.LetterSpacing, style.TextDirection, spanDigest);
        if (_memos.TryGet(key, out var memo))
        {
            if (!SameMetricSpans(memo.Spans, spans))
            {
                // Other spans with the same digest: this layout's entry takes the key over.
                memo = NewMemo(text, style, spans, bidi);
                _memos.Put(key, memo, MemoBytes(memo, text.Length));
            }
        }
        else
        {
            if (!SeenBefore(key.GetHashCode())) return null;
            memo = NewMemo(text, style, spans, bidi);
            _memos.Add(key, memo, MemoBytes(memo, text.Length));
        }
        (_laidOutMemo, _laidOutKey) = (memo, key);
        return memo;
    }

    // The entry the layout running now was handed, and its key, until it ends: a line of the
    // whole text, built later in that layout, makes the entry keep more (Remember).
    private TextMemo? _laidOutMemo;
    private MemoKey _laidOutKey;

    private TextMemo NewMemo(string text, ResolvedInheritedStyle style, IReadOnlyList<Core.Rendering.TextSpan>? spans, bool bidi)
    {
        if (!bidi) return new TextMemo(NextMemoId(), null, false, BreakOffsets(text), MetricSpansOf(spans));
        var (levels, rtlParagraph) = BidiAnalyzer.Levels(text, style.TextDirection);
        return new TextMemo(NextMemoId(), levels, rtlParagraph, BreakOffsets(text), MetricSpansOf(spans));
    }

    // The number of the next entry. No number is used twice while anything of the entry that
    // had it may be cached: after the last one, every entry, width and line goes, and the
    // numbers start over. (An int keeps an entry the size it was, which a long did not.)
    private int _lastMemoId;

    private int NextMemoId()
    {
        if (_lastMemoId == int.MaxValue)
        {
            _memos.Clear();
            _memoWidths.Clear();
            _memoLines.Clear();
            _lastMemoId = 0;
        }
        return ++_lastMemoId;
    }

    // Each layout gets a line of its own — a layout sets its lines' OffsetY — over the same
    // runs, which nothing changes once the line is built (the plain path's one-line layout
    // shares its runs the same way). The layout that builds a line keeps it; later ones get
    // copies, which read everything of it but the OffsetY that layout may have set.
    private static TextLine CopyOf(TextLine line) => new(line);

    // A remembered line, copied for this layout, or null. No delegates here: a lambda over
    // these parameters would allocate its closure on every call, hits included.
    private TextLine? RememberedLine(TextMemo memo, string text, int from, int to, bool appendEllipsis)
    {
        if (from == 0 && to == text.Length && !appendEllipsis)
            return memo.WholeLine is { } whole ? CopyOf(whole) : null;
        return _memoLines.TryGet(new MemoRange(memo.Id, from, to, appendEllipsis), out var line) ? CopyOf(line) : null;
    }

    // Remembers a line RememberedLine did not have, and hands it to the layout that built it.
    // runsNotMade is how many runs the line will make, when it has not made them yet.
    private TextLine Remember(TextMemo memo, string text, int from, int to, bool appendEllipsis, TextLine line,
        int runsNotMade = 0)
    {
        if (from == 0 && to == text.Length && !appendEllipsis)
        {
            // The entry keeps more now: it counts for it.
            memo.WholeLine = line;
            if (ReferenceEquals(memo, _laidOutMemo)) _memos.Reweigh(_laidOutKey, MemoBytes(memo, text.Length, runsNotMade));
        }
        else
        {
            int bytes = LineBytes(line, runsNotMade);
            // A line whose runs are not made keeps the whole text and its spans, for as long as it
            // is remembered, which can be longer than its entry is: the first of a string's lines
            // counts for them, or a list of previews of long paragraphs, two lines of each, would
            // keep them all by a weight of two lines.
            if (!line.RunsMade && !memo.TextWeighed)
            {
                memo.TextWeighed = true;
                bytes += 2 * text.Length + SpanBytes * (memo.Spans?.Length ?? 0);
            }
            _memoLines.Add(new MemoRange(memo.Id, from, to, appendEllipsis), line, bytes);
        }
        return line;
    }

    // MeasureBidi of break segment s.
    private float BidiSegmentWidth(string text, List<int> breaks, int s, ResolvedInheritedStyle style,
        byte[] levels, TextMemo? memo, IReadOnlyList<Core.Rendering.TextSpan>? spans)
    {
        if (memo is null) return MeasureBidi(text, breaks[s - 1], breaks[s], style, levels, spans);
        float width = memo.SegmentWidths[s];
        if (float.IsNaN(width))
            memo.SegmentWidths[s] = width = MeasureBidi(text, breaks[s - 1], breaks[s], style, levels, spans);
        return width;
    }

    private float MeasureBidi(string text, int from, int to, ResolvedInheritedStyle style, byte[] levels, TextMemo? memo,
        IReadOnlyList<Core.Rendering.TextSpan>? spans)
    {
        if (memo is null) return MeasureBidi(text, from, to, style, levels, spans);
        var key = new MemoRange(memo.Id, from, to, Ellipsis: false);
        if (!_memoWidths.TryGet(key, out float width))
        {
            width = MeasureBidi(text, from, to, style, levels, spans);
            _memoWidths.Add(key, width);
        }
        return width;
    }

    private TextLine BidiLine(string text, int from, int to, ResolvedInheritedStyle style, byte[] levels,
        TextMemo? memo, IReadOnlyList<Core.Rendering.TextSpan>? spans, bool appendEllipsis = false, bool rtlParagraph = false)
    {
        if (memo is null) return ShapeBidiLine(text, from, to, style, levels, spans, appendEllipsis, rtlParagraph);
        return RememberedLine(memo, text, from, to, appendEllipsis)
            ?? Remember(memo, text, from, to, appendEllipsis,
                ShapeBidiLine(text, from, to, style, levels, spans, appendEllipsis, rtlParagraph));
    }

    // The styled path's twins of the three above.
    private float StyledSegmentWidth(string text, List<int> breaks, int s, ResolvedInheritedStyle style,
        IReadOnlyList<Core.Rendering.TextSpan> spans, TextMemo? memo)
    {
        if (memo is null) return MeasureStyled(text, breaks[s - 1], breaks[s], style, spans);
        float width = memo.SegmentWidths[s];
        if (float.IsNaN(width))
            memo.SegmentWidths[s] = width = MeasureStyled(text, breaks[s - 1], breaks[s], style, spans);
        return width;
    }

    private float MeasureStyled(string text, int from, int to, ResolvedInheritedStyle style,
        IReadOnlyList<Core.Rendering.TextSpan> spans, TextMemo? memo)
    {
        if (memo is null) return MeasureStyled(text, from, to, style, spans);
        var key = new MemoRange(memo.Id, from, to, Ellipsis: false);
        if (!_memoWidths.TryGet(key, out float width))
        {
            width = MeasureStyled(text, from, to, style, spans);
            _memoWidths.Add(key, width);
        }
        return width;
    }

    private TextLine StyledLine(string text, int from, int to, ResolvedInheritedStyle style,
        IReadOnlyList<Core.Rendering.TextSpan> spans, TextMemo? memo, bool appendEllipsis = false)
    {
        if (memo is null) return ShapeStyledLine(text, from, to, style, spans, null, appendEllipsis);
        if (RememberedLine(memo, text, from, to, appendEllipsis) is { } remembered)
            return remembered;
        int runsBefore = _runsNotMade;
        var line = ShapeStyledLine(text, from, to, style, spans, memo, appendEllipsis);
        return Remember(memo, text, from, to, appendEllipsis, line, _runsNotMade - runsBefore);
    }

    private float MeasureBidi(string text, int from, int to, ResolvedInheritedStyle style, byte[] levels,
        IReadOnlyList<Core.Rendering.TextSpan>? spans = null)
    {
        if (to <= from) return 0;
        float width = 0;
        foreach (var (s, e, level) in SegmentsIn(levels, from, to, _bidiSegments))
        {
            // One piece per style run of the direction run; with no spans, the whole run.
            for (int i = s, b; i < e; i = b)
            {
                b = spans is null ? e : NextStyleBoundary(spans, i, e);
                var eff = spans is null ? style : EffectiveStyleAt(style, spans, i);
                width += MeasureRange(text, i, b, eff, rtl: (level & 1) == 1);
            }
        }
        return width;
    }

    // MeasureBidi's and ShapeBidiLine's segments, and ShapeBidiLine's levels, runs and visual
    // order while it builds a line: lists made anew for each line were 17 % of what laying out
    // 2,000 emoji labels allocated (plan 17, round 5). Neither calls the other.
    private readonly List<(int Start, int End, byte Level)> _bidiSegments = new();
    private readonly List<byte> _bidiLevels = new();
    private readonly List<int> _bidiFirstRuns = new();
    private readonly List<ShapedRun> _bidiRuns = new();
    private readonly List<int> _bidiOrder = new();

    /// <summary>
    /// One line, direction-aware: segments shaped with their resolved direction and
    /// placed in visual order. <see cref="TextLine.Runs"/> stays in LOGICAL order —
    /// painting reads each run's own <see cref="ShapedRun.OffsetX"/>, and the
    /// char→x mapping (spans, selection) walks runs logically — only the offsets
    /// carry the reordering. With metric <paramref name="spans"/>, each direction
    /// segment is cut at their edges too and each piece shaped in its own style, the
    /// pieces of a right-to-left segment placed right to left like its font runs.
    /// </summary>
    private TextLine ShapeBidiLine(string text, int from, int to, ResolvedInheritedStyle style, byte[] paraLevels,
        IReadOnlyList<Core.Rendering.TextSpan>? spans, bool appendEllipsis = false, bool rtlParagraph = false)
    {
        int sourceLength = to - from + (appendEllipsis ? 1 : 0);
        while (to > from && char.IsWhiteSpace(text[to - 1])) to--;

        // Each segment's level, and where its runs start in the runs of the line, in logical
        // order: kept lists, as the segments are (nothing below fills them again).
        var segments = SegmentsIn(paraLevels, from, to, _bidiSegments);
        var levels = Kept(_bidiLevels);
        var firstRuns = Kept(_bidiFirstRuns);
        var lineRuns = Kept(_bidiRuns);
        float maxAscent = 0, maxDescent = 0, maxLeading = 0;

        for (int s = 0; s < segments.Count; s++)
        {
            var (segStart, segEnd, level) = segments[s];
            levels.Add(level);
            firstRuns.Add(lineRuns.Count);
            // One piece per style run of the segment; with no spans, the whole segment. A
            // larger-size piece raises the line, the max-across-runs rule fallback follows.
            for (int i = segStart, b; i < segEnd; i = b)
            {
                b = spans is null ? segEnd : NextStyleBoundary(spans, i, segEnd);
                var eff = spans is null ? style : EffectiveStyleAt(style, spans, i);
                ItemizeRange(text, i, b, eff, Kept(_lineRuns));
                foreach (var (runStart, runEnd, font) in _lineRuns)
                {
                    var shaped = ShapeWithSpacing(text[runStart..runEnd], font, eff.FontSize, eff.LetterSpacing,
                        rtl: (level & 1) == 1);
                    shaped.Level = level;
                    lineRuns.Add(shaped);
                    maxAscent = Math.Max(maxAscent, Math.Abs(font.Ascent) * eff.FontSize);
                    maxDescent = Math.Max(maxDescent, font.Descent * eff.FontSize);
                    maxLeading = Math.Max(maxLeading, font.Leading * eff.FontSize);
                }
            }
        }
        _lineRuns.Clear();   // the runs' faces are not kept alive by the scratch list

        // Place in visual order; within an RTL segment the fallback sub-runs flip too.
        float x = 0;
        foreach (int logical in VisualOrder(levels, _bidiOrder))
        {
            int first = firstRuns[logical];
            int count = (logical + 1 < firstRuns.Count ? firstRuns[logical + 1] : lineRuns.Count) - first;
            bool rtl = (levels[logical] & 1) == 1;
            for (int r = 0; r < count; r++)
            {
                var run = lineRuns[first + (rtl ? count - 1 - r : r)];
                run.OffsetX = x;
                x += run.Width;
            }
        }

        var logicalRuns = new List<ShapedRun>(lineRuns.Count + (appendEllipsis ? 1 : 0));
        logicalRuns.AddRange(lineRuns);
        lineRuns.Clear();   // the line keeps its runs; the kept list does not

        if (appendEllipsis)
        {
            var ellipsis = ShapeWithSpacing(EllipsisString,
                _fonts.Resolve(style.FontFamily, style.FontWeight, style.FontSlant),
                style.FontSize, style.LetterSpacing);
            // The mark goes where reading ENDS, so in a right-to-left paragraph it
            // sits at the left edge and everything already placed shifts right.
            if (rtlParagraph)
            {
                foreach (var run in logicalRuns) run.OffsetX += ellipsis.Width;
                ellipsis.OffsetX = 0;
            }
            else ellipsis.OffsetX = x;
            x += ellipsis.Width;
            logicalRuns.Add(ellipsis);
        }

        if (logicalRuns.Count == 0)
        {
            var primary = _fonts.Resolve(style.FontFamily, style.FontWeight, style.FontSlant);
            maxAscent = Math.Abs(primary.Ascent) * style.FontSize;
            maxDescent = primary.Descent * style.FontSize;
            maxLeading = primary.Leading * style.FontSize;
        }

        (maxAscent, maxLeading) = ApplyLineHeight(maxAscent, maxDescent, maxLeading, style.LineHeight);
        return new TextLine(logicalRuns, x, maxAscent, maxDescent, maxLeading) { Start = from, Length = sourceLength };
    }

    // BiDi twin of BuildEllipsizedLine. The prefix is logical — truncation removes
    // the end of the text, not the right of the line — and the mark is placed by
    // ShapeBidiLine according to the paragraph's direction.
    private TextLine BuildBidiEllipsizedLine(string text, int start, int tailEnd,
        ResolvedInheritedStyle style, byte[] levels, bool rtlParagraph, float maxWidth, TextMemo? bidi,
        IReadOnlyList<Core.Rendering.TextSpan>? spans)
    {
        float ellipsisWidth = MeasureString(EllipsisString, style);
        int lo = start, hi = tailEnd, best = start;
        while (lo <= hi)
        {
            int mid = (lo + hi) >> 1;
            if (MeasureBidi(text, start, mid, style, levels, bidi, spans) + ellipsisWidth <= maxWidth)
            {
                best = mid;
                lo = mid + 1;
            }
            else hi = mid - 1;
        }
        return BidiLine(text, start, SnapPrefix(text, best), style, levels, bidi, spans,
            appendEllipsis: true, rtlParagraph);
    }

    // The direction-aware twin of ComputeWrapped: same greedy break logic over
    // logical order, measurement and line shaping direction-resolved, same
    // truncation policy.
    private TextLayout ComputeWrappedBidi(string text, ResolvedInheritedStyle style, float maxWidth, int? maxLines,
        TextOverflow overflow, TextMemo? bidi, IReadOnlyList<Core.Rendering.TextSpan>? spans = null)
    {
        // One UAX #9 resolution for the whole paragraph; every line, break
        // candidate and prefix measurement below slices into it. With the string's
        // entry it is the resolution made the first time, and the breaks, widths and
        // lines below are remembered from earlier widths too.
        var (levels, rtlParagraph) = bidi is null
            ? BidiAnalyzer.Levels(text, style.TextDirection)
            : (bidi.Levels!, bidi.RtlParagraph);
        // One unbounded line without a newline never breaks: the loop below would find ICU's
        // breaks and measure every segment between them to add no line, and the line after it is
        // the whole text. So it runs over no breaks. A text area measures the prefixes of a line of
        // BiDi text this way (MeasureWidth), several each time the line wraps at a new width.
        var allBreaks = float.IsPositiveInfinity(maxWidth) && !text.AsSpan().Contains('\n')
            ? NoBreaks
            : bidi?.Breaks ?? BreakOffsets(text);

        var lines = RentLines();
        int textOffset = 0;
        float lineWidth = 0;

        for (int s = 1; s < allBreaks.Count; s++)
        {
            int segStart = allBreaks[s - 1];
            int segEnd = allBreaks[s];

            if (segEnd - segStart == 1 && text[segStart] == '\n')
            {
                lines.Add(BidiLine(text, textOffset, segStart, style, levels, bidi, spans));
                textOffset = segEnd;
                lineWidth = 0;
                if (maxLines.HasValue && lines.Count >= maxLines.Value) break;
                continue;
            }

            float segWidth = BidiSegmentWidth(text, allBreaks, s, style, levels, bidi, spans);

            if (lineWidth + segWidth > maxWidth && lineWidth > 0)
            {
                lines.Add(BidiLine(text, textOffset, segStart, style, levels, bidi, spans));
                textOffset = segStart;
                lineWidth = 0;
                if (maxLines.HasValue && lines.Count >= maxLines.Value) break;
            }

            if (lineWidth == 0 && segWidth > maxWidth)
            {
                while (textOffset < segEnd && (!maxLines.HasValue || lines.Count < maxLines.Value))
                {
                    int fit = BidiFitPrefix(text, textOffset, segEnd, style, levels, maxWidth, bidi, spans);
                    if (fit <= textOffset) fit = NextCodePoint(text, textOffset);
                    if (fit >= segEnd) break;
                    lines.Add(BidiLine(text, textOffset, fit, style, levels, bidi, spans));
                    textOffset = fit;
                }
                if (maxLines.HasValue && lines.Count >= maxLines.Value) break;
                lineWidth = MeasureBidi(text, textOffset, segEnd, style, levels, bidi, spans);
                continue;
            }

            lineWidth += segWidth;
        }

        if (textOffset < text.Length && (!maxLines.HasValue || lines.Count < maxLines.Value))
            lines.Add(BidiLine(text, textOffset, text.Length, style, levels, bidi, spans));

        float totalHeight = 0, maxLineWidth = 0;
        foreach (var line in lines)
        {
            line.OffsetY = totalHeight;
            totalHeight += line.Height;
            maxLineWidth = Math.Max(maxLineWidth, line.Width);
        }
        if (overflow == TextOverflow.Ellipsis && TruncatedTail(text, lines, maxWidth, out int start, out int tailEnd))
            maxLineWidth = WithLastLine(lines,
                BuildBidiEllipsizedLine(text, start, tailEnd, style, levels, rtlParagraph, maxWidth, bidi, spans));
        return new TextLayout(LayoutLines(lines), new Size(maxLineWidth, totalHeight), rtlParagraph);
    }

    // ComputeWrappedBidi's breaks for a line that cannot break. Never added to.
    private static readonly List<int> NoBreaks = [];

    private int BidiFitPrefix(string text, int from, int to,
        ResolvedInheritedStyle style, byte[] levels, float maxWidth, TextMemo? bidi,
        IReadOnlyList<Core.Rendering.TextSpan>? spans)
    {
        int span = 1;
        while (from + span < to && MeasureBidi(text, from, from + span, style, levels, bidi, spans) <= maxWidth)
            span <<= 1;

        int lo = from + Math.Min(span >> 1, to - from);
        int hi = from + Math.Min(span, to - from);
        int best = lo;
        while (lo <= hi)
        {
            int mid = (lo + hi) >> 1;
            if (MeasureBidi(text, from, mid, style, levels, bidi, spans) <= maxWidth) { best = mid; lo = mid + 1; }
            else hi = mid - 1;
        }
        return SnapPrefix(text, best);
    }

    /// <summary>
    /// Every offset a line may start or end at: the UAX #14 break opportunities plus
    /// both sides of each explicit newline, sorted and deduplicated, bracketed by 0
    /// and the text length. Consecutive entries delimit one unbreakable segment —
    /// which is also exactly what min-content width is the widest of.
    /// </summary>
    private static List<int> BreakOffsets(string text)
    {
        var offsets = new List<int> { 0 };
        for (int i = 0; i < text.Length; i++)
            if (text[i] == '\n') { offsets.Add(i); offsets.Add(i + 1); }
        offsets.AddRange(LineBreaker.FindBreakOpportunities(text));
        offsets.Add(text.Length);
        offsets.Sort();
        // Duplicates out in one pass. Checking each break against the list before adding
        // it was quadratic: 33 µs for the 700 breaks of a 3,700-character paragraph, 7 µs
        // this way (measured) — next to ICU's own 29 µs to find them.
        int kept = 1;
        for (int i = 1; i < offsets.Count; i++)
            if (offsets[i] != offsets[kept - 1]) offsets[kept++] = offsets[i];
        offsets.RemoveRange(kept, offsets.Count - kept);
        return offsets;
    }

    /// <summary>
    /// A string's <see cref="BreakOffsets"/> and the width of each segment between them,
    /// kept with its shaping: both depend on the text and its style, not on the width, so
    /// re-wrapping at a new width (a resize drag) needs neither ICU nor any measuring for
    /// them again. A width is filled the first time a pass reaches its segment — a pass
    /// stopped by maxLines never measures the rest — and is NaN until then.
    /// </summary>
    private sealed class BreakSegments
    {
        public readonly List<int> Offsets;
        public readonly float[] Widths;

        public BreakSegments(List<int> offsets)
        {
            Offsets = offsets;
            Widths = new float[offsets.Count];
            Array.Fill(Widths, float.NaN);
        }
    }

    // Null on the reference path, where every pass finds and measures its segments afresh.
    private BreakSegments? BreakSegmentsOf(ShapedText shaped, string text) =>
        ReuseAcrossWidths ? shaped.BreakSegments ??= new BreakSegments(BreakOffsets(text)) : null;

    // The width of segment s (offsets[s - 1]..offsets[s]), from the cache when there is one.
    private float SegmentWidth(string text, List<int> offsets, BreakSegments? cached, int s, ResolvedInheritedStyle style)
    {
        if (cached is null) return MeasureRange(text, offsets[s - 1], offsets[s], style);
        float width = cached.Widths[s];
        if (float.IsNaN(width))
            cached.Widths[s] = width = MeasureRange(text, offsets[s - 1], offsets[s], style);
        return width;
    }

    // ── Min-content width (docs/plans/13-min-content.md) ──

    // Widest unbreakable segment per (text, font attributes). Layout asks for this
    // once per shrinking flex item per pass, and the answer is width-independent,
    // so it is worth not re-measuring every word each time.
    private readonly LruCache<ShapeKey, float> _minContentCache = new(capacity: 4096);

    /// <summary>
    /// The narrowest width at which this text does not overflow: the widest segment
    /// between line-break opportunities (CSS min-content width). Trailing whitespace
    /// at a break does not count towards it, because that is where the line ends.
    /// </summary>
    public float MinContentWidth(string text, ResolvedInheritedStyle style,
        IReadOnlyList<Core.Rendering.TextSpan>? spans = null)
    {
        BeginParagraph(text);
        if (string.IsNullOrEmpty(text)) return 0f;

        bool styled = AnyMetricAffecting(spans);
        var key = new ShapeKey(text, style.FontFamily, style.FontSize, style.FontWeight,
            style.FontSlant, style.LineHeight, style.LetterSpacing);
        if (!styled && _minContentCache.TryGet(key, out float cached))
            return cached;

        var offsets = BreakOffsets(text);
        float widest = 0f;
        for (int i = 1; i < offsets.Count; i++)
        {
            int from = offsets[i - 1], to = offsets[i];
            while (to > from && char.IsWhiteSpace(text[to - 1])) to--;
            if (to <= from) continue;
            widest = Math.Max(widest, styled
                ? MeasureStyled(text, from, to, style, spans!)
                : MeasureRange(text, from, to, style));
        }

        if (!styled) _minContentCache.Put(key, widest);
        return widest;
    }

    private const string EllipsisString = "…"; // …

    /// <summary>
    /// One truncation policy for all three layout paths, whose line builders differ (plain,
    /// styled, direction-aware): whether the last of the clipped <paramref name="lines"/> takes
    /// an ellipsis, and the source range <c>[start, tailEnd)</c> the line that replaces it
    /// (<see cref="WithLastLine"/>) may draw from.
    /// </summary>
    /// <remarks>
    /// A test and a replacement, with the builder called between them: one method handed the
    /// builder as a lambda made a closure and a delegate for every truncated layout, and for
    /// every styled or BiDi layout with Ellipsis whether it truncated or not, 10 % of what
    /// truncating 1,000 styled labels allocated (plan 17, round 6).
    /// </remarks>
    private static bool TruncatedTail(string text, List<TextLine> lines, float maxWidth, out int start, out int tailEnd)
    {
        start = tailEnd = 0;
        if (lines.Count == 0)
            return false;

        var last = lines[^1];
        int coveredEnd = last.Start + last.Length;
        bool droppedTail = coveredEnd < text.Length;
        // A greedy line never exceeds maxWidth unless it is a lone token that
        // could not be broken — that still needs to be clipped with an ellipsis.
        bool lineOverflows = last.Width > maxWidth + 0.5f;
        if (!droppedTail && !lineOverflows)
            return false;

        // The truncated line's source is everything from where the last line
        // begins up to the next explicit newline (or the end of the string).
        start = last.Start;
        int nl = text.IndexOf('\n', start);
        tailEnd = nl < 0 ? text.Length : nl;
        return true;
    }

    // The clipped lines with the last replaced by the ellipsized one, in the same place, in the
    // list they are in: no layout is made of the clipped lines. Returns the widest line now.
    // The replacement line uses the same font metrics, so the overall height is unchanged;
    // only the width can shrink.
    private static float WithLastLine(List<TextLine> lines, TextLine ellipsized)
    {
        ellipsized.OffsetY = lines[^1].OffsetY;
        lines[^1] = ellipsized;

        float maxLineWidth = 0;
        foreach (var line in lines)
            maxLineWidth = Math.Max(maxLineWidth, line.Width);
        return maxLineWidth;
    }

    // The list the line breakers fill, kept between layouts; each layout gets an exact array of
    // its lines (LayoutLines). A new list for each layout, at the default capacity and grown by
    // doubling, was the layout's own, and a truncated one made a second list and a layout to
    // replace its last line: 63 % of what truncating 1,000 styled labels allocated (plan 17,
    // round 7). Taken while a breaker fills it, so a layout made in the middle of another's —
    // a measure inside a break — fills a list of its own: a kept list never holds the lines of
    // two layouts at once.
    private List<TextLine>? _keptLines;

    // A list kept no longer than this many lines: a long document's would hold its room.
    private const int KeptLinesCapacity = 1024;

    private List<TextLine> RentLines()
    {
        var lines = _keptLines ?? new List<TextLine>(16);
        _keptLines = null;
        return lines;
    }

    // The lines as the layout's own exact array, the list given back empty.
    private TextLine[] LayoutLines(List<TextLine> lines)
    {
        var exact = lines.ToArray();
        GiveBackLines(lines);
        return exact;
    }

    private void GiveBackLines(List<TextLine> lines)
    {
        lines.Clear();   // holds no line alive
        if (lines.Capacity <= KeptLinesCapacity)
            _keptLines = lines;
    }

    // Builds a single line holding the longest prefix of <paramref name="tail"/>
    // that leaves room for the ellipsis within <paramref name="maxWidth"/>,
    // followed by the ellipsis glyph. <paramref name="prefixes"/>, when the tail
    // has them, measures each candidate prefix without itemizing or shaping it
    // again; <paramref name="ellipsisWidth"/> is MeasureString of the mark.
    private TextLine BuildEllipsizedLine(string text, string tail, int start, ResolvedInheritedStyle style, float maxWidth,
        PrefixWidths? prefixes, float ellipsisWidth)
    {
        // Binary search for the widest prefix that fits alongside the ellipsis.
        // Width is monotonic in prefix length, so this converges in O(log n)
        // shaping calls even for a pathologically long unbreakable token.
        int lo = 0, hi = tail.Length, best = 0;
        while (lo <= hi)
        {
            int mid = (lo + hi) >> 1;
            float width = prefixes is null ? MeasureRange(tail, 0, mid, style) : PrefixWidth(prefixes, mid, style);
            if (width + ellipsisWidth <= maxWidth)
            {
                best = mid;
                lo = mid + 1;
            }
            else
            {
                hi = mid - 1;
            }
        }

        // Never split a surrogate pair. Snapping down only narrows the prefix, so
        // it still fits.
        best = SnapPrefix(tail, best);

        if (!ComposeRunsWhenRead)
        {
            string content = string.Concat(tail.AsSpan(0, best), EllipsisString);
            return ShapeLineText(content, 0, content.Length, style, start);
        }

        // The line as characters, not a string: the runs that keep one are made when read. The
        // width is what they will add up to (MeasureRuns), its text's own prefix and the mark.
        int length = best + EllipsisString.Length;
        Span<char> line = length <= 256 ? stackalloc char[length] : new char[length];
        tail.AsSpan(0, best).CopyTo(line);
        EllipsisString.CopyTo(line[best..]);
        var fontRuns = Kept(_lineRuns);
        ItemizeChars(line, 0, style, ParagraphHanContext(), fontRuns);
        var (ascent, descent, leading) = LineMetrics(fontRuns, style);
        float lineWidth = MeasureChars(line, fontRuns, style);
        _runsNotMade += fontRuns.Count;
        fontRuns.Clear();   // the runs' faces are not kept alive by the scratch list
        return new TextLine(new LineSource(StyleOf(style, ellipsized: true), text, null), length, lineWidth, ascent,
            descent, leading)
        {
            Start = start,
            Length = length,
        };
    }

    // ── Truncation without re-shaping ──
    //
    // Clip and Ellipsis both look for the longest prefix that fits, and both used to find
    // it by measuring candidate prefixes from scratch: each one a new substring, itemized
    // and shaped through the word cache, ~7 of them per truncated label per width. And a
    // one-line Ellipsis first broke the whole text into lines only to replace the line it
    // got. A resize drag over 1,000 truncated labels paid that for every label, every
    // frame (samples/HultUI.Samples.PerfSuite, text/ellipsis).
    //
    // Now a string's prefix widths come from its own segments, worked out once and kept
    // with its shaping, and only a partial last word is looked up per candidate. The
    // searches are unchanged, probe for probe, and each probe returns the number
    // MeasureString would have — so the cut lands where it always did.

    /// <summary>
    /// When false, a layout at a new width works everything out from scratch — the
    /// reference path: a wrap finds and measures its break segments afresh, truncation
    /// measures every candidate prefix anew, one-line Ellipsis goes through the line
    /// breaker, and the BiDi path resolves its levels and shapes every measure and line
    /// again. The output is the same either way; tests hold the two paths to that.
    /// </summary>
    internal bool ReuseAcrossWidths { get; init; } = true;

    /// <summary>
    /// When false, any char at or above U+0590 sends its text down the BiDi path — the
    /// reference: the em dash in "Item 4 — 7 units" took it, and with it twice the time
    /// and memory to lay out. When true (the default) only text that can need the BiDi
    /// path does (<see cref="MayNeedBidi"/>); left-to-right text of other symbols and
    /// scripts lays out on the plain path. Tests hold the two to the same output.
    /// </summary>
    internal bool PlainPathForLtr { get; init; } = true;

    /// <summary>
    /// A string's font runs, and in each the space / non-space segments
    /// <see cref="ShapeByWords"/> cuts it into, with the running sums MeasureString and
    /// ShapeByWords add them up with. FontItemizer decides each character from the ones
    /// before it and never looks ahead, so every prefix of a string (not cut inside a
    /// surrogate pair) itemizes to the same runs, the last one cut short — and is measured
    /// from these.
    /// </summary>
    private sealed class PrefixWidths(string text, float fontSize, float letterSpacing,
        PrefixRun[] runs, PrefixSegment[] segments)
    {
        public readonly string Text = text;
        public readonly float FontSize = fontSize;
        public readonly float LetterSpacing = letterSpacing;
        public readonly PrefixRun[] Runs = runs;
        public readonly PrefixSegment[] Segments = segments;
    }

    // Start..End of the text in one font. Before is MeasureString's running width where the
    // run starts (the runs before it, added in order); its segments start at FirstSegment.
    private readonly record struct PrefixRun(int Start, int End, IResolvedFont Font, float Before, int FirstSegment);

    // X is where the segment starts in its run: ShapeByWords' running x, summed in its order.
    // GlyphsBefore counts the run's glyphs before it, for letter spacing.
    private readonly record struct PrefixSegment(int Start, float X, float Width, int GlyphsBefore, int Glyphs);

    private PrefixWidths? BuildPrefixWidths(string text, ResolvedInheritedStyle style)
    {
        if (text.Length == 0) return null;
        var runs = _prefixRuns;
        var segments = _prefixSegments;
        FillPrefixTable(text, 0, text.Length, style, runs, segments);
        var table = new PrefixWidths(text, style.FontSize, style.LetterSpacing, runs.ToArray(), segments.ToArray());
        ReleasePrefixScratch();
        return table;
    }

    private void ReleasePrefixScratch()
    {
        _prefixRuns.Clear();   // the runs' faces are not kept alive by the scratch list
        // A whole document's single line can have a hundred thousand segments: its list is not kept.
        if (_prefixSegments.Capacity > KeptPrefixSegments)
        {
            _prefixSegments.Clear();
            _prefixSegments.TrimExcess();
        }
    }

    // Fills `runs` and `segments` with the table of text[from..end), measured as a string of those
    // characters: its font runs and their space / non-space segments, each start a place in text.
    private void FillPrefixTable(string text, int from, int end, ResolvedInheritedStyle style,
        List<PrefixRun> runs, List<PrefixSegment> segments)
    {
        var fontRuns = _runRanges;
        ItemizeRange(text, from, end, style, fontRuns);
        runs.Clear();
        segments.Clear();
        float before = 0;
        for (int r = 0; r < fontRuns.Count; r++)
        {
            var (runStart, runEnd, font) = fontRuns[r];
            runs.Add(new PrefixRun(runStart, runEnd, font, before, segments.Count));
            float x = 0;
            int glyphs = 0;
            int fontHash = WordCache.FontHash(font, style.FontSize);
            for (int i = runStart; i < runEnd;)
            {
                bool isSpace = text[i] == ' ';
                int start = i;
                while (i < runEnd && (text[i] == ' ') == isSpace) i++;
                var seg = i == end && ReadsCuts && CutsWordShortAtEnd(text, from, start, end)
                    ? TableEndSegment(text, start, end, font, style.FontSize, fontHash)
                    : ShapeSegment(text, start, i - start, font, style.FontSize, fontHash, withCuts: true);
                segments.Add(new PrefixSegment(start, x, seg.Width, glyphs, seg.Glyphs));
                x += seg.Width;
                glyphs += seg.Glyphs;
            }
            // The run's width as ShapeWithSpacing gives it, added as MeasureString adds it.
            before += style.LetterSpacing == 0f || glyphs == 0 ? x : x + style.LetterSpacing * glyphs;
        }
        fontRuns.Clear();   // the runs' faces are not kept alive by the scratch list
    }

    // Whether a table of text[from..end) cuts short a word at its end that starts at `start` at every
    // width: it goes on past end (no space or line end there), and starts at the start of a line, after
    // a space, or where a font run starts, not at a row start inside a word, which moves with the width.
    private static bool CutsWordShortAtEnd(string text, int from, int start, int end) =>
        end < text.Length && text[end] is not (' ' or '\n') && text[start] != ' '
        && (start != from || from == 0 || text[from - 1] is ' ' or '\n');

    // Where such a word ends at every width: the first change between ASCII letters or digits and
    // other characters after its start, when the table's end does not pass it, else the next space
    // or line end; at most MaxNaturalWord characters from its start. Chinese and Japanese set Latin
    // names, commands and numbers among their ideographs, often with no space between: a word run
    // on to the next space shaped a clause where the table needed a name (plan 17, round 14: on
    // README.zh-CN.md, 58 % of the characters such words shaped were past such a change).
    private static int NaturalEnd(string text, int start, int end)
    {
        int stop = Math.Min(text.Length, start + MaxNaturalWord);
        bool latin = char.IsAsciiLetterOrDigit(text[start]);
        int change = start + 1;
        while (change < stop && text[change] is not (' ' or '\n') && char.IsAsciiLetterOrDigit(text[change]) == latin)
            change++;
        if (change >= end) return change;
        while (end < stop && text[end] is not (' ' or '\n')) end++;
        return end;
    }

    // The longest word a table's end is read off (NaturalEnd): its shaping and cuts are kept in the
    // word cache (some 26 bytes a character of CJK), and a word of text without spaces is a line long.
    private const int MaxNaturalWord = 1024;

    // FillPrefixTable's last segment, text[start..end), a word the table's end cuts short
    // (CutsWordShortAtEnd): ShapeSegment's answer, but read off the word to its natural end where a cut
    // there is safe. That word is shaped once with its cuts and kept, so the next width's table, which
    // ends somewhere else, reads its own end off it instead of shaping a new string; the reads are
    // RunCuts', exact to the bit. A segment the word cache has is its own answer: the word to its
    // natural end is neither looked up nor made for it.
    private WordShape TableEndSegment(string text, int start, int end, IResolvedFont font, float fontSize, int fontHash)
    {
        var segment = text.AsSpan(start, end - start);
        if (DigitTableFor(segment, font, fontSize) is { } digits)
            return DigitSegment(segment, digits, start);
        if (!_wordShapeCache.TryGet(segment, font, fontSize, fontHash, out var run))
        {
            int natural = NaturalEnd(text, start, end);
            if (natural > end && CutsOfNaturalWord(text, start, natural, font, fontSize, fontHash)
                    .TryPrefix(end - start, out float width, out int glyphs))
                return new WordShape(null, null, start, width, glyphs);
            // The segment itself, with its cuts, which the line search's probes then read (LastCuts).
            string word = segment.ToString();
            run = _cutShaper!.Shape(word, font, fontSize, out var cuts);
            cuts ??= RunCuts.None;
            _wordShapeCache.Put(word, font, fontSize, fontHash, run, WordBytes(word, run) + cuts.Bytes, cuts);
            _lastCuts = new LastCuts(text, start, font, fontSize, cuts);
        }
        return new WordShape(run, null, start, run.Width, run.Glyphs.Length);
    }

    // The cuts a cut in the segment text[start..end) of a table of text[from..tableEnd) is read off:
    // those of the word to its natural end where the table's end cuts it short (TableEndSegment read
    // the table's end off them), else, or when that word is not made (CutsOfNaturalWord), the
    // segment's own.
    private RunCuts CutsForCut(string text, int from, int tableEnd, int start, int end, IResolvedFont font,
        float fontSize, int fontHash)
    {
        if (end == tableEnd && CutsWordShortAtEnd(text, from, start, end) && NaturalEnd(text, start, end) is var natural
            && natural > end && CutsOfNaturalWord(text, start, natural, font, fontSize, fontHash) is var cuts
            && cuts != RunCuts.None)
            return cuts;
        return CutsOfWord(text, start, end, font, fontSize, fontHash);
    }

    // The cuts CutsOfWord last gave, of a word that starts at Start of Text, in Font at Size: a line
    // search cuts a row's word at every probe, and the probes after the first read it off these
    // without a lookup. Any word's cuts from that start read a prefix of it to the bit (RunCuts), or
    // refuse one they do not reach. Kept, with the text, until the next word's.
    private readonly record struct LastCuts(string Text, int Start, IResolvedFont Font, float Size, RunCuts Cuts)
    {
        public RunCuts? Of(string text, int start, IResolvedFont font, float size) =>
            ReferenceEquals(Text, text) && Start == start && ReferenceEquals(Font, font) && Size.Equals(size)
                ? Cuts : null;
    }

    private LastCuts _lastCuts;

    // Where the word text[start..end) can be cut: the word cache's cuts, or, when it has the word
    // without them (a line's width or a drawn row shaped it, or it is under MinCutWord) or not at all,
    // the word shaped with its cuts now and kept in their place. RunCuts.None when the shaper gives
    // none (a run shaped right to left), kept too, so the word is not shaped again for them.
    private RunCuts CutsOfWord(string text, int start, int end, IResolvedFont font, float fontSize, int fontHash)
    {
        var span = text.AsSpan(start, end - start);
        var cuts = _wordShapeCache.CutsOf(span, font, fontSize, fontHash);
        if (cuts is null)
        {
            string word = span.ToString();
            var run = _cutShaper!.Shape(word, font, fontSize, out cuts);
            cuts ??= RunCuts.None;
            _wordShapeCache.Put(word, font, fontSize, fontHash, run, WordBytes(word, run) + cuts.Bytes, cuts);
        }
        _lastCuts = new LastCuts(text, start, font, fontSize, cuts);
        return cuts;
    }

    // CutsOfWord for the word to its natural end, text[start..end), made only while such words are
    // kept until a later width reads them (_naturalCredit); RunCuts.None where it is not made.
    private RunCuts CutsOfNaturalWord(string text, int start, int end, IResolvedFont font, float fontSize,
        int fontHash)
    {
        if (_wordShapeCache.CutsOf(text.AsSpan(start, end - start), font, fontSize, fontHash) is { } kept)
        {
            if (_naturalCredit < MaxNaturalCredit) _naturalCredit++;
            return (_lastCuts = new LastCuts(text, start, font, fontSize, kept)).Cuts;
        }
        _naturalsMade ??= new int[NaturalsNoted];
        int key = HashCode.Combine(System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(text), start,
            fontHash) | 1;
        ref int made = ref _naturalsMade[key & (NaturalsNoted - 1)];
        if (made == key)
        {
            _naturalCredit = Math.Max(_naturalCredit - LetGoCost, -MaxNaturalCredit);
            return RunCuts.None;
        }
        if (_naturalCredit <= 0)
        {
            _naturalCredit++;
            return RunCuts.None;
        }
        made = key;
        return CutsOfWord(text, start, end, font, fontSize, fontHash);
    }

    // The words to their natural ends made, by a hash of the text, the word's start and the font: a
    // slot two words share forgets the first, which is made once more. 64 KB, made at the first.
    private const int NaturalsNoted = 16384;
    private int[]? _naturalsMade;

    // Words to their natural ends are made only while they are kept until a later width reads them.
    // One made before that the cache no longer has was let go first: a document whose words at one
    // width outgrow the word cache. There, making them shapes more than the tables' own segments and
    // saves nothing, so the tables shape their segments, as before. Words read again earn credit and
    // words let go cost it; with none left no more are made, and each one asked for meanwhile earns a
    // little back, so one in about LetGoCost is tried, and a document that fits makes them again.
    private int _naturalCredit = MaxNaturalCredit;
    private const int MaxNaturalCredit = 64, LetGoCost = 8;

    // BuildPrefixWidths' runs and segments while it builds a table, which gets exact arrays
    // copied out of them: lists grown anew for every table were most of what wrapping and
    // truncating allocated (a list doubles its way up: a line of 60 segments went through five
    // arrays before its own was copied out). Nothing BuildPrefixWidths calls builds a table, and a
    // pipeline is used from one thread, as MeasureString's run list is. The font runs FillPrefixTable
    // itemizes into are ranges of the text, not strings of their own. A wrap's last line reads its
    // table straight out of them for the one search it is made for (BreakIntoLines), which builds
    // no table either.
    private readonly List<PrefixRun> _prefixRuns = new();
    private readonly List<PrefixSegment> _prefixSegments = new();
    private readonly List<RunRange> _runRanges = new();

    // The most segments the kept list holds room for between tables (20 bytes each): a line of
    // ~12,000 characters of prose.
    internal const int KeptPrefixSegments = 4096;

    // The room the kept segment list holds, for the test that holds it to KeptPrefixSegments.
    internal int PrefixScratchCapacity => _prefixSegments.Capacity;

    // The room the view of a range's prefix widths holds, for the same test.
    internal int PrefixViewCapacity => _prefixView?.Segments.Capacity ?? 0;

    /// <summary>
    /// <c>MeasureString(p.Text[..length])</c>, to the bit: the same run and segment widths,
    /// added in the same order, letter spacing applied the same way. Complete runs and
    /// segments are read off the table; only a word the prefix cuts in two is looked up
    /// (through the word cache, as MeasureString would) — never its share of the whole word's
    /// glyph advances, which carry the kerning and ligatures of the letters cut off.
    /// </summary>
    private float PrefixWidth(PrefixWidths p, int length, ResolvedInheritedStyle style) =>
        PrefixWidth(p.Text, 0, p.Text.Length, p.Runs, p.Segments, p.FontSize, p.LetterSpacing, length, style);

    // The same for a table of text[from..from + tableLength), whose runs and segments start at
    // places in text (FillPrefixTable): a view of a range (PrefixView), or a whole string's table.
    private float PrefixWidth(string text, int from, int tableLength, ReadOnlySpan<PrefixRun> runs,
        ReadOnlySpan<PrefixSegment> segments, float fontSize, float letterSpacing, int length,
        ResolvedInheritedStyle style)
    {
        if (length == 0) return 0;
        int cut = from + length;
        // A cut inside a surrogate pair leaves a lone surrogate, which itemizes on its own
        // terms: that prefix is measured the long way. (Not reached today — a surrogate is
        // above U+0590, so its text takes the BiDi path — but this keeps the table honest.)
        if (length < tableLength && char.IsLowSurrogate(text[cut]) && char.IsHighSurrogate(text[cut - 1]))
            return MeasureRange(text, from, cut, style);

        // The run the cut falls in: the last starting before it.
        int lo = 0, hi = runs.Length - 1;
        while (lo < hi)
        {
            int mid = (lo + hi + 1) >> 1;
            if (runs[mid].Start < cut) lo = mid;
            else hi = mid - 1;
        }
        var run = runs[lo];

        // Its last segment starting before the cut.
        int runSegmentsEnd = lo + 1 < runs.Length ? runs[lo + 1].FirstSegment : segments.Length;
        lo = run.FirstSegment;
        hi = runSegmentsEnd - 1;
        while (lo < hi)
        {
            int mid = (lo + hi + 1) >> 1;
            if (segments[mid].Start < cut) lo = mid;
            else hi = mid - 1;
        }
        var seg = segments[lo];
        int end = lo + 1 < runSegmentsEnd ? segments[lo + 1].Start : run.End;

        float width;
        int glyphs;
        if (cut == end)
        {
            width = seg.Width;
            glyphs = seg.Glyphs;
        }
        else
        {
            int fontHash = WordCache.FontHash(run.Font, fontSize);
            var part = text.AsSpan(seg.Start, cut - seg.Start);
            if (!PrefixesFromCuts || DigitTableFor(part, run.Font, fontSize) is not null)
            {
                var shaped = ShapeSegment(text, seg.Start, cut - seg.Start, run.Font, fontSize, fontHash);
                width = shaped.Width;
                glyphs = shaped.Glyphs;
            }
            // As ShapeSegment would answer, but for a miss: the word cut short is read off the last
            // cuts given (LastCuts) when they are of a word from the same start, or is in the word
            // cache (a cut made at an earlier width or by the probe before), or is read off the whole
            // word's cuts where a cut there is safe (made now if the word has none yet, and read off
            // the word to its natural end where FillPrefixTable read the table's end off it), or is
            // shaped as a string of its own and kept.
            else if (_lastCuts.Of(text, seg.Start, run.Font, fontSize) is { } last
                     && last.TryPrefix(cut - seg.Start, out width, out glyphs))
            {
                PrefixesCut++;
            }
            else if (_wordShapeCache.TryGet(part, run.Font, fontSize, fontHash, out var known))
            {
                width = known.Width;
                glyphs = known.Glyphs.Length;
            }
            else if (_cutShaper is not null
                     && CutsForCut(text, from, from + tableLength, seg.Start, end, run.Font, fontSize, fontHash)
                         .TryPrefix(cut - seg.Start, out width, out glyphs))
            {
                PrefixesCut++;
            }
            else
            {
                var shaped = ShapeNewWord(text.Substring(seg.Start, cut - seg.Start), run.Font, fontSize, fontHash);
                width = shaped.Width;
                glyphs = shaped.Glyphs.Length;
                PrefixesShaped++;
            }
        }

        // ShapeByWords' running sum, then ShapeWithSpacing's letter spacing, then
        // MeasureString's sum over the runs.
        float x = seg.X + width;
        int total = seg.GlyphsBefore + glyphs;
        return run.Before + (letterSpacing == 0f || total == 0 ? x : x + letterSpacing * total);
    }

    // The string's prefix widths, built the first time it is truncated and kept with its
    // shaping; null on the reference path.
    private PrefixWidths? PrefixWidthsOf(ShapedText shaped, string text, ResolvedInheritedStyle style)
    {
        if (!ReuseAcrossWidths) return null;
        if (!shaped.PrefixWidthsKnown)
        {
            shaped.PrefixWidths = BuildPrefixWidths(text, style);
            shaped.PrefixWidthsKnown = true;
        }
        return shaped.PrefixWidths;
    }

    /// <summary>
    /// The widths of <paramref name="text"/>'s prefixes: <c>Width(n)</c> is the width
    /// <see cref="Layout(string, ResolvedInheritedStyle, float, int?, TextOverflow)"/> gives
    /// <c>text[..n]</c> on one unbounded line, to the bit — read off one itemizing of the text
    /// and the word cache (<see cref="PrefixWidths"/>), where laying each prefix out shapes it
    /// whole and caches it as a string of its own. Null where a prefix is not measured as the
    /// sum of its words: text on the BiDi path or with a newline in it, and on the reference
    /// path.
    /// </summary>
    public ITextPrefixWidths? MeasurePrefixes(string text, ResolvedInheritedStyle style)
    {
        if (!ReuseAcrossWidths || string.IsNullOrEmpty(text) || text.Contains('\n')
            || NeedsBidi(text, style, PlainPathForLtr))
            return null;
        BeginParagraph(text);
        return BuildPrefixWidths(text, style) is { Runs.Length: > 0 } table
            ? new MeasuredPrefixes(this, table, style)
            : null;
    }

    // A prefix is laid out as a paragraph of its own, and its Han characters take the face its
    // kana or Hangul call for (ScriptLanguage.CjkContextOf) — so a prefix that stops short of
    // the text's first kana can be drawn in another face than the table was itemized in. Such
    // a prefix is laid out the long way. Text with neither, nearly all of it, never asks.
    private sealed class MeasuredPrefixes(TextPipeline pipeline, PrefixWidths table, ResolvedInheritedStyle style)
        : ITextPrefixWidths
    {
        private readonly bool _contextVaries = ScriptLanguage.CjkContextChars(table.Text) is (true, _) or (_, true);
        private readonly string? _context = ScriptLanguage.CjkContextOf(table.Text);

        public int Length => table.Text.Length;

        public float Width(int length)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(length);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(length, table.Text.Length);
            if (_contextVaries && ScriptLanguage.CjkContextOf(table.Text.AsSpan(0, length)) != _context)
                return pipeline.Layout(table.Text[..length], style, float.PositiveInfinity, maxLines: 1).Size.Width;
            return pipeline.PrefixWidth(table, length, style);
        }
    }

    /// <summary>
    /// <see cref="MeasurePrefixes(string, ResolvedInheritedStyle)"/> of
    /// <c>text.Substring(start, length)</c>, the same widths to the bit, without the substring
    /// and built in lists the pipeline keeps: the answer is a view, good until the next call into
    /// this pipeline that measures or lays text out (this one included), and must not be kept.
    /// Null where the substring's would be.
    /// </summary>
    public ITextPrefixWidths? MeasurePrefixes(string text, int start, int length, ResolvedInheritedStyle style)
    {
        var span = text.AsSpan(start, length);   // throws for a range outside the text
        var view = _prefixView ??= new PrefixView(this);
        view.Release();
        if (!ReuseAcrossWidths || span.IsEmpty || span.Contains('\n') || NeedsBidi(span, style, PlainPathForLtr))
            return null;

        // The range is a paragraph of its own, as the substring would be (BeginParagraph): its Han
        // characters take the face its own kana or Hangul call for, not the whole text's.
        FollowCulture();
        var chars = ScriptLanguage.CjkContextChars(span);
        _paragraph = "";
        _hanContext = ScriptLanguage.CjkContextOf(chars);
        _hanContextKnown = true;
        FillPrefixTable(text, start, start + length, style, view.Runs, view.Segments);
        view.Hold(text, start, length, style, contextVaries: chars.Kana || chars.Hangul, _hanContext);
        return view;
    }

    private PrefixView? _prefixView;

    /// <summary>
    /// The prefix widths of a range of a text
    /// (<see cref="MeasurePrefixes(string, int, int, ResolvedInheritedStyle)"/>), in lists the
    /// pipeline keeps from one range to the next: one view per pipeline, filled again by each call,
    /// and let go of — the text with it — by the next call that measures or lays text out
    /// (<see cref="BeginParagraph"/>). A text area's line-break search asks for one for every line
    /// it wraps, and a table of its own each — a string of the range, an exact copy of its
    /// segments, its runs and the objects holding them — was nearly all a wrapped document's line
    /// pass allocated: 1.3 MB a step of a resize drag over a 900-line document.
    /// </summary>
    private sealed class PrefixView(TextPipeline pipeline) : ITextPrefixWidths
    {
        public readonly List<PrefixRun> Runs = new();
        public readonly List<PrefixSegment> Segments = new();
        private string? _text;
        private int _start, _length;
        private ResolvedInheritedStyle _style;
        private bool _contextVaries;
        private string? _context;

        // While Width lays a prefix out the long way, which begins a paragraph of its own: the
        // view is still in use.
        private bool _layingOut;

        public bool Holding => _text is not null;

        public int Length => _length;

        public void Hold(string text, int start, int length, ResolvedInheritedStyle style, bool contextVaries, string? context)
        {
            (_text, _start, _length, _style, _contextVaries, _context) = (text, start, length, style, contextVaries, context);
        }

        public void Release()
        {
            if (_layingOut) return;
            _text = null;
            Runs.Clear();   // their faces
            // A whole document's single line can have a hundred thousand segments: its list is not kept.
            if (Segments.Capacity > KeptPrefixSegments)
            {
                Segments.Clear();
                Segments.TrimExcess();
            }
        }

        public float Width(int length)
        {
            string text = _text ?? throw new InvalidOperationException(
                "These prefix widths were good until the next call into their pipeline, which has been made.");
            ArgumentOutOfRangeException.ThrowIfNegative(length);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(length, _length);
            if (_contextVaries && ScriptLanguage.CjkContextOf(text.AsSpan(_start, length)) != _context)
            {
                _layingOut = true;
                try
                {
                    return pipeline.Layout(text.Substring(_start, length), _style, float.PositiveInfinity, maxLines: 1).Size.Width;
                }
                finally
                {
                    _layingOut = false;
                }
            }
            return pipeline.PrefixWidth(text, _start, _length, System.Runtime.InteropServices.CollectionsMarshal.AsSpan(Runs),
                System.Runtime.InteropServices.CollectionsMarshal.AsSpan(Segments), _style.FontSize, _style.LetterSpacing, length, _style);
        }
    }

    /// <summary>
    /// What one-line Ellipsis needs of a string at any width, worked out once.
    /// <see cref="Exact"/> says the shortcut gives the reference path's answer for it.
    /// </summary>
    private sealed record OneLineEllipsis(bool Exact, PrefixWidths? Prefixes, float EllipsisWidth, float Height);

    /// <summary>
    /// One-line Ellipsis without breaking the text into lines first: exactly what
    /// <see cref="ComputeWrapped"/> returns with Ellipsis, or null for a string where that is
    /// not proven (the caller then takes it).
    /// Only reached for a text with no newline that is wider than <paramref name="maxWidth"/>.
    /// </summary>
    private TextLayout? EllipsizeOneLine(string text, ResolvedInheritedStyle style, ShapedText shaped, float maxWidth)
    {
        if (!ReuseAcrossWidths) return null;
        var e = shaped.OneLineEllipsis ??= ProveOneLineEllipsis(text, style, shaped);
        if (!e.Exact) return null;

        // The ellipsis on the wrap pass's one line, which starts at 0: the line rebuilt
        // with an ellipsis, in a layout keeping the wrapped line's height.
        var line = BuildEllipsizedLine(text, text, 0, style, maxWidth, e.Prefixes, e.EllipsisWidth);
        return new TextLayout([line], new Size(Math.Max(0f, line.Width), e.Height));
    }

    // With maxLines 1 the wrap pass keeps one line L, and the ellipsis replaces it with
    // BuildEllipsizedLine(text[L.Start..]) in a layout of L's height — unless L is the whole
    // text and fits, when the wrapped layout stands. So the shortcut is exact for a string
    // when, at every width the text overflows:
    //  - L.Start is 0. The pass starts a line past 0 only at a segment it reaches having
    //    emitted nothing with a line width of exactly 0 — when the running sum of the break
    //    segments' widths (BreakIntoLines' own lineWidth) comes to 0, as after a leading
    //    soft hyphen of no width. Checked below: every running sum is positive.
    //  - L is never the whole text. With L.Start 0, every line the pass emits ends short of
    //    the text's end (FitPrefix never answers the end of a text wider than the box), and
    //    the last resort of one character per line is short of it unless the text IS one
    //    character. Checked: two or more.
    //  - L's height is known. L is a prefix of a one-run string, so it is that run cut
    //    short, or nothing (a line of spaces, trimmed), which has the primary font's
    //    height. Checked: the two heights are the same number.
    private OneLineEllipsis ProveOneLineEllipsis(string text, ResolvedInheritedStyle style, ShapedText shaped)
    {
        var prefixes = PrefixWidthsOf(shaped, text, style);
        float ellipsisWidth = MeasureString(EllipsisString, style);
        float height = shaped.SingleLine!.Lines[0].Height;

        bool exact = prefixes is { Runs.Length: 1 }
            && text.Length >= 2
            && height == EmptyLineOfHeight(style).Size.Height
            && BreakSegmentSumsArePositive(text, style, BreakSegmentsOf(shaped, text)!);
        return new OneLineEllipsis(exact, prefixes, ellipsisWidth, height);
    }

    /// <summary>
    /// One-line Ellipsis for a string the shortcut above cannot prove — more than one font
    /// run, where the replaced line's height depends on which fonts it reached; or a first
    /// segment of no width; or a single character — without shaping the line it replaces: the
    /// wrap pass as it is, its line measured and not shaped (<see cref="MeasureLineText"/>),
    /// then the ellipsis as <see cref="ComputeWrapped"/> puts it there. That reads only the
    /// line's extent, width and height, all exact, and replaces it; where it would keep it
    /// instead (the line is the whole text and fits) the measured line is no line, and the
    /// caller takes the reference path. Null then, and on the reference path.
    /// </summary>
    private TextLayout? EllipsizeMeasuredLine(string text, ResolvedInheritedStyle style, ShapedText shaped, float maxWidth)
    {
        if (!ReuseAcrossWidths) return null;
        var prefixes = PrefixWidthsOf(shaped, text, style);
        float ellipsisWidth = (shaped.OneLineEllipsis ??= ProveOneLineEllipsis(text, style, shaped)).EllipsisWidth;
        var measured = RentLines();
        var size = BreakIntoLines(measured, text, style, maxWidth, 1, BreakSegmentsOf(shaped, text), prefixes);
        if (!TruncatedTail(text, measured, maxWidth, out int start, out int tailEnd))
        {
            GiveBackLines(measured);
            return null;
        }
        string tail = text[start..tailEnd];
        float width = WithLastLine(measured, BuildEllipsizedLine(text, tail, start, style, maxWidth,
            start == 0 && tailEnd == text.Length ? prefixes : BuildPrefixWidths(tail, style), ellipsisWidth));
        return new TextLayout(LayoutLines(measured), new Size(width, size.Height));
    }

    private bool BreakSegmentSumsArePositive(string text, ResolvedInheritedStyle style, BreakSegments segments)
    {
        float lineWidth = 0;
        for (int s = 1; s < segments.Offsets.Count; s++)
        {
            lineWidth += SegmentWidth(text, segments.Offsets, segments, s, style);
            if (!(lineWidth > 0f)) return false;
        }
        return true;
    }

    /// <summary>
    /// <see cref="MeasureWidth"/> of a prefix of a run's text in <paramref name="style"/>, as a
    /// <see cref="TextPrefixMeasurer"/>: one per style, kept. A paragraph's selection drawn over a
    /// line that merged clusters asks for it every frame; a new one each time was 64 B, and one
    /// kept on each paragraph would be 8 B more on every paragraph laid out. Keyed by the style's
    /// value: it is a struct.
    /// </summary>
    internal TextPrefixMeasurer PrefixMeasurer(ResolvedInheritedStyle style)
    {
        var measurers = _prefixMeasurers ??= new();
        if (measurers.TryGetValue(style, out var measurer))
            return measurer;
        if (measurers.Count >= PrefixMeasurerCacheMaxSize)
            measurers.Clear();
        return measurers[style] = NewPrefixMeasurer(style);
    }

    // Its own method: a lambda capturing a parameter makes its closure on entry to the method
    // that declares it, so PrefixMeasurer would make one on a hit too.
    private TextPrefixMeasurer NewPrefixMeasurer(ResolvedInheritedStyle style) =>
        (text, length) => MeasureWidth(text.AsSpan(0, length), style);

    /// <summary>
    /// The width <see cref="Layout(string, ResolvedInheritedStyle, float, int?, TextOverflow)"/>
    /// gives <paramref name="text"/> on one unbounded line, to the bit, without laying it out:
    /// its words' widths added up in the order the shaping adds them (<see cref="MeasureString"/>),
    /// and kept as one number (the width cache). Laying it out would shape it whole and keep that
    /// in the shape cache, which a text area's every line, measured once each when a document
    /// opens to see whether it fits, filled with lines nobody draws. Text on the BiDi path is laid
    /// out, and its width kept in the same cache, under its direction: a caret or a selection on a
    /// line of emoji or marks asks for the widths of the same prefixes every frame
    /// (<see cref="TextPrefixMeasurer"/>), and finds them without a string. Text with a newline in
    /// it is laid out.
    /// </summary>
    public float MeasureWidth(ReadOnlySpan<char> text, ResolvedInheritedStyle style)
    {
        if (text.IsEmpty)
            return 0f;
        if (text.Contains('\n'))
            return Layout(text.ToString(), style, float.PositiveInfinity, maxLines: 1).Size.Width;
        // The plain path's width does not depend on the direction; the BiDi path's can.
        bool bidi = NeedsBidi(text, style, PlainPathForLtr);

        FollowCulture();  // before the lookup: a width is of the culture's Han faces
        var widths = _widths ??= new Widths();
        var key = widths.KeyOf(text, style, bidi ? style.TextDirection : TextDirection.Ltr);
        if (widths.InPlace.TryGet(key, out float width))
            return width;

        var entry = WidthKeyComparer.Instance.Create(key);
        if (bidi)
        {
            width = Layout(entry.Text, style, float.PositiveInfinity, maxLines: 1).Size.Width;
        }
        else
        {
            BeginParagraph(entry.Text);
            width = MeasureString(entry.Text, style);
        }
        widths.Keep(entry, width);
        return width;
    }

    /// <summary>
    /// MeasureWidth's widths. A text area's lines repeat — a JSON response's <c>"draft": false,</c>
    /// every few dozen lines, a request sent again most of its last response — and finding one is
    /// cheaper than adding its words up again: a few hundred bytes an entry (WidthBytes), where
    /// the shape cache keeps each line's glyphs. Looked up by a span of the document
    /// (<see cref="WidthSpanKey"/>): a line becomes a string of its own only on a miss, to be the
    /// entry's key.
    /// </summary>
    private sealed class Widths
    {
        public readonly LruCache<WidthKey, float> Cache =
            new(capacity: 16384, WidthKeyComparer.Instance, maxWeight: WidthCacheBytes);
        public readonly LruCache<WidthKey, float>.InPlace<WidthSpanKey> InPlace;

        // The style last asked in, and the hash of its share of a key.
        private ResolvedInheritedStyle _style;
        private int _styleHash;

        // The last 16,384 hashes measured: about as many lines as two JSON responses have
        // distinct. Two to a slot, the older one out: two lines that take turns would otherwise
        // push each other out for good.
        private readonly int[] _seen = new int[2 * 8192];

        public Widths() => InPlace = Cache.LookedUpBy<WidthSpanKey>();

        public WidthSpanKey KeyOf(ReadOnlySpan<char> text, in ResolvedInheritedStyle style, TextDirection direction)
        {
            if (!ReferenceEquals(style.FontFamily, _style.FontFamily) || style.FontSize != _style.FontSize
                || style.FontWeight != _style.FontWeight || style.FontSlant != _style.FontSlant
                || style.LetterSpacing != _style.LetterSpacing)
            {
                _style = style;
                _styleHash = WidthKeyComparer.StyleHash(style);
            }
            return new WidthSpanKey(text, style, direction, WidthKeyComparer.HashOf(text, _styleHash, direction));
        }

        // Kept while there is room. Once the cache is full, only a line measured lately is: most
        // lines of a document are measured once, and putting each in a full cache, to push another
        // out, cost the lines that never repeat more than the cache saved on those that do (a
        // 900-line document of prose opened 9 % slower than with no cache, the suite's 19,000-line
        // JSON response 12 % faster; with this, 3 % and 24 %). Kept whatever the room, a line seen
        // again in the next document stays: Meridian's response to a request sent again shares
        // most of its lines with the last one, and measuring those again took 23 ms a response
        // where the shape cache had them, 12 ms with them found here.
        public void Keep(WidthKey entry, float width)
        {
            int bytes = WidthBytes(entry.Text);
            if (SeenBefore(entry.Hash) | Cache.Weight + bytes <= WidthCacheBytes)
            {
                // Put, not Add, as for words: a key the lookup in place missed would make Add throw.
                Cache.Put(entry, width, bytes);
            }
        }

        private bool SeenBefore(int hash)
        {
            int at = (hash & 8191) << 1;
            if (_seen[at] == hash || _seen[at + 1] == hash) return true;
            _seen[at + 1] = _seen[at];
            _seen[at] = hash;
            return false;
        }
    }

    /// <summary>
    /// The advance every printable ASCII character (U+0020 to U+007E) has in
    /// <paramref name="style"/>'s face at its size, or 0 when that is not proven (see
    /// <see cref="IRenderContext.MonospaceAsciiAdvance"/>). Proven once per face and size, with
    /// the shaper: the face has its own glyph for each of the 95, so none is drawn from a fallback
    /// face, whose advance says nothing about this one; each shaped alone is one glyph of the one
    /// advance; and a string holding every ordered pair of them side by side
    /// (<see cref="AllAsciiPairs"/>) shapes to one glyph a character, every one of that advance —
    /// no pair kerned, no two joined into a ligature, whatever the face says of itself. A line of
    /// them measures as its words' shapings added up (<see cref="MeasureString"/>), so it comes to
    /// its count times the advance, within float rounding. 0 with letter spacing.
    /// </summary>
    public float MonospaceAsciiAdvance(ResolvedInheritedStyle style)
    {
        if (style.LetterSpacing != 0f || !(style.FontSize > 0f))
            return 0f;
        var font = _fonts.Resolve(style.FontFamily, style.FontWeight, style.FontSlant);
        var key = new DigitKey(font, style.FontSize);
        var proven = _asciiAdvances ??= new();
        if (!proven.TryGetValue(key, out float advance))
        {
            if (proven.Count >= AsciiAdvanceCacheMaxSize)
                proven.Clear();
            proven[key] = advance = ProveMonospaceAscii(font, style.FontSize);
        }
        return advance;
    }

    // MonospaceAsciiAdvance's answers by face and size, 0 for not monospace; a theme cycling
    // through sizes cannot grow it past this.
    private Dictionary<DigitKey, float>? _asciiAdvances;
    private const int AsciiAdvanceCacheMaxSize = 64;

    private float ProveMonospaceAscii(IResolvedFont font, float fontSize)
    {
        for (int c = ' '; c <= '~'; c++)
            if (!font.HasGlyph(c))
                return 0f;
        float advance = 0f;
        for (int c = ' '; c <= '~'; c++)
        {
            var alone = _shaper.Shape(AsciiChars[c - ' '], font, fontSize);
            if (alone.Glyphs.Length != 1 || alone.Glyphs[0].Advance != alone.Width
                || (c > ' ' && alone.Width != advance))
                return 0f;
            advance = alone.Width;
        }
        if (!(advance > 0f))
            return 0f;
        // Beside each other, each glyph advances by that much, but for the float rounding of the
        // pen the shaper moves glyph by glyph (a 9,026-character run at 13.5 px rounded advances
        // by 1/256 px): pieces short enough to keep it far below the tolerance, and a tolerance
        // far below the least a face can kern or join by — one unit of its em (one of 2,048 is
        // 1/1,200 of Cascadia Mono's advance, 8.3e-4 of it; this allows 2.4e-4).
        float tolerance = advance * AsciiAdvanceTolerance;
        foreach (var piece in AsciiPairPieces)
        {
            var run = _shaper.Shape(piece, font, fontSize);
            if (run.Glyphs.Length != piece.Length)
                return 0f;
            foreach (var glyph in run.Glyphs)
                if (!(Math.Abs(glyph.Advance - advance) <= tolerance))
                    return 0f;
        }
        return advance;
    }

    internal const float AsciiAdvanceTolerance = 1f / 4096;

    private static readonly string[] AsciiChars =
        Enumerable.Range(' ', '~' - ' ' + 1).Select(c => ((char)c).ToString()).ToArray();


    /// <summary>
    /// Every ordered pair of printable ASCII characters side by side, each once: a de Bruijn
    /// sequence of order 2 over the 95 — each character, then that character before each later
    /// one in turn: their Lyndon words in order (9,025 characters, a cycle) — with its first
    /// character again at the end for the pair that closes the cycle.
    /// </summary>
    internal static readonly string AllAsciiPairs = AsciiPairs();

    // AllAsciiPairs in pieces of at most 256 characters, each starting with the last of the one
    // before, so every pair is inside one. (Declared after it: static fields start in order.)
    internal static readonly string[] AsciiPairPieces =
        Enumerable.Range(0, (AllAsciiPairs.Length - 2) / 255 + 1)
            .Select(p => AllAsciiPairs.Substring(255 * p, Math.Min(256, AllAsciiPairs.Length - 255 * p)))
            .ToArray();

    private static string AsciiPairs()
    {
        const int Count = '~' - ' ' + 1;
        var pairs = new System.Text.StringBuilder(Count * Count + 1);
        for (int i = 0; i < Count; i++)
        {
            pairs.Append((char)(' ' + i));
            for (int j = i + 1; j < Count; j++)
                pairs.Append((char)(' ' + i)).Append((char)(' ' + j));
        }
        return pairs.Append(' ').ToString();
    }

    // Total advance width of a string, itemized and shaped the same way as a line. Internal
    // for the tests that hold it to the glyph runs it adds up.
    internal float MeasureString(string s, ResolvedInheritedStyle style) => MeasureRange(s, 0, s.Length, style);

    /// <summary>
    /// <c>MeasureString(text[from..to])</c>, to the bit, without the string: the font runs
    /// itemized as ranges of the text, as a string of those characters itemizes, and each
    /// measured where it lies, its words looked up in the word cache by span. Measures cut
    /// their range out, and itemizing cut each of several runs out of that again. Internal for
    /// the tests that hold it to MeasureString and to what it allocates.
    /// </summary>
    internal float MeasureRange(string text, int from, int to, ResolvedInheritedStyle style, bool rtl = false)
    {
        if (to <= from)
            return 0;

        var runs = Kept(_measuredRuns);
        ItemizeRange(text, from, to, style, runs);
        float width = MeasureRuns(text, runs, style, rtl);
        runs.Clear();   // the runs' faces are not kept alive by the scratch list
        return width;
    }

    // MeasureRange's font runs, while it adds them up (nothing it calls measures a range).
    private readonly List<RunRange> _measuredRuns = new();

    // The width of font runs of text, added up as MeasureString adds a string's.
    private float MeasureRuns(string text, List<RunRange> runs, ResolvedInheritedStyle style, bool rtl = false)
    {
        float width = 0;
        foreach (var (start, end, font) in runs)
            width += MeasureRun(text, start, end, font, style.FontSize, style.LetterSpacing, rtl);
        return width;
    }

    // MeasureRuns for characters that are no string's, left to right: an ellipsized line's, whose
    // runs cut the string they keep when they are read. The same segments added in the same order.
    private float MeasureChars(ReadOnlySpan<char> text, List<RunRange> runs, ResolvedInheritedStyle style)
    {
        float total = 0;
        foreach (var (from, to, font) in runs)
        {
            float width = 0;
            int glyphs = 0;
            int fontHash = WordCache.FontHash(font, style.FontSize);
            for (int i = from; i < to;)
            {
                bool isSpace = text[i] == ' ';
                int start = i;
                while (i < to && (text[i] == ' ') == isSpace) i++;
                var seg = ShapeSegment(text.Slice(start, i - start), start, font, style.FontSize, fontHash);
                width += seg.Width;
                glyphs += seg.Glyphs;
            }
            total += style.LetterSpacing == 0f || glyphs == 0 ? width : width + style.LetterSpacing * glyphs;
        }
        return total;
    }

    // ShapeWithSpacing(text[from..to], …).Width without building the glyph array it would hand
    // back, or the run's string: the same segment widths added in the same order, letter spacing
    // applied the same way.
    private float MeasureRun(string text, int from, int to, IResolvedFont font, float fontSize, float letterSpacing,
        bool rtl = false)
    {
        float width;
        int glyphs;
        if (rtl)
        {
            var run = _shaper.Shape(text[from..to], font, fontSize, rtl: true);
            width = run.Width;
            glyphs = run.Glyphs.Length;
        }
        else if (text.AsSpan(from, to - from).IndexOf(' ') < 0)
        {
            var word = ShapeSegment(text, from, to - from, font, fontSize, WordCache.FontHash(font, fontSize));
            width = word.Width;
            glyphs = word.Glyphs;
        }
        else
        {
            width = 0;
            glyphs = 0;
            int fontHash = WordCache.FontHash(font, fontSize);
            for (int i = from; i < to;)
            {
                bool isSpace = text[i] == ' ';
                int start = i;
                while (i < to && (text[i] == ' ') == isSpace) i++;
                var seg = ShapeSegment(text, start, i - start, font, fontSize, fontHash);
                width += seg.Width;
                glyphs += seg.Glyphs;
            }
        }
        return letterSpacing == 0f || glyphs == 0 ? width : width + letterSpacing * glyphs;
    }

    // Shapes a run, then applies letter spacing by widening each glyph's advance
    // and shifting the following glyphs. Returns the shaper's result unchanged
    // when spacing is zero (the common case), so there is no allocation cost.
    private ShapedRun ShapeWithSpacing(string text, IResolvedFont font, float fontSize, float letterSpacing,
        bool rtl = false)
    {
        // RTL runs bypass the word cache and digit fast path in stage 1: correctness
        // first, and the caches' space-split assumptions are unproven for RTL
        // (Arabic joining does not cross spaces, but "likely sound" is not proven —
        // docs/plans/14-bidi.md). LTR text keeps every cache untouched.
        // Spacing moves every glyph, so a spaced run needs its glyphs now.
        var run = rtl
            ? _shaper.Shape(text, font, fontSize, rtl: true)
            : ShapeByWords(text, font, fontSize, composeNow: letterSpacing != 0f);
        if (letterSpacing == 0f || run.Glyphs.Length == 0)
            return run;

        var glyphs = run.Glyphs;
        var spaced = new GlyphPosition[glyphs.Length];
        float shift = 0;
        for (int i = 0; i < glyphs.Length; i++)
        {
            var g = glyphs[i];
            spaced[i] = g with { X = g.X + shift, Advance = g.Advance + letterSpacing };
            shift += letterSpacing;
        }

        return new ShapedRun(run.Text, run.Font, run.FontSize,
            run.Width + letterSpacing * glyphs.Length, spaced);
    }

    // Shapes a font run by splitting it into maximal space / non-space
    // segments, shaping each through the word cache, and concatenating. Single
    // segments (no space) go straight through the cache. Internal for the tests that
    // hold it to the segments shaped apart, and to what it allocates.
    //
    // A run of several segments gets its width and its glyph count here, and its glyph array
    // only when something reads it (ShapedRun.Glyphs, ComposeGlyphs): a paragraph's lines off
    // screen, the single line of a text that wraps, and the layouts at each width a resize drag
    // passes through are laid out and never drawn. Their glyphs were 59 % of what wrapping 150
    // paragraphs allocated at each width, and 37 % of opening a 5,000-block rich text document
    // (plan 17, round 5). composeNow makes the array now (letter spacing moves every glyph).
    //
    // Every run made here holds glyphs that follow from its text, face and size, and is marked so
    // (ShapedRun.GlyphsFollowText) — except one made for letter spacing to move.
    internal ShapedRun ShapeByWords(string text, IResolvedFont font, float fontSize, bool composeNow = false)
    {
        int fontHash = WordCache.FontHash(font, fontSize);
        if (text.IndexOf(' ') < 0)
        {
            var word = ShapeSegment(text, 0, text.Length, font, fontSize, fontHash);
            // Fresh ShapedRun per call site: OffsetX is mutated by layout, so the
            // cached instance must never be handed out. The glyph array is
            // read-only in practice and safe to share.
            var wordGlyphs = word.Run?.Glyphs;
            if (wordGlyphs is null)
                ComposeDigits(text, 0, text.Length, word.Digits!, 0f, wordGlyphs = new GlyphPosition[text.Length], 0);
            return new ShapedRun(text, font, fontSize, word.Width, wordGlyphs) { GlyphsFollowText = !composeNow };
        }

        if (ComposeGlyphsWhenRead && !composeNow)
        {
            // The width as the composing pass below adds it up: the segments' widths, in order.
            float width = 0;
            int glyphCount = 0;
            for (int i = 0; i < text.Length;)
            {
                bool isSpace = text[i] == ' ';
                int start = i;
                while (i < text.Length && (text[i] == ' ') == isSpace) i++;
                var word = ShapeSegment(text, start, i - start, font, fontSize, fontHash);
                width += word.Width;
                glyphCount += word.Glyphs;
            }
            if (glyphCount <= ushort.MaxValue)
                return new ShapedRun(text, font, fontSize, width, (ushort)glyphCount, this);
        }

        // The segments first, counting their glyphs, then one array of exactly that many:
        // each segment's glyphs at the widths of the segments before it, added in order.
        var words = _words;
        words.Clear();
        int count = 0;
        for (int i = 0; i < text.Length;)
        {
            bool isSpace = text[i] == ' ';
            int start = i;
            while (i < text.Length && (text[i] == ' ') == isSpace) i++;
            var word = ShapeSegment(text, start, i - start, font, fontSize, fontHash);
            words.Add(word);
            count += word.Glyphs;
        }

        var glyphs = new GlyphPosition[count];
        float x = 0;
        int at = 0;
        foreach (var word in words)
        {
            if (word.Run is { } run)
            {
                foreach (var g in run.Glyphs)
                    glyphs[at++] = g with { X = g.X + x };
            }
            else
            {
                at = ComposeDigits(text, word.Start, word.Glyphs, word.Digits!, x, glyphs, at);
            }
            x += word.Width;
        }
        words.Clear();
        return new ShapedRun(text, font, fontSize, x, glyphs) { GlyphsFollowText = !composeNow };
    }

    // ShapeByWords' segments, between its two passes (one list per pipeline, as a pipeline is
    // used from one thread; nothing ShapeByWords calls comes back to it).
    private readonly List<WordShape> _words = new();

    /// <summary>
    /// The glyph array of a run ShapeByWords made without one, the first time it is read: the
    /// same segments looked up again — a segment the word cache has let go of since is shaped
    /// again, to the same glyphs — each segment's glyphs at the widths of the segments before
    /// it, added in the order ShapeByWords added the run's width. So the array is the one
    /// ShapeByWords would have made, to the bit. It uses nothing a call into the pipeline holds
    /// between two of its steps (no scratch list, no paragraph), so it may run between any two.
    /// </summary>
    internal GlyphPosition[] ComposeGlyphs(ShapedRun run, int glyphCount)
    {
        var glyphs = new GlyphPosition[glyphCount];
        ComposeGlyphs(run, glyphs);
        return glyphs;
    }

    /// <summary>The same glyphs written into <paramref name="glyphs"/> (exactly the run's glyph
    /// count long), for a reader that only copies them on (ShapedRun.ComposeGlyphsInto).</summary>
    internal void ComposeGlyphs(ShapedRun run, Span<GlyphPosition> glyphs)
    {
        string text = run.Text;
        var font = run.Font;
        float fontSize = run.FontSize;
        int fontHash = WordCache.FontHash(font, fontSize);
        float x = 0;
        int at = 0;
        for (int i = 0; i < text.Length;)
        {
            bool isSpace = text[i] == ' ';
            int start = i;
            while (i < text.Length && (text[i] == ' ') == isSpace) i++;
            var word = ShapeSegment(text, start, i - start, font, fontSize, fontHash);
            if (word.Run is { } shaped)
            {
                foreach (var g in shaped.Glyphs)
                    glyphs[at++] = g with { X = g.X + x };
            }
            else
            {
                at = ComposeDigits(text, word.Start, word.Glyphs, word.Digits!, x, glyphs, at);
            }
            x += word.Width;
        }
    }

    /// <summary>
    /// When false, ShapeByWords makes every run's glyph array with the run — the reference: the
    /// same runs, glyphs and widths, made whether anything draws them or not. When true (the
    /// default), a run of several segments makes its array the first time it is read
    /// (<see cref="ShapedRun.Glyphs"/>). Tests hold the two to the same output.
    /// </summary>
    internal bool ComposeGlyphsWhenRead { get; init; } = true;

    /// <summary>
    /// A space or non-space segment of a run, shaped: the word cache's run for it, or, for an
    /// all-digit segment in a font that passed the digit proof, the font's digit table — whose
    /// glyphs are composed only where glyphs are wanted. <see cref="Width"/> and
    /// <see cref="Glyphs"/> (the glyph count) are the segment's either way.
    /// </summary>
    private readonly record struct WordShape(ShapedRun? Run, DigitTable? Digits, int Start, float Width, int Glyphs);

    // text[start..start + length], shaped: from the digit table, else from the word cache, looked
    // up by the span; a miss shapes the segment as a string of its own (the text itself when the
    // segment is all of it) and keeps it under that string. fontHash is
    // WordCache.FontHash(font, fontSize), which the caller works out once per run.
    private WordShape ShapeSegment(string text, int start, int length, IResolvedFont font, float fontSize,
        int fontHash, bool withCuts = false)
    {
        var segment = text.AsSpan(start, length);
        if (DigitTableFor(segment, font, fontSize) is { } digits)
            return DigitSegment(segment, digits, start);

        if (!_wordShapeCache.TryGet(segment, font, fontSize, fontHash, out var run))
            run = ShapeNewWord(text.Substring(start, length), font, fontSize, fontHash, withCuts);
        return new WordShape(run, null, start, run.Width, run.Glyphs.Length);
    }

    // The same for a segment of characters that are no string's, which starts at `start` of them.
    private WordShape ShapeSegment(ReadOnlySpan<char> segment, int start, IResolvedFont font, float fontSize, int fontHash)
    {
        if (DigitTableFor(segment, font, fontSize) is { } digits)
            return DigitSegment(segment, digits, start);

        if (!_wordShapeCache.TryGet(segment, font, fontSize, fontHash, out var run))
            run = ShapeNewWord(segment.ToString(), font, fontSize, fontHash);
        return new WordShape(run, null, start, run.Width, run.Glyphs.Length);
    }

    private static WordShape DigitSegment(ReadOnlySpan<char> segment, DigitTable digits, int start)
    {
        float x = 0;
        foreach (char c in segment)
            x += digits.Advances[c - '0'];
        return new WordShape(null, digits, start, x, segment.Length);
    }

    private ShapedRun ShapeNewWord(string word, IResolvedFont font, float fontSize, int fontHash, bool withCuts = false)
    {
        if (withCuts && _cutShaper is not null && word.Length >= MinCutWord && PrefixesFromCuts)
        {
            var cutRun = _cutShaper.Shape(word, font, fontSize, out var cuts);
            _wordShapeCache.Put(word, font, fontSize, fontHash, cutRun, WordBytes(word, cutRun) + (cuts?.Bytes ?? 0), cuts);
            return cutRun;
        }
        var run = _shaper.Shape(word, font, fontSize);
        _wordShapeCache.Put(word, font, fontSize, fontHash, run, WordBytes(word, run));
        return run;
    }

    // The proven digit table for an all-digit segment, or null when the segment isn't pure
    // digits / the font+size failed the proof (which runs at the first digits it meets).
    private DigitTable? DigitTableFor(ReadOnlySpan<char> segment, IResolvedFont font, float fontSize)
    {
        if (segment.Length == 0)
            return null;
        foreach (char c in segment)
            if (c is < '0' or > '9')
                return null;

        var key = new DigitKey(font, fontSize);
        if (!_digitTables.TryGetValue(key, out var table))
        {
            table = ProveDigitsContextFree(font, fontSize);
            _digitTables[key] = table;
        }
        return table;
    }

    // Writes the glyphs of the all-digit text[start..start + length] into glyphs from index at,
    // each at the advances before it in the segment plus x (the segment's place in its run), and
    // returns the index after them.
    private static int ComposeDigits(string text, int start, int length, DigitTable table, float x,
        Span<GlyphPosition> glyphs, int at)
    {
        float advanced = 0;
        for (int i = start; i < start + length; i++)
        {
            int d = text[i] - '0';
            glyphs[at++] = new GlyphPosition(table.GlyphIds[d], advanced + x, 0, table.Advances[d]);
            advanced += table.Advances[d];
        }
        return at;
    }

    // One-time proof per font+size (110 shaper calls): every digit must shape
    // to exactly one zero-offset glyph, and every digit pair must shape to the
    // two glyphs at exactly the summed nominal advances. Passing means digit
    // strings of any length compose identically to real shaping (pair-local
    // kerning is all a GPOS table can apply to a digit run).
    private DigitTable? ProveDigitsContextFree(IResolvedFont font, float fontSize)
    {
        const float Tol = 0.01f;
        var table = new DigitTable();

        for (int d = 0; d < 10; d++)
        {
            var run = _shaper.Shape(((char)('0' + d)).ToString(), font, fontSize);
            if (run.Glyphs.Length != 1)
                return null;
            var g = run.Glyphs[0];
            if (Math.Abs(g.X) > Tol || Math.Abs(g.Y) > Tol)
                return null;
            table.GlyphIds[d] = g.GlyphId;
            table.Advances[d] = run.Width;
        }

        for (int a = 0; a < 10; a++)
        {
            for (int b = 0; b < 10; b++)
            {
                var run = _shaper.Shape($"{(char)('0' + a)}{(char)('0' + b)}", font, fontSize);
                if (run.Glyphs.Length != 2
                    || run.Glyphs[0].GlyphId != table.GlyphIds[a]
                    || run.Glyphs[1].GlyphId != table.GlyphIds[b]
                    || Math.Abs(run.Glyphs[0].X) > Tol
                    || Math.Abs(run.Glyphs[0].Y) > Tol
                    || Math.Abs(run.Glyphs[1].Y) > Tol
                    || Math.Abs(run.Glyphs[1].X - table.Advances[a]) > Tol
                    || Math.Abs(run.Width - (table.Advances[a] + table.Advances[b])) > Tol)
                {
                    return null;
                }
            }
        }

        return table;
    }

    // Adjusts a line's ascent/leading so its total height equals an explicit
    // line-height, centering the text within the extra space (half above, half
    // below). A line-height smaller than the natural text height is ignored.
    private static (float Ascent, float Leading) ApplyLineHeight(
        float ascent, float descent, float naturalLeading, float lineHeight)
    {
        if (lineHeight <= 0f)
            return (ascent, naturalLeading);

        float extra = lineHeight - (ascent + descent);
        if (extra <= 0f)
            return (ascent, 0f);

        float half = extra / 2f;
        return (ascent + half, half);
    }

    // One code point on from index: how far a line that fits nothing advances. A line of
    // SnapPrefix(index + 1) stopped short of a surrogate pair by stepping back off it, so a
    // pair wider than the box (an emoji in a narrow column) gave an empty line that never
    // moved on, and wrapping with no line limit added such lines until memory ran out.
    private static int NextCodePoint(string text, int index) =>
        index + 1 < text.Length && char.IsHighSurrogate(text[index]) && char.IsLowSurrogate(text[index + 1])
            ? index + 2
            : index + 1;

    // Moves an index back off the trailing half of a surrogate pair.
    private static int SnapPrefix(string text, int index) =>
        index > 0 && index < text.Length && char.IsLowSurrogate(text[index]) && char.IsHighSurrogate(text[index - 1])
            ? index - 1
            : index;

    private static TextLayout BuildSingleLineLayout(List<ShapedRun> runs, float totalWidth, int textLength, float lineHeight)
    {
        float maxAscent = 0, maxDescent = 0, maxLeading = 0;
        float x = 0;
        foreach (var run in runs)
        {
            run.OffsetX = x;
            x += run.Width;
            // Font metrics are at reference size 1 — scale by actual fontSize
            maxAscent = Math.Max(maxAscent, Math.Abs(run.Font.Ascent) * run.FontSize);
            maxDescent = Math.Max(maxDescent, run.Font.Descent * run.FontSize);
            maxLeading = Math.Max(maxLeading, run.Font.Leading * run.FontSize);
        }

        (maxAscent, maxLeading) = ApplyLineHeight(maxAscent, maxDescent, maxLeading, lineHeight);
        var line = new TextLine(runs, totalWidth, maxAscent, maxDescent, maxLeading) { Start = 0, Length = textLength };
        return new TextLayout([line], new Size(totalWidth, line.Height));
    }

    // The lines of text, broken greedily, into `lines` (empty), each placed under the one before;
    // returns the size of a layout of them. measureOnly, the whole text's prefix widths, makes
    // every line a measured one (MeasureLineText) for EllipsizeMeasuredLine: the same pass,
    // nothing shaped.
    private Size BreakIntoLines(List<TextLine> lines, string text, ResolvedInheritedStyle style, float maxWidth,
        int? maxLines, BreakSegments? segments, PrefixWidths? measureOnly = null)
    {
        int textOffset = 0;

        var allBreaks = segments?.Offsets ?? BreakOffsets(text);

        // Greedy line breaking
        float lineWidth = 0;

        for (int s = 1; s < allBreaks.Count; s++)
        {
            int segEnd = allBreaks[s];

            // Check for explicit newline
            bool isNewline = segEnd - allBreaks[s - 1] == 1 && text[allBreaks[s - 1]] == '\n';
            if (isNewline)
            {
                // Emit current line
                lines.Add(NewLine(textOffset, allBreaks[s - 1]));
                textOffset = segEnd;
                lineWidth = 0;
                if (maxLines.HasValue && lines.Count >= maxLines.Value)
                    break;
                continue;
            }

            float segWidth = SegmentWidth(text, allBreaks, segments, s, style);

            if (lineWidth + segWidth > maxWidth && lineWidth > 0)
            {
                // Break here — emit line up to previous segment
                lines.Add(NewLine(textOffset, allBreaks[s - 1]));
                textOffset = allBreaks[s - 1];
                lineWidth = 0;
                if (maxLines.HasValue && lines.Count >= maxLines.Value)
                    break;
            }

            // A segment that overflows a line all by itself offers nowhere to break: cut it
            // by character rather than let it run out of the box. Only reachable at the start
            // of a line — anywhere else the block above has just broken.
            if (lineWidth == 0 && segWidth > maxWidth)
            {
                textOffset = SplitByCharacter(text, allBreaks[s - 1], segEnd, style, maxWidth, lines, maxLines,
                    measureOnly: measureOnly);
                if (maxLines.HasValue && lines.Count >= maxLines.Value)
                    break;
                lineWidth = MeasureRange(text, textOffset, segEnd, style);
                continue;
            }

            lineWidth += segWidth;
        }

        // Emit final line. A trailing run with nowhere to break needs the same character-level
        // treatment as one in the middle: the loop above never sees the last segment overflow,
        // because there is no following segment to trigger a break.
        if (textOffset < text.Length && (!maxLines.HasValue || lines.Count < maxLines.Value))
        {
            // The last line usually fits, and then the search for its cut measures all of it,
            // several prefixes over: from the line's own segments, that is one measure, and
            // the probes cost a word-cache lookup each. Not for a line that does not fit —
            // possibly a 25,000-character token, which the gallop exists not to measure whole.
            // The table is read for that one search, so it goes into the lists the pipeline keeps,
            // itemized in the paragraph's own Han context as a string of the line would be: one
            // of its own — the line cut out as a string, exact arrays of its runs and segments —
            // was 7 % of what wrapping a paragraph allocated, and more once glyphs were made only
            // where they are read (plan 17, round 5).
            bool lastLineTable = segments is not null && lineWidth <= maxWidth;
            if (lastLineTable)
                FillPrefixTable(text, textOffset, text.Length, style, _prefixRuns, _prefixSegments);
            textOffset = SplitByCharacter(text, textOffset, text.Length, style, maxWidth, lines, maxLines, lastLineTable,
                measureOnly);
            if (lastLineTable)
                ReleasePrefixScratch();
            if (textOffset < text.Length && (!maxLines.HasValue || lines.Count < maxLines.Value))
                lines.Add(NewLine(textOffset, text.Length));
        }

        // Position lines vertically
        float totalHeight = 0;
        float maxLineWidth = 0;
        foreach (var line in lines)
        {
            line.OffsetY = totalHeight;
            totalHeight += line.Height;
            maxLineWidth = Math.Max(maxLineWidth, line.Width);
        }

        return new Size(maxLineWidth, totalHeight);

        TextLine NewLine(int from, int to) => measureOnly is null
            ? ShapeLineText(text, from, to, style, start: from)
            : MeasureLineText(text, from, to, style, measureOnly);
    }

    // Cached width-independent shaping of a string. <see cref="SingleLine"/> is
    // the prebuilt layout used whenever the text fits one line; it is null only
    // when the text contains an explicit newline (which always needs breaking).
    private sealed record ShapedText(float TotalWidth, bool HasNewline, TextLayout? SingleLine)
    {
        // Filled in the first time the string is truncated (Clip or Ellipsis at one line),
        // then reused at every other width: what a resize drag would otherwise rework per
        // frame. Width-independent, and keyed with the shaping, so it lives and dies with it.
        public PrefixWidths? PrefixWidths;
        public bool PrefixWidthsKnown;
        public OneLineEllipsis? OneLineEllipsis;
        // Filled the first time the string is wrapped: its break segments and their widths.
        public BreakSegments? BreakSegments;
    }

    // The line text[from..to), standing at start in its paragraph (from, unless text is a string
    // made for the line: an ellipsized one, an empty one). Trailing whitespace is trimmed for
    // width calculation but kept in the line's extent. Its font runs are itemized as ranges of
    // text and each cut out once, as the string its shaped run keeps (the text itself for a run
    // that is all of it): the line was cut out, cut again without its trailing space, and each of
    // several runs cut out of that, and wrapping 150 paragraphs made a second string for 300 of
    // the 450 lines a step (plan 17, round 6).
    private TextLine ShapeLineText(string text, int from, int to, ResolvedInheritedStyle style, int start)
    {
        int end = to;
        while (end > from && char.IsWhiteSpace(text[end - 1])) end--;
        var fontRuns = Kept(_lineRuns);
        ItemizeRange(text, from, end, style, fontRuns);
        var (ascent, descent, leading) = LineMetrics(fontRuns, style);

        TextLine line;
        if (ComposeRunsWhenRead && fontRuns.Count > 0)
        {
            float width = MeasureRuns(text, fontRuns, style);
            _runsNotMade += fontRuns.Count;
            line = new TextLine(SourceOf(text, style), end - from, width, ascent, descent, leading);
        }
        else
        {
            var runs = ShapeRuns(text, fontRuns, style.FontSize, style.LetterSpacing, out float width);
            line = new TextLine(runs, width, ascent, descent, leading);
        }
        fontRuns.Clear();   // the runs' faces are not kept alive by the scratch list
        line.Start = start;
        line.Length = to - from;
        return line;
    }

    // The shaped runs of font runs of text, side by side; the line keeps the list: room for its
    // runs and no more.
    private List<ShapedRun> ShapeRuns(string text, List<RunRange> fontRuns, float fontSize, float letterSpacing,
        out float width)
    {
        var runs = new List<ShapedRun>(fontRuns.Count);
        width = 0;
        foreach (var (runStart, runEnd, font) in fontRuns)
        {
            var shaped = ShapeWithSpacing(text[runStart..runEnd], font, fontSize, letterSpacing);
            shaped.OffsetX = width;
            width += shaped.Width;
            runs.Add(shaped);
        }
        return runs;
    }

    // ── Runs made when read ──
    //
    // A line needs its width, its metrics and its extent to be laid out. Those are what wrapping
    // and truncating ask of it; its runs, and the strings they keep, only a draw or a caret asks
    // for, and most lines laid out are neither: of the 450 lines a step of a resize drag over 150
    // paragraphs breaks, 36 are drawn (plan 17, round 6). So a line is measured and not shaped
    // (MeasureRuns adds up what ShapeRuns would, to the bit), and makes its runs the first time
    // they are read, from what LineSource keeps. A line drawn pays for itemizing and looking its
    // words up twice.

    /// <summary>
    /// When false, a line makes its runs with the line — the reference: the same runs, widths and
    /// metrics, made whether anything reads them or not. When true (the default), a line on the
    /// plain or the styled path makes them the first time they are read
    /// (<see cref="TextLine.Runs"/>). A line on the BiDi path has them made: a right-to-left run is
    /// measured by shaping it. Tests hold the two to the same output.
    /// </summary>
    internal bool ComposeRunsWhenRead { get; init; } = true;

    // The source of the lines of the layout running now (BeginParagraph lets go of it, and of the
    // text with it).
    private LineSource? _lineSource;

    private LineSource SourceOf(string text, ResolvedInheritedStyle style) =>
        _lineSource is { } kept && ReferenceEquals(kept.Text, text)
            ? kept
            : _lineSource = new LineSource(StyleOf(style, ellipsized: false), text, null);

    // A styled line's source: its memo's, kept for every width, whose spans are a copy already; a
    // string's first layout has none, and copies the spans into a source of its own. Those are the
    // list it was handed as it is now: the caller may fill that list again before the line is read.
    //
    // Not when it was handed many. Each key typed in a paragraph makes a string laid out for the
    // first time, and most of the paragraph is on screen, so a copy of its spans and a second walk
    // over them, a piece of a line at a time, is what most of its lines would pay: 600 spans, 1,300
    // pieces, 2.33 -> 2.59 ms a key (plan 17, round 6). So many are rare, and such a paragraph laid
    // out twice at other widths has its entry's copy.
    internal const int MostSpansCopied = 32;

    private LineSource SourceOf(string text, ResolvedInheritedStyle style, IReadOnlyList<Core.Rendering.TextSpan> spans,
        TextMemo? memo, bool ellipsized)
    {
        if (memo is not null)
        {
            ref var kept = ref ellipsized ? ref memo.EllipsizedLines : ref memo.Lines;
            return kept ??= new LineSource(StyleOf(style, ellipsized), text, memo.Spans);
        }
        if (_lineSource is not { } layout || !ReferenceEquals(layout.Text, text))
            layout = _lineSource = new LineSource(StyleOf(style, false), text, MetricSpansOf(spans));
        return ellipsized ? new LineSource(StyleOf(style, true), text, layout.Spans) : layout;
    }

    // Keyed by value, as the prefix measurers are: a style is a struct. The last one is looked at
    // first: a list's labels are of one style, and hashing a style for each was 5 % of a label.
    private Dictionary<(ResolvedInheritedStyle Style, string? HanContext, bool Ellipsized), LineStyle>? _lineStyles;
    private LineStyle? _lineStyle;
    private const int LineStyleCacheMaxSize = 64;

    // Only reached once a line of the paragraph has itemized, so _hanContext is the paragraph's.
    private LineStyle StyleOf(ResolvedInheritedStyle style, bool ellipsized)
    {
        if (_lineStyle is { } last && last.Ellipsized == ellipsized && last.HanContext == _hanContext && last.Style == style)
            return last;
        var styles = _lineStyles ??= new();
        if (!styles.TryGetValue((style, _hanContext, ellipsized), out var kept))
        {
            if (styles.Count >= LineStyleCacheMaxSize)
                styles.Clear();
            styles[(style, _hanContext, ellipsized)] = kept = new LineStyle(this, style, _hanContext, _culture!, ellipsized);
        }
        return _lineStyle = kept;
    }

    /// <summary>
    /// The runs of a line that kept none (<see cref="TextLine.Runs"/>), the first time they are
    /// read: what <see cref="ShapeRuns"/> or <see cref="StyledRuns"/> gave when the line was laid
    /// out, to the bit. Its font runs come from its own Han context and the culture it was laid out
    /// under, and a word the word cache has let go of since is shaped again, to the same glyphs. It
    /// uses nothing a call into the pipeline holds between two of its steps, so it may run between
    /// any two.
    /// </summary>
    internal List<ShapedRun> ComposeRuns(LineSource source, int start, int drawnLength)
    {
        // A face the culture picks is the one the width was added up in (FollowCulture clears what
        // is keyed on text when it changes, but this line was laid out already).
        var culture = System.Globalization.CultureInfo.CurrentUICulture;
        if (ReferenceEquals(culture, source.Style.Culture))
            return ComposeRunsNow(source, start, drawnLength);
        System.Globalization.CultureInfo.CurrentUICulture = source.Style.Culture;
        try
        {
            return ComposeRunsNow(source, start, drawnLength);
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentUICulture = culture;
        }
    }

    private List<ShapedRun> ComposeRunsNow(LineSource source, int start, int drawnLength)
    {
        var line = source.Style;
        var style = line.Style;
        string text = source.Text;
        if (source.Spans is { } spans)
        {
            var styled = new List<ShapedRun>();
            StyledRuns(text, start, start + drawnLength - (line.Ellipsized ? EllipsisString.Length : 0), style, spans,
                line.Ellipsized, line.HanContext, _composedRuns, styled);
            return styled;
        }

        if (line.Ellipsized)
        {
            text = string.Concat(text.AsSpan(start, drawnLength - EllipsisString.Length), EllipsisString);
            start = 0;
        }
        ItemizeChars(text.AsSpan(start, drawnLength), start, style, line.HanContext, Kept(_composedRuns));
        var runs = ShapeRuns(text, _composedRuns, style.FontSize, style.LetterSpacing, out _);
        _composedRuns.Clear();   // the runs' faces are not kept alive by the scratch list
        return runs;
    }

    // ComposeRuns' font runs: not _lineRuns, which a call into the pipeline holds across its steps.
    private readonly List<RunRange> _composedRuns = new();

    // The font runs of the line being built, as ranges of its text: ShapeLineText's,
    // MeasureLineText's, and those of each piece of a styled or BiDi line. Nothing a line's
    // building calls while it reads them builds a line.
    private readonly List<RunRange> _lineRuns = new();

    // A line's vertical metrics from its font runs: the largest of each over the runs at the
    // style's size (font metrics are at reference size 1), the primary font's for a line with
    // none, then the line height. ShapeLineText and MeasureLineText both take them from here.
    private (float Ascent, float Descent, float Leading) LineMetrics(List<RunRange> fontRuns, ResolvedInheritedStyle style)
    {
        float maxAscent = 0, maxDescent = 0, maxLeading = 0;
        foreach (var (_, _, font) in fontRuns)
        {
            maxAscent = Math.Max(maxAscent, Math.Abs(font.Ascent) * style.FontSize);
            maxDescent = Math.Max(maxDescent, font.Descent * style.FontSize);
            maxLeading = Math.Max(maxLeading, font.Leading * style.FontSize);
        }

        if (fontRuns.Count == 0)
        {
            var primaryFont = _fonts.Resolve(style.FontFamily, style.FontWeight, style.FontSlant);
            maxAscent = Math.Abs(primaryFont.Ascent) * style.FontSize;
            maxDescent = primaryFont.Descent * style.FontSize;
            maxLeading = primaryFont.Leading * style.FontSize;
        }

        (maxAscent, maxLeading) = ApplyLineHeight(maxAscent, maxDescent, maxLeading, style.LineHeight);
        return (maxAscent, maxDescent, maxLeading);
    }

    // ShapeLineText(text, from, to) without the shaping: the same Start, Length, width and
    // metrics, and no runs. Its width is MeasureString of the trimmed text, which is what
    // ShapeLineText adds up, read off the whole text's prefix widths when the line starts at 0,
    // else added up from the font runs the metrics come from.
    private TextLine MeasureLineText(string text, int from, int to, ResolvedInheritedStyle style, PrefixWidths prefixes)
    {
        int end = to;
        while (end > from && char.IsWhiteSpace(text[end - 1])) end--;
        var fontRuns = Kept(_lineRuns);
        ItemizeRange(text, from, end, style, fontRuns);
        var (ascent, descent, leading) = LineMetrics(fontRuns, style);
        float width = from == 0 ? PrefixWidth(prefixes, end, style) : MeasureRuns(text, fontRuns, style);
        fontRuns.Clear();   // the runs' faces are not kept alive by the scratch list
        return new TextLine([], width, ascent, descent, leading) { Start = from, Length = to - from };
    }
}
