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

    /// <summary>
    /// Whether the mark is a solid triangle or two strokes meeting at a point. Defaults to the
    /// triangle these controls have always drawn: it is a glyph from the control's own font,
    /// so changing the default would repaint every dropdown in every app that never asked.
    /// <see cref="ChevronShape.Stroke"/> is the shape Fluent, and most other current design
    /// systems, use — drawn rather than typed, so it does not depend on the font having it.
    /// </summary>
    public ChevronShape ChevronShape { get; init; } = ChevronShape.Triangle;

    /// <summary>Line width of the stroked chevron. Ignored by the triangle.</summary>
    public float ChevronStrokeWidth { get; init; } = 1f;
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

/// <summary>The shape of a dropdown's drop mark. See <see cref="DropdownTheme.ChevronShape"/>.</summary>
public enum ChevronShape : byte
{
    /// <summary>A small solid triangle, typed as a glyph from the control's font.</summary>
    Triangle,

    /// <summary>Two strokes meeting at a point, drawn by the renderer.</summary>
    Stroke,
}
