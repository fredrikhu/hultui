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
    /// rows are measured.</summary>
    private FindBar NewFindBar(string placeholder, Ref<ScrollViewElement> body, Action reveal) => new(MarkDirty)
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
