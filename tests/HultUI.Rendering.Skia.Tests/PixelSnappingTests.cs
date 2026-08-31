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
}
