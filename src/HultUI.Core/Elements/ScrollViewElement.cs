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

    /// <summary>
    /// Whether a vertical wheel scrolls this view sideways when it cannot use the wheel
    /// vertically itself — the way a list in Windows' list mode moves through its columns.
    /// On for <see cref="Builders.ScrollView.Horizontal"/>: without it a mouse with no tilt
    /// wheel can only reach the rest of a horizontal view with Shift held.
    ///
    /// <para>It defers to the page. While a scroll view around this one can still move the
    /// way the wheel turned, the wheel is left to it, so a horizontal strip inside a scrolling
    /// page does not trap the wheel on its way down the page. Only when nothing around can — a
    /// strip that fills its pane, or a page already at its end — does the strip take it.</para>
    /// </summary>
    public bool VerticalWheelScrollsSideways { get; set; }

    /// <summary>
    /// Who really scrolls this view's content sideways, when it is not the view itself.
    ///
    /// <para>A virtualized list pins its content width to the viewport, so its own
    /// <see cref="ScrollOffsetX"/> never leaves zero and an app that pans a wide list keeps
    /// the position elsewhere (<see cref="Builders.HorizontalPan"/>). Set this to that, and
    /// the wheel's sideways motion and the middle-click autoscroll's go there — otherwise a
    /// reader swiping sideways, or aiming right of the anchor, over a list with a horizontal
    /// scrollbar plainly under it, gets nothing. Null means the view scrolls sideways with its
    /// own offset, as a plain one does.</para>
    /// </summary>
    public ISidewaysScroller? SidewaysScroller { get; set; }

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

    /// <summary>
    /// Takes the axes it can move on and leaves the rest to the scroll views around it — see
    /// <see cref="ScrollEvent.Consume"/>. It used to stop the event outright, which cost twice:
    /// a list at the end of its range kept the wheel from the page around it, and an app's own
    /// wheel handler on the list never ran, since stopping also ends the element's remaining
    /// handlers.
    /// </summary>
    private void OnScroll(ScrollEvent evt)
    {
        if (!evt.IsFor(this)) return;
        float dx = evt.RemainingDeltaX * ScrollSpeed;
        float dy = evt.RemainingDeltaY * ScrollSpeed;
        if (dx == 0 && dy == 0) return;

        float beforeY = _scrollOffsetY;
        if (dy != 0) ScrollOffsetY = beforeY - dy;
        bool movedY = _scrollOffsetY != beforeY;

        // A vertical wheel this view had no use for goes sideways, when it is that kind of
        // view and nothing around it wants the wheel vertically. The motion it then uses is
        // the vertical one, so that is the axis reported used.
        if (dy != 0 && !movedY && dx == 0 && VerticalWheelScrollsSideways && !AncestorCanScrollVertically(dy))
        {
            movedY = ScrollSideways(dy);
            dy = 0;
        }

        // Sideways goes to whoever really scrolls it, as the autoscroll's does (see ScrollBy),
        // and last, for the same reason: a pan rebuilds its owner from inside the call.
        bool movedX = dx != 0 && ScrollSideways(dx);

        evt.Consume(this, movedX, movedY);
    }

    /// <summary>Moves the content sideways by a wheel's <paramref name="delta"/> (positive is
    /// back towards the start, as a wheel turned up is), through <see cref="SidewaysScroller"/>
    /// when one is set. True when something moved.</summary>
    private bool ScrollSideways(float delta)
    {
        if (SidewaysScroller is { } sideways)
            return sideways.ScrollSidewaysBy(-delta);
        float before = _scrollOffsetX;
        ScrollOffsetX = before - delta;
        return _scrollOffsetX != before;
    }

    /// <summary>Whether a scroll view around this one could move vertically by a wheel's
    /// <paramref name="delta"/> — in which case the vertical wheel is that view's, not ours to
    /// turn sideways.</summary>
    private bool AncestorCanScrollVertically(float delta)
    {
        for (var e = Parent; e is not null; e = e.Parent)
            if (e is ScrollViewElement sv && sv.ClampY(sv._scrollOffsetY - delta) != sv._scrollOffsetY)
                return true;
        return false;
    }

    /// <summary>
    /// How far this view can scroll, from the children's laid-out boxes. Called by the layout
    /// pass, for every scroll view in the tree, whether or not it is on screen.
    ///
    /// <para>This used to be computed by the renderer, which is the pass that <i>culls</i>:
    /// anything falling outside a clipping ancestor is skipped, so a scroll view that had not
    /// been painted reported a content size of zero. That zero is not inert, because the offset
    /// setters clamp against it — setting <see cref="ScrollOffsetY"/> on such a view silently
    /// did nothing, and silent failure is the worse of the two ways to be wrong. A vertical
    /// scroll view nested inside another vertical one hit it every time: whatever sits above it
    /// in the outer view is usually tall enough to push it outside the outer's clip on the
    /// first frame, so it arrived permanently unable to scroll.</para>
    /// </summary>
    internal virtual void UpdateScrollExtent()
    {
        float maxRight = 0;
        float maxBottom = 0;

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
        ApplyPendingReveal();
    }

    /// <summary>
    /// The render-time hook. Separate from <see cref="UpdateScrollExtent"/> because
    /// <see cref="VirtualListElement{T}"/> also realizes and lays out its visible rows here, and
    /// that cannot happen from inside the layout pass it would re-enter.
    /// </summary>
    internal virtual void UpdateContentSize() => UpdateScrollExtent();

    protected float ClampX(float value)
    {
        var viewportWidth = Layout.AbsoluteContentBox.Width;
        float maxScroll = Math.Max(0, _contentWidth - viewportWidth);
        return Math.Clamp(value, 0, maxScroll);
    }

    protected virtual float ClampY(float value)
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
            if (_contentHeight > box.Height + 0.5f) return true;
            return SidewaysScroller is { } sideways
                ? sideways.CanScrollSideways
                : _contentWidth > box.Width + 0.5f;
        }
    }

    /// <summary>Moves the view by a scroll delta — positive is further down and further
    /// right through the content, the same sense as <see cref="ScrollOffsetY"/>. The
    /// horizontal part goes to the <see cref="SidewaysScroller"/> when there is one.</summary>
    /// <returns>True when something actually moved, so a caller can tell a scroll that ran
    /// from one already pinned against its end.</returns>
    public bool ScrollBy(float dx, float dy)
    {
        float beforeX = _scrollOffsetX, beforeY = _scrollOffsetY;
        ScrollOffsetY = beforeY + dy;
        bool moved = _scrollOffsetY != beforeY;
        // The delegate goes last: a pan is app state, and moving it rebuilds the owner from
        // inside this call. Writing this view's own offset after that would be writing it on
        // whatever the rebuild left behind, so all of that is done before the rebuild runs.
        if (SidewaysScroller is { } sideways)
        {
            if (dx != 0 && sideways.ScrollSidewaysBy(dx)) moved = true;
            return moved;
        }
        ScrollOffsetX = beforeX + dx;
        return moved || _scrollOffsetX != beforeX;
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
        if (viewportHeight <= 0)
        {
            // Asked to reveal a band before this view has been laid out — which is exactly
            // where a component naturally asks: OnAfterBuild, right after the rebuild that
            // put the row there and before the layout pass that measures it. "Scroll the
            // minimum distance" has no answer without a viewport, and answering anyway
            // parks the band a viewport off. Hold the request; the layout pass honours it
            // the moment the geometry exists.
            _pendingReveal = (top, height);
            return;
        }
        _pendingReveal = null;
        float cur = _scrollOffsetY;
        float bottom = top + height;
        // Layout is unscrolled, so the band's coordinates don't move as we scroll;
        // only the [cur, cur + viewportHeight] window does. Reveal the nearer edge.
        // (The setter clamps, so an over-shoot near the ends is corrected.)
        if (top < cur) ScrollOffsetY = top;
        else if (bottom > cur + viewportHeight) ScrollOffsetY = bottom - viewportHeight;

        // A sticky header held at the top would now cover the band's top — a row revealed by
        // arrowing up would land under its own section's header. Scroll back by as much.
        float covered = StuckHeaderReach() - (top - _scrollOffsetY);
        if (covered > 0.5f) ScrollOffsetY = _scrollOffsetY - covered;
    }

    /// <summary>
    /// How far below the viewport's top edge the sticky elements held against it reach, at the
    /// current offset; 0 when none is held. A shallow walk — sections are a level or two down —
    /// run on a reveal, never per frame; nested scroll views hold their own.
    /// </summary>
    private float StuckHeaderReach()
    {
        float viewTop = Layout.AbsoluteContentBox.Y + _scrollOffsetY;
        float reach = 0f;
        Walk(this, 0);
        return reach;

        void Walk(Element element, int depth)
        {
            var children = element.Children;
            for (int i = 0; i < children.Count; i++)
            {
                var child = children[i];
                if (child is ScrollViewElement) continue;
                var pos = child.PositionRead;
                if (pos.Type == HultUI.Core.Layout.PositionType.Sticky && !pos.Top.IsNone && child.StickyShift.Y > 0f)
                    reach = MathF.Max(reach, child.Layout.AbsoluteBorderBox.Bottom + child.StickyShift.Y - viewTop);
                if (depth < 3) Walk(child, depth + 1);
            }
        }
    }

    /// <summary>A reveal that arrived before this view knew its own size, waiting for the
    /// layout pass that gives it one.</summary>
    private (float Top, float Height)? _pendingReveal;

    /// <summary>
    /// Honours a held <see cref="ScrollRangeIntoView"/> now that the geometry is known.
    /// Called from the layout pass, so it writes the offset field rather than the property:
    /// the property marks paint dirty and notifies accessibility, neither of which belongs
    /// in the middle of a layout.
    /// </summary>
    protected void ApplyPendingReveal()
    {
        if (_pendingReveal is not { } band) return;
        float viewportHeight = Layout.AbsoluteContentBox.Height;
        if (viewportHeight <= 0) return;   // still nothing to measure against; keep holding it

        _pendingReveal = null;
        float bottom = band.Top + band.Height;
        float target = band.Top < _scrollOffsetY ? band.Top
            : bottom > _scrollOffsetY + viewportHeight ? bottom - viewportHeight
            : _scrollOffsetY;
        _scrollOffsetY = ClampY(target);
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

    /// <summary>The box's, while the scrollbars fit inside it; unbounded when one does not.</summary>
    public override Rect? PaintBounds(IRenderContext ctx)
    {
        var contentBox = Layout.AbsoluteContentBox;
        if ((ShowScrollbarY && !_vBar.PaintsInside(contentBox, vertical: true))
            || (ShowScrollbarX && !_hBar.PaintsInside(contentBox, vertical: false)))
            return null;
        return base.PaintBounds(ctx);
    }

    public void RenderScrollbars(IRenderContext ctx)
    {
        var contentBox = Layout.AbsoluteContentBox;

        if (ShowScrollbarY)
            _vBar.Paint(ctx, contentBox, _contentHeight, _scrollOffsetY, vertical: true);
        if (ShowScrollbarX)
            _hBar.Paint(ctx, contentBox, _contentWidth, _scrollOffsetX, vertical: false);
    }

    // What it copies below is scrolling behaviour and the scrollbars' looks; layout reads none of it.
    private protected override bool PatchMarksOwnLayout => GetType() == typeof(ScrollViewElement);

    internal override void PatchFrom(Element source)
    {
        base.PatchFrom(source);
        if (source is ScrollViewElement sv)
        {
            ScrollSpeed = sv.ScrollSpeed;
            MiddleClickAutoScroll = sv.MiddleClickAutoScroll;
            VerticalWheelScrollsSideways = sv.VerticalWheelScrollsSideways;
            SidewaysScroller = sv.SidewaysScroller;
            ShowScrollbarX = sv.ShowScrollbarX;
            ShowScrollbarY = sv.ShowScrollbarY;
            CopyScrollbarsFrom(sv);
            // Preserve scroll offset — don't copy from incoming
        }
    }
}
