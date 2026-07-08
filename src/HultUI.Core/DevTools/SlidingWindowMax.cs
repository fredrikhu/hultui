using System.Diagnostics;

namespace HultUI.Core.DevTools;

/// <summary>
/// Tracks the maximum value observed within a trailing time window. Backed by
/// a monotonic (decreasing) deque so both <see cref="Add"/> and reading
/// <see cref="Max"/> are O(1) amortized regardless of how many samples fall in
/// the window. Used by the performance HUD to report the slowest recent pass
/// for each stat.
/// </summary>
internal sealed class SlidingWindowMax
{
    private readonly struct Sample(long timestamp, float value)
    {
        public readonly long Timestamp = timestamp;
        public readonly float Value = value;
    }

    private readonly long _windowTicks;
    private readonly LinkedList<Sample> _samples = new();

    public SlidingWindowMax(TimeSpan window)
    {
        _windowTicks = (long)(window.TotalSeconds * Stopwatch.Frequency);
    }

    /// <summary>The largest value added within the trailing window, or 0 if empty.</summary>
    public float Max { get; private set; }

    /// <summary>
    /// Records a sample taken at <paramref name="nowTicks"/> (a
    /// <see cref="Stopwatch.GetTimestamp"/> value) and refreshes <see cref="Max"/>.
    /// </summary>
    public void Add(float value, long nowTicks)
    {
        // Any tail sample that is no larger than this one can never be the max
        // again, so drop them — this keeps the deque monotonically decreasing.
        while (_samples.Last is { } last && last.Value.Value <= value)
            _samples.RemoveLast();

        _samples.AddLast(new Sample(nowTicks, value));

        // Evict samples that have aged out of the window. The head is always
        // the current maximum because of the monotonic invariant above.
        long cutoff = nowTicks - _windowTicks;
        while (_samples.First is { } first && first.Value.Timestamp < cutoff)
            _samples.RemoveFirst();

        Max = _samples.First is { } head ? head.Value.Value : 0f;
    }
}
