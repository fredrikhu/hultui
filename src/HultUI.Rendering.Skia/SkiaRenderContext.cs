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

    // Cached positioned-blob per run of glyphs. Shaping itself is cached by
    // TextPipeline.MeasurementCache — this cache is only to skip the blob-builder
    // copy when the same run is drawn repeatedly (same frame, or across frames).
    // Keyed by what the run draws, not by the run: a paragraph laid out again after
    // an edit makes new runs for every line, the ones that did not change too.
    // The blobs are native handles (NativeTextBlob), each holding the one reference this
    // context gives up when the cache empties or the context is disposed: an SKTextBlob
    // wrapper was 80 B per new blob.
    private readonly Dictionary<ShapedRun, nint> _runBlobCache = new(SameGlyphs.Instance);
    internal const int RunBlobCacheMaxSize = 2048;
    internal int CachedBlobCount => _runBlobCache.Count;

    // Every native blob this context made and gave up, for the tests that hold the two equal.
    internal long BlobsMade { get; private set; }
    internal long BlobsReleased { get; private set; }

    // Every new blob is built in this one builder: making the blob hands it over and leaves the
    // builder empty for the next. A builder per blob was 80 B more per new blob (216 -> 136),
    // and a scrolled list draws dozens of new rows a step.
    private readonly SKTextBlobBuilder _blobBuilder = new();

    public SkiaRenderContext() => NativeTextBlob.EnsureAvailable();

    // Sub-blobs: the glyphs of one run that can ink one colour stretch of a spanned line (see
    // DrawRunPart). Keyed by the run itself, not its glyphs, so a lookup hashes nothing: a run
    // laid out again makes new ones, released with the rest when the cache empties. The map is
    // made at its bound's capacity with the first sub-blob (~74 KB, once per context that draws
    // a spanned line), so filling it allocates nothing more.
    private readonly record struct SubRun(ShapedRun Run, int Start, int End);
    private Dictionary<SubRun, nint>? _subBlobCache;
    internal int CachedSubBlobCount => _subBlobCache?.Count ?? 0;

    /// <summary>Whether a colour stretch draws only the glyphs that can reach it. Off, each
    /// stretch draws its runs whole, as before: the reference the pixel tests hold it to.</summary>
    internal bool DrawsStretchesFromSubBlobs { get; set; } = true;

    /// <summary>Glyphs handed to Skia in text blobs and outlines, for the tests and probes that
    /// count them.</summary>
    internal long GlyphsDrawn { get; private set; }

    // How far each face's glyphs ink left and right of their origin, per unit of size: its
    // bounding box and a slack (InkExtentOf), or, where Skia marks the box as a guess, each run's
    // own glyphs (RunInkExtentOf), whose bounds the face keeps (GlyphInks); neither for a colour or
    // bitmap face.
    private readonly Dictionary<IResolvedFont, FaceInk> _inkExtents = new(ReferenceEqualityComparer.Instance);
    internal readonly record struct InkExtent(float Left, float Right);
    private readonly record struct FaceInk(InkExtent? Box, GlyphInks? Glyphs, float Slack);
    private readonly SKFont _boundsFont = new() { LinearMetrics = true };

    // The glyph bounds a run's extent is taken from are read from Skia in pieces of a fixed size,
    // into kept buffers.
    private const int BoundsPiece = 256;
    private ushort[]? _boundsGlyphs;
    private SKRect[]? _glyphBounds;

    /// <summary>Glyph bounds read from Skia for the faces whose box is a guess, and runs whose
    /// extent was taken from them, for the tests that hold each glyph to one read.</summary>
    internal long GlyphInksRead { get; private set; }
    internal long RunExtentsFromGlyphs { get; private set; }

    /// <summary>
    /// How far each glyph of a face whose box is a guess inks left and right of its origin at
    /// 2048, as Skia gives its bounds: read the first time a run holds the glyph, then kept for
    /// the face. A run's extent is the union of its glyphs', taken where a spanned line draws it.
    /// The runs' extents were kept per run instead, which kept every run drawn alive after the
    /// text caches had let it go (plan 17, round 5: 2.29 MB after a 20,000-line Cascadia document
    /// closed); a face holds at most its own glyphs, in pages of 256 made as they are first needed
    /// (2 KB each: Cascadia's ASCII is one).
    /// </summary>
    private sealed class GlyphInks
    {
        private const int PageBits = 8, PageSize = 1 << PageBits;
        private readonly InkExtent[]?[] _pages = new InkExtent[]?[(ushort.MaxValue + 1) >> PageBits];

        /// <summary>Not read yet; and read by the run asking now (so it asks once).</summary>
        public static readonly InkExtent Unread = new(float.NaN, float.NaN), Asked = new(float.NaN, 0f);

        /// <summary>The page holding <paramref name="glyph"/>, at the glyph's low byte: its left
        /// and right at 2048, or <see cref="Unread"/> or <see cref="Asked"/> (a NaN left). A glyph
        /// with no ink is +∞ to −∞ (it widens nothing), one whose bounds are not numbers −∞ to +∞
        /// (its run's extent then reaches everywhere, as it did when a bound not a number joined
        /// it): the table holds no NaN once a glyph is read.</summary>
        public InkExtent[] PageOf(ushort glyph)
        {
            ref var page = ref _pages[glyph >> PageBits];
            if (page is null)
            {
                page = new InkExtent[PageSize];
                page.AsSpan().Fill(Unread);
            }
            return page;
        }
    }

    // Per spanned line, for each run: whether its glyphs are in X order (0 not asked yet, 1 yes,
    // 2 no, 3 drawn whole: no extent is known) and its ink extent; and the margin in layout units
    // that covers two device pixels under the canvas matrix (NaN: stretches draw whole runs).
    private byte[] _runXOrder = new byte[16];
    private InkExtent[] _runInk = new InkExtent[16];
    private float _stretchMargin;

    // The same runs as glyph outlines, for TextAsOutlines.
    private readonly Dictionary<ShapedRun, SKPath?> _runPathCache = new(SameGlyphs.Instance);

    /// <summary>Two runs draw the same pixels when they hold the same glyphs at the same places,
    /// in the same face and size, whichever layout made them.</summary>
    private sealed class SameGlyphs : IEqualityComparer<ShapedRun>
    {
        public static readonly SameGlyphs Instance = new();

        public bool Equals(ShapedRun? a, ShapedRun? b) =>
            ReferenceEquals(a, b)
            || (a is not null && b is not null && ReferenceEquals(a.Font, b.Font) && a.FontSize == b.FontSize
                && (ReferenceEquals(a.Glyphs, b.Glyphs) || a.Glyphs.AsSpan().SequenceEqual(b.Glyphs)));

        public int GetHashCode(ShapedRun run)
        {
            var hash = new HashCode();
            hash.Add(System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(run.Font));
            hash.Add(run.FontSize);
            foreach (var g in run.Glyphs)
            {
                hash.Add(g.GlyphId);
                hash.Add(g.X);
            }
            return hash.ToHashCode();
        }
    }
    private readonly SKFont _outlineFont = new() { Hinting = SKFontHinting.None, Subpixel = true, LinearMetrics = true };

    /// <summary>
    /// Draws text as its glyphs' outlines through Skia's own path rasteriser, rather than
    /// through the platform's glyph scaler. DirectWrite on Windows and FreeType on Linux shade
    /// the same font file differently — thousands of pixels over a few lines, even unhinted —
    /// while the outlines come out byte for byte the same. Slower, and without hinting; for
    /// golden images, which have to match on every machine.
    /// </summary>
    public bool TextAsOutlines { get; set; }

    /// <summary>The face canvas-element text draws in, when not the platform's default — a
    /// pinned collection's, so that text is pinned too.</summary>
    internal SKTypeface? CanvasTypeface { get; set; }

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

        var blur = shadow.Blur > 0f ? BlurFilter(shadow.Blur * 0.5f) : null;

        _pooledPaint.Reset();
        _pooledPaint.Color = ToSkColor(shadow.Color);
        _pooledPaint.IsAntialias = true;
        _pooledPaint.MaskFilter = blur;

        var skRect = ToSkRect(rect);
        if (radius.IsZero)
            Canvas.DrawRect(skRect, _pooledPaint);
        else
            Canvas.DrawRoundRect(ToSkRoundRect(skRect, InflateRadius(radius, shadow.Spread)), _pooledPaint);

        // Detach the blur so the pooled paint never holds a filter the cache may dispose.
        _pooledPaint.MaskFilter = null;
    }

    // ── Effect objects ──
    //
    // A blur and a gradient are native objects with a managed wrapper each: made and disposed
    // per draw they cost 104 B a shadow and 168 B a gradient, on every frame they are on screen
    // (a popover's or a dialog's shadow, the scrims over a picture). They are kept instead:
    // filters by sigma, shaders by what they draw. Both maps are bounded and emptied whole when
    // full, like the blob cache; a draw detaches what it used, so emptying never frees an object
    // a paint still holds, and Skia keeps its own reference to anything a pending draw needs.

    private readonly Dictionary<float, SKMaskFilter?> _blurFilters = new();
    internal const int BlurFilterCacheMaxSize = 32;

    // Keyed by content, not by the Gradient: an app that builds its gradient in Build hands an
    // equal new one on every rebuild. The rect is the snapped one the shader spans, so a box
    // keeps its shader while it does not move; a scrolled box does not move (scrolling
    // translates the canvas).
    private readonly Dictionary<GradientKey, SKShader?> _gradientShaders = new(GradientKeyComparer.Instance);
    internal const int GradientShaderCacheMaxSize = 256;

    internal int CachedBlurFilterCount => _blurFilters.Count;
    internal int CachedGradientShaderCount => _gradientShaders.Count;

    private SKMaskFilter? BlurFilter(float sigma)
    {
        if (_blurFilters.TryGetValue(sigma, out var filter))
            return filter;
        if (_blurFilters.Count >= BlurFilterCacheMaxSize)
            ClearBlurFilters();
        filter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, sigma);
        _blurFilters[sigma] = filter;
        return filter;
    }

    private void ClearBlurFilters()
    {
        foreach (var filter in _blurFilters.Values)
            filter?.Dispose();
        _blurFilters.Clear();
    }

    private void ClearGradientShaders()
    {
        foreach (var shader in _gradientShaders.Values)
            shader?.Dispose();
        _gradientShaders.Clear();
    }

    /// <summary>What a gradient shader draws: the gradient's kind, angle and stops over a rect. A
    /// stored key holds a copy of the stops, so a stop array the app changes later cannot change
    /// a key inside the map.</summary>
    private readonly struct GradientKey(GradientKind kind, float angle, IReadOnlyList<GradientStop> stops, SKRect rect)
    {
        public readonly GradientKind Kind = kind;
        public readonly float Angle = angle;
        public readonly IReadOnlyList<GradientStop> Stops = stops;
        public readonly SKRect Rect = rect;
    }

    private sealed class GradientKeyComparer : IEqualityComparer<GradientKey>
    {
        public static readonly GradientKeyComparer Instance = new();

        public bool Equals(GradientKey a, GradientKey b)
        {
            if (a.Kind != b.Kind || !a.Angle.Equals(b.Angle) || !a.Rect.Equals(b.Rect)
                || a.Stops.Count != b.Stops.Count)
                return false;
            for (int i = 0; i < a.Stops.Count; i++)
                if (!a.Stops[i].Equals(b.Stops[i]))
                    return false;
            return true;
        }

        public int GetHashCode(GradientKey key)
        {
            var hash = new HashCode();
            hash.Add((int)key.Kind);
            hash.Add(key.Angle);
            hash.Add(key.Rect.Left);
            hash.Add(key.Rect.Top);
            hash.Add(key.Rect.Right);
            hash.Add(key.Rect.Bottom);
            for (int i = 0; i < key.Stops.Count; i++)
                hash.Add(key.Stops[i]);
            return hash.ToHashCode();
        }
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
        var skRect = ToSkRect(rect);

        _pooledPaint.Reset();
        _pooledPaint.IsAntialias = true;
        _pooledPaint.Shader = GradientShader(gradient, skRect);

        if (radius.IsZero)
            Canvas.DrawRect(skRect, _pooledPaint);
        else
            Canvas.DrawRoundRect(ToSkRoundRect(skRect, radius), _pooledPaint);

        // Detach so the pooled paint never holds a shader the cache may dispose.
        _pooledPaint.Shader = null;
    }

    private SKShader? GradientShader(Gradient gradient, SKRect skRect)
    {
        var stops = gradient.Stops;
        if (_gradientShaders.TryGetValue(new GradientKey(gradient.Kind, gradient.AngleDegrees, stops, skRect), out var cached))
            return cached;

        var colors = new SKColor[stops.Count];
        var positions = new float[stops.Count];
        var copy = new GradientStop[stops.Count];
        for (int i = 0; i < stops.Count; i++)
        {
            copy[i] = stops[i];
            colors[i] = ToSkColor(stops[i].Color);
            positions[i] = stops[i].Offset;
        }

        float cx = skRect.MidX, cy = skRect.MidY;
        var shader = gradient.Kind == GradientKind.Linear
            ? CreateLinearShader(gradient.AngleDegrees, skRect, colors, positions)
            : SKShader.CreateRadialGradient(
                new SKPoint(cx, cy),
                MathF.Sqrt(skRect.Width * skRect.Width + skRect.Height * skRect.Height) * 0.5f,
                colors, positions, SKShaderTileMode.Clamp);

        if (_gradientShaders.Count >= GradientShaderCacheMaxSize)
            ClearGradientShaders();
        _gradientShaders[new GradientKey(gradient.Kind, gradient.AngleDegrees, copy, skRect)] = shader;
        return shader;
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

                // The pooled path: the clip copies it, and Stroke draws a round rect, not a path.
                var tri = _pooledPath;
                tri.Rewind();
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

        // Indexed, here and in every text draw below: Lines and Runs are interfaces, and a
        // foreach over one boxes its enumerator — 72 B per label per frame, on every frame.
        var lines = layout.Lines;
        for (int li = 0; li < lines.Count; li++)
        {
            var line = lines[li];
            // Shift the line horizontally for center/right alignment. The layout
            // itself is width-independent (and shared from cache), so alignment is
            // applied here at draw time against the actual box width.
            float alignOffset = alignFactor > 0f && bounds.Width > line.Width
                ? (bounds.Width - line.Width) * alignFactor
                : 0f;
            float baseX = bounds.X + alignOffset;
            float lineY = bounds.Y + line.OffsetY + line.Ascent;

            DrawRuns(line, baseX, lineY);

            if (style.Decoration != TextDecoration.None)
                DrawTextDecoration(style, baseX, lineY, line);
        }
    }

    // Every run of one line at its baseline, in the pooled paint.
    private void DrawRuns(TextLine line, float baseX, float baselineY)
    {
        var runs = line.Runs;
        for (int ri = 0; ri < runs.Count; ri++)
        {
            var run = runs[ri];
            DrawRun(run, baseX + run.OffsetX, baselineY);
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

        var lines = layout.Lines;
        for (int li = 0; li < lines.Count; li++)
        {
            var line = lines[li];
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

    public TextParagraph LayoutParagraph(string text, ResolvedInheritedStyle style, float maxWidth,
        IReadOnlyList<TextSpan>? spans = null, int? maxLines = null, TextOverflow overflow = TextOverflow.Clip) =>
        _pipeline is null
            ? TextParagraph.FromLines(this, text, style, maxWidth, spans, maxLines, overflow)
            : _pipeline.LayoutParagraph(text, style, maxWidth, spans, maxLines, overflow);

    // The span-aware DrawText's loop over lines the paragraph already holds: nothing is laid out
    // again, and the empty line a paragraph adds after a final newline has no runs to draw.
    public void DrawParagraph(TextParagraph paragraph, Point origin)
    {
        if (paragraph is not PipelineTextParagraph p)
        {
            float width = float.IsInfinity(paragraph.MaxWidth) ? paragraph.Size.Width : paragraph.MaxWidth;
            DrawText(paragraph.Text, new Rect(origin.X, origin.Y, width, paragraph.Size.Height),
                paragraph.Style, paragraph.Overflow, paragraph.MaxLines, paragraph.Spans);
            return;
        }

        var style = p.Style;
        var spans = p.Spans is { Count: > 0 } s ? s : null;
        _pooledPaint.Reset();
        _pooledPaint.IsAntialias = true;

        // Lines wholly above or below the clip draw nothing: a paragraph in an editor can run
        // far past its viewport. A line's height of slack covers what hangs out of its box.
        var clip = Canvas.LocalClipBounds;
        var lines = p.TextLines;
        for (int i = 0; i < lines.Count; i++)
        {
            var line = lines[i];
            if (line.Runs.Count == 0) continue;
            float lineTop = origin.Y + line.OffsetY;
            if (lineTop > clip.Bottom + line.Height || lineTop + 2 * line.Height < clip.Top) continue;
            float baseX = origin.X + p.LineLeft(i);
            float lineY = lineTop + line.Ascent;

            if (spans is not null)
                DrawSpannedLine(line, spans, style, baseX, lineY, lineTop);
            else
            {
                _pooledPaint.Color = ToSkColor(style.TextColor);
                DrawRuns(line, baseX, lineY);
            }

            if (style.Decoration != TextDecoration.None)
            {
                _pooledPaint.Color = ToSkColor(style.TextColor);
                DrawTextDecoration(style, baseX, lineY, line);
            }
        }
    }

    public Rect? TextPaintBounds(string text, Rect bounds, ResolvedInheritedStyle style, TextOverflow overflow,
        int? maxLines, IReadOnlyList<TextSpan>? spans)
    {
        if (string.IsNullOrEmpty(text) || _pipeline is null) return new Rect(bounds.X, bounds.Y, 0f, 0f);

        // The same layout and the same placement as the DrawText overloads above.
        var layout = spans is { Count: > 0 }
            ? _pipeline.Layout(text, style, bounds.Width, maxLines, overflow, spans)
            : _pipeline.Layout(text, style, bounds.Width, maxLines, overflow);
        float alignFactor = TextAlignment.Factor(style.TextAlign, layout.IsRtlParagraph);
        // Decorations hang below the baseline by a fraction of the size (DrawDecorationRects).
        float underlineBottom = Math.Max(1f, style.FontSize * 0.12f) + Math.Max(1f, style.FontSize * 0.07f);

        // Indexed: these are interfaces, and foreach over one allocates an enumerator per line.
        var ink = new Rect(bounds.X, bounds.Y, 0f, 0f);
        var lines = layout.Lines;
        for (int li = 0; li < lines.Count; li++)
        {
            var line = lines[li];
            float alignOffset = alignFactor > 0f && bounds.Width > line.Width
                ? (bounds.Width - line.Width) * alignFactor
                : 0f;
            float baseX = bounds.X + alignOffset;
            float lineTop = bounds.Y + line.OffsetY;
            float lineY = lineTop + line.Ascent;

            // The line box holds span backgrounds; the underline can sit below it.
            ink = ink.Union(new Rect(baseX, lineTop, line.Width,
                Math.Max(line.Height, line.Ascent + underlineBottom)));

            // Glyph ink: the blob's bounds are Skia's conservative ones, from the font's bounding
            // box at each glyph's position, so an overhang or a stacked mark is inside them.
            var runs = line.Runs;
            for (int ri = 0; ri < runs.Count; ri++)
            {
                var run = runs[ri];
                SKRect r;
                if (TextAsOutlines)
                {
                    if (BuildShapedPath(run) is not { } path) continue;
                    r = path.Bounds;
                }
                else
                {
                    nint blob = BuildShapedBlob(run);
                    if (blob == 0) continue;
                    r = NativeTextBlob.Bounds(blob);
                }
                if (r.IsEmpty) continue;
                float x = baseX + run.OffsetX;
                ink = ink.Union(new Rect(x + r.Left, lineY + r.Top, r.Width, r.Height));
            }
        }
        return ink;
    }

    // Draws one laid-out line as constant-color segments split at span boundaries.
    // Glyphs are never re-shaped or moved: each segment clips to its x-range and
    // draws the same glyphs at the same places, so kerning and ligatures render exactly
    // as the plain path does (a boundary inside a ligature splits its color mid-glyph).
    private void DrawSpannedLine(TextLine line, IReadOnlyList<TextSpan> spans, ResolvedInheritedStyle style,
        float baseX, float baselineY, float lineTop)
    {
        // The drawn text is the concatenation of the line's runs (trailing
        // whitespace is trimmed at shaping; an ellipsized line ends in "…").
        // Drawn char i corresponds to source index line.Start + i.
        int drawnLength = 0;
        var runs = line.Runs;
        for (int ri = 0; ri < runs.Count; ri++)
            drawnLength += runs[ri].Text.Length;
        if (drawnLength == 0) return;

        var measurer = PrefixMeasurer(line, style);

        // Only the spans that reach into this line: sorted and apart, so the first is a binary
        // search away and the last is the one before the first that starts past the line. A long
        // paragraph has hundreds of spans and dozens of lines; scanning every span for every
        // line was most of drawing it.
        int first = FirstSpanEndingAfter(spans, line.Start);
        int stop = first;
        while (stop < spans.Count && spans[stop].Start < line.Start + drawnLength) stop++;

        // Background pass first so every glyph (including ones whose ink crosses
        // a span boundary) draws on top of the finished highlight rects.
        for (int k = first; k < stop; k++)
        {
            var span = spans[k];
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

        // Glyphs, one stretch of one colour at a time. Spans that meet in the same colour — a bold
        // word in the line's own colour, a code span — are one stretch under one clip: a clip and
        // a redraw of the runs under it for every span edge was most of a styled line's cost.
        // Each stretch draws only the glyphs that can reach it (DrawRunPart).
        PrepareStretches(runs.Count);
        int stretch = 0, at = 0;
        var colour = style.TextColor;
        for (int k = first; k < stop && at < drawnLength; k++)
        {
            var span = spans[k];
            int s = Math.Clamp(span.Start - line.Start, 0, drawnLength);
            int e = Math.Clamp(span.End - line.Start, 0, drawnLength);
            if (e <= at) continue;
            if (s > at) Continue(at, style.TextColor);
            // Transparent span color = background-only span; glyphs keep the
            // element's text color.
            Continue(Math.Max(s, at), span.Color.A == 0 ? style.TextColor : span.Color);
            at = e;
        }
        if (at < drawnLength) Continue(at, style.TextColor);
        DrawLineSegment(line, stretch, drawnLength, colour, style, baseX, baselineY, lineTop);

        // From `from` on the text is `c`: the stretch before it is drawn when that changes its colour.
        void Continue(int from, Color c)
        {
            if (c == colour) return;
            DrawLineSegment(line, stretch, from, colour, style, baseX, baselineY, lineTop);
            stretch = from;
            colour = c;
        }

        // Decorations last, over the glyphs — the same order the element-level
        // decoration draws in, so a spanned underline crosses descenders exactly
        // the way a whole-element one does.
        for (int k = first; k < stop; k++)
        {
            var span = spans[k];
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

    // The first of `spans` (sorted by start, apart) that ends after `offset`; Count when none does.
    private static int FirstSpanEndingAfter(IReadOnlyList<TextSpan> spans, int offset)
    {
        int lo = 0, hi = spans.Count;
        while (lo < hi)
        {
            int mid = (lo + hi) >> 1;
            if (spans[mid].End <= offset) lo = mid + 1;
            else hi = mid;
        }
        return lo;
    }

    private void DrawLineSegment(TextLine line, int startChar, int endChar, Color color,
        ResolvedInheritedStyle style, float baseX, float baselineY, float lineTop)
    {
        if (color.A == 0 || endChar <= startChar) return;

        _pooledPaint.Color = ToSkColor(color);
        line.VisualRanges(startChar, endChar, _rangeBuffer, PrefixMeasurer(line, style));
        var runs = line.Runs;
        foreach (var r in _rangeBuffer)
        {
            if (r.X1 <= r.X0) continue;
            int save = Canvas.Save();
            // Hard-edged (non-AA) clip so adjacent segments tile without seams; a glyph
            // crossing the boundary is drawn by both segments, each contributing its half.
            Canvas.ClipRect(SKRect.Create(baseX + r.X0, lineTop, r.X1 - r.X0, line.Height));
            for (int ri = 0; ri < runs.Count; ri++)
            {
                var run = runs[ri];
                if (run.OffsetX >= r.X1 || run.OffsetX + run.Width <= r.X0) continue;
                DrawRunPart(run, ri, baseX + run.OffsetX, baselineY, r.X0 - run.OffsetX, r.X1 - run.OffsetX);
            }
            Canvas.RestoreToCount(save);
        }
    }

    // Before a spanned line's stretches: no run's glyph order known yet, and the margin that
    // covers two device pixels in layout units, from the canvas matrix. A glyph's coverage
    // reaches at most a pixel past its outline, and the hard-edged clip takes in pixels whose
    // centre is inside it, half a pixel past its edge; a device pixel is 1/σ layout units at
    // most, σ the matrix's smaller singular value (a rotation leaves it 1, a scale-down makes it
    // small). Under perspective, or a matrix that collapses x, stretches draw whole runs.
    private void PrepareStretches(int runCount)
    {
        if (_runXOrder.Length < runCount)
        {
            _runXOrder = new byte[Math.Max(runCount, 2 * _runXOrder.Length)];
            _runInk = new InkExtent[_runXOrder.Length];
        }
        else Array.Clear(_runXOrder, 0, runCount);

        _stretchMargin = float.NaN;
        if (TextAsOutlines || !DrawsStretchesFromSubBlobs) return;
        var m = Canvas.TotalMatrix;
        if (m.Persp0 != 0f || m.Persp1 != 0f || m.Persp2 != 1f) return;
        float a = m.ScaleX, b = m.SkewY, c = m.SkewX, d = m.ScaleY;
        float sum = a * a + b * b + c * c + d * d, det = a * d - b * c;
        float sigma = MathF.Sqrt(MathF.Max(0f, (sum - MathF.Sqrt(MathF.Max(0f, sum * sum - 4f * det * det))) / 2f));
        if (sigma < 1e-3f || !float.IsFinite(sigma)) return;
        _stretchMargin = 2f / sigma;
    }

    /// <summary>
    /// One run under one colour stretch's clip, <paramref name="x0"/>..<paramref name="x1"/>
    /// run-relative: drawn from a kept sub-blob of only the glyphs whose ink can reach the clip.
    /// The rest would be clipped away whole, so leaving them out changes no pixel; drawing every
    /// run whole under every stretch handed Skia its glyphs once per stretch (plan 17, round 3:
    /// Junction's diff, 24,527 glyphs a hover frame). A glyph at X inks inside its run's extent
    /// around X (<see cref="RunInkExtentOf"/>: its face's, or its glyphs' own where the face's box
    /// is a guess) and the margin; a face with no extent (colour emoji, bitmap strikes) draws the
    /// run whole, as does a part that is the whole run.
    /// </summary>
    private void DrawRunPart(ShapedRun run, int runIndex, float x, float y, float x0, float x1)
    {
        if (float.IsNaN(_stretchMargin))
        {
            DrawRun(run, x, y);
            return;
        }
        var glyphs = run.Glyphs;
        // Once a line for each run: its extent, and whether its glyphs are in X order.
        if (_runXOrder[runIndex] == 0)
        {
            if (RunInkExtentAndOrderOf(run, out bool xOrdered) is { } extent)
            {
                _runInk[runIndex] = extent;
                _runXOrder[runIndex] = xOrdered ? (byte)1 : (byte)2;
            }
            else _runXOrder[runIndex] = 3;
        }
        if (_runXOrder[runIndex] == 3)
        {
            DrawRun(run, x, y);
            return;
        }
        var ink = _runInk[runIndex];
        float left = ink.Left * run.FontSize - _stretchMargin, right = ink.Right * run.FontSize + _stretchMargin;
        var (start, end) = GlyphRange.Reaching(glyphs, _runXOrder[runIndex] == 1, left, right, x0, x1);
        if (end <= start) return;   // no glyph inks this stretch: the whole run would draw nothing here
        if (end - start == glyphs.Length)
        {
            DrawRun(run, x, y);
            return;
        }
        nint blob = SubBlob(run, start, end);
        if (blob != 0)
        {
            NativeTextBlob.Draw(Canvas, blob, x, y, _pooledPaint);
            GlyphsDrawn += end - start;
        }
        GC.KeepAlive(this);
    }

    // The kept sub-blob of glyphs [start, end) of a run, at their places in the run.
    private nint SubBlob(ShapedRun run, int start, int end)
    {
        var cache = _subBlobCache ??= new Dictionary<SubRun, nint>(RunBlobCacheMaxSize);
        var key = new SubRun(run, start, end);
        if (cache.TryGetValue(key, out nint blob))
            return blob;

        _pooledFont.Typeface = (SKTypeface)run.Font.NativeHandle;
        _pooledFont.Size = run.FontSize;
        var buffer = _blobBuilder.AllocateRawPositionedRun(_pooledFont, end - start);
        var glyphSpan = buffer.Glyphs;
        var positionSpan = buffer.Positions;
        var glyphs = run.Glyphs;
        for (int i = start; i < end; i++)
        {
            glyphSpan[i - start] = (ushort)glyphs[i].GlyphId;
            positionSpan[i - start] = new SKPoint(glyphs[i].X, glyphs[i].Y);
        }
        blob = NativeTextBlob.Make(_blobBuilder);
        if (blob != 0) BlobsMade++;

        if (cache.Count >= RunBlobCacheMaxSize)
            ClearSubBlobCache();
        cache[key] = blob;
        return blob;
    }

    private void ClearSubBlobCache()
    {
        if (_subBlobCache is not { } cache) return;
        foreach (var blob in cache.Values)
            Release(blob);
        cache.Clear();
    }

    /// <summary>
    /// How far left and right of its origin any glyph of <paramref name="font"/> can ink, per
    /// unit of font size: the face's bounding box — what Skia itself culls whole blobs by — plus
    /// a slack for what the box leaves out (an eighth of the size; a quarter more for a slanted
    /// face, whose synthetic slant the box may not include). Null for a face whose box Skia marks
    /// untrustworthy or that is empty — every variable font, whose box is its default instance's;
    /// its runs give their own (<see cref="RunInkExtentOf"/>) — and for colour and bitmap faces
    /// (COLR, CBDT, sbix, SVG, EBDT): their glyphs are images whose bounds are their own.
    /// </summary>
    internal InkExtent? InkExtentOf(IResolvedFont font) => FaceInkOf(font).Box;

    /// <summary>
    /// How far left and right of its origin any glyph of <paramref name="run"/> can ink, per unit
    /// of font size: its face's extent (<see cref="InkExtentOf"/>) or, for an outline face whose
    /// box Skia marks as a guess (Cascadia Mono and Code, Segoe UI Variable, Bahnschrift, Noto
    /// Sans on Fedora: variable fonts), the union of its own glyphs' bounds as Skia gives them for
    /// that face, with the same slack. The glyphs' bounds are read once and kept for the face
    /// (<see cref="GlyphInks"/>); the union is taken again on each call, once per run a spanned
    /// line draws, and holds nothing of the run. Null where neither is known: a colour or bitmap
    /// face, whose runs are drawn whole.
    /// </summary>
    internal InkExtent? RunInkExtentOf(ShapedRun run) => RunInkExtentAndOrderOf(run, out _);

    // The same, and whether the run's glyphs are in X order (GlyphRange.XOrdered), found in the
    // same pass over them where the extent is the glyphs' own.
    internal InkExtent? RunInkExtentAndOrderOf(ShapedRun run, out bool xOrdered)
    {
        var face = FaceInkOf(run.Font);
        var glyphs = run.Glyphs;
        if (face.Glyphs is not { } inks)
        {
            xOrdered = face.Box is not null && GlyphRange.XOrdered(glyphs);
            return face.Box;
        }
        RunExtentsFromGlyphs++;

        // The union of the glyphs' bounds at 2048, as the face's box is taken, in any order: the
        // glyphs read now are joined after the rest. The table holds no NaN once a glyph is read,
        // so a comparison finds the least and the most as MathF.Min and Max did over the run read
        // whole, but for the sign of a zero, which the slack added below takes away.
        const float Size = 2048f;
        float left = 0f, right = 0f, lastX = float.NegativeInfinity;
        bool ordered = true;
        int unread = 0;
        InkExtent[]? page = null;
        int pageOf = -1;
        for (int i = 0; i < glyphs.Length; i++)
        {
            ref readonly var glyph = ref glyphs[i];
            if (glyph.X < lastX) ordered = false;
            lastX = glyph.X;
            ushort id = (ushort)glyph.GlyphId;
            if (id >> 8 != pageOf)
            {
                page = inks.PageOf(id);
                pageOf = id >> 8;
            }
            var ink = page![(byte)id];
            if (float.IsNaN(ink.Left))
            {
                // Not read yet: read with the rest of its piece, once.
                if (!float.IsNaN(ink.Right)) continue;
                page[(byte)id] = GlyphInks.Asked;
                (_boundsGlyphs ??= new ushort[BoundsPiece])[unread++] = id;
                if (unread == BoundsPiece)
                {
                    ReadGlyphInks(run.Font, inks, unread, ref left, ref right);
                    unread = 0;
                }
                continue;
            }
            if (ink.Left < left) left = ink.Left;
            if (ink.Right > right) right = ink.Right;
        }
        if (unread > 0) ReadGlyphInks(run.Font, inks, unread, ref left, ref right);
        xOrdered = ordered;

        // A bound that is not a number reaches everywhere: every stretch then draws the run whole.
        return float.IsFinite(left) && float.IsFinite(right)
            ? new InkExtent(left / Size - face.Slack, right / Size + face.Slack)
            : new InkExtent(float.NegativeInfinity, float.PositiveInfinity);
    }

    // Reads the bounds of the first `count` glyphs in _boundsGlyphs from Skia, in one call (a glyph
    // Skia has metrics for already costs a lookup), keeps them in the face's table, and joins them
    // into the union.
    private void ReadGlyphInks(IResolvedFont font, GlyphInks inks, int count, ref float left, ref float right)
    {
        var ids = _boundsGlyphs!;
        var bounds = _glyphBounds ??= new SKRect[BoundsPiece];
        _boundsFont.Typeface = (SKTypeface)font.NativeHandle;
        _boundsFont.Size = 2048f;
        _boundsFont.GetGlyphWidths(ids.AsSpan(0, count), Span<float>.Empty, bounds.AsSpan(0, count));
        GlyphInksRead += count;
        for (int i = 0; i < count; i++)
        {
            var b = bounds[i];
            var ink = b.IsEmpty ? new InkExtent(float.PositiveInfinity, float.NegativeInfinity)
                : float.IsNaN(b.Left) || float.IsNaN(b.Right) ? new InkExtent(float.NegativeInfinity, float.PositiveInfinity)
                : new InkExtent(b.Left, b.Right);
            inks.PageOf(ids[i])[(byte)ids[i]] = ink;
            if (ink.Left < left) left = ink.Left;
            if (ink.Right > right) right = ink.Right;
        }
    }

    // What the face's ink is known by: its box, its runs' glyphs (an outline face whose box is a
    // guess), or neither (a colour or bitmap face, or one without outlines).
    private FaceInk FaceInkOf(IResolvedFont font)
    {
        if (_inkExtents.TryGetValue(font, out var known)) return known;
        var face = default(FaceInk);
        var typeface = (SKTypeface)font.NativeHandle;
        bool outlines = typeface.GetTableSize(Tag('g', 'l', 'y', 'f')) > 0
            || typeface.GetTableSize(Tag('C', 'F', 'F', ' ')) > 0 || typeface.GetTableSize(Tag('C', 'F', 'F', '2')) > 0;
        bool images = typeface.GetTableSize(Tag('C', 'O', 'L', 'R')) > 0 || typeface.GetTableSize(Tag('C', 'B', 'D', 'T')) > 0
            || typeface.GetTableSize(Tag('s', 'b', 'i', 'x')) > 0 || typeface.GetTableSize(Tag('S', 'V', 'G', ' ')) > 0
            || typeface.GetTableSize(Tag('E', 'B', 'D', 'T')) > 0;
        if (outlines && !images)
        {
            // As SkTypeface computes its bounds: the metrics at 2048 with linear metrics, scaled
            // to one unit. SkFontMetrics' first field is its flags (NativeTextBlobTests holds the
            // layout), and kBoundsInvalid_Flag says the box is a guess.
            const float Size = 2048f;
            const uint BoundsInvalid = 1u << 4;
            _boundsFont.Typeface = typeface;
            _boundsFont.Size = Size;
            _boundsFont.GetFontMetrics(out var metrics);
            uint flags = System.Runtime.CompilerServices.Unsafe.As<SKFontMetrics, uint>(ref metrics);
            float xMin = metrics.XMin / Size, xMax = metrics.XMax / Size;
            float slack = typeface.FontSlant == SKFontStyleSlant.Upright ? 0.125f : 0.375f;
            face = (flags & BoundsInvalid) == 0 && float.IsFinite(xMin) && float.IsFinite(xMax) && xMax > xMin
                ? new FaceInk(new InkExtent(MathF.Min(xMin, 0f) - slack, MathF.Max(xMax, 0f) + slack), null, slack)
                : new FaceInk(null, new GlyphInks(), slack);
        }
        _inkExtents[font] = face;
        return face;

        static uint Tag(char a, char b, char c, char d) => (uint)(a << 24 | b << 16 | c << 8 | d);
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

        // A renderer-owned frame's image is the surface's: it knows when the storage under it is
        // replaced, and keeps the wrapper until then (SkiaVulkanRenderSurface). A source-owned GL
        // texture has no layout to go stale and is wrapped here, once per texture and size.
        SKImage? image;
        if (frame.RendererOwned)
        {
            // A source that drew into a texture of its own before the surface lent it one.
            if (_wrappedTextures.Remove(source, out var stale)) stale.Image.Dispose();
            image = gl.WrapFrame(frame);
            if (image is null) return;
        }
        else
        {
            bool haveWrapper = _wrappedTextures.TryGetValue(source, out var wrapped);
            if (!haveWrapper || wrapped.Frame != frame)
            {
                wrapped.Image?.Dispose();
                var made = gl.WrapFrame(frame);
                if (made is null)
                {
                    _wrappedTextures.Remove(source);
                    return;
                }
                wrapped = (frame, made);
                _wrappedTextures[source] = wrapped;
            }
            image = wrapped.Image;
        }

        var (drawRect, srcRect) = ComputeImageFit(fit, frame.Width, frame.Height,
            destRect.Width, destRect.Height, destRect);

        _pooledPaint.Reset();
        _pooledPaint.IsAntialias = true;
        // A frame of linear light arrives in the producer's luminance units, not the canvas's.
        // The surface owns that conversion because only it knows what its own 1.0 is worth.
        _pooledPaint.ColorFilter = gl.FrameColorFilter(frame);
        Canvas.DrawImage(image, srcRect, ToSkRect(drawRect), FrameSampling, _pooledPaint);
        _pooledPaint.ColorFilter = null;
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

    public ITextPrefixWidths? MeasureTextPrefixes(string text, ResolvedInheritedStyle style) =>
        _pipeline?.MeasurePrefixes(text, style);

    // A view in the pipeline's scratch, good until the next text call (IRenderContext's contract).
    public ITextPrefixWidths? MeasureTextPrefixes(string text, int start, int length, ResolvedInheritedStyle style) =>
        _pipeline?.MeasurePrefixes(text, start, length, style);

    public float MonospaceAsciiAdvance(ResolvedInheritedStyle style) =>
        _pipeline?.MonospaceAsciiAdvance(style) ?? 0f;

    public float MeasureTextWidth(ReadOnlySpan<char> text, ResolvedInheritedStyle style) =>
        text.IsEmpty || _pipeline is null ? 0f : _pipeline.MeasureWidth(text, style);

    public FontMetrics MeasureFont(ResolvedInheritedStyle style) =>
        _pipeline?.MeasureFont(style) ?? FontMetrics.Estimate(style, Size.Zero);

    public bool HasGlyph(ResolvedInheritedStyle style, int codePoint) =>
        _pipeline?.HasGlyph(style, codePoint) ?? true;

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

    // The line and its measurer found once; each boundary is then what TextCaretX gives for it.
    public void TextCaretXs(string lineText, ResolvedInheritedStyle style, Span<float> xs)
    {
        var line = SingleLine(lineText, style);
        if (line is null)
        {
            xs.Clear();
            return;
        }
        var measurer = PrefixMeasurer(line, style);
        for (int k = 0; k < xs.Length; k++)
            xs[k] = line.CaretXAt(k, measurer);
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
    private TextPrefixMeasurer? PrefixMeasurer(TextLine line, ResolvedInheritedStyle style)
    {
        var runs = line.Runs;
        for (int i = 0; i < runs.Count; i++)
            if (runs[i].Glyphs.Length != runs[i].Text.Length)
                return MeasurerFor(style);
        return null;
    }

    // One measurer per style, kept. A line that merged clusters asks for one every time it is
    // drawn with spans or a selection (for its backgrounds and decorations, and again for each
    // colour stretch), and a caret or a click on it asks again: a new closure each time was
    // ~120 B, ~1 KB a frame over two such lines in a viewer. Keyed by the style's value, which is
    // a struct: a key by reference would never find the one made before.
    private readonly Dictionary<ResolvedInheritedStyle, TextPrefixMeasurer> _prefixMeasurers = new();
    private const int PrefixMeasurerCacheMaxSize = 64;

    private TextPrefixMeasurer MeasurerFor(ResolvedInheritedStyle style)
    {
        if (_prefixMeasurers.TryGetValue(style, out var measurer))
            return measurer;
        if (_prefixMeasurers.Count >= PrefixMeasurerCacheMaxSize)
            _prefixMeasurers.Clear();
        return _prefixMeasurers[style] = NewMeasurer(style);
    }

    // Its own method because a lambda capturing a parameter makes C# allocate the closure on
    // entry to the method that declares it, so MeasurerFor would make one on a hit too. A prefix
    // is measured where it stands in the run's text (MeasureTextWidth, the width MeasureText
    // gives it, to the bit), not cut out as a string for every boundary asked about.
    private TextPrefixMeasurer NewMeasurer(ResolvedInheritedStyle style) =>
        (text, length) => MeasureTextWidth(text.AsSpan(0, length), style);

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

    public void FillPolygon(ReadOnlySpan<Point> points, Color fill)
    {
        if (points.Length < 3 || fill.A == 0) return;
        var path = PathThrough(points);
        path.Close();
        _pooledPaint.Reset();
        _pooledPaint.Color = ToSkColor(fill);
        _pooledPaint.IsAntialias = true;
        Canvas.DrawPath(path, _pooledPaint);
    }

    public void DrawPolyline(ReadOnlySpan<Point> points, Color color, float strokeWidth)
    {
        if (points.Length < 2 || color.A == 0 || strokeWidth <= 0) return;
        var path = PathThrough(points);
        _pooledPaint.Reset();
        _pooledPaint.Color = ToSkColor(color);
        _pooledPaint.IsAntialias = true;
        _pooledPaint.IsStroke = true;
        _pooledPaint.StrokeWidth = strokeWidth;
        _pooledPaint.StrokeJoin = SKStrokeJoin.Round;
        _pooledPaint.StrokeCap = SKStrokeCap.Butt;
        Canvas.DrawPath(path, _pooledPaint);
    }

    // The pooled path, emptied, through the points. The draw copies the geometry, so the path is
    // free again once it returns. A new SKPath per shape was ~80 B, and the shapes are drawn
    // again every frame: a terminal's Powerline separators (5.6 KB a frame over six prompt
    // lines), a rich text editor's ticks.
    private SKPath PathThrough(ReadOnlySpan<Point> points)
    {
        var path = _pooledPath;
        path.Rewind();
        path.MoveTo(points[0].X, points[0].Y);
        for (int i = 1; i < points.Length; i++) path.LineTo(points[i].X, points[i].Y);
        return path;
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

    public Rect? ClipBounds
    {
        get
        {
            var r = Canvas.LocalClipBounds;
            return new Rect(r.Left, r.Top, r.Width, r.Height);
        }
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

    public void PushOpacity(float opacity, Rect bounds)
    {
        _pooledPaint.Reset();
        _pooledPaint.Color = new SKColor(255, 255, 255, (byte)(opacity * 255));
        // Slack for pixel snapping, which can move an edge half a device pixel outward and widens
        // a sliver to a whole one: 1/scale in layout units, transformed or not, since snapping
        // ignores the transform. Skia rounds the bounds out to whole pixels itself.
        float pad = 1f / _scale;
        var rect = new SKRect(bounds.X - pad, bounds.Y - pad, bounds.Right + pad, bounds.Bottom + pad);
        _saveStack.Push(Canvas.SaveLayer(rect, _pooledPaint));
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

    /// <summary>One shaped run at a baseline, in <see cref="_pooledPaint"/>: as a text blob, or
    /// as outlines under <see cref="TextAsOutlines"/>.</summary>
    private void DrawRun(ShapedRun run, float x, float y)
    {
        if (TextAsOutlines)
        {
            if (BuildShapedPath(run) is not { } path) return;
            Canvas.Save();
            Canvas.Translate(x, y);
            _pooledPaint.Style = SKPaintStyle.Fill;
            Canvas.DrawPath(path, _pooledPaint);
            Canvas.Restore();
            GlyphsDrawn += run.Glyphs.Length;
            return;
        }
        nint blob = BuildShapedBlob(run);
        if (blob != 0)
        {
            NativeTextBlob.Draw(Canvas, blob, x, y, _pooledPaint);
            GlyphsDrawn += run.Glyphs.Length;
        }
        // The cache, and so the blob, stays alive until the draw has returned.
        GC.KeepAlive(this);
    }

    private SKPath? BuildShapedPath(ShapedRun run)
    {
        if (_runPathCache.TryGetValue(run, out var cached))
            return cached;
        if (run.Glyphs.Length == 0)
            return null;

        _outlineFont.Typeface = (SKTypeface)run.Font.NativeHandle;
        _outlineFont.Size = run.FontSize;
        var path = new SKPath();
        foreach (var glyph in run.Glyphs)
        {
            using var outline = _outlineFont.GetGlyphPath((ushort)glyph.GlyphId);
            if (outline is not null)
                path.AddPath(outline, glyph.X, glyph.Y);
        }

        if (_runPathCache.Count >= RunBlobCacheMaxSize)
            ClearRunPathCache();
        _runPathCache[run] = path;
        return path;
    }

    private void ClearRunPathCache()
    {
        foreach (var path in _runPathCache.Values)
            path?.Dispose();
        _runPathCache.Clear();
    }

    internal nint BlobOf(ShapedRun run) => BuildShapedBlob(run);

    // The cached blob of a run, made on its first draw; 0 for a run with no glyphs.
    private nint BuildShapedBlob(ShapedRun run)
    {
        if (_runBlobCache.TryGetValue(run, out var cached))
            return cached;

        // Read once: a run of several words puts its glyph array together on the first read.
        var glyphs = run.Glyphs;
        if (glyphs.Length == 0)
            return 0;

        var typeface = (SKTypeface)run.Font.NativeHandle;
        _pooledFont.Typeface = typeface;
        _pooledFont.Size = run.FontSize;

        // The raw buffer is a struct. AllocatePositionedRun hands out the same two spans in a
        // class, 56 B more per new blob wherever the compiler does not keep it on the stack:
        // NativeAOT's build of the perf suite heap-allocated it (scroll/jump 9.1 -> 6.3 KB/op).
        var buffer = _blobBuilder.AllocateRawPositionedRun(_pooledFont, glyphs.Length);
        var glyphSpan = buffer.Glyphs;
        var positionSpan = buffer.Positions;

        for (int i = 0; i < glyphs.Length; i++)
        {
            glyphSpan[i] = (ushort)glyphs[i].GlyphId;
            positionSpan[i] = new SKPoint(glyphs[i].X, glyphs[i].Y);
        }

        nint blob = NativeTextBlob.Make(_blobBuilder);
        if (blob != 0) BlobsMade++;

        if (_runBlobCache.Count >= RunBlobCacheMaxSize)
            ClearRunBlobCache();

        _runBlobCache[run] = blob;
        return blob;
    }

    // Gives up this context's reference to every cached blob: each was made once and is
    // released once, here, whether the cache filled up or the context is going away.
    private void ClearRunBlobCache()
    {
        foreach (var blob in _runBlobCache.Values)
            Release(blob);
        _runBlobCache.Clear();
    }

    private void Release(nint blob)
    {
        if (blob == 0) return;
        NativeTextBlob.Unref(blob);
        BlobsReleased++;
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

    // A context nobody disposed still gives its blobs back, as the SKTextBlob wrappers' own
    // finalizers did: nothing can draw with an unreachable context, and Skia's unref is safe on
    // any thread. Everything else it holds is a SkiaSharp object with a finalizer of its own.
    ~SkiaRenderContext()
    {
        ClearRunBlobCache();
        ClearSubBlobCache();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        GC.SuppressFinalize(this);
        ReleaseHeldFrames();
        foreach (var wrapped in _wrappedTextures.Values) wrapped.Image.Dispose();
        _wrappedTextures.Clear();
        ClearRunBlobCache();
        ClearSubBlobCache();
        ClearRunPathCache();
        ClearBlurFilters();
        ClearGradientShaders();
        _blobBuilder.Dispose();
        _outlineFont.Dispose();
        _boundsFont.Dispose();
        _pooledPaint.Dispose();
        _pooledFont.Dispose();
        _pooledRoundRect.Dispose();
        _pooledPath.Dispose();
        _pooledCanvasFont.Dispose();
    }
}
