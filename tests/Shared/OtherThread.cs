using System.Runtime.ExceptionServices;

namespace HultUI.Testing;

/// <summary>
/// Runs code on a thread of its own and waits for it: what a test means by "another thread"
/// (an assistive-technology bridge, a worker, anything that is not the UI thread).
///
/// <para><c>Task.Run(work).Result</c> does not promise that. When the waiting thread is a
/// thread-pool thread, the wait can pop the queued task off that thread's own queue and run it
/// inline, on the waiting thread itself. xunit.runner.visualstudio 2.x ran test bodies on
/// dedicated threads, where that cannot happen; 4.x runs them on pool threads, where it does
/// in about two runs of three (docs/plans/18-major-dependency-upgrades.md, item 3). A test of
/// cross-thread behaviour then quietly tests the same thread, and fails or passes by luck.</para>
/// </summary>
internal static class OtherThread
{
    public static T Run<T>(Func<T> work)
    {
        T result = default!;
        ExceptionDispatchInfo? failure = null;
        var thread = new Thread(() =>
        {
            try { result = work(); }
            catch (Exception ex) { failure = ExceptionDispatchInfo.Capture(ex); }
        })
        {
            IsBackground = true,
            Name = "Test: other thread",
        };
        thread.Start();
        thread.Join();
        failure?.Throw();
        return result;
    }

    public static void Run(Action work) => Run(() =>
    {
        work();
        return 0;
    });
}
