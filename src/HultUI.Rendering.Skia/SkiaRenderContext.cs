using HultUI.Core.Layout;
using HultUI.Core.Primitives;
using HultUI.Core.Rendering;
using HultUI.Core.Styling;
using HultUI.Text;
using SkiaSharp;

namespace HultUI.Rendering.Skia;

public sealed class SkiaRenderContext : IRenderContext
{
    private SKCanvas? _canvas;
    private readonly Stack<int> _saveStack = new();
    private TextPipeline? _pipeline;

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
        using var paint = new SKPaint { Color = ToSkColor(fill) };
        Canvas.DrawRect(ToSkRect(rect), paint);
    }

    public void DrawRoundedRect(Rect rect, CornerRadius radius, Color fill, Color stroke, Thickness strokeWidth)
    {
        var skRect = ToSkRect(rect);

        if (fill.A > 0)
        {
            using var fillPaint = new SKPaint { Color = ToSkColor(fill), IsAntialias = true };
            if (radius.IsZero)
                Canvas.DrawRect(skRect, fillPaint);
            else
                Canvas.DrawRoundRect(ToSkRoundRect(skRect, radius), fillPaint);
        }

        if (stroke.A > 0 && (strokeWidth.Top > 0 || strokeWidth.Right > 0 || strokeWidth.Bottom > 0 || strokeWidth.Left > 0))
        {
            // Use average stroke width for now (uniform border)
            float avgStroke = (strokeWidth.Top + strokeWidth.Right + strokeWidth.Bottom + strokeWidth.Left) / 4f;
            using var strokePaint = new SKPaint
            {
                Color = ToSkColor(stroke),
                IsStroke = true,
                StrokeWidth = avgStroke,
                IsAntialias = true
            };

            if (radius.IsZero)
                Canvas.DrawRect(skRect, strokePaint);
            else
                Canvas.DrawRoundRect(ToSkRoundRect(skRect, radius), strokePaint);
        }
    }

    public void DrawText(string text, Rect bounds, ResolvedInheritedStyle style, TextOverflow overflow, int? maxLines)
    {
        if (string.IsNullOrEmpty(text) || _pipeline is null) return;

        var layout = _pipeline.Layout(text, style, bounds.Width, maxLines);
        using var paint = new SKPaint { Color = ToSkColor(style.TextColor), IsAntialias = true };

        foreach (var line in layout.Lines)
        {
            float lineY = bounds.Y + line.OffsetY + line.Ascent;

            foreach (var run in line.Runs)
            {
                var typeface = (SKTypeface)run.Font.NativeHandle;
                using var font = new SKFont(typeface, run.FontSize);
                Canvas.DrawText(run.Text, bounds.X + run.OffsetX, lineY, font, paint);
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

        using var paint = new SKPaint { IsAntialias = true };
        Canvas.DrawBitmap(bitmap, srcRect, ToSkRect(drawRect), paint);
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

    public void DrawLine(Point from, Point to, Color color, float strokeWidth)
    {
        if (color.A == 0) return;
        using var paint = new SKPaint
        {
            Color = ToSkColor(color),
            IsStroke = true,
            StrokeWidth = strokeWidth,
            IsAntialias = true
        };
        Canvas.DrawLine(from.X, from.Y, to.X, to.Y, paint);
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
        using var paint = new SKPaint { Color = new SKColor(255, 255, 255, (byte)(opacity * 255)) };
        _saveStack.Push(Canvas.SaveLayer(paint));
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
}
