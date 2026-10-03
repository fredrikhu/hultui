using HultUI.Core.Animation;
using HultUI.Core.Builders;
using HultUI.Core.Primitives;
using HultUI.Core.Rendering;
using HultUI.Terminal.Emulation;

namespace HultUI.Terminal;

/// <summary>
/// Search over the scrollback and the screen, with HultUI's <see cref="TextSearch"/>, so a query
/// means what it means in every other find bar of the framework's apps.
/// </summary>
/// <remarks>
/// <para><b>What is searched</b> is the current screen's buffer as logical lines: a line that
/// wrapped is one line, so a match across the wrap is found. The search binds when a query is
/// first set, and binds again 200 ms after output arrives (<see cref="SearchRebindDelay"/>),
/// keeping the current hit where it was.</para>
/// <para><b>Hits</b> are tinted behind the text (<see cref="TerminalTheme.SearchHit"/>, the
/// current one <see cref="TerminalTheme.SearchCurrent"/>). Moving to a hit
/// (<c>Search.Move</c>) or changing the query scrolls it into view, centred, when it is off the
/// screen.</para>
/// <para><b>The bar</b> is the application's: Ctrl+Shift+F raises <see cref="FindRequested"/>,
/// and the element draws and reveals what the search finds. With no handler the key goes to the
/// program.</para>
/// </remarks>
public sealed partial class TerminalElement
{
    /// <summary>How long after output the search binds again: a flood is one rebind, not one a
    /// frame.</summary>
    internal static readonly TimeSpan SearchRebindDelay = TimeSpan.FromMilliseconds(200);

    private TextSearch? _search;
    private List<LogicalLine> _searchLines = [];
    private bool _searchBound, _rebinding;
    private TweenHandle _rebindTimer;
    private bool _rebindPending;

    /// <summary>The search: set its query and options, read its hits and count, move between
    /// them. The element binds it to the buffer and draws and reveals its hits.</summary>
    public TextSearch Search => _search ??= new TextSearch(OnSearchChanged);

    /// <summary>The search changed: its query, options, hits or current hit. For a find bar's
    /// count.</summary>
    public event Action? SearchChanged;

    /// <summary>Ctrl+Shift+F: the user asked for the application's find bar. While nothing
    /// handles it, the key goes to the program.</summary>
    public event Action? FindRequested;

    private void OnSearchChanged()
    {
        if (_rebinding) return;
        if (_search!.Query.Length == 0) _searchBound = false; // what it was bound to goes stale
        else if (!_searchBound) Rebind();
        if (IsHosted) MarkPaintDirty();
        RevealSearchHit();
        SearchChanged?.Invoke();
    }

    /// <summary>Output arrived: bind again once it pauses.</summary>
    private void ScheduleSearchRebind()
    {
        if (_search is not { Query.Length: > 0 } || _rebindPending) return;
        _rebindPending = true;
        _rebindTimer = Animate.Value(0f, 1f, static _ => { })
            .Duration((float)SearchRebindDelay.TotalSeconds)
            .OnComplete(() =>
            {
                _rebindPending = false;
                if (!IsHosted || _search is not { Query.Length: > 0 }) return;
                Rebind();
                MarkPaintDirty();
                SearchChanged?.Invoke();
            })
            .Start();
    }

    private void StopSearchRebind()
    {
        if (!_rebindPending) return;
        _rebindTimer.Cancel();
        _rebindPending = false;
    }

    /// <summary>Binds the search to the buffer as it is now, keeping the current hit: the same
    /// line (by its place in the buffer, which output does not move) and offset.</summary>
    private void Rebind()
    {
        if (_controller is null || _search is null) return;
        (long Line, int Start)? keep = _search.Current is { } h && h.Line < _searchLines.Count
            ? (_searchLines[h.Line].FirstLine, h.Start)
            : null;
        _rebinding = true;
        try
        {
            _searchLines = _controller.ReadLogicalLines();
            var lines = _searchLines;
            _search.Bind(lines.Count, 1, (i, _) => lines[i].Text);
            if (keep is { } k && FindLogicalLine(k.Line) is >= 0 and var index) _search.SeekTo(index, 0, k.Start);
        }
        finally { _rebinding = false; }
        _searchBound = true;
    }

    /// <summary>The logical line holding absolute line <paramref name="line"/>, or -1.</summary>
    private int FindLogicalLine(long line)
    {
        int lo = 0, hi = _searchLines.Count - 1;
        while (lo <= hi)
        {
            int mid = (lo + hi) / 2;
            var l = _searchLines[mid];
            if (line < l.FirstLine) hi = mid - 1;
            else if (line >= l.FirstLine + l.RowCount) lo = mid + 1;
            else return mid;
        }
        return -1;
    }

    private static int RowOf(LogicalLine line, int offset)
    {
        int row = Array.BinarySearch(line.RowStarts, offset);
        if (row < 0) row = ~row - 1;
        // Several rows can start at one offset (blank rows at a line's end); the first holds it.
        while (row > 0 && line.RowStarts[row - 1] == line.RowStarts[row]) row--;
        return Math.Max(0, row);
    }

    /// <summary>Scrolls the current hit into view, centred, when it is off the screen.</summary>
    private void RevealSearchHit()
    {
        if (_controller is null || _search?.Current is not { } hit || hit.Line >= _searchLines.Count) return;
        var line = _searchLines[hit.Line];
        long absolute = line.FirstLine + RowOf(line, hit.Start);
        var s = _snapshot;
        long top = s.LinesDropped + s.ViewportTop;
        if (absolute >= top && absolute < top + s.Rows) return;
        long target = absolute - s.Rows / 2;
        _controller.ScrollViewport((int)Math.Clamp(target - top, int.MinValue, int.MaxValue));
    }

    /// <summary>The tints behind one row's hits, under the text.</summary>
    private void DrawSearchHits(IRenderContext ctx, TerminalSnapshot s, int row, float left, float y, float scale,
        TerminalTheme theme)
    {
        if (_search is not { HasHits: true } search || !_searchBound) return;
        long absolute = s.LinesDropped + s.ViewportTop + row;
        int index = FindLogicalLine(absolute);
        if (index < 0 || search.HitsOn(index) is not { } hits) return;

        var line = _searchLines[index];
        int k = (int)(absolute - line.FirstLine);
        int rowStart = line.RowStarts[k];
        int rowEnd = k + 1 < line.RowCount ? line.RowStarts[k + 1] : int.MaxValue;
        var cells = s.Row(row);
        var current = search.Current;

        foreach (var hit in hits)
        {
            int a = Math.Max(hit.Start, rowStart), b = Math.Min(hit.Start + hit.Length, rowEnd);
            if (b <= a) continue;
            int from = ColumnOf(cells, a - rowStart);
            int to = ColumnOf(cells, b - 1 - rowStart);
            if (to < cells.Length) to += Math.Max(1, (int)cells[to].Width);
            if (to > from)
                ctx.DrawRect(Span(left, y, from, to, scale), hit == current ? theme.SearchCurrent : theme.SearchHit);
        }
    }

    /// <summary>The column whose cell holds offset <paramref name="offset"/> of a row's text;
    /// the row's width when the offset is past its end.</summary>
    private static int ColumnOf(ReadOnlySpan<TerminalCell> cells, int offset)
    {
        int at = 0;
        for (int col = 0; col < cells.Length; col++)
        {
            int length = cells[col].TextLength;
            if (length == 0) continue;
            if (offset < at + length) return col;
            at += length;
        }
        return cells.Length;
    }
}
