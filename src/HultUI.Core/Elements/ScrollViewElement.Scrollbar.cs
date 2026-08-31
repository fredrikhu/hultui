using HultUI.Core.Primitives;

namespace HultUI.Core.Elements;

// Scrollbar appearance and drag interaction for ScrollViewElement.
//
// The arithmetic and the painting live in Scrollbar, shared with TextAreaElement. What is here
// is what only a scroll view knows: that it has two axes, and where its content size comes from.
public partial class ScrollViewElement
{
    // One per axis, so pointing at the vertical bar expands the vertical bar and leaves the
    // horizontal one where it was.
    private readonly Scrollbar _vBar = new();
    private readonly Scrollbar _hBar = new();

    public bool IsDraggingScrollbar { get; private set; }

    private enum DragAxis { None, Vertical, Horizontal }
    private DragAxis _dragAxis;
    private float _dragGrabOffset;

    // ──── Appearance ────
    //
    // Facades over both bars: apps set these per element (Junction and Vesper both do), and a
    // scroll view's two bars have never looked different from one another. Reads answer for the
    // vertical one, which is the one that exists in almost every view.

    public Color ScrollbarColor
    {
        get => _vBar.TrackColor;
        set { _vBar.TrackColor = value; _hBar.TrackColor = value; }
    }

    public Color ScrollbarThumbColor
    {
        get => _vBar.ThumbColor;
        set { _vBar.ThumbColor = value; _hBar.ThumbColor = value; }
    }

    public float ScrollbarWidth
    {
        get => _vBar.Width;
        set { _vBar.Width = value; _hBar.Width = value; }
    }

    /// <summary>
    /// Corner radius of the track and thumb. Defaults to half the width, which makes the thumb
    /// a pill — every current platform draws it that way, and a square-ended bar is the single
    /// most dated-looking thing in an otherwise modern window. Set 0 for square ends.
    /// </summary>
    public float ScrollbarRadius
    {
        get => _vBar.Radius;
        set { _vBar.Radius = value; _hBar.Radius = value; }
    }

    /// <summary>
    /// Whether the bar grows under the pointer. Windows 11 rests at a hairline and widens into a
    /// real bar with a track when you reach for it, which is how it stays out of the way without
    /// becoming impossible to grab.
    /// </summary>
    public bool ScrollbarExpandOnHover
    {
        get => _vBar.ExpandOnHover;
        set { _vBar.ExpandOnHover = value; _hBar.ExpandOnHover = value; }
    }

    /// <summary>
    /// Width of the whole bar while the pointer is on it; the thumb inside grows to half of it.
    ///
    /// <para>It is also the width of the region that counts as "on the bar", at rest as well —
    /// otherwise a 2px resting bar would be a 2px target, and nothing would ever expand it.</para>
    /// </summary>
    public float ScrollbarExpandedWidth
    {
        get => _vBar.ExpandedWidth;
        set { _vBar.ExpandedWidth = value; _hBar.ExpandedWidth = value; }
    }

    /// <summary>Track colour while expanded. Null keeps <see cref="ScrollbarColor"/>, so a view
    /// that says nothing about hover just gets a wider version of the bar it already had.</summary>
    public Color? ScrollbarTrackHoverColor
    {
        get => _vBar.TrackHoverColor;
        set { _vBar.TrackHoverColor = value; _hBar.TrackHoverColor = value; }
    }

    /// <summary>How long the expansion takes, in seconds.</summary>
    public float ScrollbarExpandDuration
    {
        get => _vBar.ExpandDuration;
        set { _vBar.ExpandDuration = value; _hBar.ExpandDuration = value; }
    }

    // ──── Hover ────

    /// <summary>
    /// Told by the window where the pointer is, or null when it is not on this view's bars.
    ///
    /// <para>The position rather than a bool, because a scroll view has two bars and only the
    /// one under the pointer should expand. See <see cref="IScrollbarDraggable"/> for why the
    /// window has to deliver this at all.</para>
    /// </summary>
    public void SetScrollbarHover(Point? position)
    {
        var box = Layout.AbsoluteContentBox;
        bool onVertical = ShowScrollbarY && position is { } p
            && _vBar.Contains(box, _contentHeight, p, vertical: true);
        bool onHorizontal = ShowScrollbarX && position is { } q
            && _hBar.Contains(box, _contentWidth, q, vertical: false);

        _vBar.SetHover(onVertical, this);
        _hBar.SetHover(onHorizontal, this);
    }

    // ──── Press, drag, release ────

    /// <summary>
    /// Tests if a pointer-down at the given position should start a scrollbar drag.
    /// Returns true if the click was on the scrollbar (consumed), false otherwise.
    /// </summary>
    public bool TryStartScrollbarDrag(Point position)
    {
        var box = Layout.AbsoluteContentBox;

        if (ShowScrollbarY)
        {
            var hit = _vBar.HitTest(box, _contentHeight, _scrollOffsetY, position, vertical: true,
                out float grab, out float trackOffset);
            if (hit != ScrollbarHit.None)
            {
                if (hit == ScrollbarHit.Thumb)
                {
                    _dragAxis = DragAxis.Vertical;
                    _dragGrabOffset = grab;
                    IsDraggingScrollbar = true;
                }
                else
                {
                    ScrollOffsetY = trackOffset;
                    MarkPaintDirty();
                }
                return true;
            }
        }

        if (ShowScrollbarX)
        {
            var hit = _hBar.HitTest(box, _contentWidth, _scrollOffsetX, position, vertical: false,
                out float grab, out float trackOffset);
            if (hit != ScrollbarHit.None)
            {
                if (hit == ScrollbarHit.Thumb)
                {
                    _dragAxis = DragAxis.Horizontal;
                    _dragGrabOffset = grab;
                    IsDraggingScrollbar = true;
                }
                else
                {
                    ScrollOffsetX = trackOffset;
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
        var box = Layout.AbsoluteContentBox;
        return (ShowScrollbarY && _vBar.Contains(box, _contentHeight, position, vertical: true))
            || (ShowScrollbarX && _hBar.Contains(box, _contentWidth, position, vertical: false));
    }

    /// <summary>
    /// Called during a scrollbar drag to update scroll position based on pointer position.
    /// </summary>
    public void UpdateScrollbarDrag(Point position)
    {
        if (!IsDraggingScrollbar) return;
        var box = Layout.AbsoluteContentBox;

        if (_dragAxis == DragAxis.Vertical)
            ScrollOffsetY = _vBar.OffsetFromDrag(box, _contentHeight, position, _dragGrabOffset, vertical: true);
        else if (_dragAxis == DragAxis.Horizontal)
            ScrollOffsetX = _hBar.OffsetFromDrag(box, _contentWidth, position, _dragGrabOffset, vertical: false);

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

    internal void CopyScrollbarsFrom(ScrollViewElement source)
    {
        _vBar.CopyFrom(source._vBar);
        _hBar.CopyFrom(source._hBar);
    }
}
