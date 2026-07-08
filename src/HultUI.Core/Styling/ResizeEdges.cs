namespace HultUI.Core.Styling;

/// <summary>
/// Which edges of a borderless window can be grabbed to resize. Corners are
/// derived: a corner is resizable when both of its adjacent edges are enabled
/// (e.g. the bottom-left corner needs <see cref="Left"/> and <see cref="Bottom"/>).
/// </summary>
[Flags]
public enum ResizeEdges
{
    None = 0,
    Left = 1,
    Right = 2,
    Top = 4,
    Bottom = 8,

    /// <summary>
    /// The common default: sides and bottom, but not the top. Apps with a
    /// custom title bar flush to the top edge usually leave top-resize off.
    /// </summary>
    SidesAndBottom = Left | Right | Bottom,

    All = Left | Right | Top | Bottom,
}
