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

// Subscribers are 0/1/many: an inline field for the single subscriber almost every
    // signal has — the effect wrapping the component that reads it — promoting to a set
    // only when a second arrives. An app with hundreds of [Reactive] properties was paying
    // a 176-byte HashSet per property to hold one entry.
    //
    // The snapshot in NotifySubscribers is load-bearing, and not only for the many case.
    // A first attempt notified the single subscriber straight off the field —
    // `_single?.NotifyDirty()` — and that regressed Junction's
    // FindRevealDeliveryTests.ARevealThatFallsDueBeforeTheBodyExistsIsStillHonoured: a find
    // reveal queued before its TextArea exists stopped being delivered, because the file's
    // text never arrived at all. Established by controlled variants, all differing only in
    // this file:
    //
    //   always-HashSet + pooled snapshot ............. passes
    //   single-slot   + direct notify ................ FAILS
    //   single-slot   + snapshot on both paths ....... passes   ← what is here
    //
    // Ruled out along the way: the bookkeeping (a shadow set maintained by the same
    // Subscribe/Unsubscribe calls agreed with the fast path at every single write, and no
    // write ever lost a subscriber), the pool itself (the shipped always-HashSet version
    // used it and passed), and flakiness (5/5 either way, and the failure was a hard 30s
    // PumpUntil timeout rather than a race).
    //
    // What is still not explained is WHY: reading the field into a snapshot and reading it
    // for a direct call both read it exactly once, so the two should be equivalent. The
    // suspect is the path ComponentHost.ForceRebuild's comment describes — a source first
    // read during a rebuild, where a freshly created per-key AsyncLoader subscribes
    // mid-flush. Do not "simplify" the single-subscriber path back to a direct call
    // without running that Junction test.
    private IReactiveNode? _single;
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
        // Snapshot before notifying, on both paths. A subscriber can subscribe or
        // unsubscribe while being notified, and — see the note on the fields — notifying
        // the single subscriber straight off the field instead of through a snapshot
        // regressed Junction's FindRevealDeliveryTests. Rented rather than allocated,
        // because this is the write path and every [Reactive] assignment runs it.
        int count = _subscribers?.Count ?? (_single is null ? 0 : 1);
        if (count == 0) return;

        var snapshot = ArrayPool<IReactiveNode>.Shared.Rent(count);
        try
        {
            if (_subscribers is null) snapshot[0] = _single!;
            else _subscribers.CopyTo(snapshot, 0);

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
        if (_subscribers is not null) { _subscribers.Add(subscriber); return; }
        if (_single is null) { _single = subscriber; return; }
        if (ReferenceEquals(_single, subscriber)) return;

        // Second subscriber: promote to the set and stop using the single slot.
        _subscribers = [_single, subscriber];
        _single = null;
    }

    void IReactiveSource.Unsubscribe(IReactiveNode subscriber)
    {
        if (_subscribers is not null) { _subscribers.Remove(subscriber); return; }
        if (ReferenceEquals(_single, subscriber)) _single = null;
    }

    public override string ToString() => $"Signal({_value})";
}
