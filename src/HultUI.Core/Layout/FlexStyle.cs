using HultUI.Core.Primitives;

namespace HultUI.Core.Layout;

public sealed class FlexStyle
{
    public FlexDirection Direction { get; set; } = FlexDirection.Column;
    public FlexWrap Wrap { get; set; } = FlexWrap.NoWrap;
    public AlignItems AlignItems { get; set; } = AlignItems.Stretch;
    public AlignContent AlignContent { get; set; } = AlignContent.FlexStart;
    public AlignSelf AlignSelf { get; set; } = AlignSelf.Auto;
    public JustifyContent JustifyContent { get; set; } = JustifyContent.FlexStart;
    public float FlexGrow { get; set; }
    public float FlexShrink { get; set; } = 1;
    public Dimension FlexBasis { get; set; } = Dimension.Auto;
    public float Gap { get; set; }
    public float RowGap { get; set; }
    public float ColumnGap { get; set; }

    public float ResolvedRowGap => RowGap > 0 ? RowGap : Gap;
    public float ResolvedColumnGap => ColumnGap > 0 ? ColumnGap : Gap;

    internal void CopyFrom(FlexStyle source)
    {
        Direction = source.Direction;
        Wrap = source.Wrap;
        AlignItems = source.AlignItems;
        AlignContent = source.AlignContent;
        AlignSelf = source.AlignSelf;
        JustifyContent = source.JustifyContent;
        FlexGrow = source.FlexGrow;
        FlexShrink = source.FlexShrink;
        FlexBasis = source.FlexBasis;
        Gap = source.Gap;
        RowGap = source.RowGap;
        ColumnGap = source.ColumnGap;
    }
}
