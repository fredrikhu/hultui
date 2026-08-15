using System.Runtime.CompilerServices;

namespace HultUI.Core.Threading;

/// <summary>
/// The single thread that owns the element tree and the reactive graph.
///
/// <para>HultUI has no locks in its hot paths — that is a deliberate consequence of the
/// single-UI-thread model (see <c>docs/DECISIONS.md</c>). The failure mode that model
/// invites is a background thread writing a <c>[Reactive]</c> property or mutating an
/// element: the write does not throw, it corrupts the reactive graph or the dirty-flag
/// walk somewhere later, and the symptom surfaces frames away from the cause. The
/// classic shape is <c>await Task.Run(…)</c> followed by touching state, where the
/// continuation never came back to the UI thread.</para>
///
/// <para>This class converts that class of heisenbug into an immediate exception naming
/// the offending operation and both threads. <see cref="Bind"/> is called by
/// <c>Application</c>'s constructor and <see cref="Unbind"/> by its disposal, so code
/// that never starts an application (pure layout/reactivity unit tests) is never
/// checked — there is no UI thread to violate.</para>
/// </summary>
public static class UIThread
{
    // 0 = unbound. Read on every guarded mutation, so it stays a plain int:
    // the only writers are Bind/Unbind on the UI thread itself, and a stale
    // read on another thread can only mean "not bound yet", which is safe.
    private static int _uiThreadId;

    private static bool _enforce =
#if DEBUG
        true;
#else
        false;
#endif

    /// <summary>
    /// Whether a cross-thread mutation throws. Defaults to <c>true</c> in Debug builds of
    /// HultUI.Core and <c>false</c> in Release. Turning it on in a Release diagnostic
    /// build is supported and costs a thread-local read per guarded call — measured at
    /// ~0.44ns, below the noise floor of the framework benchmark.
    /// </summary>
    public static bool Enforce
    {
        get => _enforce;
        set => _enforce = value;
    }

    /// <summary>The managed thread id of the UI thread, or <c>null</c> when unbound.</summary>
    public static int? BoundThreadId => _uiThreadId == 0 ? null : _uiThreadId;

    /// <summary>True when there is a UI thread and this is it (also true when unbound).</summary>
    public static bool IsCurrent
    {
        get
        {
            int bound = _uiThreadId;
            return bound == 0 || bound == Environment.CurrentManagedThreadId;
        }
    }

    /// <summary>
    /// Claims the calling thread as the UI thread. Called by <c>Application</c>'s
    /// constructor; apps hosting the element tree themselves (a custom pump) call it
    /// before building any UI.
    /// </summary>
    public static void Bind() => _uiThreadId = Environment.CurrentManagedThreadId;

    /// <summary>
    /// Releases the UI-thread claim, returning the guard to its inert state. Called when
    /// the application is disposed, so a test host that runs the next session on a
    /// different pool thread does not inherit a stale binding.
    /// </summary>
    public static void Unbind() => _uiThreadId = 0;

    /// <summary>
    /// Throws when called from a thread other than the bound UI thread. No-op while
    /// unbound. <paramref name="operation"/> names what was being mutated and appears in
    /// the message, so keep it specific (<c>"Signal&lt;T&gt;.Value"</c>, not <c>"write"</c>).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void VerifyAccess(string operation)
    {
        // Enforce is tested first on purpose. It gates the thread-local read, which is
        // effectively the whole cost of the check (0.65ns enforcing vs 0.18ns not), so a
        // Release build with enforcement off pays one static bool read and nothing else.
        // Bind still records the id, so switching Enforce on at runtime works.
        if (!_enforce)
            return;
        int bound = _uiThreadId;
        if (bound == 0 || bound == Environment.CurrentManagedThreadId)
            return;
        ThrowCrossThread(operation, bound);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ThrowCrossThread(string operation, int uiThreadId)
    {
        var current = Thread.CurrentThread;
        string origin = current.IsThreadPoolThread
            ? "a thread-pool thread"
            : string.IsNullOrEmpty(current.Name) ? "a background thread" : $"thread '{current.Name}'";

        throw new InvalidOperationException(
            $"{operation} was called from {origin} (managed thread {current.ManagedThreadId}); " +
            $"HultUI owns the element tree and the reactive graph on the UI thread " +
            $"(managed thread {uiThreadId}) and takes no locks, so this would corrupt state " +
            $"silently rather than fail here. " +
            $"Marshal the mutation back with Application.InvokeOnMainThread(...), or drop the " +
            $"ConfigureAwait(false) so the continuation resumes on the UI thread. " +
            $"Set UIThread.Enforce = false to disable this check.");
    }
}
