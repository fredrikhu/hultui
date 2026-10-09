namespace HultUI.Testing;

/// <summary>
/// What a piece of work allocates on the calling thread once it has settled: the measure every
/// "allocates nothing" test uses (plan 17). Linked into each test project that has one.
///
/// <para>A test counts <see cref="GC.GetAllocatedBytesForCurrentThread"/> over a round of work,
/// after a warm-up. One round is not enough. Some allocations happen once, on whichever frame
/// they fall on, and say nothing about the frames after it. Two were found (plan 17, 7b):
/// <list type="bullet">
/// <item>A buffer that grows to the most it will hold: the DevTools peak trackers did, 536 B a
/// time, until they got a fixed ring. That was the Headless flake (5.4 B a frame).</item>
/// <item>Bytes counted with no object behind them. The runtime hands a thread memory in pieces
/// of about 8 KB. When the GC suspends the process without collecting (a background GC does
/// this while it runs), it can retire a thread's piece: the unused rest becomes a free object,
/// and <see cref="GC.GetAllocatedBytesForCurrentThread"/> counts that rest as allocated. Seen
/// as 0.7 to 7.7 KB once in a frame of the Skia tests, whose classes run in parallel and keep
/// background GCs going: GC pause time grew in that frame, no collection count did, and a heap
/// walk between two marker arrays found one free object exactly 24 B larger than the count,
/// then other threads' objects. That was the Skia flake (22.9 and 78.9 B a frame). No code can
/// avoid it.</item>
/// </list>
/// Which round they fall in depends on timing: how far the JIT has got, how loaded the machine
/// is. A round that caught one failed the test now and then, most often on the first run after
/// a build.</para>
///
/// <para><see cref="Least"/> runs the round up to <see cref="MaxRounds"/> times and gives the
/// least that one round allocated, stopping at the first round that meets the goal. A one-off
/// costs at most one round. Something allocated on every frame, every call or every pass is
/// allocated in every round, so the least is still at least one of it and the test still
/// fails.</para>
///
/// <para>The projects that link this turn dynamic PGO off (<c>TieredPGO</c> false). More rounds
/// give the JIT time to reach tier 1, and with a profile tier-1 code can guess the type behind an
/// interface and put a boxed enumerator or a closure on the stack: a <c>foreach</c> over
/// <c>Children</c> in a scroll view's extent, 40 B a frame, passed two runs of three with PGO
/// and failed every run without it. NativeAOT has no profile and tier 0 has no stack
/// allocation, so the code allocates there; the tests are about the code, not the profile.</para>
/// </summary>
internal static class Allocations
{
    /// <summary>The most rounds <see cref="Least"/> measures before it gives up and returns the
    /// least it saw.</summary>
    public const int MaxRounds = 8;

    /// <summary>
    /// Bytes this thread allocates in one run of <paramref name="round"/>: the least of up to
    /// <see cref="MaxRounds"/> runs one after another, stopping at the first that allocates no more
    /// than <paramref name="goal"/>. The round runs at least once, and is the same work each time:
    /// keep what it needs (and any counter it advances) outside it.
    /// </summary>
    public static long Least(Action round, long goal = 0)
    {
        long least = long.MaxValue;
        for (int i = 0; i < MaxRounds && least > goal; i++)
        {
            long before = GC.GetAllocatedBytesForCurrentThread();
            round();
            least = Math.Min(least, GC.GetAllocatedBytesForCurrentThread() - before);
        }
        return least;
    }

    /// <summary>Bytes per call of <paramref name="step"/> over <paramref name="calls"/> calls in a
    /// row, from the least round as <see cref="Least"/> measures it; a round that averages no more
    /// than <paramref name="goal"/> a call ends it.</summary>
    public static double PerCall(int calls, Action step, double goal = 0) =>
        Least(() => { for (int i = 0; i < calls; i++) step(); }, (long)(goal * calls)) / (double)calls;
}
