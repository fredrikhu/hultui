using HultUI.Core.Accessibility;
using HultUI.Core.Builders;
using HultUI.Core.Elements;
using HultUI.Core.Primitives;
using Xunit;
using TextB = HultUI.Core.Builders.Text;

namespace HultUI.Headless.Tests;

/// <summary>
/// A frame only accessibility asks for — a client attaching to an idle window, a coalesced
/// refresh coming due after a scroll or between the keys of a walk — runs the accessibility phase
/// and draws nothing: nothing on screen changed. It drew the whole window again, and Junction's
/// History walk, a key every 60 ms with a client listening, drew half a frame more per key for it.
/// </summary>
public class A11yUndrawnFrameTests
{
    private int _draws;

    /// <summary>A pixel that counts the frames drawn.</summary>
    private Element Counter() => Canvas.New(_ => _draws++).Width(Dimension.Px(1)).Height(Dimension.Px(1));

    private static VirtualListElement<string> List(int count) =>
        VirtualListElement<string>.New().ItemHeight(24)
            .RenderWith(t => Flex.Row().Padding(new Thickness(2, 8)).Children(TextB.New(t).FontSize(12f)))
            .Items(Enumerable.Range(0, count).Select(i => $"item {i}").ToList());

    [Fact]
    public void AClientAttachingToAnIdleWindow_GetsItsTree_AndNothingIsDrawn()
    {
        using var session = HeadlessSession.Create(400, 300);
        session.Window.SetRoot(Flex.Column().Children(Counter(), TextB.New("hello")));
        session.PumpFrames(2);
        Assert.False(session.PumpFrame(), "an idle window drew a frame");
        int draws = _draws;

        var bridge = session.Window.InstallAccessibilityBridge();
        Assert.False(session.PumpFrame());

        Assert.Equal(draws, _draws);
        Assert.NotNull(bridge.CurrentTree);
        Assert.Contains(bridge.CurrentTree!.Children, n => n.Name == "hello");
    }

    [Fact]
    public void ARefreshDueAfterASelectionMoveThatScrolled_IsToldWithoutAFrameDrawn()
    {
        // A walk past the bottom of a list: the key's frame draws the move and tells the
        // selection; the scroll's refresh comes due 100 ms on, in a frame that draws nothing.
        using var session = HeadlessSession.Create(400, 300);
        var bridge = session.Window.InstallAccessibilityBridge();
        var list = List(200);
        var label = TextB.New("status");
        session.Window.SetRoot(Flex.Column().Children(Counter(), label, list.FlexGrow(1)));
        session.PumpFrames(2);
        session.PumpFrame(TimeSpan.FromMilliseconds(120));
        session.PumpFrame(TimeSpan.FromMilliseconds(120));
        label.Content = "status!";   // a rebuild now, so the scroll's refresh waits its 100 ms
        session.PumpFrame();
        bridge.ClearDiffs();

        list.SelectedIndex = 150;
        list.ScrollRangeIntoView(150 * 24f, 24f);
        int draws = _draws;
        Assert.True(session.PumpFrame());
        Assert.Equal(draws + 1, _draws);
        var patch = Assert.Single(bridge.Diffs);
        Assert.All(patch.PropertyChanged, c => Assert.Equal(AccessibilityProperties.IsSelected, c.PropertyName));

        bridge.ClearDiffs();
        int rendered = 0;
        for (int i = 0; i < 8; i++)
            if (session.PumpFrame()) rendered++;

        Assert.Equal(0, rendered);
        Assert.Equal(draws + 1, _draws);
        var refresh = Assert.Single(bridge.Diffs);
        Assert.Contains(refresh.PropertyChanged,
            c => c.Node.ElementId == list.Id && c.PropertyName == AccessibilityProperties.ScrollPosition);
    }

    [Fact]
    public void AChangeThatIsDrawn_IsStillDrawn_WhenAccessibilityAlsoAsksForTheFrame()
    {
        using var session = HeadlessSession.Create(400, 300);
        var bridge = session.Window.InstallAccessibilityBridge();
        var label = TextB.New("one");
        session.Window.SetRoot(Flex.Column().Children(Counter(), label));
        session.PumpFrames(2);
        int draws = _draws;

        label.Content = "two";
        Assert.True(session.PumpFrame());

        Assert.Equal(draws + 1, _draws);
        Assert.Contains(bridge.CurrentTree!.Children, n => n.Name == "two");
    }
}
