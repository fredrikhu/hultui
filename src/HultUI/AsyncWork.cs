namespace HultUI;

/// <summary>
/// The hop <see cref="AsyncLoader{T}"/> producers take off the UI thread. In a running
/// app the work goes straight to the thread pool. A headless session installs a deferred
/// queue instead and drains it at a fixed point in its frame pump, so a load still lands
/// on a later frame — the ordering apps depend on — but at a deterministic one, immune
/// to machine load. Running the producer inline was tried first and broke Junction's
/// reveal flow, which requires the value to arrive on a frame after the Build that
/// requested it.
///
/// <para>Work comes in two halves: the part that runs off the UI thread, which returns the
/// part that applies its result on the UI thread. Kept apart, a headless session can run
/// every producer a frame asked for at once — as the pool would — and still apply the
/// results in the order they were asked for, not the order they happened to finish.</para>
/// </summary>
internal static class AsyncWork
{
    // Written only by the headless session (its ctor and Dispose, both on the test
    // thread) and read on the UI thread in Load, which the session's Application has
    // bound to that same thread — so no volatile, no lock.
    private static Action<Func<Action?>>? _scheduler;

    /// <summary>
    /// Runs <paramref name="work"/> off the calling code path — thread pool by default, or
    /// through the installed scheduler — then posts the action it returns, if any, to the
    /// UI thread of the application that scheduled it (<see cref="Application.Post"/>): with none,
    /// or one that has ended by then, the result is dropped, not handed to its successor.
    /// </summary>
    internal static void Schedule(Func<Action?> work)
    {
        var scheduler = _scheduler;
        if (scheduler is null)
        {
            var app = Application.Current;
            _ = Task.Run(() =>
            {
                if (work() is { } apply)
                    app?.Post(apply);
            });
        }
        else
            scheduler(work);
    }

    /// <summary>Replaces the thread-pool hop; null restores it. The scheduler takes over
    /// posting each work item's result to the UI thread.</summary>
    internal static void SetScheduler(Action<Func<Action?>>? scheduler) => _scheduler = scheduler;
}
