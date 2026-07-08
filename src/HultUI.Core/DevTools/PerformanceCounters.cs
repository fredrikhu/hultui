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
    private readonly Stopwatch _presentTimer = new();

    private readonly float[] _frameTimes = new float[60];
    private int _frameIndex;

    private readonly SlidingWindowMax _framePeak = new(PeakWindow);
    private readonly SlidingWindowMax _stylePeak = new(PeakWindow);
    private readonly SlidingWindowMax _layoutPeak = new(PeakWindow);
    private readonly SlidingWindowMax _renderPeak = new(PeakWindow);
    private readonly SlidingWindowMax _presentPeak = new(PeakWindow);

    public float FrameTimeMs { get; private set; }
    public float LayoutTimeMs { get; private set; }
    public float RenderTimeMs { get; private set; }
    public float StyleTimeMs { get; private set; }
    public float PresentTimeMs { get; private set; }
    public float AverageFrameTimeMs { get; private set; }
    public float Fps => AverageFrameTimeMs > 0 ? 1000f / AverageFrameTimeMs : 0;
    public int ElementCount { get; set; }

    // ──── Slowest pass over the trailing PeakWindow ────
    public float FrameTimePeakMs => _framePeak.Max;
    public float StyleTimePeakMs => _stylePeak.Max;
    public float LayoutTimePeakMs => _layoutPeak.Max;
    public float RenderTimePeakMs => _renderPeak.Max;
    public float PresentTimePeakMs => _presentPeak.Max;

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

        // Fold this frame's pass timings into the rolling peaks. A single
        // timestamp keeps every stat on the same window and bounds the work to
        // one Add per stat per frame.
        long now = Stopwatch.GetTimestamp();
        _framePeak.Add(FrameTimeMs, now);
        _stylePeak.Add(StyleTimeMs, now);
        _layoutPeak.Add(LayoutTimeMs, now);
        _renderPeak.Add(RenderTimeMs, now);
        _presentPeak.Add(PresentTimeMs, now);
    }

    public void BeginStyle() => _styleTimer.Restart();
    public void EndStyle() { _styleTimer.Stop(); StyleTimeMs = (float)_styleTimer.Elapsed.TotalMilliseconds; }

    public void BeginLayout() => _layoutTimer.Restart();
    public void EndLayout() { _layoutTimer.Stop(); LayoutTimeMs = (float)_layoutTimer.Elapsed.TotalMilliseconds; }

    public void BeginRender() => _renderTimer.Restart();
    public void EndRender() { _renderTimer.Stop(); RenderTimeMs = (float)_renderTimer.Elapsed.TotalMilliseconds; }

    public void BeginPresent() => _presentTimer.Restart();
    public void EndPresent() { _presentTimer.Stop(); PresentTimeMs = (float)_presentTimer.Elapsed.TotalMilliseconds; }

    public static int CountElements(Core.Elements.Element root)
    {
        int count = 1;
        foreach (var child in root.Children)
            count += CountElements(child);
        return count;
    }
}
