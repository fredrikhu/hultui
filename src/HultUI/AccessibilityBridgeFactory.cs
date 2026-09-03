using System.Runtime.Versioning;
using HultUI.Core.Accessibility;
using HultUI.Core.Platform;
using HultUI.Platform.Accessibility.Linux;
using HultUI.Platform.Accessibility.Windows;
using HultUI.Platform.SDL3;

namespace HultUI;

/// <summary>
/// Picks the right <see cref="IAccessibilityBridge"/> for the current
/// operating system. The returned bridge stays inactive
/// (<see cref="IAccessibilityBridge.IsActive"/> returns false) until an
/// assistive technology actually probes the app, so the cost on machines
/// without AT attached is a single OS check at window creation.
/// </summary>
internal static class AccessibilityBridgeFactory
{
    public static IAccessibilityBridge Create(Window window)
    {
        // Master opt-out, honored on every platform: skip the accessibility
        // bridge entirely. Lets a benchmark equalize against a machine with no
        // assistive tech attached (on Linux the AT-SPI bridge otherwise
        // eager-connects whenever an a11y bus is reachable — e.g. any live
        // desktop session), and is an escape hatch if a bridge misbehaves.
        // Windows also has the finer HULTUI_UIA_EVENTS=0, which keeps the tree
        // exported but suppresses only the live event notifications.
        if (Environment.GetEnvironmentVariable("HULTUI_A11Y") == "0")
            return NullAccessibilityBridge.Instance;

        if (OperatingSystem.IsWindows())
            return CreateWindows(window);

        // Linux keeps a concrete-type test where Windows no longer needs one.
        // AT-SPI has no handle-shaped seam to key on — the bridge registers the
        // application on the D-Bus session rather than attaching to a window — so
        // there is nothing for a backend to implement, and "is this a real desktop
        // window" is still the question being asked. Headless platform windows stay
        // on the Null bridge, which also avoids probing a bus a display-less CI
        // environment does not have. Revisit if a second Linux backend ever exists.
        if (OperatingSystem.IsLinux() && window.PlatformWindow is SDL3Window)
            return CreateLinux(window);

        return NullAccessibilityBridge.Instance;
    }

    /// <summary>
    /// The UIA bridge needs an HWND and nothing else — it subclasses that window's
    /// procedure to answer <c>WM_GETOBJECT</c>. Keying on
    /// <see cref="IWin32WindowHandle"/> rather than on the SDL backend is what lets a
    /// HultUI surface hosted in someone else's window (a WinForms or WPF control) be
    /// read by a screen reader; a backend with no handle — headless — falls through to
    /// the Null bridge exactly as before.
    /// </summary>
    [SupportedOSPlatform("windows")]
    private static IAccessibilityBridge CreateWindows(Window window)
    {
        if (window.PlatformWindow is not IWin32WindowHandle handleSource)
            return NullAccessibilityBridge.Instance;

        var hwnd = handleSource.Hwnd;
        if (hwnd == IntPtr.Zero) return NullAccessibilityBridge.Instance;

        // Marshals AT-triggered actions onto the UI thread's per-frame drain.
        var router = new AccessibilityActionRouter(window);
        var host = new UiaHostBridge(window, router);
        window.SetUiaHostBridge(host);
        window.SetActionRouter(router);
        return new WindowsAccessibilityBridge(hwnd, host);
    }

    [SupportedOSPlatform("linux")]
    private static IAccessibilityBridge CreateLinux(Window window)
    {
        var router = new AccessibilityActionRouter(window);
        window.SetActionRouter(router);
        return new LinuxAccessibilityBridge(router);
    }
}
