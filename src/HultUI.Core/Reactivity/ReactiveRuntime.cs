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

    private const int MaxIterations = 1000;

    /// <summary>
    /// Runs the given action inside a batch. Signal changes within the batch
    /// will not trigger effects until the outermost batch completes.
    /// </summary>
    public static void Batch(Action action)
    {
        _batchDepth++;
        try
        {
            action();
        }
        finally
        {
            _batchDepth--;
            if (_batchDepth == 0)
                Flush();
        }
    }

    /// <summary>
    /// Schedules an effect for re-execution. If not inside a batch, flushes immediately.
    /// </summary>
    internal static void ScheduleEffect(Effect effect)
    {
        _pendingEffects.Enqueue(effect);

        if (_batchDepth == 0 && !_isFlushing)
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
