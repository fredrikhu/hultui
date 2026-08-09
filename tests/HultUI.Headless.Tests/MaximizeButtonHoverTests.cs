using System.Reflection;
using HultUI.Core.Builders;
using HultUI.Core.Elements;
using HultUI.Core.Primitives;
using Xunit;

namespace HultUI.Headless.Tests;

/// <summary>
/// The custom-chrome maximize button's hover highlight.
///
/// <para>Windows owns this button as non-client so it can offer Snap Layouts, which means
/// the client pointer pipeline deliberately skips it — <c>PointerState</c> never sees it,
/// and the framework drives its hover itself from the frame's NC callback. That makes it
/// the one hover path no ordinary interaction test covers, and it broke silently when
/// hover styling moved from <c>OnPointerEnter</c> handlers to declarative <c>.OnHover</c>:
/// dispatching the event still ran nothing, because declarative styling is applied from
/// the interaction state instead.</para>
/// </summary>
public class MaximizeButtonHoverTests
{
    private static readonly Color Hover = new(60, 60, 90);

    private static void SetHover(Window window, bool hovered) =>
        typeof(Window).GetMethod("SetMaximizeButtonHover", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(window, [hovered]);

    private static (HeadlessSession session, BoxElement button) Build(bool declarative)
    {
        var session = HeadlessSession.Create(width: 400, height: 200);
        var button = Box.New().Width(40).Height(30);
        button.IsWindowMaximizeButton = true;

        if (declarative)
            button.OnHover(b => ((BoxElement)b).BackgroundColor = Hover);
        else
            button.OnPointerEnter(e => ((BoxElement)e.CurrentTarget).BackgroundColor = Hover);

        session.Window.SetRoot(Flex.Row().Children(button));
        session.PumpFrame();
        return (session, button);
    }

    [Fact]
    public void DeclarativeHoverStylingIsApplied()
    {
        var (session, button) = Build(declarative: true);
        using var _ = session;

        SetHover(session.Window.Window, true);
        session.PumpFrame();

        Assert.Equal(Hover, button.BackgroundColor);
    }

    [Fact]
    public void ImperativeHoverHandlersStillRun()
    {
        // The event dispatch was the original mechanism and apps may still rely on it,
        // so the fix adds the interaction state rather than replacing the dispatch.
        var (session, button) = Build(declarative: false);
        using var _ = session;

        SetHover(session.Window.Window, true);
        session.PumpFrame();

        Assert.Equal(Hover, button.BackgroundColor);
    }

    [Fact]
    public void LeavingClearsTheHighlight()
    {
        var (session, button) = Build(declarative: true);
        using var _ = session;

        SetHover(session.Window.Window, true);
        session.PumpFrame();
        Assert.Equal(Hover, button.BackgroundColor);

        SetHover(session.Window.Window, false);
        session.PumpFrame();

        Assert.NotEqual(Hover, button.BackgroundColor);
    }

    [Fact]
    public void TheButtonReportsItsHoverStateWhileHeld()
    {
        var (session, button) = Build(declarative: true);
        using var _ = session;

        SetHover(session.Window.Window, true);
        Assert.True(button.IsHovered);

        SetHover(session.Window.Window, false);
        Assert.False(button.IsHovered);
    }
}
