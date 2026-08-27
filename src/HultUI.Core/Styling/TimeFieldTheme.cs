using HultUI.Core.Primitives;

namespace HultUI.Core.Styling;

/// <summary>
/// The segmented field drawn by <see cref="Elements.TimePickerElement"/>.
///
/// <para>Its box borrows <see cref="DropdownTheme"/> so a time field sits beside a
/// dropdown and a date field without three different boxes in one row. What is here is
/// only what a segmented field has and the others do not: how the active segment is
/// marked, and how much room a two-digit number needs.</para>
/// </summary>
public sealed class TimeFieldTheme : IThemeExtension<TimeFieldTheme>
{
    /// <summary>Behind the segment the keyboard is on.</summary>
    public Color ActiveSegmentBackground { get; init; } = new(79, 70, 229);   // indigo-600
    public Color ActiveSegmentTextColor { get; init; } = Color.White;
    /// <summary>The <c>:</c> between the segments, and the placeholder dashes.</summary>
    public Color SeparatorColor { get; init; } = new(148, 163, 184);          // slate-400
    public float SegmentCornerRadius { get; init; } = 4f;
    /// <summary>Breathing room either side of a segment's digits.</summary>
    public float SegmentPaddingX { get; init; } = 2f;

    /// <summary>
    /// Room between the box's edge and the first and last segment. Narrower than the
    /// dropdown padding the rest of the box borrows: a dropdown's padding sits beside a
    /// sentence of arbitrary length, while this field is a fixed handful of digits, and the
    /// dropdown's allowance around them reads as a field several characters wider than it
    /// has any content for.
    /// </summary>
    /// <remarks>Only the horizontal padding is overridden. The vertical padding still comes
    /// from the dropdown theme, so a time field keeps the same height and baseline as the
    /// date field and the dropdown beside it.</remarks>
    public float PaddingX { get; init; } = 6f;

    public static TimeFieldTheme CreateDefault(Theme theme) => new()
    {
        ActiveSegmentBackground = theme.Primary,
        ActiveSegmentTextColor = theme.PrimaryText,
        SeparatorColor = theme.Secondary,
    };
}
