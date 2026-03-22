using HultUI.Core.Events;
using HultUI.Core.Layout;
using HultUI.Core.Primitives;
using HultUI.Core.Rendering;

namespace HultUI.Core.Elements;

public class ScrollViewElement : BoxElement
{
    private float _scrollOffsetX;
    private float _scrollOffsetY;
    private float _contentWidth;
    private float _contentHeight;

    public float ScrollOffsetX
    {
        get => _scrollOffsetX;
        set => _scrollOffsetX = ClampX(value);
    }

    public float ScrollOffsetY
    {
        get => _scrollOffsetY;
        set => _scrollOffsetY = ClampY(value);
    }

    public float ContentWidth => _contentWidth;
    public float ContentHeight => _contentHeight;

    public float ScrollSpeed { get; set; } = 40f;

    public bool ShowScrollbarX { get; set; }
    public bool ShowScrollbarY { get; set; } = true;

    public Color ScrollbarColor { get; set; } = new(128, 128, 128, 100);
    public Color ScrollbarThumbColor { get; set; } = new(128, 128, 128, 180);
    public float ScrollbarWidth { get; set; } = 6f;

    public ScrollViewElement()
    {
        // Default: vertical scroll. OverflowY=Scroll gives children unlimited
        // vertical space in layout. OverflowX=Hidden clips horizontally.
        Box.OverflowX = Overflow.Hidden;
        Box.OverflowY = Overflow.Scroll;

        On<ScrollEvent>(OnScroll);
    }

    public new static ScrollViewElement New() => new();

    private void OnScroll(ScrollEvent evt)
    {
        ScrollOffsetX -= evt.DeltaX * ScrollSpeed;
        ScrollOffsetY -= evt.DeltaY * ScrollSpeed;
        evt.StopPropagation = true;
        MarkPaintDirty();
    }

    internal void UpdateContentSize()
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

    private float ClampX(float value)
    {
        var viewportWidth = Layout.AbsoluteContentBox.Width;
        float maxScroll = Math.Max(0, _contentWidth - viewportWidth);
        return Math.Clamp(value, 0, maxScroll);
    }

    private float ClampY(float value)
    {
        var viewportHeight = Layout.AbsoluteContentBox.Height;
        float maxScroll = Math.Max(0, _contentHeight - viewportHeight);
        return Math.Clamp(value, 0, maxScroll);
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
            ShowScrollbarX = sv.ShowScrollbarX;
            ShowScrollbarY = sv.ShowScrollbarY;
            ScrollbarColor = sv.ScrollbarColor;
            ScrollbarThumbColor = sv.ScrollbarThumbColor;
            ScrollbarWidth = sv.ScrollbarWidth;
            // Preserve scroll offset — don't copy from incoming
        }
    }
}
