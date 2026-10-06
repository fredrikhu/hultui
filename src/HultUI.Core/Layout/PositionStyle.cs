using HultUI.Core.Primitives;

namespace HultUI.Core.Layout;

public sealed class PositionStyle
{
    /// <summary>Shared all-defaults instance for elements that never set position
    /// properties. Read-only by contract — never mutate.</summary>
    internal static readonly PositionStyle Default = new();

    public PositionType Type { get; set; } = PositionType.Relative;
    public Dimension Top { get; set; } = Dimension.None;
    public Dimension Right { get; set; } = Dimension.None;
    public Dimension Bottom { get; set; } = Dimension.None;
    public Dimension Left { get; set; } = Dimension.None;
    public int ZIndex { get; set; }

    /// <returns>Whether anything changed: a patch that changes nothing in a layout style
    /// leaves the element's layout standing (Element.PatchFrom).</returns>
    internal bool CopyFrom(PositionStyle source)
    {
        if (Type == source.Type
            && Top == source.Top
            && Right == source.Right
            && Bottom == source.Bottom
            && Left == source.Left
            && ZIndex == source.ZIndex)
            return false;

        Type = source.Type;
        Top = source.Top;
        Right = source.Right;
        Bottom = source.Bottom;
        Left = source.Left;
        ZIndex = source.ZIndex;
        return true;
    }
}
