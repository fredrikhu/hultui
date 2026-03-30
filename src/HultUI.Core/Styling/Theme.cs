using HultUI.Core.Primitives;

namespace HultUI.Core.Styling;

/// <summary>
/// Root-level style defaults. Provides inherited text properties and semantic colors.
/// Swappable at runtime to toggle light/dark mode.
/// </summary>
public sealed class Theme
{
    // Inherited text defaults
    public string FontFamily { get; init; } = "sans-serif";
    public float FontSize { get; init; } = 16f;
    public FontWeight FontWeight { get; init; } = FontWeight.Regular;
    public Color TextColor { get; init; } = new(0, 0, 0);
    public TextDirection TextDirection { get; init; } = TextDirection.Ltr;
    public CursorType Cursor { get; init; } = CursorType.Default;

    // Component styles
    public ButtonTheme Button { get; init; } = new();
    public TextInputTheme TextInput { get; init; } = new();
    public CheckboxTheme Checkbox { get; init; } = new();
    public RadioButtonTheme RadioButton { get; init; } = new();
    public TooltipTheme Tooltip { get; init; } = new();
    public ContextMenuTheme ContextMenu { get; init; } = new();

    // Semantic colors
    public Color Background { get; init; } = Color.White;
    public Color Surface { get; init; } = new(248, 250, 252);       // slate-50
    public Color SurfaceVariant { get; init; } = new(241, 245, 249); // slate-100
    public Color Primary { get; init; } = new(79, 70, 229);          // indigo-600
    public Color PrimaryText { get; init; } = Color.White;
    public Color Secondary { get; init; } = new(100, 116, 139);      // slate-500
    public Color SecondaryText { get; init; } = Color.White;
    public Color Border { get; init; } = new(226, 232, 240);         // slate-200
    public Color Danger { get; init; } = new(220, 38, 38);           // red-600
    public Color DangerText { get; init; } = Color.White;
    public Color FocusRing { get; init; } = new(59, 130, 246, 180);  // blue-500 with alpha

    /// <summary>
    /// Converts the theme's text properties to a ResolvedInheritedStyle
    /// for use as the root defaults in style resolution.
    /// </summary>
    public ResolvedInheritedStyle ToResolvedStyle() => new(
        FontFamily: FontFamily,
        FontSize: FontSize,
        FontWeight: FontWeight,
        TextColor: TextColor,
        TextDirection: TextDirection,
        Cursor: Cursor);

    public static Theme Light { get; } = new();

    public static Theme Dark { get; } = new()
    {
        TextColor = new Color(226, 232, 240),           // slate-200
        Background = new Color(15, 23, 42),              // slate-900
        Surface = new Color(30, 41, 59),                 // slate-800
        SurfaceVariant = new Color(51, 65, 85),          // slate-700
        Primary = new Color(99, 102, 241),               // indigo-500
        PrimaryText = Color.White,
        Secondary = new Color(148, 163, 184),            // slate-400
        SecondaryText = new Color(15, 23, 42),           // slate-900
        Border = new Color(51, 65, 85),                  // slate-700
        Danger = new Color(239, 68, 68),                 // red-500
        DangerText = Color.White,
        FocusRing = new Color(99, 102, 241, 180),       // indigo-500 with alpha
        Tooltip = new TooltipTheme
        {
            Background = new Color(51, 65, 85),          // slate-700
            TextColor = new Color(226, 232, 240),        // slate-200
        },
        ContextMenu = new ContextMenuTheme
        {
            Background = new Color(30, 41, 59),          // slate-800
            BorderColor = new Color(51, 65, 85),         // slate-700
            TextColor = new Color(226, 232, 240),        // slate-200
            DisabledTextColor = new Color(100, 116, 139),// slate-500
            HoverBackground = new Color(51, 65, 85),     // slate-700
            SeparatorColor = new Color(51, 65, 85),      // slate-700
            ShortcutTextColor = new Color(148, 163, 184),// slate-400
        },
    };
}

public sealed class TooltipTheme
{
    public Color Background { get; init; } = new(15, 23, 42);       // slate-900
    public Color TextColor { get; init; } = new(248, 250, 252);     // slate-50
    public float CornerRadius { get; init; } = 4f;
    public Thickness Padding { get; init; } = new(6, 10);
    public float FontSize { get; init; } = 13f;
    public float ShowDelay { get; init; } = 0.5f;
    public float Gap { get; init; } = 4f;
}
