using HultUI.Core.Builders;
using HultUI.Core.Elements;
using HultUI.Core.Layout;
using HultUI.Core.Primitives;
using HultUI.Rendering.Skia;
using HultUI.Icons;
using SkiaSharp;

namespace VesperCommander;

public enum IconKind { Folder, Parent, File, Code, Image, Archive, Solution, Project, ViewDetail, ViewBrief, ViewThumb, NavBack, NavForward, NavCaret }

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
    /// <summary>
    /// Device pixels per layout unit, kept in step with the window by
    /// <see cref="CommanderApp"/>. Icons rasterize at this scale so the renderer never
    /// resamples them — the same treatment Junction's Phosphor glyphs get, so the two
    /// apps render shared artwork identically.
    /// </summary>
    public static float DisplayScale
    {
        get => _displayScale;
        set
        {
            if (MathF.Abs(_displayScale - value) < 0.001f) return;
            _displayScale = value;
            InvalidateCache();   // every cached raster was cut for the old scale
        }
    }
    private static float _displayScale = 1f;

    /// <summary>The app's Nocturne ramp, handed to the shared icon library.</summary>
    public static FileIconPalette Palette => new(
        Theme.Nocturne.Accent400, Theme.Nocturne.Accent500, Theme.Nocturne.Accent600,
        Theme.Nocturne.Neutral400, Theme.Nocturne.Neutral500, Theme.Nocturne.Neutral600);

    /// <summary>
    /// Which glyph a row gets. Delegates to <see cref="FileIcons.KindFor"/> so Vesper and
    /// Junction classify files identically — the classification is driven by the syntax
    /// highlighter, and a second copy of it here is exactly what drifted before.
    /// </summary>
    public static IconKind KindFor(FileEntry e) => FileIcons.KindFor(e.Name, e.IsDir, e.IsParent) switch
    {
        FileIconKind.Folder => IconKind.Folder,
        FileIconKind.Parent => IconKind.Parent,
        FileIconKind.Code => IconKind.Code,
        FileIconKind.Image => IconKind.Image,
        FileIconKind.Archive => IconKind.Archive,
        FileIconKind.Solution => IconKind.Solution,
        FileIconKind.Project => IconKind.Project,
        _ => IconKind.File,
    };

    private static FileIconKind? SharedKind(IconKind kind) => kind switch
    {
        IconKind.Folder => FileIconKind.Folder,
        IconKind.Parent => FileIconKind.Parent,
        IconKind.File => FileIconKind.File,
        IconKind.Code => FileIconKind.Code,
        IconKind.Image => FileIconKind.Image,
        IconKind.Archive => FileIconKind.Archive,
        IconKind.Solution => FileIconKind.Solution,
        IconKind.Project => FileIconKind.Project,
        _ => null,   // Vesper-only chrome (view toggles, nav chevrons)
    };

    public static Element Draw(IconKind kind, Color? tint = null, float size = 16f)
    {
        if (SharedKind(kind) is { } shared) return FileIcons.Draw(shared, Palette, tint, size, DisplayScale);

        var img = Image.New(Rasterize(kind, tint));
        img.Fit = ImageFit.Contain;
        return img.Width(size).Height(size).FlexShrink(0);
    }

    /// <summary>Drop cached bitmaps after the accent ramp changes (design colours read the
    /// live ramp). Call from the Options "save" path.</summary>
    public static void InvalidateCache()
    {
        foreach (var v in _cache.Values) v.Dispose();
        _cache.Clear();
        FileIcons.InvalidateCache();   // the shared glyphs read the same live ramp
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

    private static SKPath Rect(float x, float y, float w, float h, float r)
    {
        var p = new SKPath();
        p.AddRoundRect(new SKRect(x, y, x + w, y + h), r, r);
        return p;
    }

    // The design's file-type glyphs (batch-1/2 handoff sprite). Colours read the live ramp.
    private static IEnumerable<(SKPath path, bool fill, float stroke, Color color)> Parts(IconKind kind)
    {
        switch (kind)
        {
            // View-mode toggle glyphs (rows / two columns / 2×2 grid). Filled rounded
            // rects; the toggle passes a tint so idle/hover/active colours override these.
            case IconKind.ViewDetail:
                foreach (float y in new[] { 3f, 7f, 11f })
                {
                    yield return (Rect(2, y, 2, 2, 0.6f), true, 0, Theme.Nocturne.Neutral500);
                    yield return (Rect(6, y, 8, 2, 1f), true, 0, Theme.Nocturne.Neutral500);
                }
                break;

            case IconKind.ViewBrief:
                foreach (float x in new[] { 2f, 9f })
                    foreach (float y in new[] { 3f, 7f, 11f })
                        yield return (Rect(x, y, 5, 2, 1f), true, 0, Theme.Nocturne.Neutral500);
                break;

            case IconKind.ViewThumb:
                foreach (float x in new[] { 2f, 8.8f })
                    foreach (float y in new[] { 2f, 8.8f })
                        yield return (Rect(x, y, 5.2f, 5.2f, 1f), true, 0, Theme.Nocturne.Neutral500);
                break;

            // Navigation chevrons (design §A mock: 16-unit box, 1.4 stroke, round caps).
            // The caller tints these per state (Neutral400 enabled / Neutral700 disabled).
            case IconKind.NavBack:
                yield return (Svg("M9.5 12L5.5 8l4-4"), false, 1.4f, Theme.Nocturne.Neutral400);
                break;

            case IconKind.NavForward:
                yield return (Svg("M6.5 4l4 4-4 4"), false, 1.4f, Theme.Nocturne.Neutral400);
                break;

            case IconKind.NavCaret:
                yield return (Svg("M5 6.5l3 3 3-3"), false, 1.4f, Theme.Nocturne.Neutral400);
                break;

            default:
                yield break;   // file-type kinds never reach here (see SharedKind)
        }
    }
}
