using HultUI.Core.Styling;
using HultUI.Text;
using SkiaSharp;

namespace HultUI.Rendering.Skia;

/// <summary>
/// The text half of a render surface — font collection, shaper, layout pipeline and
/// measure context — with no GL context, window or display behind it.
///
/// <para>Both render surfaces build one of these. It is a separate object because
/// nothing in it needs a GPU: an app can construct it (and measure text with it)
/// while the OpenGL driver is still loading on another thread, which is what
/// <c>HULTUI_GL_PRELOAD</c> does. See <see cref="SkiaGLRenderSurface"/>.</para>
/// </summary>
public sealed class SkiaTextServices
{
    public SkiaFontCollection Fonts { get; }
    public SkiaTextShaper Shaper { get; }
    public TextPipeline Pipeline { get; }
    public SkiaMeasureContext MeasureContext { get; }

    public SkiaTextServices() : this(new SkiaFontCollection()) { }

    /// <summary>Text services over <paramref name="fonts"/> — a collection with faces of its own
    /// registered, or a pinned one (<see cref="SkiaFontCollection.Pinned"/>).</summary>
    public SkiaTextServices(SkiaFontCollection fonts)
    {
        Fonts = fonts;
        Shaper = new SkiaTextShaper();
        Pipeline = new TextPipeline(Fonts, Shaper);
        MeasureContext = new SkiaMeasureContext(Pipeline);
    }

    /// <summary>
    /// Pulls the OS font manager, the family lookup and the metrics path into memory
    /// for <paramref name="familyName"/>, so the first real measurement doesn't pay
    /// for them. Cheap (~2 ms) and safe to call before any window exists — the point
    /// is to spend it while something slower is running elsewhere.
    /// </summary>
    public void Warm(string familyName = "sans-serif")
    {
        _ = SKFontManager.Default;
        var font = Fonts.Resolve(familyName, FontWeight.Regular, FontSlant.Normal);
        _ = font.LineHeight;
    }
}
