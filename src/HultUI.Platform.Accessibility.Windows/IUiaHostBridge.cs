using System.Runtime.Versioning;
using HultUI.Core.Accessibility;
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
    // either snapshot or marshal internally. The bridge no longer calls them
    // (plan 17, round 4): it answers every read from the accessibility tree it
    // applied, and a list's on-demand items from the list its node carries
    // (AccessibilityNode.ItemSource). They stay so a host's implementation of
    // this interface still compiles.
    string? GetValue(ElementId id);
    bool? GetToggleState(ElementId id);

    // On-demand item queries for a large virtualized list (see
    // AccessibilityNode.IsVirtualItemContainer). Called from worker threads for
    // items that were never materialized as elements — hosts read defensively.
    int GetItemCount(ElementId listId);
    string? GetItemLabel(ElementId listId, int index);
    bool GetItemSelected(ElementId listId, int index);
    /// <summary>The list's selected item index, or -1 (for the Selection pattern).</summary>
    int GetSelectedIndex(ElementId listId);
    /// <summary>Whether the list allows selecting more than one item at once.</summary>
    bool GetCanSelectMultiple(ElementId listId);
    /// <summary>All selected item indices, ascending (for the container Selection pattern).</summary>
    IReadOnlyList<int> GetSelectedIndices(ElementId listId);
    /// <summary>Makes <paramref name="index"/> the sole selection (SelectionItem.Select).</summary>
    void SelectItemOnly(ElementId listId, int index);
    /// <summary>Adds/removes <paramref name="index"/> from a multi-selection.</summary>
    void SetItemSelected(ElementId listId, int index, bool selected);
    /// <summary>Finds an item by property without realizing all items (ItemContainer), or -1.</summary>
    int FindItemIndex(ElementId listId, int startAfter, AccessibleItemQuery query);
    bool GetItemOffscreen(ElementId listId, int index);
    /// <summary>The item's on-screen rect in layout units (scroll-adjusted like a node's bounds).</summary>
    bool TryGetItemLayout(ElementId listId, int index, out Rect layoutRect);

    // Scroll pattern for a scroll view / virtualized list.
    bool TryGetScrollInfo(ElementId id, out ScrollInfo info);
    void SetScrollPercent(ElementId id, double horizontalPercent, double verticalPercent);
    void ScrollByAmount(ElementId id, int horizontalAmount, int verticalAmount);

    // RangeValue pattern for a slider.
    bool TryGetRangeValue(ElementId id, out RangeValueInfo info);
    void SetRangeValue(ElementId id, double value);

    /// <summary>
    /// A client asked the window for its root (<c>WM_GETOBJECT</c>), on the UI thread, possibly
    /// inside the frame loop's wait, which does not end for a message SDL makes no event of. A
    /// client that has just attached gets the root before any tree is built; the host wakes the
    /// loop so its next frame builds one.
    /// </summary>
    void RootRequested() { }
}

/// <summary>UIA ScrollPattern state for a scroll view. Percents are 0..100, or -1 (no scroll).</summary>
public readonly record struct ScrollInfo(
    double HorizontalPercent, double VerticalPercent,
    double HorizontalViewSize, double VerticalViewSize,
    bool HorizontallyScrollable, bool VerticallyScrollable);

/// <summary>UIA RangeValuePattern state for a slider.</summary>
public readonly record struct RangeValueInfo(
    double Value, double Minimum, double Maximum,
    double SmallChange, double LargeChange, bool IsReadOnly);
