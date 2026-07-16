using System.Collections;

namespace HultUI.Core.Elements;

/// <summary>
/// Decorates a plain <see cref="IList{T}"/> so every mutation marks the owning
/// element paint-dirty. Used for list-typed visual properties (e.g.
/// <see cref="BoxElement.BoxShadows"/>): a property setter can make assignment
/// self-marking, but it cannot see <c>list.Add(...)</c> on a list handed out
/// earlier — this wrapper closes that gap, so in-place mutations repaint
/// without an explicit <see cref="Element.MarkPaintDirty"/>.
///
/// <para>The wrapper holds the given list by reference (no copy), preserving
/// the sharing semantics a raw list assignment has. Reads never mark.</para>
/// </summary>
public sealed class DirtyList<T> : IList<T>, IReadOnlyList<T>
{
    private readonly List<T> _items;
    private readonly Element _owner;

    public DirtyList(Element owner)
    {
        _owner = owner;
        _items = [];
    }

    public DirtyList(Element owner, IEnumerable<T> items)
    {
        _owner = owner;
        // Unwrap rather than chain when re-wrapping another element's DirtyList
        // (the reconciler assigns one element's list to another): decorating a
        // decorator would mark both owners on every mutation.
        _items = items is DirtyList<T> dl ? dl._items : items as List<T> ?? [.. items];
    }

    /// <summary>The undecorated backing list.</summary>
    internal List<T> Inner => _items;

    public T this[int index]
    {
        get => _items[index];
        set
        {
            if (EqualityComparer<T>.Default.Equals(_items[index], value)) return;
            _items[index] = value;
            _owner.MarkPaintDirty();
        }
    }

    public int Count => _items.Count;
    public bool IsReadOnly => false;

    public void Add(T item)
    {
        _items.Add(item);
        _owner.MarkPaintDirty();
    }

    public void Insert(int index, T item)
    {
        _items.Insert(index, item);
        _owner.MarkPaintDirty();
    }

    public bool Remove(T item)
    {
        if (!_items.Remove(item)) return false;
        _owner.MarkPaintDirty();
        return true;
    }

    public void RemoveAt(int index)
    {
        _items.RemoveAt(index);
        _owner.MarkPaintDirty();
    }

    public void Clear()
    {
        if (_items.Count == 0) return;
        _items.Clear();
        _owner.MarkPaintDirty();
    }

    public bool Contains(T item) => _items.Contains(item);
    public int IndexOf(T item) => _items.IndexOf(item);
    public void CopyTo(T[] array, int arrayIndex) => _items.CopyTo(array, arrayIndex);

    public List<T>.Enumerator GetEnumerator() => _items.GetEnumerator();
    IEnumerator<T> IEnumerable<T>.GetEnumerator() => _items.GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => _items.GetEnumerator();
}
