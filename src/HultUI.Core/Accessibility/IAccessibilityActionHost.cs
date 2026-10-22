using HultUI.Core.Elements;

namespace HultUI.Core.Accessibility;

/// <summary>
/// Routes assistive-technology "actions" — invoke a button, toggle a checkbox,
/// select a radio — from a platform bridge back onto a live element by its
/// <see cref="ElementId"/>. Implemented in the windowing layer (which owns the
/// element tree and the UI thread) and consumed by the platform accessibility
/// bridges, so a bridge can trigger an action without depending on the windowing
/// assembly directly.
/// </summary>
/// <remarks>
/// Calls arrive on assistive-tech threads (UIA worker threads, the AT-SPI D-Bus
/// receive thread); implementations marshal the work onto the UI thread.
/// </remarks>
public interface IAccessibilityActionHost
{
    /// <summary>Activates the element (dispatches a click), as for a button.</summary>
    void InvokeElement(ElementId id);

    /// <summary>Flips a checkbox / switch element's checked state.</summary>
    void ToggleElement(ElementId id);

    /// <summary>Selects the element, as for a radio button or list item.</summary>
    void SelectElement(ElementId id);

    /// <summary>Gives the element (or a described node, seam W11) the keyboard.</summary>
    void FocusElement(ElementId id) { }

    /// <summary>Sets a range's value (a slider's, a number field's: <see cref="IRangeValueElement"/>),
    /// clamped to its range; nothing for a read-only one.</summary>
    void SetRangeValue(ElementId id, double value) { }

    /// <summary>Opens or closes what the element opens (a dropdown's list, an expander, a
    /// submenu), as the keyboard or the pointer would. The default invokes it, which toggles an
    /// element that opens on a click.</summary>
    void ExpandCollapseElement(ElementId id, bool expand) => InvokeElement(id);

    /// <summary>Scrolls every scroll view holding the element (or list item) until it is in
    /// view, as moving the keyboard to it would.</summary>
    void ScrollIntoView(ElementId id) { }

    /// <summary>
    /// The bridge's <see cref="IAccessibilityBridge.IsActive"/> may have changed, on any thread:
    /// assistive technology was switched on or off, or a client attached. The window reads
    /// <c>IsActive</c> in its frame loop, which waits for its next deadline or a wake rather
    /// than coming round every 16 ms, so a change it is not told of is not seen until something
    /// else wakes it.
    /// </summary>
    void ActiveChanged() { }
}
