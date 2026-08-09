using HultUI.Core.Builders;
using HultUI.Core.Elements;
using HultUI.Core.Layout;
using HultUI.Core.Primitives;
using HultUI.Rendering.Skia;
using HultUI.Syntax;
using SkiaSharp;

namespace HultUI.Icons;

/// <summary>What kind of thing a row represents, and therefore which glyph it gets.</summary>
public enum FileIconKind { Folder, Parent, File, Code, Image, Archive, Solution, Project }

/// <summary>
/// The ramp stops the glyphs are drawn from. Each app passes its own theme's values, so
/// the icons sit in that app's palette without this library knowing about either theme.
/// </summary>
public readonly record struct FileIconPalette(
    Color Accent400, Color Accent500, Color Accent600,
    Color Neutral400, Color Neutral500, Color Neutral600);

/// <summary>
/// File-list icons, rasterized from SVG path data to small tinted bitmaps (cached per
/// kind + palette + tint). HultUI's canvas has no arbitrary path fill or arcs, so rather
/// than approximate the vectors with line primitives the real path data is parsed by
/// Skia — which is what keeps the solid folder, the rounded page outlines, and the
/// Visual Studio notches exact rather than lookalikes.
/// </summary>
public static class FileIcons
{
    /// <summary>
    /// The glyph for a directory entry. Families with an icon of their own are matched
    /// first — <c>.svg</c> is XML and <c>.csproj</c> is XML, but the specific glyph is
    /// the more useful one — and everything else defers to the syntax highlighter.
    ///
    /// <para>That last step is why this lives in one place: a per-app list of "code"
    /// extensions drifts from the grammar set, and two apps' lists drift from each
    /// other. Whatever can be coloured is code, in every app, by construction.</para>
    /// </summary>
    public static FileIconKind KindFor(string name, bool isDir = false, bool isParent = false)
    {
        if (isParent) return FileIconKind.Parent;
        if (isDir) return FileIconKind.Folder;

        return System.IO.Path.GetExtension(name).ToLowerInvariant() switch
        {
            ".png" or ".jpg" or ".jpeg" or ".gif" or ".bmp" or ".svg" or ".webp" or ".ico" or ".tiff"
                => FileIconKind.Image,
            ".zip" or ".7z" or ".rar" or ".gz" or ".tar" or ".bz2" or ".xz" or ".tgz"
                => FileIconKind.Archive,
            ".sln" or ".slnx" or ".slnf" => FileIconKind.Solution,
            ".csproj" or ".fsproj" or ".vbproj" or ".vcxproj" or ".shproj" or ".esproj"
                or ".pyproj" or ".njsproj" or ".props" or ".targets" => FileIconKind.Project,
            _ => SyntaxHighlighter.CanHighlight(name) ? FileIconKind.Code : FileIconKind.File,
        };
    }

    /// <summary>
    /// An image element showing <paramref name="kind"/>. A <paramref name="tint"/> overrides
    /// every part's colour (a marked row goes red); otherwise each part uses its palette colour.
    /// </summary>
    public static Element Draw(FileIconKind kind, FileIconPalette palette, Color? tint = null, float size = 16f)
    {
        var img = Image.New(Rasterize(kind, palette, tint));
        img.Fit = ImageFit.Contain;
        return img.Width(size).Height(size).FlexShrink(0);
    }

    /// <summary>Drops cached bitmaps. Call after a palette change (e.g. a new accent ramp).</summary>
    public static void InvalidateCache()
    {
        lock (Gate)
        {
            foreach (var v in Cache.Values) v.Dispose();
            Cache.Clear();
        }
    }

    private static readonly object Gate = new();
    private static readonly Dictionary<(FileIconKind, FileIconPalette, uint), SkiaImageSource> Cache = new();
    private const int Supersample = 32;   // rendered at 2x for a 16px display

    private static SkiaImageSource Rasterize(FileIconKind kind, FileIconPalette palette, Color? tint)
    {
        var key = (kind, palette, tint is { } t ? Pack(t) : 0xFFFF_FFFFu);
        lock (Gate)
        {
            if (Cache.TryGetValue(key, out var cached)) return cached;

            var src = new SkiaImageSource(RenderBitmap(kind, palette, tint));
            Cache[key] = src;
            return src;
        }
    }

    /// <summary>Draws one glyph into a fresh supersampled bitmap.</summary>
    internal static SKBitmap RenderBitmap(FileIconKind kind, FileIconPalette palette, Color? tint = null)
    {
        var bmp = new SKBitmap(Supersample, Supersample, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var canvas = new SKCanvas(bmp);
        canvas.Clear(SKColors.Transparent);
        canvas.Scale(Supersample / 16f);   // author in a 16-unit box
        foreach (var (path, fill, stroke, color) in Parts(kind, palette))
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
        return bmp;
    }

    private static uint Pack(Color c) => ((uint)c.A << 24) | ((uint)c.R << 16) | ((uint)c.G << 8) | c.B;

    private static SKPath Svg(string d) => SKPath.ParseSvgPathData(d);

    /// <summary>
    /// Parses path data authored in a <paramref name="from"/>-unit box and places it as a
    /// <paramref name="box"/>-unit glyph at (<paramref name="x"/>, <paramref name="y"/>).
    ///
    /// <para><paramref name="fillType"/> has to be passed explicitly for imported artwork.
    /// SVG carries the fill rule as a presentation attribute — often, as in the Visual
    /// Studio icon, buried in a CSS class rather than on the element — and Skia's path
    /// parser reads only the <c>d</c> string, defaulting to nonzero. A shape whose holes
    /// are wound the same way as its outline then fills solid instead of being cut out.</para>
    /// </summary>
    private static SKPath Fitted(string d, float from, float box, float x, float y,
        SKPathFillType fillType = SKPathFillType.Winding)
    {
        var p = SKPath.ParseSvgPathData(d);
        p.FillType = fillType;
        p.Transform(SKMatrix.CreateScale(box / from, box / from));
        p.Transform(SKMatrix.CreateTranslation(x, y));
        return p;
    }

    // The Visual Studio mark, taken from the mask path of the official 2019 icon
    // (commons.wikimedia.org/wiki/File:Visual_Studio_Icon_2019.svg — PD-textlogo: simple
    // geometric shapes, below the threshold of originality, so not copyrightable).
    //
    // The source file paints four purple quadrants through this mask and lays a gradient
    // over them. None of that survives a 16px row, and Skia's path parser handles neither
    // masks nor gradients — but the mask path on its own IS the silhouette, notches
    // included, so filling it in the app accent is both exact and in-palette. Authored in
    // the file's 96-unit box; Fitted() scales it into the 16-unit one.
    //
    // The two trailing subpaths are the notches, and they are holes only under even-odd.
    // The source declares that through a CSS class, which path data alone cannot carry —
    // see Fitted(), and IconKindTests, which asserts both notches are actually cut out.
    private const string VisualStudioMark =
        "M68.89,95.6a6,6,0,0,0,3.93-.44L92.6,85.65A6,6,0,0,0,96,80.24V15.76a6,6,0,0,0-3.4-5.41L72.82.84A6,6,0,0,0,68.34.55," +
        "6,6,0,0,0,66,2L34.12,37.26,15.5,22l-1.63-1.4a4,4,0,0,0-3.61-.83,2.55,2.55,0,0,0-.53.18L2.46,23A4,4,0,0,0,0,26.37c0," +
        ".1,0,.2,0,.3V69.33c0,.1,0,.2,0,.3A4,4,0,0,0,2.46,73l7.27,3a2.55,2.55,0,0,0,.53.18,4,4,0,0,0,3.61-.83L15.5,74,34.12," +
        "58.74,66,94A6,6,0,0,0,68.89,95.6ZM72,27.68,47.21,48,72,68.32ZM12,34.27,24.41,48,12,61.73Z";

    private static IEnumerable<(SKPath path, bool fill, float stroke, Color color)> Parts(
        FileIconKind kind, FileIconPalette p)
    {
        const string page = "M4 1.5h5.5L13 5v9a1 1 0 0 1-1 1H4a1 1 0 0 1-1-1V2.5a1 1 0 0 1 1-1z";
        switch (kind)
        {
            case FileIconKind.Folder:
                yield return (Svg("M1.5 4a1.5 1.5 0 0 1 1.5-1.5h3l1.6 1.8H13A1.5 1.5 0 0 1 14.5 6v6A1.5 1.5 0 0 1 13 13.5H3A1.5 1.5 0 0 1 1.5 12V4z"),
                    true, 0, p.Accent600);
                break;

            case FileIconKind.Parent:
                yield return (Svg("M8 13V4m0 0L4.5 7.5M8 4l3.5 3.5"), false, 1.4f, p.Neutral400);
                break;

            case FileIconKind.Code:
                yield return (Svg(page), false, 1.2f, p.Accent500);
                yield return (Svg("M6.4 8.4L5.2 9.6l1.2 1.2m3.2-2.4l1.2 1.2-1.2 1.2"), false, 1.1f, p.Accent400);
                break;

            case FileIconKind.Image:
                var rect = new SKPath();
                rect.AddRoundRect(new SKRect(2.5f, 3, 13.5f, 13), 1.2f, 1.2f);
                yield return (rect, false, 1.2f, p.Neutral500);
                var lens = new SKPath();
                lens.AddCircle(6, 6.4f, 1);
                yield return (lens, true, 0, p.Neutral500);
                yield return (Svg("M3.4 11.6l3-3 2.4 2.4 1.8-1.6 2 2.2"), false, 1.1f, p.Neutral500);
                break;

            case FileIconKind.Archive:
                yield return (Svg(page), false, 1.2f, p.Neutral600);
                yield return (Svg("M7.6 2.5v3.6m1-1.8H7"), false, 1.1f, p.Neutral500);
                break;

            // A solution is a container, so it gets the bare mark the way Folder does;
            // a project is a document, so it gets the page with the mark set into it.
            case FileIconKind.Solution:
                yield return (Fitted(VisualStudioMark, 96f, 14f, 1f, 1f, SKPathFillType.EvenOdd), true, 0, p.Accent400);
                break;

            case FileIconKind.Project:
                yield return (Svg(page), false, 1.2f, p.Accent600);
                // Sized to clear the page's 1.2-wide outline on both sides rather than
                // sit against it.
                yield return (Fitted(VisualStudioMark, 96f, 7.8f, 4.3f, 6.1f, SKPathFillType.EvenOdd), true, 0, p.Accent400);
                break;

            default: // File
                yield return (Svg(page), false, 1.2f, p.Neutral600);
                yield return (Svg("M9.5 1.5V5H13"), false, 1.2f, p.Neutral600);
                break;
        }
    }
}
