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
    Absolute,
    /// <summary>
    /// In the flow like <see cref="Relative"/>, but held against an edge of the nearest scroll
    /// view while it is scrolled past — a section header that stays in sight while its section
    /// scrolls under it. <see cref="PositionStyle.Top"/> (or Bottom, Left, Right) says how far
    /// from that edge; the element never leaves its parent's box, so the next section's header
    /// pushes it off. It moves when drawn, hit and read, never in layout.
    /// </summary>
    Sticky,
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
