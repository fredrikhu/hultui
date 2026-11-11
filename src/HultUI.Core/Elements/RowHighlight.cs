using HultUI.Core.Primitives;

namespace HultUI.Core.Elements;

/// <summary>
/// The look of a selected row when a <see cref="VirtualListElement{T}"/> draws the selection
/// itself (<see cref="VirtualListElement{T}.SelectionHighlight"/>): a box at the row's border
/// box, under the row, drawn exactly as a <see cref="BoxElement"/> with this background, corner
/// radius and solid border draws its own — the same calls, so the same pixels, snapping
/// included.
/// </summary>
/// <param name="Background">The fill.</param>
/// <param name="CornerRadius">The corners, as <see cref="BoxElement.CornerRadius"/>.</param>
/// <param name="BorderColor">A ring drawn with the fill, inside the row's box as a border is;
/// transparent (the default) draws none.</param>
/// <param name="BorderWidth">The ring's width, the same on every side. For a width per side, set
/// <see cref="BorderThickness"/> instead.</param>
public readonly record struct RowHighlight(
    Color Background,
    CornerRadius CornerRadius = default,
    Color BorderColor = default,
    float BorderWidth = 0f)
{
    /// <summary>
    /// The ring's width on each side, as a box's <c>.BorderWidth(Thickness)</c>: a row whose
    /// selected look is an accent on one edge (<c>new Thickness(2, 0, 0, 0)</c>, the top) keeps it
    /// when the list draws the selection. Null (the default) uses <see cref="BorderWidth"/> on every
    /// side; when set, it is used instead of <see cref="BorderWidth"/>.
    /// <para>
    /// Widths that differ are drawn as a box draws them: the fill, then the border on its own. The
    /// same width on every side is drawn as <see cref="BorderWidth"/> is.
    /// </para>
    /// </summary>
    /// <example><code>
    /// list.SelectionHighlight(new RowHighlight(fill, new CornerRadius(3), accent)
    /// {
    ///     BorderThickness = new Thickness(2, 0, 0, 0),
    /// });
    /// </code></example>
    public Thickness? BorderThickness { get; init; }

    /// <summary>The ring's width on each side as drawn: <see cref="BorderThickness"/>, or else
    /// <see cref="BorderWidth"/> on every side.</summary>
    internal Thickness Widths => BorderThickness ?? new Thickness(BorderWidth);
}
