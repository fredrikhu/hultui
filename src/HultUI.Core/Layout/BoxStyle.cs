using HultUI.Core.Primitives;

namespace HultUI.Core.Layout;

public sealed class BoxStyle
{
    /// <summary>Shared all-defaults instance for elements that never set box
    /// properties. Read-only by contract — never mutate.</summary>
    internal static readonly BoxStyle Default = new();

    public Dimension Width { get; set; } = Dimension.Auto;
    public Dimension Height { get; set; } = Dimension.Auto;
    public Dimension MinWidth { get; set; } = Dimension.None;
    public Dimension MinHeight { get; set; } = Dimension.None;
    public Dimension MaxWidth { get; set; } = Dimension.None;
    public Dimension MaxHeight { get; set; } = Dimension.None;
    public Thickness Padding { get; set; }
    public Thickness BorderWidth { get; set; }
    public Thickness Margin { get; set; }
    public Overflow OverflowX { get; set; } = Overflow.Visible;
    public Overflow OverflowY { get; set; } = Overflow.Visible;

    internal void CopyFrom(BoxStyle source)
    {
        Width = source.Width;
        Height = source.Height;
        MinWidth = source.MinWidth;
        MinHeight = source.MinHeight;
        MaxWidth = source.MaxWidth;
        MaxHeight = source.MaxHeight;
        Padding = source.Padding;
        BorderWidth = source.BorderWidth;
        Margin = source.Margin;
        OverflowX = source.OverflowX;
        OverflowY = source.OverflowY;
    }
}
