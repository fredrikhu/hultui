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

    // Pooled Skia objects to avoid per-draw-call allocations. The canvas drawing
    // context (CanvasElement's callback surface) draws through the same pool — its
    // callback runs inside this context's render pass, and everything here is reset
    // before each use, so sharing is safe and keeps the pool in one place.
    private readonly SKPaint _pooledPaint = new();
    private readonly SKFont _pooledFont = new() { Subpixel = true };
    private readonly SKRoundRect _pooledRoundRect = new();
    private readonly SKPoint[] _pooledRadii = new SKPoint[4];
    private readonly SKPath _pooledPath = new();
    // Canvas text has always drawn SKTypeface.Default without subpixel positioning;
    // a dedicated font keeps those pixels identical rather than inheriting the text
    // renderer's settings.
    private readonly SKFont _pooledCanvasFont = new(SKTypeface.Default, 12f);
    // Reused by every offset→x range query below: a spanned line asks once per
    // span per pass, so a fresh list each time would be per-frame garbage.
    private readonly List<VisualRange> _rangeBuffer = new();
    private SkiaCanvasDrawingContext? _canvasContext;
    private bool _disposed;

    // Frames borrowed from IPixelImageSources during the frame being drawn. The
    // wrapper image references the source's bytes without copying, and Skia reads
    // them lazily — on the GPU backend the upload happens at flush, not at the draw
    // call — so the borrow is held until EndFrame has flushed and only then handed
    // back. A producer can therefore never overwrite, or free, bytes Skia has yet
    // to read.
    private readonly List<HeldFrame> _heldFrames = new();
    private readonly record struct HeldFrame(IPixelImageSource Source, PixelFrame Frame, SKImage Image);

    // The GL surface this context draws for, when it is one; the raster surface leaves
    // it null and IGpuImageSources then fall back to their pixel path. Wrapped
    // textures are kept per source and rebuilt only when the frame's texture, size or
    // orientation changes — a wrapper is a view, so its content follows the texture.
    private ISkiaExternalSurface? _glSurface;
    private readonly Dictionary<IGpuImageSource, (GpuFrame Frame, SKImage Image)> _wrappedTextures = new();

    internal SKPaint PooledPaint => _pooledPaint;
    internal SKPath PooledPath => _pooledPath;
    internal SKFont PooledCanvasFont => _pooledCanvasFont;

    // Cached positioned-blob per ShapedRun instance. Shaping itself is cached by
    // TextPipeline.MeasurementCache — this cache is only to skip the blob-builder
    // copy when the same run is drawn repeatedly (same frame, or across frames
    // when the layout cache returns the same ShapedRun reference).
    private readonly Dictionary<ShapedRun, SKTextBlob?> _runBlobCache = new();
    private const int RunBlobCacheMaxSize = 2048;

    // The device scale of the frame being drawn. The canvas carries it as a transform, so
    // everything below works in layout units and never sees device pixels — except the border
    // snapping, which is entirely about where a band lands on the device grid.
    private float _scale = 1f;

    internal void SetCanvas(SKCanvas canvas) => _canvas = canvas;
    internal void SetGLSurface(ISkiaExternalSurface surface) => _glSurface = surface;
    internal void SetPipeline(TextPipeline pipeline) => _pipeline = pipeline;
    internal SKCanvas Canvas => _canvas ?? throw new InvalidOperationException("No canvas set. Call BeginFrame first.");

    public void BeginFrame(float width, float height, float scaleFactor, Color? clearColor = null)
    {
        _scale = scaleFactor > 0f ? scaleFactor : 1f;
        // Nothing should be held here; a frame that threw between a draw and its
        // EndFrame would have leaked its borrows into this one.
        ReleaseHeldFrames();
        Canvas.ResetMatrix();
        Canvas.Scale(scaleFactor);
        Canvas.Clear(clearColor is { } c ? ToSkColor(c) : SKColors.Transparent);
    }

    public void EndFrame()
    {
        Canvas.Flush();
        ReleaseHeldFrames();
    }

    private void ReleaseHeldFrames()
    {
        if (_heldFrames.Count == 0) return;
        foreach (var held in _heldFrames)
        {
            held.Image.Dispose();
            held.Source.Release(held.Frame);
        }
        _heldFrames.Clear();
    }

    public void DrawRect(Rect rect, Color fill)
    {
        if (fill.A == 0) return;
        _pooledPaint.Reset();
        _pooledPaint.Color = ToSkColor(fill);
        // Snapped: these are carets, selection bands and dividers, and every one of them is a
        // hard-edged shape that reads as blurry when it lands between pixels.
        Canvas.DrawRect(ToSkRect(SnapFill(rect)), _pooledPaint);
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

        // Same grid as a solid background, or a gradient-filled box and a solid one beside it
        // would sit half a pixel apart.
        rect = SnapBox(rect);

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

    /// <summary>A corner radius pulled in by <paramref name="by"/>, so a stroke inset into the
    /// box keeps its arcs concentric with the box's own instead of bulging.</summary>
    private static CornerRadius Deflate(CornerRadius r, float by) => new(
        MathF.Max(0f, r.TopLeft - by),
        MathF.Max(0f, r.TopRight - by),
        MathF.Max(0f, r.BottomRight - by),
        MathF.Max(0f, r.BottomLeft - by));

    // ── Pixel snapping ───────────────────────────────────────────────────────────────────
    //
    // A stroke centred on a box edge straddles it: at 100% a 1px border came out as two
    // half-covered pixels, and at 150% as two at three-quarters. Nothing was ever crisp, and
    // half the width spilled outside the border box. WinUI paints two fully-covered pixels at
    // 150% and nothing either side, which is what these put back.
    //
    // The rule is CSS's, and the order matters: snap the *edges* onto the device grid, then take
    // the width from the snapped edges — never round position and size independently. Two boxes
    // that share a coordinate snap it to the same pixel, so a gap cannot open between things
    // that were flush. Rounding them apart is the classic way to get a one-pixel seam.
    //
    // Only borders are snapped, plus the box a border is drawn on so the two agree. Arcs are
    // left alone: snapping a curve steps it, and WinUI antialiases its corners too.

    /// <summary>A layout coordinate moved onto the nearest device pixel boundary.</summary>
    private float SnapCoord(float dip) => MathF.Round(dip * _scale, MidpointRounding.AwayFromZero) / _scale;

    /// <summary>A border width in whole device pixels. Never rounds a border away entirely —
    /// a hairline asked for at all stays visible, the way CSS keeps a 0.5px border at 1x.</summary>
    private float SnapWidth(float dip) => dip <= 0f
        ? 0f
        : MathF.Max(1f, MathF.Round(dip * _scale, MidpointRounding.AwayFromZero)) / _scale;

    /// <summary>The box with every edge on the grid. Because the edges land first, a width
    /// measured from them comes out the same on all four sides.</summary>
    private Rect SnapBox(Rect box)
    {
        float l = SnapCoord(box.X), t = SnapCoord(box.Y);
        float r = SnapCoord(box.X + box.Width), b = SnapCoord(box.Y + box.Height);
        return new Rect(l, t, MathF.Max(0f, r - l), MathF.Max(0f, b - t));
    }

    private Thickness SnapThickness(Thickness w) =>
        new(SnapWidth(w.Top), SnapWidth(w.Right), SnapWidth(w.Bottom), SnapWidth(w.Left));

    /// <summary>
    /// A filled rect on the grid. Same edge-first rule as <see cref="SnapBox"/>, plus the clamp
    /// that keeps a thin one visible: two edges of a sub-pixel rect can round to the same device
    /// pixel and collapse it to nothing, which for a caret or a divider is a worse bug than the
    /// softness being fixed. A rect that was already empty stays empty — an unselected range
    /// must not turn into a one-pixel smear.
    /// </summary>
    private Rect SnapFill(Rect r)
    {
        float l = SnapCoord(r.X), t = SnapCoord(r.Y);
        float w = r.Width <= 0f ? 0f : MathF.Max(1f / _scale, SnapCoord(r.X + r.Width) - l);
        float h = r.Height <= 0f ? 0f : MathF.Max(1f / _scale, SnapCoord(r.Y + r.Height) - t);
        return new Rect(l, t, w, h);
    }

    public void DrawBorder(Rect box, CornerRadius radius, Color color, Thickness w, BorderStyle style)
    {
        if (color.A == 0) return;

        box = SnapBox(box);
        w = SnapThickness(w);

        bool uniform = w.Top == w.Right && w.Right == w.Bottom && w.Bottom == w.Left;
        var skColor = ToSkColor(color);

        if (!uniform)
        {
            float x = box.X, y = box.Y, bw = box.Width, bh = box.Height;

            if (radius.IsZero)
            {
                // Square box: paint each edge as a rect inside the border box, so a
                // single-sided or asymmetric border renders exactly (no averaging).
                _pooledPaint.Reset();
                _pooledPaint.Color = skColor;
                _pooledPaint.IsAntialias = true;
                if (w.Top > 0) Canvas.DrawRect(SKRect.Create(x, y, bw, w.Top), _pooledPaint);
                if (w.Bottom > 0) Canvas.DrawRect(SKRect.Create(x, y + bh - w.Bottom, bw, w.Bottom), _pooledPaint);
                if (w.Left > 0) Canvas.DrawRect(SKRect.Create(x, y, w.Left, bh), _pooledPaint);
                if (w.Right > 0) Canvas.DrawRect(SKRect.Create(x + bw - w.Right, y, w.Right, bh), _pooledPaint);
                return;
            }

            // Rounded box: the rects above would cut straight across the corner arcs and leave
            // square ends sticking out of a rounded control. Stroke the real outline instead,
            // clipped to one region at a time.
            //
            // Two kinds of region, and both are needed. An edge's band covers its straight run.
            // A corner's arc lies inside neither adjacent band — it bulges away from both — so
            // each side also draws its half of the corners it touches, split on the corner box's
            // diagonal the way CSS splits them. Without the corner halves a border between two
            // present sides simply stops at the curve and starts again after it, which is what
            // the sample's content frame looked like: a rounded panel with a border that gave up
            // at the corner.
            float tl = radius.TopLeft, tr = radius.TopRight;
            float br = radius.BottomRight, bl = radius.BottomLeft;

            if (w.Top > 0)
            {
                StrokeClipped(SKRect.Create(x, y, bw, w.Top), w.Top);
                StrokeCorner(x, y, x + tl, y, x + tl, y + tl, w.Top);
                StrokeCorner(x + bw, y, x + bw - tr, y, x + bw - tr, y + tr, w.Top);
            }
            if (w.Bottom > 0)
            {
                StrokeClipped(SKRect.Create(x, y + bh - w.Bottom, bw, w.Bottom), w.Bottom);
                StrokeCorner(x, y + bh, x + bl, y + bh, x + bl, y + bh - bl, w.Bottom);
                StrokeCorner(x + bw, y + bh, x + bw - br, y + bh, x + bw - br, y + bh - br, w.Bottom);
            }
            if (w.Left > 0)
            {
                StrokeClipped(SKRect.Create(x, y, w.Left, bh), w.Left);
                StrokeCorner(x, y, x, y + tl, x + tl, y + tl, w.Left);
                StrokeCorner(x, y + bh, x, y + bh - bl, x + bl, y + bh - bl, w.Left);
            }
            if (w.Right > 0)
            {
                StrokeClipped(SKRect.Create(x + bw - w.Right, y, w.Right, bh), w.Right);
                StrokeCorner(x + bw, y, x + bw, y + tr, x + bw - tr, y + tr, w.Right);
                StrokeCorner(x + bw, y + bh, x + bw, y + bh - br, x + bw - br, y + bh - br, w.Right);
            }
            return;

            // The outline, inset by half the stroke so it lands inside the border box — matching
            // where the square-corner path puts its bars, so switching a control to a rounded
            // corner does not also move its border half a pixel outwards.
            void Stroke(float width)
            {
                float half = width / 2f;
                var path = ToSkRect(new Rect(
                    box.X + half, box.Y + half, box.Width - width, box.Height - width));

                _pooledPaint.Reset();
                _pooledPaint.Color = skColor;
                _pooledPaint.IsAntialias = true;
                _pooledPaint.IsStroke = true;
                _pooledPaint.StrokeWidth = width;
                Canvas.DrawRoundRect(ToSkRoundRect(path, Deflate(radius, half)), _pooledPaint);
            }

            void StrokeClipped(SKRect clip, float width)
            {
                Canvas.Save();
                Canvas.ClipRect(clip);
                Stroke(width);
                Canvas.Restore();
            }

            // One side's half of one corner: the triangle from the box's outer corner, along
            // this side's edge to where the arc begins, and back along the diagonal.
            void StrokeCorner(float ax, float ay, float bx, float by, float cx, float cy, float width)
            {
                if (Math.Abs(bx - ax) < 0.01f && Math.Abs(by - ay) < 0.01f) return;   // no radius here

                using var tri = new SKPath();
                tri.MoveTo(ax, ay);
                tri.LineTo(bx, by);
                tri.LineTo(cx, cy);
                tri.Close();

                Canvas.Save();
                Canvas.ClipPath(tri, antialias: true);
                Stroke(width);
                Canvas.Restore();
            }
        }

        // Uniform width: stroke the outline inset by half the width, so the band lands inside
        // the border box on whole device pixels rather than straddling the edge.
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

        float half = sw / 2f;
        var skRect = ToSkRect(new Rect(box.X + half, box.Y + half, box.Width - sw, box.Height - sw));
        if (radius.IsZero)
            Canvas.DrawRect(skRect, _pooledPaint);
        else
            Canvas.DrawRoundRect(ToSkRoundRect(skRect, Deflate(radius, half)), _pooledPaint);

        _pooledPaint.PathEffect = null;
    }

    public void DrawRoundedRect(Rect rect, CornerRadius radius, Color fill, Color stroke, Thickness strokeWidth)
    {
        bool stroked = stroke.A > 0 && (strokeWidth.Top > 0 || strokeWidth.Right > 0
                                        || strokeWidth.Bottom > 0 || strokeWidth.Left > 0);
        // The box snaps whether or not it carries a border, so a background lands on the grid
        // like everything else — and, because two boxes sharing a coordinate snap it to the same
        // pixel, adjacent fills stay flush instead of showing a seam between them.
        rect = SnapBox(rect);
        if (stroked)
            strokeWidth = SnapThickness(strokeWidth);

        var skRect = ToSkRect(rect);

        if (fill.A > 0)
        {
            _pooledPaint.Reset();
            _pooledPaint.Color = ToSkColor(fill);
            _pooledPaint.IsAntialias = true;

            // Pull the fill back under the border rather than out to the same edge. Two
            // antialiased curves on one boundary do not compose into a clean line — they blend,
            // and on a rounded box the fill showed *through* the border at the corners while the
            // straight runs stayed clean. Half the border width is enough: the fill's soft edge
            // then falls in the middle of the band and the border covers it outright. Half rather
            // than the whole width so the fill still runs under most of the border, which is what
            // a translucent border composites against.
            var fillRect = skRect;
            var fillRadius = radius;
            if (stroked)
            {
                float inset = (strokeWidth.Top + strokeWidth.Right + strokeWidth.Bottom + strokeWidth.Left) / 8f;
                fillRect = ToSkRect(new Rect(rect.X + inset, rect.Y + inset,
                    MathF.Max(0f, rect.Width - inset * 2f), MathF.Max(0f, rect.Height - inset * 2f)));
                fillRadius = Deflate(radius, inset);
            }

            if (fillRadius.IsZero)
                Canvas.DrawRect(fillRect, _pooledPaint);
            else
                Canvas.DrawRoundRect(ToSkRoundRect(fillRect, fillRadius), _pooledPaint);
        }

        if (stroked)
        {
            // Use average stroke width for now (uniform border)
            float avgStroke = (strokeWidth.Top + strokeWidth.Right + strokeWidth.Bottom + strokeWidth.Left) / 4f;
            _pooledPaint.Reset();
            _pooledPaint.Color = ToSkColor(stroke);
            _pooledPaint.IsStroke = true;
            _pooledPaint.StrokeWidth = avgStroke;
            _pooledPaint.IsAntialias = true;

            // Inset by half the width: a stroke is centred on the path it follows, so stroking
            // the box's own outline would put half the band outside the border box.
            float half = avgStroke / 2f;
            var band = ToSkRect(new Rect(rect.X + half, rect.Y + half,
                MathF.Max(0f, rect.Width - avgStroke), MathF.Max(0f, rect.Height - avgStroke)));
            if (radius.IsZero)
                Canvas.DrawRect(band, _pooledPaint);
            else
                Canvas.DrawRoundRect(ToSkRoundRect(band, Deflate(radius, half)), _pooledPaint);
        }
    }

    public void DrawText(string text, Rect bounds, ResolvedInheritedStyle style, TextOverflow overflow, int? maxLines)
    {
        if (string.IsNullOrEmpty(text) || _pipeline is null) return;

        var layout = _pipeline.Layout(text, style, bounds.Width, maxLines, overflow);
        _pooledPaint.Reset();
        _pooledPaint.Color = ToSkColor(style.TextColor);
        _pooledPaint.IsAntialias = true;

        // Start/End follow the direction the text actually resolved to, which for
        // TextDirection.Auto is only known now that it has been laid out.
        float alignFactor = TextAlignment.Factor(style.TextAlign, layout.IsRtlParagraph);

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

        // Span-aware layout: metric-affecting spans (bold, italic, size) shape and
        // wrap here; paint-only span lists take the identical plain-layout path.
        var layout = _pipeline.Layout(text, style, bounds.Width, maxLines, overflow, spans);
        _pooledPaint.Reset();
        _pooledPaint.IsAntialias = true;

        // Start/End follow the direction the text actually resolved to, which for
        // TextDirection.Auto is only known now that it has been laid out.
        float alignFactor = TextAlignment.Factor(style.TextAlign, layout.IsRtlParagraph);

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

        var measurer = PrefixMeasurer(line, style);

        // Background pass first so every glyph (including ones whose ink crosses
        // a span boundary) draws on top of the finished highlight rects.
        foreach (var span in spans)
        {
            if (span.Background.A == 0) continue;
            int bs = Math.Clamp(span.Start - line.Start, 0, drawnLength);
            int be = Math.Clamp(span.End - line.Start, 0, drawnLength);
            if (be <= bs) continue;
            _pooledPaint.Color = ToSkColor(span.Background);
            // One rect per contiguous visual piece: a span crossing a direction
            // boundary covers two stretches of the line, not the span between them.
            line.VisualRanges(bs, be, _rangeBuffer, measurer);
            foreach (var r in _rangeBuffer)
                if (r.X1 > r.X0)
                    Canvas.DrawRect(SKRect.Create(baseX + r.X0, lineTop, r.X1 - r.X0, line.Height), _pooledPaint);
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

        // Decorations last, over the glyphs — the same order the element-level
        // decoration draws in, so a spanned underline crosses descenders exactly
        // the way a whole-element one does.
        foreach (var span in spans)
        {
            if (span.Decoration == TextDecoration.None) continue;
            int s = Math.Clamp(span.Start - line.Start, 0, drawnLength);
            int e = Math.Clamp(span.End - line.Start, 0, drawnLength);
            if (e <= s) continue;
            var fg = span.Color.A == 0 ? style.TextColor : span.Color;
            _pooledPaint.Color = ToSkColor(fg);
            line.VisualRanges(s, e, _rangeBuffer, measurer);
            foreach (var r in _rangeBuffer)
                if (r.X1 > r.X0)
                    DrawDecorationRects(span.Decoration, style.FontSize, line.Ascent,
                        baseX + r.X0, r.X1 - r.X0, baselineY);
        }
    }

    private void DrawLineSegment(TextLine line, int startChar, int endChar, Color color,
        ResolvedInheritedStyle style, float baseX, float baselineY, float lineTop)
    {
        if (color.A == 0 || endChar <= startChar) return;

        _pooledPaint.Color = ToSkColor(color);
        line.VisualRanges(startChar, endChar, _rangeBuffer, PrefixMeasurer(line, style));
        foreach (var r in _rangeBuffer)
        {
            if (r.X1 <= r.X0) continue;
            int save = Canvas.Save();
            // Hard-edged (non-AA) clip so adjacent segments tile without seams; a glyph
            // crossing the boundary is drawn by both segments, each contributing its half.
            Canvas.ClipRect(SKRect.Create(baseX + r.X0, lineTop, r.X1 - r.X0, line.Height));
            foreach (var run in line.Runs)
            {
                if (run.OffsetX >= r.X1 || run.OffsetX + run.Width <= r.X0) continue;
                var blob = BuildShapedBlob(run);
                if (blob is not null)
                    Canvas.DrawText(blob, baseX + run.OffsetX, baselineY, _pooledPaint);
            }
            Canvas.RestoreToCount(save);
        }
    }

    // Draws underline and/or strike-through for a line. The paint is already set
    // to the text color; decorations reuse it as a fill for the rule rectangles.
    private void DrawTextDecoration(ResolvedInheritedStyle style, float x, float baselineY, TextLine line)
        => DrawDecorationRects(style.Decoration, style.FontSize, line.Ascent, x, line.Width, baselineY);

    // One geometry for element-level and span-level decorations, in whatever color
    // the pooled paint currently holds.
    private void DrawDecorationRects(TextDecoration decoration, float fontSize, float ascent,
        float x, float width, float baselineY)
    {
        float thickness = Math.Max(1f, fontSize * 0.07f);

        if ((decoration & TextDecoration.Underline) != 0)
        {
            float y = baselineY + Math.Max(1f, fontSize * 0.12f);
            Canvas.DrawRect(SKRect.Create(x, y, width, thickness), _pooledPaint);
        }

        if ((decoration & TextDecoration.LineThrough) != 0)
        {
            // Roughly the x-height midpoint above the baseline.
            float y = baselineY - ascent * 0.33f - thickness / 2f;
            Canvas.DrawRect(SKRect.Create(x, y, width, thickness), _pooledPaint);
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
        // On a GL surface a GPU source is always drawn as one — no frame yet means
        // nothing this frame, not its pixel path. Elsewhere it is whatever else it is.
        //
        // A Vulkan surface is an external surface only when its driver will share an image
        // with a GL context, so it is asked rather than assumed: if it will not, the source
        // falls through to its pixel path below, which is what every non-GL surface does and
        // what a video did on this renderer before phase 4.
        if (source is IGpuImageSource gpuSource && _glSurface is { } gl && gl.SupportsExternalGL)
        {
            DrawGpuFrame(gpuSource, gl, destRect, fit);
            return;
        }

        if (source is IPixelImageSource pixelSource)
        {
            DrawPixelFrame(pixelSource, destRect, fit);
            return;
        }

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

    /// <summary>
    /// One rectangle of the image, in its own pixels, stretched over the destination —
    /// how a frame is taken out of a sprite sheet. A GPU or pixel-stream source has no
    /// sheet to cut from, so it draws whole.
    /// </summary>
    public void DrawImage(IImageSource source, Rect destRect, Rect sourceRect)
    {
        if (source is not SkiaImageSource skiaSource || skiaSource.Image is not { } image)
        {
            DrawImage(source, destRect, ImageFit.Fill);
            return;
        }

        _pooledPaint.Reset();
        _pooledPaint.IsAntialias = true;
        Canvas.DrawImage(image, ToSkRect(sourceRect), ToSkRect(destRect), DownscaleSampling, _pooledPaint);
    }

    /// <summary>
    /// Bilinear only, no mipmaps: a raster image's mip chain is rebuilt every time
    /// the image is new, and a frame source's image is new every frame — several
    /// milliseconds per 1080p frame for nothing. Downscaling beyond 2× will alias;
    /// a video that small on screen is not where quality is being judged.
    /// </summary>
    private static readonly SKSamplingOptions FrameSampling =
        new(SKFilterMode.Linear, SKMipmapMode.None);

    /// <summary>
    /// Forces alpha to one. An image declared opaque is still blitted with whatever
    /// its fourth byte holds — Skia's opaque fast path copies rather than blends —
    /// and a decoder's rgb0 leaves that byte as zero or garbage, which then reads as
    /// transparent wherever the destination alpha matters (a headless capture, a
    /// layer). Identity on colour, 1 on alpha.
    /// </summary>
    private static readonly SKColorFilter ForceOpaque = SKColorFilter.CreateColorMatrix(
    [
        1, 0, 0, 0, 0,
        0, 1, 0, 0, 0,
        0, 0, 1, 0, 0,
        0, 0, 0, 0, 1,
    ]);

    private void DrawGpuFrame(IGpuImageSource source, ISkiaExternalSurface gl, Rect destRect, ImageFit fit)
    {
        if (!source.TryRenderFrame(gl, out var frame))
            return;

        // A renderer-owned frame is re-wrapped every time, never cached. The wrapper holds
        // the image's layout, and Skia moves that layout as it draws — the same lesson the
        // swapchain render target taught, where caching one drew three good frames and then
        // black for ever. A source-owned GL texture has no layout and caches fine.
        bool haveWrapper = _wrappedTextures.TryGetValue(source, out var wrapped);
        if (frame.RendererOwned || !haveWrapper || wrapped.Frame != frame)
        {
            wrapped.Image?.Dispose();
            var image = gl.WrapFrame(frame);
            if (image is null)
            {
                _wrappedTextures.Remove(source);
                return;
            }
            wrapped = (frame, image);
            _wrappedTextures[source] = wrapped;
        }

        var (drawRect, srcRect) = ComputeImageFit(fit, frame.Width, frame.Height,
            destRect.Width, destRect.Height, destRect);

        _pooledPaint.Reset();
        _pooledPaint.IsAntialias = true;
        Canvas.DrawImage(wrapped.Image, srcRect, ToSkRect(drawRect), FrameSampling, _pooledPaint);
    }

    /// <summary>
    /// Drops the wrapper kept for a source, freeing its reference to the texture. A
    /// source that is about to delete its texture calls this first; one that simply
    /// stops being drawn may leave it, since a wrapper is a few bytes and a reference.
    /// </summary>
    public void ForgetGpuSource(IGpuImageSource source)
    {
        if (_wrappedTextures.Remove(source, out var wrapped))
            wrapped.Image.Dispose();
    }

    private void DrawPixelFrame(IPixelImageSource source, Rect destRect, ImageFit fit)
    {
        if (!source.TryAcquire(out var frame))
            return;

        var (colorType, alphaType) = frame.Format switch
        {
            PixelFormat.Rgba8888 => (SKColorType.Rgba8888, SKAlphaType.Premul),
            PixelFormat.Bgra8888 => (SKColorType.Bgra8888, SKAlphaType.Premul),
            PixelFormat.Rgbx8888 => (SKColorType.Rgba8888, SKAlphaType.Opaque),
            PixelFormat.Bgrx8888 => (SKColorType.Bgra8888, SKAlphaType.Opaque),
            _ => throw new ArgumentOutOfRangeException(nameof(frame.Format), frame.Format, null),
        };

        var info = new SKImageInfo(frame.Width, frame.Height, colorType, alphaType);
        // No copy: the image is a view over the source's bytes, so the borrow
        // outlives this call — see _heldFrames.
        var image = SKImage.FromPixels(info, frame.Pixels, frame.Stride);
        if (image is null)
        {
            source.Release(frame);
            return;
        }
        _heldFrames.Add(new HeldFrame(source, frame, image));

        var (drawRect, srcRect) = ComputeImageFit(fit, frame.Width, frame.Height,
            destRect.Width, destRect.Height, destRect);

        _pooledPaint.Reset();
        _pooledPaint.IsAntialias = true;
        if (alphaType == SKAlphaType.Opaque) _pooledPaint.ColorFilter = ForceOpaque;
        Canvas.DrawImage(image, srcRect, ToSkRect(drawRect), FrameSampling, _pooledPaint);
        _pooledPaint.ColorFilter = null;
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

    // ── Offset ↔ x mapping, direction-aware (docs/plans/14-bidi.md, stage 2) ──
    // All three go through the same laid-out line the text is drawn from, so a
    // caret, a click and a highlight agree with the glyphs on screen even when the
    // line's visual order is not its logical order.

    public float TextCaretX(string lineText, ResolvedInheritedStyle style, int offset)
    {
        var line = SingleLine(lineText, style);
        return line is null ? 0f : line.CaretXAt(offset, PrefixMeasurer(line, style));
    }

    public int TextOffsetAtX(string lineText, ResolvedInheritedStyle style, float x)
    {
        var line = SingleLine(lineText, style);
        return line is null ? 0 : line.OffsetAtX(x, PrefixMeasurer(line, style));
    }

    public bool TryMoveCaretVisually(
        string lineText, ResolvedInheritedStyle style, int offset, bool right, out int result)
    {
        result = offset;
        var line = SingleLine(lineText, style);
        return line is not null
            && line.TryMoveCaretVisually(offset, right, out result, PrefixMeasurer(line, style));
    }

    public IReadOnlyList<TextRangeBox> TextSelectionRanges(
        string lineText, ResolvedInheritedStyle style, int from, int to)
    {
        var line = SingleLine(lineText, style);
        if (line is null) return [];
        line.VisualRanges(from, to, _rangeBuffer, PrefixMeasurer(line, style));
        if (_rangeBuffer.Count == 0) return [];
        var boxes = new TextRangeBox[_rangeBuffer.Count];
        for (int i = 0; i < _rangeBuffer.Count; i++)
            boxes[i] = new TextRangeBox(_rangeBuffer[i].X0, _rangeBuffer[i].X1);
        return boxes;
    }

    // The single laid-out line behind one string. Unbounded width and one line, so
    // this is the cached single-line layout DrawText and MeasureText already built.
    private TextLine? SingleLine(string lineText, ResolvedInheritedStyle style)
    {
        if (string.IsNullOrEmpty(lineText) || _pipeline is null) return null;
        var layout = _pipeline.Layout(lineText, style, float.PositiveInfinity, maxLines: 1);
        return layout.Lines.Count > 0 ? layout.Lines[0] : null;
    }

    // Only lines whose shaping merged clusters (ligatures, marks, emoji) need a
    // prefix measurer; for the 1:1 case the glyph array answers exactly and this
    // stays null, so the common path allocates no closure.
    private Func<string, float>? PrefixMeasurer(TextLine line, ResolvedInheritedStyle style)
    {
        foreach (var run in line.Runs)
            if (run.Glyphs.Length != run.Text.Length)
                return s => MeasureText(s, style).Width;
        return null;
    }

    public void DrawLine(Point from, Point to, Color color, float strokeWidth)
    {
        if (color.A == 0) return;
        _pooledPaint.Reset();
        _pooledPaint.Color = ToSkColor(color);
        _pooledPaint.IsStroke = true;
        _pooledPaint.IsAntialias = true;

        // Only axis-aligned lines snap. Every diagonal here is a glyph drawn by hand — the
        // checkmark in a checkbox, the two legs of a chevron — and forcing their endpoints onto
        // the grid kinks the shape rather than sharpening it. A horizontal or vertical rule has
        // no such shape to lose, so it gets the same treatment as a border: the band centred on
        // a snapped half-pixel so a whole number of device pixels is covered.
        bool horizontal = MathF.Abs(from.Y - to.Y) < 0.001f;
        bool vertical = MathF.Abs(from.X - to.X) < 0.001f;
        if (horizontal || vertical)
        {
            float w = SnapWidth(strokeWidth);
            _pooledPaint.StrokeWidth = w;
            float half = w / 2f;
            if (horizontal)
            {
                float y = SnapCoord(from.Y - half) + half;
                Canvas.DrawLine(SnapCoord(from.X), y, SnapCoord(to.X), y, _pooledPaint);
            }
            else
            {
                float x = SnapCoord(from.X - half) + half;
                Canvas.DrawLine(x, SnapCoord(from.Y), x, SnapCoord(to.Y), _pooledPaint);
            }
            return;
        }

        _pooledPaint.StrokeWidth = strokeWidth;
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
        Canvas.Concat(in sk);
    }

    public void Pop()
    {
        if (_saveStack.Count > 0)
            Canvas.RestoreToCount(_saveStack.Pop());
    }

    public ICanvasDrawingContext CreateCanvasContext(Rect bounds)
    {
        // One reusable instance: the context is only valid inside the canvas callback
        // it is handed to, and callbacks cannot nest (a canvas has no children), so
        // re-aiming the same object replaces a per-canvas-per-frame allocation.
        _canvasContext ??= new SkiaCanvasDrawingContext(this);
        _canvasContext.Bounds = bounds;
        return _canvasContext;
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
    internal SKRoundRect ToSkRoundRect(SKRect rect, CornerRadius r)
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
        ReleaseHeldFrames();
        foreach (var wrapped in _wrappedTextures.Values) wrapped.Image.Dispose();
        _wrappedTextures.Clear();
        ClearRunBlobCache();
        _pooledPaint.Dispose();
        _pooledFont.Dispose();
        _pooledRoundRect.Dispose();
        _pooledPath.Dispose();
        _pooledCanvasFont.Dispose();
    }
}
