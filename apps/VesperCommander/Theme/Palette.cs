using HultUI.Core.Primitives;
using HultUI.Themes;

namespace VesperCommander.Theme;

/// <summary>
/// Every colour Vesper draws with, resolved for one theme. <see cref="Nocturne"/> reads the
/// active one; nothing else constructs these.
/// </summary>
/// <remarks>
/// Only the colours that cannot be worked out from the others live here. Anything derived —
/// the hairline divider, the diff tints, the muted text steps — stays a property on
/// <see cref="Nocturne"/> so it tracks whatever palette is current instead of freezing at
/// startup.
/// </remarks>
public sealed record Palette
{
    public required NamedTheme Theme { get; init; }

    public required Color Bg { get; init; }
    public required Color Surface { get; init; }
    public required Color Text { get; init; }
    public required Color Accent { get; init; }

    /// <summary>Nine steps each, index 0 = the "100" step. Which end is lightest depends on
    /// the theme — see <see cref="HultUI.Themes.Ramp"/>.</summary>
    public required Color[] Neutrals { get; init; }
    public required Color[] Accents { get; init; }

    /// <summary>The marked-file red and its dimmed metadata companion. Red in every theme:
    /// the design handoff calls marking the one convention that does not bend, and a file
    /// manager where "selected for deletion" is a different colour per theme is a file
    /// manager that will eventually delete the wrong thing.</summary>
    public required Color MarkRed { get; init; }
    public required Color MarkRedDim { get; init; }

    public required Color DiffGreen { get; init; }
    public required Color DiffGreenText { get; init; }

    /// <summary>Find-hit highlight. Deliberately not the accent: a hit lands on top of code
    /// the diff has already tinted with the accent, and two accent-coloured backgrounds over
    /// each other say nothing about which is which.</summary>
    public required Color MarkAmber { get; init; }

    // ── The app's own design system ──────────────────────────────────────

    /// <summary>Nocturne, transcribed. Written out rather than generated from
    /// <see cref="ThemeCatalog"/>'s curve because these values were drawn by hand in the
    /// design handoff — running them back through a generator would shift the default look
    /// of the app by a few points per channel for no benefit at all.</summary>
    public static Palette NocturneDefault { get; } = new()
    {
        Theme = ThemeCatalog.Nocturne,
        Bg = Hex("#161826"),
        Surface = Hex("#232532"),
        Text = Hex("#e9e9ed"),
        Accent = Hex("#9184d9"),
        Neutrals =
        [
            Hex("#f3f5fe"), Hex("#e4e7f5"), Hex("#cfd3e5"), Hex("#b2b6ca"), Hex("#9397ab"),
            Hex("#75798c"), Hex("#595d6c"), Hex("#3f424d"), Hex("#292b31"),
        ],
        Accents =
        [
            Hex("#f5f4ff"), Hex("#e7e5fe"), Hex("#d2cefd"), Hex("#b5abfc"), Hex("#968ae0"),
            Hex("#796cbf"), Hex("#5d5294"), Hex("#423a6a"), Hex("#2b2741"),
        ],
        // The design's marked-file red is oklch(0.72 0.14 21) with a dimmed oklch(0.55 0.1 21);
        // HultUI Color has no oklch constructor, so these are sRGB approximations of the two.
        MarkRed = Hex("#e0685f"),
        MarkRedDim = Hex("#a95349"),
        DiffGreen = Hex("#3eb37e"),
        DiffGreenText = Hex("#7fd0a5"),
        MarkAmber = Hex("#e3b341"),
    };

    /// <summary>
    /// The palette for <paramref name="theme"/>, with <paramref name="accentIndex"/> applied
    /// on top. Index 0 means "the theme's own accent"; 1..3 swap in one of
    /// <see cref="AccentOverrides"/>, which is how the accent swatches on the Colors page
    /// worked before there were themes and how they keep working now.
    /// </summary>
    public static Palette For(NamedTheme theme, int accentIndex)
    {
        var p = theme.Id == ThemeCatalog.Nocturne.Id ? NocturneDefault : Generate(theme);
        if (accentIndex <= 0 || accentIndex >= AccentOverrides.Length) return p;

        var accent = Color.FromHex(AccentOverrides[accentIndex]);
        return p with { Accent = accent, Accents = Ramp.Generate(accent, theme.IsDark) };
    }

    /// <summary>Accent bases the Colors page offers on top of any theme. Index 0 is the
    /// theme's own, so the swatch row always has a "leave it alone" position.</summary>
    public static readonly string[] AccentOverrides = ["", "#7fb2d9", "#8fd0b0", "#d9a06b"];

    private static Palette Generate(NamedTheme t) => new()
    {
        Theme = t,
        Bg = t.Bg,
        Surface = t.Surface,
        Text = t.Text,
        Accent = t.Accent,
        Neutrals = t.Neutrals,
        Accents = t.Accents,
        // Marking stays red even where the theme's own red is used for something else.
        MarkRed = t.Red,
        MarkRedDim = ColorMath.Tone(t.Red, t.IsDark ? 0.47 : 0.38, 0.75),
        DiffGreen = t.Green,
        DiffGreenText = ColorMath.Tone(t.Green, t.IsDark ? 0.66 : 0.34),
        MarkAmber = t.Amber,
    };

    private static Color Hex(string h) => Color.FromHex(h);
}
