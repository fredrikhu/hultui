using System.Diagnostics;

namespace HultUI.Core.DevTools;

public sealed class PerformanceCounters
{
    private readonly Stopwatch _frameTimer = new();
    private readonly Stopwatch _layoutTimer = new();
    private readonly Stopwatch _renderTimer = new();
    private readonly Stopwatch _styleTimer = new();
    private readonly Stopwatch _presentTimer = new();

    private readonly float[] _frameTimes = new float[60];
    private int _frameIndex;

    public float FrameTimeMs { get; private set; }
    public float LayoutTimeMs { get; private set; }
    public float RenderTimeMs { get; private set; }
    public float StyleTimeMs { get; private set; }
    public float PresentTimeMs { get; private set; }
    public float AverageFrameTimeMs { get; private set; }
    public float Fps => AverageFrameTimeMs > 0 ? 1000f / AverageFrameTimeMs : 0;
    public int ElementCount { get; set; }

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
