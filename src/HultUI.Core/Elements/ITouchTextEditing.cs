using HultUI.Core.Primitives;

namespace HultUI.Core.Elements;

/// <summary>
/// What the window's touch selection needs of a text control (docs/design/SOFT-KEYBOARD.md §4):
/// where the selection's ends are, a word chosen by a long press, the ends moved by their handles,
/// and the edit items for the bar. <see cref="TextInputElement"/>, <see cref="TextAreaElement"/>
/// and <see cref="RichTextEditorElement"/> implement it; the window draws the handles and the bar
/// once for the three. Points and boxes are in the space the control is laid out in
/// (<c>Layout.Absolute*</c>); the window maps a finger there through the control's
/// <see cref="Element.WindowBorderBox"/>, so scrolled pages and overlays are taken out.
/// </summary>
internal interface ITouchTextEditing
{
    /// <summary>Whether text is selected, so the window shows two handles rather than one.</summary>
    bool HasTouchSelection { get; }

    /// <summary>The caret's box at the selection's start and at its end, as the control lays them
    /// out (the control's own scroll applied, its ancestors' not); both are the caret's when
    /// nothing is selected. Null while the control cannot measure, before its first frame.</summary>
    (Rect Start, Rect End)? TouchCaretBoxes();

    /// <summary>Selects the word at <paramref name="position"/>: a long press.</summary>
    void TouchSelectWordAt(Point position);

    /// <summary>A handle's drag begins: <paramref name="movesEnd"/> says which end of the selection
    /// moves, and the other stays. With nothing selected the caret moves alone.</summary>
    void TouchBeginHandleDrag(bool movesEnd);

    /// <summary>The moving end, or the caret, to the text at <paramref name="position"/>. An end
    /// never reaches the one that stays: the selection never collapses under a handle.</summary>
    void TouchDragHandleTo(Point position);

    /// <summary>The handle was let go.</summary>
    void TouchEndHandleDrag();

    /// <summary>The control's context menu, whose Cut, Copy, Paste and Select All the edit bar
    /// shows (<see cref="TouchEdit.BarItems"/>), and the rest behind its "more" item.</summary>
    IReadOnlyList<ContextMenuItem> TouchEditItems();
}

/// <summary>The edit bar's share of a text control's context menu.</summary>
internal static class TouchEdit
{
    /// <summary>The labels the three controls give their clipboard items: what a phone's edit bar
    /// offers. Undo and Redo, a link's items and a table's stay in the menu.</summary>
    private static readonly string[] BarLabels = ["Cut", "Copy", "Paste", "Select All"];

    /// <summary>The menu's enabled clipboard items, in the menu's order.</summary>
    public static List<ContextMenuItem> BarItems(IReadOnlyList<ContextMenuItem> menu)
    {
        var items = new List<ContextMenuItem>();
        foreach (var item in menu)
            if (IsBarItem(item) && !item.IsDisabled && item.Action is not null) items.Add(item);
        return items;
    }

    /// <summary>Whether the menu holds something enabled beyond the bar's items, which the bar's
    /// "more" item opens the menu for.</summary>
    public static bool HasMore(IReadOnlyList<ContextMenuItem> menu)
    {
        foreach (var item in menu)
            if (!item.IsSeparator && !item.IsDisabled && !IsBarItem(item) && item.Label is not ("Undo" or "Redo")) return true;
        return false;
    }

    private static bool IsBarItem(ContextMenuItem item) => !item.IsSeparator && Array.IndexOf(BarLabels, item.Label) >= 0;
}
