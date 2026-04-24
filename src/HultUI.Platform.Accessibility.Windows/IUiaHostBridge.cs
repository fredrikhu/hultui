using System.Runtime.Versioning;
using HultUI.Core.Elements;

namespace HultUI.Platform.Accessibility.Windows;

/// <summary>
/// Callback delegate signature for Win32 messages forwarded from the host's
/// window-message hook. Return <c>true</c> if the message was handled — the
/// host should then short-circuit its own dispatch.
/// </summary>
public delegate bool UiaMessageHandler(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);

/// <summary>
/// Collaboration surface between <see cref="WindowsAccessibilityBridge"/> and
/// the hosting application. The host knows how to route element actions back
/// onto the UI thread and how to read element state without cross-thread
/// footguns; the bridge knows UIA.
/// </summary>
[SupportedOSPlatform("windows")]
public interface IUiaHostBridge
{
    /// <summary>Subscribe to raw Win32 messages on the window (SDL3 exposes this).</summary>
    void InstallMessageHook(UiaMessageHandler handler);
    void UninstallMessageHook();

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
