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
    private readonly SKRoundRect _pooledRoundRect = new();
    private readonly SKPoint[] _pooledRadii = new SKPoint[4];
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

    public void DrawBoxShadow(Rect box, CornerRadius radius, BoxShadow shadow)
    {
        if (shadow.Color.A == 0) return;

        // Grow (or shrink) by spread, then offset. Blur is expressed as a diameter;
        // Skia's mask filter takes a sigma, ~half the blur radius.
        var rect = new Rect(
            box.X - shadow.Spread + shadow.OffsetX,
            box.Y - shadow.Spread + shadow.OffsetY,
            box.Width + shadow.Spread * 2f,
            box.Height + shadow.Spread * 2f);

        using var blur = shadow.Blur > 0f
            ? SKMaskFilter.CreateBlur(SKBlurStyle.Normal, shadow.Blur * 0.5f)
            : null;

        _pooledPaint.Reset();
        _pooledPaint.Color = ToSkColor(shadow.Color);
        _pooledPaint.IsAntialias = true;
        _pooledPaint.MaskFilter = blur;

        var skRect = ToSkRect(rect);
        if (radius.IsZero)
            Canvas.DrawRect(skRect, _pooledPaint);
        else
            Canvas.DrawRoundRect(ToSkRoundRect(skRect, InflateRadius(radius, shadow.Spread)), _pooledPaint);

        // Detach the blur before it is disposed so the pooled paint never holds a
        // freed native filter.
        _pooledPaint.MaskFilter = null;
    }

    private static CornerRadius InflateRadius(CornerRadius r, float d) => new(
        Math.Max(0f, r.TopLeft + d),
        Math.Max(0f, r.TopRight + d),
        Math.Max(0f, r.BottomRight + d),
        Math.Max(0f, r.BottomLeft + d));

    public void DrawGradientRect(Rect rect, CornerRadius radius, Gradient gradient)
    {
        var stops = gradient.Stops;
        if (stops.Count == 0) return;

        var colors = new SKColor[stops.Count];
        var positions = new float[stops.Count];
        for (int i = 0; i < stops.Count; i++)
        {
            colors[i] = ToSkColor(stops[i].Color);
            positions[i] = stops[i].Offset;
        }

        var skRect = ToSkRect(rect);
        float cx = skRect.MidX, cy = skRect.MidY;

        using var shader = gradient.Kind == GradientKind.Linear
            ? CreateLinearShader(gradient.AngleDegrees, skRect, colors, positions)
            : SKShader.CreateRadialGradient(
                new SKPoint(cx, cy),
                MathF.Sqrt(skRect.Width * skRect.Width + skRect.Height * skRect.Height) * 0.5f,
                colors, positions, SKShaderTileMode.Clamp);

        _pooledPaint.Reset();
        _pooledPaint.IsAntialias = true;
        _pooledPaint.Shader = shader;

        if (radius.IsZero)
            Canvas.DrawRect(skRect, _pooledPaint);
        else
            Canvas.DrawRoundRect(ToSkRoundRect(skRect, radius), _pooledPaint);

        // Detach before the shader is disposed so the pooled paint never holds a
        // freed native shader.
        _pooledPaint.Shader = null;
    }

    // Builds a linear-gradient shader whose axis spans the rect edge-to-edge along
    // the given angle (0 = left→right, 90 = top→bottom, clockwise).
    private static SKShader CreateLinearShader(float angleDegrees, SKRect rect, SKColor[] colors, float[] positions)
    {
        float rad = angleDegrees * (MathF.PI / 180f);
        float dx = MathF.Cos(rad), dy = MathF.Sin(rad);
        float cx = rect.MidX, cy = rect.MidY;
        // Projection of the half-extents onto the axis makes the gradient reach the
        // far edges rather than stopping at the centre.
        float half = MathF.Abs(dx) * (rect.Width * 0.5f) + MathF.Abs(dy) * (rect.Height * 0.5f);
        var start = new SKPoint(cx - dx * half, cy - dy * half);
        var end = new SKPoint(cx + dx * half, cy + dy * half);
        return SKShader.CreateLinearGradient(start, end, colors, positions, SKShaderTileMode.Clamp);
    }

    public void DrawBorder(Rect box, CornerRadius radius, Color color, Thickness w, BorderStyle style)
    {
        if (color.A == 0) return;

        bool uniform = w.Top == w.Right && w.Right == w.Bottom && w.Bottom == w.Left;
        var skColor = ToSkColor(color);

        if (!uniform)
        {
            // Per-side widths: paint each edge as a rect inside the border box, so a
            // single-sided or asymmetric border renders exactly (no averaging).
            _pooledPaint.Reset();
            _pooledPaint.Color = skColor;
            _pooledPaint.IsAntialias = true;
            float x = box.X, y = box.Y, bw = box.Width, bh = box.Height;
            if (w.Top > 0) Canvas.DrawRect(SKRect.Create(x, y, bw, w.Top), _pooledPaint);
            if (w.Bottom > 0) Canvas.DrawRect(SKRect.Create(x, y + bh - w.Bottom, bw, w.Bottom), _pooledPaint);
            if (w.Left > 0) Canvas.DrawRect(SKRect.Create(x, y, w.Left, bh), _pooledPaint);
            if (w.Right > 0) Canvas.DrawRect(SKRect.Create(x + bw - w.Right, y, w.Right, bh), _pooledPaint);
            return;
        }

        // Uniform width, dashed or dotted: stroke the (rounded) outline with a dash
        // path effect, matching DrawRoundedRect's centered-stroke geometry.
        float sw = w.Top;
        if (sw <= 0) return;

        _pooledPaint.Reset();
        _pooledPaint.Color = skColor;
        _pooledPaint.IsStroke = true;
        _pooledPaint.StrokeWidth = sw;
        _pooledPaint.IsAntialias = true;
        if (style == BorderStyle.Dotted)
            _pooledPaint.StrokeCap = SKStrokeCap.Round;

        using var dash = style switch
        {
            BorderStyle.Dashed => SKPathEffect.CreateDash([sw * 3f, sw * 2f], 0f),
            BorderStyle.Dotted => SKPathEffect.CreateDash([sw * 0.1f, sw * 2f], 0f),
            _ => null,
        };
        _pooledPaint.PathEffect = dash;

        var skRect = ToSkRect(box);
        if (radius.IsZero)
            Canvas.DrawRect(skRect, _pooledPaint);
        else
            Canvas.DrawRoundRect(ToSkRoundRect(skRect, radius), _pooledPaint);

        _pooledPaint.PathEffect = null;
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

        var layout = _pipeline.Layout(text, style, bounds.Width, maxLines, overflow);
        _pooledPaint.Reset();
        _pooledPaint.Color = ToSkColor(style.TextColor);
        _pooledPaint.IsAntialias = true;

        float alignFactor = style.TextAlign switch
        {
            TextAlign.Center => 0.5f,
            TextAlign.Right => 1f,
            _ => 0f,
        };

        foreach (var line in layout.Lines)
        {
            // Shift the line horizontally for center/right alignment. The layout
            // itself is width-independent (and shared from cache), so alignment is
            // applied here at draw time against the actual box width.
            float alignOffset = alignFactor > 0f && bounds.Width > line.Width
                ? (bounds.Width - line.Width) * alignFactor
                : 0f;
            float baseX = bounds.X + alignOffset;
            float lineY = bounds.Y + line.OffsetY + line.Ascent;

            foreach (var run in line.Runs)
            {
                var blob = BuildShapedBlob(run);
                if (blob is not null)
                    Canvas.DrawText(blob, baseX + run.OffsetX, lineY, _pooledPaint);
            }

            if (style.Decoration != TextDecoration.None)
                DrawTextDecoration(style, baseX, lineY, line);
        }
    }

    public void DrawText(string text, Rect bounds, ResolvedInheritedStyle style, TextOverflow overflow,
        int? maxLines, IReadOnlyList<TextSpan>? spans)
    {
        if (spans is null || spans.Count == 0)
        {
            DrawText(text, bounds, style, overflow, maxLines);
            return;
        }
        if (string.IsNullOrEmpty(text) || _pipeline is null) return;

        var layout = _pipeline.Layout(text, style, bounds.Width, maxLines, overflow);
        _pooledPaint.Reset();
        _pooledPaint.IsAntialias = true;

        float alignFactor = style.TextAlign switch
        {
            TextAlign.Center => 0.5f,
            TextAlign.Right => 1f,
            _ => 0f,
        };

        foreach (var line in layout.Lines)
        {
            float alignOffset = alignFactor > 0f && bounds.Width > line.Width
                ? (bounds.Width - line.Width) * alignFactor
                : 0f;
            float baseX = bounds.X + alignOffset;
            float lineY = bounds.Y + line.OffsetY + line.Ascent;

            DrawSpannedLine(line, spans, style, baseX, lineY, bounds.Y + line.OffsetY);

            if (style.Decoration != TextDecoration.None)
            {
                _pooledPaint.Color = ToSkColor(style.TextColor);
                DrawTextDecoration(style, baseX, lineY, line);
            }
        }
    }

    // Draws one laid-out line as constant-color segments split at span boundaries.
    // Glyphs are never re-shaped or moved: each segment clips to its x-range and
    // draws the same cached blobs, so kerning and ligatures render exactly as the
    // plain path does (a boundary inside a ligature splits its color mid-glyph).
    private void DrawSpannedLine(TextLine line, IReadOnlyList<TextSpan> spans, ResolvedInheritedStyle style,
        float baseX, float baselineY, float lineTop)
    {
        // The drawn text is the concatenation of the line's runs (trailing
        // whitespace is trimmed at shaping; an ellipsized line ends in "…").
        // Drawn char i corresponds to source index line.Start + i.
        int drawnLength = 0;
        foreach (var run in line.Runs)
            drawnLength += run.Text.Length;
        if (drawnLength == 0) return;

        // Background pass first so every glyph (including ones whose ink crosses
        // a span boundary) draws on top of the finished highlight rects.
        foreach (var span in spans)
        {
            if (span.Background.A == 0) continue;
            int bs = Math.Clamp(span.Start - line.Start, 0, drawnLength);
            int be = Math.Clamp(span.End - line.Start, 0, drawnLength);
            if (be <= bs) continue;
            float bx0 = LineXAt(line, bs, style);
            float bx1 = LineXAt(line, be, style);
            if (bx1 <= bx0) continue;
            _pooledPaint.Color = ToSkColor(span.Background);
            Canvas.DrawRect(SKRect.Create(baseX + bx0, lineTop, bx1 - bx0, line.Height), _pooledPaint);
        }

        int cursor = 0;
        foreach (var span in spans)
        {
            int s = Math.Clamp(span.Start - line.Start, 0, drawnLength);
            int e = Math.Clamp(span.End - line.Start, 0, drawnLength);
            if (e <= cursor) continue;
            if (s > cursor)
                DrawLineSegment(line, cursor, s, style.TextColor, style, baseX, baselineY, lineTop);
            // Transparent span color = background-only span; glyphs keep the
            // element's text color.
            var fg = span.Color.A == 0 ? style.TextColor : span.Color;
            DrawLineSegment(line, Math.Max(s, cursor), e, fg, style, baseX, baselineY, lineTop);
            cursor = e;
            if (cursor >= drawnLength) break;
        }
        if (cursor < drawnLength)
            DrawLineSegment(line, cursor, drawnLength, style.TextColor, style, baseX, baselineY, lineTop);
    }

    private void DrawLineSegment(TextLine line, int startChar, int endChar, Color color,
        ResolvedInheritedStyle style, float baseX, float baselineY, float lineTop)
    {
        if (color.A == 0) return;
        float x0 = LineXAt(line, startChar, style);
        float x1 = endChar >= startChar ? LineXAt(line, endChar, style) : x0;
        if (x1 <= x0) return;

        _pooledPaint.Color = ToSkColor(color);
        int save = Canvas.Save();
        // Hard-edged (non-AA) clip so adjacent segments tile without seams; a glyph
        // crossing the boundary is drawn by both segments, each contributing its half.
        Canvas.ClipRect(SKRect.Create(baseX + x0, lineTop, x1 - x0, line.Height));
        foreach (var run in line.Runs)
        {
            if (run.OffsetX >= x1 || run.OffsetX + run.Width <= x0) continue;
            var blob = BuildShapedBlob(run);
            if (blob is not null)
                Canvas.DrawText(blob, baseX + run.OffsetX, baselineY, _pooledPaint);
        }
        Canvas.RestoreToCount(save);
    }

    // X position (line-relative) of the boundary before drawn character charIdx.
    // Glyph origins give the exact position when glyphs map 1:1 to characters (the
    // usual case for code and Latin text); runs where shaping merged clusters
    // (ligatures, marks) fall back to measuring the prefix, which may sit a hair
    // off inside a ligature but never moves any glyph.
    private float LineXAt(TextLine line, int charIdx, ResolvedInheritedStyle style)
    {
        int offset = 0;
        foreach (var run in line.Runs)
        {
            int len = run.Text.Length;
            int rel = charIdx - offset;
            if (rel <= 0) return run.OffsetX;
            if (rel < len)
                return run.Glyphs.Length == len
                    ? run.OffsetX + run.Glyphs[rel].X
                    : run.OffsetX + MeasureText(run.Text[..rel], style).Width;
            offset += len;
        }
        return line.Width;
    }

    // Draws underline and/or strike-through for a line. The paint is already set
    // to the text color; decorations reuse it as a fill for the rule rectangles.
    private void DrawTextDecoration(ResolvedInheritedStyle style, float x, float baselineY, TextLine line)
    {
        float thickness = Math.Max(1f, style.FontSize * 0.07f);

        if ((style.Decoration & TextDecoration.Underline) != 0)
        {
            float y = baselineY + Math.Max(1f, style.FontSize * 0.12f);
            Canvas.DrawRect(SKRect.Create(x, y, line.Width, thickness), _pooledPaint);
        }

        if ((style.Decoration & TextDecoration.LineThrough) != 0)
        {
            // Roughly the x-height midpoint above the baseline.
            float y = baselineY - line.Ascent * 0.33f - thickness / 2f;
            Canvas.DrawRect(SKRect.Create(x, y, line.Width, thickness), _pooledPaint);
        }
    }

    /// <summary>
    /// Linear filtering with mipmaps: images here are usually drawn smaller than their
    /// source (icons rasterized above their display size, thumbnails), which is exactly
    /// the case point sampling handles worst.
    /// </summary>
    private static readonly SKSamplingOptions DownscaleSampling =
        new(SKFilterMode.Linear, SKMipmapMode.Linear);

    public void DrawImage(IImageSource source, Rect destRect, ImageFit fit)
    {
        if (source is not SkiaImageSource skiaSource || skiaSource.Bitmap is not { } bitmap
            || skiaSource.Image is not { } image)
            return;

        var srcW = (float)bitmap.Width;
        var srcH = (float)bitmap.Height;
        var destW = destRect.Width;
        var destH = destRect.Height;

        var (drawRect, srcRect) = ComputeImageFit(fit, srcW, srcH, destW, destH, destRect);

        _pooledPaint.Reset();
        _pooledPaint.IsAntialias = true;
        // IsAntialias smooths the destination rect's edges; it says nothing about how
        // the source is resampled, and Skia's default for that is nearest-neighbour.
        // Every downscaled image was therefore point-sampled — icons came out visibly
        // aliased, and geometry thinner than a destination pixel (Phosphor's `minus`
        // bar is 6% of its box) could land between samples and vanish outright.
        Canvas.DrawImage(image, srcRect, ToSkRect(drawRect), DownscaleSampling, _pooledPaint);
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
        Canvas.ClipRoundRect(ToSkRoundRect(ToSkRect(clipRect), radius));
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

    public void PushTransform(System.Numerics.Matrix3x2 m)
    {
        _saveStack.Push(Canvas.Save());
        // SKMatrix rows: (scaleX, skewX, transX), (skewY, scaleY, transY).
        var sk = new SKMatrix(m.M11, m.M21, m.M31, m.M12, m.M22, m.M32, 0f, 0f, 1f);
        Canvas.Concat(ref sk);
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

    // Returns the shared pooled SKRoundRect configured for the given rect/radii.
    // Valid until the next call — callers must not hold on to (or dispose) it.
    // Draw calls copy the geometry into the native command stream immediately,
    // so sequential reuse within a frame is safe.
    private SKRoundRect ToSkRoundRect(SKRect rect, CornerRadius r)
    {
        _pooledRadii[0] = new SKPoint(r.TopLeft, r.TopLeft);
        _pooledRadii[1] = new SKPoint(r.TopRight, r.TopRight);
        _pooledRadii[2] = new SKPoint(r.BottomRight, r.BottomRight);
        _pooledRadii[3] = new SKPoint(r.BottomLeft, r.BottomLeft);
        _pooledRoundRect.SetRectRadii(rect, _pooledRadii);
        return _pooledRoundRect;
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
        _pooledRoundRect.Dispose();
    }
}
