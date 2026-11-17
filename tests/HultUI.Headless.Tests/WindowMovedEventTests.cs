using HultUI.Core.Builders;
using HultUI.Core.Platform;
using Xunit;

namespace HultUI.Headless.Tests;

/// <summary>
/// <see cref="Window.Moved"/> and <see cref="Window.DisplayChanged"/>: a window dragged to another
/// display went unnoticed, and the app had to poll <see cref="Window.Placement"/> (finding 17).
/// The Windows-like frame's moves are in <see cref="WindowsFrameTests"/>.
/// </summary>
public class WindowMovedEventTests
{
    private static readonly MonitorInfo[] Desk =
    [
        HeadlessMonitors.Create("A", 0, 0, 1920, 1080, primary: true),
        HeadlessMonitors.Create("B", 1920, 0, 2560, 1440),
    ];

    [Fact]
    public void APlacedWindow_TellsEachMoveOnce_AndTheDisplayItWentTo()
    {
        using var session = HeadlessSession.Create(800, 600, options: new HeadlessOptions { Monitors = Desk });
        session.Window.SetRoot(Box.New());
        var window = session.Window.Window;
        session.PumpFrame();
        int moved = 0, display = 0;
        string? monitorTold = null;
        window.Moved += () => moved++;
        window.DisplayChanged += () => { display++; monitorTold = window.Monitor?.Name; };

        window.Placement = new WindowPlacement(100, 100, 800, 600, false);
        Assert.Equal((0, 0), (moved, display));   // told by the frame, not by the setter
        session.PumpFrame();
        Assert.Equal((1, 0), (moved, display));

        window.Placement = new WindowPlacement(100, 100, 800, 600, false);
        session.PumpFrame();
        Assert.Equal((1, 0), (moved, display));   // the same place is no move

        window.MoveToMonitor(Desk[1], MonitorFit.Centered);
        session.PumpFrame();
        Assert.Equal((2, 1), (moved, display));
        Assert.Equal("B", monitorTold);

        session.PumpFrame();
        Assert.Equal((2, 1), (moved, display));
    }

    [Fact]
    public void WithNoMonitors_AMoveIsTold_AndNoDisplayChange()
    {
        using var session = HeadlessSession.Create(800, 600);
        session.Window.SetRoot(Box.New());
        var window = session.Window.Window;
        session.PumpFrame();
        int moved = 0, display = 0;
        window.Moved += () => moved++;
        window.DisplayChanged += () => display++;

        window.Placement = new WindowPlacement(50, 60, 800, 600, false);
        session.PumpFrame();
        Assert.Equal((1, 0), (moved, display));
    }
}
