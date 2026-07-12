namespace HultUI.Core.Layout;

public enum FlexDirection : byte
{
    Row,
    RowReverse,
    Column,
    ColumnReverse
}

public enum FlexWrap : byte
{
    NoWrap,
    Wrap,
    WrapReverse
}

public enum JustifyContent : byte
{
    FlexStart,
    FlexEnd,
    Center,
    SpaceBetween,
    SpaceAround,
    SpaceEvenly
}

public enum AlignItems : byte
{
    FlexStart,
    FlexEnd,
    Center,
    Stretch,
    Baseline
}

public enum AlignSelf : byte
{
    Auto,
    FlexStart,
    FlexEnd,
    Center,
    Stretch,
    Baseline
}

public enum AlignContent : byte
{
    FlexStart,
    FlexEnd,
    Center,
    Stretch,
    SpaceBetween,
    SpaceAround
}

public enum PositionType : byte
{
    Relative,
    Absolute
}

public enum Overflow : byte
{
    Visible,
    Hidden,
    Scroll
}

public enum TextOverflow : byte
{
    Clip,
    Ellipsis
}

public enum BorderStyle : byte
{
    Solid,
    Dashed,
    Dotted
}

public enum ImageFit : byte
{
    Contain,
    Cover,
    Fill,
    ScaleDown,
    None
}
