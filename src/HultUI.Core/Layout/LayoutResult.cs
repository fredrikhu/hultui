using HultUI.Core.Primitives;

namespace HultUI.Core.Layout;

public readonly record struct LayoutResult(
    float X,
    float Y,
    float Width,
    float Height,
    Thickness Padding,
    Thickness Border,
    Thickness Margin)
{
    public Rect BorderBox => new(X, Y, Width, Height);
    public Rect PaddingBox => BorderBox.Deflate(Border);
    public Rect ContentBox => BorderBox.Deflate(Border, Padding);

    public float AbsoluteX { get; init; }
    public float AbsoluteY { get; init; }
    public Rect AbsoluteBorderBox => new(AbsoluteX, AbsoluteY, Width, Height);
    public Rect AbsoluteContentBox => AbsoluteBorderBox.Deflate(Border, Padding);
}
