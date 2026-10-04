using HultUI.Core.Builders;
using HultUI.Core.Platform;
using Xunit;

namespace HultUI.Headless.Tests;

/// <summary>
/// A headless window records its placement rather than applying it, so an app's save-and-restore
/// wiring can be tested without a desktop: what was set comes back out, and the surface the test
/// lays out against stays the size the session made it.
/// </summary>
public sealed class WindowPlacementTests
{
    [Fact]
    public void ASetPlacement_ComesBackOut_AndLeavesTheSurfaceAlone()
    {
        using var session = HeadlessSession.Create(width: 400, height: 300);
        session.Window.SetRoot(Box.New());
        session.PumpFrame();

        var saved = new WindowPlacement(-2000, 500, 1000, 700, false);
        session.Window.Window.Placement = saved;
        session.PumpFrame();

        Assert.Equal(saved, session.Window.Window.Placement);
        Assert.Equal(400, session.Window.Window.LayoutWidth);
        Assert.Equal(300, session.Window.Window.LayoutHeight);
    }

    [Fact]
    public void AMaximizedPlacement_MaximizesTheWindow_AndRestoreIsReported()
    {
        using var session = HeadlessSession.Create(width: 400, height: 300);
        session.Window.Window.Placement = new WindowPlacement(10, 20, 1000, 700, true);
        Assert.True(session.Window.Window.IsMaximized);

        session.Window.Window.Restore();
        Assert.Equal(new WindowPlacement(10, 20, 1000, 700, false), session.Window.Window.Placement);
    }

    [Fact]
    public void NothingSaved_ChangesNothing()
    {
        using var session = HeadlessSession.Create(width: 400, height: 300, scale: 1.5f);
        var before = session.Window.Window.Placement;

        session.Window.Window.Placement = null;
        session.Window.Window.Placement = new WindowPlacement(5, 5, 0, 0, true);

        Assert.Equal(new WindowPlacement(0, 0, 600, 450, false), before);
        Assert.Equal(before, session.Window.Window.Placement);
        Assert.False(session.Window.Window.IsMaximized);
    }
}
