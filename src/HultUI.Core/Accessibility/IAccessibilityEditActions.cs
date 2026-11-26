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
}
