using HultUI.Core.Primitives;

namespace HultUI.Core.Layout;

public sealed class PositionStyle
{
    public PositionType Type { get; set; } = PositionType.Relative;
    public Dimension Top { get; set; } = Dimension.None;
    public Dimension Right { get; set; } = Dimension.None;
    public Dimension Bottom { get; set; } = Dimension.None;
    public Dimension Left { get; set; } = Dimension.None;
    public int ZIndex { get; set; }
}
