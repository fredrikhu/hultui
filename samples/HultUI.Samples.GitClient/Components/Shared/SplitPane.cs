using HultUI.Core.Builders;
using HultUI.Core.Elements;
using HultUI.Core.Events;
using HultUI.Core.Layout;
using HultUI.Core.Primitives;
using HultUI.Core.Styling;
using GitClient.Theme;

namespace GitClient.Components.Shared;

/// <summary>
/// A resizable split pane. Supports horizontal (side-by-side) and vertical (top-bottom) splits.
/// Uses direct mutation for smooth drag performance.
///
/// Handler closures are wired once (on the first Build) and capture first-Build
/// locals + stable fields.  PatchFrom keeps the first Build's handlers on the
/// live elements across rebuilds; subsequent Builds produce fresh, handler-free
/// elements whose styles are patched into the live tree.
/// </summary>
public class SplitPane
{
    private readonly bool _horizontal;
    private float _splitRatio;
    private bool _dragging;

    // Set once on the first Build — always point to the live elements.
    private BoxElement? _firstPanel;
    private BoxElement? _secondPanel;
    private bool _handlersWired;

    public SplitPane(bool horizontal = true, float initialRatio = 0.5f)
    {
        _horizontal = horizontal;
        _splitRatio = initialRatio;
    }

    public Element Build(Element first, Element second)
    {
        var direction = _horizontal ? FlexDirection.Row : FlexDirection.Column;
        var container = Box.New();
        container.Flex.Direction = direction;
        container.Flex.FlexGrow = 1;

        // First panel
        var firstPanel = Box.New();
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

        // Second panel
        var secondPanel = Box.New();
        secondPanel.Flex.FlexGrow = 1;
        secondPanel.Flex.FlexShrink = 1;
        secondPanel.Box.OverflowX = Overflow.Hidden;
        secondPanel.Box.OverflowY = Overflow.Hidden;
        secondPanel.ReplaceChildren(second);

        // Only store live-element references and wire handlers on the first
        // Build.  PatchFrom keeps the first Build's handlers on the live
        // elements; the closures capture first-Build locals (container, handle)
        // which ARE the live elements, and read fields (_firstPanel,
        // _secondPanel) set once below via ??=.
        _firstPanel ??= firstPanel;
        _secondPanel ??= secondPanel;

        if (!_handlersWired)
        {
            _handlersWired = true;

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
                if (!_dragging || _firstPanel == null || _secondPanel == null) return;

                var contentBox = container.Layout.AbsoluteContentBox;
                var containerSize = _horizontal ? contentBox.Width : contentBox.Height;
                if (containerSize <= 0) return;

                var localPos = _horizontal
                    ? e.Position.X - contentBox.X
                    : e.Position.Y - contentBox.Y;
                var ratio = (float)(localPos / containerSize);
                ratio = Math.Clamp(ratio, 0.15f, 0.85f);

                _splitRatio = ratio;
                _firstPanel.Flex.FlexBasis = Dimension.Percent(ratio * 100);
                _secondPanel.Flex.FlexBasis = Dimension.Percent((1 - ratio) * 100);

                // Direct style mutation doesn't invalidate layout — propagate the dirty
                // flag to root so the next frame's layout pass picks up the new basis.
                container.MarkLayoutDirty();
            });

            container.On<PointerUpEvent>(_ =>
            {
                _dragging = false;
                handle.BackgroundColor = GitClientColors.Border;
            });
        }

        container.ReplaceChildren(firstPanel, handle, secondPanel);
        return container;
    }
}
