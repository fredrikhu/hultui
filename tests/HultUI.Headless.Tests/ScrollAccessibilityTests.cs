using HultUI.Core.Accessibility;
using HultUI.Core.Builders;
using HultUI.Core.Elements;
using Xunit;

namespace HultUI.Headless.Tests;

// Inside a HultUI.* namespace bare `Text` binds to the HultUI.Text namespace.
using Text = HultUI.Core.Builders.Text;

/// <summary>
/// A scroll reaches assistive technology on the schedule a resize does: the tree is rebuilt as
/// the scroll starts, at most every 100 ms while it goes on, and once more after it stops. A
/// scroll moves the bounds of everything under the view and changes nothing else, and the
/// rebuild is of the whole tree — on every frame of a wheel turn it was 0.7 ms of a 1.9 ms
/// frame in Junction's History list, and up to 9 ms.
///
/// <para>Virtual time moves a sixtieth of a second per pump, and the coalescing runs on the
/// platform clock, so these counts are exact.</para>
/// </summary>
public class ScrollAccessibilityTests
{
    // 40 labelled rows of 30px in a 200px view: 1,000px to scroll, 25 notches of 40px.
    private static (HeadlessSession Session, ScrollViewElement List, HeadlessAccessibilityBridge Bridge) Fixture()
    {
        var session = HeadlessSession.Create(400, 300);
        var rows = Enumerable.Range(0, 40)
            .Select(i => (Element)Box.New().Height(30f).FlexShrink(0).Children(Text.New($"row {i}")))
            .ToArray();
        var list = ScrollView.New().Width(300f).Height(200f).Children(Flex.Column(gap: 0).Children(rows));
        session.Application.SetRoot(Box.New().Children(list));
        var bridge = session.Window.InstallAccessibilityBridge();
        session.PumpFrame();
        // A quiet spell, so the first scroll starts a fresh schedule.
        session.PumpFrame(TimeSpan.FromMilliseconds(200));
        bridge.ClearDiffs();
        return (session, list, bridge);
    }

    private static void WheelDown(HeadlessSession session) => session.Window.Wheel(150, 100, 0, -1);

    private static float RowTop(AccessibilityNode tree, string name) =>
        Walk(tree).First(n => n.Name == name).BoundingRect.Y;

    private static IEnumerable<AccessibilityNode> Walk(AccessibilityNode node)
    {
        yield return node;
        foreach (var child in node.Children)
            foreach (var d in Walk(child))
                yield return d;
    }

    [Fact]
    public void AWheelTurnRebuildsTheTreeAtMostEveryHundredMilliseconds()
    {
        var (session, list, bridge) = Fixture();
        using var _ = session;

        // 18 frames, one notch each: 300 ms of scrolling.
        for (int i = 0; i < 18; i++)
        {
            WheelDown(session);
            session.PumpFrame();
        }

        Assert.Equal(18 * 40, list.ScrollOffsetY);   // every notch moved the list
        // As the scroll starts, then 100 and 200 ms in — not once a frame.
        Assert.Equal(3, bridge.Diffs.Count);
        Assert.All(bridge.Diffs, d => Assert.NotEmpty(d.PropertyChanged));
    }

    [Fact]
    public void WhenTheWheelStopsTheLastPositionReachesTheTreeByItself()
    {
        var (session, list, bridge) = Fixture();
        using var _ = session;

        for (int i = 0; i < 4; i++)
        {
            WheelDown(session);
            session.PumpFrame();
        }
        float settled = RowTop(session.Window.AccessibilityTree, "row 20");
        // The tree has the first notch's position, not the last one's: the rest is due.
        Assert.NotEqual(settled, RowTop(bridge.CurrentTree!, "row 20"));

        // Nothing is dirty and nothing happens, yet the window asks for the frame that
        // delivers the position the scroll stopped at: one rebuild, and nothing drawn, since
        // nothing on screen changed.
        int diffs = bridge.Diffs.Count;
        int rendered = 0;
        for (int i = 0; i < 10; i++)
            if (session.PumpFrame()) rendered++;

        Assert.Equal(0, rendered);
        Assert.Equal(diffs + 1, bridge.Diffs.Count);
        Assert.Equal(settled, RowTop(bridge.CurrentTree!, "row 20"));
        Assert.Equal(4 * 40, list.ScrollOffsetY);
    }

    [Fact]
    public void AScrollAfterAQuietSpellReachesTheTreeInItsOwnFrame()
    {
        var (session, _, bridge) = Fixture();
        using var __ = session;

        WheelDown(session);
        session.PumpFrame();

        Assert.Single(bridge.Diffs);
        Assert.Equal(RowTop(session.Window.AccessibilityTree, "row 20"), RowTop(bridge.CurrentTree!, "row 20"));
    }
}
