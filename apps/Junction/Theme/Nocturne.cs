using HultUI.Core.Primitives;
using HultUI.Core.Styling;
using HultUI.Themes;

namespace Junction.Theme;

/// <summary>
/// The colour register the whole app draws from. Named for <b>Nocturne</b>, the design system
/// Junction was built in and still opens in, but it is now a window onto whichever theme is
/// selected — Gruvbox, Nord, Solarized and the rest live in <see cref="ThemeCatalog"/>, and
/// <see cref="Palette"/> resolves one into the values below.
///
/// <para>Every member is a property rather than a field, and that is load-bearing: a
/// <c>static readonly</c> freezes at type-init, so a colour captured that way would keep the
/// startup theme for the life of the process. Call <see cref="Use"/>, then rebuild.</para>
///
/// <para>Colours the design expresses as <c>color-mix(in srgb, X N%, transparent)</c> are
/// reproduced by scaling the source colour's alpha to <c>N%</c> (a source-over mix with
/// transparent is exactly the colour at that alpha). Use <see cref="On"/> for the common text
/// steps and <see cref="Alpha"/> for arbitrary tints.</para>
/// </summary>
public static class Nocturne
{
    private static Palette _p = Palette.NocturneDefault;

    /// <summary>The theme currently in force.</summary>
    public static NamedTheme Theme => _p.Theme;

    /// <summary>
    /// Switch to <paramref name="themeId"/>; an unknown id falls back to Nocturne, which is
    /// what a settings file written by a different build reads as.
    ///
    /// <para>Three things have to follow a call to this, and none of them happen here:
    /// <c>Ui.InvalidateIconCache()</c> because glyphs are rasterized against the ramp,
    /// <c>app.Theme = ToHultTheme()</c> because the framework's own widgets carry their own
    /// copy, and a rebuild.</para>
    /// </summary>
    public static void Use(string? themeId)
    {
        _p = Palette.For(ThemeCatalog.ById(themeId));
        HultUI.Syntax.SyntaxHighlighter.SetTheme(_p.Theme.SyntaxTheme);
    }

    // ── Ground / surfaces ────────────────────────────────────────────────
    public static Color Bg => _p.Bg;              // app ground
    public static Color Surface => _p.Surface;    // raised bars, menus, palette
    public static Color Text => _p.Text;
    public static Color Accent => _p.Accent;
    public static Color Accent2 => _p.Accent2;

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

    // ── Second-accent ramp ───────────────────────────────────────────────
    public static Color Accent2_100 => _p.Accent2s[0];
    public static Color Accent2_200 => _p.Accent2s[1];
    public static Color Accent2_300 => _p.Accent2s[2];
    public static Color Accent2_400 => _p.Accent2s[3];
    public static Color Accent2_500 => _p.Accent2s[4];
    public static Color Accent2_600 => _p.Accent2s[5];
    public static Color Accent2_700 => _p.Accent2s[6];
    public static Color Accent2_800 => _p.Accent2s[7];
    public static Color Accent2_900 => _p.Accent2s[8];

    // ── Radii ────────────────────────────────────────────────────────────
    public const float RadiusSm = 4f;
    public const float RadiusMd = 8f;
    public const float RadiusLg = 14f;
    public const float RadiusRow = 6f;    // small rows / segments
    public const float RadiusWindow = 12f;

    // ── Diff colours ─────────────────────────────────────────────────────
    // The row tint and the intra-line highlight are one hue at two opacities, so a theme
    // cannot end up with a wash and a highlight that disagree about what "added" looks like.
    //
    // The highlight is written as the alpha byte rather than a percentage: the handoff calls
    // it 28% but shipped it as 72, and 0.28 × 255 truncates to 71. One unit is invisible, but
    // "the default scheme is exactly what it was" is worth more than the tidier literal.
    private const byte HlAlpha = 72;

    public static Color DiffAddBg => Alpha(_p.AddBase, 0.13);
    public static Color DiffAddHl => WithAlpha(_p.AddBase, HlAlpha);   // word-level emphasis
    public static Color DiffAddText => _p.AddText;
    public static Color DiffAddSign => _p.AddSign;
    public static Color DiffDelBg => Alpha(_p.DelBase, 0.13);
    public static Color DiffDelHl => WithAlpha(_p.DelBase, HlAlpha);
    public static Color DiffDelText => _p.DelText;
    public static Color DiffDelSign => _p.DelSign;
    public static Color DiffBlankBg => Alpha(Neutral900, 0.40);

    private static Color WithAlpha(Color c, byte a) => new(c.R, c.G, c.B, a);

    // ── Find-in-text highlight ───────────────────────────────────────────
    // Every hit takes the dim tint; the one the cursor is on takes the bright one, so it is
    // findable in a screenful of matches.
    public static Color MarkAmber => _p.MarkAmber;
    public static Color FindHitBg => Alpha(MarkAmber, 0.24);
    public static Color FindCurrentBg => Alpha(MarkAmber, 0.62);

    public static Color DangerText => _p.DangerText;   // destructive menu items
    public static Color DangerIcon => _p.DangerIcon;
    public static Color AddCount => _p.AddCount;       // +additions
    public static Color DelCount => _p.DelCount;       // −deletions

    // ── Helpers ──────────────────────────────────────────────────────────

    /// <summary>The app text colour at <paramref name="pct"/> opacity (0..1) —
    /// the design's <c>color-mix(text N%, transparent)</c> muted steps.</summary>
    public static Color On(double pct) => Alpha(Text, pct);

    /// <summary>An arbitrary colour scaled to <paramref name="pct"/> opacity (0..1).</summary>
    public static Color Alpha(Color c, double pct) => ColorMath.Alpha(c, pct);

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
        // What the framework outlines and fills its own controls with — a toast, a
        // dropdown, a number field: the same hairline and raised step the app uses.
        Border = Neutral700,
        SurfaceVariant = Neutral800,
        Danger = DangerText,
        // Junction's button families, as Ui.cs drew them by hand before ButtonElement took
        // over: every one outlined or bare on no fill, lit by a wash of its own colour. The
        // framework's filled primary would be the loudest thing in any view here. Danger
        // follows the same shape so a framework dialog's destructive action belongs too.
        Button = new ButtonTheme
        {
            CornerRadius = new(RadiusMd),
            Padding = new(5, 10, 5, 10),
            FontSize = 13f,
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
                Padding = new Thickness(3, 4, 3, 4), Text = Accent,
                HoverBackground = Alpha(Accent, 0.10), PressedBackground = Alpha(Accent, 0.18),
            },
            Danger = new ButtonStyle
            {
                Background = Color.Transparent, Border = DangerText, Text = DangerText,
                HoverBackground = Alpha(DangerText, 0.12), PressedBackground = Alpha(DangerText, 0.20),
            },
        },
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
        // A key named anywhere — the Options pane, the chrome, the palette: filled, a hairline
        // outline, mono keys.
        Kbd = new KbdTheme
        {
            Background = Surface, Border = Divider, Text = On(0.55),
            FontFamily = Junction.Fonts.Mono, FontSize = 10f,
            Padding = new Thickness(2, 5, 2, 5), CornerRadius = 4f,
        },
        // The palette as JunctionApp.Palette drew it: a wide card under the top edge, rows with a
        // tinted kind square, a second line and a kind tag, the chosen row washed in the accent.
        CommandPalette = new CommandPaletteTheme
        {
            Width = 600f, MaxHeight = 460f, TopOffset = 96f,
            Scrim = new Color(0, 0, 0, 140),
            Background = Surface, Border = Neutral700, CornerRadius = RadiusLg, Divider = Divider,
            HeaderHeight = 56f, FieldFontSize = 14f,
            ListPadding = new Thickness(6), RowHeight = 42f, DetailRowHeight = 50f,
            RowCornerRadius = RadiusMd, RowPadding = new Thickness(0, 10, 0, 10), RowGap = 11f,
            SelectedBackground = Alpha(Accent, 0.14), HoverBackground = Neutral900,
            TitleFontSize = 13.5f, DetailFontSize = 11f, Detail = On(0.45),
            Group = Neutral100, GroupBackground = Neutral800, GroupFontSize = 9f,
            Muted = On(0.42), MutedFontSize = 11f,
        },
        // The find bar over the diff and the Files view, as JunctionApp.FindBar drew it by hand:
        // the count in the chrome's monospace, the two option chips as outlined mono labels that
        // light up in the accent, and square 26px icon buttons.
        FindBar = new FindBarTheme
        {
            Background = Surface,
            Divider = Divider,
            StatusFontFamily = Junction.Fonts.Mono,
            StatusFontSize = 10.5f,
            StatusColor = On(0.50),
            StatusErrorColor = DangerText,
            ToggleFontFamily = Junction.Fonts.Mono,
            Toggle = new ButtonStyle
            {
                Padding = new Thickness(3, 7, 3, 7), CornerRadius = new(RadiusSm), FontSize = 10f,
                FontWeight = FontWeight.Regular,
                Background = Color.Transparent, Border = Neutral700, Text = Neutral300,
            },
            ToggleOn = new ButtonStyle
            {
                Padding = new Thickness(3, 7, 3, 7), CornerRadius = new(RadiusSm), FontSize = 10f,
                FontWeight = FontWeight.Regular,
                Background = Accent800, Border = Accent600, Text = Accent200,
            },
            Button = new ButtonStyle { Size = 26f, Padding = new Thickness(0) },
        },
    };
}
