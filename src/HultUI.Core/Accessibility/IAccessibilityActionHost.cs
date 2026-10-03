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
}
