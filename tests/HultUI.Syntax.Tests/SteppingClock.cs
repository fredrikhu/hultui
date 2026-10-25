namespace HultUI.Syntax.Tests;

/// <summary>A clock that moves <c>step</c> on every reading. <see cref="Standing"/> never moves,
/// so no tokenize budget runs out on it: a test that compares two tokenizes of one text gets
/// the same colour from both however busy the machine is. On the system clock a CI runner cut
/// one of eight concurrent tokenizes short, 1,395 spans against 1,400.</summary>
internal sealed class SteppingClock(TimeSpan step) : TimeProvider
{
    public static readonly SteppingClock Standing = new(TimeSpan.Zero);

    private long _now;
    public override long TimestampFrequency => TimeSpan.TicksPerSecond;
    public override long GetTimestamp() => Interlocked.Add(ref _now, step.Ticks);
}
