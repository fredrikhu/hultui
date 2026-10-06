namespace HultUI.Core.Layout;

public sealed class GridItemStyle
{
    /// <summary>Shared all-defaults instance for elements that never set grid-item
    /// properties. Read-only by contract — never mutate.</summary>
    internal static readonly GridItemStyle Default = new();

    public int Row { get; set; }
    public int Column { get; set; }

    /// <summary>Override container's <see cref="GridStyle.JustifyItems"/>. Auto = inherit.</summary>
    public JustifySelf JustifySelf { get; set; } = JustifySelf.Auto;

    /// <summary>Override container's <see cref="GridStyle.AlignItems"/>. Auto = inherit.</summary>
    public AlignSelf AlignSelf { get; set; } = AlignSelf.Auto;

    /// <returns>Whether anything changed: a patch that changes nothing in a layout style
    /// leaves the element's layout standing (Element.PatchFrom).</returns>
    internal bool CopyFrom(GridItemStyle source)
    {
        if (Row == source.Row
            && Column == source.Column
            && JustifySelf == source.JustifySelf
            && AlignSelf == source.AlignSelf)
            return false;

        Row = source.Row;
        Column = source.Column;
        JustifySelf = source.JustifySelf;
        AlignSelf = source.AlignSelf;
        return true;
    }
}

public enum JustifySelf : byte
{
    Auto,
    Start,
    End,
    Center,
    Stretch,
}
