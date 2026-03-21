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
    };
}
