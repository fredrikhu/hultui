using HultUI.Core.Accessibility;
using HultUI.Core.Events;
using HultUI.Core.Layout;
using HultUI.Core.Primitives;
using HultUI.Core.Rendering;
using HultUI.Core.Styling;

namespace HultUI.Core.Elements;

/// <summary>
/// A single-select dropdown: a focusable control showing the current choice, opening a
/// list in an overlay anchored beneath itself. The list is the context-menu machinery,
/// so it looks and navigates like every other popup in the app.
///
/// <para><b>Keyboard.</b> Focus stays on the control while the list is open (the W3C
/// combobox pattern), so every key is handled here rather than in the window's key pump:
/// Enter, Space, Down or Alt+Down open; Up/Down move; Home/End jump; Enter or Space
/// commit; Escape closes without changing the selection.</para>
///
/// <para><b>Sizing.</b> The control measures to its widest item so it does not resize
/// when the selection changes; the open list is at least as wide as the control. The
/// list does not scroll — a dropdown is for choosing among a screenful of options, and
/// anything larger deserves a searchable picker, not a longer menu.</para>
/// </summary>
public class DropdownElement : Element
{
    private const string CheckMark = "✓"; // ✓

    private IReadOnlyList<string> _items = [];
    private int _selectedIndex = -1;
    private bool _focused;
    private bool _open;
    private OverlayHandle _popupHandle;
    private ContextMenuElement? _popup;

    // Set when the host dismissed the popup because of a press on the control itself:
    // that press's own PointerDown arrives right after the dismissal and must read as
    // "close" (which already happened), not "open again". A press elsewhere clears it
    // through Blur, so it cannot swallow a later, legitimate open.
    private bool _suppressOpenFromDismiss;

    private Color _background = Color.White;
    private Color _borderColor = new(226, 232, 240);
    private Color _textColor = new(15, 23, 42);
    private Color _placeholderColor = new(148, 163, 184);
    private Color _chevronColor = new(100, 116, 139);
    private Color _focusRingColor = new(59, 130, 246, 180);
    private float _cornerRadius = 6f;
    private Thickness _padding = new(6, 10);
    private float _fontSize = 13f;
    private float _minWidth = 120f;
    private float _chevronGap = 8f;
    private float _popupGap = 2f;
    private ChevronShape _chevronShape = ChevronShape.Triangle;
    private float _chevronStroke = 1f;

    /// <summary>The options, in display order. Setting a new list clamps the selection
    /// and closes an open list, whose rows would otherwise be stale.</summary>
    public IReadOnlyList<string> Items
    {
        get => _items;
        set
        {
            if (ReferenceEquals(_items, value)) return;
            _items = value ?? [];
            if (_selectedIndex >= _items.Count) SelectedIndex = _items.Count - 1;
            if (_open) ClosePopup();
            MarkLayoutDirty();
        }
    }

    /// <summary>Index into <see cref="Items"/>, or -1 for no selection (the control
    /// shows <see cref="Placeholder"/>). Setting it does not raise
    /// <see cref="OnSelectionChanged"/> — that is reserved for the user's choice.</summary>
    public int SelectedIndex
    {
        get => _selectedIndex;
        set
        {
            int clamped = Math.Max(-1, Math.Min(value, _items.Count - 1));
            if (_selectedIndex == clamped) return;
            _selectedIndex = clamped;
            AccessibleName = SelectedItem ?? Placeholder;
            MarkPaintDirty();
            NotifyAccessibilityPropertyChanged(AccessibilityProperties.Value);
        }
    }

    /// <summary>The selected option's text, or null when nothing is selected.</summary>
    public string? SelectedItem =>
        _selectedIndex >= 0 && _selectedIndex < _items.Count ? _items[_selectedIndex] : null;

    /// <summary>Shown (in the placeholder colour) while nothing is selected.</summary>
    public string Placeholder { get; set; } = "Select…";

    /// <summary>Runs when the user commits a different option — not when
    /// <see cref="SelectedIndex"/> is set from code, and not when the same option is
    /// picked again.</summary>
    public Action<int>? OnSelectionChanged { get; set; }

    /// <summary>True while the list is open.</summary>
    public bool IsOpen => _open;

    public DropdownElement()
    {
        IsFocusable = true;
        Role = AccessibilityRole.ComboBox;
        AccessibleExpanded = false;
        Inherited.Cursor = CursorType.Pointer;

        OnOwn<PointerDownEvent>(OnPointerDown);
        OnOwn<FocusEvent>(_ => { _focused = true; MarkPaintDirty(); });
        OnOwn<BlurEvent>(_ =>
        {
            _focused = false;
            _suppressOpenFromDismiss = false;
            ClosePopup();
            MarkPaintDirty();
        });
        OnOwn<KeyDownEvent>(OnKeyDown);
    }

    public static DropdownElement New(IReadOnlyList<string> items, int selectedIndex = -1,
        [OriginFile] string? file = null, [OriginLine] int line = 0)
    {
        var dd = new DropdownElement { Items = items };
        dd.SelectedIndex = selectedIndex;
        dd.AccessibleName = dd.SelectedItem ?? dd.Placeholder;
        return (DropdownElement)dd.WithOrigin(file, line);
    }

    /// <summary>Fluent registration of <see cref="OnSelectionChanged"/>.</summary>
    public DropdownElement Changed(Action<int> handler)
    {
        OnSelectionChanged = handler;
        return this;
    }

    protected internal override void ApplyTheme(Theme theme)
    {
        var dt = theme.Dropdown;
        _background = dt.Background;
        _borderColor = dt.BorderColor;
        _textColor = dt.TextColor;
        _placeholderColor = dt.PlaceholderTextColor;
        _chevronColor = dt.ChevronColor;
        _cornerRadius = dt.CornerRadius;
        _padding = dt.Padding;
        _fontSize = dt.FontSize;
        _minWidth = dt.MinWidth;
        _chevronGap = dt.ChevronGap;
        _popupGap = dt.PopupGap;
        _chevronShape = dt.ChevronShape;
        _chevronStroke = dt.ChevronStrokeWidth;
        _focusRingColor = theme.FocusRing;
    }

    // ──── Open / close ────

    private void OnPointerDown(PointerDownEvent evt)
    {
        if (evt.Button != 0) return;
        if (_suppressOpenFromDismiss)
        {
            // This press closed the popup via the host's outside-click dismissal,
            // which runs before the press is dispatched to us.
            _suppressOpenFromDismiss = false;
            return;
        }
        if (_open) ClosePopup();
        else OpenPopup();
    }

    private void OpenPopup()
    {
        if (_open || OverlayHost is null || _items.Count == 0) return;

        var menuTheme = OverlayHost.ActiveTheme?.ContextMenu ?? new ContextMenuTheme();
        var rows = new ContextMenuItem[_items.Count];
        for (int i = 0; i < _items.Count; i++)
        {
            int index = i;
            rows[i] = new ContextMenuItem
            {
                Label = _items[i],
                Glyph = i == _selectedIndex ? CheckMark : null,
                Action = () => Commit(index),
            };
        }

        var popup = new ContextMenuElement(rows, menuTheme, ClosePopup);
        // At least as wide as the control, so the open list reads as its extension.
        popup.Box.MinWidth = Dimension.Px(Layout.Width);
        popup.SetHoveredIndex(_selectedIndex);

        // Screen box, not layout box: an overlay is placed in window coordinates, and inside a
        // scrolled page the two differ by however far the page has been scrolled.
        var box = ScreenBorderBox;
        _popupHandle = OverlayHost.ShowOverlay(popup, box.X, box.Bottom + _popupGap,
            dismissOnOutsideClick: true, hitTestable: true, onDismissed: OnPopupDismissed);
        _popup = popup;
        _open = true;
        AccessibleExpanded = true;
        MarkPaintDirty();
    }

    private void ClosePopup()
    {
        if (!_open) return;
        var handle = _popupHandle;
        _popupHandle = default;
        _popup = null;
        _open = false;
        AccessibleExpanded = false;
        OverlayHost?.CloseOverlay(handle);
        MarkPaintDirty();
    }

    private void OnPopupDismissed()
    {
        // The host already removed the overlay; only the bookkeeping is left.
        _popupHandle = default;
        _popup = null;
        _open = false;
        AccessibleExpanded = false;
        _suppressOpenFromDismiss = true;
        MarkPaintDirty();
    }

    private void Commit(int index)
    {
        if (index != _selectedIndex)
        {
            SelectedIndex = index;
            OnSelectionChanged?.Invoke(index);
        }
        // The popup closes itself (via closeMenu) before running this action.
        _open = false;
    }

    // ──── Keyboard ────

    private void OnKeyDown(KeyDownEvent evt)
    {
        if (!_focused) return;

        if (!_open)
        {
            bool opens = evt.Key is KeyCode.Enter or KeyCode.Space or KeyCode.Down;
            if (opens)
            {
                OpenPopup();
                evt.StopPropagation = true;
            }
            return;
        }

        switch (evt.Key)
        {
            case KeyCode.Up:
                _popup!.MoveSelection(-1);
                break;
            case KeyCode.Down:
                _popup!.MoveSelection(1);
                break;
            case KeyCode.Home:
                _popup!.SetHoveredIndex(-1);
                _popup.MoveSelection(1);
                break;
            case KeyCode.End:
                _popup!.SetHoveredIndex(_items.Count);
                _popup.MoveSelection(-1);
                break;
            case KeyCode.Enter:
            case KeyCode.Space:
                // Commit the hovered row; with nothing hovered there is nothing to
                // commit, so the list just closes.
                if (!_popup!.ActivateSelected())
                    ClosePopup();
                break;
            case KeyCode.Escape:
                ClosePopup();
                break;
            default:
                return;   // unhandled keys keep bubbling
        }
        evt.StopPropagation = true;
    }

    // ──── Measure / render ────

    internal override Size MeasureContent(float availableWidth, float availableHeight, IMeasureContext ctx)
    {
        var style = ResolvedInherited with { FontSize = _fontSize };

        // Widest of every option and the placeholder, so the control keeps its size
        // as the selection changes instead of reflowing its row.
        float maxText = ctx.MeasureText(Placeholder, style, float.MaxValue, 1).Width;
        // The row height still comes off the glyph even when the chevron is drawn: the control's
        // height should not change with the shape of its drop mark.
        float lineHeight = ctx.MeasureText(ChevronPainter.Glyph, style, float.MaxValue, 1).Height;
        foreach (var item in _items)
        {
            var size = ctx.MeasureText(item, style, float.MaxValue, 1);
            maxText = Math.Max(maxText, size.Width);
            lineHeight = Math.Max(lineHeight, size.Height);
        }
        float chevronWidth = ChevronPainter.Width(_chevronShape, _fontSize, ctx, style);

        float width = Math.Max(_minWidth,
            _padding.Left + maxText + _chevronGap + chevronWidth + _padding.Right);
        return new Size(width, _padding.Top + lineHeight + _padding.Bottom);
    }

    public override void Render(IRenderContext ctx)
    {
        var box = Layout.AbsoluteBorderBox;
        var radius = new CornerRadius(_cornerRadius);

        ctx.DrawRoundedRect(box, radius, _background,
            _focused ? _focusRingColor : _borderColor,
            new Thickness(_focused ? 1.5f : 1f));

        var style = ResolvedInherited with
        {
            FontSize = _fontSize,
            TextColor = SelectedItem is null ? _placeholderColor : _textColor,
        };
        var chevronStyle = style with { TextColor = _chevronColor };

        float chevronWidth = ChevronPainter.Width(_chevronShape, _fontSize, ctx, chevronStyle);
        float textRight = box.Right - _padding.Right - chevronWidth - _chevronGap;

        var textRect = new Rect(
            box.X + _padding.Left,
            box.Y + _padding.Top,
            Math.Max(0, textRight - (box.X + _padding.Left)),
            box.Height - _padding.Top - _padding.Bottom);
        ctx.DrawText(SelectedItem ?? Placeholder, textRect, style, TextOverflow.Ellipsis, 1);

        var chevronArea = new Rect(
            box.Right - _padding.Right - chevronWidth, box.Y, chevronWidth, box.Height);
        ChevronPainter.Draw(ctx, _chevronShape, chevronArea, _chevronColor, _chevronStroke,
            chevronStyle, _fontSize);
    }

    internal override void PatchFrom(Element source)
    {
        base.PatchFrom(source);
        if (source is DropdownElement dd)
        {
            Placeholder = dd.Placeholder;
            OnSelectionChanged = dd.OnSelectionChanged;
            Items = dd._items;          // closes an open, now-stale popup itself
            SelectedIndex = dd._selectedIndex;
        }
    }
}
