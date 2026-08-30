using HultUI.Core.Accessibility;
using HultUI.Core.Events;
using HultUI.Core.Layout;
using HultUI.Core.Primitives;
using HultUI.Core.Rendering;

namespace HultUI.Core.Elements;

public partial class ScrollViewElement : BoxElement, IScrollbarDraggable, IAutoScrollable
{
    protected float _scrollOffsetX;
    protected float _scrollOffsetY;
    protected float _contentWidth;
    protected float _contentHeight;

    public float ScrollOffsetX
    {
        get => _scrollOffsetX;
        set
        {
            ThrowIfDetached(nameof(ScrollOffsetX));
            float clamped = ClampX(value);
            if (clamped == _scrollOffsetX) return;
            _scrollOffsetX = clamped;
            MarkPaintDirty();
            // Children's on-screen positions shifted, so every descendant node's
            // accessibility bounds changed — refresh the a11y tree.
            NotifyAccessibilityPropertyChanged(AccessibilityProperties.BoundingRect);
        }
    }

    public float ScrollOffsetY
    {
        get => _scrollOffsetY;
        set
        {
            // Scrolling a discarded element is the shape this guard exists for: an overlay
            // that captured the list during a build keeps setting an offset nothing reads.
            ThrowIfDetached(nameof(ScrollOffsetY));
            float clamped = ClampY(value);
            if (clamped == _scrollOffsetY) return;
            _scrollOffsetY = clamped;
            MarkPaintDirty();
            NotifyAccessibilityPropertyChanged(AccessibilityProperties.BoundingRect);
        }
    }

    public float ContentWidth => _contentWidth;
    public float ContentHeight => _contentHeight;

    public float ScrollSpeed { get; set; } = 40f;

    public bool ShowScrollbarX { get; set; }
    public bool ShowScrollbarY { get; set; } = true;

    /// <summary>
    /// Windows-style autoscroll: holding the middle button anchors an origin, and moving the
    /// pointer away from it scrolls continuously in that direction — faster the further out,
    /// stopping when the pointer comes back inside the dead zone or the button is released.
    /// On by default, because in a list the middle button otherwise does nothing at all — set
    /// it false where the middle button already means something (a tab strip that closes tabs
    /// with it, say).
    ///
    /// <para>Autoscroll is only claimed when this view can actually scroll, so a middle press
    /// in a short list falls through to the app's own handlers untouched.</para>
    /// </summary>
    public bool MiddleClickAutoScroll { get; set; } = true;

    public Color ScrollbarColor { get; set; } = new(128, 128, 128, 100);
    public Color ScrollbarThumbColor { get; set; } = new(128, 128, 128, 180);
    public float ScrollbarWidth { get; set; } = 6f;

    public ScrollViewElement()
    {
        // Default: vertical scroll. OverflowY=Scroll gives children unlimited
        // vertical space in layout. OverflowX=Hidden clips horizontally.
        Box.OverflowX = Overflow.Hidden;
        Box.OverflowY = Overflow.Scroll;

        OnOwn<ScrollEvent>(OnScroll);
    }

    public new static ScrollViewElement New([OriginFile] string? file = null, [OriginLine] int line = 0) =>
        new ScrollViewElement().WithOrigin(file, line);

    private void OnScroll(ScrollEvent evt)
    {
        ScrollOffsetX -= evt.DeltaX * ScrollSpeed;
        ScrollOffsetY -= evt.DeltaY * ScrollSpeed;
        evt.StopPropagation = true;
        MarkPaintDirty();
    }

    internal virtual void UpdateContentSize()
    {
        float maxRight = 0;
        float maxBottom = 0;
        var contentBox = Layout.AbsoluteContentBox;

        foreach (var child in Children)
        {
            var childBox = child.Layout;
            float childRight = childBox.X + childBox.Width + childBox.Margin.Right;
            float childBottom = childBox.Y + childBox.Height + childBox.Margin.Bottom;
            maxRight = Math.Max(maxRight, childRight);
            maxBottom = Math.Max(maxBottom, childBottom);
        }

        _contentWidth = maxRight;
        _contentHeight = maxBottom;

        // Re-clamp offsets after content size change
        _scrollOffsetX = ClampX(_scrollOffsetX);
        _scrollOffsetY = ClampY(_scrollOffsetY);
    }

    protected float ClampX(float value)
    {
        var viewportWidth = Layout.AbsoluteContentBox.Width;
        float maxScroll = Math.Max(0, _contentWidth - viewportWidth);
        return Math.Clamp(value, 0, maxScroll);
    }

    protected float ClampY(float value)
    {
        var viewportHeight = Layout.AbsoluteContentBox.Height;
        float maxScroll = Math.Max(0, _contentHeight - viewportHeight);
        return Math.Clamp(value, 0, maxScroll);
    }

    // ──── Autoscroll ────

    /// <summary>Whether either axis has anywhere left to go — what decides if a middle
    /// press starts an autoscroll or is just a middle press.</summary>
    public bool CanAutoScroll
    {
        get
        {
            var box = Layout.AbsoluteContentBox;
            return _contentHeight > box.Height + 0.5f || _contentWidth > box.Width + 0.5f;
        }
    }

    /// <summary>Moves the view by a scroll delta — positive is further down and further
    /// right through the content, the same sense as <see cref="ScrollOffsetY"/>.</summary>
    /// <returns>True when something actually moved, so a caller can tell a scroll that ran
    /// from one already pinned against its end.</returns>
    public bool ScrollBy(float dx, float dy)
    {
        float beforeX = _scrollOffsetX, beforeY = _scrollOffsetY;
        ScrollOffsetX = beforeX + dx;
        ScrollOffsetY = beforeY + dy;
        return _scrollOffsetX != beforeX || _scrollOffsetY != beforeY;
    }

    // ──── Scroll into view ────

    /// <summary>
    /// Scrolls the minimum distance needed to bring the vertical band
    /// <c>[top, top + height]</c> fully into the viewport, and no further. A no-op
    /// when the band is already visible. Coordinates are in this scroll's own
    /// content space — the unscrolled space layout positions live in, where 0 is
    /// the top of the content — so callers with a fixed row height can pass
    /// <c>index * rowHeight</c> without materialising the row element.
    /// </summary>
    public void ScrollRangeIntoView(float top, float height)
    {
        var viewportHeight = Layout.AbsoluteContentBox.Height;
        float cur = _scrollOffsetY;
        float bottom = top + height;
        // Layout is unscrolled, so the band's coordinates don't move as we scroll;
        // only the [cur, cur + viewportHeight] window does. Reveal the nearer edge.
        // (The setter clamps, so an over-shoot near the ends is corrected.)
        if (top < cur) ScrollOffsetY = top;
        else if (bottom > cur + viewportHeight) ScrollOffsetY = bottom - viewportHeight;
    }

    /// <summary>
    /// Horizontal counterpart of <see cref="ScrollRangeIntoView"/>: scrolls the
    /// minimum distance needed to bring the horizontal band <c>[left, left + width]</c>
    /// fully into the viewport. Coordinates are in this scroll's own content space
    /// (0 is the left edge of the content).
    /// </summary>
    public void ScrollRangeIntoViewHorizontal(float left, float width)
    {
        var viewportWidth = Layout.AbsoluteContentBox.Width;
        float cur = _scrollOffsetX;
        float right = left + width;
        if (left < cur) ScrollOffsetX = left;
        else if (right > cur + viewportWidth) ScrollOffsetX = right - viewportWidth;
    }

    /// <summary>
    /// Scrolls <paramref name="child"/> — a descendant laid out inside this view —
    /// fully into vertical view. Reads the child's laid-out box, so it handles
    /// variable-height rows; call it after the frame the child was laid out in.
    /// </summary>
    public void ScrollIntoView(Element child)
    {
        // Layout positions are unscrolled (the renderer applies ScrollOffsetY as a
        // render-time translation), so the child's gap from the viewport top is
        // already its offset in content space — no ScrollOffsetY term is added.
        var box = child.Layout.AbsoluteBorderBox;
        ScrollRangeIntoView(box.Y - Layout.AbsoluteContentBox.Y, box.Height);
    }

    public override void Render(IRenderContext ctx)
    {
        base.Render(ctx);
    }

    public void RenderScrollbars(IRenderContext ctx)
    {
        var contentBox = Layout.AbsoluteContentBox;

        // Vertical scrollbar
        if (ShowScrollbarY && _contentHeight > contentBox.Height)
        {
            float trackHeight = contentBox.Height;
            float thumbRatio = contentBox.Height / _contentHeight;
            float thumbHeight = Math.Max(20, trackHeight * thumbRatio);
            float scrollRatio = _scrollOffsetY / Math.Max(1, _contentHeight - contentBox.Height);
            float thumbY = contentBox.Y + scrollRatio * (trackHeight - thumbHeight);
            float trackX = contentBox.X + contentBox.Width - ScrollbarWidth;

            // Track
            ctx.DrawRect(
                new Rect(trackX, contentBox.Y, ScrollbarWidth, trackHeight),
                ScrollbarColor);

            // Thumb
            ctx.DrawRect(
                new Rect(trackX, thumbY, ScrollbarWidth, thumbHeight),
                ScrollbarThumbColor);
        }

        // Horizontal scrollbar
        if (ShowScrollbarX && _contentWidth > contentBox.Width)
        {
            float trackWidth = contentBox.Width;
            float thumbRatio = contentBox.Width / _contentWidth;
            float thumbWidth = Math.Max(20, trackWidth * thumbRatio);
            float scrollRatio = _scrollOffsetX / Math.Max(1, _contentWidth - contentBox.Width);
            float thumbX = contentBox.X + scrollRatio * (trackWidth - thumbWidth);
            float trackY = contentBox.Y + contentBox.Height - ScrollbarWidth;

            ctx.DrawRect(
                new Rect(contentBox.X, trackY, trackWidth, ScrollbarWidth),
                ScrollbarColor);

            ctx.DrawRect(
                new Rect(thumbX, trackY, thumbWidth, ScrollbarWidth),
                ScrollbarThumbColor);
        }
    }

    internal override void PatchFrom(Element source)
    {
        base.PatchFrom(source);
        if (source is ScrollViewElement sv)
        {
            ScrollSpeed = sv.ScrollSpeed;
            MiddleClickAutoScroll = sv.MiddleClickAutoScroll;
            ShowScrollbarX = sv.ShowScrollbarX;
            ShowScrollbarY = sv.ShowScrollbarY;
            ScrollbarColor = sv.ScrollbarColor;
            ScrollbarThumbColor = sv.ScrollbarThumbColor;
            ScrollbarWidth = sv.ScrollbarWidth;
            // Preserve scroll offset — don't copy from incoming
        }
    }
}
