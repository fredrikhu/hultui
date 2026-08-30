using HultUI.Core.Builders;
using HultUI.Core.Elements;
using Xunit;

namespace HultUI.Headless.Tests;

/// <summary>
/// Clicking an element inside a scrolled view.
///
/// <para>A scroll view does not move its children's layout boxes; it offsets them when
/// painting and hit-testing. So <c>Click(element)</c>, which aims at the centre of the layout
/// box, used to fire into empty space as soon as anything had been scrolled — and because a
/// click that hits nothing is not an error, the test around it passed with the handler never
/// having run. That is the worst shape a bug can have in a test harness, so it gets its own
/// tests.</para>
/// </summary>
public class ScrolledElementClickTests
{
    private static (HeadlessSession Session, BoxElement Target, List<string> Hits) Build()
    {
        var session = HeadlessSession.Create(400, 200);
        var hits = new List<string>();

        BoxElement Row(string name) =>
            Box.New().Height(80f).FlexShrink(0)
                .OnClick(_ => hits.Add(name));

        var target = Row("target");
        var scroll = ScrollView.New().Height(200f).Children(
            Row("a"), Row("b"), Row("c"), Row("d"), target, Row("f"));

        session.Application.SetRoot(scroll);
        session.PumpFrames(2);
        return (session, target, hits);
    }

    [Fact]
    public void AnElementScrolledIntoViewIsClickedWhereItActuallyIs()
    {
        var (session, target, hits) = Build();
        using var _ = session;

        // Four rows of 80 off the top puts the fifth in view. Without the scroll correction
        // the click aims 320px below the window and nothing at all happens.
        session.Window.Wheel(200, 100, 0, -20);
        session.PumpFrames(4);

        session.Window.Click(target);
        session.PumpFrames(2);

        Assert.Equal(["target"], hits);
    }

    [Fact]
    public void AnUnscrolledViewIsUnaffected()
    {
        var (session, _target, hits) = Build();
        using var _ = session;

        session.Window.Click(200, 40);
        session.PumpFrames(2);

        Assert.Equal(["a"], hits);
    }
}
