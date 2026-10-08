using HultUI.Core.Primitives;
using HultUI.Core.Rendering;
using HultUI.Core.Styling;
using HultUI.Text;
using Xunit;

namespace HultUI.Rendering.Skia.Tests;

/// <summary>
/// Who disposes text services. <see cref="SkiaTextShaper"/> keeps a HarfBuzz font per typeface,
/// with a stream over the font file that no finalizer frees: before plan 17's round 1 every
/// window built its own services and none was ever disposed, so each window closed left one per
/// face it had shaped behind (+44 KB of private bytes a window over 600 open/close cycles). A
/// surface now disposes the services it built, and leaves services it was given to their owner —
/// the windowing platform, which shares one set between all its windows.
/// </summary>
public class SkiaTextServicesLifetimeTests
{
    private static ResolvedInheritedStyle Style(string family, FontWeight weight = FontWeight.Regular) => new(
        FontFamily: family, FontSize: 14f, FontWeight: weight,
        TextColor: new Color(20, 20, 20, 255), TextDirection: TextDirection.Ltr, Cursor: CursorType.Default,
        TextAlign: TextAlign.Start, Decoration: TextDecoration.None);

    private static SkiaTextShaper ShaperOf(IRenderSurface surface) =>
        (SkiaTextShaper)((SkiaMeasureContext)surface.MeasureContext).Pipeline.Shaper;

    // Two faces where the machine has them; at least one anywhere.
    private static float MeasureSome(IRenderSurface surface) =>
        surface.MeasureContext.MeasureText("Shaped in a sans face", Style(FontFamilies.SansSerif), float.PositiveInfinity, 1).Width
        + surface.MeasureContext.MeasureText("and a bold one", Style(FontFamilies.SansSerif, FontWeight.Bold), float.PositiveInfinity, 1).Width;

    [Fact]
    public void A_surface_disposes_the_text_services_it_built()
    {
        var surface = new SkiaRasterRenderSurface();
        var shaper = ShaperOf(surface);
        MeasureSome(surface);
        Assert.NotEqual(0, shaper.CachedShaperCount);

        surface.Dispose();

        Assert.Equal(0, shaper.CachedShaperCount);
    }

    [Fact]
    public void A_surface_leaves_text_services_it_was_given_to_their_owner()
    {
        using var text = new SkiaTextServices();
        var first = new SkiaRasterRenderSurface(text);
        using var second = new SkiaRasterRenderSurface(text);
        Assert.Same(first.MeasureContext, second.MeasureContext);

        float width = MeasureSome(first);
        int shapers = text.Shaper.CachedShaperCount;
        Assert.NotEqual(0, shapers);

        first.Dispose();

        Assert.Equal(shapers, text.Shaper.CachedShaperCount);
        Assert.Equal(width, MeasureSome(second));

        text.Dispose();
        Assert.Equal(0, text.Shaper.CachedShaperCount);
    }

    [Fact]
    public void A_disposed_shaper_still_shapes_the_same_and_keeps_nothing()
    {
        // TextMeasure's ambient context can outlive the application that set it, and measuring
        // through it must neither fail nor start leaking again.
        var fonts = new SkiaFontCollection();
        var font = fonts.Resolve(FontFamilies.SansSerif, FontWeight.Regular, FontSlant.Normal);
        var shaper = new SkiaTextShaper();
        var before = shaper.Shape("Office affine 0123", font, 14f);
        var rtlBefore = shaper.Shape("Office affine", font, 14f, rtl: true);
        Assert.Equal(1, shaper.CachedShaperCount);

        shaper.Dispose();
        var after = shaper.Shape("Office affine 0123", font, 14f);
        var rtlAfter = shaper.Shape("Office affine", font, 14f, rtl: true);

        Assert.Equal(0, shaper.CachedShaperCount);
        Assert.Equal(before.Width, after.Width);
        Assert.Equal(before.Glyphs, after.Glyphs);
        Assert.Equal(rtlBefore.Glyphs, rtlAfter.Glyphs);
    }
}
