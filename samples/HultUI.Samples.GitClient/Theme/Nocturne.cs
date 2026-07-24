using HultUI.Core.Primitives;
using HultUI.Core.Styling;

namespace GitClient.Theme;

/// <summary>
/// The <b>Nocturne</b> design system — the source of truth for the Junction
/// git client's look. Values mirror <c>styles.css</c> from the design handoff:
/// a single blurple accent, tonal ramps generated on one shared lightness
/// scale, and a set of diff/status literals that sit outside the ramps.
///
/// <para>Colours that the design expresses as
/// <c>color-mix(in srgb, X N%, transparent)</c> are reproduced here by scaling
/// the source colour's alpha to <c>N%</c> (srcβ mix with transparent is exactly
/// the colour at that alpha). Use <see cref="On"/> for the common text steps and
/// <see cref="Alpha"/> for arbitrary tints.</para>
/// </summary>
public static class Nocturne
{
    // ── Ground / surfaces ────────────────────────────────────────────────
    public static readonly Color Bg = Hex("#161826");        // app ground
    public static readonly Color Surface = Hex("#232532");   // raised bars, menus, palette
    public static readonly Color Text = Hex("#e9e9ed");
    public static readonly Color Accent = Hex("#9184d9");
    public static readonly Color Accent2 = Hex("#a7a1db");

    /// <summary>1px hairline borders — text at 16%.</summary>
    public static readonly Color Divider = Alpha(Text, 0.16);

    // ── Neutral ramp ─────────────────────────────────────────────────────
    public static readonly Color Neutral100 = Hex("#f3f5fe");
    public static readonly Color Neutral200 = Hex("#e4e7f5");
    public static readonly Color Neutral300 = Hex("#cfd3e5");
    public static readonly Color Neutral400 = Hex("#b2b6ca");
    public static readonly Color Neutral500 = Hex("#9397ab");
    public static readonly Color Neutral600 = Hex("#75798c");
    public static readonly Color Neutral700 = Hex("#595d6c");
    public static readonly Color Neutral800 = Hex("#3f424d");
    public static readonly Color Neutral900 = Hex("#292b31");

    // ── Accent ramp ──────────────────────────────────────────────────────
    public static readonly Color Accent100 = Hex("#f5f4ff");
    public static readonly Color Accent200 = Hex("#e7e5fe");
    public static readonly Color Accent300 = Hex("#d2cefd");
    public static readonly Color Accent400 = Hex("#b5abfc");
    public static readonly Color Accent500 = Hex("#968ae0");
    public static readonly Color Accent600 = Hex("#796cbf");
    public static readonly Color Accent700 = Hex("#5d5294");
    public static readonly Color Accent800 = Hex("#423a6a");
    public static readonly Color Accent900 = Hex("#2b2741");

    // ── Second-accent ramp (mono stand-in; reads like the accent) ────────
    public static readonly Color Accent2_100 = Hex("#f5f4ff");
    public static readonly Color Accent2_200 = Hex("#e7e5fe");
    public static readonly Color Accent2_300 = Hex("#d2cefd");
    public static readonly Color Accent2_400 = Hex("#b5afe8");
    public static readonly Color Accent2_500 = Hex("#9690c9");
    public static readonly Color Accent2_600 = Hex("#7972a9");
    public static readonly Color Accent2_700 = Hex("#5c5783");
    public static readonly Color Accent2_800 = Hex("#423e5d");
    public static readonly Color Accent2_900 = Hex("#2b293a");

    // ── Radii ────────────────────────────────────────────────────────────
    public const float RadiusSm = 4f;
    public const float RadiusMd = 8f;
    public const float RadiusLg = 14f;
    public const float RadiusRow = 6f;    // small rows / segments
    public const float RadiusWindow = 12f;

    // ── Diff literals (not tokens) ───────────────────────────────────────
    public static readonly Color DiffAddBg = new(120, 178, 116, 33);   // rgba(120,178,116,0.13)
    public static readonly Color DiffAddText = Hex("#bfe0ba");
    public static readonly Color DiffAddSign = Hex("#7fbf79");
    public static readonly Color DiffDelBg = new(200, 120, 120, 33);   // rgba(200,120,120,0.13)
    public static readonly Color DiffDelText = Hex("#e3b9b9");
    public static readonly Color DiffDelSign = Hex("#cf8f8f");
    public static readonly Color DiffBlankBg = Alpha(Neutral900, 0.40);
    // Intra-line (word-level) emphasis, drawn on top of the line background.
    public static readonly Color DiffAddHl = new(120, 178, 116, 72);   // rgba(120,178,116,0.28)
    public static readonly Color DiffDelHl = new(200, 120, 120, 72);

    public static readonly Color DangerText = Hex("#e39a9a");   // destructive menu items
    public static readonly Color DangerIcon = Hex("#cf8f8f");
    public static readonly Color AddCount = Hex("#93c98d");     // +additions
    public static readonly Color DelCount = Hex("#cf9494");     // −deletions

    // ── Helpers ──────────────────────────────────────────────────────────

    /// <summary>The app text colour at <paramref name="pct"/> opacity (0..1) —
    /// the design's <c>color-mix(text N%, transparent)</c> muted steps.</summary>
    public static Color On(double pct) => Alpha(Text, pct);

    /// <summary>An arbitrary colour scaled to <paramref name="pct"/> opacity (0..1).</summary>
    public static Color Alpha(Color c, double pct) =>
        new(c.R, c.G, c.B, (byte)Math.Clamp(pct * 255.0, 0, 255));

    /// <summary>File-status letter colour (A/M/D/R/U), matching the design.</summary>
    public static Color StatusLetter(char st) => st switch
    {
        'A' => AddCount,
        'M' => Accent300,
        'D' => DelCount,
        'R' => Accent2_300,
        'U' => Neutral400,
        _ => Neutral400,
    };

    /// <summary>Maps Nocturne roles onto the framework theme (context menus, focus, etc.).</summary>
    public static HultUI.Core.Styling.Theme ToHultTheme() => new()
    {
        Background = Bg,
        TextColor = Text,
        Surface = Surface,
        Primary = Accent,
        Secondary = On(0.60),
        // Text fields are flush and borderless by default — StyleResolver re-applies
        // this every pass, so per-element border overrides don't stick. Views that
        // want a bordered field wrap the input in a bordered box instead.
        TextInput = new TextInputTheme
        {
            BorderWidth = new(0),
            BorderColor = Color.Transparent,
            FocusBorderColor = Color.Transparent,
            Background = Color.Transparent,
            PlaceholderColor = On(0.42),
            CursorColor = Accent,
            FontSize = 13.5f,
            // The renderer top-aligns text in the field's content box (no vertical
            // centering), so vertical padding is what visually centres the text.
            Padding = new(6, 0),
            CornerRadius = new(RadiusMd),
        },
        ContextMenu = new ContextMenuTheme
        {
            Background = Surface,
            BorderColor = Neutral700,
            TextColor = Text,
            DisabledTextColor = On(0.40),
            DangerTextColor = DangerText,
            HoverBackground = Neutral900,
            SeparatorColor = Divider,
            ShortcutTextColor = On(0.55),
        },
        // CheckboxElement.ApplyTheme re-applies these every style pass, so per-element
        // colour overrides don't stick — style through the theme instead.
        Checkbox = new CheckboxTheme
        {
            Size = 16f,
            CornerRadius = new(RadiusSm),
            BorderColor = On(0.42),
            CheckedBackground = Accent,
            CheckMarkColor = Accent100,
        },
    };

    private static Color Hex(string hex) => Color.FromHex(hex);
}
