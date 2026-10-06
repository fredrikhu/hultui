using HultUI.Core.Builders;
using HultUI.Core.Primitives;
using HultUI.Core.Rendering;
using HultUI.Core.RichText;

namespace HultUI.Core.Elements;

public partial class RichTextEditorElement
{
    // ── Find (docs/plans/16-rich-text-editor.md, stage 8) ──
    //
    // The search is a FindBar's TextSearch, its lines the document's blocks: a match is a block
    // and a run of characters in it. The editor binds it to the document and again after every
    // edit, tints the matches on screen behind the text, and selects the current one.

    private TextSearch? _search;
    private RichDocument? _searchedDocument;
    private (string Query, bool Case, bool Regex, int Index, int Count) _searchDrawn;

    /// <summary>
    /// The search whose matches this editor tints — a <see cref="FindBar"/>'s
    /// <see cref="FindBar.Search"/>. Set, the editor binds it to the document, one line a block,
    /// and binds it again after every edit, so the matches follow the text; null tints nothing.
    /// Hand the same instance in every build.
    /// </summary>
    public TextSearch? Search
    {
        get => _search;
        set
        {
            if (ReferenceEquals(_search, value)) return;
            _search = value;
            _searchedDocument = null;
            BindSearch();
            MarkPaintDirty();
        }
    }

    private void BindSearch()
    {
        if (_search is not { } search) return;
        var doc = _state.Document;
        if (ReferenceEquals(doc, _searchedDocument)) return;
        _searchedDocument = doc;
        search.Bind(doc.BlockCount, lanes: 1, (line, _) => (uint)line < (uint)doc.BlockCount ? doc[line].Text : null);
    }

    // A rebuild after the search moved — a new query, the next match — repaints the tints.
    private void RefreshSearchTints()
    {
        if (_search is not { } s) return;
        var drawn = (s.Query, s.MatchCase, s.UseRegex, s.Index, s.Hits.Count);
        if (drawn == _searchDrawn) return;
        _searchDrawn = drawn;
        MarkPaintDirty();
    }

    /// <summary>
    /// Selects the search's current match, which scrolls it into view: a
    /// <see cref="FindBar.Reveal"/>. Selected rather than only shown, so closing the bar leaves
    /// the caret at what was found.
    /// </summary>
    public void RevealSearchMatch()
    {
        if (_search?.Current is not { } hit || hit.Line < 0 || hit.Line >= _state.Document.BlockCount) return;
        int length = _state.Document[hit.Line].Text.Length;
        int start = Math.Clamp(hit.Start, 0, length);
        int end = Math.Clamp(hit.Start + hit.Length, start, length);
        _state.Select(new RichPosition(hit.Line, start), new RichPosition(hit.Line, end));
    }

    /// <summary>What a <see cref="FindBar"/> opens with (<see cref="FindBar.Selection"/>): the
    /// selected text and where it starts; null with nothing selected.</summary>
    public FindSelection? FindSelection
    {
        get
        {
            var s = _state.Selection;
            if (s.IsCollapsed) return null;
            return new FindSelection(_state.SelectedText, s.Start.Block, 0, s.Start.Offset);
        }
    }

    // The matches in block `index`, behind its text: the current one stronger.
    private void DrawSearchHits(IRenderContext ctx, int index, BlockLayout bl, float ox, float oy)
    {
        if (_search?.HitsOn(index) is not { } hits || bl.Paragraph is not { } para) return;
        var current = _search.Current;
        int length = bl.Block.Text.Length;
        float x0 = ox + bl.TextX, y0 = oy + bl.Top + bl.TextY;
        foreach (var hit in hits)
        {
            if (hit.Length <= 0) continue;
            int start = Math.Clamp(hit.Start, 0, length), end = Math.Clamp(hit.Start + hit.Length, start, length);
            if (end <= start) continue;
            bool isCurrent = current is { } c && c.Line == index && c.Start == hit.Start;
            _boxes.Clear();
            para.SelectionBoxes(start, end, _boxes, 0);
            foreach (var r in _boxes)
                ctx.DrawRect(new Rect(x0 + r.X, y0 + r.Y, r.Width, r.Height), isCurrent ? _findCurrent : _findHit);
        }
    }
}
