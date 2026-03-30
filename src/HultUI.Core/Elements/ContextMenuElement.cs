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
internal sealed class ContextMenuElement : Element
{
    private readonly IReadOnlyList<ContextMenuItem> _items;
    private readonly ContextMenuTheme _theme;
    private readonly Action _closeMenu;
    private int _hoveredIndex = -1;

    // Cached item layout (computed during Render, reused for hit-testing)
    private float[] _itemYOffsets = [];
    private float[] _itemHeights = [];
    private float _computedWidth;

    public ContextMenuElement(IReadOnlyList<ContextMenuItem> items, ContextMenuTheme theme, Action closeMenu)
    {
        _items = items;
        _theme = theme;
        _closeMenu = closeMenu;

        On<PointerMoveEvent>(OnPointerMove);
        On<PointerDownEvent>(OnPointerDown);
        On<PointerLeaveEvent>(_ => { _hoveredIndex = -1; MarkPaintDirty(); });
    }

    public int HoveredIndex => _hoveredIndex;

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

    /// <summary>Activate the currently selected item. Returns true if an action was invoked.</summary>
    public bool ActivateSelected()
    {
        if (_hoveredIndex < 0 || _hoveredIndex >= _items.Count) return false;
        var item = _items[_hoveredIndex];
        if (item.IsSeparator || item.IsDisabled) return false;

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
        }
    }

    private void OnPointerDown(PointerDownEvent evt)
    {
        if (evt.Button != 0) return;

        int index = IndexFromPoint(evt.Position);
        if (index < 0) return;

        var item = _items[index];
        if (item.IsSeparator || item.IsDisabled) return;

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

            totalHeight += _theme.ItemPadding.Top + labelSize.Height + _theme.ItemPadding.Bottom;
        }

        float shortcutExtra = maxShortcutWidth > 0 ? _theme.ShortcutGap + maxShortcutWidth : 0;
        float width = Math.Max(_theme.MinWidth,
            _theme.ItemPadding.Left + maxLabelWidth + shortcutExtra + _theme.ItemPadding.Right);

        return new Size(width, totalHeight);
    }

    public override void Render(IRenderContext ctx)
    {
        var box = Layout.AbsoluteBorderBox;
        var style = ResolvedInherited;
        var itemStyle = style with { FontSize = _theme.FontSize, TextColor = _theme.TextColor };
        var disabledStyle = itemStyle with { TextColor = _theme.DisabledTextColor };
        var shortcutStyle = style with { FontSize = _theme.ShortcutFontSize, TextColor = _theme.ShortcutTextColor };

        // Draw background + border
        ctx.DrawRoundedRect(box, new CornerRadius(_theme.CornerRadius),
            _theme.Background, _theme.BorderColor, new Thickness(1));

        // Build item layout arrays
        if (_itemYOffsets.Length != _items.Count)
        {
            _itemYOffsets = new float[_items.Count];
            _itemHeights = new float[_items.Count];
        }

        float y = _theme.Padding.Top;
        float contentWidth = box.Width;
        _computedWidth = contentWidth;

        ctx.PushClip(box);

        for (int i = 0; i < _items.Count; i++)
        {
            var item = _items[i];
            _itemYOffsets[i] = y;

            if (item.IsSeparator)
            {
                float sepHeight = _theme.SeparatorMargin * 2 + 1;
                _itemHeights[i] = sepHeight;
                float lineY = box.Y + y + _theme.SeparatorMargin;
                ctx.DrawLine(
                    new Point(box.X + _theme.ItemPadding.Left, lineY),
                    new Point(box.X + contentWidth - _theme.ItemPadding.Right, lineY),
                    _theme.SeparatorColor, 1f);
                y += sepHeight;
                continue;
            }

            var textSize = ctx.MeasureText(item.Label, itemStyle);
            float itemHeight = _theme.ItemPadding.Top + textSize.Height + _theme.ItemPadding.Bottom;
            _itemHeights[i] = itemHeight;

            // Hover highlight
            if (i == _hoveredIndex && !item.IsDisabled)
            {
                ctx.DrawRect(
                    new Rect(box.X + _theme.Padding.Left, box.Y + y,
                        contentWidth - _theme.Padding.Left - _theme.Padding.Right, itemHeight),
                    _theme.HoverBackground);
            }

            // Label
            var labelRect = new Rect(
                box.X + _theme.ItemPadding.Left,
                box.Y + y + _theme.ItemPadding.Top,
                contentWidth - _theme.ItemPadding.Left - _theme.ItemPadding.Right,
                textSize.Height);

            ctx.DrawText(item.Label, labelRect,
                item.IsDisabled ? disabledStyle : itemStyle,
                TextOverflow.Clip, 1);

            // Shortcut hint (right-aligned)
            if (item.ShortcutHint is not null)
            {
                var hintSize = ctx.MeasureText(item.ShortcutHint, shortcutStyle);
                var hintRect = new Rect(
                    box.X + contentWidth - _theme.ItemPadding.Right - hintSize.Width,
                    box.Y + y + _theme.ItemPadding.Top + (textSize.Height - hintSize.Height) / 2,
                    hintSize.Width,
                    hintSize.Height);
                ctx.DrawText(item.ShortcutHint, hintRect,
                    item.IsDisabled ? disabledStyle : shortcutStyle,
                    TextOverflow.Clip, 1);
            }

            y += itemHeight;
        }

        ctx.Pop(); // clip
    }

    public override bool HitTest(Point point) =>
        Layout.AbsoluteBorderBox.Contains(point);
}
