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
    };

    private static Color Hex(string hex) => Color.FromHex(hex);
}
