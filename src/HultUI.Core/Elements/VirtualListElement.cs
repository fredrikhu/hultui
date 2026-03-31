using HultUI.Core.Layout;
using HultUI.Core.Primitives;

namespace HultUI.Core.Elements;

/// <summary>
/// Virtualized list element that only materializes elements for visible items.
/// Uses a fixed item height for efficient offset calculation and element recycling.
/// Extends ScrollViewElement to reuse scroll rendering, hit testing, and scrollbar UI.
/// </summary>
public sealed class VirtualListElement<T> : ScrollViewElement
{
    private IReadOnlyList<T> _items = [];
    private float _itemHeight = 40f;
    private Func<T, Element>? _itemTemplate;

    // Visible range tracking
    private int _visibleStart = -1;
    private int _visibleEnd = -1;

    // The spacer element pushes visible items to their correct virtual positions
    private readonly BoxElement _spacer = new();

    // Element recycling pool
    private readonly List<Element> _recyclePool = [];

    // Currently materialized elements (index → element)
    private readonly Dictionary<int, Element> _activeElements = [];

    public VirtualListElement()
    {
        // Column direction so children stack vertically
        Flex.Direction = FlexDirection.Column;
    }

    public static new VirtualListElement<T> New() => new();

    public IReadOnlyList<T> Items
    {
        get => _items;
        set
        {
            _items = value ?? [];
            InvalidateItems();
        }
    }

    public float ItemHeight
    {
        get => _itemHeight;
        set
        {
            if (_itemHeight == value) return;
            _itemHeight = value;
            InvalidateItems();
        }
    }

    public Func<T, Element>? ItemTemplate
    {
        get => _itemTemplate;
        set
        {
            _itemTemplate = value;
            InvalidateItems();
        }
    }

    private void InvalidateItems()
    {
        _visibleStart = -1;
        _visibleEnd = -1;
        _activeElements.Clear();
        _recyclePool.Clear();
        MarkLayoutDirty();
        MarkPaintDirty();
    }

    internal override void UpdateContentSize()
    {
        // Set virtual content height from total item count
        _contentHeight = _items.Count * _itemHeight;
        _contentWidth = Layout.AbsoluteContentBox.Width;

        // Re-clamp scroll offset
        _scrollOffsetX = ClampX(_scrollOffsetX);
        _scrollOffsetY = ClampY(_scrollOffsetY);

        // Compute which items are visible and sync children
        SyncVisibleItems();
    }

    private void SyncVisibleItems()
    {
        if (_itemTemplate is null || _items.Count == 0)
        {
            if (Children.Count > 0)
                ReplaceChildren();
            _visibleStart = -1;
            _visibleEnd = -1;
            return;
        }

        var viewport = Layout.AbsoluteContentBox;
        if (viewport.Height <= 0) return;

        // Calculate visible range with 1-item overscan on each side
        int newStart = Math.Max(0, (int)(_scrollOffsetY / _itemHeight) - 1);
        int maxVisible = (int)Math.Ceiling(viewport.Height / _itemHeight) + 3;
        int newEnd = Math.Min(_items.Count, newStart + maxVisible);

        if (newStart == _visibleStart && newEnd == _visibleEnd)
            return;

        // Return elements that are no longer visible to the recycle pool
        foreach (var (index, element) in _activeElements)
        {
            if (index < newStart || index >= newEnd)
                _recyclePool.Add(element);
        }

        // Remove stale entries
        var staleKeys = new List<int>();
        foreach (var index in _activeElements.Keys)
        {
            if (index < newStart || index >= newEnd)
                staleKeys.Add(index);
        }
        foreach (var key in staleKeys)
            _activeElements.Remove(key);

        // Create or reuse elements for newly visible items
        for (int i = newStart; i < newEnd; i++)
        {
            if (_activeElements.ContainsKey(i))
                continue;

            Element element;
            if (_recyclePool.Count > 0)
            {
                // Recycle: take from pool, but recreate from template
                // (recycled element structure may differ)
                _recyclePool.RemoveAt(_recyclePool.Count - 1);
                element = _itemTemplate(_items[i]);
            }
            else
            {
                element = _itemTemplate(_items[i]);
            }

            // Force item to fixed height, full width
            element.Box.Height = _itemHeight;
            element.Flex.FlexShrink = 0;

            _activeElements[i] = element;
        }

        // Update spacer height to push items to correct virtual positions
        _spacer.Box.Height = newStart * _itemHeight;
        _spacer.Flex.FlexShrink = 0;
        _spacer.BackgroundColor = Color.Transparent;

        // Build children array: spacer + visible items in order
        var children = new Element[1 + (newEnd - newStart)];
        children[0] = _spacer;
        for (int i = newStart; i < newEnd; i++)
            children[1 + i - newStart] = _activeElements[i];

        ReplaceChildren(children);

        _visibleStart = newStart;
        _visibleEnd = newEnd;
    }

    internal override void PatchFrom(Element source)
    {
        base.PatchFrom(source);
        if (source is VirtualListElement<T> vl)
        {
            _itemTemplate = vl._itemTemplate;
            _itemHeight = vl._itemHeight;

            // Only update items if reference changed
            if (!ReferenceEquals(_items, vl._items))
            {
                _items = vl._items;
                InvalidateItems();
            }
        }
    }
}
