using HultUI.Core.Accessibility;
using HultUI.Core.Events;
using HultUI.Core.Layout;
using HultUI.Core.Primitives;
using HultUI.Core.Rendering;
using HultUI.Core.Styling;
using HultUI.Core.Threading;

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
    // Rows that depend only on their item and whether it is selected, opt-in. Null means every
    // patch realizes the visible rows again from _itemTemplate, as it always has.
    private Func<T, bool, Element>? _itemRowTemplate;

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
    /// <para>On item rows (<see cref="ItemRowTemplate"/>) the list keeps each item's label
    /// while the item at its index is equal to the one it was made from and this is the same
    /// delegate: the accessibility tree asks for every item's label each time it is built, and
    /// a label made on every ask is a string per item per tree. The label is held to the rows'
    /// contract — it reads its item and nothing else, or the list is told with
    /// <see cref="InvalidateRows"/> — so a label that reads other state either goes with a
    /// shape the app invalidates on, or is a new delegate each build (a lambda that captures
    /// that state is), which keeps nothing.</para>
    /// </summary>
    public Func<T, string?>? AccessibleLabel
    {
        get => _accessibleLabel;
        set
        {
            if (ReferenceEquals(_accessibleLabel, value)) return;
            _accessibleLabel = value;
            _labels = null;
        }
    }
    private Func<T, string?>? _accessibleLabel;

    // Item rows only: the label each index was named with and the item it was made from. Kept
    // while that item stays equal (EqualityComparer<T>.Default) and the delegate is the same;
    // InvalidateRows, new Items and another delegate let them all go.
    private ItemLabel[]? _labels;

    private readonly record struct ItemLabel(T Item, string? Label, bool Made);

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
    /// <remarks>A change tells assistive technology, as a checkbox's toggle does: the item that
    /// had the selection and the one that has it change their selected state.</remarks>
    public int SelectedIndex
    {
        get => _selectedIndex;
        set
        {
            if (_selectedIndex == value) return;
            _selectedIndex = value;
            NotifyAccessibilityPropertyChanged(AccessibilityProperties.IsSelected);
            // Kept rows drew the selection they were made with: the one that had it and the one
            // that has it are made again.
            DropRowsWhoseSelectionChanged();
        }
    }
    private int _selectedIndex = -1;

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
        NotifyAccessibilityPropertyChanged(AccessibilityProperties.IsSelected);
        DropRowsWhoseSelectionChanged();
        OnSelectionChanged?.Invoke(SelectedIndices);
    }

    private bool IsItemSelected(int index) =>
        CanSelectMultiple ? _multiSelected.Contains(index) : index == _selectedIndex;

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

    bool IAccessibleItemSource.IsItemSelected(int index) => IsItemSelected(index);

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
        // Kept on item rows, and only on the UI thread: a UI Automation client reads a long
        // list's labels on its own threads, while the UI thread may be changing what is kept.
        if (_itemRowTemplate is null || !UIThread.IsCurrent) return LabelOf(item);

        var labels = _labels;
        if (labels is null || labels.Length != _items.Count)
        {
            // The entries kept stay where they are: each is checked against its item anyway.
            Array.Resize(ref labels, _items.Count);
            _labels = labels;
        }
        ref var kept = ref labels[index];
        if (kept.Made && EqualityComparer<T>.Default.Equals(kept.Item, item))
            return kept.Label;
        var label = LabelOf(item);
        kept = new ItemLabel(item, label, true);
        return label;
    }

    private string? LabelOf(T item) => _accessibleLabel is { } label ? label(item) : item?.ToString();

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

    // Currently materialized rows (index → row)
    private readonly Dictionary<int, Row> _activeElements = [];

    /// <summary>A realized row and what it was made from. With <see cref="ItemRowTemplate"/> a
    /// rebuild keeps the row while its item and its selected state are still these.</summary>
    private readonly record struct Row(Element Element, T Item, bool Selected);

    // The indices of rows about to be let go, gathered before the dictionary is changed. Kept
    // so that letting go of a row or two on a selection move allocates nothing.
    private readonly List<int> _dropping = [];

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
        // Kept rows were made with the colours of the theme in force then. Another theme makes
        // them again; this runs on every style pass, so only a different theme object counts.
        if (_itemRowTemplate is not null && _activeTheme is not null && !ReferenceEquals(theme, _activeTheme))
            InvalidateItems();
        _activeTheme = theme;
    }

    /// <summary>Kept rows were made at the display scale in force then: a glyph an app cuts at the
    /// scale (Junction's icons) is a raster for that scale, which the app's cache retires when the
    /// scale changes and frees at the next change. Another scale makes them again, as another theme
    /// does, so a row whose shape forgot the scale cannot keep a freed raster (Junction History,
    /// junction-v1.20.0: every icon gone after a move to 150 % and back).</summary>
    internal override void OnDisplayScaleChanged()
    {
        if (_itemRowTemplate is not null) InvalidateItems();
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

    /// <summary>
    /// Builds a row from its item. Every rebuild of the owner realizes the visible rows again
    /// from it, so a row may show anything the owner's state holds. Setting it clears
    /// <see cref="ItemRowTemplate"/>.
    /// </summary>
    public Func<T, Element>? ItemTemplate
    {
        get => _itemTemplate;
        set
        {
            _itemTemplate = value;
            if (value is not null) _itemRowTemplate = null;
            InvalidateItems();
        }
    }

    /// <summary>
    /// Builds a row from its item and whether that item is selected — and from nothing else,
    /// which is what lets the list keep the row. Set by <c>.RenderWith((item, selected) => …)</c>;
    /// setting it clears <see cref="ItemTemplate"/>.
    /// <para>
    /// With it, a rebuild of the owner keeps every realized row whose item at that index is
    /// still equal to the one it was made from (<see cref="EqualityComparer{T}.Default"/>:
    /// the same object for a class, equal values for a record or a struct) and whose selected
    /// state (<see cref="SelectedIndex"/>, or the multi-selection) is unchanged. Only the other
    /// rows are made again — a selection move makes two. A kept row is the same element, so
    /// it keeps its hover, focus and layout.
    /// </para>
    /// <para>
    /// The contract: a row that shows anything else — the owner's state, data that arrives
    /// later for an unchanged item, a window width, a setting — must be told when that changes,
    /// with <see cref="InvalidateRows"/> on the mounted list (before the owner's rebuild, through
    /// its ref, or after it), or with <see cref="InvalidateRow"/> for the rows it changed when it
    /// changes only a few. Otherwise the kept rows go on showing what they showed when they
    /// were made, until they scroll out and back in. A switch to another <see cref="Theme"/>
    /// object makes the rows again by itself, and so does a change of the window's display scale
    /// (<see cref="Element.OnDisplayScaleChanged"/>); a theme changed in place does not. Handlers in a
    /// kept row are the ones it was made with, so they should read live state rather than
    /// capture what the build saw.
    /// </para>
    /// <para>
    /// The contract covers the items' accessible labels too (<see cref="AccessibleLabel"/>):
    /// the list keeps each item's label while the item is equal and the label is the same
    /// delegate, and lets them all go with the rows on <see cref="InvalidateRows"/> or new
    /// <see cref="Items"/>.
    /// </para>
    /// </summary>
    public Func<T, bool, Element>? ItemRowTemplate
    {
        get => _itemRowTemplate;
        set
        {
            _itemRowTemplate = value;
            if (value is not null) _itemTemplate = null;
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
    /// position is kept. With <see cref="ItemRowTemplate"/> this is how anything a row shows
    /// beyond its item and its selected state reaches the rows the list keeps, and the items'
    /// accessible labels, which it keeps the same way.
    /// </summary>
    public void InvalidateRows() => InvalidateItems();

    /// <summary>
    /// <see cref="InvalidateRows"/> for one item: throws away the realized row at
    /// <paramref name="index"/>, and the item's kept accessible label, so the next frame makes
    /// them again from the latest template and label; every other row stays as it is. For item
    /// rows (<see cref="ItemRowTemplate"/>) that show something beside their item and selected
    /// state which only a row or two show at a time and which moves — a second highlight that
    /// follows another list's cursor, say — when the app knows which rows it left and reached:
    /// told through <see cref="InvalidateRows"/>, each move made every row on screen again.
    /// An index whose row is not realized lets go of its label only; one outside the items the
    /// list holds now does nothing (no row is realized there). The scroll position is kept. Like
    /// <see cref="InvalidateRows"/> it can be called through the list's ref at the top of the
    /// owner's build, before the list is patched, or after it.
    /// </summary>
    public void InvalidateRow(int index)
    {
        if ((uint)index >= (uint)_items.Count) return;
        if (_labels is { } labels && index < labels.Length) labels[index] = default;
        if (!_activeElements.ContainsKey(index)) return;
        _dropping.Add(index);
        DropGathered();
    }

    private void InvalidateItems()
    {
        _offsets = null;
        _labels = null;
        _visibleStart = -1;
        _visibleEnd = -1;
        foreach (var row in _activeElements.Values)
            Retain(row.Element);
        _activeElements.Clear();
        _recyclePool.Clear();
        MarkLayoutDirty();
        MarkPaintDirty();
    }

    /// <summary>Holds a keyed row back for the next realization of its key (see <see cref="_retained"/>).</summary>
    private void Retain(Element element)
    {
        if (element.Key is { } key)
            (_retained ??= new Dictionary<string, Element>(StringComparer.Ordinal))[key] = element;
    }

    /// <summary>
    /// With <see cref="ItemRowTemplate"/>, lets go of the realized rows whose selected state is
    /// not the one they were made with, so the next frame makes them again; the rest stay.
    /// </summary>
    private void DropRowsWhoseSelectionChanged()
    {
        if (_itemRowTemplate is null || _activeElements.Count == 0) return;
        foreach (var (index, row) in _activeElements)
            if (row.Selected != IsItemSelected(index))
                _dropping.Add(index);
        DropGathered();
    }

    /// <summary>Lets go of the rows gathered in <see cref="_dropping"/>, and has the next frame
    /// realize the window again — the kept rows as they are, the others from the template.</summary>
    private void DropGathered()
    {
        if (_dropping.Count == 0) return;
        foreach (int index in _dropping)
        {
            if (_activeElements.Remove(index, out var row))
                Retain(row.Element);
        }
        _dropping.Clear();
        _visibleStart = -1;
        _visibleEnd = -1;
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
                // Under this list, as the style pass resolves a row whenever it next passes
                // through, and as it resolves any other child. Resolved under the theme's
                // defaults instead, a row inherited nothing set above the list, and the next
                // style pass changed its font size (the theme's 16 px to the 12–13 px apps set
                // above their lists) and marked every box and icon of every row for layout.
                StyleResolver.ResolveUnder(child, ResolvedInherited, _activeTheme);
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
        var children = element.Children;   // by index: foreach over the interface boxes an enumerator
        for (int i = 0; i < children.Count; i++)
        {
            var child = children[i];
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
        if ((_itemTemplate is null && _itemRowTemplate is null) || _items.Count == 0)
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
        foreach (var (index, row) in _activeElements)
        {
            if (index < newStart || index >= newEnd)
            {
                _recyclePool.Add(row.Element);
                _dropping.Add(index);
            }
        }

        // Remove stale entries
        foreach (var index in _dropping)
            _activeElements.Remove(index);
        _dropping.Clear();

        // Create or reuse elements for newly visible items
        for (int i = newStart; i < newEnd; i++)
        {
            if (_activeElements.TryGetValue(i, out var live))
            {
                // A kept row's height can move without its item changing (a height function
                // rebuilt with the owner); it is forced again, the way a new row is below.
                if (_itemRowTemplate is not null && live.Element.Box.Height != Dimension.Px(HeightOf(i)))
                {
                    live.Element.Box.Height = HeightOf(i);
                    live.Element.MarkLayoutDirty();
                }
                continue;
            }

            // Recycle: take from pool, but recreate from template
            // (recycled element structure may differ)
            if (_recyclePool.Count > 0)
                _recyclePool.RemoveAt(_recyclePool.Count - 1);
            var item = _items[i];
            bool selected = _itemRowTemplate is not null && IsItemSelected(i);
            var element = _itemRowTemplate is { } rowTemplate ? rowTemplate(item, selected) : _itemTemplate!(item);

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

            _activeElements[i] = new Row(element, item, selected);
        }
        // Whatever was held back and not asked for again has left the viewport for good.
        _retained = null;

        // Update spacer height to push items to correct virtual positions
        _spacer.Box.Height = OffsetOf(newStart);
        _spacer.Flex.FlexShrink = 0;
        _spacer.BackgroundColor = Color.Transparent;

        // Kept rows, all of them, in the order they stand: only a height moved, and replacing
        // the children with themselves would tell assistive technology the structure changed.
        if (_itemRowTemplate is not null && ChildrenAreRealized(newStart, newEnd))
        {
            MarkLayoutDirty();
            _visibleStart = newStart;
            _visibleEnd = newEnd;
            return;
        }

        // Build children array: spacer + visible items in order
        var children = new Element[1 + (newEnd - newStart)];
        children[0] = _spacer;
        for (int i = newStart; i < newEnd; i++)
            children[1 + i - newStart] = _activeElements[i].Element;

        ReplaceChildren(children);

        _visibleStart = newStart;
        _visibleEnd = newEnd;
    }

    /// <summary>Whether the children are already the spacer and rows <paramref name="start"/> to
    /// <paramref name="end"/> as realized, in order.</summary>
    private bool ChildrenAreRealized(int start, int end)
    {
        var children = Children;
        if (children.Count != 1 + (end - start) || children[0] != _spacer)
            return false;
        for (int i = start; i < end; i++)
            if (children[1 + i - start] != _activeElements[i].Element)
                return false;
        return true;
    }

    // Of what it copies below, the rows and their heights reach layout, and every path that
    // changes them marks it: InvalidateItems, DropGathered, and the height check in
    // KeepUnchangedRows. An item-rows list patched by a rebuild that changed none of them keeps
    // its layout, and so do the boxes around it.
    private protected override bool PatchMarksOwnLayout => true;

    internal override void PatchFrom(Element source)
    {
        var offsetsBefore = _offsets;
        base.PatchFrom(source);
        if (source is VirtualListElement<T> vl)
        {
            // Rows are kept only from item rows to item rows of the same height; anything else
            // makes them another way, and they are all made again.
            bool keepRows = _itemRowTemplate is not null && vl._itemRowTemplate is not null
                && _itemHeight == vl._itemHeight;
            bool heightsFromFunction = _itemHeightFor is not null || vl._itemHeightFor is not null;
            _itemTemplate = vl._itemTemplate;
            _itemRowTemplate = vl._itemRowTemplate;
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

            if (keepRows)
            {
                KeepUnchangedRows(vl._items, heightsFromFunction, offsetsBefore);
                return;
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

    /// <summary>
    /// The item-rows patch: adopts the rebuilt items and lets go of only the realized rows that
    /// no longer match what they were made from — another item at their index (compared with
    /// <see cref="EqualityComparer{T}.Default"/>, also when the same list came back, since it
    /// may have been changed in place), another selected state, or an index past the end. The
    /// rest stay as they are, children of this list, laid out; the next frame realizes only the
    /// gaps. Rows are matched by index, so an insertion above them makes the rows it moves again.
    /// </summary>
    /// <param name="heightsFromFunction">Whether row heights came, or now come, from
    /// <see cref="ItemHeightFor"/>: then they are worked out again.</param>
    /// <param name="offsetsBefore">The row offsets before the patch, to tell whether they moved.</param>
    private void KeepUnchangedRows(IReadOnlyList<T> items, bool heightsFromFunction, float[]? offsetsBefore)
    {
        _items = items;
        var comparer = EqualityComparer<T>.Default;
        foreach (var (index, row) in _activeElements)
        {
            if (index >= items.Count || row.Selected != IsItemSelected(index) || !comparer.Equals(row.Item, items[index]))
                _dropping.Add(index);
        }
        DropGathered();

        // A height function closes over the rebuilt rows, so its heights are worked out again,
        // as every patch has always done. When one moved, the window is realized again so the
        // spacer and the kept rows take them (without re-making a row whose item did not change),
        // and the list is laid out again; the same when a list stops using one and every row goes
        // back to ItemHeight. When none moved there is nothing to redo: a diff list patched by a
        // rebuild for something beside it laid out itself and everything around it on every key.
        if (heightsFromFunction)
        {
            _offsets = null;
            EnsureOffsets();
            if (offsetsBefore is null || _offsets is not { } now || !offsetsBefore.AsSpan().SequenceEqual(now))
            {
                _offsets = null;
                _visibleStart = -1;
                _visibleEnd = -1;
                MarkLayoutDirty();
                MarkPaintDirty();
            }
        }
    }
}
