using HultUI.Core.Primitives;
using HultUI.Themes;

namespace Junction.Theme;

/// <summary>
/// Every colour Junction draws with, resolved for one theme. <see cref="Nocturne"/> reads the
/// active one; nothing else constructs these.
/// </summary>
/// <remarks>
/// Only colours that cannot be worked out from the others live here. Anything derived — the
/// hairline divider, the diff tints and their intra-line highlights, the muted text steps —
/// stays a property on <see cref="Nocturne"/> so it tracks whatever palette is current
/// instead of freezing at startup.
/// </remarks>
public sealed record Palette
{
    public required NamedTheme Theme { get; init; }

    public required Color Bg { get; init; }
    public required Color Surface { get; init; }
    public required Color Text { get; init; }
    public required Color Accent { get; init; }
    public required Color Accent2 { get; init; }

    /// <summary>Nine steps each, index 0 = the "100" step. Which end is lightest depends on
    /// the theme — see <see cref="HultUI.Themes.Ramp"/>.</summary>
    public required Color[] Neutrals { get; init; }
    public required Color[] Accents { get; init; }
    public required Color[] Accent2s { get; init; }

    /// <summary>The hue a diff's added lines are tinted with. The row background and the
    /// intra-line highlight are this colour at two opacities, so they cannot drift apart.</summary>
    public required Color AddBase { get; init; }
    public required Color AddText { get; init; }
    public required Color AddSign { get; init; }
    public required Color AddCount { get; init; }

    public required Color DelBase { get; init; }
    public required Color DelText { get; init; }
    public required Color DelSign { get; init; }
    public required Color DelCount { get; init; }

    public required Color DangerText { get; init; }
    public required Color DangerIcon { get; init; }

    /// <summary>Find-hit highlight. Deliberately not the accent and not either diff colour: a
    /// search hit lands on top of a line that is already tinted green, red or accent, and a
    /// fourth colour drawn from those three says nothing about which is which.</summary>
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
        Accent2 = Hex("#a7a1db"),
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
        // A mono stand-in that reads like the accent — the design's second register.
        Accent2s =
        [
            Hex("#f5f4ff"), Hex("#e7e5fe"), Hex("#d2cefd"), Hex("#b5afe8"), Hex("#9690c9"),
            Hex("#7972a9"), Hex("#5c5783"), Hex("#423e5d"), Hex("#2b293a"),
        ],
        AddBase = Hex("#78b274"),
        AddText = Hex("#bfe0ba"),
        AddSign = Hex("#7fbf79"),
        AddCount = Hex("#93c98d"),
        DelBase = Hex("#c87878"),
        DelText = Hex("#e3b9b9"),
        DelSign = Hex("#cf8f8f"),
        DelCount = Hex("#cf9494"),
        DangerText = Hex("#e39a9a"),
        DangerIcon = Hex("#cf8f8f"),
        MarkAmber = Hex("#e3b341"),
    };

    /// <summary>The palette for <paramref name="theme"/>.</summary>
    public static Palette For(NamedTheme theme) =>
        theme.Id == ThemeCatalog.Nocturne.Id ? NocturneDefault : Generate(theme);

    private static Palette Generate(NamedTheme t) => new()
    {
        Theme = t,
        Bg = t.Bg,
        Surface = t.Surface,
        Text = t.Text,
        Accent = t.Accent,
        Accent2 = t.Accent2,
        Neutrals = t.Neutrals,
        Accents = t.Accents,
        Accent2s = t.Accent2s,

        // Four steps of one hue, at the lightnesses Nocturne's hand-picked greens sit at:
        // the tint the row is washed with, the text on it, the +/− sign in the gutter, and
        // the count in a file's header. A light theme wants them below the paper rather
        // than above the ground, so every step flips.
        //
        // The added text on a light ground sits lower than the deleted: green hues carry
        // more luminance than reds at the same HSL lightness, and at 0.30 Solarized's
        // yellow-green came out at 2.3:1 on its own word highlight.
        AddBase = Wash(t, t.Green, 0.45, 0.40),
        AddText = Tone(t, 0.80, 0.24),
        AddSign = Tone(t, 0.61, 0.40),
        AddCount = Tone(t, 0.67, 0.36),

        DelBase = Wash(t, t.Red, 0.50, 0.40),
        DelText = Tone(t, 0.80, 0.30, red: true),
        DelSign = Tone(t, 0.69, 0.40, red: true),
        DelCount = Tone(t, 0.70, 0.38, red: true),

        DangerText = Tone(t, 0.74, 0.34, red: true),
        DangerIcon = Tone(t, 0.69, 0.40, red: true),
        MarkAmber = Wash(t, t.Amber, 0.45, 0.70),
    };

    /// <summary>The colour a diff row is washed with and its changed words are lit with, or
    /// a search hit is marked in — the one that goes under text rather than being it.
    ///
    /// <para>Nocturne's own are muted mid-tones, picked so the syntax colours drawn over them
    /// stay legible. A scheme's published green, red and amber are terminal colours — Dracula's
    /// green is <c>#50fa7b</c>, its amber a pale yellow — and at the same opacities they
    /// compose to a highlight bright enough to swallow that scheme's own comments, numbers and
    /// strings: Dracula's comment sat at 1.4:1 on its word highlight, and a search hit on one of
    /// its strings disappeared entirely, the string and the mark being the same yellow. So on a
    /// dark ground the hue is kept and the lightness and saturation are capped, a little under
    /// Nocturne's because these grounds mostly sit above Nocturne's. Capped, never lifted: a
    /// green already darker than the cap (Solarized's) keeps its lightness and only loses
    /// saturation, and one under both caps comes through as published. A light theme mixes its
    /// wash under dark text, where a deep, saturated base is exactly what reads, so it passes
    /// through.</para>
    /// </summary>
    private static Color Wash(NamedTheme t, Color published, double maxLightness, double maxSaturation)
    {
        if (!t.IsDark) return published;
        var (h, s, l) = ColorMath.ToHsl(published);
        if (l <= maxLightness && s <= maxSaturation) return published;
        return ColorMath.FromHsl(h, Math.Min(s, maxSaturation), Math.Min(l, maxLightness));
    }

    /// <summary>One step of the theme's green (or red), lit for a dark or a light ground.
    /// Saturation eases off with the extremes for the same reason the ramps do.</summary>
    private static Color Tone(NamedTheme t, double onDark, double onLight, bool red = false) =>
        ColorMath.Tone(red ? t.Red : t.Green, t.IsDark ? onDark : onLight, t.IsDark ? 0.62 : 0.85);

    private static Color Hex(string h) => Color.FromHex(h);
}
