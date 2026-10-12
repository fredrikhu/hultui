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
    // Looked up by a span of the text (WordSpanKey, through _wordsInPlace): a segment becomes a
    // string of its own only on a miss, to be the entry's key.
    private readonly LruCache<WordShapeKey, ShapedRun> _wordShapeCache =
        new(capacity: 8192, WordShapeKeyComparer.Instance, maxWeight: WordCacheBytes);
    private readonly LruCache<WordShapeKey, ShapedRun>.InPlace<WordSpanKey> _wordsInPlace;

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

    private static int LayoutBytes(string text, TextLayout layout)
    {
        int bytes = 300 + 4 * text.Length + GlyphBytes(layout);
        var lines = layout.Lines;
        for (int i = 0; i < lines.Count; i++)
            bytes += 120 + 80 * lines[i].Runs.Count;
        return bytes;
    }

    private static int GlyphBytes(TextLayout layout)
    {
        int glyphs = 0;
        var lines = layout.Lines;
        for (int i = 0; i < lines.Count; i++)
        {
            var runs = lines[i].Runs;
            for (int r = 0; r < runs.Count; r++)
                glyphs += runs[r].Glyphs.Length;
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
        _wordsInPlace = _wordShapeCache.LookedUpBy<WordSpanKey>();
    }

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
                Array.Clear(_seen);
            }
            _culture = culture;
        }
    }

    private List<FontRun> Itemize(string text, ResolvedInheritedStyle style) =>
        Itemize(text, style, new List<FontRun>());

    // Into a list the caller owns, cleared first: MeasureString's, which needs the runs only
    // while it adds them up.
    private List<FontRun> Itemize(string text, ResolvedInheritedStyle style, List<FontRun> into)
    {
        if (!_hanContextKnown)
        {
            _hanContext = ScriptLanguage.CjkContextOf(_paragraph);
            _hanContextKnown = true;
        }
        return FontItemizer.Itemize(text, _fonts, style.FontFamily, style.FontWeight, style.FontSlant, _hanContext, into);
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
    private static int MetricDigest(IReadOnlyList<Core.Rendering.TextSpan> spans)
    {
        var hash = new HashCode();
        for (int i = 0; i < spans.Count; i++)
        {
            var s = spans[i];
            if (!s.IsMetricAffecting) continue;
            hash.Add(s.Start); hash.Add(s.Length);
            hash.Add(s.Weight); hash.Add(s.Slant); hash.Add(s.FontSize); hash.Add(s.FontFamily);
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
        {
            layout = ComputeWrapped(text, style, maxWidth, maxLines, shaped);
            if (overflow == TextOverflow.Ellipsis)
                layout = ApplyEllipsis(text, style, layout, maxWidth);
        }
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
    internal TextLine EmptyLine(ResolvedInheritedStyle style, int start) => ShapeLineText("", style, start);

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

        var line = ShapeLineText(text[..best], style, 0);
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
    /// <c>text[from..to]</c> (or of more of the text from <paramref name="from"/> on).</para>
    /// </remarks>
    private int FitPrefix(string text, int from, int to, ResolvedInheritedStyle style, float maxWidth,
        PrefixWidths? prefixes = null)
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

        float Measure(int end) => prefixes is null
            ? MeasureString(text[from..end], style)
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
    /// <para><paramref name="prefixes"/>, when given, is the prefix widths of
    /// <c>text[from..to]</c>, for the first cut only.</para>
    /// </summary>
    private int SplitByCharacter(string text, int from, int to, ResolvedInheritedStyle style,
        float maxWidth, List<TextLine> lines, int? maxLines, PrefixWidths? prefixes = null,
        PrefixWidths? measureOnly = null)
    {
        while (from < to && (!maxLines.HasValue || lines.Count < maxLines.Value))
        {
            int fit = FitPrefix(text, from, to, style, maxWidth, prefixes);
            prefixes = null;
            // A box too narrow for even one character would otherwise never advance. One
            // character per line is the sane floor, and what a browser does.
            if (fit <= from) fit = NextCodePoint(text, from);
            if (fit >= to) break;             // what is left fits a line of its own
            lines.Add(measureOnly is null
                ? ShapeLineText(text[from..fit], style, from)
                : MeasureLineText(text, from, fit, style, measureOnly));
            from = fit;
        }
        return from;
    }

    // A line with no glyphs but the full height of one, taken from the primary font.
    private TextLayout EmptyLineOfHeight(ResolvedInheritedStyle style)
    {
        var line = ShapeLineText("", style, 0);
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
    private TextLayout ComputeWrapped(string text, ResolvedInheritedStyle style, float maxWidth, int? maxLines,
        ShapedText shaped)
        => BreakIntoLines(text, style, maxWidth, maxLines, BreakSegmentsOf(shaped, text));

    // ── Styled layout internals ──
    // A styled line is segments cut at metric-span boundaries, each itemized with its
    // effective weight/slant and shaped at its effective size. Offsets are absolute
    // into the source string throughout, because span ranges are.

    // Both lookups below binary-search the spans, which TextSpan requires sorted by start and
    // apart. They are asked once per piece of every line, and a scan of every span for each
    // made a long paragraph's layout quadratic: 600 spans, 1,300 pieces, a million steps a
    // keystroke in a rich text editor.

    private ResolvedInheritedStyle EffectiveStyleAt(
        ResolvedInheritedStyle style, IReadOnlyList<Core.Rendering.TextSpan> spans, int offset)
    {
        int i = FirstSpanEndingAfter(spans, offset);
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
    private static int NextStyleBoundary(IReadOnlyList<Core.Rendering.TextSpan> spans, int offset, int limit)
    {
        for (int i = FirstSpanEndingAfter(spans, offset); i < spans.Count; i++)
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
            int b = NextStyleBoundary(spans, i, to);
            width += MeasureString(text[i..b], EffectiveStyleAt(style, spans, i));
            i = b;
        }
        return width;
    }

    private TextLine ShapeStyledLine(string text, int from, int to,
        ResolvedInheritedStyle style, IReadOnlyList<Core.Rendering.TextSpan> spans,
        bool appendEllipsis = false)
    {
        int sourceLength = to - from + (appendEllipsis ? 1 : 0);
        // Trailing whitespace is trimmed for width, as in ShapeLineText.
        while (to > from && char.IsWhiteSpace(text[to - 1])) to--;

        var runs = new List<ShapedRun>();
        float width = 0, maxAscent = 0, maxDescent = 0, maxLeading = 0;
        int i = from;
        while (i < to)
        {
            int b = NextStyleBoundary(spans, i, to);
            var eff = EffectiveStyleAt(style, spans, i);
            foreach (var fr in Itemize(text[i..b], eff))
            {
                var shaped = ShapeWithSpacing(fr.Text, fr.Font, eff.FontSize, eff.LetterSpacing);
                shaped.OffsetX = width;
                width += shaped.Width;
                runs.Add(shaped);
                // A larger-size run raises its whole line, the same max-across-runs
                // rule font fallback already applies.
                maxAscent = Math.Max(maxAscent, Math.Abs(fr.Font.Ascent) * eff.FontSize);
                maxDescent = Math.Max(maxDescent, fr.Font.Descent * eff.FontSize);
                maxLeading = Math.Max(maxLeading, fr.Font.Leading * eff.FontSize);
            }
            i = b;
        }

        // The ellipsis is the element's own text, not the span's: a truncation mark
        // rendered bold because the last word happened to be bold reads as content.
        if (appendEllipsis)
            foreach (var fr in Itemize(EllipsisString, style))
            {
                var shaped = ShapeWithSpacing(fr.Text, fr.Font, style.FontSize, style.LetterSpacing);
                shaped.OffsetX = width;
                width += shaped.Width;
                runs.Add(shaped);
                maxAscent = Math.Max(maxAscent, Math.Abs(fr.Font.Ascent) * style.FontSize);
                maxDescent = Math.Max(maxDescent, fr.Font.Descent * style.FontSize);
                maxLeading = Math.Max(maxLeading, fr.Font.Leading * style.FontSize);
            }

        if (runs.Count == 0)
        {
            var primary = _fonts.Resolve(style.FontFamily, style.FontWeight, style.FontSlant);
            maxAscent = Math.Abs(primary.Ascent) * style.FontSize;
            maxDescent = primary.Descent * style.FontSize;
            maxLeading = primary.Leading * style.FontSize;
        }

        (maxAscent, maxLeading) = ApplyLineHeight(maxAscent, maxDescent, maxLeading, style.LineHeight);
        return new TextLine(runs, width, maxAscent, maxDescent, maxLeading) { Start = from, Length = sourceLength };
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

        var lines = new List<TextLine>();
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
        var styledLayout = new TextLayout(lines, new Size(maxLineWidth, totalHeight));
        return overflow == TextOverflow.Ellipsis
            ? ApplyEllipsis(text, styledLayout, maxWidth,
                (start, tailEnd) => BuildStyledEllipsizedLine(text, start, tailEnd, style, spans, maxWidth, memo))
            : styledLayout;
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
    /// </remarks>
    private static List<(int Start, int End, byte Level)> SegmentsIn(byte[] levels, int from, int to)
    {
        var segments = new List<(int, int, byte)>();
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
    /// lines — so the reorder does not depend on a second ICU API surface.
    /// </summary>
    private static int[] VisualOrder(IReadOnlyList<byte> levels)
    {
        int n = levels.Count;
        var order = new int[n];
        for (int i = 0; i < n; i++) order[i] = i;

        byte max = 0, minOdd = byte.MaxValue;
        foreach (var l in levels)
        {
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
                    Array.Reverse(order, i, j - i);
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
    private static int MemoBytes(TextMemo memo, int textLength)
    {
        int bytes = 300 + 2 * textLength + 4 * memo.Breaks.Capacity + 4 * memo.SegmentWidths.Length;
        if (memo.Levels is { } levels) bytes += levels.Length;
        if (memo.Spans is { } spans) bytes += 48 * spans.Length;
        if (memo.WholeLine is { } line) bytes += LineBytes(line);
        return bytes;
    }

    // A remembered line: each run's object, text and 16 bytes a glyph, and the line's entry.
    // Within 5 % of what full collections measure for lines of one run to thirty (a line of
    // Hebrew mixed with Latin is a run a word or two), ellipsized or not.
    private static int LineBytes(TextLine line)
    {
        int bytes = 180;
        var runs = line.Runs;
        for (int i = 0; i < runs.Count; i++)
            bytes += 104 + 2 * runs[i].Text.Length + 16 * runs[i].Glyphs.Length;
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

    // A metric span as layout sees it: what EffectiveStyleAt and NextStyleBoundary read.
    private readonly record struct MetricSpan(int Start, int Length, FontWeight? Weight, FontSlant? Slant, float? FontSize,
        string? FontFamily);

    private sealed class TextMemo
    {
        // What its ranges are keyed by (MemoRange.Owner).
        public readonly int Id;
        // Null on the styled path, which is left-to-right only.
        public readonly byte[]? Levels;
        public readonly bool RtlParagraph;
        public readonly List<int> Breaks;
        // The metric spans this entry was made for, in order; null without spans.
        public readonly MetricSpan[]? Spans;
        // The width of segment s (Breaks[s - 1]..Breaks[s]); NaN until a pass reaches it.
        public readonly float[] SegmentWidths;
        // The line of the whole text: the one line of every width it fits.
        public TextLine? WholeLine;

        public TextMemo(int id, byte[]? levels, bool rtlParagraph, List<int> breaks, MetricSpan[]? spans)
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

    private static MetricSpan[]? MetricSpansOf(IReadOnlyList<Core.Rendering.TextSpan>? spans)
    {
        if (spans is null) return null;
        var kept = new List<MetricSpan>();
        for (int i = 0; i < spans.Count; i++)
            if (spans[i].IsMetricAffecting)
                kept.Add(new MetricSpan(spans[i].Start, spans[i].Length, spans[i].Weight, spans[i].Slant, spans[i].FontSize,
                    spans[i].FontFamily));
        return kept.Count == 0 ? null : kept.ToArray();
    }

    private static bool SameMetricSpans(MetricSpan[]? kept, IReadOnlyList<Core.Rendering.TextSpan>? spans)
    {
        int k = 0;
        if (spans is not null)
            for (int i = 0; i < spans.Count; i++)
            {
                var span = spans[i];
                if (!span.IsMetricAffecting) continue;
                if (kept is null || k >= kept.Length
                    || kept[k] != new MetricSpan(span.Start, span.Length, span.Weight, span.Slant, span.FontSize,
                        span.FontFamily))
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
    private static TextLine CopyOf(TextLine line) =>
        new(line.Runs, line.Width, line.Ascent, line.Descent, line.Leading) { Start = line.Start, Length = line.Length };

    // A remembered line, copied for this layout, or null. No delegates here: a lambda over
    // these parameters would allocate its closure on every call, hits included.
    private TextLine? RememberedLine(TextMemo memo, string text, int from, int to, bool appendEllipsis)
    {
        if (from == 0 && to == text.Length && !appendEllipsis)
            return memo.WholeLine is { } whole ? CopyOf(whole) : null;
        return _memoLines.TryGet(new MemoRange(memo.Id, from, to, appendEllipsis), out var line) ? CopyOf(line) : null;
    }

    // Remembers a line RememberedLine did not have, and hands it to the layout that built it.
    private TextLine Remember(TextMemo memo, string text, int from, int to, bool appendEllipsis, TextLine line)
    {
        if (from == 0 && to == text.Length && !appendEllipsis)
        {
            // The entry keeps more now: it counts for it.
            memo.WholeLine = line;
            if (ReferenceEquals(memo, _laidOutMemo)) _memos.Reweigh(_laidOutKey, MemoBytes(memo, text.Length));
        }
        else _memoLines.Add(new MemoRange(memo.Id, from, to, appendEllipsis), line, LineBytes(line));
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
        if (memo is null) return ShapeStyledLine(text, from, to, style, spans, appendEllipsis);
        return RememberedLine(memo, text, from, to, appendEllipsis)
            ?? Remember(memo, text, from, to, appendEllipsis, ShapeStyledLine(text, from, to, style, spans, appendEllipsis));
    }

    private float MeasureBidi(string text, int from, int to, ResolvedInheritedStyle style, byte[] levels,
        IReadOnlyList<Core.Rendering.TextSpan>? spans = null)
    {
        if (to <= from) return 0;
        float width = 0;
        foreach (var (s, e, level) in SegmentsIn(levels, from, to))
        {
            // One piece per style run of the direction run; with no spans, the whole run.
            for (int i = s, b; i < e; i = b)
            {
                b = spans is null ? e : NextStyleBoundary(spans, i, e);
                var eff = spans is null ? style : EffectiveStyleAt(style, spans, i);
                foreach (var fr in Itemize(text[i..b], eff))
                    width += MeasureRun(fr.Text, fr.Font, eff.FontSize, eff.LetterSpacing,
                        rtl: (level & 1) == 1);
            }
        }
        return width;
    }

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

        var segments = SegmentsIn(paraLevels, from, to);
        var segRuns = new List<ShapedRun>[segments.Count];
        var levels = new byte[segments.Count];
        float maxAscent = 0, maxDescent = 0, maxLeading = 0;

        for (int s = 0; s < segments.Count; s++)
        {
            var (segStart, segEnd, level) = segments[s];
            levels[s] = level;
            var runs = new List<ShapedRun>();
            // One piece per style run of the segment; with no spans, the whole segment. A
            // larger-size piece raises the line, the max-across-runs rule fallback follows.
            for (int i = segStart, b; i < segEnd; i = b)
            {
                b = spans is null ? segEnd : NextStyleBoundary(spans, i, segEnd);
                var eff = spans is null ? style : EffectiveStyleAt(style, spans, i);
                foreach (var fr in Itemize(text[i..b], eff))
                {
                    var shaped = ShapeWithSpacing(fr.Text, fr.Font, eff.FontSize, eff.LetterSpacing,
                        rtl: (level & 1) == 1);
                    shaped.Level = level;
                    runs.Add(shaped);
                    maxAscent = Math.Max(maxAscent, Math.Abs(fr.Font.Ascent) * eff.FontSize);
                    maxDescent = Math.Max(maxDescent, fr.Font.Descent * eff.FontSize);
                    maxLeading = Math.Max(maxLeading, fr.Font.Leading * eff.FontSize);
                }
            }
            segRuns[s] = runs;
        }

        // Place in visual order; within an RTL segment the fallback sub-runs flip too.
        float x = 0;
        foreach (int logical in VisualOrder(levels))
        {
            var runs = segRuns[logical];
            bool rtl = (levels[logical] & 1) == 1;
            for (int r = 0; r < runs.Count; r++)
            {
                var run = runs[rtl ? runs.Count - 1 - r : r];
                run.OffsetX = x;
                x += run.Width;
            }
        }

        var logicalRuns = new List<ShapedRun>();
        foreach (var runs in segRuns) logicalRuns.AddRange(runs);

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
        var allBreaks = bidi?.Breaks ?? BreakOffsets(text);

        var lines = new List<TextLine>();
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
        var bidiLayout = new TextLayout(lines, new Size(maxLineWidth, totalHeight), rtlParagraph);
        return overflow == TextOverflow.Ellipsis
            ? ApplyEllipsis(text, bidiLayout, maxWidth,
                (start, tailEnd) => BuildBidiEllipsizedLine(text, start, tailEnd, style, levels, rtlParagraph, maxWidth, bidi, spans))
            : bidiLayout;
    }

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

    // MeasureString of segment s (offsets[s - 1]..offsets[s]), from the cache when there is one.
    private float SegmentWidth(string text, List<int> offsets, BreakSegments? cached, int s, ResolvedInheritedStyle style)
    {
        if (cached is null) return MeasureString(text[offsets[s - 1]..offsets[s]], style);
        float width = cached.Widths[s];
        if (float.IsNaN(width))
            cached.Widths[s] = width = MeasureString(text[offsets[s - 1]..offsets[s]], style);
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
                : MeasureString(text[from..to], style));
        }

        if (!styled) _minContentCache.Put(key, widest);
        return widest;
    }

    private const string EllipsisString = "…"; // …

    // Rewrites the last line of an already-broken (clipped) layout with a
    // trailing ellipsis when content was truncated. Truncation happens either
    // because a maxLines limit dropped trailing text, or because a single
    // unbreakable token is wider than the available width. Only the final line
    // is touched; earlier lines keep their exact shaping and positions.
    private TextLayout ApplyEllipsis(string text, ResolvedInheritedStyle style, TextLayout clip, float maxWidth)
        => ApplyEllipsis(text, clip, maxWidth, (start, tailEnd) =>
        {
            string tail = text[start..tailEnd];
            return BuildEllipsizedLine(tail, start, style, maxWidth,
                ReuseAcrossWidths ? BuildPrefixWidths(tail, style) : null, MeasureString(EllipsisString, style));
        });

    /// <summary>
    /// One truncation policy for all three layout paths; only the line builder
    /// differs (plain, styled, direction-aware). <paramref name="buildEllipsized"/>
    /// is handed the source range the final line may draw from.
    /// </summary>
    private TextLayout ApplyEllipsis(string text, TextLayout clip, float maxWidth,
        Func<int, int, TextLine> buildEllipsized)
    {
        if (clip.Lines.Count == 0)
            return clip;

        var last = clip.Lines[^1];
        int coveredEnd = last.Start + last.Length;
        bool droppedTail = coveredEnd < text.Length;
        // A greedy line never exceeds maxWidth unless it is a lone token that
        // could not be broken — that still needs to be clipped with an ellipsis.
        bool lineOverflows = last.Width > maxWidth + 0.5f;
        if (!droppedTail && !lineOverflows)
            return clip;

        // The truncated line's source is everything from where the last line
        // begins up to the next explicit newline (or the end of the string).
        int nl = text.IndexOf('\n', last.Start);
        int tailEnd = nl < 0 ? text.Length : nl;

        var ellipsized = buildEllipsized(last.Start, tailEnd);
        ellipsized.OffsetY = last.OffsetY;

        var lines = new List<TextLine>(clip.Lines.Count);
        for (int i = 0; i < clip.Lines.Count - 1; i++)
            lines.Add(clip.Lines[i]);
        lines.Add(ellipsized);

        float maxLineWidth = 0;
        foreach (var line in lines)
            maxLineWidth = Math.Max(maxLineWidth, line.Width);

        // The replacement line uses the same font metrics, so the overall height
        // is unchanged; only the max width can shrink.
        return new TextLayout(lines, new Size(maxLineWidth, clip.Size.Height), clip.IsRtlParagraph);
    }

    // Builds a single line holding the longest prefix of <paramref name="tail"/>
    // that leaves room for the ellipsis within <paramref name="maxWidth"/>,
    // followed by the ellipsis glyph. <paramref name="prefixes"/>, when the tail
    // has them, measures each candidate prefix without itemizing or shaping it
    // again; <paramref name="ellipsisWidth"/> is MeasureString of the mark.
    private TextLine BuildEllipsizedLine(string tail, int start, ResolvedInheritedStyle style, float maxWidth,
        PrefixWidths? prefixes, float ellipsisWidth)
    {
        // Binary search for the widest prefix that fits alongside the ellipsis.
        // Width is monotonic in prefix length, so this converges in O(log n)
        // shaping calls even for a pathologically long unbreakable token.
        int lo = 0, hi = tail.Length, best = 0;
        while (lo <= hi)
        {
            int mid = (lo + hi) >> 1;
            float width = prefixes is null ? MeasureString(tail[..mid], style) : PrefixWidth(prefixes, mid, style);
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

        string content = tail[..best] + EllipsisString;
        return ShapeLineText(content, style, start);
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
        var fontRuns = Itemize(text, style, _prefixRuns);
        var runs = new PrefixRun[fontRuns.Count];
        var segments = _prefixSegments;
        segments.Clear();
        float before = 0;
        int runStart = 0;
        for (int r = 0; r < fontRuns.Count; r++)
        {
            var fr = fontRuns[r];
            int runEnd = runStart + fr.Text.Length;
            runs[r] = new PrefixRun(runStart, runEnd, fr.Font, before, segments.Count);
            float x = 0;
            int glyphs = 0;
            int fontHash = WordShapeKeyComparer.FontHash(fr.Font, style.FontSize);
            for (int i = runStart; i < runEnd;)
            {
                bool isSpace = text[i] == ' ';
                int start = i;
                while (i < runEnd && (text[i] == ' ') == isSpace) i++;
                var seg = ShapeSegment(text, start, i - start, fr.Font, style.FontSize, fontHash);
                segments.Add(new PrefixSegment(start, x, seg.Width, glyphs, seg.Glyphs));
                x += seg.Width;
                glyphs += seg.Glyphs;
            }
            // The run's width as ShapeWithSpacing gives it, added as MeasureString adds it.
            before += style.LetterSpacing == 0f || glyphs == 0 ? x : x + style.LetterSpacing * glyphs;
            runStart = runEnd;
        }
        fontRuns.Clear();   // the runs' text and faces are not kept alive by the scratch list
        var table = new PrefixWidths(text, style.FontSize, style.LetterSpacing, runs, segments.ToArray());
        // A whole document's single line can have a hundred thousand segments: its list is not kept.
        if (segments.Capacity > KeptPrefixSegments)
        {
            segments.Clear();
            segments.TrimExcess();
        }
        return table;
    }

    // BuildPrefixWidths' font runs and segments while it builds a table, which gets exact arrays
    // copied out of them: lists grown anew for every table were most of what wrapping and
    // truncating allocated (a list doubles its way up: a line of 60 segments went through five
    // arrays before its own was copied out). Nothing BuildPrefixWidths calls builds a table, and a
    // pipeline is used from one thread, as MeasureString's run list is.
    private readonly List<FontRun> _prefixRuns = new();
    private readonly List<PrefixSegment> _prefixSegments = new();

    // The most segments the kept list holds room for between tables (20 bytes each): a line of
    // ~12,000 characters of prose.
    internal const int KeptPrefixSegments = 4096;

    // The room the kept segment list holds, for the test that holds it to KeptPrefixSegments.
    internal int PrefixScratchCapacity => _prefixSegments.Capacity;

    /// <summary>
    /// <c>MeasureString(p.Text[..length])</c>, to the bit: the same run and segment widths,
    /// added in the same order, letter spacing applied the same way. Complete runs and
    /// segments are read off the table; only a word the prefix cuts in two is looked up
    /// (through the word cache, as MeasureString would) — never its share of the whole word's
    /// glyph advances, which carry the kerning and ligatures of the letters cut off.
    /// </summary>
    private float PrefixWidth(PrefixWidths p, int length, ResolvedInheritedStyle style)
    {
        string text = p.Text;
        if (length == 0) return 0;
        // A cut inside a surrogate pair leaves a lone surrogate, which itemizes on its own
        // terms: that prefix is measured the long way. (Not reached today — a surrogate is
        // above U+0590, so its text takes the BiDi path — but this keeps the table honest.)
        if (length < text.Length && char.IsLowSurrogate(text[length]) && char.IsHighSurrogate(text[length - 1]))
            return MeasureString(text[..length], style);

        // The run the cut falls in: the last starting before it.
        var runs = p.Runs;
        int lo = 0, hi = runs.Length - 1;
        while (lo < hi)
        {
            int mid = (lo + hi + 1) >> 1;
            if (runs[mid].Start < length) lo = mid;
            else hi = mid - 1;
        }
        var run = runs[lo];

        // Its last segment starting before the cut.
        var segments = p.Segments;
        int runSegmentsEnd = lo + 1 < runs.Length ? runs[lo + 1].FirstSegment : segments.Length;
        lo = run.FirstSegment;
        hi = runSegmentsEnd - 1;
        while (lo < hi)
        {
            int mid = (lo + hi + 1) >> 1;
            if (segments[mid].Start < length) lo = mid;
            else hi = mid - 1;
        }
        var seg = segments[lo];
        int end = lo + 1 < runSegmentsEnd ? segments[lo + 1].Start : run.End;

        float width;
        int glyphs;
        if (length == end)
        {
            width = seg.Width;
            glyphs = seg.Glyphs;
        }
        else
        {
            var part = ShapeSegment(text, seg.Start, length - seg.Start, run.Font, p.FontSize,
                WordShapeKeyComparer.FontHash(run.Font, p.FontSize));
            width = part.Width;
            glyphs = part.Glyphs;
        }

        // ShapeByWords' running sum, then ShapeWithSpacing's letter spacing, then
        // MeasureString's sum over the runs.
        float x = seg.X + width;
        int total = seg.GlyphsBefore + glyphs;
        return run.Before + (p.LetterSpacing == 0f || total == 0 ? x : x + p.LetterSpacing * total);
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
    /// What one-line Ellipsis needs of a string at any width, worked out once.
    /// <see cref="Exact"/> says the shortcut gives the reference path's answer for it.
    /// </summary>
    private sealed record OneLineEllipsis(bool Exact, PrefixWidths? Prefixes, float EllipsisWidth, float Height);

    /// <summary>
    /// One-line Ellipsis without breaking the text into lines first: exactly what
    /// <see cref="ComputeWrapped"/> + <see cref="ApplyEllipsis(string, ResolvedInheritedStyle, TextLayout, float)"/>
    /// return, or null for a string where that is not proven (the caller then takes them).
    /// Only reached for a text with no newline that is wider than <paramref name="maxWidth"/>.
    /// </summary>
    private TextLayout? EllipsizeOneLine(string text, ResolvedInheritedStyle style, ShapedText shaped, float maxWidth)
    {
        if (!ReuseAcrossWidths) return null;
        var e = shaped.OneLineEllipsis ??= ProveOneLineEllipsis(text, style, shaped);
        if (!e.Exact) return null;

        // ApplyEllipsis on the wrap pass's one line, which starts at 0: the line rebuilt
        // with an ellipsis, in a layout keeping the wrapped line's height.
        var line = BuildEllipsizedLine(text, 0, style, maxWidth, e.Prefixes, e.EllipsisWidth);
        return new TextLayout([line], new Size(Math.Max(0f, line.Width), e.Height));
    }

    // With maxLines 1 the wrap pass keeps one line L, and ApplyEllipsis replaces it with
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
    /// then ApplyEllipsis as it is. ApplyEllipsis reads only that line's extent, width and
    /// height, all exact, and replaces it; where it would keep it instead (the line is the
    /// whole text and fits) the measured layout is no layout, and the caller takes the
    /// reference path. Null then, and on the reference path.
    /// </summary>
    private TextLayout? EllipsizeMeasuredLine(string text, ResolvedInheritedStyle style, ShapedText shaped, float maxWidth)
    {
        if (!ReuseAcrossWidths) return null;
        var prefixes = PrefixWidthsOf(shaped, text, style);
        float ellipsisWidth = (shaped.OneLineEllipsis ??= ProveOneLineEllipsis(text, style, shaped)).EllipsisWidth;
        var measured = BreakIntoLines(text, style, maxWidth, 1, BreakSegmentsOf(shaped, text), prefixes);
        var layout = ApplyEllipsis(text, measured, maxWidth, (start, tailEnd) =>
        {
            string tail = text[start..tailEnd];
            return BuildEllipsizedLine(tail, start, style, maxWidth,
                start == 0 && tailEnd == text.Length ? prefixes : BuildPrefixWidths(tail, style), ellipsisWidth);
        });
        return ReferenceEquals(layout, measured) ? null : layout;
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

    // Total advance width of a string, itemized and shaped the same way as a line. Internal
    // for the tests that hold it to the glyph runs it adds up.
    internal float MeasureString(string s, ResolvedInheritedStyle style)
    {
        if (s.Length == 0)
            return 0;

        var runs = Itemize(s, style, _measuredRuns);
        float width = 0;
        foreach (var run in runs)
            width += MeasureRun(run.Text, run.Font, style.FontSize, style.LetterSpacing);
        runs.Clear();
        return width;
    }

    // MeasureString's font runs, while it adds them up (nothing it calls measures a string).
    private readonly List<FontRun> _measuredRuns = new();

    // ShapeWithSpacing(…).Width without building the glyph array it would hand back: the same
    // segment widths added in the same order, letter spacing applied the same way.
    private float MeasureRun(string text, IResolvedFont font, float fontSize, float letterSpacing, bool rtl = false)
    {
        float width;
        int glyphs;
        if (rtl)
        {
            var run = _shaper.Shape(text, font, fontSize, rtl: true);
            width = run.Width;
            glyphs = run.Glyphs.Length;
        }
        else if (text.IndexOf(' ') < 0)
        {
            var word = ShapeSegment(text, 0, text.Length, font, fontSize, WordShapeKeyComparer.FontHash(font, fontSize));
            width = word.Width;
            glyphs = word.Glyphs;
        }
        else
        {
            width = 0;
            glyphs = 0;
            int fontHash = WordShapeKeyComparer.FontHash(font, fontSize);
            for (int i = 0; i < text.Length;)
            {
                bool isSpace = text[i] == ' ';
                int start = i;
                while (i < text.Length && (text[i] == ' ') == isSpace) i++;
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
        var run = rtl
            ? _shaper.Shape(text, font, fontSize, rtl: true)
            : ShapeByWords(text, font, fontSize);
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
    internal ShapedRun ShapeByWords(string text, IResolvedFont font, float fontSize)
    {
        int fontHash = WordShapeKeyComparer.FontHash(font, fontSize);
        if (text.IndexOf(' ') < 0)
        {
            var word = ShapeSegment(text, 0, text.Length, font, fontSize, fontHash);
            // Fresh ShapedRun per call site: OffsetX is mutated by layout, so the
            // cached instance must never be handed out. The glyph array is
            // read-only in practice and safe to share.
            var wordGlyphs = word.Run?.Glyphs;
            if (wordGlyphs is null)
                ComposeDigits(text, 0, text.Length, word.Digits!, 0f, wordGlyphs = new GlyphPosition[text.Length], 0);
            return new ShapedRun(text, font, fontSize, word.Width, wordGlyphs);
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
        return new ShapedRun(text, font, fontSize, x, glyphs);
    }

    // ShapeByWords' segments, between its two passes (one list per pipeline, as a pipeline is
    // used from one thread; nothing ShapeByWords calls comes back to it).
    private readonly List<WordShape> _words = new();

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
    // WordShapeKeyComparer.FontHash(font, fontSize), which the caller works out once per run.
    private WordShape ShapeSegment(string text, int start, int length, IResolvedFont font, float fontSize,
        int fontHash)
    {
        var segment = text.AsSpan(start, length);
        if (DigitTableFor(segment, font, fontSize) is { } digits)
        {
            float x = 0;
            foreach (char c in segment)
                x += digits.Advances[c - '0'];
            return new WordShape(null, digits, start, x, length);
        }

        if (!_wordsInPlace.TryGet(new WordSpanKey(segment, font, fontSize, fontHash), out var run))
        {
            string word = text.Substring(start, length);
            run = _shaper.Shape(word, font, fontSize);
            // Put, not Add: next to the shaping a second lookup is nothing, and a key the
            // lookup in place had missed would make Add throw.
            _wordShapeCache.Put(new WordShapeKey(word, font, fontSize), run, WordBytes(word, run));
        }
        return new WordShape(run, null, start, run.Width, run.Glyphs.Length);
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
        GlyphPosition[] glyphs, int at)
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

    // measureOnly, the whole text's prefix widths, makes every line a measured one
    // (MeasureLineText) for EllipsizeMeasuredLine: the same pass, nothing shaped.
    private TextLayout BreakIntoLines(string text, ResolvedInheritedStyle style, float maxWidth, int? maxLines,
        BreakSegments? segments, PrefixWidths? measureOnly = null)
    {
        var lines = new List<TextLine>();
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
                lineWidth = MeasureString(text[textOffset..segEnd], style);
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
            var lastLine = segments is not null && lineWidth <= maxWidth
                ? BuildPrefixWidths(text[textOffset..], style)
                : null;
            textOffset = SplitByCharacter(text, textOffset, text.Length, style, maxWidth, lines, maxLines, lastLine,
                measureOnly);
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

        return new TextLayout(lines, new Size(maxLineWidth, totalHeight));

        TextLine NewLine(int from, int to) => measureOnly is null
            ? ShapeLineText(text[from..to], style, from)
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

    private TextLine ShapeLineText(string lineText, ResolvedInheritedStyle style, int start)
    {
        // Trim trailing whitespace for width calculation but keep for shaping
        var trimmed = lineText.TrimEnd();
        var fontRuns = Itemize(trimmed, style);

        var runs = new List<ShapedRun>();
        float width = 0;

        foreach (var fr in fontRuns)
        {
            var shaped = ShapeWithSpacing(fr.Text, fr.Font, style.FontSize, style.LetterSpacing);
            shaped.OffsetX = width;
            width += shaped.Width;
            runs.Add(shaped);
        }

        var (ascent, descent, leading) = LineMetrics(fontRuns, style);
        return new TextLine(runs, width, ascent, descent, leading) { Start = start, Length = lineText.Length };
    }

    // A line's vertical metrics from its font runs: the largest of each over the runs at the
    // style's size (font metrics are at reference size 1), the primary font's for a line with
    // none, then the line height. ShapeLineText and MeasureLineText both take them from here.
    private (float Ascent, float Descent, float Leading) LineMetrics(List<FontRun> fontRuns, ResolvedInheritedStyle style)
    {
        float maxAscent = 0, maxDescent = 0, maxLeading = 0;
        foreach (var fr in fontRuns)
        {
            maxAscent = Math.Max(maxAscent, Math.Abs(fr.Font.Ascent) * style.FontSize);
            maxDescent = Math.Max(maxDescent, fr.Font.Descent * style.FontSize);
            maxLeading = Math.Max(maxLeading, fr.Font.Leading * style.FontSize);
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

    // ShapeLineText(text[from..to]) without the shaping: the same Start, Length, width and
    // metrics, and no runs. Its width is MeasureString of the trimmed text, which is what
    // ShapeLineText adds up, read off the whole text's prefix widths when the line starts at 0.
    private TextLine MeasureLineText(string text, int from, int to, ResolvedInheritedStyle style, PrefixWidths prefixes)
    {
        string lineText = text[from..to];
        string trimmed = lineText.TrimEnd();
        var (ascent, descent, leading) = LineMetrics(Itemize(trimmed, style), style);
        float width = from == 0 ? PrefixWidth(prefixes, trimmed.Length, style) : MeasureString(trimmed, style);
        return new TextLine([], width, ascent, descent, leading) { Start = from, Length = lineText.Length };
    }
}
