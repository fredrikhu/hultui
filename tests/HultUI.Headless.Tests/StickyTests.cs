using HultUI.Core.Accessibility;
using HultUI.Core.Builders;
using HultUI.Core.Elements;
using HultUI.Core.Events;
using HultUI.Core.Layout;
using HultUI.Core.Primitives;
using SkiaSharp;
using Xunit;

namespace HultUI.Headless.Tests;

/// <summary>
/// <see cref="PositionType.Sticky"/>: a section header held at the top of its scroll view while
/// its section scrolls under it, pushed off by the section's end, drawn over the rows, hit where
/// it is drawn and read where it is drawn; a footer held at the bottom; nothing outside a scroll
/// view.
/// </summary>
public sealed class StickyTests
{
    private static readonly Color HeaderFill = new(200, 30, 30);
    private static readonly Color RowFill = new(30, 30, 200);

    private sealed class Fixture : IDisposable
    {
        public readonly HeadlessSession Session = HeadlessSession.Create(300, 260);
        public readonly ScrollViewElement Scroll;
        public readonly BoxElement Header1, Header2, Section1;
        public int HeaderClicks, RowClicks;

        public Fixture()
        {
            Header1 = Header("First");
            Header2 = Header("Second");
            Section1 = Section(Header1);
            Scroll = ScrollView.Vertical().Height(200)
                .Children(Flex.Column(gap: 0).Children(Section1, Section(Header2)));
            Session.Window.SetRoot(Flex.Column(gap: 0).Padding(new Thickness(30, 0, 0, 0)).Children(Scroll));
            Session.PumpFrames(2);
        }

        private BoxElement Header(string name)
        {
            var header = Box.New().Height(30).Background(HeaderFill).Sticky()
                .AccessibleRole(AccessibilityRole.Heading).AccessibleName(name);
            header.On<ClickEvent>(_ => HeaderClicks++);
            return header;
        }

        // A header and ten 40px rows: 430 tall.
        private BoxElement Section(BoxElement header)
        {
            var section = Flex.Column(gap: 0);
            section.AddChild(header);
            for (int i = 0; i < 10; i++)
            {
                var row = Box.New().Height(40).FlexShrink(0).Background(RowFill);
                row.On<ClickEvent>(_ => RowClicks++);
                section.AddChild(row);
            }
            return section;
        }

        public float ViewTop => Scroll.Layout.AbsoluteContentBox.Y;

        public void ScrollTo(float y)
        {
            Scroll.ScrollOffsetY = y;
            Session.PumpFrames(2);
        }

        public Color PixelAt(float x, float y)
        {
            using var image = Session.Window.CapturePixels();
            using var bitmap = SKBitmap.FromImage(image);
            var c = bitmap.GetPixel((int)x, (int)y);
            return new Color(c.Red, c.Green, c.Blue);
        }

        public void Dispose() => Session.Dispose();
    }

    [Fact]
    public void ItIsHeldAtTheTop_WhileItsSectionScrollsUnderIt()
    {
        using var f = new Fixture();
        Assert.Equal(f.ViewTop, f.Header1.ScreenBorderBox.Y, 0.5f);

        f.ScrollTo(100);
        Assert.Equal(f.ViewTop, f.Header1.ScreenBorderBox.Y, 0.5f);
        // Drawn there, over the rows that scrolled under it.
        Assert.Equal(HeaderFill, f.PixelAt(150, f.ViewTop + 15));
        Assert.Equal(RowFill, f.PixelAt(150, f.ViewTop + 60));
    }

    [Fact]
    public void TheEndOfItsSectionPushesItOff_AndTheNextHeaderTakesOver()
    {
        using var f = new Fixture();
        // 420 scrolled: 10 of the first section left, so its header is pushed up by 20.
        f.ScrollTo(420);
        Assert.Equal(f.ViewTop - 20, f.Header1.ScreenBorderBox.Y, 0.5f);
        Assert.Equal(f.ViewTop + 10, f.Header2.ScreenBorderBox.Y, 0.5f);

        f.ScrollTo(500);
        Assert.Equal(f.ViewTop, f.Header2.ScreenBorderBox.Y, 0.5f);
    }

    [Fact]
    public void APressWhereItIsDrawnIsItsPress_AndAReaderIsToldItIsThere()
    {
        using var f = new Fixture();
        f.ScrollTo(100);
        f.Session.Window.MouseDown(150, f.ViewTop + 10);
        f.Session.Window.MouseUp(150, f.ViewTop + 10);
        Assert.Equal(1, f.HeaderClicks);
        Assert.Equal(0, f.RowClicks);

        var heading = f.Session.Window.FindByRole(AccessibilityRole.Heading, "First")!;
        Assert.Equal(f.ViewTop, heading.Bounds.Y, 0.5f);
        Assert.False(heading.Node.IsOffscreen);
    }

    [Fact]
    public void AFooterIsHeldAtTheBottom_UntilItsPlaceComesIntoView()
    {
        using var session = HeadlessSession.Create(300, 260);
        var footer = Box.New().Height(30).Background(HeaderFill).Sticky(top: null, bottom: 0);
        var section = Flex.Column(gap: 0).Children(Box.New().Height(400).FlexShrink(0), footer);
        var scroll = ScrollView.Vertical().Height(200).Children(section);
        session.Window.SetRoot(Flex.Column(gap: 0).Children(scroll));
        session.PumpFrames(2);

        float viewBottom = scroll.Layout.AbsoluteContentBox.Bottom;
        Assert.Equal(viewBottom - 30, footer.ScreenBorderBox.Y, 0.5f);

        scroll.ScrollOffsetY = 230;   // all 430 in view: in its own place
        session.PumpFrames(2);
        Assert.Equal(viewBottom - 30, footer.ScreenBorderBox.Y, 0.5f);
        Assert.Equal(400 - 230 + scroll.Layout.AbsoluteContentBox.Y, footer.ScreenBorderBox.Y, 0.5f);
    }

    /// <summary>
    /// Headers that are the scroll view's own children, rows between them — a change list's
    /// "Staged" and "Changes". Their parent's box is only the viewport, so they are held across
    /// everything that scrolls instead, and a later one is drawn over an earlier one.
    /// </summary>
    [Fact]
    public void FlatHeadersInTheScrollViewItself_AreHeldThroughout_TheLaterOverTheEarlier()
    {
        using var session = HeadlessSession.Create(300, 260);
        var second = new Color(30, 160, 30);
        var h1 = Box.New().Height(30).Background(HeaderFill).Sticky();
        var h2 = Box.New().Height(30).Background(second).Sticky();
        var scroll = ScrollView.Vertical().Height(200);
        scroll.AddChild(h1);
        for (int i = 0; i < 10; i++) scroll.AddChild(Box.New().Height(40).FlexShrink(0).Background(RowFill));
        scroll.AddChild(h2);
        for (int i = 0; i < 10; i++) scroll.AddChild(Box.New().Height(40).FlexShrink(0).Background(RowFill));
        session.Window.SetRoot(Flex.Column(gap: 0).Children(scroll));
        session.PumpFrames(2);
        float top = scroll.Layout.AbsoluteContentBox.Y;

        scroll.ScrollOffsetY = 300;
        session.PumpFrames(2);
        Assert.Equal(top, h1.ScreenBorderBox.Y, 0.5f);

        scroll.ScrollOffsetY = 500;
        session.PumpFrames(2);
        Assert.Equal(top, h1.ScreenBorderBox.Y, 0.5f);
        Assert.Equal(top, h2.ScreenBorderBox.Y, 0.5f);
        using var image = session.Window.CapturePixels();
        using var bitmap = SKBitmap.FromImage(image);
        var c = bitmap.GetPixel(150, (int)top + 15);
        Assert.Equal(second, new Color(c.Red, c.Green, c.Blue));
    }

    /// <summary>A row revealed by arrowing up lands clear of its section's header, not under it.</summary>
    [Fact]
    public void RevealingARowScrollsItClearOfTheHeldHeader()
    {
        using var f = new Fixture();
        f.ScrollTo(200);
        var row = f.Section1.Children[3];   // the third row: 30 + 2 × 40 down the section
        f.Scroll.ScrollIntoView(row);
        f.Session.PumpFrames(2);

        Assert.True(row.ScreenBorderBox.Y >= f.Header1.ScreenBorderBox.Bottom - 0.5f,
            $"the row at {row.ScreenBorderBox.Y} is under the header ending at {f.Header1.ScreenBorderBox.Bottom}");
        Assert.True(row.ScreenBorderBox.Y < f.ViewTop + 40, "it scrolled further than it had to");
    }

    [Fact]
    public void OutsideAScrollView_ItStaysWhereItIs()
    {
        using var session = HeadlessSession.Create(300, 200);
        var header = Box.New().Height(30).Sticky();
        session.Window.SetRoot(Flex.Column(gap: 0).Children(Box.New().Height(50), header));
        session.PumpFrames(2);
        Assert.Equal(50, header.ScreenBorderBox.Y, 0.5f);
    }
}
