using HultUI.Core.Elements;
using HultUI.Core.Rendering;
using VesperCommander.Theme;

namespace VesperCommander;

public partial class CommanderApp
{
    // ── Find in the open file (Ctrl+F, inside the Shift+F3 viewer) ───────
    //
    // The bar, the keys and the search itself are shared with the comparison view — see
    // CommanderApp.FindBar.cs. What is left here is the part only this view can answer: which
    // text the search runs over, where a match sits on screen, and how a line is tinted.

    private readonly FindBarState _viewerFind;

    /// <summary>Points the search at the file now open. Called when one lands, so a query
    /// typed before it arrived — or carried over from the last file — searches this one.</summary>
    private void BindViewerSearch()
    {
        if (_viewerState != ViewerState.Ok) { _viewerFind.Search.Unbind(); return; }
        _viewerFind.Search.Bind(_viewerLines.Length, lanes: 1,
            (line, _) => line >= 0 && line < _viewerLines.Length ? _viewerLines[line] : null);
    }

    /// <summary>Brings the current match on screen. Read from the row index, which knows every
    /// line's position whether or not it has been realized, so this answers for a match ten
    /// thousand lines below the viewport.</summary>
    private void RevealViewerHit()
    {
        if (_viewerScrollRef.Current is not { } sv) return;
        if (_viewerFind.Search.Current is not { } hit) return;
        int line = hit.Line;
        if (line < 0 || line + 1 >= _viewerTops.Length) return;
        sv.ScrollRangeIntoView(_viewerTops[line], _viewerTops[line + 1] - _viewerTops[line]);
    }

    /// <summary>The spans a line is drawn with: its syntax colouring, with any matches tinted
    /// over it as backgrounds so a matched word keeps the colour its grammar gave it.</summary>
    private IReadOnlyList<TextSpan>? ViewerLineSpans(int line, string text) =>
        _viewerFind.Search.Decorate(ViewerSpansFor(line), line, lane: 0, text.Length,
            Nocturne.FindHitBg, Nocturne.FindCurrentBg);
}
