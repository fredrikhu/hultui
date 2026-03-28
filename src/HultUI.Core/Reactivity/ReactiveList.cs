using System.Collections;

namespace HultUI.Core.Reactivity;

/// <summary>
/// A reactive list that integrates with the Signal/Effect system.
/// Reading (Count, indexer, iteration) tracks dependencies.
/// Mutations (Add, Remove, etc.) notify subscribers, triggering rebuilds.
/// </summary>
public sealed class ReactiveList<T> : IReadOnlyList<T>, IReactiveSource
{
    private readonly List<T> _items;
    private HashSet<IReactiveNode>? _subscribers;
    private int _version;

    public ReactiveList()
    {
        _items = [];
    }

    public ReactiveList(IEnumerable<T> items)
    {
        _items = [..items];
    }

    // ──── Read access (tracks dependencies) ────

    public int Count
    {
        get
        {
            ReactiveScope.TrackRead(this);
            return _items.Count;
        }
    }

    public T this[int index]
    {
        get
        {
            ReactiveScope.TrackRead(this);
            return _items[index];
        }
        set
        {
            _items[index] = value;
            NotifyChanged();
        }
    }

    public IEnumerator<T> GetEnumerator()
    {
        ReactiveScope.TrackRead(this);
        return _items.GetEnumerator();
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    // ──── Mutations (notify subscribers) ────

    public void Add(T item)
    {
        _items.Add(item);
        NotifyChanged();
    }

    public void Insert(int index, T item)
    {
        _items.Insert(index, item);
        NotifyChanged();
    }

    public bool Remove(T item)
    {
        bool removed = _items.Remove(item);
        if (removed) NotifyChanged();
        return removed;
    }

    public void RemoveAt(int index)
    {
        _items.RemoveAt(index);
        NotifyChanged();
    }

    public int RemoveAll(Predicate<T> match)
    {
        int count = _items.RemoveAll(match);
        if (count > 0) NotifyChanged();
        return count;
    }

    public void Clear()
    {
        if (_items.Count == 0) return;
        _items.Clear();
        NotifyChanged();
    }

    // ──── Reactive infrastructure ────

    private void NotifyChanged()
    {
        _version++;
        if (_subscribers is null) return;

        var snapshot = new List<IReactiveNode>(_subscribers);
        foreach (var subscriber in snapshot)
            subscriber.NotifyDirty();
    }

    int IReactiveSource.Version => _version;
    void IReactiveSource.Refresh() { }

    void IReactiveSource.Subscribe(IReactiveNode subscriber)
    {
        _subscribers ??= [];
        _subscribers.Add(subscriber);
    }

    void IReactiveSource.Unsubscribe(IReactiveNode subscriber)
    {
        _subscribers?.Remove(subscriber);
    }

    public override string ToString() => $"ReactiveList({_items.Count} items)";
}
