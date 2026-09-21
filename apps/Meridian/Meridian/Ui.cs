using HultUI.Core.Builders;
using HultUI.Core.Elements;
using HultUI.Core.Layout;
using HultUI.Core.Primitives;
using HultUI.Core.Styling;
using HultUI.Icons;
using Meridian.Theme;

namespace Meridian;

/// <summary>
/// Small, stateless builders shared across the Meridian views — icon glyphs,
/// monospace text, key-hint chips. Keeps the view code declarative and on-theme.
/// </summary>
public static class Ui
{
    /// <summary>
    /// Converts a nominal icon size into the box a vector icon needs to look the
    /// same. Same measured ratio the other apps use: Phosphor's paths ink ~72% of
    /// their box where the design's nominal sizes assumed ~64% inking.
    /// </summary>
    public static float IconBox(float nominal) => MathF.Round(nominal * 0.88f * 2f) / 2f;

    /// <summary>
    /// Device pixels per layout unit, kept in step with the window by
    /// <see cref="MeridianApp"/>. Icons rasterize at this scale so the renderer
    /// never resamples them.
    /// </summary>
    public static float DisplayScale
    {
        get => _displayScale;
        set
        {
            if (MathF.Abs(_displayScale - value) < 0.001f) return;
            _displayScale = value;
            // Every cached raster was built for the old scale and would now be resampled.
            Phosphor.InvalidateCache();
        }
    }
    private static float _displayScale = 1f;

    /// <summary>A Phosphor glyph as vector artwork. Use the constants in
    /// <see cref="Icons"/> so a typo is caught by MeridianIconTests.</summary>
    public static Element Icon(string name, float size, Color color) =>
        Phosphor.Draw(name, IconBox(size), color, DisplayScale);

    public static TextElement Mono(string text, float size, Color color) =>
        Text.New(text).FontFamily(Fonts.Mono).FontSize(size).TextColor(color);

    /// <summary>
    /// A chip's label — one line, always. <c>FlexShrink(0)</c> on the chip box
    /// is not enough on its own: in a tight row the chip is <i>measured</i>
    /// under the leftover width, the label wraps during that measurement, and
    /// a no-shrink chip then faithfully keeps the two-line size. MaxLines(1)
    /// makes it ellipsize instead. (Junction hit exactly this — a remote chip
    /// rendered as "origi/n" — so every chip here bakes in both halves.)
    /// </summary>
    public static TextElement ChipLabel(string text, float size, Color color) =>
        Mono(text, size, color).TextOverflow(TextOverflow.Ellipsis).MaxLines(1);

    /// <summary>
    /// Declarative hover tint (Interaction system): re-applied after any rebuild, so
    /// it survives mid-hover reconciliation; un-hover restores the element's base
    /// snapshot. <paramref name="normal"/> documents the resting colour; the restore
    /// itself comes from the base snapshot.
    /// </summary>
    public static T HoverBg<T>(this T el, Color hover, Color normal) where T : BoxElement =>
        el.OnHover(b => b.BackgroundColor = hover);

    /// <summary>
    /// A box the pointer passes straight through — CSS <c>pointer-events: none</c>.
    /// Hit testing skips the whole subtree, so a decorative overlay can span the
    /// window without stealing clicks from what is underneath.
    ///
    /// <para>The toast needs this: it is a full-width strip pinned to the bottom
    /// of the window, and it was swallowing clicks on any control near the
    /// bottom edge (found when a "Generate request" button stopped responding
    /// while an import toast was up).</para>
    /// </summary>
    public sealed class PassThroughBox : BoxElement
    {
        public override bool HitTest(Point point) => false;
    }

    /// <summary>Key-hint chip: mono 10, az-border box (the sheet's kbd idiom).</summary>
    public static BoxElement Kbd(string text) =>
        Box.New()
            .Padding(new Thickness(2, 5, 2, 5))
            .CornerRadius(Azimuth.Radius1)
            .BorderColor(Azimuth.Border).BorderWidth(1f)
            .Children(Mono(text, 10f, Azimuth.TextMuted));

    // ── Buttons ──────────────────────────────────────────────────────────
    // The framework's ButtonElement in the families Azimuth.ToHultTheme() gives its
    // ButtonTheme: the accent outline primary, the az-border secondary, the bare ghost,
    // the status-fail danger. Chrome, hover, focus, keyboard activation, the disabled
    // state and the accessibility role are the element's; what this adds is Meridian's
    // content — the label, an optional keycap — and the automation id every control here
    // carries. Tabs, rows, chips and cards are not buttons and keep their own boxes.

    /// <param name="onClick">Null when the caller wires the click itself — a handler that
    /// needs the event, or one that opens a dialog with a closure over the row.</param>
    public static ButtonElement Button(ButtonVariant variant, string label, Action? onClick,
        string? automationId = null, string? kbd = null, ButtonStyle? style = null)
    {
        var b = HultUI.Core.Builders.Button.New(variant).Gap(6).Style(style);
        // Named for the label alone: the keycap is a hint for sighted users, and a screen
        // reader announcing "Send Ctrl ↵" would be reading the furniture.
        b.AccessibleName = label;
        b.AddChild(Text.New(label));
        if (kbd != null) b.AddChild(Kbd(kbd));
        if (automationId != null) b.AutomationId(automationId);
        if (onClick != null) b.OnClick(_ => onClick());
        return b;
    }

    /// <summary>The warn-coloured outline: Cancel and Abort, which interrupt rather than destroy.</summary>
    public static readonly ButtonStyle Warn = new()
    {
        Border = Azimuth.StatusWarn, Text = Azimuth.StatusWarn,
        HoverBackground = Azimuth.Tint(Azimuth.StatusWarn), PressedBackground = Azimuth.Alpha(Azimuth.StatusWarn, 0.24),
    };

    /// <summary>A bar's small action — reload, keep mine, a dialog's cancel.</summary>
    public static readonly ButtonStyle Small = new()
    {
        Padding = new Thickness(4, Azimuth.S2, 4, Azimuth.S2), FontSize = 11.5f, FontWeight = FontWeight.Regular,
    };

    /// <summary>A dialog's confirming action: the primary outline at the dialog's size.</summary>
    public static readonly ButtonStyle Dialog = new()
    {
        Padding = new Thickness(4, Azimuth.S3, 4, Azimuth.S3), FontSize = 12f,
    };

    /// <summary>The warn-filled fix-it cell: "missing — add to dev", "not set — set value".</summary>
    public static readonly ButtonStyle WarnFill = new()
    {
        Background = Azimuth.Alpha(Azimuth.StatusWarn, 0.10), Text = Azimuth.StatusWarn,
        HoverBackground = Azimuth.Alpha(Azimuth.StatusWarn, 0.18), PressedBackground = Azimuth.Alpha(Azimuth.StatusWarn, 0.26),
        Padding = new Thickness(4, Azimuth.S2, 4, Azimuth.S2), FontSize = 10.5f, FontWeight = FontWeight.Regular,
    };

    /// <summary>A 22px ✕ that removes its row.</summary>
    public static readonly ButtonStyle Remove = new()
    {
        Size = 22f, Padding = new Thickness(0), Text = Azimuth.TextFaint, FontSize = 10f, FontWeight = FontWeight.Regular,
    };

    /// <summary>A ghost action that adds a row — "＋ header" — in the editor's muted text.</summary>
    public static readonly ButtonStyle AddRow = new()
    {
        Padding = new Thickness(5, Azimuth.S2, 5, Azimuth.S2), FontSize = 11.5f, FontWeight = FontWeight.Regular,
    };

    /// <summary>The smallest ghost action in a bar: flip pane.</summary>
    public static readonly ButtonStyle Tiny = new()
    {
        Padding = new Thickness(3, 6, 3, 6), FontSize = 11f, FontWeight = FontWeight.Regular,
    };

    // ── Fields ───────────────────────────────────────────────────────────
    // The theme's field is flush; these are the bordered ones the views draw — what a
    // wrapper box used to supply. The element centres its line in whatever height the
    // caller gives it. Azimuth's colours are fixed, so a static is safe.

    /// <summary>A field on the raised Surface: a grid cell, a find box.</summary>
    public static readonly TextInputStyle Field = new()
    {
        Background = Azimuth.Surface, Border = Azimuth.Border, FocusBorder = Azimuth.Border,
        BorderWidth = new Thickness(1f), CornerRadius = new CornerRadius(Azimuth.Radius1),
        Padding = new Thickness(0, Azimuth.S2, 0, Azimuth.S2),
    };

    /// <summary>A field on the app ground: a dialog's input, the response finder.</summary>
    public static readonly TextInputStyle GroundField = new()
    {
        Background = Azimuth.Bg, Border = Azimuth.Border, FocusBorder = Azimuth.Border,
        BorderWidth = new Thickness(1f), CornerRadius = new CornerRadius(Azimuth.Radius1),
        Padding = new Thickness(0, Azimuth.S2, 0, Azimuth.S2),
    };
}
