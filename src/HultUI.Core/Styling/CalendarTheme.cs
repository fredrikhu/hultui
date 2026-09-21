using HultUI.Core.Primitives;

namespace HultUI.Core.Styling;

/// <summary>
/// The month grid drawn by <see cref="Elements.CalendarElement"/>, and shown by
/// <see cref="Elements.DatePickerElement"/> when it opens.
///
/// <para>Sizes are here rather than derived from the font because a calendar is a grid
/// first: every cell has to be the same width whatever is in it, or the columns stop
/// lining up under their weekday headings.</para>
/// </summary>
public sealed class CalendarTheme : IThemeExtension<CalendarTheme>
{
    public Color Background { get; init; } = Color.White;
    public Color BorderColor { get; init; } = new(226, 232, 240);          // slate-200
    public float BorderWidth { get; init; } = 1f;
    public Color TextColor { get; init; } = new(15, 23, 42);               // slate-900
    /// <summary>Days belonging to the neighbouring months, and the weekday headings.</summary>
    public Color MutedTextColor { get; init; } = new(148, 163, 184);       // slate-400
    /// <summary>Days outside the allowed range: present, and visibly not offered.</summary>
    public Color DisabledTextColor { get; init; } = new(203, 213, 225);    // slate-300
    public Color SelectedBackground { get; init; } = new(79, 70, 229);     // indigo-600
    public Color SelectedTextColor { get; init; } = Color.White;
    public Color HoverBackground { get; init; } = new(241, 245, 249);      // slate-100
    /// <summary>The ring around today when today is not the selected day.</summary>
    public Color TodayColor { get; init; } = new(79, 70, 229);
    public Color FocusRingColor { get; init; } = new(59, 130, 246, 180);

    public float FontSize { get; init; } = 13f;
    public float HeaderFontSize { get; init; } = 13.5f;
    public float WeekdayFontSize { get; init; } = 11f;

    public float CellWidth { get; init; } = 34f;
    public float CellHeight { get; init; } = 30f;
    public float HeaderHeight { get; init; } = 32f;
    public float WeekdayHeight { get; init; } = 20f;
    public float CornerRadius { get; init; } = 8f;
    /// <summary>Corner radius of a single day cell.</summary>
    public float CellCornerRadius { get; init; } = 6f;
    public Thickness Padding { get; init; } = new(8f);

    public static CalendarTheme CreateDefault(Theme theme) => new()
    {
        Background = theme.Background,
        BorderColor = theme.Border,
        TextColor = theme.TextColor,
        MutedTextColor = theme.Secondary,
        SelectedBackground = theme.Primary,
        SelectedTextColor = theme.PrimaryText,
        HoverBackground = theme.SurfaceVariant,
        TodayColor = theme.Primary,
        FocusRingColor = theme.FocusRing,
    };
}
