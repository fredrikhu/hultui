using HultUI.Core.Builders;
using HultUI.Core.Elements;
using HultUI.Core.Layout;
using HultUI.Core.Primitives;
using HultUI.Core.Rendering;
using HultUI.Core.Styling;
using HultUI.Rendering.Skia;
using SkiaSharp;
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
    // can shade a step or two differently — up to 9 levels, 1 pixel on Windows and 8 on Linux.
    // Pinning the groups' origin back at the corner made every frame match within the default.
    // Content cut off differs by far more than 16: it is ink against background.
    private static readonly GoldenTolerance MovedOrigin = new(Channel: 16, Pixels: 0);

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

    // ── A faded image draws its picture at its alpha ──

    /// <summary>96×64, drawn at half size as an icon is: an opaque gradient on the left, a
    /// translucent green on the right, a white bar across, so the fade shows in every channel and
    /// in alpha too.</summary>
    private static SkiaImageSource Picture()
    {
        var bitmap = new SKBitmap(96, 64, SKColorType.Rgba8888, SKAlphaType.Premul);
        for (int y = 0; y < 64; y++)
            for (int x = 0; x < 96; x++)
                bitmap.SetPixel(x, y, y is >= 28 and < 36 ? SKColors.White
                    : x < 48 ? new SKColor((byte)(255 - x * 5), 40, (byte)(x * 5))
                    : new SKColor(34, 197, 94, 128));
        return new SkiaImageSource(bitmap);
    }

    [Fact]
    public void AFadedImageDrawsAtItsAlphaWithoutAGroup()
    {
        using var session = Recording();
        using var picture = Picture();
        session.Window.SetRoot(Box.New().Padding(new Thickness(20)).Children(
            Image.New(picture).Width(48).Height(32).Opacity(0.45f)));
        session.PumpFrame();

        var log = session.Window.RenderLog.ToString();
        Assert.DoesNotContain("PushOpacity", log);
        Assert.Contains("DrawImage (20,20 48x32) Contain intrinsic=96x64 opacity=0.45", log);
    }

    /// <summary>The picture faded on its own and faded in a group (a box around it, which fades as
    /// one), side by side over a backdrop, at 1× and 1.5×: every pixel within 2 levels of the
    /// group's, the backdrop showing through as much, and only the group's draw a layer.</summary>
    [Theory]
    [InlineData(1f)]
    [InlineData(1.5f)]
    public void AFadedImageLooksAsItsGroupDid(float scale)
    {
        using var session = HeadlessSession.Create(200, 80, scale,
            options: new HeadlessOptions { RecordDrawCalls = true, PinnedText = true });
        using var picture = Picture();
        var grouped = Absolute(Box.New().Opacity(0.45f).Children(Image.New(picture).Width(48).Height(32)), 20, 20);
        var alone = Absolute(Box.New().Children(Image.New(picture).Width(48).Height(32).Opacity(0.45f)), 120, 20);
        session.Window.SetRoot(Box.New().Width(200).Height(80).Background(Indigo).Children(grouped, alone));
        session.PumpFrame();
        session.Window.RenderLog.Clear();

        using var image = session.Window.CapturePixels();
        using var bitmap = SKBitmap.FromImage(image);
        Assert.Single(session.Window.RenderLog.Named("PushOpacity"));   // the group's, not the image's

        int dx = (int)MathF.Round(100 * scale), x0 = (int)MathF.Round(16 * scale), y0 = (int)MathF.Round(16 * scale);
        int w = (int)MathF.Round(56 * scale), h = (int)MathF.Round(40 * scale);
        int max = 0, faded = 0;
        for (int y = y0; y < y0 + h; y++)
            for (int x = x0; x < x0 + w; x++)
            {
                SKColor a = bitmap.GetPixel(x, y), b = bitmap.GetPixel(x + dx, y);
                max = Math.Max(max, Math.Max(Math.Abs(a.Red - b.Red), Math.Max(Math.Abs(a.Green - b.Green), Math.Abs(a.Blue - b.Blue))));
                if (a != new SKColor(Indigo.R, Indigo.G, Indigo.B)) faded++;
            }
        Assert.True(max <= 2, $"the image alone differs from its group by up to {max} levels");
        Assert.True(faded > 48 * 32 * scale * scale * 0.9, $"only {faded} pixels show the picture");

        // On the bar, white at 45 % over the backdrop: the fade itself, not merely the same as the group's.
        var bar = bitmap.GetPixel(dx + (int)MathF.Round(30 * scale), (int)MathF.Round(36 * scale));
        static int Over(byte backdrop) => (int)MathF.Round(255 * 0.45f + backdrop * 0.55f);
        Assert.InRange(bar.Red, Over(Indigo.R) - 2, Over(Indigo.R) + 2);
        Assert.InRange(bar.Blue, Over(Indigo.B) - 2, Over(Indigo.B) + 2);
    }

    [Fact]
    public void AFadedFrameStreamOrCropKeepsItsGroup()
    {
        using var session = Recording();
        using var picture = Picture();
        using var frames = new PixelFrameSink();
        frames.Write(4, 4, PixelFormat.Rgba8888, Enumerable.Repeat((byte)200, 64).ToArray(), 16);
        session.Window.SetRoot(Flex.Row(gap: 10).Padding(new Thickness(20)).Children(
            Image.New(frames).Width(16).Height(16).Opacity(0.5f),
            new ImageElement { Source = picture, SourceRect = new Rect(0, 0, 48, 32) }.Width(24).Height(16).Opacity(0.5f)));
        session.PumpFrame();

        Assert.Equal(2, session.Window.RenderLog.Named("PushOpacity").Count());
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

    /// <summary>
    /// A faded row of sliders — focused, at either end, one squeezed flatter than its thumb — is
    /// drawn through a group of its own bounds, and loses nothing to them: every pixel is within
    /// the moved origin's few levels of the same row drawn through a group the window's size (an
    /// element no type vouches for beside them). The sliders' bounds were null, so a video
    /// player's bar faded through a window-sized layer (plan 17, round 17).
    /// </summary>
    [Theory]
    [InlineData(1f)]
    [InlineData(1.5f)]
    public void FadedSlidersAreBoundedAndLoseNothing(float scale)
    {
        // Each slider faded alone, in a box no bigger than it, so nothing but the slider's own
        // bounds holds what reaches past its box.
        Element Faded(SliderElement slider, bool unbounded)
        {
            var box = Box.New().Opacity(0.6f).Children(slider);
            if (unbounded) box.AddChild(new UnboundedElement().Width(1).Height(1));
            return box;
        }

        SliderElement? low = null;
        Element Row(bool unbounded)
        {
            low = Slider.New(0).Width(90);
            var high = Slider.New(100).Width(90);
            var flat = Slider.New(60).Width(90).Height(6);
            return Flex.Row(gap: 24).AlignItems(AlignItems.Center).Padding(new Thickness(14, 30)).Children(
                Faded(low, unbounded), Faded(high, unbounded), Faded(flat, unbounded));
        }

        SKBitmap Draw(bool unbounded, out string log)
        {
            using var session = HeadlessSession.Create(420, 90, scale,
                options: new HeadlessOptions { RecordDrawCalls = true, PinnedText = true });
            var row = Row(unbounded);
            session.Window.SetRoot(Box.New().Width(420).Height(90).Background(Color.White).Children(row));
            session.PumpFrame();
            low!.Focus();   // the ring, 3 past the thumb at the track's start
            session.PumpFrame();
            session.Window.RenderLog.Clear();
            using var image = session.Window.CapturePixels();
            log = session.Window.RenderLog.ToString();
            return SKBitmap.FromImage(image);
        }

        using var bounded = Draw(unbounded: false, out var boundedLog);
        using var window = Draw(unbounded: true, out _);
        Assert.Equal(3, boundedLog.Split('\n').Count(l => l.Contains("PushOpacity 0.6 bounds=", StringComparison.Ordinal)));

        int max = 0, far = 0;
        for (int y = 0; y < bounded.Height; y++)
            for (int x = 0; x < bounded.Width; x++)
            {
                SKColor a = bounded.GetPixel(x, y), b = window.GetPixel(x, y);
                int d = Math.Max(Math.Abs(a.Red - b.Red), Math.Max(Math.Abs(a.Green - b.Green), Math.Abs(a.Blue - b.Blue)));
                max = Math.Max(max, d);
                if (d > MovedOrigin.Channel) far++;
            }
        Assert.True(far == 0, $"{far} pixels differ by more than {MovedOrigin.Channel} levels (at most {max}): the bounds cut something off");
    }

    /// <summary>
    /// Faded number fields, each in a box no bigger than itself — one given an edit wider than the
    /// field (its one line keeps what fits), one squeezed shorter than its value's line, one
    /// narrower than its step strip, whose hairline and chevrons then stand left of the box — at
    /// rest, focused mid-edit and held on a step button: each is drawn through a group
    /// of its own bounds and loses nothing to them, every pixel within the moved origin's few
    /// levels of the same row drawn through groups the window's size. The fields' bounds were
    /// null, so Vesper's Multi-rename faded its row of three through a layer of the whole dialog
    /// (plan 17, round 18).
    /// </summary>
    [Theory]
    [InlineData("rest", 1f)]
    [InlineData("rest", 1.5f)]
    [InlineData("editing", 1f)]
    [InlineData("editing", 1.5f)]
    [InlineData("held", 1.5f)]
    public void FadedNumberFieldsAreBoundedAndLoseNothing(string state, float scale)
    {
        NumericUpDownElement? wide = null, flat = null, thin = null;
        Element Faded(NumericUpDownElement field, bool unbounded)
        {
            var box = Box.New().Opacity(0.6f).Children(field);
            if (unbounded) box.AddChild(Pinned(new UnboundedElement().Width(1).Height(1)));
            return box;
        }

        Element Row(bool unbounded)
        {
            wide = NumericUpDown.New(5, 0, 9).Width(60);
            flat = NumericUpDown.New(42, 0, 100).Height(6);
            thin = NumericUpDown.New(7, 0, 9).Width(8);
            return Flex.Row(gap: 40).AlignItems(AlignItems.Center).Padding(new Thickness(14, 30)).Children(
                Faded(wide, unbounded), Faded(flat, unbounded), Faded(thin, unbounded));
        }

        SKBitmap Draw(bool unbounded, out string log)
        {
            using var session = HeadlessSession.Create(420, 90, scale,
                options: new HeadlessOptions { RecordDrawCalls = true, PinnedText = true });
            session.Window.SetRoot(Box.New().Width(420).Height(90).Background(Color.White).Children(Row(unbounded)));
            session.PumpFrame();
            if (state == "editing")
            {
                wide!.Focus();
                session.PumpFrame();
                session.Window.TypeText("123456789012");
                Assert.True(wide.IsEditing, "the edit did not reach the field");
            }
            else if (state == "held")
            {
                var b = thin!.Layout.AbsoluteBorderBox;
                session.Window.MouseDown(b.X + b.Width / 2, b.Y + 3);   // the upper half is the up button
            }
            session.PumpFrame();
            session.Window.RenderLog.Clear();
            session.PumpFrame(); // after the input's frame, the state settled
            using var image = session.Window.CapturePixels();
            log = session.Window.RenderLog.ToString();
            if (state == "held") Assert.Equal(8, thin!.Value);
            return SKBitmap.FromImage(image);
        }

        using var bounded = Draw(unbounded: false, out var boundedLog);
        using var window = Draw(unbounded: true, out _);
        Assert.Equal(3, boundedLog.Split('\n').Count(l => l.Contains("PushOpacity 0.6 bounds=", StringComparison.Ordinal)));

        int max = 0, far = 0;
        for (int y = 0; y < bounded.Height; y++)
            for (int x = 0; x < bounded.Width; x++)
            {
                SKColor a = bounded.GetPixel(x, y), b = window.GetPixel(x, y);
                int d = Math.Max(Math.Abs(a.Red - b.Red), Math.Max(Math.Abs(a.Green - b.Green), Math.Abs(a.Blue - b.Blue)));
                max = Math.Max(max, d);
                if (d > MovedOrigin.Channel) far++;
            }
        Assert.True(far == 0, $"{far} pixels differ by more than {MovedOrigin.Channel} levels (at most {max}): the bounds cut something off");
    }

    private static T Pinned<T>(T el) where T : Element
    {
        el.Position.Type = PositionType.Absolute;
        el.Position.Left = Dimension.Px(0);
        el.Position.Top = Dimension.Px(0);
        return el;
    }

    // ── At opacity 0 a subtree that only paints is not drawn ──

    /// <summary>A box at opacity 0 holding a box, a label, a canvas, a slider and an image: none of
    /// it is drawn, not even the group, and the canvas's callback does not run; brought back to 0.5
    /// it all is. The window's pixels are those of the same scene without the faded box.</summary>
    [Fact]
    public void ASubtreeAtOpacityZeroThatOnlyPaintsIsNotDrawn()
    {
        using var session = Recording();
        using var picture = Picture();
        int draws = 0;
        var faded = Box.New().Width(200).Height(60).Background(Panel).Opacity(0f).Children(
            Box.New().Width(20).Height(20).Background(Red),
            Text.New("Hidden").FontSize(14f),
            Canvas.New(_ => draws++).Width(16).Height(16),
            Slider.New(40).Width(80),
            Image.New(picture).Width(24).Height(16));
        var page = Box.New().Width(300).Height(200).Background(Indigo);
        session.Window.SetRoot(page.Children(Absolute(faded, 20, 20)));
        session.PumpFrame();

        var log = session.Window.RenderLog.ToString();
        Assert.Equal(0, draws);
        Assert.DoesNotContain("PushOpacity", log);
        Assert.DoesNotContain("Hidden", log);
        using (var hidden = session.Window.CapturePixels())
        using (var without = PixelsWithout(page, faded, session))
            Assert.True(SamePixels(hidden, without), "a subtree at opacity 0 changed the window's pixels");

        faded.Opacity = 0.5f;
        session.Window.RenderLog.Clear();
        session.PumpFrame();
        Assert.Equal(1, draws);
        Assert.Contains("PushOpacity 0.5", session.Window.RenderLog.ToString());
    }

    /// <summary>Anything in the subtree that does more in Render than paint keeps the whole subtree
    /// drawn at opacity 0: a scroll view (its extent is worked out there, a list's rows made), a
    /// text field (the context its clicks are measured with), a label that can be selected (the
    /// lines a click reads), and an element no type vouches for.</summary>
    [Theory]
    [InlineData("scroll")]
    [InlineData("field")]
    [InlineData("selectable")]
    [InlineData("custom")]
    public void ASubtreeAtOpacityZeroThatDoesMoreThanPaintIsDrawn(string kind)
    {
        using var session = Recording();
        int draws = 0;
        Element other = kind switch
        {
            "scroll" => ScrollView.New().Height(Dimension.Px(40)).Children(Box.New().Height(200)),
            "field" => TextInput.New("text").Width(80),
            "selectable" => Text.New("Selectable").Selectable(),
            _ => new UnboundedElement().Width(10).Height(10),
        };
        var faded = Flex.Row().Opacity(0f).Children(Canvas.New(_ => draws++).Width(16).Height(16), other);
        session.Window.SetRoot(Box.New().Padding(new Thickness(20)).Children(faded));
        session.PumpFrame();

        Assert.Equal(1, draws);
        Assert.Contains("PushOpacity 0", session.Window.RenderLog.ToString());
        if (other is ScrollViewElement scroll) Assert.Equal(200f, scroll.ContentHeight);
    }

    private static SKImage PixelsWithout(Element page, Element faded, HeadlessSession session)
    {
        page.RemoveChild(faded);
        session.PumpFrame();
        var image = session.Window.CapturePixels();
        page.AddChild(faded);
        session.PumpFrame();
        return image;
    }

    private static bool SamePixels(SKImage a, SKImage b)
    {
        using var pa = a.PeekPixels() ?? throw new InvalidOperationException("no pixels");
        using var pb = b.PeekPixels() ?? throw new InvalidOperationException("no pixels");
        return pa.GetPixelSpan().SequenceEqual(pb.GetPixelSpan());
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
