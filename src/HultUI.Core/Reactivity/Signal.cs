using System.Buffers;
using System.Collections.Generic;
using HultUI.Core.Threading;

namespace HultUI.Core.Reactivity;

/// <summary>
/// A mutable reactive value. Reading inside a Computed or Effect tracks the dependency.
/// Setting a new value notifies all subscribers.
/// </summary>
public sealed class Signal<T> : IReactiveSource
{
    // Built once per closed generic type, not per write: the setter's guard needs a
    // description, and interpolating one on every assignment would put an allocation
    // on the hottest write path in the framework.
    private static readonly string SetterName = $"Signal<{typeof(T).Name}>.Value";

    private T _value;
    private readonly IEqualityComparer<T> _comparer;

    // Allocated on first subscribe, so a signal nobody reads carries no set.
    //
    // A 0/1/many layout — an inline field for the single subscriber almost every signal
    // has, promoting to a set only on the second — was tried and reverted. It regressed
    // Junction's FindRevealDeliveryTests.ARevealThatFallsDueBeforeTheBodyExistsIsStillHonoured,
    // where a reveal queued before its TextArea exists stopped being delivered. Isolated to
    // the single-slot path specifically: the pooled snapshot below passes that test with the
    // set always allocated. Worth another attempt with the ordering worked out, but not
    // worth shipping a subscription bug to save 176 bytes per signal.
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
            // Checked before the equality short-circuit: a cross-thread write is a bug
            // whether or not the value happened to match, and a guard that only fires
            // sometimes is the heisenbug it exists to remove.
            UIThread.VerifyAccess(SetterName);

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

        // A subscriber can subscribe or unsubscribe while being notified, so iterate a copy
        // — rented rather than allocated, because this is the write path and every
        // [Reactive] assignment runs it.
        int count = _subscribers.Count;
        if (count == 0) return;

        var snapshot = ArrayPool<IReactiveNode>.Shared.Rent(count);
        try
        {
            _subscribers.CopyTo(snapshot, 0);
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

    public override string ToString() => $"Signal({_value})";
}
