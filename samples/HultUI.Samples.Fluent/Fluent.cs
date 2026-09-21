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
    //
    // The title bar and the nav pane sit on this. It is flat, and that is not a simplification:
    // measured on the Gallery, the whole top and left is a uniform #202020. An earlier version
    // of this sample put a slight vertical gradient here on the theory that Mica fades — it does
    // not, and the gradient was the most visible wrong thing in the window.
    //
    // Real Mica samples the desktop wallpaper, blurs it hard and desaturates what is left. There
    // is no wallpaper behind an off-screen surface, so this stands in for it as a flat colour.
    public required Color Backdrop { get; init; }

    /// <summary>The content pane — WinUI's <c>LayerFillColorDefault</c>, one step off the
    /// backdrop so the page reads as sitting on the window rather than being it.</summary>
    public required Color Layer { get; init; }

    // ── Surfaces ──
    public required Color Card { get; init; }               // CardBackgroundFillColorDefault
    public required Color CardHover { get; init; }          // CardBackgroundFillColorSecondary
    public required Color CardStroke { get; init; }         // CardStrokeColorDefault

    /// <summary>
    /// The same stroke where a surface meets the <see cref="Backdrop"/> rather than the
    /// <see cref="Layer"/> — the content pane's own outline, and nothing else here.
    ///
    /// <para>One token, two values, because <c>CardStrokeColorDefault</c> is not a colour: it is
    /// black at 10% (6% in light), and a WinUI border paints its background <i>inside</i> the
    /// stroke, so the stroke lands on whatever is behind the element. A border here paints over
    /// the element's own fill, the way CSS does with <c>border-box</c>, so the compositing has to
    /// be done up front — once against each surface a stroke can sit on.</para>
    ///
    /// <para>Measured on the Gallery, and the difference is visible: a card on the #272727 pane
    /// is outlined #232323, while the pane on the #202020 backdrop is outlined #1D1D1D —
    /// <i>darker than the backdrop it sits on</i>. Using the card's value for both puts a line
    /// lighter than the backdrop down the side of the window, which reads as a seam.</para>
    /// </summary>
    public required Color LayerStroke { get; init; }
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

    /// <summary>The faint fill inside an unchecked toggle track or checkbox —
    /// <c>ControlAltFillColorSecondary</c>. Barely visible on purpose, and missing it makes an
    /// off switch look like a hole rather than a control.</summary>
    public required Color ControlAltFill { get; init; }      // ControlAltFillColorSecondary

    // ── Subtle (nav items, icon buttons, list rows) ──
    //
    // Tertiary is always *closer to the ground* than secondary — less white over a dark theme,
    // less black over a light one. So a nav item that is both selected and hovered gets quieter
    // rather than louder, which is the opposite of the intuition that a second state should add
    // emphasis, and is measurable: on the Gallery, hover is #2D2D2D and selected-hovered #292929
    // against a #202020 ground.
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

    /// <summary>An accent button's own border is a highlight rather than an outline: white
    /// along the top, black along the bottom, both at a few percent over the accent itself.</summary>
    public Color AccentStrokeTop => new(255, 255, 255, (byte)(IsDark ? 20 : 36));
    public Color AccentStrokeBottom => new(0, 0, 0, (byte)(IsDark ? 36 : 64));

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
            Backdrop = Hex("#202020"),
            Layer = Hex("#272727"),
            Card = Hex("#2D2D2D"),
            CardHover = Hex("#323232"),
            // Darker than the card, not lighter: CardStrokeColorDefault is black at 10% in dark
            // mode, so a card is outlined by a line that recedes into the page. Measured on real
            // Settings — page #1A2226, card #262D31, stroke #171F22, below both.
            //
            // Both values are that 10% black composited by hand: over Layer for a card, over
            // Backdrop for the pane. FluentSampleTests derives them, so changing a surface
            // without recomputing its stroke fails rather than drifting.
            CardStroke = Hex("#232323"),
            LayerStroke = Hex("#1D1D1D"),
            Divider = Hex("#333333"),
            Flyout = Hex("#2C2C2C"),
            ControlFill = Hex("#373737"),
            ControlHover = Hex("#3D3D3D"),
            ControlPressed = Hex("#343434"),
            ControlInputActive = Hex("#1F1F1F"),
            // Measured off the WinUI Gallery: in dark mode the *top* edge is the brighter one
            // and the bottom recedes (button fill #2D2D2D, top #353535, bottom #303030). The rule
            // is that the bottom edge always reads as shadow — which in light means darker, and
            // in dark means the top is the highlight. Having it the other way round is subtle
            // and wrong, and it is the kind of thing only a side-by-side catches.
            ControlStroke = Hex("#4C4C4C"),
            ControlStrokeBottom = Hex("#484848"),
            ControlStrong = new Color(255, 255, 255, 139),
            ControlAltFill = Hex("#292929"),
            SubtleHover = Hex("#2D2D2D"),
            SubtlePressed = Hex("#292929"),
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
            Backdrop = Hex("#F3F3F3"),
            Layer = Hex("#F9F9F9"),
            Card = Hex("#FBFBFB"),
            CardHover = Hex("#F6F6F6"),
            CardStroke = Hex("#EAEAEA"),
            LayerStroke = Hex("#E5E5E5"),
            Divider = Hex("#E5E5E5"),
            Flyout = Hex("#F9F9F9"),
            ControlFill = Color.White,
            ControlHover = Hex("#F9F9F9"),
            ControlPressed = Hex("#F5F5F5"),
            ControlInputActive = Color.White,
            ControlStroke = Hex("#E1E1E1"),
            ControlStrokeBottom = Hex("#CFCFCF"),
            ControlStrong = new Color(0, 0, 0, 156),
            ControlAltFill = Hex("#F2F2F2"),
            SubtleHover = Hex("#EAEAEA"),
            SubtlePressed = Hex("#EEEEEE"),
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

        // Fluent's fills and strokes per family, so Controls.Button and the framework's own
        // dialog buttons read the same tokens. The border here is the bottom edge of the
        // two-tone stroke — the darker line that seats a control on the page; the lighter
        // top edge is the inner box Controls.Button adds, since a box has one border colour.
        Button = new ButtonTheme
        {
            CornerRadius = new CornerRadius(Fluent.ControlRadius),
            Padding = new Thickness(5, 12, 6, 12),
            FontSize = Fluent.BodySize,
            FontWeight = FontWeight.Regular,
            Primary = new ButtonStyle
            {
                Background = Accent, HoverBackground = AccentHover, PressedBackground = AccentPressed,
                Text = TextOnAccent, Border = AccentStrokeBottom,
            },
            Secondary = new ButtonStyle
            {
                Background = ControlFill, HoverBackground = ControlHover, PressedBackground = ControlPressed,
                Text = TextPrimary, Border = ControlStrokeBottom,
            },
            Ghost = new ButtonStyle
            {
                Background = Color.Transparent, HoverBackground = SubtleHover, PressedBackground = SubtlePressed,
                Text = TextPrimary,
            },
            Danger = new ButtonStyle
            {
                Background = Critical, Text = TextOnAccent, Border = AccentStrokeBottom,
            },
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
            BorderWidth = new Thickness(1),
            FontSize = Fluent.BodySize,
            Background = ControlFill,
            BorderColor = ControlStroke,

            // Measured off the Gallery, and not what a button does. A dark WinUI text box has
            // #303030 sides and a #9A9A9A bottom — ControlStrongFillColorDefault, not a slightly
            // darker stroke. It is the loudest thing about the control at rest, and it is what a
            // text field is recognised by; drawn as an ordinary border it looks disabled.
            UnderlineColor = ControlStrong,
            UnderlineThickness = 1f,

            // On focus the accent grows along the bottom to 2px and the other three sides do not
            // move at all. Lighting the whole ring is the obvious thing to do and is wrong.
            FocusUnderlineColor = Accent,
            FocusUnderlineThickness = 2f,
            FocusBorderColor = ControlStroke,

            // And the fill goes *darker* on a dark theme — ControlFillColorInputActive, which
            // exists to make the field read as a hole you are typing into.
            FocusBackground = ControlInputActive,
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

    /// <summary>The third optical size, for 12px and below. Easy to miss — the Gallery's type
    /// ramp assigns Caption to <i>Small</i>, not to Text, and using Text at 12px gives away that
    /// the ramp was copied by size alone.</summary>
    public const string SmallFont = "Segoe UI Variable Small";

    // Size and line height, from the Gallery's type ramp. The line heights are the half that
    // gets forgotten: the ramp is published as "12/16", "14/20", "20/28", "28/36", and leaving
    // them to the font's own metrics puts multi-line body copy on the wrong rhythm — visible
    // the moment a description wraps, which in a page of settings cards is most of them.
    public const float CaptionSize = 12f;
    public const float CaptionLineHeight = 16f;
    public const float BodySize = 14f;
    public const float BodyLineHeight = 20f;
    public const float SubtitleSize = 20f;
    public const float SubtitleLineHeight = 28f;
    public const float TitleSize = 28f;
    public const float TitleLineHeight = 36f;

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
