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
    // A note on the snapshot in NotifySubscribers, and a correction.
    //
    // A first attempt at this storage notified the single subscriber straight off the
    // field (`_single?.NotifyDirty()`), and Junction's
    // FindRevealDeliveryTests.ARevealThatFallsDueBeforeTheBodyExistsIsStillHonoured failed
    // against it — reproducibly at the time, three runs, each a hard 30s PumpUntil timeout
    // with the file's text never arriving. Reverting this file fixed it; reverting only the
    // event-dispatch change did not. That was recorded here as "the snapshot is
    // load-bearing".
    //
    // That conclusion does not hold. Re-tested afterwards with everything else identical,
    // the direct-notify shape passes 13/13 in isolation — 3 runs, then 10 more on an idle
    // machine with the test project pre-built so no compilation competed with it. The
    // original failures all landed while heavy builds were running concurrently, and that
    // test waits on a real background file read behind a 30s PumpUntil, so load is the
    // likeliest explanation. Whatever it was, it was not this line. The bookkeeping was independently cleared at the time: a
    // shadow set maintained by the same Subscribe/Unsubscribe calls agreed with the fast
    // path at every single write, and no write ever lost a subscriber.
    //
    // The snapshot is kept anyway, and the reason is that dropping it buys nothing: the
    // rented buffer allocates zero bytes, so a single-subscriber write costs the same
    // either way, and only the rent/return call is saved. No measurable upside against an
    // unexplained failure is not a trade worth taking. The original failure remains
    // unexplained and is not currently reproducible. If it returns, the sequence worth tracing is a source first read
    // during a rebuild, where a freshly created per-key AsyncLoader subscribes mid-flush —
    // see ComponentHost.ForceRebuild's comment.
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
