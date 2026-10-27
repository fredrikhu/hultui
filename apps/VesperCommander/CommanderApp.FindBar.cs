using HultUI.Core.Builders;
using HultUI.Core.Elements;
using HultUI.Core.Reactivity;
using VesperCommander.Theme;

namespace VesperCommander;

public partial class CommanderApp
{
    // ── Find in the open text (Ctrl+F) ───────────────────────────────────
    //
    // Shared by the file viewer and the comparison view. Deliberately not the Alt+F7 find,
    // which walks folders and answers "which files"; this answers "where in this one", so it
    // is a bar over the text rather than a dialog over the app, every match is tinted where it
    // sits, and the answer is a position to scroll to rather than a list to pick from.
    //
    // The bar is HultUI's FindBar, the one Junction and Meridian use, and its look is
    // Nocturne's FindBarTheme. What lives here is where each view's selection is.

    /// <summary>A bar over one of the two views. The reveal lands on the same frame: both views
    /// scroll with ScrollRangeIntoView, which holds a request made before layout until the
    /// rows are measured. View-only: a step of the bar changes nothing a part shows, and a full
    /// build made the title bar and the command line again on every Enter.</summary>
    private FindBar NewFindBar(string placeholder, Ref<ScrollViewElement> body, Action reveal) => new(MarkViewDirty)
    {
        Placeholder = placeholder,
        FieldHeight = Ui.FieldHeight,
        Body = () => body.Current,
        Reveal = reveal,
        RevealAfterLayout = false,
        LetterKeys = true,
        Icon = FindIcon,
    };

    private static Element FindIcon(FindBarIcon icon) => icon switch
    {
        FindBarIcon.Search => Ui.Icon(Glyphs.Search, 13f, Nocturne.On(0.45)),
        FindBarIcon.Previous => Ui.Icon(Glyphs.ArrowUp, 12f, Nocturne.Neutral300),
        FindBarIcon.Next => Ui.Icon(Glyphs.ArrowDown, 12f, Nocturne.Neutral300),
        _ => Ui.Icon(Glyphs.Close, 12f, Nocturne.Neutral300),
    };

    /// <summary>The current match as the rows show it: its index and the line it sits on (a row of
    /// the view: the search's line numbers are the rows'), or -1 for both when there is none.</summary>
    private static (int Index, int Line) CurrentHit(TextSearch search) =>
        search.Index >= 0 && search.Index < search.Hits.Count ? (search.Index, search.Hits[search.Index].Line) : (-1, -1);

    /// <summary>Makes again, before the build patches the list, every row when what all of them show
    /// changed (<paramref name="all"/>), else the rows the current match left and reached when it
    /// moved. The current match used to be in the rows' shape, so every Enter or F3 made every row
    /// on screen again (the reader on a 3,000-line file: 35 rows and 330 KB an Enter, 2.5 rows and
    /// 69 KB now).</summary>
    private static void RemakeForFind<T>(VirtualListElement<T> list, bool all, (int Index, int Line) was, (int Index, int Line) now)
    {
        if (all) list.InvalidateRows();
        else if (now != was)
        {
            list.InvalidateRow(was.Line);   // -1 does nothing
            list.InvalidateRow(now.Line);
        }
    }

    /// <summary>The selection in the viewer. Its line numbers are the search's line numbers.</summary>
    private FindSelection? ViewerFindSelection() => At(_viewerGroup, lane: 0);

    /// <summary>The selection in the comparison. Row numbers are the search's line numbers,
    /// and the side the selection is on is its lane.</summary>
    private FindSelection? DiffFindSelection() =>
        At(_diffGroupLeft, lane: 0) ?? At(_diffGroupRight, lane: 1);

    private static FindSelection? At(TextSelectionGroup group, int lane)
    {
        if (!group.HasSelection) return null;
        var (line, offset) = group.Start;
        return new FindSelection(group.SelectedText, line, lane, offset);
    }
}
