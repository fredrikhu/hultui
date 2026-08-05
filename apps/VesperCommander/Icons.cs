using HultUI.Core.Builders;
using HultUI.Core.Elements;
using HultUI.Core.Layout;
using HultUI.Core.Primitives;
using HultUI.Rendering.Skia;
using SkiaSharp;

namespace VesperCommander;

public enum IconKind { Folder, Parent, File, Code, Image, Archive }

/// <summary>
/// File-list icons rasterized from the design's inline-SVG glyphs. HultUI's canvas has no
/// arbitrary path fill or arcs, so instead of approximating the vectors with line
/// primitives we parse the real SVG path data with Skia and rasterize each icon to a small
/// tinted bitmap (cached per kind + tint). This reproduces the design exactly — the solid
/// folder, the rounded-corner page outlines, the framed image glyph — rather than a
/// stroke-only lookalike. A <paramref name="tint"/> overrides every part's colour (marked
/// rows go red); otherwise each part uses its design colour.
/// </summary>
public static class Icons
{
    public static IconKind KindFor(FileEntry e)
    {
        if (e.IsParent) return IconKind.Parent;
        if (e.IsDir) return IconKind.Folder;
        return System.IO.Path.GetExtension(e.Name).ToLowerInvariant() switch
        {
            ".cs" or ".js" or ".ts" or ".jsx" or ".tsx" or ".py" or ".go" or ".rs" or ".java"
                or ".c" or ".cpp" or ".h" or ".hpp" or ".json" or ".xml" or ".html" or ".css"
                or ".md" or ".sh" or ".yml" or ".yaml" or ".toml" or ".sql" or ".rb" or ".php" => IconKind.Code,
            ".png" or ".jpg" or ".jpeg" or ".gif" or ".bmp" or ".svg" or ".webp" or ".ico" or ".tiff" => IconKind.Image,
            ".zip" or ".7z" or ".rar" or ".gz" or ".tar" or ".bz2" or ".xz" or ".tgz" => IconKind.Archive,
            _ => IconKind.File,
        };
    }

    public static Element Draw(IconKind kind, Color? tint = null)
    {
        var img = Image.New(Rasterize(kind, tint));
        img.Fit = ImageFit.Contain;
        return img.Width(16).Height(16).FlexShrink(0);
    }

    /// <summary>Drop cached bitmaps after the accent ramp changes (design colours read the
    /// live ramp). Call from the Options "save" path.</summary>
    public static void InvalidateCache()
    {
        foreach (var v in _cache.Values) v.Dispose();
        _cache.Clear();
    }

    private static readonly Dictionary<long, SkiaImageSource> _cache = new();
    private const int Supersample = 32;   // rendered at 2× for a 16px display

    private static SkiaImageSource Rasterize(IconKind kind, Color? tint)
    {
        long key = ((long)(tint is { } t ? Pack(t) : 0xFFFF_FFFFu) << 8) | (byte)kind;
        if (_cache.TryGetValue(key, out var cached)) return cached;

        var bmp = new SKBitmap(Supersample, Supersample, SKColorType.Rgba8888, SKAlphaType.Premul);
        using (var canvas = new SKCanvas(bmp))
        {
            canvas.Clear(SKColors.Transparent);
            canvas.Scale(Supersample / 16f);   // author in the design's 16-unit box
            foreach (var (path, fill, stroke, color) in Parts(kind))
            {
                using var _ = path;
                var c = tint ?? color;
                using var paint = new SKPaint
                {
                    IsAntialias = true,
                    Color = new SKColor(c.R, c.G, c.B, c.A),
                    Style = fill ? SKPaintStyle.Fill : SKPaintStyle.Stroke,
                    StrokeWidth = stroke,
                    StrokeCap = SKStrokeCap.Round,
                    StrokeJoin = SKStrokeJoin.Round,
                };
                canvas.DrawPath(path, paint);
            }
        }
        var src = new SkiaImageSource(bmp);
        _cache[key] = src;
        return src;
    }

    private static uint Pack(Color c) => ((uint)c.A << 24) | ((uint)c.R << 16) | ((uint)c.G << 8) | c.B;

    private static SKPath Svg(string d) => SKPath.ParseSvgPathData(d);

    // The design's file-type glyphs (batch-1/2 handoff sprite). Colours read the live ramp.
    private static IEnumerable<(SKPath path, bool fill, float stroke, Color color)> Parts(IconKind kind)
    {
        const string page = "M4 1.5h5.5L13 5v9a1 1 0 0 1-1 1H4a1 1 0 0 1-1-1V2.5a1 1 0 0 1 1-1z";
        switch (kind)
        {
            case IconKind.Folder:
                yield return (Svg("M1.5 4a1.5 1.5 0 0 1 1.5-1.5h3l1.6 1.8H13A1.5 1.5 0 0 1 14.5 6v6A1.5 1.5 0 0 1 13 13.5H3A1.5 1.5 0 0 1 1.5 12V4z"),
                    true, 0, Theme.Nocturne.Accent600);
                break;

            case IconKind.Parent:
                yield return (Svg("M8 13V4m0 0L4.5 7.5M8 4l3.5 3.5"), false, 1.4f, Theme.Nocturne.Neutral400);
                break;

            case IconKind.Code:
                yield return (Svg(page), false, 1.2f, Theme.Nocturne.Accent500);
                yield return (Svg("M6.4 8.4L5.2 9.6l1.2 1.2m3.2-2.4l1.2 1.2-1.2 1.2"), false, 1.1f, Theme.Nocturne.Accent400);
                break;

            case IconKind.Image:
                var rect = new SKPath();
                rect.AddRoundRect(new SKRect(2.5f, 3, 13.5f, 13), 1.2f, 1.2f);
                yield return (rect, false, 1.2f, Theme.Nocturne.Neutral500);
                var lens = new SKPath();
                lens.AddCircle(6, 6.4f, 1);
                yield return (lens, true, 0, Theme.Nocturne.Neutral500);
                yield return (Svg("M3.4 11.6l3-3 2.4 2.4 1.8-1.6 2 2.2"), false, 1.1f, Theme.Nocturne.Neutral500);
                break;

            case IconKind.Archive:
                yield return (Svg(page), false, 1.2f, Theme.Nocturne.Neutral600);
                yield return (Svg("M7.6 2.5v3.6m1-1.8H7"), false, 1.1f, Theme.Nocturne.Neutral500);
                break;

            default: // File
                yield return (Svg(page), false, 1.2f, Theme.Nocturne.Neutral600);
                yield return (Svg("M9.5 1.5V5H13"), false, 1.2f, Theme.Nocturne.Neutral600);
                break;
        }
    }
}
