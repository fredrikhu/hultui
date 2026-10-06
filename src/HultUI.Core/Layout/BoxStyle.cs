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

    /// <summary>
    /// Width over height of the border box, or 0 for none. With one side definite — given,
    /// stretched by the parent, or flexed — the other follows from it; with both definite it
    /// is ignored; with neither, the width comes from the content and the height from that.
    /// Width wins: a parent stretches a ratio box across, never down, and in a column a ratio
    /// box of auto width and height takes the room the column has, as a block does. Given a
    /// height, the height decides.
    /// </summary>
    public float AspectRatio { get; set; }

    /// <returns>Whether anything changed: a patch that changes nothing in a layout style
    /// leaves the element's layout standing (Element.PatchFrom).</returns>
    internal bool CopyFrom(BoxStyle source)
    {
        if (Width == source.Width
            && Height == source.Height
            && MinWidth == source.MinWidth
            && MinHeight == source.MinHeight
            && MaxWidth == source.MaxWidth
            && MaxHeight == source.MaxHeight
            && Padding == source.Padding
            && BorderWidth == source.BorderWidth
            && Margin == source.Margin
            && OverflowX == source.OverflowX
            && OverflowY == source.OverflowY
            && AspectRatio == source.AspectRatio)
            return false;

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
        AspectRatio = source.AspectRatio;
        return true;
    }
}
