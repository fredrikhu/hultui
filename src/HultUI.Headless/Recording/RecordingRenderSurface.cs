using HultUI.Core.Rendering;
using HultUI.Rendering.Skia;
using SkiaSharp;

namespace HultUI.Headless.Recording;

/// <summary>
/// Wraps a render surface so every drawing call is captured into a
/// <see cref="RenderLog"/> while still rendering normally through the inner
/// surface — snapshot and pixel capture can be used together.
/// </summary>
internal sealed class RecordingRenderSurface : IRenderSurface, ISkiaFrameCapture
{
    private readonly IRenderSurface _inner;
    private readonly RecordingRenderContext _context;
    private readonly IMeasureContext _measureContext;

    /// <param name="inner">The surface that actually renders.</param>
    /// <param name="measureOverride">
    /// Replaces the inner surface's text measurement. Supply
    /// <see cref="FixedMetricsMeasureContext"/> for baselines that must match
    /// across operating systems; leave null to measure with real fonts.
    /// </param>
    public RecordingRenderSurface(IRenderSurface inner, IMeasureContext? measureOverride = null)
    {
        _inner = inner;
        _context = new RecordingRenderContext(inner.RenderContext);
        _measureContext = measureOverride ?? inner.MeasureContext;
    }

    public RenderLog Log => _context.Log;

    public IRenderContext RenderContext => _context;
    public IMeasureContext MeasureContext => _measureContext;

    public void Resize(int widthPx, int heightPx, float scaleFactor)
        => _inner.Resize(widthPx, heightPx, scaleFactor);

    public void Present() => _inner.Present();

    /// <summary>The inner surface's frame: recording draws nothing of its own.</summary>
    public SKImage PresentCapturing()
    {
        if (_inner is ISkiaFrameCapture capture) return capture.PresentCapturing();
        _inner.Present();
        throw new NotSupportedException("The recorded surface cannot hand over its frame.");
    }

    public void Dispose() => _inner.Dispose();
}
