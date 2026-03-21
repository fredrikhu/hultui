namespace HultUI.Core.Reactivity;

/// <summary>
/// A node in the reactive dependency graph. Can be a Signal, Computed, or Effect.
/// </summary>
internal interface IReactiveNode
{
    /// <summary>
    /// Called when a dependency has changed. Marks this node as dirty and
    /// propagates the notification to its own subscribers.
    /// </summary>
    void NotifyDirty();
}

/// <summary>
/// A reactive node that can be subscribed to (Signal or Computed).
/// </summary>
internal interface IReactiveSource
{
    void Subscribe(IReactiveNode subscriber);
    void Unsubscribe(IReactiveNode subscriber);

    /// <summary>
    /// Version counter that increments each time the value actually changes.
    /// Used by Effects to detect whether a re-run is necessary.
    /// </summary>
    int Version { get; }

    /// <summary>
    /// Ensures the source's value is up-to-date (forces recompute for dirty Computeds).
    /// </summary>
    void Refresh();
}

/// <summary>
/// A reactive node that tracks which sources it depends on, for cleanup on re-evaluation.
/// Implemented by Computed and Effect.
/// </summary>
internal interface IDependencyTracker
{
    void AddDependency(IReactiveSource source);
}
