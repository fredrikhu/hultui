using HultUI.Core.Builders;
using HultUI.Core.Elements;
using HultUI.Core.Layout;
using HultUI.Core.Primitives;
using HultUI.Rendering.Skia;
using HultUI.Syntax;
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
    public static IconKind KindFor(FileEntry e)
    {
        if (e.IsParent) return IconKind.Parent;
        if (e.IsDir) return IconKind.Folder;
        // Families with a glyph of their own come first: several are also highlightable
        // (.svg is XML, .csproj is XML), and the specific icon is the more useful one.
        return System.IO.Path.GetExtension(e.Name).ToLowerInvariant() switch
        {
            ".png" or ".jpg" or ".jpeg" or ".gif" or ".bmp" or ".svg" or ".webp" or ".ico" or ".tiff" => IconKind.Image,
            ".zip" or ".7z" or ".rar" or ".gz" or ".tar" or ".bz2" or ".xz" or ".tgz" => IconKind.Archive,
            ".sln" or ".slnx" or ".slnf" => IconKind.Solution,
            ".csproj" or ".fsproj" or ".vbproj" or ".vcxproj" or ".shproj" or ".esproj"
                or ".pyproj" or ".njsproj" or ".props" or ".targets" => IconKind.Project,

            // Everything else defers to the highlighter. Keeping a second hand-written
            // list of "code" extensions here is what made .csproj and .ps1 show a blank
            // page icon while the preview would happily have coloured them — and it was
            // this same list the preview gated on, so the two drifted together.
            _ => SyntaxHighlighter.CanHighlight(e.Name) ? IconKind.Code : IconKind.File,
        };
    }

    public static Element Draw(IconKind kind, Color? tint = null, float size = 16f)
    {
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
        const string page = "M4 1.5h5.5L13 5v9a1 1 0 0 1-1 1H4a1 1 0 0 1-1-1V2.5a1 1 0 0 1 1-1z";
        // The Visual Studio family mark: a folded ribbon, drawn as a stroked lemniscate.
        // Full size for a solution, inset into the page body for a project.
        const string ribbon = "M3 8c0-2.2 3-2.2 5 0s5 2.2 5 0-3-2.2-5 0-5 2.2-5 0z";
        const string ribbonInset = "M4.6 10.2c0-1.5 2-1.5 3.4 0s3.4 1.5 3.4 0-2-1.5-3.4 0-3.4 1.5-3.4 0z";
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

            // Visual Studio family — an original glyph in the house style, not a
            // reproduction of Microsoft's mark. It takes the idea the VS logo is built
            // on (a ribbon folded into an infinity) and draws it the way every other
            // icon here is drawn: one stroked path, round caps, no fill.
            //
            // The literal silhouette was tried first and abandoned: reduced to two
            // triangular wings it reads as media transport arrows at 16px, whichever
            // way the gap and offset are tuned, and being solid it was also the only
            // filled glyph in a stroke-based set. The ribbon survives the size.
            //
            // A solution is a container, so it gets the bare mark the way Folder does;
            // a project is a document, so it gets the page with the mark set into it.
            case IconKind.Solution:
                yield return (Svg(ribbon), false, 1.3f, Theme.Nocturne.Accent400);
                break;

            case IconKind.Project:
                yield return (Svg(page), false, 1.2f, Theme.Nocturne.Accent600);
                yield return (Svg(ribbonInset), false, 1.1f, Theme.Nocturne.Accent400);
                break;

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

            default: // File
                yield return (Svg(page), false, 1.2f, Theme.Nocturne.Neutral600);
                yield return (Svg("M9.5 1.5V5H13"), false, 1.2f, Theme.Nocturne.Neutral600);
                break;
        }
    }
}
