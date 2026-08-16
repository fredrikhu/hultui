namespace HultUI;

/// <summary>
/// The hop <see cref="AsyncLoader{T}"/> producers take off the UI thread. In a running
/// app the work goes straight to the thread pool. A headless session installs a deferred
/// queue instead and drains it at a fixed point in its frame pump, so a load still lands
/// on a later frame — the ordering apps depend on — but at a deterministic one, immune
/// to machine load. Running the producer inline was tried first and broke Junction's
/// reveal flow, which requires the value to arrive on a frame after the Build that
/// requested it.
/// </summary>
internal static class AsyncWork
{
    // Written only by the headless session (its ctor and Dispose, both on the test
    // thread) and read on the UI thread in Load, which the session's Application has
    // bound to that same thread — so no volatile, no lock.
    private static Action<Action>? _scheduler;

    /// <summary>
    /// Runs <paramref name="work"/> off the calling code path — thread pool by
    /// default, or through the installed scheduler.
    /// </summary>
    internal static void Schedule(Action work)
    {
        var scheduler = _scheduler;
        if (scheduler is null)
            _ = Task.Run(work);
        else
            scheduler(work);
    }

    /// <summary>Replaces the thread-pool hop; null restores it.</summary>
    internal static void SetScheduler(Action<Action>? scheduler) => _scheduler = scheduler;
}
