using HultUI.Headless;

namespace Meridian.Tests.Fixtures;

/// <summary>
/// A fixed local time of day for tests that put sends in History. History reads "today" from the
/// app's clock, and a test that wrote its sends from <see cref="DateTimeOffset.Now"/> failed when
/// it ran just after midnight local: a send from a minute ago was yesterday's (meridian-v1.8.0's
/// installer run, at 00:00:41 UTC). These write their sends from <see cref="Noon"/> and set the
/// session's clock to it, so every send from the last twelve hours is today's.
/// </summary>
internal static class HistoryClock
{
    /// <summary>Noon, local time, on a fixed day.</summary>
    public static readonly DateTimeOffset Noon = At(12, 0, 0);

    /// <summary>A local time of day on the same fixed day as <see cref="Noon"/>.</summary>
    public static DateTimeOffset At(int hour, int minute, int second)
    {
        // The day after the headless clock's epoch (2020-01-01 00:00 UTC), which it only moves
        // forward from: any local time on it is at or after the epoch in every time zone.
        var local = new DateTime(2020, 1, 2, hour, minute, second, DateTimeKind.Unspecified);
        return new DateTimeOffset(local, TimeZoneInfo.Local.GetUtcOffset(local));
    }

    /// <summary>Sets the session's clock to <paramref name="at"/>, before the app reads it.</summary>
    public static void Set(HeadlessSession session, DateTimeOffset at)
    {
        var delta = at - session.Time.GetUtcNow();
        if (delta < TimeSpan.Zero)
            throw new InvalidOperationException($"the session's clock is already past {at:O}");
        session.AdvanceTime(delta);
    }
}
