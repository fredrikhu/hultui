using System.Runtime.Versioning;

namespace HultUI.Platform.Accessibility.Windows;

/// <summary>
/// A provider that says nothing itself and stands for a window: UI Automation resolves it to
/// that window's element, and asks the window's own providers everything. It exists so a hit
/// test can answer "the thing at this point is in that window" when the window cannot be found
/// by position — a browser engine's windows under a composited page are not on screen.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class UiaWindowStandIn(IntPtr window) : INativeSimple, INativeFragment, INativeFragmentRoot
{
    public int GetProviderOptions(out int pRetVal)
    {
        pRetVal = UiaInterop.ProviderOptions_ServerSideProvider | UiaInterop.ProviderOptions_OverrideProvider;
        return 0;
    }

    public int GetPatternProvider(int patternId, out IntPtr pPatternProvider) { pPatternProvider = IntPtr.Zero; return 0; }

    public int GetPropertyValue(int propertyId, out UiaVariant pRetVal) { pRetVal = UiaVariant.Empty; return 0; }

    public int GetHostRawElementProvider(out IntPtr pRetVal)
    {
        pRetVal = IntPtr.Zero;
        if (UiaInterop.UiaHostProviderFromHwnd(window, out var host) >= 0) pRetVal = host;
        return 0;
    }

    public int Navigate(int direction, out IntPtr pRetVal) { pRetVal = IntPtr.Zero; return 0; }
    public int GetRuntimeId(out IntPtr pRetVal) { pRetVal = IntPtr.Zero; return 0; }
    public int GetBoundingRectangle(out UiaRect pRetVal) { pRetVal = default; return 0; }
    public int GetEmbeddedFragmentRoots(out IntPtr pRetVal) { pRetVal = IntPtr.Zero; return 0; }
    public int SetFocus() => 0;
    public int GetFragmentRoot(out IntPtr pRetVal) { pRetVal = IntPtr.Zero; return 0; }

    public int ElementProviderFromPoint(double x, double y, out IntPtr pRetVal) { pRetVal = IntPtr.Zero; return 0; }
    public int GetFocus(out IntPtr pRetVal) { pRetVal = IntPtr.Zero; return 0; }
}
