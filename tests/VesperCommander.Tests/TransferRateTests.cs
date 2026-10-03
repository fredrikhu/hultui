using System.Reflection;
using Xunit;

namespace VesperCommander.Tests;

/// <summary>
/// The rate and ETA a transfer reports, and the paused time that must not count towards them.
/// <para>TransferOp carried PausedTick and PausedTotal, documented as "excludes paused time",
/// from the day the overlay was written — and nothing ever assigned either. The compiler had
/// been saying so (CS0649) the whole time: a run held for a minute went back to reporting the
/// rate it would have had if it had spent that minute copying, which is to say almost none.</para>
/// </summary>
public class TransferRateTests
{
    /// <summary>Pausing and resuming accumulates the time spent waiting. This is the wiring that
    /// was missing: the field existed and stayed zero for the life of every transfer.</summary>
    [Fact]
    public void ResumingAccumulatesTheTimeSpentPaused()
    {
        using var t = new DiffViewTests.Fixture();
        t.Open();
        var op = NewOp();
        SetLong(op, "StartTick", Environment.TickCount64);
        t.SetField("_op", op);

        t.Invoke("ToggleTransferPause");                 // pause
        Assert.True(GetBool(op, "Paused"), "the pause did not take");
        Assert.True(GetLong(op, "PausedTick") > 0, "the moment the pause began was not recorded");

        // Hold long enough that the elapsed pause is unambiguous at millisecond resolution.
        System.Threading.Thread.Sleep(40);
        t.Invoke("ToggleTransferPause");                 // resume

        Assert.False(GetBool(op, "Paused"));
        Assert.True(GetLong(op, "PausedTotal") > 0,
            "resuming did not fold the pause into the total the rate ignores");
    }

    /// <summary>And the rate reads that total. Two runs identical but for the time one of them
    /// spent paused must not report the same throughput — before the fix they did, because
    /// nothing subtracted it.</summary>
    [Fact]
    public void TimeSpentPausedDoesNotCountAgainstTheRate()
    {
        long now = Environment.TickCount64;

        // Ten seconds since it started, two of them actually copying.
        var held = NewOp();
        SetLong(held, "StartTick", now - 10_000);
        SetLong(held, "PausedTotal", 8_000);
        SetLong(held, "TotalBytes", 10_000);
        SetLong(held, "TotalBytesDone", 2_000);

        // The same ten seconds, none of it paused.
        var straight = NewOp();
        SetLong(straight, "StartTick", now - 10_000);
        SetLong(straight, "TotalBytes", 10_000);
        SetLong(straight, "TotalBytesDone", 2_000);

        // The same `now` the runs were set up with: read again, the clock may have moved.
        var heldText = Rate(held, now);
        var straightText = Rate(straight, now);

        Assert.NotEqual("", straightText);
        Assert.NotEqual(straightText, heldText);
        // The held run moved 2000 bytes in the 2 seconds it was actually copying: 1000 B/s, so
        // the 8000 still to go take 8 seconds. Counting the paused 8 seconds as copying time
        // would call the same run 200 B/s and promise 40 seconds — which is what it used to do.
        Assert.Contains("1000 B/s", heldText);
        Assert.Contains("8s left", heldText);
        Assert.Contains("200 B/s", straightText);
        Assert.Contains("40s left", straightText);
    }

    [Fact]
    public void APausedRunSaysSoRatherThanQuotingARate()
    {
        var op = NewOp();
        SetLong(op, "StartTick", Environment.TickCount64 - 5_000);
        SetLong(op, "TotalBytes", 10_000);
        SetLong(op, "TotalBytesDone", 2_000);
        op.GetType().GetField("Paused", BindingFlags.Public | BindingFlags.Instance)!.SetValue(op, true);

        Assert.Equal(" · paused", Rate(op));
    }

    // ── plumbing ─────────────────────────────────────────────────────────
    // TransferOp and TransferRate are both private to CommanderApp; reached reflectively rather
    // than widened for the tests.
    private static readonly Type OpType =
        typeof(CommanderApp).GetNestedType("TransferOp", BindingFlags.NonPublic)!;

    private static object NewOp() => Activator.CreateInstance(OpType, nonPublic: true)!;

    private static void SetLong(object op, string field, long value) =>
        OpType.GetField(field, BindingFlags.Public | BindingFlags.Instance)!.SetValue(op, value);

    private static long GetLong(object op, string field) =>
        (long)OpType.GetField(field, BindingFlags.Public | BindingFlags.Instance)!.GetValue(op)!;

    private static bool GetBool(object op, string field) =>
        (bool)OpType.GetField(field, BindingFlags.Public | BindingFlags.Instance)!.GetValue(op)!;

    /// <param name="now">The tick to compute at; null lets TransferRate read the clock.</param>
    private static string Rate(object op, long? now = null) =>
        (string)typeof(CommanderApp)
            .GetMethod("TransferRate", BindingFlags.NonPublic | BindingFlags.Static)!
            .Invoke(null, [op, now])!;
}
