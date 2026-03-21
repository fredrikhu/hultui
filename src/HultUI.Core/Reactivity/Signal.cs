using System.Collections.Generic;

namespace HultUI.Core.Reactivity;

/// <summary>
/// A mutable reactive value. Reading inside a Computed or Effect tracks the dependency.
/// Setting a new value notifies all subscribers.
/// </summary>
public sealed class Signal<T> : IReactiveSource
{
    private T _value;
    private readonly IEqualityComparer<T> _comparer;
    private HashSet<IReactiveNode>? _subscribers;
    private int _version;

    public Signal(T initialValue, IEqualityComparer<T>? comparer = null)
    {
        _value = initialValue;
        _comparer = comparer ?? EqualityComparer<T>.Default;
    }

    /// <summary>
    /// Gets or sets the value. Getting tracks the read in the current reactive scope.
    /// Setting notifies subscribers if the value changed.
    /// </summary>
    public T Value
    {
        get
        {
            ReactiveScope.TrackRead(this);
            return _value;
        }
        set
        {
            if (_comparer.Equals(_value, value))
                return;

            _value = value;
            _version++;
            NotifySubscribers();
        }
    }

    /// <summary>
    /// Read the current value without tracking the dependency.
    /// </summary>
    public T Peek() => _value;

    int IReactiveSource.Version => _version;

    void IReactiveSource.Refresh() { } // Signals are always up-to-date

    private void NotifySubscribers()
    {
        if (_subscribers is null) return;

        // Snapshot to avoid mutation during iteration
        var snapshot = new List<IReactiveNode>(_subscribers);
        foreach (var subscriber in snapshot)
            subscriber.NotifyDirty();
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

    public override string ToString() => $"Signal({_value})";
}
