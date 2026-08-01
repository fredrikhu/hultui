using HultUI.Core.Builders;
using HultUI.Core.Elements;
using HultUI.Core.Primitives;
using HultUI.Core.Rendering;

namespace VesperCommander;

public enum IconKind { Folder, Parent, File, Code, Image, Archive }

/// <summary>
/// Canvas-drawn 16px outline icons for the file list (folder / parent / file /
/// code / image / archive). HultUI's canvas exposes strokes and rounded rects but
/// no arbitrary path fill, so these are built from lines, rounded-rect strokes,
/// and circles — the design's inline-SVG glyphs approximated with primitives.
/// Coordinates are in a local 0..16 box (the element is 16×16).
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

    public static CanvasElement Draw(IconKind kind, Color color) =>
        Canvas.New(ctx => Render(ctx, kind, color)).Width(16).Height(16).FlexShrink(0);

    private static void Render(ICanvasDrawingContext ctx, IconKind kind, Color c)
    {
        const float st = 1.25f;
        switch (kind)
        {
            case IconKind.Folder:
                ctx.DrawRoundedRectStroke(new Rect(2, 5.5f, 12, 8), new CornerRadius(1.5f), c, st);
                ctx.DrawLine(new Point(2, 5.6f), new Point(3.3f, 3.9f), c, st);
                ctx.DrawLine(new Point(3.3f, 3.9f), new Point(6.4f, 3.9f), c, st);
                ctx.DrawLine(new Point(6.4f, 3.9f), new Point(7.7f, 5.6f), c, st);
                break;

            case IconKind.Parent:
                ctx.DrawLine(new Point(8, 3.5f), new Point(8, 12.5f), c, st);
                ctx.DrawLine(new Point(8, 3.5f), new Point(4.8f, 6.7f), c, st);
                ctx.DrawLine(new Point(8, 3.5f), new Point(11.2f, 6.7f), c, st);
                break;

            case IconKind.Code:
                Page(ctx, c, st);
                ctx.DrawLine(new Point(7, 6.8f), new Point(5.4f, 9), c, st);
                ctx.DrawLine(new Point(5.4f, 9), new Point(7, 11.2f), c, st);
                ctx.DrawLine(new Point(9, 6.8f), new Point(10.6f, 9), c, st);
                ctx.DrawLine(new Point(10.6f, 9), new Point(9, 11.2f), c, st);
                break;

            case IconKind.Image:
                Page(ctx, c, st);
                ctx.DrawCircleStroke(new Point(6.4f, 6.6f), 1f, c, st * 0.85f);
                ctx.DrawLine(new Point(4.5f, 11.4f), new Point(7.4f, 8.4f), c, st);
                ctx.DrawLine(new Point(7.4f, 8.4f), new Point(11.5f, 11.4f), c, st);
                break;

            case IconKind.Archive:
                Page(ctx, c, st);
                for (float y = 4.5f; y < 12; y += 2f)
                    ctx.DrawLine(new Point(8, y), new Point(8, y + 1f), c, st);
                break;

            default: // File
                Page(ctx, c, st);
                ctx.DrawLine(new Point(5.6f, 7), new Point(10.4f, 7), c, st * 0.8f);
                ctx.DrawLine(new Point(5.6f, 9.4f), new Point(10.4f, 9.4f), c, st * 0.8f);
                ctx.DrawLine(new Point(5.6f, 11.8f), new Point(8.8f, 11.8f), c, st * 0.8f);
                break;
        }
    }

    private static void Page(ICanvasDrawingContext ctx, Color c, float st) =>
        ctx.DrawRoundedRectStroke(new Rect(3.5f, 2.5f, 9, 11), new CornerRadius(1.3f), c, st);
}
