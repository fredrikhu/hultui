using System.Runtime.Versioning;
using HultUI.Core.Elements;

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

    // Reads from UIA. Should be safe to call from worker threads — hosts
    // either snapshot or marshal internally.
    string? GetValue(ElementId id);
    bool? GetToggleState(ElementId id);
}
