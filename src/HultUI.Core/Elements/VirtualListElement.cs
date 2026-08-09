using HultUI.Core.Accessibility;
using HultUI.Core.Events;
using HultUI.Core.Layout;
using HultUI.Core.Primitives;
using HultUI.Core.Rendering;
using HultUI.Core.Styling;

namespace HultUI.Core.Elements;

/// <summary>
/// Virtualized list element that only materializes elements for visible items.
/// Uses a fixed item height for efficient offset calculation and element recycling.
/// Extends ScrollViewElement to reuse scroll rendering, hit testing, and scrollbar UI.
/// </summary>
public sealed class VirtualListElement<T> : ScrollViewElement, IAccessibleItemSource
{
    private IReadOnlyList<T> _items = [];
    private float _itemHeight = 40f;
    private Func<T, Element>? _itemTemplate;

    /// <summary>
    /// Optional accessible label for an item, used by assistive tech to name each
    /// row. Should read cheaply from item data — it may be called for every item.
    /// Falls back to <c>item?.ToString()</c> when unset.
    /// </summary>
    public Func<T, string?>? AccessibleLabel { get; set; }

    /// <summary>
    /// Optional handler run when assistive tech activates an item (its primary
    /// action, as a click would do). Lets a screen-reader user act on any item,
    /// including off-screen ones the list hasn't realized. Wire it to the same
    /// logic your row's click handler uses.
    /// </summary>
    public Action<T>? OnItemActivate { get; set; }

    /// <summary>
    /// Index of the selected item (-1 for none), surfaced to assistive tech as the
    /// UIA selection. Set it to keep the a11y selection in sync with your own.
    /// In multi-select mode (see <see cref="CanSelectMultiple"/>) the selection is
    /// tracked as a set instead — see <see cref="SelectedIndices"/>.
    /// </summary>
    public int SelectedIndex { get; set; } = -1;

    /// <summary>
    /// When true, the list manages a set of selected indices that assistive tech
    /// can extend via UIA <c>SelectionItem.AddToSelection</c> — surfaced to the
    /// container's Selection pattern as a multi-item selection.
    /// </summary>
    public bool CanSelectMultiple { get; set; }

    private readonly HashSet<int> _multiSelected = [];

    /// <summary>The current multi-selection, ascending (empty in single-select mode).</summary>
    public IReadOnlyList<int> SelectedIndices => _multiSelected.Order().ToArray();

    /// <summary>Raised after the multi-selection changes (via assistive tech or code).</summary>
    public Action<IReadOnlyList<int>>? OnSelectionChanged { get; set; }

    private void NotifySelectionChanged()
    {
        MarkPaintDirty();
        OnSelectionChanged?.Invoke(SelectedIndices);
    }

    // ── IAccessibleItemSource: exposes all items to the a11y tree, even the ones
    //    virtualization hasn't materialized as elements. ──
    int IAccessibleItemSource.ItemCount => _items.Count;
    float IAccessibleItemSource.ItemHeight => _itemHeight;
    int IAccessibleItemSource.SelectedIndex => SelectedIndex;
    bool IAccessibleItemSource.CanSelectMultiple => CanSelectMultiple;

    IReadOnlyList<int> IAccessibleItemSource.SelectedIndices =>
        CanSelectMultiple ? SelectedIndices
                          : (SelectedIndex >= 0 ? new[] { SelectedIndex } : []);

    bool IAccessibleItemSource.IsItemSelected(int index) =>
        CanSelectMultiple ? _multiSelected.Contains(index) : index == SelectedIndex;

    void IAccessibleItemSource.SelectOnly(int index)
    {
        if (!CanSelectMultiple)
        {
            ((IAccessibleItemSource)this).ActivateItem(index);
            return;
        }
        _multiSelected.Clear();
        if (index >= 0 && index < _items.Count) _multiSelected.Add(index);
        NotifySelectionChanged();
    }

    void IAccessibleItemSource.SetItemSelected(int index, bool selected)
    {
        if (!CanSelectMultiple || index < 0 || index >= _items.Count) return;
        bool changed = selected ? _multiSelected.Add(index) : _multiSelected.Remove(index);
        if (changed) NotifySelectionChanged();
    }

    string? IAccessibleItemSource.GetItemLabel(int index)
    {
        if (index < 0 || index >= _items.Count) return null;
        var item = _items[index];
        return AccessibleLabel is not null ? AccessibleLabel(item) : item?.ToString();
    }

    void IAccessibleItemSource.ActivateItem(int index)
    {
        if (index >= 0 && index < _items.Count)
            OnItemActivate?.Invoke(_items[index]);
    }

    // Visible range tracking
    private int _visibleStart = -1;
    private int _visibleEnd = -1;

    // The spacer element pushes visible items to their correct virtual positions
    private readonly BoxElement _spacer = new();

    // Element recycling pool
    private readonly List<Element> _recyclePool = [];

    // Currently materialized elements (index → element)
    private readonly Dictionary<int, Element> _activeElements = [];

    // Captured during normal pipeline phases for inline child processing
    private Theme? _activeTheme;
    private LayoutEngine? _childLayoutEngine;

    /// <summary>
    /// When true (and the list is focused), Up/Down scroll by one item, PageUp/PageDown
    /// by a viewport, and Home/End jump to the ends. Off by default so a list whose
    /// owner already drives selection from these keys doesn't get double handling.
    /// </summary>
    /// <remarks>
    /// Chorded keys are deliberately left alone: a key held with Shift, Ctrl, Alt or
    /// Super bubbles untouched, so application shortcuts still reach their handler
    /// even while the list has focus. (Only those four count — <c>KeyModifiers</c> also
    /// carries CapsLock/NumLock, which are lock states that must not suppress scrolling.)
    /// </remarks>
    public bool KeyboardScrollEnabled { get; set; }

    /// <summary>The modifiers that make a key a chord belonging to someone else.
    /// Excludes the lock states, which say nothing about what is being held.</summary>
    private const KeyModifiers ChordModifiers =
        KeyModifiers.Shift | KeyModifiers.Ctrl | KeyModifiers.Alt | KeyModifiers.Super;

    public VirtualListElement()
    {
        // Column direction so children stack vertically
        Flex.Direction = FlexDirection.Column;
        OnOwn<KeyDownEvent>(HandleKeyboardScroll);
    }

    private void HandleKeyboardScroll(KeyDownEvent e)
    {
        if (!KeyboardScrollEnabled) return;
        if ((e.Modifiers & ChordModifiers) != 0) return;   // someone else's shortcut

        // A page keeps one row of context, matching how editors page.
        float page = Math.Max(_itemHeight, Layout.AbsoluteContentBox.Height - _itemHeight);
        switch (e.Key)
        {
            case KeyCode.Down: ScrollOffsetY += _itemHeight; break;
            case KeyCode.Up: ScrollOffsetY -= _itemHeight; break;
            case KeyCode.PageDown: ScrollOffsetY += page; break;
            case KeyCode.PageUp: ScrollOffsetY -= page; break;
            case KeyCode.Home: ScrollOffsetY = 0; break;
            case KeyCode.End: ScrollOffsetY = _contentHeight; break;   // ScrollOffsetY clamps
            default: return;                                            // not ours; let it bubble
        }
        e.StopPropagation = true;
    }

    public static new VirtualListElement<T> New([OriginFile] string? file = null, [OriginLine] int line = 0) =>
        new VirtualListElement<T>().WithOrigin(file, line);

    protected internal override void ApplyTheme(Theme theme)
    {
        base.ApplyTheme(theme);
        _activeTheme = theme;
    }

    internal override Size MeasureContent(float availableWidth, float availableHeight, IMeasureContext ctx)
    {
        _childLayoutEngine ??= new LayoutEngine(ctx);
        return base.MeasureContent(availableWidth, availableHeight, ctx);
    }

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
        int prevStart = _visibleStart;
        int prevEnd = _visibleEnd;
        SyncVisibleItems();

        // Children are created during the render pass, after the normal
        // style/layout phases have completed. Run inline style resolution
        // and layout so the children are renderable immediately.
        if (_visibleStart != prevStart || _visibleEnd != prevEnd)
            LayoutVisibleChildren();
    }

    private void LayoutVisibleChildren()
    {
        if (_childLayoutEngine is null || Children.Count == 0)
            return;

        var contentBox = Layout.AbsoluteContentBox;
        var viewportWidth = contentBox.Width;

        // Style-resolve and layout only newly created children (those still dirty).
        // Reused elements already have valid internal layout from a prior frame.
        foreach (var child in Children)
        {
            if ((child.DirtyFlags & DirtyFlags.Style) != 0)
            {
                if (_activeTheme is not null)
                    StyleResolver.Resolve(child, _activeTheme);
                else
                    StyleResolver.Resolve(child);
            }

            if ((child.DirtyFlags & DirtyFlags.Layout) != 0)
                _childLayoutEngine.PerformLayout(child, viewportWidth, _itemHeight);
        }

        // Position every child at its exact virtual Y coordinate.
        // Using analytical positions (index * itemHeight) avoids two pitfalls:
        //  - accumulating from Layout.Height would use stale cached spacer height
        //  - additive offsetting would double-shift reused elements
        for (int i = 0; i < Children.Count; i++)
        {
            var child = Children[i];
            float targetY = child == _spacer
                ? contentBox.Y
                : contentBox.Y + (_visibleStart + (i - 1)) * _itemHeight;

            SetAbsolutePosition(child, contentBox.X, targetY);
        }
    }

    private static void SetAbsolutePosition(Element element, float targetAbsX, float targetAbsY)
    {
        float dx = targetAbsX - element.Layout.AbsoluteX;
        float dy = targetAbsY - element.Layout.AbsoluteY;

        if (dx == 0 && dy == 0) return;

        element.Layout = element.Layout with
        {
            AbsoluteX = element.Layout.AbsoluteX + dx,
            AbsoluteY = element.Layout.AbsoluteY + dy
        };
        OffsetDescendants(element, dx, dy);
    }

    private static void OffsetDescendants(Element element, float dx, float dy)
    {
        foreach (var child in element.Children)
        {
            child.Layout = child.Layout with
            {
                AbsoluteX = child.Layout.AbsoluteX + dx,
                AbsoluteY = child.Layout.AbsoluteY + dy
            };
            OffsetDescendants(child, dx, dy);
        }
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

            // Accessibility state is rebuilt fresh each render in declarative UIs,
            // so it must ride across the patch — otherwise the a11y selection and
            // activation wiring freeze at their first-mount values.
            SelectedIndex = vl.SelectedIndex;
            OnItemActivate = vl.OnItemActivate;
            AccessibleLabel = vl.AccessibleLabel;
            CanSelectMultiple = vl.CanSelectMultiple;
            OnSelectionChanged = vl.OnSelectionChanged;
            // Handlers aren't copied on patch, but this flag gates the mounted
            // instance's own handler, so it has to ride across.
            KeyboardScrollEnabled = vl.KeyboardScrollEnabled;
            // The live multi-selection lives on the mounted instance (it's mutated
            // by AT, not rebuilt), so only adopt the rebuilt set when it carries one.
            if (vl._multiSelected.Count > 0)
            {
                _multiSelected.Clear();
                foreach (var i in vl._multiSelected) _multiSelected.Add(i);
            }

            // Adopt the rebuilt items and re-realize the visible window on every
            // patch — not only when the Items reference changed. The tree differ
            // wipes our internally-realized rows on each reconcile (the incoming
            // element carries no children; rows are created during render), so if
            // we skip InvalidateItems here, SyncVisibleItems' early-out sees an
            // unchanged visible range and never rebuilds — leaving the rows blank
            // after any MarkDirty() rebuild that reused the same list instance.
            // Re-realizing spans only the viewport (~dozens of rows), so it's cheap
            // even for large lists, and it refreshes each row's template closure so
            // captured per-row state (cursor/selection/marks) can't go stale.
            _items = vl._items;
            InvalidateItems();
        }
    }
}
