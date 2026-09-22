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

    // Variable row heights, opt-in. Null means every row is _itemHeight and every path below
    // takes the original uniform arithmetic — existing lists are untouched.
    private Func<T, int, float>? _itemHeightFor;
    // Prefix sums: _offsets[i] is the Y of row i, _offsets[Count] the total height. One O(n)
    // pass per invalidation buys O(log n) scroll-offset lookups and O(1) row positions, which
    // is what lets a list of unequal rows stay as cheap to scroll as a uniform one.
    private float[]? _offsets;

    /// <summary>
    /// Optional accessible label for an item, used by assistive tech to name each
    /// row. Should read cheaply from item data — it may be called for every item.
    /// Falls back to <c>item?.ToString()</c> when unset.
    /// </summary>
    public Func<T, string?>? AccessibleLabel { get; set; }

    /// <summary>
    /// Set when one item is a <i>row of controls</i> rather than one thing — a wrap grid
    /// driven through this list, where each row holds several cards and every card is its
    /// own button.
    ///
    /// <para>Off (the default), the list publishes one <c>ListItem</c> per item, named by
    /// <see cref="AccessibleLabel"/>, including the items it has not realized — which is
    /// right for a list and wrong for a grid, where it would replace a row's buttons with
    /// a single node named after all of them. On, the accessibility tree walks the
    /// realized rows instead, so each card appears with its own name, role and automation
    /// id, exactly as it would outside a list. The trade is that off-screen rows are not
    /// in the tree; they enter it as they realize, the same way a scroll view's content
    /// does.</para>
    /// </summary>
    public bool RowsAreComposite { get; set; }

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
    bool IAccessibleItemSource.PublishesItemNodes => !RowsAreComposite;
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

    // Rows held back across an invalidation, by their Key. A row whose template gives it a
    // key is reconciled with the next realization of that key instead of being replaced by
    // it — so a text field inside a row keeps its focus, caret and selection through the
    // rebuilds the rest of the tree goes through on every keystroke. Cleared once the next
    // window of rows has been realized: a key that did not come back is gone.
    private Dictionary<string, Element>? _retained;

    /// <summary>The rows are realized here, from the template — never handed in by a
    /// build — so the reconciler must not clear them when it patches the list.</summary>
    internal override bool ManagesOwnChildren => true;

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

        // A page keeps one row of context, matching how editors page. With unequal rows the
        // step follows whichever row is at the top, so a line-at-a-time key moves a line.
        float step = _offsets is null ? _itemHeight : HeightOf(IndexAt(_scrollOffsetY));
        float page = Math.Max(step, Layout.AbsoluteContentBox.Height - step);
        switch (e.Key)
        {
            case KeyCode.Down: ScrollOffsetY += step; break;
            case KeyCode.Up: ScrollOffsetY -= step; break;
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

    /// <summary>
    /// Per-row height, for lists whose rows are not all the same size — a wrapped diff, say,
    /// where a long line occupies several rows' worth. Called once per row per invalidation
    /// (not per frame), so it must be cheap but need not be free. Leave it null and the list
    /// keeps its uniform <see cref="ItemHeight"/> behaviour exactly.
    /// <para>
    /// The height returned is the height the row is <b>given</b> — the list forces it, as it
    /// does with <see cref="ItemHeight"/>. It has to match what the row would lay out to, or
    /// the row is clipped or leaves a gap.
    /// </para>
    /// </summary>
    public Func<T, int, float>? ItemHeightFor
    {
        get => _itemHeightFor;
        set { _itemHeightFor = value; InvalidateItems(); }
    }

    /// <summary>
    /// Recomputes row heights when they change without the items changing — the usual cause
    /// being a resize, which re-wraps every row. Cheaper than reassigning Items, and it keeps
    /// the scroll position.
    /// </summary>
    public void InvalidateHeights()
    {
        _offsets = null;
        _visibleStart = -1;
        _visibleEnd = -1;
        MarkLayoutDirty();
        MarkPaintDirty();
    }

    /// <summary>
    /// Throws away the realized rows so the next frame rebuilds them from the template. Use
    /// it when the rows' <i>appearance</i> depends on state the list cannot see — a shared
    /// horizontal offset, say — since a recycled row is otherwise reused as-is. The scroll
    /// position is kept.
    /// </summary>
    public void InvalidateRows() => InvalidateItems();

    private void InvalidateItems()
    {
        _offsets = null;
        _visibleStart = -1;
        _visibleEnd = -1;
        foreach (var element in _activeElements.Values)
            if (element.Key is { } key)
                (_retained ??= new Dictionary<string, Element>(StringComparer.Ordinal))[key] = element;
        _activeElements.Clear();
        _recyclePool.Clear();
        MarkLayoutDirty();
        MarkPaintDirty();
    }

    // ── Row geometry. With no height function these all reduce to the uniform arithmetic. ──

    private void EnsureOffsets()
    {
        if (_itemHeightFor is null) { _offsets = null; return; }
        if (_offsets is not null && _offsets.Length == _items.Count + 1) return;

        var offsets = new float[_items.Count + 1];
        float y = 0f;
        for (int i = 0; i < _items.Count; i++)
        {
            offsets[i] = y;
            // A row of zero height would make scroll-offset lookups ambiguous and let an item
            // hide behind its neighbour, so every row occupies at least a pixel.
            y += Math.Max(1f, _itemHeightFor(_items[i], i));
        }
        offsets[_items.Count] = y;
        _offsets = offsets;
    }

    private float HeightOf(int index) =>
        _offsets is { } o && index >= 0 && index < _items.Count
            ? o[index + 1] - o[index]
            : _itemHeight;

    private float OffsetOf(int index) =>
        _offsets is { } o
            ? o[Math.Clamp(index, 0, _items.Count)]
            : index * _itemHeight;

    private float TotalHeight =>
        _offsets is { } o ? o[^1] : _items.Count * _itemHeight;

    /// <summary>
    /// How far this list can scroll comes from the rows it holds <i>now</i>, not from the
    /// extent the last layout pass left behind. Reconcile assigns the new items before a
    /// component's OnAfterBuild runs, and OnAfterBuild — the natural place for "reveal the
    /// selected row" — runs before the next layout. Clamping against the stale extent
    /// therefore swallowed the request whole whenever the list had just grown: a pane that
    /// listed a folder and asked to land on its 60th file stayed at the top, because at the
    /// moment it asked, the extent still said the list was empty.
    /// </summary>
    protected override float ClampY(float value)
    {
        EnsureOffsets();
        float viewportHeight = Layout.AbsoluteContentBox.Height;
        return Math.Clamp(value, 0, Math.Max(0, TotalHeight - viewportHeight));
    }

    /// <summary>The row containing <paramref name="y"/> in content coordinates.</summary>
    private int IndexAt(float y)
    {
        if (_offsets is not { } o) return (int)(y / _itemHeight);
        int lo = 0, hi = _items.Count - 1;
        while (lo < hi)
        {
            int mid = (lo + hi + 1) / 2;
            if (o[mid] <= y) lo = mid; else hi = mid - 1;
        }
        return lo;
    }

    /// <summary>The extent comes from the row geometry rather than from the children, because
    /// the children here are only the realized window onto the items. Safe to run from the
    /// layout pass: it measures and clamps, and touches no children.</summary>
    internal override void UpdateScrollExtent()
    {
        EnsureOffsets();
        _contentHeight = TotalHeight;
        _contentWidth = Layout.AbsoluteContentBox.Width;

        _scrollOffsetX = ClampX(_scrollOffsetX);
        _scrollOffsetY = ClampY(_scrollOffsetY);
        ApplyPendingReveal();
    }

    internal override void UpdateContentSize()
    {
        UpdateScrollExtent();

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
        for (int i = 0; i < Children.Count; i++)
        {
            var child = Children[i];
            if ((child.DirtyFlags & DirtyFlags.Style) != 0)
            {
                if (_activeTheme is not null)
                    StyleResolver.Resolve(child, _activeTheme);
                else
                    StyleResolver.Resolve(child);
            }

            // Children[0] is the spacer; item k sits at Children[1 + k].
            if ((child.DirtyFlags & DirtyFlags.Layout) != 0)
                _childLayoutEngine.PerformLayout(child, viewportWidth,
                    child == _spacer ? _itemHeight : HeightOf(_visibleStart + (i - 1)));
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
                : contentBox.Y + OffsetOf(_visibleStart + (i - 1));

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
        int newStart, newEnd;
        if (_offsets is null)
        {
            newStart = Math.Max(0, (int)(_scrollOffsetY / _itemHeight) - 1);
            int maxVisible = (int)Math.Ceiling(viewport.Height / _itemHeight) + 3;
            newEnd = Math.Min(_items.Count, newStart + maxVisible);
        }
        else
        {
            // Unequal rows: find the first by binary search, then walk to the viewport's
            // bottom. Walking costs one step per visible row, not per row in the list.
            newStart = Math.Max(0, IndexAt(_scrollOffsetY) - 1);
            float bottom = _scrollOffsetY + viewport.Height;
            newEnd = newStart;
            while (newEnd < _items.Count && OffsetOf(newEnd) < bottom) newEnd++;
            newEnd = Math.Min(_items.Count, newEnd + 2);   // overscan below
        }

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

            // Recycle: take from pool, but recreate from template
            // (recycled element structure may differ)
            if (_recyclePool.Count > 0)
                _recyclePool.RemoveAt(_recyclePool.Count - 1);
            var element = _itemTemplate(_items[i]);

            // A keyed row that was on screen before the invalidation is patched from the
            // fresh one and kept, the way the reconciler keeps a keyed child: same object,
            // same focus, same caret. Its layout is stale, so it is laid out again below.
            if (element.Key is { } key && _retained is { } retained
                && retained.Remove(key, out var kept) && Components.TreeDiffer.TryPatch(kept, element))
            {
                element = kept;
                element.MarkLayoutDirty();
            }

            // Force item to its row height, full width
            element.Box.Height = HeightOf(i);
            element.Flex.FlexShrink = 0;

            _activeElements[i] = element;
        }
        // Whatever was held back and not asked for again has left the viewport for good.
        _retained = null;

        // Update spacer height to push items to correct virtual positions
        _spacer.Box.Height = OffsetOf(newStart);
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
            // The height function closes over the freshly built row model, so it must ride
            // across the patch like the template does — keeping the old one would measure
            // the previous render's rows. Its offsets are dropped so they rebuild.
            if (!ReferenceEquals(_itemHeightFor, vl._itemHeightFor))
            {
                _itemHeightFor = vl._itemHeightFor;
                _offsets = null;
            }

            // Accessibility state is rebuilt fresh each render in declarative UIs,
            // so it must ride across the patch — otherwise the a11y selection and
            // activation wiring freeze at their first-mount values.
            SelectedIndex = vl.SelectedIndex;
            OnItemActivate = vl.OnItemActivate;
            AccessibleLabel = vl.AccessibleLabel;
            RowsAreComposite = vl.RowsAreComposite;
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
