using HultUI.Core.Layout;
using HultUI.Core.Primitives;
using HultUI.Core.Rendering;
using HultUI.Core.Styling;
using HultUI.Text;
using SkiaSharp;

namespace HultUI.Rendering.Skia;

public sealed class SkiaRenderContext : IRenderContext, IDisposable
{
    private SKCanvas? _canvas;
    private readonly Stack<int> _saveStack = new();
    private TextPipeline? _pipeline;

    // Pooled Skia objects to avoid per-draw-call allocations
    private readonly SKPaint _pooledPaint = new();
    private readonly SKFont _pooledFont = new() { Subpixel = true };
    private bool _disposed;

    // Cached positioned-blob per ShapedRun instance. Shaping itself is cached by
    // TextPipeline.MeasurementCache — this cache is only to skip the blob-builder
    // copy when the same run is drawn repeatedly (same frame, or across frames
    // when the layout cache returns the same ShapedRun reference).
    private readonly Dictionary<ShapedRun, SKTextBlob?> _runBlobCache = new();
    private const int RunBlobCacheMaxSize = 2048;

    internal void SetCanvas(SKCanvas canvas) => _canvas = canvas;
    internal void SetPipeline(TextPipeline pipeline) => _pipeline = pipeline;
    private SKCanvas Canvas => _canvas ?? throw new InvalidOperationException("No canvas set. Call BeginFrame first.");

    public void BeginFrame(float width, float height, float scaleFactor, Color? clearColor = null)
    {
        Canvas.ResetMatrix();
        Canvas.Scale(scaleFactor);
        Canvas.Clear(clearColor is { } c ? ToSkColor(c) : SKColors.Transparent);
    }

    public void EndFrame()
    {
        Canvas.Flush();
    }

    public void DrawRect(Rect rect, Color fill)
    {
        if (fill.A == 0) return;
        _pooledPaint.Reset();
        _pooledPaint.Color = ToSkColor(fill);
        Canvas.DrawRect(ToSkRect(rect), _pooledPaint);
    }

    public void DrawRoundedRect(Rect rect, CornerRadius radius, Color fill, Color stroke, Thickness strokeWidth)
    {
        var skRect = ToSkRect(rect);

        if (fill.A > 0)
        {
            _pooledPaint.Reset();
            _pooledPaint.Color = ToSkColor(fill);
            _pooledPaint.IsAntialias = true;
            if (radius.IsZero)
                Canvas.DrawRect(skRect, _pooledPaint);
            else
                Canvas.DrawRoundRect(ToSkRoundRect(skRect, radius), _pooledPaint);
        }

        if (stroke.A > 0 && (strokeWidth.Top > 0 || strokeWidth.Right > 0 || strokeWidth.Bottom > 0 || strokeWidth.Left > 0))
        {
            // Use average stroke width for now (uniform border)
            float avgStroke = (strokeWidth.Top + strokeWidth.Right + strokeWidth.Bottom + strokeWidth.Left) / 4f;
            _pooledPaint.Reset();
            _pooledPaint.Color = ToSkColor(stroke);
            _pooledPaint.IsStroke = true;
            _pooledPaint.StrokeWidth = avgStroke;
            _pooledPaint.IsAntialias = true;

            if (radius.IsZero)
                Canvas.DrawRect(skRect, _pooledPaint);
            else
                Canvas.DrawRoundRect(ToSkRoundRect(skRect, radius), _pooledPaint);
        }
    }

    public void DrawText(string text, Rect bounds, ResolvedInheritedStyle style, TextOverflow overflow, int? maxLines)
    {
        if (string.IsNullOrEmpty(text) || _pipeline is null) return;

        var layout = _pipeline.Layout(text, style, bounds.Width, maxLines);
        _pooledPaint.Reset();
        _pooledPaint.Color = ToSkColor(style.TextColor);
        _pooledPaint.IsAntialias = true;

        foreach (var line in layout.Lines)
        {
            float lineY = bounds.Y + line.OffsetY + line.Ascent;

            foreach (var run in line.Runs)
            {
                var blob = BuildShapedBlob(run);
                if (blob is not null)
                    Canvas.DrawText(blob, bounds.X + run.OffsetX, lineY, _pooledPaint);
            }
        }
    }

    public void DrawImage(IImageSource source, Rect destRect, ImageFit fit)
    {
        if (source is not SkiaImageSource skiaSource || skiaSource.Bitmap is not { } bitmap)
            return;

        var srcW = (float)bitmap.Width;
        var srcH = (float)bitmap.Height;
        var destW = destRect.Width;
        var destH = destRect.Height;

        var (drawRect, srcRect) = ComputeImageFit(fit, srcW, srcH, destW, destH, destRect);

        _pooledPaint.Reset();
        _pooledPaint.IsAntialias = true;
        Canvas.DrawBitmap(bitmap, srcRect, ToSkRect(drawRect), _pooledPaint);
    }

    private static (Rect drawRect, SKRect srcRect) ComputeImageFit(
        ImageFit fit, float srcW, float srcH, float destW, float destH, Rect destRect)
    {
        var fullSrc = new SKRect(0, 0, srcW, srcH);

        switch (fit)
        {
            case ImageFit.Fill:
                return (destRect, fullSrc);

            case ImageFit.None:
            {
                // Draw at natural size, centered
                float x = destRect.X + (destW - srcW) / 2;
                float y = destRect.Y + (destH - srcH) / 2;
                return (new Rect(x, y, srcW, srcH), fullSrc);
            }

            case ImageFit.Contain:
            {
                float scale = Math.Min(destW / srcW, destH / srcH);
                float w = srcW * scale;
                float h = srcH * scale;
                float x = destRect.X + (destW - w) / 2;
                float y = destRect.Y + (destH - h) / 2;
                return (new Rect(x, y, w, h), fullSrc);
            }

            case ImageFit.Cover:
            {
                float scale = Math.Max(destW / srcW, destH / srcH);
                float visibleW = destW / scale;
                float visibleH = destH / scale;
                float srcX = (srcW - visibleW) / 2;
                float srcY = (srcH - visibleH) / 2;
                return (destRect, new SKRect(srcX, srcY, srcX + visibleW, srcY + visibleH));
            }

            case ImageFit.ScaleDown:
            {
                // Like Contain but only shrink, never enlarge
                float scale = Math.Min(1f, Math.Min(destW / srcW, destH / srcH));
                float w = srcW * scale;
                float h = srcH * scale;
                float x = destRect.X + (destW - w) / 2;
                float y = destRect.Y + (destH - h) / 2;
                return (new Rect(x, y, w, h), fullSrc);
            }

            default:
                return (destRect, fullSrc);
        }
    }

    public Size MeasureText(string text, ResolvedInheritedStyle style)
    {
        if (string.IsNullOrEmpty(text) || _pipeline is null)
            return Size.Zero;

        // Route through the same pipeline DrawText uses so CJK / emoji characters
        // are measured with their fallback fonts, not the primary font's .notdef
        // (which would return 0 width and put the caret in the wrong place).
        var layout = _pipeline.Layout(text, style, float.PositiveInfinity, maxLines: 1);
        return layout.Size;
    }

    public IReadOnlyList<TextLineBox> LayoutTextLines(string text, ResolvedInheritedStyle style, float maxWidth, int? maxLines)
    {
        if (string.IsNullOrEmpty(text) || _pipeline is null)
            return [];

        var layout = _pipeline.Layout(text, style, maxWidth, maxLines);
        var boxes = new TextLineBox[layout.Lines.Count];
        for (int i = 0; i < layout.Lines.Count; i++)
        {
            var line = layout.Lines[i];
            boxes[i] = new TextLineBox(line.Start, line.Length, line.OffsetY, line.Height, line.Width);
        }
        return boxes;
    }

    public void DrawLine(Point from, Point to, Color color, float strokeWidth)
    {
        if (color.A == 0) return;
        _pooledPaint.Reset();
        _pooledPaint.Color = ToSkColor(color);
        _pooledPaint.IsStroke = true;
        _pooledPaint.StrokeWidth = strokeWidth;
        _pooledPaint.IsAntialias = true;
        Canvas.DrawLine(from.X, from.Y, to.X, to.Y, _pooledPaint);
    }

    public void PushClip(Rect clipRect)
    {
        _saveStack.Push(Canvas.Save());
        Canvas.ClipRect(ToSkRect(clipRect));
    }

    public void PushRoundedClip(Rect clipRect, CornerRadius radius)
    {
        _saveStack.Push(Canvas.Save());
        using var rrect = ToSkRoundRect(ToSkRect(clipRect), radius);
        Canvas.ClipRoundRect(rrect);
    }

    public void PushTranslate(float x, float y)
    {
        _saveStack.Push(Canvas.Save());
        Canvas.Translate(x, y);
    }

    public void PushOpacity(float opacity)
    {
        _pooledPaint.Reset();
        _pooledPaint.Color = new SKColor(255, 255, 255, (byte)(opacity * 255));
        _saveStack.Push(Canvas.SaveLayer(_pooledPaint));
    }

    public void Pop()
    {
        if (_saveStack.Count > 0)
            Canvas.RestoreToCount(_saveStack.Pop());
    }

    public ICanvasDrawingContext CreateCanvasContext(Rect bounds)
    {
        return new SkiaCanvasDrawingContext(Canvas, bounds);
    }

    // ── Text blob building (positioned from HarfBuzz-shaped glyphs) ──

    private SKTextBlob? BuildShapedBlob(ShapedRun run)
    {
        if (_runBlobCache.TryGetValue(run, out var cached))
            return cached;

        if (run.Glyphs.Length == 0)
            return null;

        var typeface = (SKTypeface)run.Font.NativeHandle;
        _pooledFont.Typeface = typeface;
        _pooledFont.Size = run.FontSize;

        using var builder = new SKTextBlobBuilder();
        var buffer = builder.AllocatePositionedRun(_pooledFont, run.Glyphs.Length);
        var glyphSpan = buffer.Glyphs;
        var positionSpan = buffer.Positions;

        for (int i = 0; i < run.Glyphs.Length; i++)
        {
            glyphSpan[i] = (ushort)run.Glyphs[i].GlyphId;
            positionSpan[i] = new SKPoint(run.Glyphs[i].X, run.Glyphs[i].Y);
        }

        var blob = builder.Build();

        if (_runBlobCache.Count >= RunBlobCacheMaxSize)
            ClearRunBlobCache();

        _runBlobCache[run] = blob;
        return blob;
    }

    private void ClearRunBlobCache()
    {
        foreach (var blob in _runBlobCache.Values)
            blob?.Dispose();
        _runBlobCache.Clear();
    }

    // ── Conversion helpers ──

    private static SKColor ToSkColor(Color c) => new(c.R, c.G, c.B, c.A);

    private static SKRect ToSkRect(Rect r) => new(r.X, r.Y, r.X + r.Width, r.Y + r.Height);

    private static SKRoundRect ToSkRoundRect(SKRect rect, CornerRadius r)
    {
        var rrect = new SKRoundRect();
        rrect.SetRectRadii(rect,
        [
            new SKPoint(r.TopLeft, r.TopLeft),
            new SKPoint(r.TopRight, r.TopRight),
            new SKPoint(r.BottomRight, r.BottomRight),
            new SKPoint(r.BottomLeft, r.BottomLeft)
        ]);
        return rrect;
    }

    private static SKFontStyleWeight ToSkFontWeight(FontWeight weight) => weight switch
    {
        FontWeight.Thin => SKFontStyleWeight.Thin,
        FontWeight.Light => SKFontStyleWeight.Light,
        FontWeight.Regular => SKFontStyleWeight.Normal,
        FontWeight.Medium => SKFontStyleWeight.Medium,
        FontWeight.SemiBold => SKFontStyleWeight.SemiBold,
        FontWeight.Bold => SKFontStyleWeight.Bold,
        FontWeight.ExtraBold => SKFontStyleWeight.ExtraBold,
        FontWeight.Black => SKFontStyleWeight.Black,
        _ => SKFontStyleWeight.Normal
    };

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        ClearRunBlobCache();
        _pooledPaint.Dispose();
        _pooledFont.Dispose();
    }
}
