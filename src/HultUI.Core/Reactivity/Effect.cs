namespace HultUI.Core.Reactivity;

/// <summary>
/// A side effect that automatically re-runs when its dependencies change.
/// The effect function is executed immediately on creation, then re-runs
/// whenever any Signal or Computed it read during execution changes.
/// </summary>
public sealed class Effect : IReactiveNode, IDependencyTracker, IDisposable
{
    private readonly Action _run;
    private List<IReactiveSource>? _dependencies;
    private List<int>? _depVersions;
    private bool _dirty;
    private bool _disposed;
    private bool _executing;
    private bool _firstRun = true;

    public Effect(Action run)
    {
        _run = run;
        Execute();
    }

    /// <summary>
    /// Runs the effect, tracking dependencies. Called on creation and when re-scheduled.
    /// </summary>
    internal void Execute() => Execute(force: false);

    /// <summary>
    /// Runs the effect, re-tracking its dependencies.
    /// </summary>
    /// <param name="force">
    /// Skips the "did any dependency actually change?" check. Used when something
    /// <b>outside</b> the reactive graph invalidated the effect — e.g. a component's
    /// <c>MarkDirty()</c> after non-reactive state changed. No dependency version has
    /// moved, but the effect must still re-run <i>and</i> re-collect its dependencies,
    /// so that sources first read during this run are subscribed to.
    /// </param>
    internal void Execute(bool force)
    {
        if (_disposed) return;

        // A forced run re-entered from inside the effect body (MarkDirty called during
        // Build) would recurse; ignore it, matching NotifyDirty's behaviour while executing.
        if (force && _executing) return;

        // On subsequent runs, check if any dependency actually changed.
        // Force dirty Computeds to refresh, then compare versions.
        if (!force && !_firstRun && _dependencies is not null && _depVersions is not null)
        {
            bool anyChanged = false;
            for (int i = 0; i < _dependencies.Count; i++)
            {
                var dep = _dependencies[i];
                dep.Refresh(); // Forces Computed recompute if dirty
                if (dep.Version != _depVersions[i])
                {
                    anyChanged = true;
                    break;
                }
            }
            if (!anyChanged)
            {
                _dirty = false;
                return; // Skip: no dependency value actually changed
            }
        }

        _dirty = false;
        _executing = true;
        _firstRun = false;

        // Unsubscribe from old dependencies
        ClearDependencies();

        // Track new dependencies
        var previous = ReactiveScope.SetSubscriber(this);
        try
        {
            _run();
        }
        finally
        {
            _executing = false;
            ReactiveScope.SetSubscriber(previous);

            // Snapshot current dependency versions
            if (_dependencies is not null)
            {
                _depVersions ??= new List<int>(_dependencies.Count);
                _depVersions.Clear();
                foreach (var dep in _dependencies)
                    _depVersions.Add(dep.Version);
            }
        }
    }

    void IReactiveNode.NotifyDirty()
    {
        if (_disposed || _dirty || _executing) return;
        _dirty = true;
        ReactiveRuntime.ScheduleEffect(this);
    }

    void IDependencyTracker.AddDependency(IReactiveSource source)
    {
        _dependencies ??= [];
        _dependencies.Add(source);
    }

    private void ClearDependencies()
    {
        if (_dependencies is null) return;
        foreach (var dep in _dependencies)
            dep.Unsubscribe(this);
        _dependencies.Clear();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        ClearDependencies();
    }
}
