using HultUI.Core.Primitives;
using HultUI.Core.Styling;

namespace HultUI.Core.DevTools;

/// <summary>
/// Colors for the inspector's own chrome, derived from the application theme.
///
/// Only three theme tokens are read: <see cref="Theme.Background"/>,
/// <see cref="Theme.TextColor"/>, and <see cref="Theme.Primary"/>. Everything else —
/// surfaces, borders, hover tints, muted text — is blended between them.
///
/// That is deliberate. A <see cref="Theme"/> is a record with light defaults for every
/// token, and a real app typically overrides only the handful it cares about; a dark
/// theme that never assigns <see cref="Theme.SurfaceVariant"/> or
/// <see cref="Theme.Border"/> silently keeps slate-100 and slate-200. Reading those
/// directly puts near-white buttons and divider lines on a dark panel, which is exactly
/// what a partially-specified theme produced. Deriving from the background instead means
/// the panel is internally consistent for any theme, complete or not, while still
/// following the app's light/dark and accent.
/// </summary>
public readonly record struct InspectorPalette(
    Color Background, Color Surface, Color Control, Color ControlHover, Color RowHover,
    Color Border, Color Text, Color Muted, Color Accent, Color AccentText,
    Color PickTint, Color Warning, Color WarningTint)
{
    private static readonly Color Amber = new(240, 180, 60);
    private static readonly Color PickGreen = new(120, 220, 160);

    public static InspectorPalette From(Theme theme)
    {
        var background = theme.Background;
        var text = theme.TextColor;
        var accent = theme.Primary;

        // Blending toward the text color rather than toward white or black keeps the
        // steps going the right direction on both light and dark grounds: on a dark
        // theme each step lightens, on a light theme each step darkens.
        return new InspectorPalette(
            Background: background,
            Surface: Mix(background, text, 0.05f),
            Control: Mix(background, text, 0.12f),
            ControlHover: Mix(background, text, 0.22f),
            RowHover: Mix(background, text, 0.08f),
            Border: Mix(background, text, 0.16f),
            Text: text,
            Muted: Mix(text, background, 0.40f),
            Accent: accent,
            // Not Theme.PrimaryText: it defaults to white, which is unreadable on a
            // pale accent. Pick the side of the accent that actually contrasts.
            AccentText: IsLight(accent) ? new Color(20, 20, 24) : Color.White,
            PickTint: Mix(background, PickGreen, 0.35f),
            Warning: Mix(text, Amber, 0.75f),
            WarningTint: Mix(background, Amber, 0.22f));
    }

    /// <summary>Blends <paramref name="amount"/> of <paramref name="b"/> into <paramref name="a"/>, keeping a's alpha.</summary>
    public static Color Mix(Color a, Color b, float amount)
    {
        byte Blend(byte x, byte y) => (byte)Math.Clamp(MathF.Round(x + (y - x) * amount), 0, 255);
        return new Color(Blend(a.R, b.R), Blend(a.G, b.G), Blend(a.B, b.B), a.A);
    }

    /// <summary>Relative luminance test, used to decide what contrasts against a color.</summary>
    public static bool IsLight(Color c) => 0.2126f * c.R + 0.7152f * c.G + 0.0722f * c.B > 140f;
}
