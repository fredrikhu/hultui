using System.Runtime.Versioning;
using HultUI.Core.Elements;

namespace HultUI.Platform.Accessibility.Windows;

/// <summary>
/// Pattern providers look up their target element via the bridge each time a
/// UIA client invokes them, so they don't become stale across tree rebuilds.
/// The actual element mutation routes through the same event/handler path as
/// pointer clicks — this keeps behavior identical whether a human or an AT
/// triggered it.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class UiaInvokeProvider(WindowsAccessibilityBridge bridge, ElementId elementId) : IInvokeProvider
{
    public int Invoke()
    {
        bridge.InvokeElement(elementId);
        return 0;
    }
}

[SupportedOSPlatform("windows")]
internal sealed class UiaToggleProvider(WindowsAccessibilityBridge bridge, ElementId elementId) : IToggleProvider
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
internal sealed class UiaValueProvider(WindowsAccessibilityBridge bridge, ElementId elementId) : IValueProvider
{
    public int SetValue(string val)
    {
        bridge.SetValue(elementId, val);
        return 0;
    }

    public int GetValue(out string pRetVal)
    {
        pRetVal = bridge.GetValue(elementId) ?? "";
        return 0;
    }

    public int GetIsReadOnly(out bool pRetVal)
    {
        pRetVal = false;
        return 0;
    }
}

[SupportedOSPlatform("windows")]
internal sealed class UiaSelectionItemProvider(WindowsAccessibilityBridge bridge, ElementId elementId)
    : ISelectionItemProvider
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

    public int RemoveFromSelection()
    {
        // Radio buttons can't be unselected this way — S_OK, no-op.
        return 0;
    }

    public int GetIsSelected(out bool pRetVal)
    {
        pRetVal = bridge.GetToggleState(elementId) == true;
        return 0;
    }

    public int GetSelectionContainer(out IRawElementProviderSimple? pRetVal)
    {
        pRetVal = null;
        return 0;
    }
}
