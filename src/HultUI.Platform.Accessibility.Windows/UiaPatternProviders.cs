using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using HultUI.Core.Accessibility;
using HultUI.Core.Elements;

namespace HultUI.Platform.Accessibility.Windows;

/// <summary>
/// Pattern providers look up their target element via the bridge each time a
/// UIA client invokes them, so they don't become stale across tree rebuilds.
/// The actual element mutation routes through the same event/handler path as
/// pointer clicks — behavior is identical whether a human or an AT triggered it.
/// Exposed to UIA as native objects (see <see cref="NativeProvider"/>).
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class UiaInvokeProvider(WindowsAccessibilityBridge bridge, ElementId elementId) : INativeInvoke
{
    public int Invoke()
    {
        bridge.InvokeElement(elementId);
        return 0;
    }
}

[SupportedOSPlatform("windows")]
internal sealed class UiaToggleProvider(WindowsAccessibilityBridge bridge, ElementId elementId) : INativeToggle
{
    public int Toggle()
    {
        bridge.ToggleElement(elementId);
        return 0;
    }

    public int GetToggleState(out int pRetVal)
    {
        pRetVal = bridge.GetToggleState(elementId) switch
        {
            true => UiaInterop.ToggleState_On,
            false => UiaInterop.ToggleState_Off,
            null => UiaInterop.ToggleState_Indeterminate,
        };
        return 0;
    }
}

[SupportedOSPlatform("windows")]
internal sealed class UiaValueProvider(WindowsAccessibilityBridge bridge, ElementId elementId, bool readOnly = false)
    : INativeValue
{
    /// <summary>UIA_E_ELEMENTNOTENABLED: what SetValue on a read-only value answers.</summary>
    private const int ElementNotEnabled = unchecked((int)0x80040200);

    public int SetValue(IntPtr pwsz)
    {
        if (readOnly) return ElementNotEnabled;
        bridge.SetValue(elementId, Marshal.PtrToStringUni(pwsz) ?? "");
        return 0;
    }

    public int GetValue(out IntPtr pRetVal)
    {
        pRetVal = Marshal.StringToBSTR(bridge.GetValue(elementId) ?? "");
        return 0;
    }

    public int GetIsReadOnly(out int pRetVal)
    {
        pRetVal = readOnly ? 1 : 0;
        return 0;
    }
}

[SupportedOSPlatform("windows")]
internal sealed class UiaSelectionItemProvider(WindowsAccessibilityBridge bridge, ElementId elementId)
    : INativeSelectionItem
{
    // This provider serves both list items (virtualized and eager) and radio
    // buttons. The bridge resolves list items to (list, index) — for anything it
    // doesn't recognize as a list item, we fall back to the radio-button path.

    public int Select()
    {
        if (!bridge.TrySelectItemOnly(elementId)) bridge.SelectElement(elementId);
        return 0;
    }

    public int AddToSelection()
    {
        if (!bridge.TrySetItemSelected(elementId, true)) bridge.SelectElement(elementId);
        return 0;
    }

    public int RemoveFromSelection()
    {
        bridge.TrySetItemSelected(elementId, false); // no-op for radios / single-select
        return 0;
    }

    public int GetIsSelected(out int pRetVal)
    {
        // A list item's selection comes from its source; a radio is selected when
        // it's checked.
        bool selected = bridge.TryGetItemSelected(elementId, out bool s)
            ? s
            : bridge.GetToggleState(elementId) == true;
        pRetVal = selected ? -1 : 0; // VARIANT_BOOL
        return 0;
    }

    public int GetSelectionContainer(out IntPtr pRetVal)
    {
        // A list item's selection container is its list; a radio's is implicit.
        pRetVal = bridge.ItemSelectionContainer(elementId);
        return 0;
    }
}

/// <summary>
/// Container-level Selection for a list. Reports every selected item as an array
/// (empty when nothing is selected) and advertises whether the list is
/// multi-select. Complements the per-item <see cref="UiaSelectionItemProvider"/>.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class UiaSelectionProvider(WindowsAccessibilityBridge bridge, ElementId elementId) : INativeSelection
{
    public int GetSelection(out IntPtr pRetVal)
    {
        var indices = bridge.GetSelectedIndices(elementId);
        if (indices.Count == 0)
        {
            pRetVal = UiaInterop.BuildProviderArray(ReadOnlySpan<IntPtr>.Empty);
            return 0;
        }

        // Materialize a provider per selected item; skip any that don't resolve so
        // the array never carries null slots (UIA would Release them).
        Span<IntPtr> providers = indices.Count <= 16 ? stackalloc IntPtr[indices.Count] : new IntPtr[indices.Count];
        int n = 0;
        for (int i = 0; i < indices.Count; i++)
        {
            IntPtr p = bridge.ItemSimpleProvider(elementId, indices[i]);
            if (p != IntPtr.Zero) providers[n++] = p;
        }
        pRetVal = UiaInterop.BuildProviderArray(providers[..n]);
        return 0;
    }

    public int GetCanSelectMultiple(out int pRetVal)
    { pRetVal = bridge.GetCanSelectMultiple(elementId) ? -1 : 0; return 0; }

    public int GetIsSelectionRequired(out int pRetVal) { pRetVal = 0; return 0; }
}

/// <summary>
/// ItemContainer for a list: locates an item by property (name / selection)
/// without the client walking every sibling — the win for large virtualized
/// lists, where the match is found from item data and only that one item is
/// realized as a provider.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class UiaItemContainerProvider(WindowsAccessibilityBridge bridge, ElementId elementId) : INativeItemContainer
{
    public int FindItemByProperty(IntPtr pStartAfter, int propertyId, UiaVariant value, out IntPtr pFound)
    {
        int start = bridge.DecodeItemIndex(elementId, pStartAfter);
        pFound = bridge.FindItemByProperty(elementId, start, propertyId, value.AsString());
        return 0;
    }
}

/// <summary>
/// ExpandCollapse for a tree item / expander. State comes from the node snapshot;
/// Expand/Collapse reuse the element's click-to-toggle (via Invoke), acting only
/// when the current state actually needs to change.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class UiaExpandCollapseProvider(WindowsAccessibilityBridge bridge, ElementId elementId)
    : INativeExpandCollapse
{
    public int Expand()
    {
        if (bridge.GetExpanded(elementId) == false) bridge.InvokeElement(elementId);
        return 0;
    }

    public int Collapse()
    {
        if (bridge.GetExpanded(elementId) == true) bridge.InvokeElement(elementId);
        return 0;
    }

    public int GetExpandCollapseState(out int pRetVal)
    {
        pRetVal = bridge.GetExpanded(elementId) switch
        {
            true => UiaInterop.ExpandCollapseState_Expanded,
            false => UiaInterop.ExpandCollapseState_Collapsed,
            _ => UiaInterop.ExpandCollapseState_LeafNode,
        };
        return 0;
    }
}

/// <summary>
/// Scroll pattern for a scroll view / virtualized list. State is read live from
/// the element; Scroll and SetScrollPercent marshal onto the UI thread. The
/// BOOL out-params use VARIANT_BOOL (-1 = true).
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class UiaScrollProvider(WindowsAccessibilityBridge bridge, ElementId elementId) : INativeScroll
{
    public int Scroll(int horizontalAmount, int verticalAmount)
    {
        bridge.ScrollByAmount(elementId, horizontalAmount, verticalAmount);
        return 0;
    }

    public int SetScrollPercent(double horizontalPercent, double verticalPercent)
    {
        bridge.SetScrollPercent(elementId, horizontalPercent, verticalPercent);
        return 0;
    }

    public int GetHorizontalScrollPercent(out double pRetVal)
    { pRetVal = bridge.TryGetScrollInfo(elementId, out var i) ? i.HorizontalPercent : -1; return 0; }

    public int GetVerticalScrollPercent(out double pRetVal)
    { pRetVal = bridge.TryGetScrollInfo(elementId, out var i) ? i.VerticalPercent : -1; return 0; }

    public int GetHorizontalViewSize(out double pRetVal)
    { pRetVal = bridge.TryGetScrollInfo(elementId, out var i) ? i.HorizontalViewSize : 100; return 0; }

    public int GetVerticalViewSize(out double pRetVal)
    { pRetVal = bridge.TryGetScrollInfo(elementId, out var i) ? i.VerticalViewSize : 100; return 0; }

    public int GetHorizontallyScrollable(out int pRetVal)
    { pRetVal = bridge.TryGetScrollInfo(elementId, out var i) && i.HorizontallyScrollable ? -1 : 0; return 0; }

    public int GetVerticallyScrollable(out int pRetVal)
    { pRetVal = bridge.TryGetScrollInfo(elementId, out var i) && i.VerticallyScrollable ? -1 : 0; return 0; }
}

/// <summary>
/// RangeValue for a slider. State is read live from the element; SetValue
/// marshals onto the UI thread. BOOL out-params use VARIANT_BOOL (-1 = true).
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class UiaRangeValueProvider(WindowsAccessibilityBridge bridge, ElementId elementId) : INativeRangeValue
{
    public int SetValue(double value) { bridge.SetRangeValue(elementId, value); return 0; }
    public int GetValue(out double pRetVal)
    { pRetVal = bridge.TryGetRangeValue(elementId, out var i) ? i.Value : 0; return 0; }
    public int GetIsReadOnly(out int pRetVal)
    { pRetVal = bridge.TryGetRangeValue(elementId, out var i) && i.IsReadOnly ? -1 : 0; return 0; }
    public int GetMaximum(out double pRetVal)
    { pRetVal = bridge.TryGetRangeValue(elementId, out var i) ? i.Maximum : 0; return 0; }
    public int GetMinimum(out double pRetVal)
    { pRetVal = bridge.TryGetRangeValue(elementId, out var i) ? i.Minimum : 0; return 0; }
    public int GetLargeChange(out double pRetVal)
    { pRetVal = bridge.TryGetRangeValue(elementId, out var i) ? i.LargeChange : 0; return 0; }
    public int GetSmallChange(out double pRetVal)
    { pRetVal = bridge.TryGetRangeValue(elementId, out var i) ? i.SmallChange : 0; return 0; }
}
