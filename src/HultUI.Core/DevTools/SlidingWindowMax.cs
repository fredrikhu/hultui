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
/// that otherwise allocates nothing.</para>
///
/// <para>The ring never grows. The window is cut into <see cref="Buckets"/> slices of time. A
/// sample smaller than the last one kept, in the same slice, is folded into it: the kept one
/// keeps its value and takes the new time. So the deque holds at most one sample a slice and
/// always fits <see cref="Capacity"/>, however fast samples come. It used to start at 16 samples
/// and double when a falling run outlasted it, 536 B each time. Frame times fall run after run
/// while an app's code is still being compiled, and an unpaced loop puts tens of thousands of
/// samples in a window, so every window allocated on some frame early in its life. That made the
/// "allocates nothing" frame tests fail now and then, most often on the first run after a build
/// (plan 17, round 2).</para>
///
/// <para>Folding never makes <see cref="Max"/> smaller than the largest sample in the window. It
/// can make it larger for less than one slice (the window / 62: 81 ms of 5 s), because the folded
/// value counts until the later sample leaves the window. Samples in different slices are kept
/// exactly.</para>
/// </summary>
internal sealed class SlidingWindowMax
{
    private readonly struct Sample(long timestamp, float value)
    {
        public readonly long Timestamp = timestamp;
        public readonly float Value = value;
    }

    /// <summary>Slices of the window: the deque holds samples from at most this many plus two
    /// (the slices the window's two ends fall in), which is <see cref="Capacity"/>.</summary>
    internal const int Buckets = Capacity - 2;
    internal const int Capacity = 64;              // power of two, so wrapping is a mask

    private readonly long _windowTicks;
    private readonly long _bucketTicks;
    private readonly Sample[] _ring = new Sample[Capacity];
    private int _head;                             // index of the oldest sample (the max)
    private int _count;

    public SlidingWindowMax(TimeSpan window)
    {
        _windowTicks = (long)(window.TotalSeconds * Stopwatch.Frequency);
        // Rounded up, so a window spans no more than Buckets whole slices.
        _bucketTicks = Math.Max(1, (_windowTicks + Buckets - 1) / Buckets);
    }

    /// <summary>The largest value added within the trailing window, or 0 if empty.</summary>
    public float Max { get; private set; }

    /// <summary>
    /// Records a sample taken at <paramref name="nowTicks"/> (a
    /// <see cref="Stopwatch.GetTimestamp"/> value) and refreshes <see cref="Max"/>.
    /// </summary>
    public void Add(float value, long nowTicks)
    {
        const int mask = Capacity - 1;

        // Evict samples that have aged out of the window first, so they take no room. The head
        // is always the current maximum because of the monotonic invariant below.
        long cutoff = nowTicks - _windowTicks;
        while (_count > 0 && _ring[_head].Timestamp < cutoff)
        {
            _head = (_head + 1) & mask;
            _count--;
        }

        // Any tail sample that is no larger than this one can never be the max
        // again, so drop them — this keeps the deque monotonically decreasing.
        while (_count > 0 && _ring[(_head + _count - 1) & mask].Value <= value)
            _count--;

        int tail = (_head + _count - 1) & mask;
        if (_count > 0 && (_count == Capacity
            || _ring[tail].Timestamp / _bucketTicks == nowTicks / _bucketTicks))
        {
            // The tail is larger and in this sample's slice (or, for timestamps that do not
            // move forward, the ring is full): it stands for both until this one ages out.
            _ring[tail] = new Sample(Math.Max(nowTicks, _ring[tail].Timestamp), _ring[tail].Value);
        }
        else
        {
            _ring[(_head + _count) & mask] = new Sample(nowTicks, value);
            _count++;
        }

        Max = _count > 0 ? _ring[_head].Value : 0f;
    }
}
