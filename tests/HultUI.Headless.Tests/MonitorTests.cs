using HultUI.Core.Builders;
using HultUI.Core.Platform;
using Xunit;

namespace HultUI.Headless.Tests;

/// <summary>
/// Simulated monitors: a headless session given <see cref="HeadlessOptions.Monitors"/> has a
/// desktop, so an app's multi-monitor wiring — listing monitors, putting a window on one,
/// maximized or full screen, and surviving a monitor being unplugged — runs and renders on a
/// machine with one screen.
/// </summary>
public sealed class MonitorTests
{
    private static HeadlessSession Desk(params (int Width, int Height)[] sizes) =>
        HeadlessSession.Create(width: 800, height: 600,
            options: new HeadlessOptions { Monitors = HeadlessMonitors.Row(sizes) });

    [Fact]
    public void WithoutMonitors_NoneAreListed_AndTheWindowIsOnNone()
    {
        using var session = HeadlessSession.Create(width: 400, height: 300);

        Assert.Empty(session.Application.Monitors);
        Assert.Null(session.Window.Window.Monitor);
    }

    [Fact]
    public void ARow_StandsSideBySide_WithTheFirstPrimary_AndARotatedOnePortrait()
    {
        using var session = Desk((1920, 1080), (2560, 1440), (1440, 2560));

        var monitors = session.Application.Monitors;
        Assert.Equal(3, monitors.Count);
        Assert.True(monitors[0].IsPrimary);
        Assert.False(monitors[1].IsPrimary);
        Assert.Equal(1920, monitors[1].Bounds.X);
        Assert.Equal(1920 + 2560, monitors[2].Bounds.X);
        Assert.Equal(MonitorOrientation.Landscape, monitors[1].Orientation);
        Assert.Equal(MonitorOrientation.Portrait, monitors[2].Orientation);
        Assert.Equal(96, monitors[0].Dpi);
    }

    [Fact]
    public void MoveToMonitor_Maximized_FillsItsWorkArea()
    {
        using var session = HeadlessSession.Create(width: 800, height: 600,
            options: new HeadlessOptions { Monitors = HeadlessMonitors.Row(taskBar: 48, (1920, 1080), (2560, 1440)) });
        var window = session.Window.Window;
        window.SetRoot(Box.New());

        window.MoveToMonitor(session.Application.Monitors[0]);
        session.PumpFrame();
        Assert.Equal(1920, window.LayoutWidth);
        Assert.Equal(1080 - 48, window.LayoutHeight);
        Assert.True(window.IsMaximized);

        window.MoveToMonitor(session.Application.Monitors[1]);
        session.PumpFrame();
        Assert.Equal("DISPLAY2", window.Monitor?.Id);
        Assert.Equal(2560, window.LayoutWidth);
        Assert.Equal(1440, window.LayoutHeight);
    }

    [Fact]
    public void MoveToMonitor_Fullscreen_CoversThePortraitMonitor_AndCapturesAtItsSize()
    {
        using var session = Desk((1920, 1080), (1080, 1920));
        var window = session.Window.Window;
        window.SetRoot(Box.New());

        window.MoveToMonitor(session.Application.Monitors[1], MonitorFit.Fullscreen);
        using var image = session.Window.CapturePixels();

        Assert.True(window.IsFullscreen);
        Assert.Equal(1080, image.Width);
        Assert.Equal(1920, image.Height);
    }

    [Fact]
    public void MoveToMonitor_Centered_KeepsTheSize()
    {
        using var session = Desk((1920, 1080), (2560, 1440));
        var window = session.Window.Window;

        window.MoveToMonitor(session.Application.Monitors[1], MonitorFit.Centered);

        Assert.False(window.IsMaximized);
        Assert.Equal(new WindowPlacement(1920 + (2560 - 800) / 2, (1440 - 600) / 2, 800, 600, false), window.Placement);
        Assert.Equal(800, window.LayoutWidth);
    }

    [Fact]
    public void AScaledMonitor_LaysOutInFewerLayoutUnits()
    {
        var monitors = new[]
        {
            HeadlessMonitors.Create("A", 0, 0, 1920, 1080, primary: true),
            HeadlessMonitors.Create("B", 1920, 0, 3840, 2160, scale: 1.5f),
        };
        using var session = HeadlessSession.Create(options: new HeadlessOptions { Monitors = monitors });
        var window = session.Window.Window;

        window.MoveToMonitor(monitors[1]);

        Assert.Equal(1.5f, window.DisplayScale);
        Assert.Equal(2560, window.LayoutWidth);
        Assert.Equal(1440, window.LayoutHeight);
        Assert.Equal(144, monitors[1].Dpi);
    }

    [Fact]
    public void UnpluggingAMonitor_BringsItsWindowOntoOneThatIsLeft_AndAnnouncesTheChange()
    {
        using var session = Desk((1920, 1080), (2560, 1440), (2560, 1440));
        var second = session.CreateWindow("Images 2");
        second.Window.SetRoot(Box.New());
        second.Window.MoveToMonitor(session.Application.Monitors[2]);
        int changes = 0;
        session.Application.MonitorsChanged += () => changes++;

        session.SetMonitors(HeadlessMonitors.Row((1920, 1080), (2560, 1440)));
        Assert.Equal(0, changes);   // announced from the event pump, not from inside SetMonitors
        session.PumpFrame();

        Assert.Equal(1, changes);
        Assert.Equal(2, session.Application.Monitors.Count);
        var on = second.Window.Monitor;
        Assert.NotNull(on);
        Assert.Contains(on, session.Application.Monitors);
        var p = second.Window.Placement!;
        Assert.InRange(p.X, 0, 1920 + 2560 - p.Width);
        Assert.True(second.Window.IsMaximized);
        Assert.Equal(on.WorkArea.Width, second.Window.LayoutWidth);
    }

    [Fact]
    public void For_PicksTheMonitorOverlappedMost_ElseTheNearest()
    {
        var monitors = HeadlessMonitors.Row((1000, 1000), (1000, 1000));

        Assert.Equal("DISPLAY2", MonitorInfo.For(new(900, 0, 400, 400), monitors)!.Id);
        Assert.Equal("DISPLAY1", MonitorInfo.For(new(-5000, 10, 100, 100), monitors)!.Id);
        Assert.Equal("DISPLAY2", MonitorInfo.For(new(9000, 10, 100, 100), monitors)!.Id);
        Assert.Null(MonitorInfo.For(new(0, 0, 10, 10), []));
    }

    [Fact]
    public void InReadingOrder_IsLeftToRight()
    {
        var row = HeadlessMonitors.Row((1000, 1000), (800, 600), (1000, 1000));
        var shuffled = new[] { row[2], row[0], row[1] };

        Assert.Equal(["DISPLAY1", "DISPLAY2", "DISPLAY3"], MonitorInfo.InReadingOrder(shuffled).Select(m => m.Id));
    }
}
