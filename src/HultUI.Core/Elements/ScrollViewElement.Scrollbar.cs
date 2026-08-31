using HultUI.Core.Animation;
using HultUI.Core.Events;
using HultUI.Core.Primitives;

namespace HultUI.Core.Elements;

// Scrollbar drag interaction for ScrollViewElement
public partial class ScrollViewElement
{
    public bool IsDraggingScrollbar { get; private set; }

    private enum DragAxis { None, Vertical, Horizontal }
    private DragAxis _dragAxis;
    private float _dragGrabOffset; // offset from thumb top/left to click position

    // ──── Expand on hover ────

    private float _hoverT;                 // 0 at rest, 1 fully expanded
    private TweenHandle _hoverTween;
    private bool _scrollbarHovered;

    /// <summary>
    /// Whether the bar grows under the pointer. Windows 11 rests at a hairline and widens into a
    /// real bar with a track when you reach for it, which is how it stays out of the way without
    /// becoming impossible to grab.
    /// </summary>
    public bool ScrollbarExpandOnHover { get; set; } = true;

    /// <summary>
    /// Width of the whole bar while the pointer is on it. The thumb inside grows to half of it,
    /// the proportion Windows uses (a 12px bar around a 6px thumb).
    ///
    /// <para>It is also the width of the region that counts as "on the bar", at rest as well —
    /// otherwise a 2px resting bar would be a 2px target, and nothing would ever expand it.</para>
    /// </summary>
    public float ScrollbarExpandedWidth { get; set; } = 12f;

    /// <summary>Track colour while expanded. Null keeps <see cref="ScrollbarColor"/>, so a view
    /// that says nothing about hover just gets a wider version of the bar it already had.</summary>
    public Color? ScrollbarTrackHoverColor { get; set; }

    /// <summary>How long the expansion takes, in seconds.</summary>
    public float ScrollbarExpandDuration { get; set; } = 0.12f;

    /// <summary>The reach of the bar: what the pointer has to be within for it to expand, and
    /// what a press has to land in to count as a scrollbar press.</summary>
    internal float ScrollbarHitWidth =>
        ScrollbarExpandOnHover ? Math.Max(ScrollbarWidth, ScrollbarExpandedWidth) : ScrollbarWidth;

    /// <summary>The bar's drawn width right now, part-way through the expansion.</summary>
    internal float ScrollbarTrackWidthNow =>
        ScrollbarExpandOnHover
            ? ScrollbarWidth + (ScrollbarExpandedWidth - ScrollbarWidth) * _hoverT
            : ScrollbarWidth;

    /// <summary>The thumb's drawn width right now. Never narrower than at rest, so a view whose
    /// resting bar is already wide keeps it and only gains the track around it.</summary>
    internal float ScrollbarThumbWidthNow
    {
        get
        {
            if (!ScrollbarExpandOnHover) return ScrollbarWidth;
            float expanded = Math.Max(ScrollbarWidth, ScrollbarExpandedWidth / 2f);
            return ScrollbarWidth + (expanded - ScrollbarWidth) * _hoverT;
        }
    }

    internal Color ScrollbarTrackColorNow =>
        ScrollbarTrackHoverColor is { } hover
            ? Lerp.Color(ScrollbarColor, hover, _hoverT)
            : ScrollbarColor;

    /// <summary>
    /// Told by the window whether the pointer is on this bar. Kept as a method on
    /// <see cref="IScrollbarDraggable"/> rather than an ordinary pointer handler because the bar
    /// is painted over its own content: the element under the pointer there is whatever the bar
    /// happens to cover, so the hover has to be decided by geometry, which is what the window
    /// already does to pick the cursor.
    /// </summary>
    public void SetScrollbarHover(bool hovered)
    {
        if (hovered == _scrollbarHovered) return;
        _scrollbarHovered = hovered;

        if (!ScrollbarExpandOnHover)
            return;

        float target = hovered ? 1f : 0f;
        _hoverTween.Cancel();
        if (ScrollbarExpandDuration <= 0f)
        {
            _hoverT = target;
            MarkPaintDirty();
            return;
        }

        _hoverTween = Animate.Value(_hoverT, target, v => { _hoverT = v; MarkPaintDirty(); })
            .Duration(ScrollbarExpandDuration * MathF.Abs(target - _hoverT))
            .Ease(Easing.EaseOutCubic)
            .Start();
    }

    /// <summary>
    /// Tests if a pointer-down at the given position should start a scrollbar drag.
    /// Returns true if the click was on the scrollbar (consumed), false otherwise.
    /// </summary>
    public bool TryStartScrollbarDrag(Point position)
    {
        var contentBox = Layout.AbsoluteContentBox;

        // Test vertical scrollbar
        if (ShowScrollbarY && _contentHeight > contentBox.Height)
        {
            float trackX = contentBox.X + contentBox.Width - ScrollbarHitWidth;
            if (position.X >= trackX && position.X <= trackX + ScrollbarHitWidth &&
                position.Y >= contentBox.Y && position.Y <= contentBox.Y + contentBox.Height)
            {
                float trackHeight = contentBox.Height;
                float thumbRatio = contentBox.Height / _contentHeight;
                float thumbHeight = Math.Max(20, trackHeight * thumbRatio);
                float scrollRatio = _scrollOffsetY / Math.Max(1, _contentHeight - contentBox.Height);
                float thumbY = contentBox.Y + scrollRatio * (trackHeight - thumbHeight);

                if (position.Y >= thumbY && position.Y <= thumbY + thumbHeight)
                {
                    // Clicked on thumb — start drag
                    _dragAxis = DragAxis.Vertical;
                    _dragGrabOffset = position.Y - thumbY;
                    IsDraggingScrollbar = true;
                }
                else
                {
                    // Clicked on track — jump to position
                    float clickRatio = (position.Y - contentBox.Y - thumbHeight / 2) / (trackHeight - thumbHeight);
                    clickRatio = Math.Clamp(clickRatio, 0, 1);
                    ScrollOffsetY = clickRatio * (_contentHeight - contentBox.Height);
                    MarkPaintDirty();
                }
                return true;
            }
        }

        // Test horizontal scrollbar
        if (ShowScrollbarX && _contentWidth > contentBox.Width)
        {
            float trackY = contentBox.Y + contentBox.Height - ScrollbarHitWidth;
            if (position.Y >= trackY && position.Y <= trackY + ScrollbarHitWidth &&
                position.X >= contentBox.X && position.X <= contentBox.X + contentBox.Width)
            {
                float trackWidth = contentBox.Width;
                float thumbRatio = contentBox.Width / _contentWidth;
                float thumbWidth = Math.Max(20, trackWidth * thumbRatio);
                float scrollRatio = _scrollOffsetX / Math.Max(1, _contentWidth - contentBox.Width);
                float thumbX = contentBox.X + scrollRatio * (trackWidth - thumbWidth);

                if (position.X >= thumbX && position.X <= thumbX + thumbWidth)
                {
                    _dragAxis = DragAxis.Horizontal;
                    _dragGrabOffset = position.X - thumbX;
                    IsDraggingScrollbar = true;
                }
                else
                {
                    float clickRatio = (position.X - contentBox.X - thumbWidth / 2) / (trackWidth - thumbWidth);
                    clickRatio = Math.Clamp(clickRatio, 0, 1);
                    ScrollOffsetX = clickRatio * (_contentWidth - contentBox.Width);
                    MarkPaintDirty();
                }
                return true;
            }
        }

        return false;
    }

    /// <summary>True when the position (screen coords) falls on a visible scrollbar track.</summary>
    public bool IsPointOverScrollbar(Point position)
    {
        var contentBox = Layout.AbsoluteContentBox;
        if (ShowScrollbarY && _contentHeight > contentBox.Height)
        {
            float trackX = contentBox.X + contentBox.Width - ScrollbarHitWidth;
            if (position.X >= trackX && position.X <= trackX + ScrollbarHitWidth &&
                position.Y >= contentBox.Y && position.Y <= contentBox.Y + contentBox.Height)
                return true;
        }
        if (ShowScrollbarX && _contentWidth > contentBox.Width)
        {
            float trackY = contentBox.Y + contentBox.Height - ScrollbarHitWidth;
            if (position.Y >= trackY && position.Y <= trackY + ScrollbarHitWidth &&
                position.X >= contentBox.X && position.X <= contentBox.X + contentBox.Width)
                return true;
        }
        return false;
    }

    /// <summary>
    /// Called during a scrollbar drag to update scroll position based on pointer position.
    /// </summary>
    public void UpdateScrollbarDrag(Point position)
    {
        if (!IsDraggingScrollbar) return;

        var contentBox = Layout.AbsoluteContentBox;

        if (_dragAxis == DragAxis.Vertical)
        {
            float trackHeight = contentBox.Height;
            float thumbRatio = contentBox.Height / _contentHeight;
            float thumbHeight = Math.Max(20, trackHeight * thumbRatio);
            float thumbTop = position.Y - _dragGrabOffset;
            float scrollRatio = (thumbTop - contentBox.Y) / (trackHeight - thumbHeight);
            scrollRatio = Math.Clamp(scrollRatio, 0, 1);
            ScrollOffsetY = scrollRatio * (_contentHeight - contentBox.Height);
        }
        else if (_dragAxis == DragAxis.Horizontal)
        {
            float trackWidth = contentBox.Width;
            float thumbRatio = contentBox.Width / _contentWidth;
            float thumbWidth = Math.Max(20, trackWidth * thumbRatio);
            float thumbLeft = position.X - _dragGrabOffset;
            float scrollRatio = (thumbLeft - contentBox.X) / (trackWidth - thumbWidth);
            scrollRatio = Math.Clamp(scrollRatio, 0, 1);
            ScrollOffsetX = scrollRatio * (_contentWidth - contentBox.Width);
        }

        MarkPaintDirty();
    }

    /// <summary>
    /// Ends a scrollbar drag.
    /// </summary>
    public void EndScrollbarDrag()
    {
        IsDraggingScrollbar = false;
        _dragAxis = DragAxis.None;
    }
}
