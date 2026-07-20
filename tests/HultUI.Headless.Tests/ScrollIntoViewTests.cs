using HultUI.Core.Builders;
using HultUI.Core.Elements;
using HultUI.Core.Primitives;
using Xunit;

namespace HultUI.Headless.Tests;

/// <summary>
/// Regression coverage for <see cref="ScrollViewElement.ScrollIntoView"/> and
/// <see cref="ScrollViewElement.ScrollRangeIntoView"/>. The subtle case is
/// scrolling *up* to reveal a row above the viewport: layout positions are
/// unscrolled (the renderer applies ScrollOffsetY as a render-time translate),
/// so the reveal math must not add the current offset — doing so collapses the
/// up-scroll test to "content-offset &lt; 0", which never fires.
/// </summary>
public class ScrollIntoViewTests
{
    private const float RowH = 40f;
    private const float ViewH = 100f;

    // 10 rows of 40 → 400 of content in a 100-tall viewport (maxScroll 300).
    private static (ScrollViewElement scroll, Element[] rows) BuildList(HeadlessSession session)
    {
        var rows = new Element[10];
        for (int i = 0; i < rows.Length; i++)
            rows[i] = Box.New().Height(RowH).FlexShrink(0).Background(Color.Red);
        var scroll = ScrollView.New().Height(ViewH).Children(Flex.Column().Children(rows));
        session.Window.SetRoot(scroll);
        session.PumpFrame();
        return (scroll, rows);
    }

    // A row's unscrolled top within the scroll's content space.
    private static float ContentTop(ScrollViewElement s, Element row)
        => row.Layout.AbsoluteBorderBox.Y - s.Layout.AbsoluteContentBox.Y;

    private static bool FullyVisible(ScrollViewElement s, Element row)
    {
        var v = s.Layout.AbsoluteContentBox;
        var b = row.Layout.AbsoluteBorderBox;
        float screenTop = b.Y - s.ScrollOffsetY;          // render translates by ScrollOffsetY
        float screenBottom = screenTop + b.Height;
        return screenTop >= v.Y - 0.5f && screenBottom <= v.Y + v.Height + 0.5f;
    }

    [Fact]
    public void RowBelowViewport_ScrollsDownToItsBottomEdge()
    {
        using var session = HeadlessSession.Create(width: 200, height: 300);
        var (scroll, rows) = BuildList(session);

        scroll.ScrollIntoView(rows[6]);   // top 240, below the 0..100 window

        Assert.True(FullyVisible(scroll, rows[6]));
        // Minimal: the row's bottom lands exactly on the viewport's bottom edge.
        Assert.Equal(ContentTop(scroll, rows[6]) + RowH - ViewH, scroll.ScrollOffsetY, 3);
    }

    [Fact]
    public void RowAboveViewport_ScrollsUpToItsTopEdge()
    {
        // The regression: from a scrolled-down state, revealing an earlier row must
        // decrease the offset. The pre-fix math left it unchanged (arrow-up "stuck").
        using var session = HeadlessSession.Create(width: 200, height: 300);
        var (scroll, rows) = BuildList(session);

        scroll.ScrollIntoView(rows[6]);          // scroll down first
        float scrolledDown = scroll.ScrollOffsetY;
        Assert.True(scrolledDown > 0f);

        scroll.ScrollIntoView(rows[1]);          // now reveal a row above the window

        Assert.True(scroll.ScrollOffsetY < scrolledDown, "arrow-up should scroll back");
        Assert.True(FullyVisible(scroll, rows[1]));
        Assert.Equal(ContentTop(scroll, rows[1]), scroll.ScrollOffsetY, 3);
    }

    [Fact]
    public void RowAlreadyVisible_IsANoOp()
    {
        using var session = HeadlessSession.Create(width: 200, height: 300);
        var (scroll, rows) = BuildList(session);

        scroll.ScrollIntoView(rows[6]);          // scroll to a middle state (offset 180)
        float before = scroll.ScrollOffsetY;
        Assert.True(FullyVisible(scroll, rows[5]));   // row 5 sits fully inside the window

        scroll.ScrollIntoView(rows[5]);

        Assert.Equal(before, scroll.ScrollOffsetY, 3);
    }

    [Fact]
    public void ScrollRangeIntoView_RevealsAFixedHeightBandBothWays()
    {
        // The index-based entry point used by virtualised / fixed-row lists, where
        // the target row may not be materialised as an element.
        using var session = HeadlessSession.Create(width: 200, height: 300);
        var (scroll, _) = BuildList(session);

        scroll.ScrollRangeIntoView(9 * RowH, RowH);      // last row → clamps to max
        Assert.Equal(10 * RowH - ViewH, scroll.ScrollOffsetY, 3);   // 300

        scroll.ScrollRangeIntoView(0, RowH);             // first row → back to top
        Assert.Equal(0f, scroll.ScrollOffsetY, 3);
    }
}
