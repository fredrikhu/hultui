using HultUI.Core.Builders;
using HultUI.Core.Elements;
using Xunit;

namespace HultUI.Headless.Tests;

/// <summary>
/// The bar widens under the pointer, the way Windows 11's does: a hairline while you are
/// reading, a real bar with a track the moment you reach for it.
///
/// <para>Measured off the WinUI Gallery, where the resting indicator is 2px and no track at all.
/// The two halves have to move together — a bar that stays 2px wide is unusable, and one that is
/// always 12px is the thing the hairline exists to avoid.</para>
/// </summary>
public class ScrollbarHoverTests
{
    private const float RestWidth = 2f;
    private const float ExpandedWidth = 12f;

    private static (HeadlessSession Session, ScrollViewElement Scroll) Start()
    {
        var session = HeadlessSession.Create(200, 150,
            options: new HeadlessOptions { RecordDrawCalls = true });

        var scroll = ScrollView.New().Height(150f).Children(Box.New().Height(900f).FlexShrink(0));
        scroll.ScrollbarWidth = RestWidth;
        scroll.ScrollbarExpandedWidth = ExpandedWidth;
        session.Application.SetRoot(scroll);
        session.PumpFrames(2);
        return (session, scroll);
    }

    /// <summary>The two scrollbar rects of the last frame: track first, then thumb.</summary>
    private static string Bars(HeadlessSession session)
    {
        session.Window.RenderLog.Clear();
        session.PumpFrame(session.DefaultFrameDelta, forceRender: true);
        return string.Join(" | ", session.Window.RenderLog.Named("DrawRoundedRect").Select(o => o.Args));
    }

    /// <summary>Somewhere inside the bar's reach. Deliberately not on the 2px bar itself — the
    /// point of the wider hit region is that you do not have to be.</summary>
    private static void PointAtTheBar(HeadlessSession session) => session.Window.MoveMouse(194, 60);

    private static void PointAway(HeadlessSession session) => session.Window.MoveMouse(40, 60);

    [Fact]
    public void AtRestTheBarIsAHairline()
    {
        var (session, _scroll) = Start();
        using var _ = session;

        var bars = Bars(session);

        Assert.Contains("2x150", bars);   // track
        Assert.Contains("2x25", bars);    // thumb
    }

    [Fact]
    public void PointingAtItExpandsTheTrackAndTheThumb()
    {
        var (session, _scroll) = Start();
        using var _ = session;

        PointAtTheBar(session);
        session.PumpFrames(20);           // past the 120ms expansion

        var bars = Bars(session);
        Assert.Contains("12x150", bars);  // track grows to the full width
        Assert.Contains("6x25", bars);    // thumb to half of it, the Windows proportion
    }

    [Fact]
    public void MovingAwayCollapsesItAgain()
    {
        var (session, _scroll) = Start();
        using var _ = session;

        PointAtTheBar(session);
        session.PumpFrames(20);
        Assert.Contains("12x150", Bars(session));

        PointAway(session);
        session.PumpFrames(20);

        Assert.Contains("2x150", Bars(session));
    }

    /// <summary>
    /// The reach is the expanded width even at rest. Without it a 2px bar would be a 2px target,
    /// nothing would ever expand it, and the hairline would be decoration rather than a control.
    /// </summary>
    [Fact]
    public void APressBesideTheHairlineStillCountsAsThePress()
    {
        var (session, scroll) = Start();
        using var _ = session;

        // 8px in from the right edge: outside the 2px bar, inside the 12px reach.
        Assert.True(scroll.TryStartScrollbarDrag(new Core.Primitives.Point(192, 60)));
    }

    /// <summary>Opting out leaves the old behaviour exactly as it was — one width, no animation,
    /// and a hit region no wider than the bar.</summary>
    [Fact]
    public void OptingOutKeepsTheFixedBar()
    {
        var (session, scroll) = Start();
        using var _ = session;

        scroll.ScrollbarExpandOnHover = false;
        scroll.ScrollbarWidth = 6f;

        PointAtTheBar(session);
        session.PumpFrames(20);

        Assert.Contains("6x150", Bars(session));
        Assert.False(scroll.TryStartScrollbarDrag(new Core.Primitives.Point(190, 60)));
    }

    /// <summary>The track can stay invisible at rest and appear only while expanded, which is
    /// what WinUI does and what a hairline needs to not read as a stray line.</summary>
    [Fact]
    public void TheTrackCanFadeInOnlyWhileExpanded()
    {
        var (session, scroll) = Start();
        using var _ = session;

        scroll.ScrollbarColor = Core.Primitives.Color.Transparent;
        scroll.ScrollbarTrackHoverColor = Core.Primitives.Color.FromHex("#123456");

        Assert.DoesNotContain("#123456", Bars(session));

        PointAtTheBar(session);
        session.PumpFrames(20);

        Assert.Contains("#123456", Bars(session));
    }
}
