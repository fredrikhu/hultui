using System.Runtime.Versioning;
using HultUI.Core.Accessibility;
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

        // Only real OS windows have an accessibility surface to bridge to.
        // Headless platform windows stay on the Null bridge everywhere — on
        // Linux this also avoids probing the AT-SPI D-Bus session, which a
        // display-less CI environment doesn't have.
        if (window.PlatformWindow is not SDL3Window)
            return NullAccessibilityBridge.Instance;

        // Shared across platforms: it marshals AT-triggered actions onto the UI
        // thread's per-frame drain. The active bridge triggers actions through it.
        var router = new AccessibilityActionRouter(window);

        if (OperatingSystem.IsWindows())
            return CreateWindows(window, router);

        if (OperatingSystem.IsLinux())
            return CreateLinux(window, router);

        return NullAccessibilityBridge.Instance;
    }

    [SupportedOSPlatform("windows")]
    private static IAccessibilityBridge CreateWindows(Window window, AccessibilityActionRouter router)
    {
        var hwnd = ((SDL3Window)window.PlatformWindow).GetWin32Hwnd();
        if (hwnd == IntPtr.Zero) return NullAccessibilityBridge.Instance;

        var host = new Sdl3UiaHostBridge(window, router);
        window.SetUiaHostBridge(host);
        window.SetActionRouter(router);
        return new WindowsAccessibilityBridge(hwnd, host);
    }

    [SupportedOSPlatform("linux")]
    private static IAccessibilityBridge CreateLinux(Window window, AccessibilityActionRouter router)
    {
        window.SetActionRouter(router);
        return new LinuxAccessibilityBridge(router);
    }
}
