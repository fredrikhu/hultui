using HultUI.Core.Builders;
using HultUI.Core.Elements;
using HultUI.Core.Layout;
using HultUI.Core.Primitives;
using HultUI.Core.Reactivity;
using HultUI.Core.Rendering;
using HultUI.Core.Styling;
using VesperCommander.Theme;

namespace VesperCommander;

public partial class CommanderApp
{
    // ── Find in the open file (Ctrl+F, inside the Shift+F3 viewer) ───────
    //
    // Deliberately not the Alt+F7 find, which walks folders and answers "which files".
    // This answers "where in this one", so it is a bar over the text rather than a dialog
    // over the app, every hit is tinted where it sits, and the answer is a position to
    // scroll to rather than a list to pick from.

    private bool _vfindOpen;
    private string _vfindQuery = "";
    private bool _vfindMatchCase;
    // Every hit, in reading order: the line it is on and where it starts in that line.
    private readonly List<(int Line, int Start)> _vfindHits = new();
    // The same hits keyed by line, because the row template asks the question the other way
    // round — "what do I tint in this line" — once per realized row per frame.
    private Dictionary<int, List<int>>? _vfindByLine;
    private int _vfindIdx;
    private bool _vfindCapped;
    private bool _pendingVfindFocus, _pendingVfindScroll, _pendingViewerBodyFocus;
    private readonly Ref<TextInputElement> _vfindRef = new();

    /// <summary>
    /// How many hits are kept. A one-letter query against a large file matches hundreds of
    /// thousands of times, and neither the list nor the person reading it gains anything past
    /// the first few thousand. The bar says when the count was cut, rather than quietly
    /// showing a total that isn't one.
    /// </summary>
    private const int MaxViewerHits = 20_000;

    private void OpenViewerFind()
    {
        _vfindOpen = true;
        _pendingVfindFocus = true;
        MarkDirty();
    }

    /// <summary>Closes the bar but not the view. Focus has to go somewhere still in the tree:
    /// the field is about to be unmounted, and an event dispatched to a detached element
    /// reaches only that element — it never bubbles to the handler that reads the view's keys,
    /// so leaving focus on it would make the viewer deaf.</summary>
    private void CloseViewerFind()
    {
        _vfindOpen = false;
        _vfindQuery = "";
        ClearViewerFind();
        _pendingViewerBodyFocus = true;
        MarkDirty();
    }

    private void ClearViewerFind()
    {
        _vfindHits.Clear();
        _vfindByLine = null;
        _vfindIdx = 0;
        _vfindCapped = false;
    }

    /// <summary>
    /// Rescans the file for the current query.
    /// <para>
    /// On the UI thread, unlike the read and the tokenize either side of it, and bounded by
    /// the same 4MB ceiling the reader enforces: an ordinal scan of that much text is a few
    /// milliseconds, which is cheaper than the generation-guard dance an off-thread scan would
    /// need to keep a stale result from landing on a query the user has since retyped.
    /// </para>
    /// </summary>
    private void RunViewerFind()
    {
        ClearViewerFind();
        if (_vfindQuery.Length == 0 || _viewerState != ViewerState.Ok) { MarkDirty(); return; }

        var cmp = _vfindMatchCase ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        var byLine = new Dictionary<int, List<int>>();
        for (int i = 0; i < _viewerLines.Length && _vfindHits.Count < MaxViewerHits; i++)
        {
            string line = _viewerLines[i];
            int from = 0;
            while (from <= line.Length - _vfindQuery.Length)
            {
                int at = line.IndexOf(_vfindQuery, from, cmp);
                if (at < 0) break;
                _vfindHits.Add((i, at));
                if (!byLine.TryGetValue(i, out var starts)) byLine[i] = starts = new List<int>();
                starts.Add(at);
                if (_vfindHits.Count >= MaxViewerHits) { _vfindCapped = true; break; }
                // Past this hit rather than one character on: overlapping hits would tint the
                // same run twice and make "next" walk a character at a time.
                from = at + _vfindQuery.Length;
            }
        }
        _vfindByLine = byLine;
        _pendingVfindScroll = true;
        MarkDirty();
    }

    /// <summary>Walks the hits, wrapping at either end — a search that stops dead at the last
    /// match reads as broken when the one you wanted is above where you started.</summary>
    private void ViewerFindNav(int dir)
    {
        if (_vfindHits.Count == 0) return;
        _vfindIdx = (_vfindIdx + dir + _vfindHits.Count) % _vfindHits.Count;
        _pendingVfindScroll = true;
        MarkDirty();
    }

    private void ToggleViewerFindCase()
    {
        _vfindMatchCase = !_vfindMatchCase;
        RunViewerFind();
    }

    /// <summary>Brings the current hit on screen. Read from the row index, which knows every
    /// line's position whether or not it has been realized, so this answers for a match ten
    /// thousand lines below the viewport.</summary>
    private void RevealViewerHit()
    {
        if (_viewerScrollRef.Current is not { } sv) return;
        if (_vfindIdx < 0 || _vfindIdx >= _vfindHits.Count) return;
        int line = _vfindHits[_vfindIdx].Line;
        if (line < 0 || line + 1 >= _viewerTops.Length) return;
        sv.ScrollRangeIntoView(_viewerTops[line], _viewerTops[line + 1] - _viewerTops[line]);
    }

    /// <summary>Where the current hit starts, if it is on <paramref name="line"/> — the one
    /// run that takes the bright tint. -1 when the current hit is elsewhere.</summary>
    private int CurrentHitStartOn(int line) =>
        _vfindIdx >= 0 && _vfindIdx < _vfindHits.Count && _vfindHits[_vfindIdx].Line == line
            ? _vfindHits[_vfindIdx].Start
            : -1;

    /// <summary>
    /// The spans a line is drawn with: its syntax colouring, with any search hits riding along
    /// as backgrounds. Hits take a background and never a foreground, so a matched word keeps
    /// the colour its grammar gave it instead of being repainted flat.
    /// </summary>
    private IReadOnlyList<TextSpan>? ViewerLineSpans(int line, string text)
    {
        var syntax = ViewerSpansFor(line);
        if (_vfindByLine is null || !_vfindByLine.TryGetValue(line, out var starts)) return syntax;
        return MergeFindSpans(syntax, starts, _vfindQuery.Length, text.Length, CurrentHitStartOn(line));
    }

    /// <summary>
    /// Folds syntax colouring and hit tinting into the one sorted, non-overlapping list
    /// <see cref="TextElement.Spans"/> accepts. Where the two disagree about boundaries the
    /// text is cut at every edge of either, so between two consecutive cuts both the colour
    /// and the tint are constant.
    /// </summary>
    private static IReadOnlyList<TextSpan>? MergeFindSpans(
        IReadOnlyList<TextSpan>? syntax, List<int> starts, int len, int length, int currentStart)
    {
        if (len <= 0 || length <= 0) return syntax;
        bool hasSyntax = syntax is { Count: > 0 };

        var cuts = new SortedSet<int> { 0, length };
        foreach (int s in starts)
        {
            cuts.Add(Math.Clamp(s, 0, length));
            cuts.Add(Math.Clamp(s + len, 0, length));
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
                var bg = HitTintAt(starts, len, from, currentStart);
                if (fg != default || bg != default)
                    merged.Add(new TextSpan(from, cut - from, fg, bg));
            }
            prev = cut;
        }
        return merged.Count > 0 ? merged : null;
    }

    /// <summary>The tint covering <paramref name="offset"/>: bright for the hit the cursor is
    /// on, dim for the rest, none outside them.</summary>
    private static Color HitTintAt(List<int> starts, int len, int offset, int currentStart)
    {
        foreach (int s in starts)
        {
            if (offset < s) break;                     // ascending; past it
            if (offset < s + len)
                return s == currentStart ? Nocturne.FindCurrentBg : Nocturne.FindHitBg;
        }
        return default;
    }

    // ── The bar ──────────────────────────────────────────────────────────

    private Element BuildViewerFindBar()
    {
        var row = Flex.Row(gap: 8).FlexShrink(0).AlignItems(AlignItems.Center)
            .Padding(new Thickness(5, 12, 5, 12))
            .Background(Nocturne.Surface)
            .BorderColor(Nocturne.Divider).BorderWidth(new Thickness(0, 0, 1, 0));

        row.AddChild(Ui.Icon(Glyphs.Search, 13f, Nocturne.On(0.45)));

        // Explicit height: a flex-only TextInput collapses its content box and paints neither
        // text nor placeholder. See Ui.FieldHeight.
        var input = TextInput.New(_vfindQuery).Width(280).Height(Ui.FieldHeight);
        input.PlaceholderText("Find in file…");
        input.OnTextChanged(v => { _vfindQuery = v; RunViewerFind(); });
        input.WithRef(_vfindRef);
        row.AddChild(input);

        row.AddChild(Ui.Mono(ViewerFindStatus(), 10.5f, _vfindQuery.Length > 0 && _vfindHits.Count == 0
            ? Nocturne.MarkRed : Nocturne.On(0.50)).MaxLines(1).FlexShrink(0));

        row.AddChild(Box.New().FlexGrow(1));

        row.AddChild(ViewerFindToggle("Aa", _vfindMatchCase, "Match case · Alt+C", ToggleViewerFindCase));
        row.AddChild(DiffNavBtn(Glyphs.ArrowUp, "p / Shift+Enter", () => ViewerFindNav(-1)));
        row.AddChild(DiffNavBtn(Glyphs.ArrowDown, "n / Enter", () => ViewerFindNav(1)));
        row.AddChild(DiffNavBtn(Glyphs.Close, "Esc", CloseViewerFind));
        return row;
    }

    private string ViewerFindStatus()
    {
        if (_vfindQuery.Length == 0) return "";
        if (_vfindHits.Count == 0) return "no matches";
        // "of 20,000+" when the scan stopped at the cap, so a count that is not the whole
        // truth never reads as one.
        string total = _vfindCapped ? $"{MaxViewerHits:N0}+" : $"{_vfindHits.Count:N0}";
        return $"{_vfindIdx + 1:N0} of {total}";
    }

    private Element ViewerFindToggle(string label, bool on, string tip, Action onClick)
    {
        var b = Box.New().Cursor(CursorType.Pointer).Padding(new Thickness(3, 7, 3, 7)).CornerRadius(4f)
            .Background(on ? Nocturne.Accent800 : Color.Transparent)
            .BorderColor(on ? Nocturne.Accent600 : Nocturne.Neutral700).BorderWidth(1f)
            .Children(Ui.Mono(label, 10f, on ? Nocturne.Accent200 : Nocturne.Neutral300))
            .Tooltip(tip);
        b.OnClick(_ => onClick());
        return b.HoverBg(Nocturne.Neutral900, Color.Transparent);
    }
}
