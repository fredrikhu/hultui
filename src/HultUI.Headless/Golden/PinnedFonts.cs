using HultUI.Rendering.Skia;
using SkiaSharp;

namespace HultUI.Headless;

/// <summary>
/// The faces <see cref="HeadlessOptions.PinnedText"/> draws in, read once from this assembly:
/// DejaVu Sans upright and oblique, regular and bold, and DejaVu Sans Mono. Every family name
/// resolves to one of them, so a frame's text depends on these files, not on what the machine
/// has installed.
/// </summary>
internal static class PinnedFonts
{
    private static readonly Lazy<SKTypeface[]> s_faces = new(Load);

    /// <summary>DejaVu Sans, Sans Bold, Sans Oblique, Sans Bold Oblique, Sans Mono.</summary>
    public static IReadOnlyList<SKTypeface> Faces => s_faces.Value;

    /// <summary>The regular proportional face — what canvas-element text draws in.</summary>
    public static SKTypeface Regular => s_faces.Value[0];

    /// <summary>Text services that know only the pinned faces.</summary>
    public static SkiaTextServices CreateServices() => new(SkiaFontCollection.Pinned(s_faces.Value));

    /// <summary>A raster surface over pinned <paramref name="text"/> (<see cref="CreateServices"/>),
    /// drawing it as outlines. The caller keeps <paramref name="text"/>.</summary>
    public static SkiaRasterRenderSurface CreateSurface(SkiaTextServices text)
    {
        return new SkiaRasterRenderSurface(text)
        {
            TextAsOutlines = true,
            CanvasTypeface = Regular,
        };
    }

    private static SKTypeface[] Load() =>
    [
        Read("DejaVuSans.ttf"),
        Read("DejaVuSans-Bold.ttf"),
        Read("DejaVuSans-Oblique.ttf"),
        Read("DejaVuSans-BoldOblique.ttf"),
        Read("DejaVuSansMono.ttf"),
    ];

    private static SKTypeface Read(string file)
    {
        string name = "HultUI.Headless.Fonts." + file;
        using var stream = typeof(PinnedFonts).Assembly.GetManifestResourceStream(name)
            ?? throw new InvalidOperationException($"The embedded font {name} is missing from HultUI.Headless.");
        // Copied into Skia's own memory: the typeface outlives the resource stream.
        using var data = SKData.Create(stream);
        return SKTypeface.FromData(data)
            ?? throw new InvalidOperationException($"Skia could not read the embedded font {name}.");
    }
}
