using HultUI.Core.Builders;
using HultUI.Core.Elements;
using HultUI.Core.Events;
using Xunit;

namespace HultUI.Headless.Tests;

/// <summary>
/// A scroll view given its offset before its first layout, as a build that makes it gives it one,
/// shows that offset on its first frame, clamped to its content then. The setter used to clamp
/// against a view with no size and no content, which answered 0: a view made where the user had left
/// the old one (Vesper's Brief, back from another view) opened at the start.
/// </summary>
public class ScrollOffsetBeforeLayoutTests
{
    // Twenty columns 100 wide (2,000 of content) in a view 300 wide: 1,700 to scroll.
    private static (HeadlessSession Session, ScrollViewElement View, List<BoxElement> Columns) Make(params float[] offsets)
    {
        var session = HeadlessSession.Create(300, 120);
        var view = ScrollView.Horizontal().Width(300f).Height(120f);
        var columns = new List<BoxElement>();
        for (int c = 0; c < 20; c++)
        {
            var column = Box.New().Width(100f).Height(100f).FlexShrink(0);
            columns.Add(column);
            view.AddChild(column);
        }
        foreach (float x in offsets) view.ScrollOffsetX = x;
        session.Application.SetRoot(view);
        session.PumpFrame();
        return (session, view, columns);
    }

    [Fact]
    public void AnOffsetSetBeforeTheFirstLayout_IsShownOnTheFirstFrame()
    {
        var (session, view, columns) = Make(650f);
        using var _ = session;

        Assert.Equal(650f, view.ScrollOffsetX);
        Assert.Same(columns[6], HitTester.HitTest(view, new(10f, 50f)));
        Assert.Same(columns[9], HitTester.HitTest(view, new(290f, 50f)));
    }

    [Fact]
    public void AnOffsetPastTheEnd_IsClampedToTheContentLaidOut()
    {
        var (session, view, columns) = Make(9_000f);
        using var _ = session;

        Assert.Equal(1_700f, view.ScrollOffsetX);
        Assert.Same(columns[19], HitTester.HitTest(view, new(290f, 50f)));
    }

    [Fact]
    public void ALaterScrollFromCodeBeforeTheFirstLayout_Wins()
    {
        var (session, view, columns) = Make(650f, 120f);
        using var _ = session;

        Assert.Equal(120f, view.ScrollOffsetX);
        Assert.Same(columns[1], HitTester.HitTest(view, new(10f, 50f)));
    }
}
