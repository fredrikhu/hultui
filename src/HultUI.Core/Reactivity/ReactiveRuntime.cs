using HultUI.Core.Threading;

namespace HultUI.Core.Reactivity;

/// <summary>
/// Manages batching and scheduling of reactive effects.
/// Multiple signal changes within a batch coalesce into a single flush.
/// </summary>
public static class ReactiveRuntime
{
    private static readonly Queue<Effect> _pendingEffects = new();
    private static bool _isFlushing;
    private static int _batchDepth;
    private static int _suspendDepth;

    private const int MaxIterations = 1000;

    /// <summary>
    /// When true (the default), the framework wraps each event dispatch in a
    /// batch so multiple signal writes in a handler coalesce into a single
    /// rebuild. Set to false to restore immediate, per-write flushing globally
    /// — a safety valve for code that relies on a rebuild being visible partway
    /// through a handler. Prefer <see cref="FlushNow"/> or
    /// <see cref="SuspendBatching"/> for narrow, local opt-outs.
    /// </summary>
    public static bool AutoBatch { get; set; } = true;

    /// <summary>
    /// Runs the given action inside a batch. Signal changes within the batch
    /// will not trigger effects until the outermost batch completes.
    /// </summary>
    public static void Batch(Action action)
    {
        BeginBatch();
        try
        {
            action();
        }
        finally
        {
            EndBatch();
        }
    }

    /// <summary>
    /// Opens a batch scope. Must be paired with <see cref="EndBatch"/> (use
    /// try/finally). Prefer <see cref="Batch"/> unless you need to avoid a
    /// closure allocation on a hot path (e.g. per-event dispatch).
    /// </summary>
    public static void BeginBatch() => _batchDepth++;

    /// <summary>
    /// Closes a batch scope opened with <see cref="BeginBatch"/>. When the
    /// outermost batch closes, pending effects are flushed.
    /// </summary>
    public static void EndBatch()
    {
        _batchDepth--;
        if (_batchDepth == 0)
            Flush();
    }

    /// <summary>
    /// Forces any pending effects to run immediately, even inside a batch. Use
    /// as an escape hatch when a handler writes a signal and then needs the
    /// resulting rebuild to be applied before it reads element state (e.g. to
    /// focus or measure a just-created element). No-op if a flush is already in
    /// progress.
    /// </summary>
    public static void FlushNow() => Flush();

    /// <summary>
    /// Suspends batching within the returned scope: signal changes flush
    /// immediately even if an enclosing batch is open. Dispose to restore
    /// batching. Use for a handler that wants the pre-auto-batch synchronous
    /// behaviour wholesale.
    /// </summary>
    public static SuspendScope SuspendBatching()
    {
        _suspendDepth++;
        return default;
    }

    /// <summary>Disposable scope returned by <see cref="SuspendBatching"/>.</summary>
    public readonly struct SuspendScope : IDisposable
    {
        public void Dispose()
        {
            if (_suspendDepth > 0)
                _suspendDepth--;
        }
    }

    /// <summary>
    /// Schedules an effect for re-execution. Flushes immediately unless a batch
    /// is open (and batching hasn't been suspended).
    /// </summary>
    internal static void ScheduleEffect(Effect effect)
    {
        // The queue and the flush flags below are plain statics, and a flush runs
        // component Build() methods against the live tree. Reaching here off-thread
        // means someone found a path around the Signal setter's guard (a Computed, a
        // ReactiveList, a hand-scheduled Effect) — catch it at the shared choke point.
        UIThread.VerifyAccess("ReactiveRuntime.ScheduleEffect");

        _pendingEffects.Enqueue(effect);

        if (!_isFlushing && (_batchDepth == 0 || _suspendDepth > 0))
            Flush();
    }

    /// <summary>
    /// Executes all pending effects. Effects may trigger further effects,
    /// so this loops until the queue is drained or the safety limit is hit.
    /// </summary>
    private static void Flush()
    {
        if (_isFlushing) return;
        _isFlushing = true;

        try
        {
            int iterations = 0;
            while (_pendingEffects.Count > 0)
            {
                if (++iterations > MaxIterations)
                {
                    _pendingEffects.Clear();
                    throw new InvalidOperationException(
                        $"Reactive effect loop exceeded {MaxIterations} iterations. " +
                        "This likely indicates a circular dependency between effects.");
                }

                var effect = _pendingEffects.Dequeue();
                effect.Execute();
            }
        }
        finally
        {
            _isFlushing = false;
        }
    }

    /// <summary>
    /// Returns true if there are pending effects waiting to be flushed.
    /// </summary>
    public static bool HasPendingEffects => _pendingEffects.Count > 0;
}
