using HultUI.Core.Accessibility;
using HultUI.Icons;
using HultUI.Core.Animation;
using HultUI.Core.Builders;
using HultUI.Core.Elements;
using HultUI.Core.Events;
using HultUI.Core.Layout;
using HultUI.Core.Primitives;
using HultUI.Core.Styling;

namespace HultUI.Samples.Fluent;

// Inside the namespace on purpose. This assembly sits under HultUI, so an unqualified `Text`
// here binds to the HultUI.Text namespace (the shaping engine) rather than to the label
// builder — and an alias declared above the namespace loses to that. Declared in the
// namespace body, it wins.
using Text = HultUI.Core.Builders.Text;

/// <summary>
/// The Fluent control vocabulary, composed out of boxes, rows and labels.
///
/// <para>None of this is a framework feature and none of it needed to be. A design language is
/// a set of decisions about spacing, colour and shape, and the whole of WinUI's is expressible
/// in the primitives HultUI already has — the interesting question was never whether a
/// rounded rectangle is possible, it is whether the <i>details</i> survive: the two-tone
/// control border, the selection pill, the black text on a pale accent in dark mode. Those are
/// what this file is mostly about.</para>
/// </summary>
public static class F
{
    // ──────────────────────────────────────────────────────────────────────
    // Type ramp
    // ──────────────────────────────────────────────────────────────────────

    // Size, weight, optical size and line height together — the Gallery publishes the ramp as
    // four columns and taking three of them is how a ramp ends up nearly right. Caption is on
    // the Small cut rather than Text, and every style carries the published line height.

    public static TextElement Title(FluentPalette p, string s) =>
        Text.New(s).FontFamily(Fluent.DisplayFont).FontSize(Fluent.TitleSize)
            .LineHeight(Fluent.TitleLineHeight)
            .FontWeight(FontWeight.SemiBold).TextColor(p.TextPrimary);

    public static TextElement Subtitle(FluentPalette p, string s) =>
        Text.New(s).FontFamily(Fluent.DisplayFont).FontSize(Fluent.SubtitleSize)
            .LineHeight(Fluent.SubtitleLineHeight)
            .FontWeight(FontWeight.SemiBold).TextColor(p.TextPrimary);

    public static TextElement Body(FluentPalette p, string s) =>
        Text.New(s).FontSize(Fluent.BodySize).LineHeight(Fluent.BodyLineHeight)
            .TextColor(p.TextPrimary);

    public static TextElement BodyStrong(FluentPalette p, string s) =>
        Text.New(s).FontSize(Fluent.BodySize).LineHeight(Fluent.BodyLineHeight)
            .FontWeight(FontWeight.SemiBold).TextColor(p.TextPrimary);

    public static TextElement Secondary(FluentPalette p, string s) =>
        Text.New(s).FontFamily(Fluent.SmallFont).FontSize(Fluent.CaptionSize)
            .LineHeight(Fluent.CaptionLineHeight).TextColor(p.TextSecondary);

    public static TextElement Caption(FluentPalette p, string s) =>
        Text.New(s).FontFamily(Fluent.SmallFont).FontSize(Fluent.CaptionSize)
            .LineHeight(Fluent.CaptionLineHeight).TextColor(p.TextTertiary);

    /// <summary>The small bold label above a run of cards — "Related settings" and friends.
    /// BodyStrong, not a heading size: WinUI groups content with weight rather than scale.</summary>
    public static Element SectionHeader(FluentPalette p, string s) =>
        BodyStrong(p, s).Margin(new Thickness(8, 0, 0, 0)).FlexShrink(0);

    // ──────────────────────────────────────────────────────────────────────
    // Keyboard
    // ──────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Makes a control composed out of boxes behave like a control: reachable with Tab, showing
    /// the theme's focus visual once it is, and activating on Space or Enter.
    ///
    /// <para>A box is not focusable and does not answer to the keyboard, so every one of these
    /// would otherwise be mouse-only — invisible to Tab and to a screen reader driving it. The
    /// focus visual is opt-in per element precisely so a composed control can ask for it; this
    /// is the one line that asks.</para>
    /// </summary>
    public static T Focusable<T>(T el, Action activate) where T : Element
    {
        el.IsFocusable = true;
        el.ShowsFocusVisual = true;
        el.OnKeyDown(e =>
        {
            if (e.Key is not (KeyCode.Space or KeyCode.Enter)) return;
            activate();
            e.StopPropagation = true;
        });
        return el;
    }

    // ──────────────────────────────────────────────────────────────────────
    // Surfaces
    // ──────────────────────────────────────────────────────────────────────

    public static BoxElement Card(FluentPalette p) =>
        Box.New().FlexShrink(0)
            .Background(p.Card)
            .CornerRadius(Fluent.ControlRadius)
            .Border(new Thickness(1), p.CardStroke);

    /// <summary>
    /// A row of the Windows 11 Settings kind: glyph, a title with an explanation under it, and
    /// whatever operates the setting pushed to the right edge. 68px tall unless the text wraps.
    /// </summary>
    public static Element SettingsCard(
        FluentPalette p, Icon icon, string title, string? description, Element? control)
    {
        var text = Flex.Column(gap: 2).FlexGrow(1).Children(
            description is null
                ? [Body(p, title)]
                : new[] { Body(p, title), Secondary(p, description) });

        var children = new List<Element>
        {
            IconRenderer.Draw(icon, 20f, p.TextPrimary),
            text,
        };
        if (control is not null) children.Add(control);

        return Card(p)
            .MinHeight(68f)
            .Padding(new Thickness(12, 16, 12, 16))
            .FlexDirection(FlexDirection.Row)
            .Gap(16f)
            .AlignItems(AlignItems.Center)
            .Children([.. children]);
    }

    /// <summary>
    /// A settings card that opens. The header keeps the card's shape; the panel underneath
    /// shares its border so the two read as one object rather than as two stacked cards —
    /// which is the whole trick, and the reason the header loses its bottom corners when open.
    /// </summary>
    public static Element Expander(
        FluentPalette p, Icon icon, string title, string? description, bool expanded,
        Action<bool> onChanged, params Element[] content) =>
        // The framework's expander, in the card look FluentTheme gives it; the header is
        // WinUI's: an icon, the title, and a second line under it.
        HultUI.Core.Builders.Expander.New(title, Flex.Column(gap: 12).Children(content))
            .Header(Flex.Row(gap: 16).AlignItems(AlignItems.Center).Children(
                IconRenderer.Draw(icon, 20f, p.TextPrimary),
                Flex.Column(gap: 2).FlexGrow(1).Children(
                    description is null ? [Body(p, title)] : new[] { Body(p, title), Secondary(p, description) })))
            .Expanded(expanded)
            .OnExpandedChanged(onChanged);

    // ──────────────────────────────────────────────────────────────────────
    // Buttons
    // ──────────────────────────────────────────────────────────────────────

    /// <summary>
    /// A Fluent button, which is two nested boxes rather than one.
    ///
    /// <para>WinUI paints a control's 1px border as a vertical gradient: the sides and top get
    /// <c>ControlStrokeColorDefault</c> and the bottom edge gets the darker
    /// <c>ControlStrokeColorSecondary</c>. That single darker line is what makes a Fluent
    /// button look like it is sitting on the page. A box takes one border colour, so the
    /// outer box draws the bottom edge and the inner one draws the other three — 1px in total
    /// either way, and the two-tone survives.</para>
    /// </summary>
    /// <summary>
    /// The outer box's shape: no padding of its own — the inner box carries it, along with
    /// the top edge of the two-tone stroke — and only the bottom edge of the border, whose
    /// colour the palette's <c>ButtonTheme</c> supplies per family. The fills, hover and
    /// pressed colours come from the theme too, so the framework's own dialog buttons and
    /// this one read the same tokens.
    /// </summary>
    private static readonly ButtonStyle ButtonShape = new()
    {
        Padding = new Thickness(0),
        BorderWidth = new Thickness(0, 0, 1, 0),
    };

    public static Element Button(
        FluentPalette p, string label, Action onClick, bool accent = false, Icon? icon = null)
    {
        var text = accent ? p.TextOnAccent : p.TextPrimary;
        var strokeTop = accent ? p.AccentStrokeTop : p.ControlStroke;

        // The inner box is stretched to the outer's height (the button aligns its content
        // Stretch rather than its default Center) so its top edge draws at the top of the
        // control; it must not grow sideways, or a button in a wrapping row fills the row.
        var inner = Flex.Row(gap: 8f)
            .AlignItems(AlignItems.Center).JustifyContent(JustifyContent.Center)
            .Padding(new Thickness(0, 11, 0, 11))
            .CornerRadius(Fluent.ControlRadius)
            .Border(new Thickness(1, 1, 0, 1), strokeTop)
            .Children(icon is null
                ? [Body(p, label).TextColor(text)]
                : new Element[] { IconRenderer.Draw(icon.Value, 16f, text), Body(p, label).TextColor(text) });

        // The framework's button: focusable, Space and Enter, the focus visual, the role.
        // Its hover and pressed states are interaction styles, so the 90ms fade applies.
        return HultUI.Core.Builders.Button.New(accent ? ButtonVariant.Primary : ButtonVariant.Secondary)
            .Style(ButtonShape)
            .AlignItems(AlignItems.Stretch)
            .Height(Fluent.ControlHeight)
            .Transition(TransitionProperty.Background, 90f)
            .OnClick(_ => onClick())
            .AccessibleName(label)
            .Children(inner);
    }

    /// <summary>The subtle button's shape at one size, with an optional hover fill of its
    /// own (the InfoBar's close). One instance per pair: a style is compared by reference.</summary>
    private static readonly Dictionary<(float Size, Color? Hover), ButtonStyle> SubtleShapes = new();

    private static ButtonStyle SubtleShape(float size, Color? hoverFill)
    {
        var key = (size, hoverFill);
        if (!SubtleShapes.TryGetValue(key, out var shape))
            SubtleShapes[key] = shape = new ButtonStyle
            {
                Size = size, Padding = new Thickness(0), HoverBackground = hoverFill,
            };
        return shape;
    }

    /// <summary>A borderless square button — the title bar's and the InfoBar's. Fluent calls
    /// this the subtle style: nothing at rest, a faint fill under the pointer.</summary>
    public static Element SubtleIconButton(
        FluentPalette p, Icon icon, string name, Action onClick,
        float size = 32f, float glyph = 16f, Color? tint = null, Color? hoverFill = null) =>
        HultUI.Core.Builders.Button.New(ButtonVariant.Ghost)
            .Style(SubtleShape(size, hoverFill))
            .OnClick(_ => onClick())
            .AccessibleName(name)
            .Children(IconRenderer.Draw(icon, glyph, tint ?? p.TextPrimary));

    /// <summary>
    /// The toggle switch, which is the single most WinUI-looking control there is.
    ///
    /// <para>40x20 track, 12px knob, 4px of clearance all round, and 20px of travel. The knob
    /// is moved by a transform rather than by re-laying the row out, so the transition has
    /// something continuous to animate — a change of <c>JustifyContent</c> would just snap.</para>
    /// </summary>
    public static Element ToggleSwitch(FluentPalette p, bool on, Action<bool> onChanged, string name)
    {
        var knob = Box.New().FlexShrink(0)
            .Width(12f).Height(12f).CornerRadius(6f)
            // Measured off the WinUI Gallery: the off knob is TextFillColorSecondary (78.6% white),
            // not the dimmer ControlStrongFillColorDefault the track border uses. The knob reads
            // as text; the ring around it reads as a border.
            .Background(on ? p.TextOnAccent : p.TextSecondary)
            .TranslateBy(on ? 20f : 0f, 0f)
            .Transition(TransitionProperty.Transform, 150f);

        return Focusable(Box.New().FlexShrink(0)
            .Width(40f).Height(20f).CornerRadius(10f)
            .Background(on ? p.Accent : p.ControlAltFill)
            .Border(new Thickness(1), on ? p.Accent : p.ControlStrong)
            .Padding(new Thickness(0, 3, 0, 3))
            .FlexDirection(FlexDirection.Row)
            .AlignItems(AlignItems.Center)
            .Cursor(CursorType.Pointer)
            .OnHover(b => b.Background(on ? p.AccentHover : p.SubtleHover))
            .Transition(TransitionProperty.Background, 120f)
            .OnClick(_ => onChanged(!on))
            .AccessibleRole(AccessibilityRole.Switch)
            .AccessibleName(name)
            .Children(knob), () => onChanged(!on));
    }

    // ──────────────────────────────────────────────────────────────────────
    // Indicators
    // ──────────────────────────────────────────────────────────────────────

    public static Element ProgressBar(FluentPalette p, float fraction)
    {
        var fill = Box.New()
            .Width(Dimension.Percent(Math.Clamp(fraction, 0f, 1f) * 100f))
            .Height(4f).CornerRadius(2f)
            .Background(p.Accent);

        return Box.New().FlexShrink(0)
            .Height(4f).CornerRadius(2f)
            .Background(p.ControlStrokeBottom)
            .Overflow(Overflow.Hidden)
            .Children(fill);
    }

    public enum Severity { Informational, Success, Warning, Error }

    /// <summary>
    /// The InfoBar's severity mark: a 16px disc filled with the severity colour and the symbol
    /// knocked out of it in <see cref="FluentPalette.TextOnAccent"/> — black on a dark theme,
    /// white on a light one.
    ///
    /// <para>Measured off the Gallery rather than guessed. The four Segoe glyphs it uses
    /// (<c>F13C</c>–<c>F13F</c>) are the bare symbols — an exclamation mark, a cross, a tick, a
    /// letter i — with no ring or disc of their own, which is why the disc has to be drawn
    /// behind them. Reaching for the outlined <c>ⓘ</c>-style glyphs instead is the obvious
    /// mistake, and it was the one this sample had made.</para>
    ///
    /// <para>Without the icon font there is nothing to knock out — Phosphor has no bare "i" —
    /// so the fallback keeps the outlined icon it had. Better an honest circle-with-a-tick than
    /// a disc with a hole the wrong shape in it.</para>
    /// </summary>
    private static Element SeverityBadge(FluentPalette p, Color tint, Icon badge, Icon outline)
    {
        if (!IconRenderer.UseSegoe)
            return IconRenderer.Draw(outline, 20f, tint);

        return Box.New().FlexShrink(0)
            .Width(20f).Height(20f)
            .AlignItems(AlignItems.Center).JustifyContent(JustifyContent.Center)
            .Children(
                Box.New().FlexShrink(0)
                    .Width(16f).Height(16f).CornerRadius(8f)
                    .Background(tint)
                    .AlignItems(AlignItems.Center).JustifyContent(JustifyContent.Center)
                    // 16, not something smaller: the F13x glyphs sit well inside their em box,
                    // so a "10px" symbol comes out around five pixels tall and reads as a
                    // smudge in the middle of the disc. Matched to the Gallery by measurement.
                    .Children(IconRenderer.Draw(badge, 16f, p.TextOnAccent)));
    }

    /// <summary>An InfoBar: severity glyph, a bold title, the message beside it, and a
    /// dismiss button. The tinted background and the matching glyph colour are a pair —
    /// WinUI never uses one without the other.</summary>
    public static Element InfoBar(
        FluentPalette p, Severity severity, string title, string message, Action? onClose = null)
    {
        var (tint, back, badge, outline) = severity switch
        {
            Severity.Success => (p.Success, p.SuccessBg, Icons.BadgeSuccess, Icons.Success),
            Severity.Warning => (p.Caution, p.CautionBg, Icons.BadgeWarning, Icons.Warning),
            Severity.Error => (p.Critical, p.CriticalBg, Icons.BadgeError, Icons.Error),
            _ => (p.Accent, p.AttentionBg, Icons.BadgeInfo, Icons.Info),
        };

        var children = new List<Element>
        {
            SeverityBadge(p, tint, badge, outline),
            Flex.Row(gap: 8f).FlexGrow(1).FlexWrap(FlexWrap.Wrap).AlignItems(AlignItems.Center).Children(
                BodyStrong(p, title),
                Body(p, message).TextColor(p.TextPrimary)),
        };
        if (onClose is not null)
            children.Add(SubtleIconButton(p, Icons.Close, "Close", onClose, 28f, 12f, p.TextSecondary));

        return Box.New().FlexShrink(0)
            .MinHeight(48f)
            .Background(back)
            .CornerRadius(Fluent.ControlRadius)
            .Border(new Thickness(1), p.CardStroke)
            .Padding(new Thickness(10, 12, 10, 16))
            .FlexDirection(FlexDirection.Row)
            .Gap(13f)
            .AlignItems(AlignItems.Center)
            .AccessibleRole(AccessibilityRole.Alert)
            // Titles already carry their own punctuation ("Saved.", "Low disk space."), so the
            // format string must not add a second full stop — the screen reader reads it.
            .AccessibleName($"{title} {message}")
            .Children([.. children]);
    }

    // ──────────────────────────────────────────────────────────────────────
    // Navigation
    // ──────────────────────────────────────────────────────────────────────

    /// <summary>
    /// A NavigationView item. The 3x16 rounded bar on the left is the selection indicator, and
    /// it is load-bearing: WinUI marks the selected item with the pill and only a very faint
    /// fill, so a nav list that uses a strong highlight instead reads as some other toolkit
    /// however right the colours are.
    /// </summary>
    public static Element NavItem(
        FluentPalette p, Icon icon, string label, bool selected, Action onClick)
    {
        var pill = Box.New().FlexShrink(0)
            .Width(3f).Height(16f).CornerRadius(1.5f)
            .Background(selected ? p.Accent : Color.Transparent);

        var row = Flex.Row(gap: 16f).FlexGrow(1)
            .AlignItems(AlignItems.Center)
            .Padding(new Thickness(0, 12, 0, 13))
            .Children(
                IconRenderer.Draw(icon, 16f, p.TextPrimary),
                Body(p, label).MaxLines(1).TextOverflow(TextOverflow.Ellipsis));

        return Focusable(Box.New().FlexShrink(0)
            .Height(36f)
            .CornerRadius(Fluent.ControlRadius)
            .Background(selected ? p.SubtleHover : Color.Transparent)
            .FlexDirection(FlexDirection.Row)
            .AlignItems(AlignItems.Center)
            .Cursor(CursorType.Pointer)
            // Selected-and-hovered goes *quieter*, not louder — SubtleFillColorTertiary is closer
            // to the ground than Secondary. Reaching for a brighter fill is the intuitive move
            // and is what this had.
            .OnHover(b => b.Background(selected ? p.SubtlePressed : p.SubtleHover))
            .OnPressed(b => b.Background(p.SubtlePressed))
            .Transition(TransitionProperty.Background, 90f)
            .OnClick(_ => onClick())
            .AccessibleRole(AccessibilityRole.ListItem)
            .AccessibleName(label)
            .Children(pill, row), onClick);
    }
}
