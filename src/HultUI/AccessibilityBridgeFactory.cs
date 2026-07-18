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
        // Only real OS windows have an accessibility surface to bridge to.
        // Headless platform windows stay on the Null bridge everywhere — on
        // Linux this also avoids probing the AT-SPI D-Bus session, which a
        // display-less CI environment doesn't have.
        if (window.PlatformWindow is not SDL3Window)
            return NullAccessibilityBridge.Instance;

        if (OperatingSystem.IsWindows())
            return CreateWindows(window);

        if (OperatingSystem.IsLinux())
            return CreateLinux();

        return NullAccessibilityBridge.Instance;
    }

    [SupportedOSPlatform("windows")]
    private static IAccessibilityBridge CreateWindows(Window window)
    {
        var hwnd = ((SDL3Window)window.PlatformWindow).GetWin32Hwnd();
        if (hwnd == IntPtr.Zero) return NullAccessibilityBridge.Instance;

        var host = new Sdl3UiaHostBridge(window);
        window.SetUiaHostBridge(host);
        return new WindowsAccessibilityBridge(hwnd, host);
    }

    [SupportedOSPlatform("linux")]
    private static IAccessibilityBridge CreateLinux() =>
        new LinuxAccessibilityBridge();
}
