using HultUI.Core.Primitives;
using HultUI.Core.Styling;

namespace Reel;

/// <summary>
/// The <b>Emulsion</b> palette — Reel's look. Sibling to Halide (Aperture), Nocturne
/// (Vesper) and Azimuth (Meridian), and deliberately the smallest of them.
///
/// <para>Those apps are rooms full of furniture: rails, grids, tables, dialogs. Reel is a
/// black rectangle with a film in it. Almost every pixel belongs to the picture, so this
/// defines only what the four surfaces that are <i>not</i> the picture need — the empty
/// state, the chapter panel, and the transport bar the media layer draws. A 177-line design
/// system here would be 130 lines of colours nothing ever asks for.</para>
///
/// <para>The one rule: <b>nothing competes with the picture.</b> Chrome sits darker than
/// any frame of video, text is warm rather than blue-white so it reads as paper beside a
/// lit image, and the accent is a mark — an edge, a glyph, a tint — never a filled region.
/// The same rule Halide follows, for the same reason, in a tenth of the space.</para>
/// </summary>
public static class Emulsion
{
    // ── Surfaces ─────────────────────────────────────────────────────────

    /// <summary>The letterbox, and the ground behind everything. Not pure black: a true
    /// #000 makes a dark frame of video look like a hole in the window.</summary>
    public static readonly Color Bg = Hex("#0B0B0E");

    /// <summary>Panels laid over the picture.</summary>
    public static readonly Color Surface = Hex("#141418");

    /// <summary>Hover and the row the playhead is in.</summary>
    public static readonly Color Raised = Hex("#1E1E24");

    public static readonly Color Border = Hex("#282830");

    // ── Text: warm, so it reads as paper beside a lit picture ────────────

    public static readonly Color Text = Hex("#ECEAE4");
    public static readonly Color TextDim = Hex("#BFBBB2");
    public static readonly Color TextMuted = Hex("#8A867E");
    public static readonly Color TextFaint = Hex("#5A5751");

    // ── Accent: cold silver-blue, against Halide's projector amber ───────
    //
    // Aperture is a room with the lights down and a warm accent to match. Reel runs on a
    // desktop next to everything else, and the mark that says "you are here" reads better
    // cold at this size.

    public static readonly Color Accent = Hex("#7FB4D8");
    public static readonly Color AccentDim = Hex("#4E7B97");

    /// <summary>A barely-there wash of the accent, for the row being played. A fill at
    /// full strength would be the brightest thing on screen next to the film.</summary>
    public static readonly Color AccentTint = Hex("#16222B");

    public static readonly Color Warning = Hex("#D8A657");

    // ── Metrics ──────────────────────────────────────────────────────────

    public const float S1 = 4f;
    public const float S2 = 8f;
    public const float S3 = 12f;
    public const float S4 = 16f;
    public const float Radius = 6f;

    /// <summary>The app theme, so that a scrollbar, a menu or a button Reel never styles by
    /// hand still belongs to this palette rather than to the framework's blue-grey default.</summary>
    public static Theme ToHultTheme() => new()
    {
        Background = Bg,
        TextColor = Text,
        Surface = Surface,
        SurfaceVariant = Raised,
        Primary = Accent,
        Secondary = TextMuted,
        Border = Border,
        Button = new ButtonTheme
        {
            CornerRadius = new(Radius),
            Padding = new(8, S4, 8, S4),
            FontSize = 13f,
            FontWeight = FontWeight.Medium,
            // The empty state's "Open…" is the only button in the app, and it is the only
            // thing on screen when it shows — so it may be a fill.
            Primary = new ButtonStyle
            {
                Background = Accent, Border = Color.Transparent, Text = Hex("#0B1A22"),
                HoverBackground = Hex("#9AC8E6"), PressedBackground = AccentDim,
            },
            Secondary = new ButtonStyle
            {
                Background = Raised, Border = Border, Text = TextDim,
                HoverBackground = Hex("#262630"),
            },
        },
    };

    private static Color Hex(string hex) => Color.FromHex(hex);
}
