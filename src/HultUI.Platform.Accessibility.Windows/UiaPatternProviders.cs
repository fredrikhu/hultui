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
internal sealed class UiaValueProvider(WindowsAccessibilityBridge bridge, ElementId elementId) : INativeValue
{
    public int SetValue(IntPtr pwsz)
    {
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
        pRetVal = 0; // VARIANT_FALSE
        return 0;
    }
}

[SupportedOSPlatform("windows")]
internal sealed class UiaSelectionItemProvider(WindowsAccessibilityBridge bridge, ElementId elementId)
    : INativeSelectionItem
{
    public int Select()
    {
        bridge.SelectElement(elementId);
        return 0;
    }

    public int AddToSelection()
    {
        bridge.SelectElement(elementId);
        return 0;
    }

    public int RemoveFromSelection() => 0; // single-select: can't be deselected this way

    public int GetIsSelected(out int pRetVal)
    {
        // A list item is selected when it's the list's selected index; a radio
        // button is selected when it's checked.
        bool selected = VirtualItemId.TryDecode(elementId, out var listId, out var index)
            ? bridge.GetVirtualItemSelected(listId, index)
            : bridge.GetToggleState(elementId) == true;
        pRetVal = selected ? -1 : 0; // VARIANT_BOOL
        return 0;
    }

    public int GetSelectionContainer(out IntPtr pRetVal)
    {
        // A list item's selection container is its list; a radio's is implicit.
        pRetVal = VirtualItemId.TryDecode(elementId, out var listId, out _)
            ? bridge.NativeFragmentSimple(listId)
            : IntPtr.Zero;
        return 0;
    }
}
