using HultUI.Core.Builders;
using HultUI.Core.Events;
using Xunit;

namespace HultUI.Headless.Tests;

/// <summary>
/// The input safety net renders a frame whenever an app handler ran, because a
/// handler may mutate without marking. OnPassive is the escape for high-frequency
/// listeners that promise to mark dirty themselves: Junction attaches a drag
/// tracker to its root (HorizontalPan.AttachDrag), and registering it as an
/// ordinary handler turned every pointer move over an idle window — including
/// empty space under the repository list — into a full frame.
/// </summary>
public class PassiveHandlerTests
{
    [Fact]
    public void APassiveMoveHandlerOnTheRoot_DoesNotForceFramesOnAnIdleWindow()
    {
        using var session = HeadlessSession.Create();
        var root = Box.New();
        int calls = 0;
        root.OnPassive<PointerMoveEvent>(_ => calls++);
        session.Window.SetRoot(root);
        session.PumpFrame();

        session.Window.MoveMouse(100, 100);   // hover null → root: this one may render
        session.PumpFrame();
        session.Window.MoveMouse(105, 105);   // same element, nothing changed
        bool rendered = session.PumpFrame();

        Assert.True(calls >= 2, "the passive handler should still run on every move");
        Assert.False(rendered,
            "a passive no-op move handler forced a frame on an idle window");
    }

    [Fact]
    public void AnOrdinaryMoveHandler_StillGetsItsSafetyNetFrame()
    {
        // The contrast case, and this test pair's built-in negative control: the
        // identical scenario with the ordinary registration must keep rendering,
        // because an app handler may mutate visuals without marking dirty.
        using var session = HeadlessSession.Create();
        var root = Box.New();
        root.On<PointerMoveEvent>(_ => { });
        session.Window.SetRoot(root);
        session.PumpFrame();

        session.Window.MoveMouse(100, 100);
        session.PumpFrame();
        session.Window.MoveMouse(105, 105);
        bool rendered = session.PumpFrame();

        Assert.True(rendered,
            "the safety net must still cover ordinary handlers that ran");
    }
}
