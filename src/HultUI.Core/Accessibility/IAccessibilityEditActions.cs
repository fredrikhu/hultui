using HultUI.Core.Elements;

namespace HultUI.Core.Accessibility;

/// <summary>
/// The actions a bridge routes beyond <see cref="IAccessibilityActionHost"/>'s, for a bridge whose
/// platform asks for them and that cannot reach the windowing layer itself: TalkBack's
/// (<c>HultUI.Platform.Accessibility.Android</c>), which HultUI cannot reference. The window's action
/// router implements them on the same queue, onto the UI thread. Internal, so they add no API; the
/// Windows bridge reaches the same router through its own host.
/// </summary>
internal interface IAccessibilityEditActions
{
    /// <summary>Replaces a text field's whole value, as a paste over it: undoable, and refused where
    /// a paste is (Android's <c>ACTION_SET_TEXT</c>, UIA's Value.SetValue).</summary>
    void SetValue(ElementId id, string value);

    /// <summary>
    /// Scrolls what the element scrolls (<see cref="IPanScrollable"/>: a scroll view, a list, a text
    /// area) by <paramref name="dx"/>, <paramref name="dy"/> layout units, positive further down and
    /// right through the content, as a finger would: the user's scroll, clamped to the content
    /// (Android's scroll actions).
    /// </summary>
    void ScrollBy(ElementId id, float dx, float dy);

    /// <summary>
    /// Selects a field's text from <paramref name="anchor"/> to <paramref name="caret"/>, offsets in
    /// the text it offers a reader (<see cref="AccessibilityNode.CurrentText"/>); equal offsets place
    /// the caret. As a click there would: no edit, the caret scrolled into view (Android's
    /// <c>ACTION_SET_SELECTION</c>, and its moves by character, word and line in a field).
    /// </summary>
    void SetSelection(ElementId id, int anchor, int caret);

    /// <summary>
    /// A long press on the element, or on a list's item (<see cref="VirtualItemId"/>), as a finger's
    /// there: a long-press handler on the way up runs, else the context menu opens, from the
    /// keyboard's place, its first item the reader's to go on from (Android's long-click action,
    /// which TalkBack performs on a double tap held: <see cref="AccessibilityNode.OffersLongPress"/>).
    /// </summary>
    void LongPress(ElementId id);

    /// <summary>Closes what the node is, as Escape where the keyboard is would: a dialog, an open menu
    /// (Android's dismiss action).</summary>
    void Dismiss(ElementId id);
}
