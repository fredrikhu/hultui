using System.Runtime.InteropServices;
using HultUI.Core.Builders;
using HultUI.Core.Primitives;
using HultUI.Platform.SDL3;
using Xunit;

namespace HultUI.UiTests;

/// <summary>
/// <see cref="Window.Owner"/> and <see cref="Window.ShowWithoutActivation"/> on real windows.
/// The owner is Windows' own (<c>GW_OWNER</c>), which is what keeps the window above its owner
/// and hides it while its owner is minimized. A window shown without activation asks for none:
/// the framework refuses it, so the guard further down the chain sees nothing, where a window
/// shown the ordinary way, without the hint, asks and is refused by the guard.
/// </summary>
[Collection("ui")] // one Application per process, and SDL is process-global
public sealed class OwnedWindowNativeTests
{
    [UiFact]
    public void TheOwner_IsWindowsOwnWindowOwner()
    {
        using var guard = ForegroundGuard.Begin();
        using var app = new Application(new SdlWindowingPlatform(), "owner", 400, 300);
        app.SetRoot(Box.New().Background(new Color(40, 80, 120)).FlexGrow(1));
        var tool = app.CreateWindow("owned", 200, 150);
        tool.SetRoot(Box.New().Background(new Color(120, 80, 40)).FlexGrow(1));

        tool.Owner = app.PrimaryWindow;
        app.RunFrame(TimeSpan.FromMilliseconds(16));

        Assert.Equal(app.PrimaryWindow.NativeHandle, GetWindow(tool.NativeHandle, GW_OWNER));

        tool.Owner = null;
        Assert.Equal(0, GetWindow(tool.NativeHandle, GW_OWNER));
    }

    [UiTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void ShowWithoutActivation_AsksForNoActivation_WithoutTheHint(bool withoutActivation)
    {
        using var guard = ForegroundGuard.BeginWithoutTheHint();
        using var app = new Application(new SdlWindowingPlatform(), "main", 400, 300);
        app.SetRoot(Box.New().Background(new Color(40, 80, 120)).FlexGrow(1));
        app.RunFrame(TimeSpan.FromMilliseconds(16));

        var second = app.CreateWindow("second", 200, 150);
        second.SetRoot(Box.New().Background(new Color(120, 80, 40)).FlexGrow(1));
        second.ShowWithoutActivation = withoutActivation;
        int before = ForegroundGuard.Refused;
        app.RunFrame(TimeSpan.FromMilliseconds(16));   // its first frame shows it

        Assert.True(IsWindowVisible(second.NativeHandle));
        int asked = ForegroundGuard.Refused - before;
        if (withoutActivation) Assert.True(asked == 0, $"{asked} activation(s) reached the guard");
        // The control: without it the same show asks, or the test proves nothing.
        else Assert.True(asked > 0, "an ordinary show asked for no activation");
    }

    private const uint GW_OWNER = 4;
    [DllImport("user32.dll")] private static extern nint GetWindow(nint hWnd, uint cmd);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool IsWindowVisible(nint hWnd);
}
