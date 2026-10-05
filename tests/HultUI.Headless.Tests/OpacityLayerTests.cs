using HultUI.Core.Builders;
using HultUI.Core.Elements;
using HultUI.Core.Layout;
using HultUI.Core.Primitives;
using HultUI.Core.Rendering;
using HultUI.Core.Styling;
using Xunit;

namespace HultUI.Headless.Tests;

using Text = HultUI.Core.Builders.Text;

/// <summary>
/// An element below full opacity is drawn into an offscreen group sized to what its subtree
/// paints, not to the window. Everything here paints outside some element's own box — a child
/// that overflows its parent, text longer than its box, a shadow, an outline, a rotation, a
/// sticky header — so a bound that is too tight shows as something cut off.
/// <para>The golden baselines were drawn before the groups had bounds, when every group was
/// as large as the window: matching them is the proof that the bound loses nothing.</para>
/// </summary>
public sealed class OpacityLayerTests
{
    private static readonly Color Panel = Color.FromHex("#CBD5E1");
    private static readonly Color Indigo = Color.FromHex("#4F46E5");
    private static readonly Color Red = Color.FromHex("#EF4444");
    private static readonly Color Green = Color.FromHex("#22C55E");
    private static readonly Color Amber = Color.FromHex("#F59E0B");

    // A group starts on its own pixel, and Skia places a path's edges on a 1/64-pixel grid in the
    // group's pixels: moved from the window's corner to the content's, an antialiased glyph edge
    // can shade a step differently (one pixel did, by 8). Pinning the groups' origin back at the
    // corner made every frame match within the default. Content cut off is a strip of pixels.
    private static readonly GoldenTolerance MovedOrigin = new(Channel: 2, Pixels: 4);

    private static HeadlessSession Session(int width, int height) =>
        HeadlessSession.Create(width, height, options: HeadlessOptions.Golden());

    private static BoxElement Faded(float opacity = 0.6f) =>
        Box.New().Width(80).Height(40).Background(Panel).Opacity(opacity);

    private static BoxElement Absolute(BoxElement el, float left, float top)
    {
        el.Position.Type = PositionType.Absolute;
        el.Position.Left = Dimension.Px(left);
        el.Position.Top = Dimension.Px(top);
        return el;
    }

    [Fact]
    public void ChildrenAndTextThatOverflowTheFadedElement()
    {
        using var session = Session(480, 300);
        session.Window.SetRoot(Flex.Column(gap: 40).Padding(new Thickness(30)).Children(
            Flex.Row(gap: 60).Children(
                // Wider than its parent.
                Faded().Children(Box.New().Width(150).Height(24).FlexShrink(0).Background(Red)),
                // Pulled out past the parent's top-left corner.
                Faded().Children(Box.New().Width(40).Height(40).Margin(new Thickness(-16, 0, 0, -16))
                    .Background(Green).CornerRadius(20f)),
                // Placed outside the parent altogether.
                Faded().Children(Absolute(Box.New().Width(30).Height(30).Background(Amber), 70, 25))),
            Flex.Row(gap: 60).Children(
                // A word longer than its box, and more lines than the box is tall.
                Faded(0.7f).Children(
                    Text.New("Unbreakable").FontSize(18).Width(40),
                    Text.New("one two three four five").FontSize(13).Width(60).Height(14)),
                // Italic overhang: the glyphs reach past their advance, and the box is the advance.
                Box.New().Opacity(0.7f).Children(Text.New("ff").Italic().FontSize(40).TextColor(Indigo)),
                // An underline under an auto-sized label.
                Box.New().Opacity(0.7f).Children(Text.New("gjpqy").Underline().FontSize(22)),
                // Wider than its box on one line: a glyph that cannot break, and a capped line.
                Box.New().Opacity(0.7f).Children(
                    Text.New("W").FontSize(40).Width(12),
                    Text.New("Held to one line").MaxLines(1).Width(30))),
            Flex.Row(gap: 60).Children(
                // A rotated child poking out of the faded parent.
                Faded().Children(Box.New().Width(70).Height(18).FlexShrink(0).Rotate(35f).Background(Red)),
                // A faded group inside a faded group, the inner one's child overflowing both.
                Box.New().Width(80).Height(40).Background(Panel).Opacity(0.8f).Children(
                    Box.New().Width(50).Height(30).Background(Indigo).Opacity(0.5f).Children(
                        Box.New().Width(120).Height(12).FlexShrink(0).Background(Green))),
                // Rotated and faded itself, with a child that overflows it.
                Faded().Rotate(-12f).Children(Box.New().Width(110).Height(16).FlexShrink(0).Background(Amber)))));
        session.PumpFrames(2);
        GoldenImage.Match(session.Window, "opacity-overflow", MovedOrigin);
    }

    [Fact]
    public void ShadowsOutlinesAndRotationOnTheFadedElement()
    {
        using var session = Session(420, 300);
        session.Window.SetRoot(Flex.Column(gap: 50).Padding(new Thickness(40)).Children(
            Flex.Row(gap: 50).Children(
                Box.New().Width(90).Height(60).Background(Color.White).CornerRadius(10f).Opacity(0.85f)
                    .BoxShadow(0, 12, 32, new Color(15, 23, 42, 90))
                    .Children(Text.New("Shadow").Margin(10)),
                Box.New().Width(90).Height(60).Background(Color.White).CornerRadius(10f).Opacity(0.85f)
                    .BoxShadow(-24, 0, 10, 6, new Color(79, 70, 229, 160))
                    .Outline(3f, Amber, offset: 4f)
                    .Children(Text.New("Outline").Margin(10)),
                // The perf suite's tile: rotated, faded, shadowed.
                Box.New().Width(90).Height(60).Background(Color.White).CornerRadius(10f).Padding(8)
                    .Rotate(4f).Opacity(0.85f).BoxShadow(0, 4, 14, new Color(15, 23, 42, 55))
                    .Children(Text.New("Tile 9").FontSize(12f).FontWeight(FontWeight.SemiBold))),
            Flex.Row(gap: 24).Children(
                // A plain faded box paints one shape and fades without a group.
                Box.New().Width(50).Height(40).Background(Indigo).CornerRadius(8f).Opacity(0.25f),
                Box.New().Width(50).Height(40).Background(Indigo).CornerRadius(8f).Opacity(0.5f),
                Box.New().Width(50).Height(40).Background(Indigo).Opacity(0.85f),
                Box.New().Width(50).Height(40).Background(new Color(239, 68, 68, 128)).Opacity(0.5f),
                Button.New("Disabled").Enabled(false))));
        session.PumpFrames(2);
        GoldenImage.Match(session.Window, "opacity-shadow", MovedOrigin);
    }

    [Fact]
    public void FadedContentInAScrolledViewWithAStickyHeader()
    {
        using var session = Session(300, 220);
        var rows = new Element[12];
        for (int i = 0; i < rows.Length; i++)
            rows[i] = Box.New().Height(28).Margin(new Thickness(4, 12)).Background(Color.White).CornerRadius(6f)
                .BoxShadow(0, 3, 8, new Color(15, 23, 42, 70)).Opacity(i % 2 == 0 ? 0.6f : 1f)
                .Children(Text.New($"Row {i}").Margin(new Thickness(6, 8)));
        var scroll = ScrollView.New().Height(Dimension.Px(220)).Background(Color.FromHex("#EEF2F7")).Children(
            Flex.Column().FlexShrink(0).Children(
                // The whole section fades; its header stays in sight as the section scrolls under it.
                Flex.Column().FlexShrink(0).Opacity(0.75f).Children(
                    [Box.New().Height(26).Background(Indigo).Sticky()
                        .Children(Text.New("Section").TextColor(Color.White).Margin(new Thickness(4, 8))),
                     ..rows]),
                Box.New().Height(300)));
        session.Window.SetRoot(scroll);
        session.PumpFrame();
        scroll.ScrollOffsetY = 75;
        session.PumpFrames(2);
        GoldenImage.Match(session.Window, "opacity-scrolled", MovedOrigin);
    }

    [Fact]
    public void ControlsInAFadedGroup()
    {
        using var session = Session(300, 300);
        session.Window.SetRoot(Flex.Column(gap: 14).Padding(new Thickness(20)).Opacity(0.6f).Children(
            Flex.Row(gap: 16).Children(Checkbox.New(true, "Checked"), RadioButton.New("g", true, "Picked")),
            ToggleSwitch.New(true, "On"),
            Slider.New(0).Width(200),
            Slider.New(100).Width(200),
            ProgressBar.New(0.6).Width(200),
            TextInput.New("Some text").Width(200),
            Dropdown.New(["Alpha", "Beta"], 0).Width(200)));
        session.PumpFrames(3);
        GoldenImage.Match(session.Window, "opacity-controls", MovedOrigin);
    }

    // ── The display list: what the group is bounded to ──

    private static HeadlessSession Recording(int width = 300, int height = 200) =>
        HeadlessSession.Create(width, height, options: new HeadlessOptions { RecordDrawCalls = true, PinnedText = true });

    [Fact]
    public void TheGroupIsBoundedByTheElementAndTheChildThatOverflowsIt()
    {
        using var session = Recording();
        session.Window.SetRoot(Box.New().Padding(new Thickness(20)).Children(
            Faded(0.5f).Children(Box.New().Width(150).Height(24).FlexShrink(0).Background(Red))));
        session.PumpFrame();

        Assert.Contains("PushOpacity 0.5 bounds=(20,20 150x40)", session.Window.RenderLog.ToString());
    }

    [Fact]
    public void TheGroupReachesAsFarAsTheShadowBlurs()
    {
        using var session = Recording();
        // Blur 20 is a sigma of 10, which reaches 30 past the box; the offset moves that down 10.
        session.Window.SetRoot(Box.New().Padding(new Thickness(40)).Children(
            Box.New().Width(100).Height(60).Background(Color.White).Opacity(0.5f)
                .BoxShadow(0, 10, 20, new Color(0, 0, 0, 80))));
        session.PumpFrame();

        Assert.Contains("PushOpacity 0.5 bounds=(10,20 160x120)", session.Window.RenderLog.ToString());
    }

    [Fact]
    public void TheGroupHoldsTextLongerThanItsBox()
    {
        using var session = Recording();
        // A glyph that cannot break, in a box a third of its width; the faded box shrinks to it.
        var faded = Box.New().Opacity(0.5f).Children(Text.New("W").FontSize(40).Width(12));
        session.Window.SetRoot(Flex.Row().Padding(new Thickness(20)).Children(faded));
        session.PumpFrame();

        var bounds = OpacityBounds(session);
        var box = faded.Layout.AbsoluteBorderBox;
        Assert.Equal(12f, box.Width);
        Assert.True(bounds.Right > box.Right + 15, $"bounds {bounds} stop near the faded box {box}");
        Assert.True(bounds.X <= box.X && bounds.Y <= box.Y && bounds.Bottom >= box.Bottom, $"bounds {bounds}");
    }

    [Fact]
    public void ATransformedGroupIsBoundedInItsOwnCoordinates()
    {
        using var session = Recording();
        session.Window.SetRoot(Box.New().Padding(new Thickness(40)).Children(
            Faded(0.5f).Rotate(30f).Children(Box.New().Width(10).Height(10).Background(Red))));
        session.PumpFrame();

        // Inside the transform: the element's own box, which the surface then rotates.
        var log = session.Window.RenderLog.ToString();
        Assert.Contains("PushOpacity 0.5 bounds=(40,40 80x40)", log);
        Assert.True(log.IndexOf("PushTransform", StringComparison.Ordinal)
                    < log.IndexOf("PushOpacity", StringComparison.Ordinal), log);
    }

    [Fact]
    public void APlainBoxFadesWithoutAGroup()
    {
        using var session = Recording();
        session.Window.SetRoot(Box.New().Padding(new Thickness(20)).Children(
            Box.New().Width(50).Height(40).Background(Indigo).CornerRadius(8f).Opacity(0.5f)));
        session.PumpFrame();

        var log = session.Window.RenderLog.ToString();
        Assert.DoesNotContain("PushOpacity", log);
        Assert.Contains("DrawRoundedRect (20,20 50x40) r=8 fill=#4F46E580", log);
    }

    [Fact]
    public void AnElementThatCannotBoundItsPaintLeavesTheGroupUnbounded()
    {
        using var session = Recording();
        session.Window.SetRoot(Box.New().Padding(new Thickness(20)).Children(
            Box.New().Opacity(0.5f).Children(new UnboundedElement().Width(20).Height(20))));
        session.PumpFrame();

        var log = session.Window.RenderLog.ToString();
        Assert.Contains("PushOpacity 0.5", log);
        Assert.DoesNotContain("bounds=", log);
    }

    private sealed class UnboundedElement : Element
    {
        public override Rect? PaintBounds(IRenderContext ctx) => null;
    }

    private static Rect OpacityBounds(HeadlessSession session)
    {
        var line = session.Window.RenderLog.ToString().Split('\n')
            .Single(l => l.TrimStart().StartsWith("PushOpacity", StringComparison.Ordinal));
        var inner = line[(line.IndexOf("bounds=(", StringComparison.Ordinal) + 8)..line.LastIndexOf(')')];
        var parts = inner.Split(' ');
        var xy = parts[0].Split(',');
        var wh = parts[1].Split('x');
        static float P(string s) => float.Parse(s, System.Globalization.CultureInfo.InvariantCulture);
        return new Rect(P(xy[0]), P(xy[1]), P(wh[0]), P(wh[1]));
    }
}
