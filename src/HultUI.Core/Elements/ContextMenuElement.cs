using System.Linq;
using HultUI.Core.Events;
using HultUI.Core.Layout;
using HultUI.Core.Primitives;
using HultUI.Core.Rendering;
using HultUI.Core.Styling;

namespace HultUI.Core.Elements;

/// <summary>
/// Renders a context menu as a single leaf element. Tracks hover/selection
/// index and exposes methods for the Window to drive keyboard navigation.
/// </summary>
internal sealed class ContextMenuElement : Element, Accessibility.IAccessibleItemSource
{
    private const string SubmenuArrow = "▸";

    private readonly IReadOnlyList<ContextMenuItem> _items;
    private readonly ContextMenuTheme _theme;
    private readonly Action _closeMenu;
    private readonly bool _hasIcons;
    private int _hoveredIndex = -1;

    // Cached item layout (computed during Render, reused for hit-testing)
    private float[] _itemYOffsets = [];
    private float[] _itemHeights = [];

    // Each label's height, measured on the first frame and again only when the text style
    // changes: a hover frame of a long menu measured every row again.
    private float[] _textHeights = [];
    private ResolvedInheritedStyle _measuredStyle;
    private bool _measured;
    private float _computedWidth;

    public ContextMenuElement(IReadOnlyList<ContextMenuItem> items, ContextMenuTheme theme, Action closeMenu)
    {
        _items = items;
        _theme = theme;
        _closeMenu = closeMenu;
        // A choice reserves the icon column whether or not it is the one in force, so
        // turning one on does not shift every label sideways.
        _hasIcons = items.Any(i => i.Glyph is not null || i.Icon is not null || i.IsChecked is not null);

        OnOwn<PointerMoveEvent>(OnPointerMove);
        OnOwn<PointerDownEvent>(OnPointerDown);
        OnOwn<PointerLeaveEvent>(_ => { _hoveredIndex = -1; MarkPaintDirty(); });
    }

    public int HoveredIndex => _hoveredIndex;

    // ──── Accessibility ────
    // The menu draws its items itself rather than building an element each, which used to
    // mean a screen reader found a box with nothing in it: the whole menu was one node with
    // no name. The items are published as virtual ones instead — the same contract a
    // virtualized list uses — so they can be read, hit-tested and invoked.
    //
    // Separators are left out: they are a line, not a choice, and an unnamed item in a menu
    // is worse than no item. Indices are therefore the menu's own, mapped through here.

    private int[] ChoiceMap
    {
        get
        {
            if (_choiceMap is not null && _choiceMapItems == _items.Count) return _choiceMap;
            var map = new List<int>(_items.Count);
            for (int i = 0; i < _items.Count; i++)
                if (!_items[i].IsSeparator) map.Add(i);
            _choiceMapItems = _items.Count;
            return _choiceMap = [.. map];
        }
    }
    private int[]? _choiceMap;
    private int _choiceMapItems = -1;

    int Accessibility.IAccessibleItemSource.ItemCount => ChoiceMap.Length;

    float Accessibility.IAccessibleItemSource.ItemHeight =>
        _itemHeights.Length > 0 ? _itemHeights[0] : 0f;

    (float Top, float Height)? Accessibility.IAccessibleItemSource.ItemBounds(int index)
    {
        int item = ItemIndex(index);
        if (item < 0 || item >= _itemYOffsets.Length || item >= _itemHeights.Length) return null;
        return (_itemYOffsets[item], _itemHeights[item]);
    }

    int Accessibility.IAccessibleItemSource.SelectedIndex
    {
        get
        {
            var map = ChoiceMap;
            for (int i = 0; i < map.Length; i++)
                if (map[i] == _hoveredIndex) return i;
            return -1;
        }
    }

    string? Accessibility.IAccessibleItemSource.GetItemLabel(int index)
    {
        int item = ItemIndex(index);
        if (item < 0) return null;
        var menuItem = _items[item];
        // The shortcut is part of what the item is — "Copy, Ctrl C" is how a reader
        // announces it everywhere else.
        return string.IsNullOrEmpty(menuItem.ShortcutHint) ? menuItem.Label : $"{menuItem.Label}, {menuItem.ShortcutHint}";
    }

    bool? Accessibility.IAccessibleItemSource.ItemIsToggled(int index)
    {
        int item = ItemIndex(index);
        return item < 0 ? null : _items[item].IsChecked;
    }

    void Accessibility.IAccessibleItemSource.ActivateItem(int index)
    {
        int item = ItemIndex(index);
        if (item < 0) return;
        var menuItem = _items[item];
        if (menuItem.IsDisabled) return;

        if (menuItem.HasChildren)
        {
            // Opening a submenu is what activating its parent means; the host owns the
            // flyout, so it is told rather than opened from here.
            _hoveredIndex = item;
            HoverChanged?.Invoke(item);
            return;
        }
        _closeMenu();
        menuItem.Action?.Invoke();
    }

    private int ItemIndex(int choice)
    {
        var map = ChoiceMap;
        return choice >= 0 && choice < map.Length ? map[choice] : -1;
    }

    /// <summary>Raised when the hovered item changes (index, or −1 for none).
    /// The menu host uses it to open/close the flyout for items with
    /// <see cref="ContextMenuItem.Children"/>.</summary>
    internal Action<int>? HoverChanged;

    /// <summary>Parent item of the currently open submenu; rendered highlighted
    /// even while the pointer is inside the flyout.</summary>
    internal int PinnedIndex { get; set; } = -1;

    internal ContextMenuItem? ItemAt(int index) =>
        index >= 0 && index < _items.Count ? _items[index] : null;

    /// <summary>Top edge (element-local) of an item row, for anchoring its
    /// flyout. Falls back to 0 before the first render pass.</summary>
    internal float ItemTop(int index) =>
        index >= 0 && index < _itemYOffsets.Length ? _itemYOffsets[index] : 0f;

    /// <summary>Places keyboard selection directly, without walking. The dropdown uses it
    /// to open with its current value highlighted, and as the anchor for Home/End.</summary>
    internal void SetHoveredIndex(int index)
    {
        _hoveredIndex = index;
        MarkPaintDirty();
    }

    /// <summary>Move keyboard selection up or down, skipping separators and disabled items.</summary>
    public void MoveSelection(int direction)
    {
        int start = _hoveredIndex;
        int index = start;

        for (int i = 0; i < _items.Count; i++)
        {
            index += direction;
            if (index < 0) index = _items.Count - 1;
            if (index >= _items.Count) index = 0;

            var item = _items[index];
            if (!item.IsSeparator && !item.IsDisabled)
            {
                _hoveredIndex = index;
                MarkPaintDirty();
                return;
            }
        }
    }

    /// <summary>Activate the currently selected item. Returns true if an action was invoked.
    /// Items with children open their submenu (via <see cref="HoverChanged"/>) instead.</summary>
    public bool ActivateSelected()
    {
        if (_hoveredIndex < 0 || _hoveredIndex >= _items.Count) return false;
        var item = _items[_hoveredIndex];
        if (item.IsSeparator || item.IsDisabled) return false;

        if (item.HasChildren)
        {
            HoverChanged?.Invoke(_hoveredIndex);
            return true;
        }

        _closeMenu();
        item.Action?.Invoke();
        return true;
    }

    private void OnPointerMove(PointerMoveEvent evt)
    {
        int index = IndexFromPoint(evt.Position);
        if (index != _hoveredIndex)
        {
            _hoveredIndex = index;
            MarkPaintDirty();
            if (index >= 0)   // gaps/separators don't close an open flyout
                HoverChanged?.Invoke(index);
        }
    }

    private void OnPointerDown(PointerDownEvent evt)
    {
        if (evt.Button != 0) return;

        int index = IndexFromPoint(evt.Position);
        if (index < 0) return;

        var item = _items[index];
        if (item.IsSeparator || item.IsDisabled) return;

        if (item.HasChildren)
        {
            HoverChanged?.Invoke(index);
            return;
        }

        _closeMenu();
        item.Action?.Invoke();
    }

    private int IndexFromPoint(Point screenPos)
    {
        if (_itemYOffsets.Length == 0) return -1;

        // The overlay system translates for us, so we use the AbsoluteBorderBox
        float localY = screenPos.Y - Layout.AbsoluteBorderBox.Y;

        for (int i = 0; i < _items.Count; i++)
        {
            if (localY >= _itemYOffsets[i] && localY < _itemYOffsets[i] + _itemHeights[i])
            {
                var item = _items[i];
                return (item.IsSeparator || item.IsDisabled) ? -1 : i;
            }
        }
        return -1;
    }

    internal override Size MeasureContent(float availableWidth, float availableHeight, IMeasureContext ctx)
    {
        float maxLabelWidth = 0;
        float maxShortcutWidth = 0;
        float totalHeight = _theme.Padding.Top + _theme.Padding.Bottom;

        var style = ResolvedInherited;
        var itemStyle = style with { FontSize = _theme.FontSize };
        var shortcutStyle = style with { FontSize = _theme.ShortcutFontSize };

        for (int i = 0; i < _items.Count; i++)
        {
            var item = _items[i];
            if (item.IsSeparator)
            {
                totalHeight += _theme.SeparatorMargin * 2 + 1;
                continue;
            }

            var labelSize = ctx.MeasureText(item.Label, itemStyle, float.MaxValue, 1);
            maxLabelWidth = Math.Max(maxLabelWidth, labelSize.Width);

            if (item.ShortcutHint is not null)
            {
                var hintSize = ctx.MeasureText(item.ShortcutHint, shortcutStyle, float.MaxValue, 1);
                maxShortcutWidth = Math.Max(maxShortcutWidth, hintSize.Width);
            }
            if (item.HasChildren)
            {
                var arrowSize = ctx.MeasureText(SubmenuArrow, itemStyle, float.MaxValue, 1);
                maxShortcutWidth = Math.Max(maxShortcutWidth, arrowSize.Width);
            }

            totalHeight += _theme.ItemPadding.Top + labelSize.Height + _theme.ItemPadding.Bottom;
        }

        float shortcutExtra = maxShortcutWidth > 0 ? _theme.ShortcutGap + maxShortcutWidth : 0;
        float iconCol = _hasIcons ? _theme.FontSize + _theme.IconGap : 0;
        float width = Math.Max(_theme.MinWidth,
            _theme.ItemPadding.Left + iconCol + maxLabelWidth + shortcutExtra + _theme.ItemPadding.Right);

        return new Size(width, totalHeight);
    }

    public override void Render(IRenderContext ctx)
    {
        var box = Layout.AbsoluteBorderBox;
        var style = ResolvedInherited;
        var itemStyle = style with { FontSize = _theme.FontSize, TextColor = _theme.TextColor };
        var disabledStyle = itemStyle with { TextColor = _theme.DisabledTextColor };
        var dangerStyle = itemStyle with { TextColor = _theme.DangerTextColor };
        var shortcutStyle = style with { FontSize = _theme.ShortcutFontSize, TextColor = _theme.ShortcutTextColor };
        float iconCol = _hasIcons ? _theme.FontSize + _theme.IconGap : 0;

        // Draw background + border
        ctx.DrawRoundedRect(box, new CornerRadius(_theme.CornerRadius),
            _theme.Background, _theme.BorderColor, new Thickness(_theme.BorderWidth));

        // Build item layout arrays
        if (_itemYOffsets.Length != _items.Count)
        {
            _itemYOffsets = new float[_items.Count];
            _itemHeights = new float[_items.Count];
            _textHeights = new float[_items.Count];
            _measured = false;
        }
        bool measure = !_measured || _measuredStyle != itemStyle;
        _measured = true;
        _measuredStyle = itemStyle;

        float y = _theme.Padding.Top;
        float contentWidth = box.Width;
        _computedWidth = contentWidth;

        ctx.PushClip(box);

        // What the clips leave visible. A long menu runs past the window's edge (a dropdown of a
        // few hundred branches is as tall as its rows): its rows out there are laid out, since
        // hit-testing, keyboard moves, a flyout's anchor and the accessible items read every
        // row's place, but not drawn. A 200-item dropdown drew all 200 labels every frame it was
        // open, 172 of them below the window (plan 17, round 4).
        float visibleTop = float.NegativeInfinity, visibleBottom = float.PositiveInfinity;
        if (ctx.ClipBounds is { } clip)
        {
            visibleTop = clip.Y;
            visibleBottom = clip.Bottom;
        }

        for (int i = 0; i < _items.Count; i++)
        {
            var item = _items[i];
            _itemYOffsets[i] = y;

            if (item.IsSeparator)
            {
                float sepHeight = _theme.SeparatorMargin * 2 + 1;
                _itemHeights[i] = sepHeight;
                if (box.Y + y + sepHeight > visibleTop && box.Y + y < visibleBottom)
                {
                    float lineY = box.Y + y + _theme.SeparatorMargin;
                    ctx.DrawLine(
                        new Point(box.X + _theme.ItemPadding.Left, lineY),
                        new Point(box.X + contentWidth - _theme.ItemPadding.Right, lineY),
                        _theme.SeparatorColor, 1f);
                }
                y += sepHeight;
                continue;
            }

            if (measure) _textHeights[i] = ctx.MeasureText(item.Label, itemStyle).Height;
            float textHeight = _textHeights[i];
            float itemHeight = _theme.ItemPadding.Top + textHeight + _theme.ItemPadding.Bottom;
            _itemHeights[i] = itemHeight;
            if (box.Y + y + itemHeight <= visibleTop || box.Y + y >= visibleBottom)
            {
                y += itemHeight;
                continue;
            }

            // Hover highlight (the open submenu's parent stays highlighted). Inset by the
            // border at least: the menu's own 1px edge is drawn inside the box, and a theme
            // with no horizontal padding would otherwise have the fill paint over it — a
            // hovered row read as a notch cut out of the frame.
            if ((i == _hoveredIndex || i == PinnedIndex) && !item.IsDisabled)
            {
                float insetL = MathF.Max(_theme.Padding.Left, 1f);
                float insetR = MathF.Max(_theme.Padding.Right, 1f);
                ctx.DrawRect(
                    new Rect(box.X + insetL, box.Y + y, contentWidth - insetL - insetR, itemHeight),
                    _theme.HoverBackground);
            }

            var labelStyle = item.IsDisabled ? disabledStyle : item.IsDanger ? dangerStyle : itemStyle;

            // Leading glyph (fixed icon column, so labels stay aligned). The check mark
            // for the choice in force lives in the same column, so a menu never mixes an
            // icon with a mark on one row.
            var glyph = item.Glyph ?? (item.IsChecked == true ? _theme.CheckGlyph : null);
            if (glyph is not null || item.Icon is not null)
            {
                var glyphRect = new Rect(
                    box.X + _theme.ItemPadding.Left,
                    box.Y + y + _theme.ItemPadding.Top,
                    iconCol - _theme.IconGap,
                    textHeight);
                // Artwork wins over the text glyph; it is asked for the same colour the
                // label resolved to, so disabled and danger items stay consistent.
                if (item.Icon?.Invoke(labelStyle.TextColor) is { } icon)
                    ctx.DrawImage(icon, glyphRect, ImageFit.Contain);
                else if (glyph is not null)
                    ctx.DrawText(glyph, glyphRect, labelStyle, TextOverflow.Clip, 1);
            }

            // Label
            var labelRect = new Rect(
                box.X + _theme.ItemPadding.Left + iconCol,
                box.Y + y + _theme.ItemPadding.Top,
                contentWidth - _theme.ItemPadding.Left - iconCol - _theme.ItemPadding.Right,
                textHeight);

            ctx.DrawText(item.Label, labelRect, labelStyle, TextOverflow.Clip, 1);

            // Shortcut hint or submenu arrow (right-aligned)
            var trailing = item.HasChildren ? SubmenuArrow : item.ShortcutHint;
            if (trailing is not null)
            {
                var trailingStyle = item.IsDisabled ? disabledStyle
                    : item.HasChildren ? labelStyle
                    : shortcutStyle;
                var hintSize = ctx.MeasureText(trailing, trailingStyle);
                var hintRect = new Rect(
                    box.X + contentWidth - _theme.ItemPadding.Right - hintSize.Width,
                    box.Y + y + _theme.ItemPadding.Top + (textHeight - hintSize.Height) / 2,
                    hintSize.Width,
                    hintSize.Height);
                ctx.DrawText(trailing, hintRect, trailingStyle, TextOverflow.Clip, 1);
            }

            y += itemHeight;
        }

        ctx.Pop(); // clip
    }

    public override bool HitTest(Point point) =>
        Layout.AbsoluteBorderBox.Contains(point);
}
