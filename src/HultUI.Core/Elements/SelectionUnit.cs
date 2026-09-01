namespace HultUI.Core.Elements;

/// <summary>
/// How much text one step of a drag selection takes. Set by the press that starts the drag —
/// one click selects by character, two by word, three by line (or by the whole value, where
/// there is only one line) — and held for as long as the button is down, so the motion after
/// a double-click keeps snapping to whole words instead of dropping back to characters.
/// </summary>
internal enum SelectionUnit
{
    Character,
    Word,
    Line,
}

internal static class DragSelection
{
    /// <summary>
    /// The unit a press with <paramref name="clickCount"/> presses behind it selects by.
    /// Anything past three keeps the line, the way a browser does.
    /// </summary>
    public static SelectionUnit UnitFor(int clickCount) => clickCount switch
    {
        <= 1 => SelectionUnit.Character,
        2 => SelectionUnit.Word,
        _ => SelectionUnit.Line,
    };

    /// <summary>
    /// Where the two ends of the selection go once the pointer has moved onto
    /// <paramref name="hit"/> — the unit range under it — having started on
    /// <paramref name="anchor"/>. Returns (fixed end, moving end): dragging backwards past
    /// the anchor flips them, so the anchor unit stays wholly selected in both directions.
    /// </summary>
    public static (int Anchor, int Caret) Extend((int Start, int End) anchor, (int Start, int End) hit)
        => hit.Start < anchor.Start ? (anchor.End, hit.Start) : (anchor.Start, hit.End);
}
