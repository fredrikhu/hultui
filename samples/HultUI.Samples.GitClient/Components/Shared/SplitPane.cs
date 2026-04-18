using HultUI.Core.Builders;
using HultUI.Core.Elements;
using HultUI.Core.Events;
using HultUI.Core.Layout;
using HultUI.Core.Primitives;
using HultUI.Core.Reactivity;
using HultUI.Core.Styling;
using GitClient.Theme;

namespace GitClient.Components.Shared;

/// <summary>
/// A resizable split pane. Supports horizontal (side-by-side) and vertical (top-bottom) splits.
/// Uses direct mutation for smooth drag performance.
///
/// Cross-element references use <see cref="Ref{T}"/> so they survive
/// reconciliation: the framework keeps the first Build's handlers on the
/// live elements and updates each Ref to point to the live element during
/// PatchFrom, so the handler closures always reach the in-tree elements.
/// </summary>
public class SplitPane
{
    private readonly bool _horizontal;
    private readonly string _key;
    private float _splitRatio;
    private bool _dragging;

    private readonly Ref<BoxElement> _firstPanel = new();
    private readonly Ref<BoxElement> _secondPanel = new();

    public SplitPane(bool horizontal = true, float initialRatio = 0.5f)
    {
        _horizontal = horizontal;
        _splitRatio = initialRatio;
        // Unique key prevents the reconciler from patching one SplitPane's
        // container over another's when they swap positions in the tree.
        // Without this, handlers from the first SplitPane persist on the
        // live element, causing axis mismatches (e.g. a vertical split's
        // Y-axis handler on a horizontal split's container).
        _key = $"split-{Guid.NewGuid():N}";
    }

    public Element Build(Element first, Element second)
    {
        var direction = _horizontal ? FlexDirection.Row : FlexDirection.Column;
        var container = Box.New();
        container.Key = _key;
        container.Flex.Direction = direction;
        container.Flex.FlexGrow = 1;

        // First panel
        var firstPanel = (BoxElement)Box.New().WithRef(_firstPanel);
        firstPanel.Flex.FlexBasis = Dimension.Percent(_splitRatio * 100);
        firstPanel.Flex.FlexShrink = 0;
        firstPanel.Box.OverflowX = Overflow.Hidden;
        firstPanel.Box.OverflowY = Overflow.Hidden;
        firstPanel.ReplaceChildren(first);

        // Drag handle
        var handle = Box.New();
        if (_horizontal)
        {
            handle.Box.Width = Dimension.Px(5);
            handle.Inherited.Cursor = CursorType.ResizeEW;
        }
        else
        {
            handle.Box.Height = Dimension.Px(5);
            handle.Inherited.Cursor = CursorType.ResizeNS;
        }
        handle.BackgroundColor = GitClientColors.Border;

        handle.On<PointerEnterEvent>(_ => handle.BackgroundColor = GitClientColors.SelectionBorder);
        handle.On<PointerLeaveEvent>(_ =>
        {
            if (!_dragging) handle.BackgroundColor = GitClientColors.Border;
        });

        handle.On<PointerDownEvent>(_ =>
        {
            _dragging = true;
            handle.BackgroundColor = GitClientColors.SelectionBorder;
        });

        // Listen on the container for move/up so drag continues even when pointer leaves handle
        container.On<PointerMoveEvent>(e =>
        {
            var fp = _firstPanel.Current;
            var sp = _secondPanel.Current;
            if (!_dragging || fp == null || sp == null) return;

            var contentBox = container.Layout.AbsoluteContentBox;
            var containerSize = _horizontal ? contentBox.Width : contentBox.Height;
            if (containerSize <= 0) return;

            var localPos = _horizontal
                ? e.Position.X - contentBox.X
                : e.Position.Y - contentBox.Y;
            var ratio = (float)(localPos / containerSize);
            ratio = Math.Clamp(ratio, 0.15f, 0.85f);

            _splitRatio = ratio;
            fp.Flex.FlexBasis = Dimension.Percent(ratio * 100);
            sp.Flex.FlexBasis = Dimension.Percent((1 - ratio) * 100);

            // Direct style mutation doesn't invalidate layout — propagate the dirty
            // flag to root so the next frame's layout pass picks up the new basis.
            container.MarkLayoutDirty();
        });

        container.On<PointerUpEvent>(_ =>
        {
            _dragging = false;
            handle.BackgroundColor = GitClientColors.Border;
        });

        // Second panel
        var secondPanel = (BoxElement)Box.New().WithRef(_secondPanel);
        secondPanel.Flex.FlexGrow = 1;
        secondPanel.Flex.FlexShrink = 1;
        secondPanel.Box.OverflowX = Overflow.Hidden;
        secondPanel.Box.OverflowY = Overflow.Hidden;
        secondPanel.ReplaceChildren(second);

        container.ReplaceChildren(firstPanel, handle, secondPanel);
        return container;
    }
}
