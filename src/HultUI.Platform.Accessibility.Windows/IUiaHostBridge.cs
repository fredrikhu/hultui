using System.Runtime.Versioning;
using HultUI.Core.Elements;
using HultUI.Core.Primitives;

namespace HultUI.Platform.Accessibility.Windows;

/// <summary>
/// Collaboration surface between <see cref="WindowsAccessibilityBridge"/> and
/// the hosting application. The host knows how to route element actions back
/// onto the UI thread and how to read element state without cross-thread
/// footguns; the bridge knows UIA. (<c>WM_GETOBJECT</c> is serviced by the
/// bridge's own window-procedure subclass, so no message hook is needed here.)
/// </summary>
[SupportedOSPlatform("windows")]
public interface IUiaHostBridge
{
    // Actions from UIA clients — hosts must marshal these onto the UI thread.
    void InvokeElement(ElementId id);
    void ToggleElement(ElementId id);
    void SelectElement(ElementId id);
    void SetValue(ElementId id, string value);
    void RequestFocus(ElementId id);

    /// <summary>
    /// Current display scale (OS DPI factor, e.g. 1.5 at 150%). Element bounds
    /// are in density-independent layout units; the bridge multiplies by this to
    /// convert to the physical client pixels UIA reports. Read live so it tracks
    /// the window moving between monitors of different scaling.
    /// </summary>
    float ContentScale { get; }

    // Reads from UIA. Should be safe to call from worker threads — hosts
    // either snapshot or marshal internally.
    string? GetValue(ElementId id);
    bool? GetToggleState(ElementId id);

    // On-demand item queries for a large virtualized list (see
    // AccessibilityNode.IsVirtualItemContainer). Called from worker threads for
    // items that were never materialized as elements — hosts read defensively.
    int GetItemCount(ElementId listId);
    string? GetItemLabel(ElementId listId, int index);
    /// <summary>The item's on-screen rect in layout units (scroll-adjusted like a node's bounds).</summary>
    bool TryGetItemLayout(ElementId listId, int index, out Rect layoutRect);
}
