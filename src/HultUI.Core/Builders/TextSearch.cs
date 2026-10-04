using System.Text.RegularExpressions;
using HultUI.Core.Primitives;
using HultUI.Core.Rendering;

namespace HultUI.Core.Builders;

/// <summary>
/// Find-in-text for a view that draws lines: the query and its options, where every match is,
/// which one the user is on, and the spans that tint them.
///
/// <para>Extracted because four views wanted the same thing and would otherwise have grown
/// four slightly different answers to the same awkward questions — what a regex that matches
/// nothing at every position should do, how to tint a match without discarding the syntax
/// colour under it, and what to show when the pattern itself is wrong.</para>
///
/// <para><b>Lanes.</b> A comparison view has two columns of text sharing one set of line
/// numbers, so a match is identified by line <i>and</i> lane. A view with a single column
/// passes one lane and ignores it. Hits are ordered by line first and lane second, which is
/// reading order in both layouts.</para>
///
/// <para>The search re-runs itself whenever the query or an option changes; the owner calls
/// <see cref="Bind"/> again when the <i>content</i> changes. Every mutator ends in the
/// <c>onChanged</c> callback given to the constructor, which is where the owner repaints.</para>
/// </summary>
public sealed class TextSearch
{
    /// <summary>One match: which line and lane it is on, and the run it covers within that
    /// line. <see cref="Length"/> can be zero — see <see cref="Run"/>.</summary>
    public readonly record struct Hit(int Line, int Lane, int Start, int Length);

    private readonly Action _onChanged;
    private readonly List<Hit> _hits = [];
    // The same hits keyed by (lane, line), because a row template asks the question the other
    // way round — "what do I tint in this line" — once per realized row per frame.
    private readonly Dictionary<(int Lane, int Line), List<Hit>> _byLine = [];

    private int _lineCount;
    private int _lanes = 1;
    private Func<int, int, string?>? _textAt;

    /// <param name="onChanged">Runs whenever the results or the current match move — normally
    /// the owner's <c>MarkDirty</c>.</param>
    public TextSearch(Action onChanged) => _onChanged = onChanged;

    /// <summary>
    /// How many matches are kept. A one-letter query — or <c>.</c> as a pattern — matches
    /// hundreds of thousands of times in a large file, and nothing downstream gains from the
    /// tail of that. <see cref="Capped"/> says when the count was cut, so a total that is not
    /// the whole truth never reads as one.
    /// </summary>
    public const int MaxHits = 20_000;

    /// <summary>
    /// How long a regex may spend on a single line. A pattern like <c>(a+)+$</c> backtracks
    /// for geometric time on the wrong input, and this search runs on the UI thread — without
    /// a bound, one typed character could hang the window with no way back. A timeout surfaces
    /// as <see cref="Error"/>, the same as a pattern that will not compile.
    /// </summary>
    private static readonly TimeSpan RegexBudget = TimeSpan.FromMilliseconds(250);

    public string Query { get; private set; } = "";
    public bool MatchCase { get; private set; }
    public bool UseRegex { get; private set; }

    /// <summary>Why the current query found nothing usable — an unparseable pattern, or one
    /// that ran out of time. Null when the query is fine (including when it simply has no
    /// matches).</summary>
    public string? Error { get; private set; }

    /// <summary>True when the scan stopped at <see cref="MaxHits"/>.</summary>
    public bool Capped { get; private set; }

    public IReadOnlyList<Hit> Hits => _hits;
    public bool HasHits => _hits.Count > 0;

    /// <summary>Position of the current match in <see cref="Hits"/>, or -1 when there are
    /// none.</summary>
    public int Index { get; private set; } = -1;

    public Hit? Current => Index >= 0 && Index < _hits.Count ? _hits[Index] : null;

    /// <summary>
    /// Points the search at the text to search and runs it. Call again whenever the content
    /// changes — the search cannot tell that the lines behind the delegate have moved.
    /// </summary>
    /// <param name="lineCount">How many lines there are to search.</param>
    /// <param name="lanes">How many lanes each line has — 1 for ordinary text, 2 for a
    /// side-by-side diff whose halves are searched as one document.</param>
    /// <param name="textAt">Line and lane to the text there, or null where that lane has no
    /// line (the blank half opposite an insertion, say). Null lines are skipped, so they
    /// contribute no matches.</param>
    public void Bind(int lineCount, int lanes, Func<int, int, string?> textAt)
    {
        _lineCount = Math.Max(0, lineCount);
        _lanes = Math.Max(1, lanes);
        _textAt = textAt;
        Rerun();
    }

    /// <summary>Forgets the content, dropping every result. The query and its options are
    /// kept, so the next thing bound is searched for the same thing.</summary>
    public void Unbind()
    {
        _textAt = null;
        _lineCount = 0;
        ClearResults();
        _onChanged();
    }

    public void SetQuery(string query)
    {
        if (Query == query) return;
        Query = query ?? "";
        Rerun();
    }

    public void SetMatchCase(bool on)
    {
        if (MatchCase == on) return;
        MatchCase = on;
        Rerun();
    }

    public void SetRegex(bool on)
    {
        if (UseRegex == on) return;
        UseRegex = on;
        Rerun();
    }

    public void ToggleMatchCase() => SetMatchCase(!MatchCase);
    public void ToggleRegex() => SetRegex(!UseRegex);

    /// <summary>
    /// Moves to the next match in <paramref name="dir"/>, wrapping at either end. A search
    /// that stops dead at the last match reads as broken when the one you wanted is above
    /// where you started.
    /// </summary>
    public void Move(int dir)
    {
        if (_hits.Count == 0) return;
        Index = (Index + dir + _hits.Count) % _hits.Count;
        _onChanged();
    }

    /// <summary>
    /// Makes the first match at or after a position the current one, wrapping to the first
    /// match in the document when nothing lies beyond it.
    ///
    /// <para>For a query that came from a selection: the match the person is looking at is
    /// the selection itself, and a search that then scrolled away to the first occurrence at
    /// the top of the file would lose them the place they were reading. Positions compare in
    /// hit order — line, then lane, then start.</para>
    /// </summary>
    public void SeekTo(int line, int lane, int start)
    {
        if (_hits.Count == 0) return;
        int at = 0;
        for (int i = 0; i < _hits.Count; i++)
        {
            var h = _hits[i];
            if (h.Line > line || (h.Line == line && (h.Lane > lane || (h.Lane == lane && h.Start >= start))))
            {
                at = i;
                break;
            }
        }
        if (at == Index) return;
        Index = at;
        _onChanged();
    }

    /// <summary>
    /// What a find bar opens with when text is selected: the selection's first line, or null
    /// when there is nothing usable in it.
    ///
    /// <para>A query is one line — the field is single-line, and a match never spans a line
    /// break — so a selection dragged across several lines contributes its first. Whitespace
    /// within the line is kept, because it is part of what was selected and matches the same
    /// text again; a selection that is nothing but whitespace seeds nothing, and the bar keeps
    /// whatever query it had.</para>
    /// </summary>
    public static string? QueryFromSelection(string? selected)
    {
        if (string.IsNullOrEmpty(selected)) return null;
        int cut = selected.IndexOfAny(['\n', '\r']);
        var line = cut < 0 ? selected : selected[..cut];
        return string.IsNullOrWhiteSpace(line) ? null : line;
    }

    /// <summary>The matches on a line of a lane, in order, or null when there are none — the
    /// question a row template asks while it is being built.</summary>
    public IReadOnlyList<Hit>? HitsOn(int line, int lane = 0) =>
        _byLine.TryGetValue((lane, line), out var hits) ? hits : null;

    /// <summary>
    /// Folds match tinting into a line's syntax colouring, producing the one sorted,
    /// non-overlapping list <c>TextElement.Spans</c> accepts.
    ///
    /// <para>A match takes a <i>background</i> and never a foreground, so a matched keyword
    /// keeps the colour its grammar gave it instead of being repainted flat. Where the two
    /// disagree about boundaries the text is cut at every edge of either, so between two
    /// consecutive cuts both the colour and the tint are constant.</para>
    ///
    /// <para>Returns <paramref name="syntax"/> unchanged when this line has no matches, which
    /// is almost every line — no allocation on the common path.</para>
    /// </summary>
    public IReadOnlyList<TextSpan>? Decorate(
        IReadOnlyList<TextSpan>? syntax, int line, int lane, int length,
        Color hitBackground, Color currentBackground)
    {
        if (length <= 0) return syntax;
        if (_byLine.TryGetValue((lane, line), out var hits) is false) return syntax;

        var current = Current;
        bool hasSyntax = syntax is { Count: > 0 };

        var cuts = new SortedSet<int> { 0, length };
        foreach (var h in hits)
        {
            if (h.Length <= 0) continue;              // nothing to tint — see Run
            cuts.Add(Math.Clamp(h.Start, 0, length));
            cuts.Add(Math.Clamp(h.Start + h.Length, 0, length));
        }
        if (hasSyntax)
            foreach (var s in syntax!)
            {
                cuts.Add(Math.Clamp(s.Start, 0, length));
                cuts.Add(Math.Clamp(s.Start + s.Length, 0, length));
            }

        var merged = new List<TextSpan>();
        int? prev = null;
        foreach (int cut in cuts)
        {
            if (prev is int from && cut > from)
            {
                var fg = hasSyntax ? ColorAt(syntax!, from) : default;
                var bg = TintAt(hits, from, current, hitBackground, currentBackground);
                if (fg != default || bg != default)
                    merged.Add(new TextSpan(from, cut - from, fg, bg));
            }
            prev = cut;
        }
        return merged.Count > 0 ? merged : syntax;
    }

    private static Color TintAt(List<Hit> hits, int offset, Hit? current, Color hit, Color currentHit)
    {
        foreach (var h in hits)
        {
            if (h.Length <= 0) continue;
            if (offset < h.Start) break;               // ascending; past it
            if (offset < h.Start + h.Length)
                return current is { } c && c.Line == h.Line && c.Lane == h.Lane && c.Start == h.Start
                    ? currentHit : hit;
        }
        return default;
    }

    /// <summary>The syntax colour covering an offset, or the default — which means "leave the
    /// element's own colour alone", since a tokenizer emits no span for text already in the
    /// foreground colour.</summary>
    private static Color ColorAt(IReadOnlyList<TextSpan> spans, int offset)
    {
        foreach (var s in spans)
        {
            if (offset < s.Start) break;               // sorted; past it
            if (offset < s.Start + s.Length) return s.Color;
        }
        return default;
    }

    private void Rerun()
    {
        Run();
        _onChanged();
    }

    private void ClearResults()
    {
        _hits.Clear();
        _byLine.Clear();
        Index = -1;
        Capped = false;
        Error = null;
    }

    /// <summary>
    /// Scans the bound content for the current query.
    ///
    /// <para>A regex match can be <b>zero length</b> — <c>^</c>, <c>$</c> and <c>^\s*$</c> all
    /// are, and the last of those is how anyone finds blank lines. Those are kept as hits, so
    /// they can be navigated to, but tint nothing, because there is nothing under them to
    /// tint. The scan then advances a character past them, or a pattern that matches the empty
    /// string would match forever at the same position.</para>
    /// </summary>
    private void Run()
    {
        ClearResults();
        if (Query.Length == 0 || _textAt is null || _lineCount == 0) return;

        Regex? regex = null;
        if (UseRegex)
        {
            try
            {
                var opts = RegexOptions.CultureInvariant | (MatchCase ? RegexOptions.None : RegexOptions.IgnoreCase);
                regex = new Regex(Query, opts, RegexBudget);
            }
            catch (ArgumentException)
            {
                Error = "bad pattern";
                return;
            }
        }
        var cmp = MatchCase ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;

        try
        {
            ScanAll(regex, cmp);
        }
        catch (RegexMatchTimeoutException)
        {
            ClearResults();
            Error = "pattern too slow";
            return;
        }

        // Also after a scan that stopped at the cap: what it found is still the answer, from its
        // first match. It used to return before this, leaving no current match — the count read
        // "0 of 20,000+" and nothing was tinted or scrolled to until the first Enter.
        if (_hits.Count > 0) Index = 0;
    }

    private void ScanAll(Regex? regex, StringComparison cmp)
    {
        for (int line = 0; line < _lineCount; line++)
            for (int lane = 0; lane < _lanes; lane++)
            {
                // A null line is a lane with nothing on it — the blank half opposite an
                // insertion. An empty one is a real, searchable line: "^$" finds it.
                if (_textAt!(line, lane) is not { } text) continue;
                if (regex is null ? !ScanPlain(text, line, lane, cmp) : !ScanRegex(regex, text, line, lane))
                    return;   // hit the cap
            }
    }

    /// <summary>False when the cap has been reached and the scan should stop.</summary>
    private bool ScanPlain(string text, int line, int lane, StringComparison cmp)
    {
        int from = 0;
        while (from <= text.Length - Query.Length)
        {
            int at = text.IndexOf(Query, from, cmp);
            if (at < 0) break;
            if (!Add(new Hit(line, lane, at, Query.Length))) return false;
            // Past this match rather than one character on: overlapping matches would tint the
            // same run twice and make "next" walk a character at a time.
            from = at + Query.Length;
        }
        return true;
    }

    private bool ScanRegex(Regex regex, string text, int line, int lane)
    {
        int from = 0;
        while (from <= text.Length)
        {
            var m = regex.Match(text, from);
            if (!m.Success) break;
            if (!Add(new Hit(line, lane, m.Index, m.Length))) return false;
            from = m.Index + Math.Max(1, m.Length);
        }
        return true;
    }

    private bool Add(Hit hit)
    {
        _hits.Add(hit);
        var key = (hit.Lane, hit.Line);
        if (!_byLine.TryGetValue(key, out var list)) _byLine[key] = list = [];
        list.Add(hit);
        if (_hits.Count < MaxHits) return true;
        Capped = true;
        return false;
    }
}
