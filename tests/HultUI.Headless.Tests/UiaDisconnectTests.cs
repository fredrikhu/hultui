using System.Runtime.Versioning;
using HultUI.Core.Builders;
using HultUI.Platform.Accessibility.Windows;
using Xunit;
using TextB = HultUI.Core.Builders.Text;

namespace HultUI.Headless.Tests;

/// <summary>
/// A closed window's UIA bridge tells UI Automation its providers are gone. UI Automation keeps the
/// root provider of a window a client asked about, and the root reaches the bridge, its host and
/// the window's whole tree: with this machine's UIA client listening, every closed 60-row window
/// kept ~357 KB, about 100 of them at a time (plan 17, round 3). What UI Automation then lets go of
/// shows only with a real client, out of process; the commit has those numbers. Here: that the
/// bridge says it, once, for its window, when the window closes.
/// </summary>
[SupportedOSPlatform("windows")]
public class UiaDisconnectTests
{
    [WindowsFact]
    public void AClosingWindow_TellsUIAutomationItsProvidersAreGone_Once()
    {
        using var hwnd = TestHwnd.Create();
        using var session = HeadlessSession.Create(300, 200);
        session.Window.SetRoot(Flex.Column().Children(TextB.New("a row"), TextB.New("another")));
        var window = session.Window.Window;
        var router = new AccessibilityActionRouter(window);
        var bridge = new WindowsAccessibilityBridge(hwnd.Handle, new UiaHostBridge(window, router));
        var released = new List<IntPtr>();
        bridge.ProvidersReleased = released.Add;
        session.Window.InstallAccessibilityBridge(bridge);
        session.PumpFrame();
        Assert.Empty(released);

        window.Dispose();
        Assert.Equal([hwnd.Handle], released);

        // Once: a second dispose says nothing more.
        bridge.Dispose();
        Assert.Equal([hwnd.Handle], released);
    }
}
