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
            float trackX = contentBox.X + contentBox.Width - ScrollbarWidth;
            if (position.X >= trackX && position.X <= trackX + ScrollbarWidth &&
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
            float trackY = contentBox.Y + contentBox.Height - ScrollbarWidth;
            if (position.Y >= trackY && position.Y <= trackY + ScrollbarWidth &&
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
            float trackX = contentBox.X + contentBox.Width - ScrollbarWidth;
            if (position.X >= trackX && position.X <= trackX + ScrollbarWidth &&
                position.Y >= contentBox.Y && position.Y <= contentBox.Y + contentBox.Height)
                return true;
        }
        if (ShowScrollbarX && _contentWidth > contentBox.Width)
        {
            float trackY = contentBox.Y + contentBox.Height - ScrollbarWidth;
            if (position.Y >= trackY && position.Y <= trackY + ScrollbarWidth &&
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
