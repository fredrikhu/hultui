using HultUI.Core.Primitives;
using HultUI.Core.Styling;

namespace Meridian.Theme;

/// <summary>
/// The <b>Azimuth</b> design system — the source of truth for Meridian's look.
/// Values mirror the token sheet in <c>docs/design/meridian/01 Azimuth Tokens.dc.html</c>:
/// cool cyan on blue-black, compact rows, squared corners, mono wherever the data is
/// technical. Sibling to Nocturne (Vesper) and the Junction register: same shape, its
/// own personality.
///
/// <para>Two rules the sheet states outright, kept here so every view honours them:
/// the accent never floods a region (at most a 2dip edge, a 16% tint, or a glyph), and
/// a status colour never appears without its glyph (✓ ✗ ! −) — including the method
/// chips, which always carry their text.</para>
/// </summary>
public static class Azimuth
{
    // ── Surfaces & text (elevation order: Bg → Surface → Raised) ─────────
    public static readonly Color Bg = Hex("#0C1118");            // app ground
    public static readonly Color Surface = Hex("#121A24");       // rail, titlebar, inputs
    public static readonly Color Raised = Hex("#18222F");        // selection, hover, expanded rows
    public static readonly Color Border = Hex("#223041");        // region separators
    public static readonly Color BorderStrong = Hex("#33455C");  // pane-flip divider
    public static readonly Color TextFaint = Hex("#586878");
    public static readonly Color TextMuted = Hex("#8A99AC");
    public static readonly Color TextDim = Hex("#C6D0DC");
    public static readonly Color Text = Hex("#E8EEF5");

    // ── Accent ramp ──────────────────────────────────────────────────────
    public static readonly Color Accent100 = Hex("#D9F6FA");     // text on 700/900
    public static readonly Color Accent300 = Hex("#86DFEB");     // links, var chips
    public static readonly Color Accent = Hex("#38C5D8");        // the accent (az-accent-500)
    public static readonly Color Accent700 = Hex("#1E7E8D");     // pressed, selection, find-match fill
    public static readonly Color Accent900 = Hex("#0E3B44");     // tint fills

    // ── Method register (chips always carry their text) ──────────────────
    public static readonly Color MethodGet = Hex("#4EA1F2");
    public static readonly Color MethodPost = Hex("#4FBE7E");
    public static readonly Color MethodPut = Hex("#E8763F");
    public static readonly Color MethodPatch = Hex("#A78BE0");
    public static readonly Color MethodDelete = Hex("#E5636B");  // "DEL" at tree scale

    public static Color MethodColor(string method) => method.ToUpperInvariant() switch
    {
        "GET" => MethodGet,
        "POST" => MethodPost,
        "PUT" => MethodPut,
        "PATCH" => MethodPatch,
        "DELETE" => MethodDelete,
        _ => TextMuted,
    };

    // ── Status register (glyph rides along: ✓ ✗ ! −) ─────────────────────
    // Infra failures (DNS, refused, TLS, timeout, unreachable) are Warn, never
    // Fail — a failed assertion and an unreachable host must not share a colour.
    public static readonly Color StatusPass = Hex("#4FBE7E");    // 2xx, assertion pass
    public static readonly Color StatusFail = Hex("#E5636B");    // 5xx, assertion fail
    public static readonly Color StatusWarn = Hex("#E8C14A");    // 4xx, unresolved var, infra
    public static readonly Color StatusSkip = Hex("#8A99AC");    // skipped; 3xx muted

    // ── Geometry ─────────────────────────────────────────────────────────
    public const float Radius1 = 3f;      // chips, buttons, inputs
    public const float Radius2 = 6f;      // cards, panes

    // Spacing scale az-s-1…5.
    public const float S1 = 4f;
    public const float S2 = 8f;
    public const float S3 = 12f;
    public const float S4 = 16f;
    public const float S5 = 24f;

    public const float RowList = 24f;     // tree / list rows
    public const float RowRun = 28f;      // runner / history rows
    public const float RowForm = 32f;     // form rows
    public const float RailWidth = 48f;
    public const float TreeWidth = 264f;
    public const float ResponseRight = 560f;
    public const float ResponseBottom = 380f;

    /// <summary>The wordmark's letter tracking: 0.35em.</summary>
    public static float WordmarkTracking(float fontSize) => fontSize * 0.35f;

    // ── Helpers ──────────────────────────────────────────────────────────

    /// <summary>An arbitrary colour scaled to <paramref name="pct"/> opacity (0..1).</summary>
    public static Color Alpha(Color c, double pct) =>
        new(c.R, c.G, c.B, (byte)Math.Clamp(pct * 255.0, 0, 255));

    /// <summary>The sheet's standard chip/selection tint: the colour at 16%.</summary>
    public static Color Tint(Color c) => Alpha(c, 0.16);

    /// <summary>Hover tint where a surface change won't do: 12% accent.</summary>
    public static readonly Color HoverAccent = Alpha(Hex("#38C5D8"), 0.12);

    /// <summary>Maps Azimuth roles onto the framework theme (context menus, focus…).</summary>
    public static HultUI.Core.Styling.Theme ToHultTheme() => new()
    {
        Background = Bg,
        TextColor = Text,
        Surface = Surface,
        Primary = Accent,
        Secondary = TextMuted,
        // What the framework outlines and fills its own controls with; the toast
        // keeps the raised surface and strong border it always had.
        Border = BorderStrong,
        SurfaceVariant = Raised,
        Danger = StatusFail,
        // Meridian's button families, as the views drew them by hand before ButtonElement
        // took over: outlined or bare on no fill, lit by a wash of the accent or the raised
        // step. Danger follows the same shape so a framework dialog's destructive action
        // belongs too.
        Button = new ButtonTheme
        {
            CornerRadius = new(Radius1),
            Padding = new(6, S4, 6, S4),
            FontSize = 12.5f,
            FontWeight = FontWeight.Medium,
            DisabledOpacity = 0.45f,
            Primary = new ButtonStyle
            {
                Background = Color.Transparent, Border = Accent, Text = Accent,
                HoverBackground = Tint(Accent), PressedBackground = Alpha(Accent, 0.24),
            },
            Secondary = new ButtonStyle
            {
                Border = Border, Text = TextDim,
                HoverBackground = Raised, PressedBackground = Raised,
            },
            Ghost = new ButtonStyle
            {
                Text = TextMuted,
                HoverBackground = Raised, PressedBackground = Raised,
            },
            Danger = new ButtonStyle
            {
                Background = Color.Transparent, Border = StatusFail, Text = StatusFail,
                HoverBackground = Tint(StatusFail), PressedBackground = Alpha(StatusFail, 0.24),
            },
        },
        Toast = new ToastTheme { Background = Raised },
        // Inputs are az-surface on az-border per the sheet, but StyleResolver
        // re-applies the theme every pass (per-element border overrides don't
        // stick), so fields stay flush here and views wrap them in a bordered
        // box — the same arrangement the other two apps use.
        TextInput = new TextInputTheme
        {
            BorderWidth = new(0),
            BorderColor = Color.Transparent,
            FocusBorderColor = Color.Transparent,
            Background = Color.Transparent,
            PlaceholderColor = TextFaint,
            CursorColor = Accent,
            FontSize = 13f,
            Padding = new(6, 0),
            CornerRadius = new(Radius1),
        },
        ContextMenu = new ContextMenuTheme
        {
            Background = Surface,
            BorderColor = BorderStrong,
            TextColor = Text,
            DisabledTextColor = Alpha(Text, 0.45),
            DangerTextColor = StatusFail,
            HoverBackground = Raised,
            SeparatorColor = Border,
            ShortcutTextColor = TextMuted,
        },
        Checkbox = new CheckboxTheme
        {
            Size = 16f,
            CornerRadius = new(Radius1),
            BorderColor = TextFaint,
            CheckedBackground = Accent,
            CheckMarkColor = Accent100,
        },
        // The response find bar, as MeridianApp.Response drew it by hand: a ground field with the
        // query in mono, the count in mono, and the small secondary buttons the request bar uses.
        // A key named anywhere: an outline, no fill, the keys in mono.
        Kbd = new KbdTheme
        {
            Background = Color.Transparent, Border = Border, Text = TextMuted,
            FontFamily = Meridian.Fonts.Mono, FontSize = 10f,
            Padding = new Thickness(2, 5, 2, 5), CornerRadius = Radius1,
        },
        // The palette as MeridianApp.Palette drew it: a card under the top edge, the group in a
        // mono column, the chosen row raised.
        CommandPalette = new CommandPaletteTheme
        {
            Width = 560f, MaxHeight = 420f, TopOffset = 90f,
            Scrim = Alpha(Bg, 0.62),
            Background = Surface, Border = BorderStrong, CornerRadius = Radius2, Divider = Border,
            HeaderHeight = 38f, FieldFontSize = 13f,
            ListPadding = new Thickness(S1), RowHeight = RowRun, DetailRowHeight = RowRun + 14f,
            RowCornerRadius = Radius1, RowPadding = new Thickness(0, S3, 0, S3), RowGap = S2,
            SelectedBackground = Raised, HoverBackground = Raised,
            TitleFontSize = 12.5f, Title = TextDim,
            Group = TextFaint, GroupFontFamily = Meridian.Fonts.Mono, GroupFontSize = 9.5f, GroupWidth = 84f,
            Muted = TextMuted, MutedFontSize = 12f,
        },
        FindBar = new FindBarTheme
        {
            Background = Surface,
            Divider = Border,
            Padding = new Thickness(4, S3, 4, S3),
            Gap = S2,
            Field = new TextInputStyle
            {
                Background = Bg, Border = Border, FocusBorder = Border,
                BorderWidth = new Thickness(1f), CornerRadius = new CornerRadius(Radius1),
                Padding = new Thickness(0, S2, 0, S2), FontSize = 11.5f,
            },
            FieldFontFamily = Meridian.Fonts.Mono,
            StatusFontFamily = Meridian.Fonts.Mono,
            StatusFontSize = 10.5f,
            StatusColor = TextMuted,
            StatusErrorColor = StatusWarn,
            ToggleFontFamily = Meridian.Fonts.Mono,
            Toggle = SmallButton,
            ToggleOn = SmallButton,
            Button = SmallButton,
        },
    };

    // The same as Ui.Small — the request bar's small button — so the theme stands on its own.
    private static readonly ButtonStyle SmallButton = new()
    {
        Padding = new Thickness(4, S2, 4, S2), FontSize = 11.5f, FontWeight = FontWeight.Regular,
    };

    private static Color Hex(string hex) => Color.FromHex(hex);
}
