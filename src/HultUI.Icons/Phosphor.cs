using HultUI.Core.Builders;
using HultUI.Core.Elements;
using HultUI.Core.Layout;
using HultUI.Core.Primitives;
using HultUI.Core.Rendering;
using HultUI.Rendering.Skia;
using SkiaSharp;

namespace HultUI.Icons;

/// <summary>
/// Phosphor Icons (MIT), regular weight, rasterized from the set's own path data.
///
/// <para>The alternative was bundling the Phosphor font and addressing glyphs by
/// private-use codepoint. That needs font-file registration, which
/// <c>SkiaFontCollection</c> does not have — it resolves by family name only — and it
/// would put icon rendering back on the text pipeline, where size, colour and crispness
/// are the font's business rather than the caller's. Path data needs no framework
/// change and reuses the same cache the file-type glyphs use.</para>
///
/// <para>Names are Phosphor's own, kebab-case: <c>git-branch</c>, <c>arrows-clockwise</c>.
/// Unknown names render nothing rather than throwing, so a typo costs an icon and not
/// a crash — <see cref="Has"/> and the icon-set test catch them instead.</para>
/// </summary>
public static partial class Phosphor
{
    /// <summary>Whether <paramref name="name"/> is in the bundled set.</summary>
    public static bool Has(string name) => Paths.ContainsKey(name);

    /// <summary>Every bundled icon name, for tests and pickers.</summary>
    public static IReadOnlyCollection<string> Names => Paths.Keys;

    /// <summary>
    /// An image element showing <paramref name="name"/> at <paramref name="size"/> in
    /// <paramref name="color"/>. Returns an empty, correctly-sized box for an unknown
    /// name so layout does not shift.
    /// </summary>
    public static Element Draw(string name, float size, Color color, float scale = 1f)
    {
        if (!Paths.ContainsKey(name))
            return Box.New().Width(size).Height(size).FlexShrink(0);

        var img = Image.New(Rasterize(name, color, size, scale));
        img.Fit = ImageFit.Contain;
        return img.Width(size).Height(size).FlexShrink(0);
    }

    /// <summary>
    /// The raster for <paramref name="name"/>, for callers that need an image source
    /// rather than an element — the framework's context menu, which paints its own rows.
    /// Null for an unknown name.
    /// </summary>
    public static IImageSource? Source(string name, float size, Color color, float scale = 1f) =>
        Paths.ContainsKey(name) ? Rasterize(name, color, size, scale) : null;

    /// <summary>Drops every cached raster. Call after a palette change.</summary>
    public static void InvalidateCache()
    {
        lock (Gate)
        {
            foreach (var v in Cache.Values) v.Dispose();
            Cache.Clear();
        }
    }

    private static readonly object Gate = new();
    private static readonly Dictionary<(string, uint, int), SkiaImageSource> Cache = new();

    private static SkiaImageSource Rasterize(string name, Color color, float size, float scale)
    {
        // Rastered at the size it will actually occupy on screen — layout size times the
        // display scale — so the renderer resamples nothing. Anything else is a resample
        // on the way to the screen: a fixed 2x raster looked soft at 100% and chunky at
        // 150%, and hairlines (Phosphor's `minus` bar is 6% of its box) could fall
        // between samples and disappear.
        //
        // The floor keeps a legible raster for very small nominal sizes; the ceiling
        // stops an absurd scale from allocating something huge.
        int px = Math.Clamp((int)MathF.Ceiling(size * scale), 8, 512);
        var key = (name, Pack(color), px);

        lock (Gate)
        {
            if (Cache.TryGetValue(key, out var cached)) return cached;

            var bmp = new SKBitmap(px, px, SKColorType.Rgba8888, SKAlphaType.Premul);
            using (var canvas = new SKCanvas(bmp))
            {
                canvas.Clear(SKColors.Transparent);
                canvas.Scale(px / SourceBox);
                using var path = SKPath.ParseSvgPathData(Paths[name]);
                using var paint = new SKPaint
                {
                    IsAntialias = true,
                    Color = new SKColor(color.R, color.G, color.B, color.A),
                    Style = SKPaintStyle.Fill,
                };
                canvas.DrawPath(path, paint);
            }
            var src = new SkiaImageSource(bmp, alternateText: name);
            Cache[key] = src;
            return src;
        }
    }

    private static uint Pack(Color c) => ((uint)c.A << 24) | ((uint)c.R << 16) | ((uint)c.G << 8) | c.B;
}
