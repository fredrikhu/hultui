using HultUI.Core.Builders;
using HultUI.Core.Elements;
using Junction.Theme;

namespace Junction;

public partial class JunctionApp
{
    // ── Find in the text on screen (Ctrl+F) ──────────────────────────────
    //
    // Shared by the diff pane and the Files view. Not the command palette, which answers
    // "which command"; this answers "where in this text", so it is a bar over the content,
    // every match is tinted where it sits, and the answer is a position to scroll to.
    //
    // The bar is HultUI's FindBar — the same one Vesper and Meridian use, over the same
    // TextSearch, so its keys and a regular expression mean the same thing everywhere; its look
    // is Nocturne's FindBarTheme. What lives here is which bar a view uses and where in each
    // body the selection is.

    /// <summary>The bar Ctrl+F opens here, or null where there is no body of text to search.
    /// The Files view has its own; every other view that shows one shows the diff pane.</summary>
    private FindBar? ActiveFindBar() => View switch
    {
        JunctionView.Files => _filesFind,
        JunctionView.Commit or JunctionView.Changes => _diffFind,
        _ => _diffFull ? _diffFind : null,
    };

    // The view's: a find bar searches the text on screen, which is nothing the chrome shows.
    private FindBar NewFindBar(string placeholder, Func<Element?> body, Action reveal) => new(MarkViewDirty)
    {
        Placeholder = placeholder,
        Body = body,
        Reveal = reveal,
        LetterKeys = true,
        Icon = FindIcon,
    };

    private static Element FindIcon(FindBarIcon icon) => icon switch
    {
        FindBarIcon.Search => Ui.Icon(Icons.MagnifyingGlass, 13f, Nocturne.On(0.45)),
        FindBarIcon.Previous => Ui.Icon(Icons.ArrowUp, 15f, Nocturne.Text),
        FindBarIcon.Next => Ui.Icon(Icons.ArrowDown, 15f, Nocturne.Text),
        _ => Ui.Icon(Icons.X, 15f, Nocturne.Text),
    };

    /// <summary>The selection in the Files body, as a line and a column. One TextArea over the
    /// joined text: its offsets are absolute, and the line starts the tinting pass memoized
    /// turn them back.</summary>
    private FindSelection? FilesFindSelection()
    {
        if (_fileBodyRef.Current is not { HasSelection: true } area || !_window.IsInTree(area)) return null;
        int offset = area.SelectionStart;
        int lineAt = Array.BinarySearch(_fileLineStarts, offset);
        if (lineAt < 0) lineAt = ~lineAt - 1;
        if (lineAt < 0) return new(area.SelectedText, 0, 0, offset);
        return new(area.SelectedText, lineAt, 0, offset - _fileLineStarts[lineAt]);
    }

    /// <summary>The selection in the diff. Row numbers into the flattened diff are the search's
    /// line numbers too; the side a split selection is on is its lane.</summary>
    private FindSelection? DiffFindSelection()
    {
        if (_diffGroupUnified.HasSelection) return At(_diffGroupUnified, lane: 0);
        if (_diffGroupLeft.HasSelection) return At(_diffGroupLeft, lane: 0);
        if (_diffGroupRight.HasSelection) return At(_diffGroupRight, lane: 1);
        return null;

        static FindSelection At(TextSelectionGroup g, int lane)
        {
            var (index, offset) = g.Start;
            return new(g.SelectedText, index, lane, offset);
        }
    }
}
