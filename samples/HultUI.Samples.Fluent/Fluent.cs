using HultUI.Core.Primitives;
using HultUI.Core.Styling;

namespace HultUI.Samples.Fluent;

/// <summary>
/// The Windows 11 / WinUI 3 design language, written out as the handful of values a Fluent
/// surface is actually built from.
///
/// <para>The names are Microsoft's, not invented ones: <c>CardBackgroundFillColorDefault</c>,
/// <c>ControlStrokeColorSecondary</c>, <c>SubtleFillColorSecondary</c>. Keeping them makes
/// the sample checkable against the real thing — anyone can open the WinUI colour table and
/// see whether a value here is the published one. The values themselves are the composited
/// results: WinUI publishes most of them as white or black at some percentage, which only
/// means anything once you know what is underneath, and a flat colour is what the renderer
/// wants anyway.</para>
///
/// <para>This is a sample, so everything is literal. A real app would generate the ramps —
/// see <c>HultUI.Themes</c>, which does exactly that for the schemes the two shipped apps
/// share.</para>
/// </summary>
public sealed record FluentPalette
{
    public required bool IsDark { get; init; }

    // ── Backdrop ──
    // Real Mica samples the desktop wallpaper, blurs it hard and desaturates what is left.
    // There is no wallpaper behind an off-screen surface and no blur behind a sample, so
    // this is the part that is deliberately faked: a base colour plus a very slight vertical
    // tint, which is what Mica reduces to once the wallpaper is out of focus anyway.
    public required Color BackdropTop { get; init; }
    public required Color BackdropBottom { get; init; }

    /// <summary>The content pane — WinUI's <c>LayerFillColorDefault</c>, one step off the
    /// backdrop so the page reads as sitting on the window rather than being it.</summary>
    public required Color Layer { get; init; }

    // ── Surfaces ──
    public required Color Card { get; init; }               // CardBackgroundFillColorDefault
    public required Color CardHover { get; init; }          // CardBackgroundFillColorSecondary
    public required Color CardStroke { get; init; }         // CardStrokeColorDefault
    public required Color Divider { get; init; }            // DividerStrokeColorDefault
    public required Color Flyout { get; init; }             // AcrylicBackgroundFillColorDefault

    // ── Controls ──
    public required Color ControlFill { get; init; }        // ControlFillColorDefault
    public required Color ControlHover { get; init; }       // ControlFillColorSecondary
    public required Color ControlPressed { get; init; }     // ControlFillColorTertiary
    public required Color ControlInputActive { get; init; } // ControlFillColorInputActive
    public required Color ControlStroke { get; init; }      // ControlStrokeColorDefault

    /// <summary>The bottom edge of a control. WinUI paints the control border as a vertical
    /// gradient that is darker along the bottom, which is what gives a Fluent button its
    /// slight sense of resting on the page rather than floating over it.</summary>
    public required Color ControlStrokeBottom { get; init; } // ControlStrokeColorSecondary

    public required Color ControlStrong { get; init; }       // ControlStrongFillColorDefault

    // ── Subtle (nav items, icon buttons, list rows) ──
    public required Color SubtleHover { get; init; }        // SubtleFillColorSecondary
    public required Color SubtlePressed { get; init; }      // SubtleFillColorTertiary

    // ── Text ──
    public required Color TextPrimary { get; init; }        // TextFillColorPrimary
    public required Color TextSecondary { get; init; }      // TextFillColorSecondary
    public required Color TextTertiary { get; init; }       // TextFillColorTertiary
    public required Color TextDisabled { get; init; }       // TextFillColorDisabled

    // ── Accent ──
    public required Color Accent { get; init; }             // AccentFillColorDefault
    public required Color AccentHover { get; init; }        // AccentFillColorSecondary
    public required Color AccentPressed { get; init; }      // AccentFillColorTertiary

    /// <summary>Black in dark mode. WinUI's dark accent is a pale blue and the text on it is
    /// black — the inversion is one of the most recognisable things about Windows 11, and
    /// getting it backwards is the fastest way to look like something else.</summary>
    public required Color TextOnAccent { get; init; }       // TextOnAccentFillColorPrimary

    // ── Severity (InfoBar) ──
    public required Color Success { get; init; }
    public required Color SuccessBg { get; init; }
    public required Color Caution { get; init; }
    public required Color CautionBg { get; init; }
    public required Color Critical { get; init; }
    public required Color CriticalBg { get; init; }
    public required Color AttentionBg { get; init; }

    public static FluentPalette For(bool dark, AccentPreset accent) => dark
        ? new FluentPalette
        {
            IsDark = true,
            BackdropTop = Hex("#26262A"),
            BackdropBottom = Hex("#1C1C1F"),
            Layer = Hex("#272727"),
            Card = Hex("#2D2D2D"),
            CardHover = Hex("#323232"),
            CardStroke = Hex("#373737"),
            Divider = Hex("#333333"),
            Flyout = Hex("#2C2C2C"),
            ControlFill = Hex("#373737"),
            ControlHover = Hex("#3D3D3D"),
            ControlPressed = Hex("#343434"),
            ControlInputActive = Hex("#1F1F1F"),
            ControlStroke = Hex("#454545"),
            ControlStrokeBottom = Hex("#4F4F4F"),
            ControlStrong = new Color(255, 255, 255, 139),
            SubtleHover = Hex("#2F2F2F"),
            SubtlePressed = Hex("#2A2A2A"),
            TextPrimary = Color.White,
            TextSecondary = new Color(255, 255, 255, 200),
            TextTertiary = new Color(255, 255, 255, 139),
            TextDisabled = new Color(255, 255, 255, 93),
            Accent = accent.Dark,
            AccentHover = Mix(accent.Dark, Color.Black, 0.10f),
            AccentPressed = Mix(accent.Dark, Color.Black, 0.20f),
            TextOnAccent = Color.Black,
            Success = Hex("#6CCB5F"),
            SuccessBg = Hex("#393D1B"),
            Caution = Hex("#FCE100"),
            CautionBg = Hex("#433519"),
            Critical = Hex("#FF99A4"),
            CriticalBg = Hex("#442726"),
            AttentionBg = Hex("#2E2E2E"),
        }
        : new FluentPalette
        {
            IsDark = false,
            BackdropTop = Hex("#F3F3F3"),
            BackdropBottom = Hex("#EBEBEB"),
            Layer = Hex("#F9F9F9"),
            Card = Hex("#FBFBFB"),
            CardHover = Hex("#F6F6F6"),
            CardStroke = Hex("#E5E5E5"),
            Divider = Hex("#E5E5E5"),
            Flyout = Hex("#F9F9F9"),
            ControlFill = Color.White,
            ControlHover = Hex("#F9F9F9"),
            ControlPressed = Hex("#F5F5F5"),
            ControlInputActive = Color.White,
            ControlStroke = Hex("#E1E1E1"),
            ControlStrokeBottom = Hex("#CFCFCF"),
            ControlStrong = new Color(0, 0, 0, 156),
            SubtleHover = Hex("#EDEDED"),
            SubtlePressed = Hex("#E5E5E5"),
            TextPrimary = Hex("#1A1A1A"),
            TextSecondary = Hex("#5D5D5D"),
            TextTertiary = Hex("#8A8A8A"),
            TextDisabled = Hex("#9D9D9D"),
            Accent = accent.Light,
            AccentHover = Mix(accent.Light, Color.White, 0.10f),
            AccentPressed = Mix(accent.Light, Color.White, 0.20f),
            TextOnAccent = Color.White,
            Success = Hex("#0F7B0F"),
            SuccessBg = Hex("#DFF6DD"),
            Caution = Hex("#9D5D00"),
            CautionBg = Hex("#FFF4CE"),
            Critical = Hex("#C42B1C"),
            CriticalBg = Hex("#FDE7E9"),
            AttentionBg = Hex("#F6F6F6"),
        };

    /// <summary>
    /// What the framework's own widgets — checkbox, radio, dropdown, slider, text box, menu,
    /// tooltip — read. They are drawn by HultUI, not by this sample, so without this they
    /// come up in the framework's stock indigo-on-white in the middle of a Fluent window.
    /// Every value is a token from above; nothing here is a second palette.
    /// </summary>
    public Theme ToHultTheme() => new()
    {
        FontFamily = Fluent.BodyFont,
        FontSize = Fluent.BodySize,
        TextColor = TextPrimary,
        Background = Layer,
        Surface = Card,
        SurfaceVariant = ControlFill,
        Primary = Accent,
        PrimaryText = TextOnAccent,
        Secondary = TextSecondary,
        SecondaryText = TextOnAccent,
        Border = CardStroke,
        Danger = Critical,
        DangerText = TextOnAccent,
        FocusRing = Accent,

        Button = new ButtonTheme
        {
            CornerRadius = new CornerRadius(Fluent.ControlRadius),
            Padding = new Thickness(5, 12, 6, 12),
            FontSize = Fluent.BodySize,
            FontWeight = FontWeight.Regular,
            HoverDarken = IsDark ? 0.90f : 1.0f,
            PressedDarken = IsDark ? 0.80f : 1.0f,
        },
        Slider = new SliderTheme
        {
            // WinUI leaves the unfilled track a mid grey rather than a hairline, and puts the
            // accent *inside* a ring the colour of whatever the slider is sitting on.
            TrackColor = ControlStrong,
            FillColor = Accent,
            ThumbColor = Accent,
            ThumbBorderColor = Card,
        },
        Checkbox = new CheckboxTheme
        {
            Size = 20f,
            CornerRadius = new CornerRadius(3f),
            BorderColor = ControlStrong,
            CheckedBackground = Accent,
            CheckMarkColor = TextOnAccent,
        },
        RadioButton = new RadioButtonTheme
        {
            Size = 20f,
            BorderColor = ControlStrong,
            SelectedColor = Accent,
            DotColor = TextOnAccent,
        },
        TextInput = new TextInputTheme
        {
            CornerRadius = new CornerRadius(Fluent.ControlRadius),
            Padding = new Thickness(5, 10, 6, 10),
            // Heavier along the bottom, the same two-tone the buttons get. On focus the
            // whole ring turns accent, which is one step louder than WinUI (it lights the
            // bottom edge only) but is the shape a single border colour can express.
            BorderWidth = new Thickness(1, 1, 2, 1),
            FontSize = Fluent.BodySize,
            Background = ControlFill,
            BorderColor = ControlStroke,
            FocusBorderColor = Accent,
            PlaceholderColor = TextTertiary,
            SelectionColor = new Color(Accent.R, Accent.G, Accent.B, 90),
            CursorColor = TextPrimary,
        },
        Dropdown = new DropdownTheme
        {
            Background = ControlFill,
            BorderColor = ControlStroke,
            TextColor = TextPrimary,
            PlaceholderTextColor = TextTertiary,
            ChevronColor = TextSecondary,
            CornerRadius = Fluent.ControlRadius,
            Padding = new Thickness(5, 11, 6, 11),
            FontSize = Fluent.BodySize,
            MinWidth = 140f,
            PopupGap = 4f,
            // Fluent's drop mark is a stroked chevron, not a solid triangle. Drawn rather than
            // typed, so it does not depend on the UI font shipping the glyph.
            ChevronShape = ChevronShape.Stroke,
            ChevronStrokeWidth = 1f,
        },
        ContextMenu = new ContextMenuTheme
        {
            Background = Flyout,
            BorderColor = IsDark ? Hex("#3B3B3B") : Hex("#E0E0E0"),
            TextColor = TextPrimary,
            DisabledTextColor = TextDisabled,
            HoverBackground = SubtleHover,
            SeparatorColor = Divider,
            ShortcutTextColor = TextTertiary,
        },
        Tooltip = new TooltipTheme
        {
            Background = Flyout,
            TextColor = TextPrimary,
            CornerRadius = Fluent.ControlRadius,
            Padding = new Thickness(5, 8),
            FontSize = Fluent.CaptionSize,
        },
    };

    private static Color Hex(string s) => Color.FromHex(s);

    /// <summary>Blends <paramref name="a"/> toward <paramref name="b"/>. WinUI's hover and
    /// pressed accents are the accent at 90% and 80% over the page, which is this.</summary>
    private static Color Mix(Color a, Color b, float t) => new(
        (byte)(a.R + (b.R - a.R) * t),
        (byte)(a.G + (b.G - a.G) * t),
        (byte)(a.B + (b.B - a.B) * t),
        a.A);
}

/// <summary>
/// One of the accent colours the Windows personalisation page offers. Both values are given
/// rather than derived: Windows computes its light/dark pair in a perceptual space, and a
/// naive lighten of #0078D4 lands on a washed-out blue instead of the cyan-leaning #60CDFF
/// that dark-mode Windows actually uses.
/// </summary>
public readonly record struct AccentPreset(string Name, Color Light, Color Dark)
{
    public static readonly AccentPreset Blue = new("Windows blue", Color.FromHex("#005FB8"), Color.FromHex("#60CDFF"));
    public static readonly AccentPreset Purple = new("Purple", Color.FromHex("#6B4FA8"), Color.FromHex("#C4A5FF"));
    public static readonly AccentPreset Teal = new("Teal", Color.FromHex("#00706B"), Color.FromHex("#62E3D8"));
    public static readonly AccentPreset Green = new("Green", Color.FromHex("#0E7A0B"), Color.FromHex("#6CCB5F"));
    public static readonly AccentPreset Orange = new("Orange", Color.FromHex("#C4581A"), Color.FromHex("#FFB176"));
    public static readonly AccentPreset Red = new("Red", Color.FromHex("#B01F2A"), Color.FromHex("#FF9AA5"));

    public static readonly AccentPreset[] All = [Blue, Purple, Teal, Green, Orange, Red];
}

/// <summary>Metrics and type. WinUI's are a small, fixed set — four radii and six text
/// styles do the whole system, which is most of why it looks consistent.</summary>
public static class Fluent
{
    // Segoe UI Variable is the Windows 11 UI face: Text for body copy, Display for headings,
    // which are optically different cuts rather than the same font at two sizes. Off Windows
    // both fall back to the platform default, so the layout survives and only the texture
    // changes.
    public const string BodyFont = "Segoe UI Variable Text";
    public const string DisplayFont = "Segoe UI Variable Display";

    public const float CaptionSize = 12f;
    public const float BodySize = 14f;
    public const float SubtitleSize = 20f;
    public const float TitleSize = 28f;

    /// <summary>ControlCornerRadius — buttons, text boxes, cards, nav items.</summary>
    public const float ControlRadius = 4f;

    /// <summary>OverlayCornerRadius — flyouts, dialogs, and the window itself.</summary>
    public const float OverlayRadius = 8f;

    /// <summary>Standard control height. Every WinUI control that sits in a row is this tall,
    /// which is what lets a button, a combo box and a text box line up without being told to.</summary>
    public const float ControlHeight = 32f;

    public const float TitleBarHeight = 48f;
    public const float NavPaneWidth = 320f;
}
