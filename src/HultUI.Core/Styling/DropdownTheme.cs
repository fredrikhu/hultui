using HultUI.Core.Primitives;

namespace HultUI.Core.Styling;

/// <summary>
/// The closed face of a <see cref="Elements.DropdownElement"/> — and, deliberately, of
/// <see cref="Elements.DatePickerElement"/> and <see cref="Elements.TimePickerElement"/>
/// too, so a form does not end up with three differently-shaped boxes in one row. The
/// open list reuses <see cref="ContextMenuTheme"/> and the open calendar
/// <see cref="CalendarTheme"/>, so a themed menu and a themed dropdown always match.
/// </summary>
public sealed class DropdownTheme : IThemeExtension<DropdownTheme>
{
    public Color Background { get; init; } = Color.White;
    public Color BorderColor { get; init; } = new(226, 232, 240);        // slate-200
    public Color TextColor { get; init; } = new(15, 23, 42);             // slate-900
    public Color PlaceholderTextColor { get; init; } = new(148, 163, 184); // slate-400
    public Color ChevronColor { get; init; } = new(100, 116, 139);       // slate-500
    public float CornerRadius { get; init; } = 6f;
    public Thickness Padding { get; init; } = new(6, 10);
    public float FontSize { get; init; } = 13f;
    public float MinWidth { get; init; } = 120f;
    public float ChevronGap { get; init; } = 8f;
    /// <summary>Vertical distance between the control and its opened list.</summary>
    public float PopupGap { get; init; } = 2f;

    public static DropdownTheme CreateDefault(Theme theme) => new()
    {
        Background = theme.Background,
        BorderColor = theme.Border,
        TextColor = theme.TextColor,
        PlaceholderTextColor = theme.Secondary,
        ChevronColor = theme.Secondary,
    };
}
