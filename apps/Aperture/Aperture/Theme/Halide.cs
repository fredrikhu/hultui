using HultUI.Core.Primitives;
using HultUI.Core.Styling;

namespace Aperture.Theme;

/// <summary>
/// The <b>Halide</b> design system — Aperture's look. Sibling to Nocturne (Vesper), the
/// Junction register and Azimuth (Meridian): same skeleton, its own personality. Where
/// those three are workbenches — cool, dense, mono-first — this one is a room with the
/// lights down: near-black ground, warm off-white text, a single projector-amber accent,
/// and artwork given room to be the brightest thing on screen.
///
/// <para>Two rules every view honours. <b>The artwork is the interface</b>: chrome stays
/// dark and low-contrast so a poster is never the second-brightest thing in its own tile.
/// <b>The accent is a mark, not a surface</b>: a 2dip edge, a tint, a glyph or a progress
/// bar — never a filled region, which at this brightness would glow.</para>
/// </summary>
public static class Halide
{
    // ── Surfaces & text (elevation: Bg → Surface → Raised → Float) ───────
    public static readonly Color Bg = Hex("#0A0A0C");            // app ground, and the player's letterbox
    public static readonly Color Surface = Hex("#131316");       // rail, title bar, cards
    public static readonly Color Raised = Hex("#1C1C21");        // hover, selection, poster backing
    public static readonly Color Float = Hex("#24242B");         // menus, dialogs
    public static readonly Color Border = Hex("#26262D");        // region separators
    public static readonly Color BorderStrong = Hex("#3A3A45");  // dialog edges, focus rings

    public static readonly Color Text = Hex("#F2EFE9");          // warm white: paper, not screen
    public static readonly Color TextDim = Hex("#C6C1B8");
    public static readonly Color TextMuted = Hex("#8C8880");
    public static readonly Color TextFaint = Hex("#5A5751");

    // ── Accent ramp: projector amber ─────────────────────────────────────
    public static readonly Color Accent100 = Hex("#FDF1DC");     // text on a 700/900 fill
    public static readonly Color Accent300 = Hex("#F4C87E");     // links, active rail glyph
    public static readonly Color Accent = Hex("#E8A33D");        // the accent
    public static readonly Color Accent700 = Hex("#96631B");     // pressed, progress track fill
    public static readonly Color Accent900 = Hex("#3A270C");     // tint fills

    // ── Status ───────────────────────────────────────────────────────────
    public static readonly Color StatusOk = Hex("#6FBF73");      // watched, direct play
    public static readonly Color StatusWarn = Hex("#E8C14A");    // transcoding, slow server
    public static readonly Color StatusFail = Hex("#E5636B");    // unreachable, sign-in refused

    // ── Geometry ─────────────────────────────────────────────────────────
    public const float Radius1 = 4f;      // chips, buttons, inputs
    public const float Radius2 = 8f;      // cards, panes
    public const float Radius3 = 12f;     // posters, the detail hero

    public const float S1 = 4f;
    public const float S2 = 8f;
    public const float S3 = 12f;
    public const float S4 = 16f;
    public const float S5 = 24f;
    public const float S6 = 32f;

    public const float TitleBarHeight = 40f;
    public const float RailWidth = 64f;

    /// <summary>Poster tile: 2:3 artwork, the shape every film and series library uses.</summary>
    public const float PosterWidth = 168f;
    public const float PosterHeight = 252f;
    /// <summary>The caption under a poster — title line plus a subline.</summary>
    public const float PosterCaption = 46f;
    public const float PosterGap = S4;

    /// <summary>Continue-watching cards are 16:9 stills, not posters: the frame you stopped
    /// on says more about where you were than the cover art does.</summary>
    public const float StillWidth = 300f;
    public const float StillHeight = 169f;

    /// <summary>The wordmark's letter tracking: 0.32em.</summary>
    public static float WordmarkTracking(float fontSize) => fontSize * 0.32f;

    // ── Helpers ──────────────────────────────────────────────────────────

    public static Color Alpha(Color c, double pct) =>
        new(c.R, c.G, c.B, (byte)Math.Clamp(pct * 255.0, 0, 255));

    /// <summary>The standard chip/selection tint: the colour at 16%.</summary>
    public static Color Tint(Color c) => Alpha(c, 0.16);

    /// <summary>Hover where a surface step won't do: 10% accent.</summary>
    public static readonly Color HoverAccent = Alpha(Hex("#E8A33D"), 0.10);

    /// <summary>The scrim under text laid over artwork — a hero backdrop, a card's caption.
    /// Two stops of black so a bright still never eats its own title. The horizontal one
    /// covers only part of the hero, so it fades out to nothing where it ends: at 15 % it
    /// left a seam down the picture.</summary>
    public static Gradient BackdropScrim(bool vertical = true) => vertical
        ? Gradient.Vertical(Alpha(Bg, 0.10), Alpha(Bg, 0.97))
        : Gradient.Horizontal(Alpha(Bg, 0.96), Alpha(Bg, 0));

    /// <summary>Maps Halide onto the framework theme: buttons, menus, inputs, toasts.</summary>
    public static HultUI.Core.Styling.Theme ToHultTheme() => new()
    {
        Background = Bg,
        TextColor = Text,
        Surface = Surface,
        Primary = Accent,
        Secondary = TextMuted,
        Border = BorderStrong,
        SurfaceVariant = Raised,
        Danger = StatusFail,
        Button = new ButtonTheme
        {
            CornerRadius = new(Radius1),
            Padding = new(8, S4, 8, S4),
            FontSize = 13f,
            FontWeight = FontWeight.Medium,
            DisabledOpacity = 0.45f,
            // Primary is the one place a fill is allowed: Play is the app's verb, and it
            // has to win against a backdrop behind it.
            Primary = new ButtonStyle
            {
                Background = Accent, Border = Color.Transparent, Text = Hex("#231704"),
                HoverBackground = Accent300, PressedBackground = Accent700,
            },
            Secondary = new ButtonStyle
            {
                Background = Alpha(Text, 0.10), Border = Color.Transparent, Text = Text,
                HoverBackground = Alpha(Text, 0.18), PressedBackground = Alpha(Text, 0.24),
            },
            Ghost = new ButtonStyle
            {
                Text = TextDim,
                HoverBackground = Raised, PressedBackground = Raised,
            },
            Danger = new ButtonStyle
            {
                Background = Color.Transparent, Border = StatusFail, Text = StatusFail,
                HoverBackground = Tint(StatusFail), PressedBackground = Alpha(StatusFail, 0.24),
            },
        },
        Toast = new ToastTheme { Background = Float },
        TextInput = new TextInputTheme
        {
            BorderWidth = new(1f),
            BorderColor = Border,
            FocusBorderColor = Accent,
            Background = Surface,
            PlaceholderColor = TextFaint,
            CursorColor = Accent,
            FontSize = 14f,
            Padding = new(8, S3),
            CornerRadius = new(Radius1),
        },
        ContextMenu = new ContextMenuTheme
        {
            Background = Float,
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
            CheckMarkColor = Hex("#231704"),
        },
    };

    /// <summary>The video overlay in Halide's colours: the transport bar belongs to the
    /// app, not to a default.</summary>
    public static HultUI.Media.VideoPlayerTheme PlayerTheme() => new()
    {
        Scrim = Alpha(Bg, 0.82),
        Foreground = Text,
        Accent = Accent,
    };

    private static Color Hex(string hex) => Color.FromHex(hex);
}
