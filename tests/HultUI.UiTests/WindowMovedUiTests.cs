using HultUI.Core.Builders;
using HultUI.Core.Platform;
using HultUI.Core.Styling;
using HultUI.Platform.SDL3;
using Xunit;

namespace HultUI.UiTests;

/// <summary>
/// SDL's window-moved and display-changed events reach <see cref="Window.Moved"/> and
/// <see cref="Window.DisplayChanged"/> on a real window. The headless model of both is in
/// HultUI.Headless.Tests (WindowMovedEventTests, WindowsFrameTests); this checks the wiring.
/// </summary>
[Collection("ui")] // one Application per process, and SDL is process-global
public sealed class WindowMovedUiTests
{
    [UiFact]
    public void APlacementSomewhereElse_IsToldAsAMove()
    {
        using var guard = ForegroundGuard.Begin();
        var home = Monitors.WorkAreas()[0];
        int x = (int)home.X, y = (int)home.Y;
        using var app = new Application(new SdlWindowingPlatform(), "moved", 640, 480, WindowStyle.Default);
        app.SetRoot(Box.New().FlexGrow(1));
        var window = app.PrimaryWindow;
        window.Placement = new WindowPlacement(x + 100, y + 100, 640, 480, false);
        for (int i = 0; i < 3; i++) app.RunFrame(TimeSpan.FromMilliseconds(16));
        int moved = 0, display = 0;
        window.Moved += () => moved++;
        window.DisplayChanged += () => display++;

        window.Placement = new WindowPlacement(x + 200, y + 150, 640, 480, false);
        Assert.Equal(0, moved);   // told by the frame, not by the setter
        for (int i = 0; i < 3; i++) app.RunFrame(TimeSpan.FromMilliseconds(16));
        Assert.True(moved >= 1, "no Moved for a placement 100 px to the right");
        Assert.Equal(0, display);
    }

    /// <summary>
    /// Skipped on one monitor: it returned early there, inside the test above, and reported the
    /// display change as checked when no window had left its monitor.
    /// </summary>
    [UiMultiMonitorFact]
    public void APlacementOnAnotherMonitor_IsToldAsADisplayChange()
    {
        using var guard = ForegroundGuard.Begin();
        var areas = Monitors.WorkAreas();
        var (home, other) = (areas[0], areas[1]);
        using var app = new Application(new SdlWindowingPlatform(), "moved", 640, 480, WindowStyle.Default);
        app.SetRoot(Box.New().FlexGrow(1));
        var window = app.PrimaryWindow;
        window.Placement = new WindowPlacement((int)home.X + 100, (int)home.Y + 100, 640, 480, false);
        for (int i = 0; i < 3; i++) app.RunFrame(TimeSpan.FromMilliseconds(16));
        int display = 0;
        window.DisplayChanged += () => display++;

        window.Placement = new WindowPlacement((int)other.X + 100, (int)other.Y + 100, 640, 480, false);
        for (int i = 0; i < 3; i++) app.RunFrame(TimeSpan.FromMilliseconds(16));
        Assert.Equal(1, display);
    }
}
