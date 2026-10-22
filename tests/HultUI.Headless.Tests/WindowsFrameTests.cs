using HultUI.Core.Builders;
using HultUI.Core.Platform;
using HultUI.Core.Primitives;
using HultUI.Core.Styling;
using Xunit;

namespace HultUI.Headless.Tests;

/// <summary>
/// Windows-like windows (<see cref="HeadlessOptions.WindowsFrame"/>): what the SDL3 window does on
/// Windows — <c>WindowsPlacement</c>'s outer rectangles, the custom frame's invisible side and
/// bottom borders and its posted <c>WM_SYSCOMMAND</c> maximize and restore,
/// <c>SDL3Window.PlaceWin32</c>'s order, per-monitor scale, and SDL's window-moved event — so
/// placement code that passed headless and broke on real monitors fails here.
/// </summary>
public sealed class WindowsFrameTests
{
    // A 100 % monitor with a 40 px task bar, and a 150 % one to its right.
    private static readonly MonitorInfo[] Desk =
    [
        HeadlessMonitors.Create("A", 0, 0, 1920, 1080, primary: true, taskBar: 40),
        HeadlessMonitors.Create("B", 1920, 0, 3840, 2160, scale: 1.5f),
    ];

    private static HeadlessSession Session(WindowStyle style = WindowStyle.Borderless)
    {
        var session = HeadlessSession.Create(width: 800, height: 600, style: style,
            options: new HeadlessOptions { Monitors = Desk, WindowsFrame = new() });
        session.Window.Window.SetRoot(Box.New());
        return session;
    }

    [Fact]
    public void APlacement_IsTheOuterRectangle_WithTheContentInsetByTheSideAndBottomBorders()
    {
        using var session = Session();
        var window = session.Window.Window;

        window.Placement = new WindowPlacement(100, 100, 800, 600, false);

        Assert.Equal(new WindowPlacement(100, 100, 800, 600, false), window.Placement);
        Assert.Equal(new Rect(108, 100, 784, 592), session.Window.ContentBounds);
        Assert.Equal(784, window.LayoutWidth);
        Assert.Equal(592, window.LayoutHeight);
    }

    [Fact]
    public void OnAMonitorAt150Percent_TheBordersAreThatScales_AndTheWindowTakesItsScale()
    {
        using var session = Session();
        var window = session.Window.Window;

        window.Placement = new WindowPlacement(2000, 100, 900, 600, false);

        Assert.Equal(1.5f, window.DisplayScale);
        Assert.Equal(new Rect(2011, 100, 878, 589), session.Window.ContentBounds);
        Assert.Equal(878 / 1.5f, window.LayoutWidth, 3);
    }

    [Fact]
    public void ATileGrownByItsBorders_ReachesPastTheWorkArea_AndIsFittedBackIn()
    {
        using var session = Session();
        var window = session.Window.Window;

        // The left half of the work area (960 x 1040) as an outer rectangle: 8 px more each side
        // and below. It reaches past the work area, so it is shrunk and slid in, as on Windows.
        window.Placement = new WindowPlacement(-8, 0, 976, 1048, false);

        Assert.Equal(new WindowPlacement(0, 0, 976, 1040, false), window.Placement);
        Assert.Equal(new Rect(8, 0, 960, 1032), session.Window.ContentBounds);
    }

    [Fact]
    public void MaximizeAndRestore_LandOnTheNextPump_AndMaximizedTheContentIsTheWorkArea()
    {
        using var session = Session();
        var window = session.Window.Window;
        window.Placement = new WindowPlacement(100, 100, 800, 600, false);
        session.PumpFrame();

        window.Maximize();
        Assert.False(window.IsMaximized);
        session.PumpFrame();

        Assert.True(window.IsMaximized);
        Assert.Equal(new Rect(0, 0, 1920, 1040), session.Window.ContentBounds);
        Assert.Equal(new WindowPlacement(100, 100, 800, 600, true), window.Placement);

        window.Restore();
        Assert.True(window.IsMaximized);
        session.PumpFrame();

        Assert.False(window.IsMaximized);
        Assert.Equal(new Rect(108, 100, 784, 592), session.Window.ContentBounds);
    }

    [Fact]
    public void APlacementOnAShownMaximizedWindow_IsUndoneByItsRestoreLandingAfterTheMove()
    {
        using var session = Session();
        var window = session.Window.Window;
        window.Placement = new WindowPlacement(100, 100, 800, 600, true);
        session.PumpFrame();
        Assert.True(window.IsMaximized);

        window.Placement = new WindowPlacement(952, 0, 976, 1040, false);
        Assert.True(window.IsMaximized);   // the restore is posted; the move is made at once
        session.PumpFrame();

        // The restore lands after the move and puts back the bounds from before the maximize.
        Assert.False(window.IsMaximized);
        Assert.Equal(new WindowPlacement(100, 100, 800, 600, false), window.Placement);
    }

    [Fact]
    public void BeforeTheFirstFrame_AMaximizedPlacementMaximizesAtOnce()
    {
        using var session = Session();
        var window = session.Window.Window;

        window.MoveToMonitor(Desk[1]);

        Assert.True(window.IsMaximized);
        Assert.Equal(Desk[1].WorkArea, session.Window.ContentBounds);
        Assert.Equal(1.5f, window.DisplayScale);
        Assert.Equal(2560, window.LayoutWidth);
    }

    [Fact]
    public void AMoveToAMonitorAtAnotherScale_RaisesResizedAndMovedFromThePump()
    {
        using var session = Session();
        var window = session.Window.Window;
        window.Placement = new WindowPlacement(100, 100, 800, 600, false);
        session.PumpFrame();
        int resized = 0, moved = 0;
        window.Resized += () => resized++;
        session.Window.Moved += () => moved++;

        window.Placement = new WindowPlacement(2000, 100, 800, 600, false);
        Assert.Equal(1.5f, window.DisplayScale);
        Assert.Equal((0, 0), (resized, moved));
        session.PumpFrame();

        Assert.Equal((1, 1), (resized, moved));
        session.PumpFrame();
        Assert.Equal((1, 1), (resized, moved));
    }

    [Fact]
    public void AMoveByTheUser_IsNotFitted_TakesAWindowOutOfMaximized_AndIsToldAsAMove()
    {
        using var session = Session();
        var window = session.Window.Window;
        window.Placement = new WindowPlacement(100, 100, 800, 600, true);
        session.PumpFrame();
        int moved = 0;
        session.Window.Moved += () => moved++;

        session.Window.MoveByUser(-300, 200, 700, 500);

        Assert.False(window.IsMaximized);
        Assert.Equal(new WindowPlacement(-300, 200, 700, 500, false), window.Placement);
        session.PumpFrame();
        Assert.Equal(1, moved);
    }

    [Fact]
    public void ADefaultStyleWindow_MaximizesAtOnce_WithNoBorders()
    {
        using var session = Session(WindowStyle.Default);
        var window = session.Window.Window;
        window.Placement = new WindowPlacement(100, 100, 800, 600, false);
        session.PumpFrame();

        Assert.Equal(new Rect(100, 100, 800, 600), session.Window.ContentBounds);
        window.Maximize();
        Assert.True(window.IsMaximized);
    }

    [Fact]
    public void WithoutTheWindowsFrame_MaximizeIsImmediate_AndTheWindowsOnlyMembersSaySo()
    {
        using var session = HeadlessSession.Create(style: WindowStyle.Borderless,
            options: new HeadlessOptions { Monitors = Desk });
        var window = session.Window.Window;
        window.Placement = new WindowPlacement(100, 100, 800, 600, false);

        Assert.Equal(800, window.LayoutWidth);
        window.Maximize();
        Assert.True(window.IsMaximized);
        Assert.Throws<InvalidOperationException>(() => session.Window.ContentBounds);
        Assert.Throws<InvalidOperationException>(() => session.Window.MoveByUser(0, 0, 100, 100));
    }

    [Fact]
    public void ResizeBorderAt_TakesTheNearestScaleListed()
    {
        var frame = new HeadlessWindowsFrame();
        Assert.Equal(8, frame.ResizeBorderAt(1f));
        Assert.Equal(11, frame.ResizeBorderAt(1.4f));
        Assert.Equal(13, frame.ResizeBorderAt(3f));
        Assert.Equal(5, new HeadlessWindowsFrame { ResizeBorders = new Dictionary<float, int> { [1f] = 5 } }.ResizeBorderAt(2f));
    }
}
