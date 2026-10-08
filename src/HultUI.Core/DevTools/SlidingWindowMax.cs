using System.Diagnostics;

namespace HultUI.Core.DevTools;

/// <summary>
/// Tracks the maximum value observed within a trailing time window. Backed by
/// a monotonic (decreasing) deque so both <see cref="Add"/> and reading
/// <see cref="Max"/> are O(1) amortized regardless of how many samples fall in
/// the window. Used by the performance HUD to report the slowest recent pass
/// for each stat.
///
/// <para>The deque is a ring over an array, not a linked list. Every window keeps
/// eight of these and feeds each one sample per frame whether or not the HUD is
/// shown, so a node per sample was eight allocations a frame (~0.4 KB) on a frame
/// that otherwise allocates nothing. The array only grows, when the window holds
/// more samples than it has room for (a long, steadily falling run of values).</para>
/// </summary>
internal sealed class SlidingWindowMax
{
    private readonly struct Sample(long timestamp, float value)
    {
        public readonly long Timestamp = timestamp;
        public readonly float Value = value;
    }

    private readonly long _windowTicks;
    private Sample[] _ring = new Sample[16];   // power of two, so wrapping is a mask
    private int _head;                         // index of the oldest sample (the max)
    private int _count;

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
        int mask = _ring.Length - 1;

        // Any tail sample that is no larger than this one can never be the max
        // again, so drop them — this keeps the deque monotonically decreasing.
        while (_count > 0 && _ring[(_head + _count - 1) & mask].Value <= value)
            _count--;

        if (_count == _ring.Length)
        {
            Grow();
            mask = _ring.Length - 1;
        }
        _ring[(_head + _count) & mask] = new Sample(nowTicks, value);
        _count++;

        // Evict samples that have aged out of the window. The head is always
        // the current maximum because of the monotonic invariant above.
        long cutoff = nowTicks - _windowTicks;
        while (_count > 0 && _ring[_head].Timestamp < cutoff)
        {
            _head = (_head + 1) & mask;
            _count--;
        }

        Max = _count > 0 ? _ring[_head].Value : 0f;
    }

    private void Grow()
    {
        var bigger = new Sample[_ring.Length * 2];
        int mask = _ring.Length - 1;
        for (int i = 0; i < _count; i++)
            bigger[i] = _ring[(_head + i) & mask];
        _ring = bigger;
        _head = 0;
    }
}
