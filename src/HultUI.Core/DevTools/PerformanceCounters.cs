using System.Diagnostics;

namespace HultUI.Core.DevTools;

public sealed class PerformanceCounters
{
    /// <summary>Trailing window over which the "slowest pass" peaks are tracked.</summary>
    public static readonly TimeSpan PeakWindow = TimeSpan.FromSeconds(5);

    private readonly Stopwatch _frameTimer = new();
    private readonly Stopwatch _layoutTimer = new();
    private readonly Stopwatch _renderTimer = new();
    private readonly Stopwatch _styleTimer = new();
    private readonly Stopwatch _flushTimer = new();
    private readonly Stopwatch _presentTimer = new();
    private readonly Stopwatch _overlayTimer = new();

    private readonly float[] _frameTimes = new float[60];
    private int _frameIndex;

    private readonly SlidingWindowMax _framePeak = new(PeakWindow);
    private readonly SlidingWindowMax _stylePeak = new(PeakWindow);
    private readonly SlidingWindowMax _layoutPeak = new(PeakWindow);
    private readonly SlidingWindowMax _renderPeak = new(PeakWindow);
    private readonly SlidingWindowMax _flushPeak = new(PeakWindow);
    private readonly SlidingWindowMax _presentPeak = new(PeakWindow);
    private readonly SlidingWindowMax _overlayPeak = new(PeakWindow);
    private readonly SlidingWindowMax _otherPeak = new(PeakWindow);

    public float FrameTimeMs { get; private set; }
    public float LayoutTimeMs { get; private set; }
    public float RenderTimeMs { get; private set; }
    public float StyleTimeMs { get; private set; }

    /// <summary>
    /// GPU flush time: <c>Canvas.Flush()</c> — where the display list recorded
    /// during the Render pass is actually rasterized and submitted to the GPU.
    /// Distinct from Render (command recording) and Present (buffer swap).
    /// </summary>
    public float FlushTimeMs { get; private set; }
    public float PresentTimeMs { get; private set; }

    /// <summary>
    /// Observer tax of the DevTools overlay itself: the element-count tree walk
    /// plus recording the HUD's own draw commands. Only non-zero while the HUD
    /// is shown, so it quantifies how much the measurement perturbs the frame.
    /// </summary>
    public float OverlayTimeMs { get; private set; }

    /// <summary>
    /// Frame time not attributed to any timed phase: post-frame dirty-flag tree
    /// walks (ClearAllFlags), caption-height updates, action-router draining, and
    /// other small per-frame bookkeeping. Computed as the residual so the rows
    /// always sum to the frame total without smearing this work into a phase it
    /// doesn't belong to. Clamped at 0 (stopwatch quantization can make the timed
    /// spans sum a hair over the frame timer).
    /// </summary>
    public float OtherTimeMs { get; private set; }
    public float AverageFrameTimeMs { get; private set; }
    public float Fps => AverageFrameTimeMs > 0 ? 1000f / AverageFrameTimeMs : 0;
    public int ElementCount { get; set; }

    // ──── Slowest pass over the trailing PeakWindow ────
    public float FrameTimePeakMs => _framePeak.Max;
    public float StyleTimePeakMs => _stylePeak.Max;
    public float LayoutTimePeakMs => _layoutPeak.Max;
    public float RenderTimePeakMs => _renderPeak.Max;
    public float FlushTimePeakMs => _flushPeak.Max;
    public float PresentTimePeakMs => _presentPeak.Max;
    public float OverlayTimePeakMs => _overlayPeak.Max;
    public float OtherTimePeakMs => _otherPeak.Max;

    public void BeginFrame() => _frameTimer.Restart();

    public void EndFrame()
    {
        _frameTimer.Stop();
        FrameTimeMs = (float)_frameTimer.Elapsed.TotalMilliseconds;
        _frameTimes[_frameIndex] = FrameTimeMs;
        _frameIndex = (_frameIndex + 1) % _frameTimes.Length;

        float sum = 0;
        foreach (var t in _frameTimes) sum += t;
        AverageFrameTimeMs = sum / _frameTimes.Length;

        // Residual: whatever the frame timer captured that no phase span did.
        // Clamp at 0 — stopwatch quantization can push the phase sum a hair over.
        float phaseSum = StyleTimeMs + LayoutTimeMs + RenderTimeMs
            + FlushTimeMs + PresentTimeMs + OverlayTimeMs;
        OtherTimeMs = Math.Max(0f, FrameTimeMs - phaseSum);

        // Fold this frame's pass timings into the rolling peaks. A single
        // timestamp keeps every stat on the same window and bounds the work to
        // one Add per stat per frame.
        long now = Stopwatch.GetTimestamp();
        _framePeak.Add(FrameTimeMs, now);
        _stylePeak.Add(StyleTimeMs, now);
        _layoutPeak.Add(LayoutTimeMs, now);
        _renderPeak.Add(RenderTimeMs, now);
        _flushPeak.Add(FlushTimeMs, now);
        _presentPeak.Add(PresentTimeMs, now);
        _overlayPeak.Add(OverlayTimeMs, now);
        _otherPeak.Add(OtherTimeMs, now);
    }

    public void BeginStyle() => _styleTimer.Restart();
    public void EndStyle() { _styleTimer.Stop(); StyleTimeMs = (float)_styleTimer.Elapsed.TotalMilliseconds; }

    public void BeginLayout() => _layoutTimer.Restart();
    public void EndLayout() { _layoutTimer.Stop(); LayoutTimeMs = (float)_layoutTimer.Elapsed.TotalMilliseconds; }

    public void BeginRender() => _renderTimer.Restart();
    public void EndRender() { _renderTimer.Stop(); RenderTimeMs = (float)_renderTimer.Elapsed.TotalMilliseconds; }

    public void BeginFlush() => _flushTimer.Restart();
    public void EndFlush() { _flushTimer.Stop(); FlushTimeMs = (float)_flushTimer.Elapsed.TotalMilliseconds; }

    public void BeginPresent() => _presentTimer.Restart();
    public void EndPresent() { _presentTimer.Stop(); PresentTimeMs = (float)_presentTimer.Elapsed.TotalMilliseconds; }

    public void BeginOverlay() => _overlayTimer.Restart();
    public void EndOverlay() { _overlayTimer.Stop(); OverlayTimeMs = (float)_overlayTimer.Elapsed.TotalMilliseconds; }

    public static int CountElements(Core.Elements.Element root)
    {
        int count = 1;
        foreach (var child in root.Children)
            count += CountElements(child);
        return count;
    }
}
