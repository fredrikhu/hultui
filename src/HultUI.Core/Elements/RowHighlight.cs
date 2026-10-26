using HultUI.Core.Primitives;

namespace HultUI.Core.Elements;

/// <summary>
/// The look of a selected row when a <see cref="VirtualListElement{T}"/> draws the selection
/// itself (<see cref="VirtualListElement{T}.SelectionHighlight"/>): a box at the row's border
/// box, under the row, drawn exactly as a <see cref="BoxElement"/> with this background, corner
/// radius and uniform solid border draws its own — the same call, so the same pixels, snapping
/// included.
/// </summary>
/// <param name="Background">The fill.</param>
/// <param name="CornerRadius">The corners, as <see cref="BoxElement.CornerRadius"/>.</param>
/// <param name="BorderColor">A ring drawn with the fill, inside the row's box as a border is;
/// transparent (the default) draws none.</param>
/// <param name="BorderWidth">The ring's width, the same on every side.</param>
public readonly record struct RowHighlight(
    Color Background,
    CornerRadius CornerRadius = default,
    Color BorderColor = default,
    float BorderWidth = 0f);
