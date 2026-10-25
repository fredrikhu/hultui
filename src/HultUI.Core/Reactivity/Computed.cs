using System.Buffers;
using System.Collections.Generic;

namespace HultUI.Core.Reactivity;

/// <summary>
/// A derived reactive value that lazily recomputes when its dependencies change.
/// Acts as both a subscriber (to its inputs) and a source (to its consumers).
/// </summary>
public sealed class Computed<T> : IReactiveSource, IReactiveNode, IDependencyTracker
{
    private readonly Func<T> _compute;
    private readonly IEqualityComparer<T> _comparer;
    private T _value;
    private bool _dirty = true;
    private int _version;
    private HashSet<IReactiveNode>? _subscribers;
    private List<IReactiveSource>? _dependencies;

    public Computed(Func<T> compute, IEqualityComparer<T>? comparer = null)
    {
        _compute = compute;
        _comparer = comparer ?? EqualityComparer<T>.Default;
        _value = default!;
    }

    /// <summary>
    /// Gets the computed value. Recomputes if dirty. Tracks the read in the current scope.
    /// </summary>
    public T Value
    {
        get
        {
            ReactiveScope.TrackRead(this);
            if (_dirty)
                Recompute();
            return _value;
        }
    }

    /// <summary>
    /// Read the current cached value without tracking or recomputing.
    /// </summary>
    public T Peek() => _value;

    int IReactiveSource.Version => _version;

    void IReactiveSource.Refresh()
    {
        if (_dirty)
            Recompute();
    }

    private void Recompute()
    {
        // Unsubscribe from old dependencies
        if (_dependencies is not null)
        {
            foreach (var dep in _dependencies)
                dep.Unsubscribe(this);
            _dependencies.Clear();
        }

        // Track new dependencies by setting ourselves as the current subscriber
        var previous = ReactiveScope.SetSubscriber(this);
        try
        {
            var newValue = _compute();
            _dirty = false;

            if (!_comparer.Equals(_value, newValue))
            {
                _value = newValue;
                _version++;
                NotifySubscribers();
            }
        }
        finally
        {
            ReactiveScope.SetSubscriber(previous);
        }
    }

    void IReactiveNode.NotifyDirty()
    {
        if (_dirty) return; // Already dirty, avoid redundant propagation
        _dirty = true;
        NotifySubscribers();
    }

    private void NotifySubscribers()
    {
        // Snapshot before notifying: a subscriber can unsubscribe, subscribe or be disposed while
        // it is told, and an effect that runs at once reads this computed again, recomputes it
        // and notifies from inside the walk that told it — the inner notify rents a buffer of its
        // own. Rented, as Signal's is: a write upstream comes through here, twice when the value
        // changes (the dirty mark, then the new value), and a List copy cost 64 bytes each time.
        int count = _subscribers?.Count ?? 0;
        if (count == 0) return;

        var snapshot = ArrayPool<IReactiveNode>.Shared.Rent(count);
        try
        {
            _subscribers!.CopyTo(snapshot, 0);
            for (int i = 0; i < count; i++)
                snapshot[i].NotifyDirty();
        }
        finally
        {
            // Cleared on return: a pooled buffer holding the last notified nodes would keep
            // whole component trees alive until the buffer was next rented.
            ArrayPool<IReactiveNode>.Shared.Return(snapshot, clearArray: true);
        }
    }

    void IReactiveSource.Subscribe(IReactiveNode subscriber)
    {
        _subscribers ??= [];
        _subscribers.Add(subscriber);
    }

    void IReactiveSource.Unsubscribe(IReactiveNode subscriber)
    {
        _subscribers?.Remove(subscriber);
    }

    void IDependencyTracker.AddDependency(IReactiveSource source)
    {
        _dependencies ??= [];
        _dependencies.Add(source);
    }

    public override string ToString() => $"Computed({(_dirty ? "dirty" : _value?.ToString() ?? "null")})";
}
