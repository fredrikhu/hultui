namespace Junction.Tests;

/// <summary>
/// A clock that moves only when a test moves it; its timers fire, on the thread that moves it,
/// as it passes their time. Safe to arm from other threads while it moves: the watcher's
/// file-system events arm its debounce from the thread pool. A timer's callback runs outside the
/// clock's lock, so a callback may arm a timer again.
/// </summary>
internal sealed class ManualClock : TimeProvider
{
    private readonly object _lock = new();
    private readonly List<ManualTimer> _timers = [];
    private long _ticks;

    public override long TimestampFrequency => TimeSpan.TicksPerSecond;

    public override long GetTimestamp()
    {
        lock (_lock) return _ticks;
    }

    public override DateTimeOffset GetUtcNow() =>
        new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero).AddTicks(GetTimestamp());

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        var timer = new ManualTimer(this, callback, state);
        lock (_lock) _timers.Add(timer);
        timer.Change(dueTime, period);
        return timer;
    }

    /// <summary>Moves the clock on and fires every timer due by then, earliest first; one armed
    /// again for a time already passed fires again.</summary>
    public void Advance(TimeSpan by)
    {
        lock (_lock) _ticks += by.Ticks;
        while (true)
        {
            ManualTimer? next = null;
            lock (_lock)
            {
                foreach (var timer in _timers)
                    if (timer.Due is { } due && due <= _ticks && (next?.Due is not { } best || due < best)) next = timer;
                if (next == null) return;
                next.Due = next.Period is { } period ? next.Due + period : null;
            }
            next.Callback(next.State);
        }
    }

    private sealed class ManualTimer(ManualClock clock, TimerCallback callback, object? state) : ITimer
    {
        /// <summary>When it fires next, and how often after that. Guarded by the clock's lock.</summary>
        internal long? Due, Period;
        internal TimerCallback Callback => callback;
        internal object? State => state;

        public bool Change(TimeSpan dueTime, TimeSpan period)
        {
            lock (clock._lock)
            {
                Due = dueTime == Timeout.InfiniteTimeSpan ? null : clock._ticks + dueTime.Ticks;
                Period = period == Timeout.InfiniteTimeSpan || period == TimeSpan.Zero ? null : period.Ticks;
            }
            return true;
        }

        public void Dispose()
        {
            lock (clock._lock) clock._timers.Remove(this);
        }

        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
