using HultUI.Core.Builders;
using HultUI.Core.Elements;
using HultUI.Core.Events;
using HultUI.Core.Primitives;
using HultUI.Core.Styling;
using Xunit;

namespace HultUI.Headless.Tests;

/// <summary>
/// The two-ring focus visual: a thick outer ring with a thin one of the opposite colour inside
/// it, drawn around whatever holds keyboard focus.
///
/// <para>Two rings rather than one because a single ring can only contrast with one background —
/// white vanishes on a light card, dark vanishes on a dark button, and an indicator that
/// disappears on some controls is worse than none. The keyboard-only gate matters just as much:
/// a click focuses a control too, and a ring that appears on every click reads as something
/// being stuck.</para>
/// </summary>
public class FocusVisualTests
{
    private static readonly Color Outer = Color.FromHex("#AA1122");
    private static readonly Color Inner = Color.FromHex("#22AA11");

    private static Theme Loud => Theme.Light.With(new FocusVisualTheme
    {
        OuterColor = Outer,
        InnerColor = Inner,
        OuterThickness = 2f,
        InnerThickness = 1f,
        Margin = 1f,
    });

    private static (HeadlessSession Session, BoxElement Target) Start(bool showVisual = true, Theme? theme = null)
    {
        var session = HeadlessSession.Create(200, 120,
            options: new HeadlessOptions { RecordDrawCalls = true });
        session.Application.Theme = theme ?? Loud;

        var target = Box.New().Width(80f).Height(30f).CornerRadius(4f);
        target.IsFocusable = true;
        target.ShowsFocusVisual = showVisual;

        session.Application.SetRoot(Box.New().Padding(20f).Children(target));
        session.PumpFrames(2);
        return (session, target);
    }

    private static string Frame(HeadlessSession session)
    {
        session.Window.RenderLog.Clear();
        session.PumpFrame(session.DefaultFrameDelta, forceRender: true);
        return session.Window.RenderLog.ToString();
    }

    [Fact]
    public void TabbingToAControlDrawsBothRings()
    {
        var (session, _target) = Start();
        using var _ = session;

        session.Window.PressKey(KeyCode.Tab);
        session.PumpFrames(2);

        var frame = Frame(session);
        Assert.Contains(Hex(Outer), frame);
        Assert.Contains(Hex(Inner), frame);
    }

    /// <summary>
    /// A click focuses the control too, and must not light the ring. This is the assertion that
    /// keeps the feature from being an irritation.
    /// </summary>
    [Fact]
    public void ClickingItFocusesItWithoutTheRing()
    {
        var (session, target) = Start();
        using var _ = session;

        session.Window.Click(target);
        session.PumpFrames(2);

        Assert.Same(target, session.Application.PrimaryWindow.FocusedElement);

        var frame = Frame(session);
        Assert.DoesNotContain(Hex(Outer), frame);
        Assert.DoesNotContain(Hex(Inner), frame);
    }

    /// <summary>And the ring comes back the moment the keyboard is used again — the flag tracks
    /// how the current focus arrived, not how the session started.</summary>
    [Fact]
    public void TheRingReturnsOnTheNextTab()
    {
        var (session, target) = Start();
        using var _ = session;

        session.Window.Click(target);
        session.PumpFrames(2);
        Assert.DoesNotContain(Hex(Outer), Frame(session));

        session.Window.PressKey(KeyCode.Tab);
        session.PumpFrames(2);

        Assert.Contains(Hex(Outer), Frame(session));
    }

    [Fact]
    public void AnElementThatDidNotAskForOneGetsNothing()
    {
        var (session, _target) = Start(showVisual: false);
        using var _ = session;

        session.Window.PressKey(KeyCode.Tab);
        session.PumpFrames(2);

        var frame = Frame(session);
        Assert.DoesNotContain(Hex(Outer), frame);
        Assert.DoesNotContain(Hex(Inner), frame);
    }

    /// <summary>Opting out of the gate draws it however focus arrived, for an app that would
    /// rather always show where focus is.</summary>
    [Fact]
    public void TheKeyboardGateCanBeTurnedOff()
    {
        var (session, target) = Start(theme: Theme.Light.With(new FocusVisualTheme
        {
            OuterColor = Outer,
            InnerColor = Inner,
            KeyboardOnly = false,
        }));
        using var _ = session;

        session.Window.Click(target);
        session.PumpFrames(2);

        Assert.Contains(Hex(Outer), Frame(session));
    }

    /// <summary>
    /// The rings sit outside the control and outside each other, with the thin one against it.
    /// Asserted as geometry because "both colours appear somewhere" would pass with the two
    /// rings drawn on top of one another.
    /// </summary>
    [Fact]
    public void TheRingsAreConcentricAndOutsideTheControl()
    {
        var (session, target) = Start();
        using var _ = session;

        session.Window.PressKey(KeyCode.Tab);
        session.PumpFrames(2);
        var frame = Frame(session);

        var box = target.ScreenBorderBox;

        // Inner ring: 1px band starting 1px out, so its path is centred 1.5px out.
        Assert.Contains(Band(box, 1.5f), frame);
        // Outer ring: 2px band starting where the inner one ends, centred 1 + 1 + 1 = 3px out.
        Assert.Contains(Band(box, 3f), frame);
    }

    /// <summary>The corner radius grows with each ring, so they stay concentric with a rounded
    /// control instead of squaring off around it.</summary>
    [Fact]
    public void TheCornersFollowTheControl()
    {
        var (session, _target) = Start();
        using var _ = session;

        session.Window.PressKey(KeyCode.Tab);
        session.PumpFrames(2);
        var frame = Frame(session);

        Assert.Contains("r=5.5", frame);   // 4 + 1.5, the inner ring
        Assert.Contains("r=7", frame);     // 4 + 3, the outer
    }

    private static string Band(Rect box, float inflate) =>
        $"({F(box.X - inflate)},{F(box.Y - inflate)} {F(box.Width + inflate * 2)}x{F(box.Height + inflate * 2)})";

    private static string F(float v) =>
        MathF.Round(v, 2).ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);

    private static string Hex(Color c) => $"#{c.R:X2}{c.G:X2}{c.B:X2}";
}
