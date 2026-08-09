using HultUI.Core.Builders;
using HultUI.Core.Elements;
using HultUI.Core.Events;
using HultUI.Core.Layout;
using HultUI.Core.Primitives;
using HultUI.Core.Styling;
using Xunit;

namespace HultUI.Core.Tests.Builders;

/// <summary>
/// The offset, the clipped cell and the scrollbar that let content move sideways inside a
/// box too narrow for it. Two diff viewers depend on this; the layout behaviour it relies
/// on — a negative margin both shifting and widening a stretched child — is subtle enough
/// to be worth pinning at this level rather than only through them.
/// </summary>
public class HorizontalPanTests
{
    private static HorizontalPan Pan(float content = 1000, float viewport = 400, Action? onChanged = null)
        => new(onChanged ?? (() => { })) { ContentWidth = content, ViewportWidth = viewport };

    // ── The offset ──
    [Fact]
    public void TheOffsetIsWhatIsLeftOfTheContentAfterTheViewport()
    {
        var pan = Pan(content: 1000, viewport: 400);
        Assert.Equal(600, pan.MaxOffset);
        Assert.True(pan.CanPan);
    }

    [Fact]
    public void ContentNarrowerThanItsBoxHasNowhereToGo()
    {
        var pan = Pan(content: 100, viewport: 400);
        Assert.Equal(0, pan.MaxOffset);
        Assert.False(pan.CanPan);
        Assert.False(pan.PanBy(50));
        Assert.Equal(0, pan.Offset);
    }

    [Fact]
    public void PanningClampsAtBothMargins()
    {
        var pan = Pan();
        Assert.False(pan.PanBy(-10));            // already home
        Assert.True(pan.PanTo(float.MaxValue));
        Assert.Equal(600, pan.Offset);
        Assert.False(pan.PanBy(10));             // already at the end
        Assert.True(pan.PanTo(0));
    }

    [Fact]
    public void OnlyARealMoveNotifies()
    {
        int changes = 0;
        var pan = Pan(onChanged: () => changes++);

        pan.PanBy(50);
        pan.PanBy(0);
        pan.PanTo(50);       // already there
        Assert.Equal(1, changes);
    }

    [Fact]
    public void AnOffsetPastTheEndIsReportedClampedWhenTheContentShrinks()
    {
        // The owner re-measures on new content; an offset from the old content must not be
        // handed out as if it were still reachable.
        var pan = Pan(content: 1000, viewport: 400);
        pan.PanTo(float.MaxValue);
        pan.ContentWidth = 500;

        Assert.Equal(100, pan.Offset);
    }

    // ── Keys and wheel ──
    [Theory]
    [InlineData(KeyCode.Right, KeyModifiers.None, HorizontalPan.KeyStep)]
    [InlineData(KeyCode.Left, KeyModifiers.None, 0f)]
    [InlineData(KeyCode.Right, KeyModifiers.Ctrl, 600f)]
    public void TheArrowKeysPan(KeyCode key, KeyModifiers mods, float expected)
    {
        var pan = Pan();
        Assert.True(pan.HandleKey(new KeyDownEvent { Key = key, Modifiers = mods }));
        Assert.Equal(expected, pan.Offset);
    }

    [Fact]
    public void AKeyItDoesNotUseIsLeftAlone()
    {
        var pan = Pan();
        Assert.False(pan.HandleKey(new KeyDownEvent { Key = KeyCode.Down }));
    }

    [Fact]
    public void AnArrowIsClaimedEvenWhenTheViewCannotMove()
    {
        // Otherwise the caller would let it fall through and the key would mean two things
        // at the far edge but one everywhere else.
        var pan = Pan();
        Assert.True(pan.HandleKey(new KeyDownEvent { Key = KeyCode.Left }));
        Assert.Equal(0, pan.Offset);
    }

    [Fact]
    public void ATrackpadSwipeAndShiftWheelBothPan()
    {
        var pan = Pan();
        Assert.True(pan.HandleScroll(new ScrollEvent { DeltaX = -1 }));
        float afterSwipe = pan.Offset;
        Assert.True(afterSwipe > 0);

        Assert.True(pan.HandleScroll(new ScrollEvent { DeltaY = -1, Modifiers = KeyModifiers.Shift }));
        Assert.True(pan.Offset > afterSwipe);

        // A plain vertical wheel belongs to whatever scrolls vertically.
        Assert.False(pan.HandleScroll(new ScrollEvent { DeltaY = -1 }));
    }

    // ── The cell ──
    [Fact]
    public void TheCellShiftsItsContentLeftAndWidensItToMatch()
    {
        // The whole mechanism in one assertion. Shifting alone would leave a hole at the
        // right edge; the stretch subtracts the negative margin, so the content gains
        // exactly the width it was moved by and text arrives from the right.
        var engine = new LayoutEngine(new StubMeasureContext(charWidth: 10, lineHeight: 14));
        var pan = Pan(content: 1000, viewport: 400);
        pan.PanBy(120);

        var text = Text.New("some content");
        var root = Flex.Row(gap: 0).Width(400).Children(pan.Cell(text));
        StyleResolver.Resolve(root, new Theme());
        engine.PerformLayout(root, 400, 100);

        var cell = root.Children[0];
        Assert.Equal(400, cell.Layout.AbsoluteContentBox.Width, 1);
        Assert.Equal(cell.Layout.AbsoluteContentBox.X - 120, text.Layout.AbsoluteBorderBox.X, 1);
        Assert.Equal(520, text.Layout.AbsoluteBorderBox.Width, 1);
    }

    [Fact]
    public void TheCellClipsSoTheShiftedContentCannotSpillLeft()
    {
        var pan = Pan();
        var cell = pan.Cell(Text.New("x"));
        Assert.Equal(Overflow.Hidden, cell.Box.OverflowX);
    }

    [Fact]
    public void AtRestTheContentIsNotOffsetAtAll()
    {
        var pan = Pan();
        var text = Text.New("x");
        pan.Cell(text);
        Assert.Equal(0, text.Box.Margin.Left);
    }

    // ── The scrollbar ──
    [Fact]
    public void TheThumbIsAsWideAsTheViewportIsOfTheContent()
    {
        var engine = new LayoutEngine(new StubMeasureContext());
        var pan = Pan(content: 1000, viewport: 400);

        var bar = pan.Scrollbar(Color.Black, Color.White, Color.Black);
        var root = Flex.Column(gap: 0).Width(500).Children(bar);
        StyleResolver.Resolve(root, new Theme());
        engine.PerformLayout(root, 500, 100);

        // 400 of 1000 visible, over a 500px track.
        // Children: [travel spacer, thumb].
        Assert.Equal(200, bar.Children[1].Layout.AbsoluteBorderBox.Width, 1);
    }

    [Fact]
    public void DraggingTheThumbMapsOntoTheOffset()
    {
        var engine = new LayoutEngine(new StubMeasureContext());
        var pan = Pan(content: 1000, viewport: 400);
        var bar = pan.Scrollbar(Color.Black, Color.White, Color.Black);
        var root = Flex.Column(gap: 0).Width(500).Children(bar);
        StyleResolver.Resolve(root, new Theme());
        engine.PerformLayout(root, 500, 100);

        // Thumb is 200 wide in a 500 track, so it travels 300px for 600px of offset.
        pan.DragTo(0);
        Assert.Equal(0, pan.Offset, 1);

        pan.DragTo(1000);            // past the end
        Assert.Equal(600, pan.Offset, 1);
    }

    [Fact]
    public void AScrollbarWithNothingToScrollHasAFullWidthThumb()
    {
        var engine = new LayoutEngine(new StubMeasureContext());
        var pan = Pan(content: 100, viewport: 400);
        var bar = pan.Scrollbar(Color.Black, Color.White, Color.Black);
        var root = Flex.Column(gap: 0).Width(500).Children(bar);
        StyleResolver.Resolve(root, new Theme());
        engine.PerformLayout(root, 500, 100);

        Assert.Equal(500, bar.Children[1].Layout.AbsoluteBorderBox.Width, 1);
    }

    // ── Measuring ──
    [Fact]
    public void TheWidestLineIsTheOneWithTheMostCharacters()
    {
        // No measure context in a unit test, so this returns zero — what is being pinned is
        // that it picks by length and tolerates nulls rather than throwing.
        Assert.Equal(0, HorizontalPan.MeasureWidest(["a", null, "abc", "ab"], 12f, "monospace"));
    }
}
