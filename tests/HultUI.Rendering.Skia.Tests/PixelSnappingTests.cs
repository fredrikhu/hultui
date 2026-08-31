using HultUI.Core.Layout;
using HultUI.Core.Primitives;
using HultUI.Rendering.Skia;
using Xunit;

namespace HultUI.Rendering.Skia.Tests;

/// <summary>
/// A border lands on whole device pixels.
///
/// <para>Every other pixel test in this project runs at scale 1, where the interesting part of
/// this is invisible — so these exist to run at 1.25 and 1.5, the scales Windows actually uses.
/// Without them a snapping bug passes the whole suite.</para>
///
/// <para>The measured starting point, a 1-DIP border on a box edge, coverage per device pixel:
/// at 1.0x <c>128 128</c> — two half-covered pixels, so even at 100% a hairline was never crisp;
/// at 1.5x <c>192 192</c>; at 1.25x <c>32 255 32</c>. The stroke was centred on the box edge, so
/// it straddled it and spilled half its width outside the border box. WinUI, measured off the
/// Gallery at 150%, paints two fully-covered pixels and nothing either side.</para>
///
/// <para>Coverage is read off the green channel of a red border on white: 255 means fully the
/// border colour, 0 means untouched.</para>
/// </summary>
public class PixelSnappingTests
{
    /// <summary>Coverage across the box's left edge, device pixel by device pixel, for a border
    /// of <paramref name="widthDip"/> on a box spanning 10..30 DIPs.</summary>
    private static int[] EdgeProfile(float scale, float widthDip, float radius = 0f, bool perSide = false)
    {
        int px = (int)(40 * scale);
        using var surface = new SkiaRasterRenderSurface();
        surface.Resize(px, px, scale);
        var ctx = surface.RenderContext;

        ctx.BeginFrame(40, 40, scale, Color.White);
        var box = new Rect(10, 10, 20, 20);
        if (perSide)
            ctx.DrawBorder(box, new CornerRadius(radius), Color.Red,
                new Thickness(widthDip, 0, 0, widthDip), BorderStyle.Solid);
        else
            ctx.DrawRoundedRect(box, new CornerRadius(radius), Color.Transparent, Color.Red,
                new Thickness(widthDip));
        ctx.EndFrame();
        surface.Present();
        var p = surface.ReadPixels(out _, out _, out int rowBytes);

        int y = (int)(20 * scale);
        int from = (int)(7 * scale), to = (int)(14 * scale);
        var profile = new int[to - from + 1];
        for (int x = from; x <= to; x++)
            profile[x - from] = 255 - p[y * rowBytes + x * 4 + 1];
        return profile;
    }

    /// <summary>Every value is either fully covered or not covered at all — no partial pixel
    /// anywhere along a straight run. This is the whole property, stated once.</summary>
    private static void AssertCrisp(int[] profile, int expectedCovered, float scale, string what)
    {
        var partial = profile.Where(v => v is > 8 and < 247).ToArray();
        Assert.True(partial.Length == 0,
            $"{what} at {scale}x has {partial.Length} partial pixel(s) ({string.Join(",", partial)}) " +
            $"in [{string.Join(" ", profile)}]");
        Assert.Equal(expectedCovered, profile.Count(v => v >= 247));
    }

    /// <summary>
    /// A 1-DIP border is one whole device pixel at 100%, and two at 150%.
    ///
    /// <para>Two at 150% rather than one-and-a-half: a border with a width rounds up to the pixel
    /// grid rather than disappearing into it, which is also what keeps a hairline visible at
    /// scales below 1. Both numbers are what the WinUI Gallery paints.</para>
    /// </summary>
    [Theory]
    [InlineData(1.0f, 1)]
    [InlineData(1.25f, 1)]
    [InlineData(1.5f, 2)]
    [InlineData(2.0f, 2)]
    public void AHairlineIsWholePixels(float scale, int expected)
        => AssertCrisp(EdgeProfile(scale, 1f), expected, scale, "uniform border");

    /// <summary>The per-side path snaps the same way. It is a separate code path with its own
    /// geometry — it insets where the uniform path centred — so it needs its own assertion or
    /// half the borders in the apps stay soft.</summary>
    [Theory]
    [InlineData(1.0f, 1)]
    [InlineData(1.5f, 2)]
    public void ASingleSidedBorderSnapsToo(float scale, int expected)
        => AssertCrisp(EdgeProfile(scale, 1f, perSide: true), expected, scale, "per-side border");

    /// <summary>And so does the straight run of a rounded border — the sample's controls are all
    /// rounded, so a rule that only held for square corners would miss most of them.</summary>
    [Theory]
    [InlineData(1.0f, 1)]
    [InlineData(1.5f, 2)]
    public void TheStraightRunOfARoundedBorderSnaps(float scale, int expected)
        => AssertCrisp(EdgeProfile(scale, 1f, radius: 4f), expected, scale, "rounded border");

    /// <summary>
    /// Thicker borders keep their weight rather than all collapsing to the same crisp line.
    ///
    /// <para>Without this, "snap the width to at least one pixel" passes while quietly painting
    /// every border 1px wide. 2 DIPs at 150% is 3 device pixels, which is also the case that
    /// says the width is being rounded on its own rather than doubled from the 1-DIP answer.</para>
    /// </summary>
    [Theory]
    [InlineData(2f, 1.0f, 2)]
    [InlineData(2f, 1.5f, 3)]
    [InlineData(3f, 1.0f, 3)]
    public void AThickerBorderKeepsItsWeight(float widthDip, float scale, int expected)
        => AssertCrisp(EdgeProfile(scale, widthDip), expected, scale, $"{widthDip}dip border");

    /// <summary>
    /// A border thinner than a device pixel still paints one.
    ///
    /// <para>Rounding a width to the grid can round it to nothing, and a hairline that asked to
    /// exist should not disappear because of where the window happens to be scaled — CSS makes
    /// the same guarantee for a sub-pixel border. 0.4 DIP at 100% rounds to zero without the
    /// clamp, and the border vanishes entirely rather than merely going soft, which is a worse
    /// bug than the one this whole change is fixing.</para>
    /// </summary>
    [Theory]
    [InlineData(0.4f, 1.0f)]
    [InlineData(0.25f, 1.5f)]
    public void AHairlineTooThinForThePixelGridStillShows(float widthDip, float scale)
        => AssertCrisp(EdgeProfile(scale, widthDip), 1, scale, $"{widthDip}dip border");

    /// <summary>
    /// The band lands inside the border box, not straddling it.
    ///
    /// <para>Where it snaps to matters as much as that it snaps: a stroke centred on the edge
    /// spills half its width outside the element, which is why a bottom-only border used to poke
    /// out past a rounded control. CSS puts a border inside the border box; so does this. The
    /// box starts at DIP 10, so the first covered device pixel is at exactly 10 * scale and the
    /// one before it is clean.</para>
    /// </summary>
    [Theory]
    [InlineData(1.0f)]
    [InlineData(1.5f)]
    [InlineData(2.0f)]
    public void TheBandSitsInsideTheBox(float scale)
    {
        var profile = EdgeProfile(scale, 1f);
        int first = Array.FindIndex(profile, v => v >= 247);
        int startDevice = (int)(7 * scale) + first;

        Assert.Equal((int)(10 * scale), startDevice);
    }

    /// <summary>
    /// Two boxes meeting at one coordinate stay met.
    ///
    /// <para>This is why edges are snapped and the width derived from them, rather than position
    /// and size being rounded independently: independent rounding lets a shared edge land on two
    /// different pixels, which opens a one-pixel gap between things that were flush. Here the two
    /// fills abut at DIP 20 — at 1.5x that is device 30, which is not a pixel boundary either box
    /// would reach on its own.</para>
    /// </summary>
    [Theory]
    [InlineData(1.25f)]
    [InlineData(1.5f)]
    public void AdjacentEdgesDoNotOpenAGap(float scale)
    {
        int px = (int)(40 * scale);
        using var surface = new SkiaRasterRenderSurface();
        surface.Resize(px, px, scale);
        var ctx = surface.RenderContext;

        ctx.BeginFrame(40, 40, scale, Color.White);
        ctx.DrawBorder(new Rect(10, 10, 10, 20), CornerRadius.Zero, Color.Red,
            new Thickness(0, 1f, 0, 0), BorderStyle.Solid);   // right edge at 20
        ctx.DrawBorder(new Rect(20, 10, 10, 20), CornerRadius.Zero, Color.Red,
            new Thickness(0, 0, 0, 1f), BorderStyle.Solid);   // left edge at 20
        ctx.EndFrame();
        surface.Present();
        var p = surface.ReadPixels(out _, out _, out int rowBytes);

        // The two bands are back to back across DIP 20. The pixel each side of that seam is the
        // whole claim — the bands are only one device pixel wide at 1.25x, so anything wider
        // would be asserting about pixels neither border was ever going to reach.
        int y = (int)(20 * scale);
        int seam = (int)MathF.Round(20 * scale, MidpointRounding.AwayFromZero);
        foreach (int x in new[] { seam - 1, seam })
        {
            int cov = 255 - p[y * rowBytes + x * 4 + 1];
            Assert.True(cov >= 247, $"gap at device x={x} (coverage {cov}) at {scale}x");
        }
    }

    /// <summary>
    /// Corner arcs stay antialiased.
    ///
    /// <para>Snapping a curve would step it, and the fix for a soft hairline must not buy
    /// crispness by making every rounded control look jagged. WinUI draws it this way too —
    /// measured on the Gallery's content pane at 150%, the straight run is two solid pixels and
    /// the arc runs through #1F1F1F, #1E1E1E, #232323. So the arc is <i>required</i> to contain
    /// partial coverage, which is the opposite of what every other test here asserts.</para>
    /// </summary>
    [Fact]
    public void ArcsAreStillSmooth()
    {
        const float scale = 1.5f;
        int px = (int)(40 * scale);
        using var surface = new SkiaRasterRenderSurface();
        surface.Resize(px, px, scale);
        var ctx = surface.RenderContext;

        ctx.BeginFrame(40, 40, scale, Color.White);
        ctx.DrawRoundedRect(new Rect(10, 10, 20, 20), new CornerRadius(6f),
            Color.Transparent, Color.Red, new Thickness(1f));
        ctx.EndFrame();
        surface.Present();
        var p = surface.ReadPixels(out _, out _, out int rowBytes);

        // Walk the top-left corner's diagonal, well inside the arc.
        int partial = 0;
        for (int i = (int)(11 * scale); i < (int)(16 * scale); i++)
        {
            int cov = 255 - p[i * rowBytes + i * 4 + 1];
            if (cov is > 8 and < 247) partial++;
        }
        Assert.True(partial > 0, "the arc should be antialiased, not stepped onto the pixel grid");
    }

    // ── Fills ────────────────────────────────────────────────────────────────────────────

    /// <summary>Renders through <paramref name="draw"/> and returns coverage down a column.</summary>
    private static int[] Column(float scale, int atDip, Action<HultUI.Core.Rendering.IRenderContext> draw, int fromDip = 7, int toDip = 14)
    {
        int px = (int)(40 * scale);
        using var surface = new SkiaRasterRenderSurface();
        surface.Resize(px, px, scale);
        var ctx = surface.RenderContext;

        ctx.BeginFrame(40, 40, scale, Color.White);
        draw(ctx);
        ctx.EndFrame();
        surface.Present();
        var p = surface.ReadPixels(out _, out _, out int rowBytes);

        int x = (int)(atDip * scale);
        int from = (int)(fromDip * scale), to = (int)(toDip * scale);
        var profile = new int[to - from + 1];
        for (int y = from; y <= to; y++)
            profile[y - from] = 255 - p[y * rowBytes + x * 4 + 1];
        return profile;
    }

    /// <summary>
    /// A filled rule is whole pixels too — a caret, a selection band and a divider are all this.
    ///
    /// <para>Fills went unsnapped in the first pass, so a 1-DIP divider stayed soft while the
    /// border beside it had gone crisp. The rects here are placed off the grid on purpose: at
    /// 1.0x a rule at y=10.4 covered two pixels at 60% and 40%.</para>
    ///
    /// <para>How many pixels it lands on depends on where it starts, and that is the rule
    /// working rather than failing. The size comes from the snapped edges, so a 1-DIP rule at
    /// 1.5x is one device pixel at y=10.4 (15.6 and 17.1 round to 16 and 17) and two at y=10.0
    /// (15 and 16.5 round to 15 and 17). Deriving a position-independent size instead is exactly
    /// the mistake that opens seams between things that should tile — this test asserting a
    /// single number for 1.5x was that mistake, and it failed against correct code.</para>
    /// </summary>
    [Theory]
    [InlineData(10.4f, 1.0f, 1)]
    [InlineData(10.0f, 1.0f, 1)]
    [InlineData(10.4f, 1.5f, 1)]
    [InlineData(10.0f, 1.5f, 2)]
    public void AFilledRuleIsWholePixels(float y, float scale, int expected)
        => AssertCrisp(Column(scale, 20, c => c.DrawRect(new Rect(5, y, 30, 1f), Color.Red)),
            expected, scale, $"filled rule at y={y}");

    /// <summary>
    /// A rule too thin for the grid still shows, and an empty one still does not.
    ///
    /// <para>Two halves of one rule, and they pull in opposite directions. Rounding both edges of
    /// a 0.3-DIP rect to the same pixel erases it, and a caret or divider vanishing is worse than
    /// it being soft. But the same clamp applied to a genuinely empty rect — an unselected range
    /// is a zero-width one, drawn every frame a text field has focus — would smear a one-pixel
    /// band across the screen. So the clamp is on "was asked for at all", not on "is small".</para>
    /// </summary>
    [Fact]
    public void AThinRuleSurvivesAndAnEmptyOneStaysEmpty()
    {
        var thin = Column(1.0f, 20, c => c.DrawRect(new Rect(5, 10.6f, 30, 0.3f), Color.Red));
        Assert.Equal(1, thin.Count(v => v >= 247));

        var empty = Column(1.0f, 20, c => c.DrawRect(new Rect(5, 10.6f, 30, 0f), Color.Red));
        Assert.True(empty.All(v => v < 8), $"an empty rect painted something: [{string.Join(" ", empty)}]");
    }

    /// <summary>An axis-aligned line snaps like a rule does — the context menu's separator is
    /// one of these.</summary>
    [Theory]
    [InlineData(1.0f, 1)]
    [InlineData(1.5f, 2)]
    public void AHorizontalLineSnaps(float scale, int expected)
        => AssertCrisp(Column(scale, 20,
            c => c.DrawLine(new Point(5, 10.4f), new Point(35, 10.4f), Color.Red, 1f)),
            expected, scale, "horizontal line");

    /// <summary>A stroke width is rounded on its own, unlike a fill's — a line has a width
    /// rather than two edges, so there is nothing to derive it from. That keeps a 1-DIP rule the
    /// same weight wherever it sits, which for a menu separator matters more than tiling.</summary>
    [Theory]
    [InlineData(2f, 1.5f, 3)]
    [InlineData(1f, 2.0f, 2)]
    public void ALineKeepsItsWeightWhereverItSits(float width, float scale, int expected)
        => AssertCrisp(Column(scale, 20,
            c => c.DrawLine(new Point(5, 10.4f), new Point(35, 10.4f), Color.Red, width)),
            expected, scale, $"{width}dip line");

    /// <summary>
    /// A diagonal line does not snap.
    ///
    /// <para>Every diagonal in the framework is a glyph drawn by hand — the checkmark in a
    /// checkbox, the two legs of a chevron. Forcing their endpoints onto the grid kinks the
    /// shape instead of sharpening it, and a checkmark with a bend in it is a worse result than
    /// a soft one. So this is the one case here that <i>requires</i> partial coverage.</para>
    /// </summary>
    [Fact]
    public void ADiagonalLineIsLeftAlone()
    {
        const float scale = 1.5f;
        int px = (int)(40 * scale);
        using var surface = new SkiaRasterRenderSurface();
        surface.Resize(px, px, scale);
        var ctx = surface.RenderContext;

        ctx.BeginFrame(40, 40, scale, Color.White);
        ctx.DrawLine(new Point(10.4f, 10.4f), new Point(29.6f, 25.3f), Color.Red, 1f);
        ctx.EndFrame();
        surface.Present();
        var p = surface.ReadPixels(out _, out _, out int rowBytes);

        int partial = 0;
        for (int x = (int)(12 * scale); x < (int)(28 * scale); x++)
            for (int y = (int)(10 * scale); y < (int)(26 * scale); y++)
            {
                int cov = 255 - p[y * rowBytes + x * 4 + 1];
                if (cov is > 8 and < 247) partial++;
            }
        Assert.True(partial > 20, $"the diagonal should be antialiased along its length ({partial} partial pixels)");
    }

    /// <summary>
    /// Two backgrounds meeting at one coordinate leave no seam.
    ///
    /// <para>The fill counterpart of the border case, and the reason backgrounds snap at all
    /// rather than being left exact: two boxes abutting at a fractional coordinate each painted
    /// half of the pixel they shared, which showed as a pale line between them. Snapping the
    /// shared edge sends both to the same pixel.</para>
    /// </summary>
    [Theory]
    [InlineData(1.25f)]
    [InlineData(1.5f)]
    public void AdjacentBackgroundsLeaveNoSeam(float scale)
    {
        int px = (int)(40 * scale);
        using var surface = new SkiaRasterRenderSurface();
        surface.Resize(px, px, scale);
        var ctx = surface.RenderContext;

        ctx.BeginFrame(40, 40, scale, Color.White);
        // The shared edge has to be off the grid, or there is nothing for snapping to do and this
        // passes either way — at x=20 both 1.25x and 1.5x land on a whole pixel already, which is
        // how the first version of this test managed to pass with background snapping removed.
        ctx.DrawRoundedRect(new Rect(5, 10.4f, 15.4f, 10), CornerRadius.Zero, Color.Red, Color.Transparent, default);
        ctx.DrawRoundedRect(new Rect(20.4f, 10.4f, 14.6f, 10), CornerRadius.Zero, Color.Red, Color.Transparent, default);
        ctx.EndFrame();
        surface.Present();
        var p = surface.ReadPixels(out _, out _, out int rowBytes);

        int y = (int)(15 * scale);
        int seam = (int)MathF.Round(20.4f * scale, MidpointRounding.AwayFromZero);
        foreach (int x in new[] { seam - 1, seam })
        {
            int cov = 255 - p[y * rowBytes + x * 4 + 1];
            Assert.True(cov >= 247, $"seam at device x={x} (coverage {cov}) at {scale}x");
        }
    }

    /// <summary>
    /// A background does not paint outside its own border at the corners.
    ///
    /// <para>Reported by eye on the sample's accent swatches, and a regression from moving the
    /// stroke inside the box: its outer edge then coincided exactly with the fill's antialiased
    /// arc, and two antialiased curves on one boundary blend rather than one covering the other.
    /// The straight runs stayed clean — they are hard-edged on both sides — so this shows only at
    /// a corner, and only on a rounded box. Measured on the swatch: the border pixel was #3A5662
    /// at the corner against #252525 along the edge, visibly tinted by the fill behind it.</para>
    ///
    /// <para>Red fill, blue border, white ground. Any blend of white and blue keeps B at 255,
    /// because both have it; only red can pull it down. So B below 255 outside the box outline
    /// is fill that escaped. The outline crosses the 45 degree diagonal 6 from the corner arc's
    /// centre at (16,16) — at 11.76 — so pixel 11 is the last one wholly outside it.</para>
    /// </summary>
    [Theory]
    [InlineData(1.0f)]
    [InlineData(1.5f)]
    public void AFillDoesNotEscapePastARoundedBorder(float scale)
    {
        int px = (int)(40 * scale);
        using var surface = new SkiaRasterRenderSurface();
        surface.Resize(px, px, scale);
        var ctx = surface.RenderContext;

        ctx.BeginFrame(40, 40, scale, Color.White);
        ctx.DrawRoundedRect(new Rect(10, 10, 20, 20), new CornerRadius(6f),
            new Color(255, 0, 0), new Color(0, 0, 255), new Thickness(1f));
        ctx.EndFrame();
        surface.Present();
        var p = surface.ReadPixels(out _, out _, out int rowBytes);

        for (int d = (int)(10 * scale); d <= (int)(11.7f * scale); d++)
        {
            int i = d * rowBytes + d * 4;
            Assert.True(p[i + 2] >= 253,
                $"fill escaped at device ({d},{d}) at {scale}x: R{p[i]} G{p[i + 1]} B{p[i + 2]}");
        }
    }
}
