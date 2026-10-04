using HultUI.Core.Primitives;
using HultUI.Core.Styling;
using HultUI.Themes;

namespace VesperCommander.Theme;

/// <summary>
/// The colour register the whole app draws from. Named for <b>Nocturne</b>, the design
/// system Vesper was built in and still opens in, but it is now a window onto whichever
/// theme is selected — Gruvbox, Nord, Solarized and the rest live in
/// <see cref="ThemeCatalog"/>, and <see cref="Palette"/> resolves one into the values below.
///
/// <para>Every member is a property rather than a field, and that is load-bearing: a
/// <c>static readonly</c> freezes at type-init, so a colour captured that way would keep
/// the startup theme for the life of the process. Call <see cref="Use"/>, then rebuild.</para>
///
/// <para>Colours the design expresses as <c>color-mix(in srgb, X N%, transparent)</c> are
/// reproduced by scaling the source colour's alpha to <c>N%</c> (a source-over mix with
/// transparent is exactly the colour at that alpha). Use <see cref="On"/> for the common
/// text steps and <see cref="Alpha"/> for arbitrary tints.</para>
/// </summary>
public static class Nocturne
{
    private static Palette _p = Palette.NocturneDefault;

    /// <summary>The theme currently in force.</summary>
    public static NamedTheme Theme => _p.Theme;

    /// <summary>
    /// Switch to <paramref name="themeId"/> (unknown ids fall back to Nocturne), with
    /// <paramref name="accentIndex"/> as the accent override — 0 for the theme's own.
    ///
    /// <para>Three things have to follow a call to this, and none of them happen here:
    /// <c>Icons.InvalidateCache()</c> because icons are rasterized against the ramp,
    /// <c>app.Theme = ToHultTheme()</c> because the framework's own widgets carry their own
    /// copy, and a rebuild.</para>
    /// </summary>
    public static void Use(string? themeId, int accentIndex)
    {
        _p = Palette.For(ThemeCatalog.ById(themeId), accentIndex);
        HultUI.Syntax.SyntaxHighlighter.SetTheme(_p.Theme.SyntaxTheme);
    }

    // ── Ground / surfaces ────────────────────────────────────────────────
    public static Color Bg => _p.Bg;               // app ground
    public static Color Surface => _p.Surface;     // raised bars, menus, palette
    public static Color Text => _p.Text;
    public static Color Accent => _p.Accent;

    /// <summary>1px hairline borders — text at 16%.</summary>
    public static Color Divider => Alpha(Text, 0.16);

    // ── Neutral ramp ─────────────────────────────────────────────────────
    public static Color Neutral100 => _p.Neutrals[0];
    public static Color Neutral200 => _p.Neutrals[1];
    public static Color Neutral300 => _p.Neutrals[2];
    public static Color Neutral400 => _p.Neutrals[3];
    public static Color Neutral500 => _p.Neutrals[4];
    public static Color Neutral600 => _p.Neutrals[5];
    public static Color Neutral700 => _p.Neutrals[6];
    public static Color Neutral800 => _p.Neutrals[7];
    public static Color Neutral900 => _p.Neutrals[8];

    // ── Accent ramp ──────────────────────────────────────────────────────
    public static Color Accent100 => _p.Accents[0];
    public static Color Accent200 => _p.Accents[1];
    public static Color Accent300 => _p.Accents[2];
    public static Color Accent400 => _p.Accents[3];
    public static Color Accent500 => _p.Accents[4];
    public static Color Accent600 => _p.Accents[5];
    public static Color Accent700 => _p.Accents[6];
    public static Color Accent800 => _p.Accents[7];
    public static Color Accent900 => _p.Accents[8];

    // ── File-manager literals (outside the ramps) ────────────────────────
    public static Color MarkRed => _p.MarkRed;
    public static Color MarkRedDim => _p.MarkRedDim;

    /// <summary>Directory rows render in accent-200 (design's <c>.dir</c>).</summary>
    public static Color DirText => Accent200;

    // ── Diff / sync change colors ────────────────────────────────────────
    // Deletions reuse the marked-file red so the two reds in the app are one red; additions
    // get the theme's green; changed lines tint with the accent.
    public static Color DiffGreen => _p.DiffGreen;          // add markers, new-file rail
    public static Color DiffGreenText => _p.DiffGreenText;  // added-line text
    public static Color DiffAddBg => Alpha(DiffGreen, 0.10);
    public static Color DiffDelBg => Alpha(MarkRed, 0.22);
    public static Color DiffChangeBg => Alpha(Accent500, 0.09);
    public static Color DiffChangeMark => Alpha(Accent500, 0.28);   // intra-line highlight

    // ── Find-in-file highlight ───────────────────────────────────────────
    // Amber rather than the accent: a search hit lands on top of the same code the diff
    // tints with the accent, and two accent-coloured backgrounds over each other say
    // nothing about which is which. Every hit takes the dim tint; the one the cursor is
    // on takes the bright one, so it is findable in a screenful of matches.
    public static Color MarkAmber => _p.MarkAmber;
    public static Color FindHitBg => Alpha(MarkAmber, 0.24);
    public static Color FindCurrentBg => Alpha(MarkAmber, 0.62);

    // ── Radii ────────────────────────────────────────────────────────────
    public const float RadiusSm = 4f;
    public const float RadiusMd = 8f;
    public const float RadiusLg = 14f;
    public const float RadiusRow = 6f;    // small rows / segments / tabs
    public const float RadiusWindow = 12f;

    // ── Helpers ──────────────────────────────────────────────────────────

    /// <summary>The app text colour at <paramref name="pct"/> opacity (0..1) —
    /// the design's <c>color-mix(text N%, transparent)</c> muted steps.</summary>
    public static Color On(double pct) => Alpha(Text, pct);

    /// <summary>An arbitrary colour scaled to <paramref name="pct"/> opacity (0..1).</summary>
    public static Color Alpha(Color c, double pct) => ColorMath.Alpha(c, pct);

    /// <summary>Maps Nocturne roles onto the framework theme (context menus, focus, fields).</summary>
    public static HultUI.Core.Styling.Theme ToHultTheme() => new()
    {
        Background = Bg,
        TextColor = Text,
        Surface = Surface,
        Primary = Accent,
        Secondary = On(0.60),
        // What the framework outlines its own fields with — a number field, a dropdown —
        // matching the hairline the app draws round its text fields.
        Border = Neutral700,
        SurfaceVariant = Neutral800,
        Danger = MarkRed,
        // Vesper's button families, as Ui.cs drew them by hand before ButtonElement took
        // over: every one outlined or bare on no fill, lit by a wash of its own colour.
        // The same shapes as Junction's, with Vesper's slightly wider padding.
        Button = new ButtonTheme
        {
            CornerRadius = new(RadiusMd),
            Padding = new(5, 12, 5, 12),
            FontSize = 12.5f,
            FontWeight = FontWeight.Medium,
            DisabledOpacity = 0.4f,
            Primary = new ButtonStyle
            {
                Background = Color.Transparent, Border = Accent, Text = Accent,
                HoverBackground = Alpha(Accent, 0.12), PressedBackground = Alpha(Accent, 0.20),
            },
            Secondary = new ButtonStyle
            {
                Border = Divider, Text = Text,
                HoverBackground = Alpha(Text, 0.07), PressedBackground = Alpha(Text, 0.14),
            },
            Ghost = new ButtonStyle
            {
                Text = Accent,
                HoverBackground = Alpha(Accent, 0.10), PressedBackground = Alpha(Accent, 0.18),
            },
            Danger = new ButtonStyle
            {
                Background = Color.Transparent, Border = MarkRed, Text = MarkRed,
                HoverBackground = Alpha(MarkRed, 0.12), PressedBackground = Alpha(MarkRed, 0.20),
            },
        },
        // Fields are flush and borderless by default — StyleResolver re-applies
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
            Padding = new(6, 0),
            CornerRadius = new(RadiusMd),
        },
        ContextMenu = new ContextMenuTheme
        {
            Background = Surface,
            BorderColor = Neutral700,
            TextColor = Text,
            DisabledTextColor = On(0.40),
            HoverBackground = Neutral900,
            SeparatorColor = Divider,
            ShortcutTextColor = On(0.55),
        },
        Checkbox = new CheckboxTheme
        {
            Size = 16f,
            CornerRadius = new(RadiusSm),
            BorderColor = On(0.42),
            CheckedBackground = Accent,
            CheckMarkColor = Accent100,
        },
        // The switch Options always had: a 30×17 pill, no outline, accent when on.
        ToggleSwitch = new ToggleSwitchTheme
        {
            TrackWidth = 30f,
            TrackHeight = 17f,
            KnobSize = 13f,
            OnTrack = Accent700,
            // Text-relative rather than a ramp step: Neutral800 sits on the ground in
            // schemes whose neutral ramp is short (Dracula), and an off switch vanished.
            OffTrack = On(0.14),
            OffBorderWidth = 0f,
            OnKnob = Accent200,
            OffKnob = On(0.55),
        },
        ProgressBar = new ProgressBarTheme
        {
            Height = 5f,
            Track = Neutral900,
        },
        // The find bar over the viewer and the comparison, as CommanderApp.FindBar drew it by
        // hand: the count in monospace, the option chips as outlined mono labels lit in the
        // accent, and small outlined icon buttons that wash on hover.
        FindBar = new FindBarTheme
        {
            Background = Surface,
            Divider = Divider,
            StatusFontFamily = VesperCommander.Fonts.Mono,
            StatusFontSize = 10.5f,
            StatusColor = On(0.50),
            StatusErrorColor = MarkRed,
            ToggleFontFamily = VesperCommander.Fonts.Mono,
            Toggle = new ButtonStyle
            {
                Padding = new Thickness(3, 7, 3, 7), CornerRadius = new(4f), FontSize = 10f,
                FontWeight = FontWeight.Regular,
                Background = Color.Transparent, Border = Neutral700, Text = Neutral300,
                HoverBackground = Neutral900,
            },
            ToggleOn = new ButtonStyle
            {
                Padding = new Thickness(3, 7, 3, 7), CornerRadius = new(4f), FontSize = 10f,
                FontWeight = FontWeight.Regular,
                Background = Accent800, Border = Accent600, Text = Accent200,
            },
            Button = new ButtonStyle
            {
                Padding = new Thickness(2, 7, 2, 7), CornerRadius = new(4f),
                Background = Color.Transparent, Border = Neutral700, HoverBackground = Neutral900,
            },
        },
    };
}
