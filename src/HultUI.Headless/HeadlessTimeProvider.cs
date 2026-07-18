namespace HultUI.Headless;

/// <summary>
/// A manually advanced clock. Time only moves when the session pumps a frame
/// (or <see cref="HeadlessSession.AdvanceTime"/> is called), so anything that
/// reads the platform clock — double-click detection, most notably — behaves
/// identically on every run regardless of machine speed.
/// </summary>
public sealed class HeadlessTimeProvider : TimeProvider
{
    // An arbitrary fixed epoch: far from DateTime.MinValue so subtraction in
    // consumers can't underflow, and stable so failures reproduce exactly.
    private DateTimeOffset _utcNow = new(2020, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private long _timestamp;

    public override DateTimeOffset GetUtcNow() => _utcNow;

    public override long GetTimestamp() => _timestamp;

    public override long TimestampFrequency => TimeSpan.TicksPerSecond;

    public void Advance(TimeSpan delta)
    {
        if (delta < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(delta), "Virtual time cannot go backwards.");
        _utcNow += delta;
        _timestamp += delta.Ticks;
    }
}
