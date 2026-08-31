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

    public static TextElement Title(FluentPalette p, string s) =>
        Text.New(s).FontFamily(Fluent.DisplayFont).FontSize(Fluent.TitleSize)
            .FontWeight(FontWeight.SemiBold).TextColor(p.TextPrimary);

    public static TextElement Subtitle(FluentPalette p, string s) =>
        Text.New(s).FontFamily(Fluent.DisplayFont).FontSize(Fluent.SubtitleSize)
            .FontWeight(FontWeight.SemiBold).TextColor(p.TextPrimary);

    public static TextElement Body(FluentPalette p, string s) =>
        Text.New(s).FontSize(Fluent.BodySize).TextColor(p.TextPrimary);

    public static TextElement BodyStrong(FluentPalette p, string s) =>
        Text.New(s).FontSize(Fluent.BodySize).FontWeight(FontWeight.SemiBold).TextColor(p.TextPrimary);

    public static TextElement Secondary(FluentPalette p, string s) =>
        Text.New(s).FontSize(Fluent.CaptionSize).TextColor(p.TextSecondary);

    public static TextElement Caption(FluentPalette p, string s) =>
        Text.New(s).FontSize(Fluent.CaptionSize).TextColor(p.TextTertiary);

    /// <summary>The small bold label above a run of cards — "Related settings" and friends.
    /// BodyStrong, not a heading size: WinUI groups content with weight rather than scale.</summary>
    public static Element SectionHeader(FluentPalette p, string s) =>
        BodyStrong(p, s).Margin(new Thickness(8, 0, 0, 1)).FlexShrink(0);

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
        Action onToggle, params Element[] content)
    {
        var chevron = IconRenderer.Draw(Icons.ChevronDown, 12f, p.TextSecondary);
        chevron.Rotate(expanded ? 180f : 0f).Transition(TransitionProperty.Transform, 160f);

        var header = Focusable(Card(p)
            .MinHeight(68f)
            .CornerRadius(expanded
                ? new CornerRadius(Fluent.ControlRadius, Fluent.ControlRadius, 0, 0)
                : new CornerRadius(Fluent.ControlRadius))
            .Padding(new Thickness(12, 16, 12, 16))
            .FlexDirection(FlexDirection.Row)
            .Gap(16f)
            .AlignItems(AlignItems.Center)
            .Cursor(CursorType.Pointer)
            .OnHover(b => b.Background(p.CardHover))
            .OnClick(_ => onToggle())
            .AccessibleRole(AccessibilityRole.Button)
            .AccessibleName(title)
            .AccessibleExpanded(expanded)
            .Children(
                IconRenderer.Draw(icon, 20f, p.TextPrimary),
                Flex.Column(gap: 2).FlexGrow(1).Children(
                    description is null ? [Body(p, title)] : new[] { Body(p, title), Secondary(p, description) }),
                Box.New().Width(32f).Height(32f).CornerRadius(Fluent.ControlRadius)
                    .AlignItems(AlignItems.Center).JustifyContent(JustifyContent.Center)
                    .Children(chevron)), onToggle);

        if (!expanded) return header;

        var panel = Box.New().FlexShrink(0)
            .Background(p.IsDark ? p.Layer : Color.White)
            .Border(new Thickness(0, 1, 1, 1), p.CardStroke)
            .CornerRadius(new CornerRadius(0, 0, Fluent.ControlRadius, Fluent.ControlRadius))
            .Padding(new Thickness(16, 16, 16, 16))
            .Gap(12f)
            .Children(content);

        return Flex.Column(gap: 0).FlexShrink(0).Children(header, panel);
    }

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
    public static Element Button(
        FluentPalette p, string label, Action onClick, bool accent = false, Icon? icon = null)
    {
        var fill = accent ? p.Accent : p.ControlFill;
        var hover = accent ? p.AccentHover : p.ControlHover;
        var pressed = accent ? p.AccentPressed : p.ControlPressed;
        var text = accent ? p.TextOnAccent : p.TextPrimary;

        // An accent button's own border is a highlight rather than an outline: white along the
        // top, black along the bottom, both at a few percent over the accent itself.
        var strokeTop = accent ? new Color(255, 255, 255, (byte)(p.IsDark ? 20 : 36)) : p.ControlStroke;
        var strokeBottom = accent ? new Color(0, 0, 0, (byte)(p.IsDark ? 36 : 64)) : p.ControlStrokeBottom;

        var inner = Flex.Row(gap: 8f).FlexGrow(1)
            .AlignItems(AlignItems.Center).JustifyContent(JustifyContent.Center)
            .Padding(new Thickness(0, 11, 0, 11))
            .CornerRadius(Fluent.ControlRadius)
            .Border(new Thickness(1, 1, 0, 1), strokeTop)
            .Children(icon is null
                ? [Body(p, label).TextColor(text)]
                : new Element[] { IconRenderer.Draw(icon.Value, 16f, text), Body(p, label).TextColor(text) });

        return Focusable(Box.New().FlexShrink(0)
            .Height(Fluent.ControlHeight)
            .CornerRadius(Fluent.ControlRadius)
            .Background(fill)
            .Border(new Thickness(0, 0, 1, 0), strokeBottom)
            .Cursor(CursorType.Pointer)
            .OnHover(b => b.Background(hover))
            .OnPressed(b => b.Background(pressed))
            .Transition(TransitionProperty.Background, 90f)
            .OnClick(_ => onClick())
            .AccessibleRole(AccessibilityRole.Button)
            .AccessibleName(label)
            .Children(inner), onClick);
    }

    /// <summary>A borderless square button — the title bar's and the InfoBar's. Fluent calls
    /// this the subtle style: nothing at rest, a faint fill under the pointer.</summary>
    public static Element SubtleIconButton(
        FluentPalette p, Icon icon, string name, Action onClick,
        float size = 32f, float glyph = 16f, Color? tint = null, Color? hoverFill = null)
    {
        return Focusable(Box.New().FlexShrink(0)
            .Width(size).Height(size)
            .CornerRadius(Fluent.ControlRadius)
            .Background(Color.Transparent)
            .AlignItems(AlignItems.Center).JustifyContent(JustifyContent.Center)
            .Cursor(CursorType.Pointer)
            .OnHover(b => b.Background(hoverFill ?? p.SubtleHover))
            .OnPressed(b => b.Background(p.SubtlePressed))
            .OnClick(_ => onClick())
            .AccessibleRole(AccessibilityRole.Button)
            .AccessibleName(name)
            .Children(IconRenderer.Draw(icon, glyph, tint ?? p.TextPrimary)), onClick);
    }

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

    /// <summary>An InfoBar: severity glyph, a bold title, the message beside it, and a
    /// dismiss button. The tinted background and the matching glyph colour are a pair —
    /// WinUI never uses one without the other.</summary>
    public static Element InfoBar(
        FluentPalette p, Severity severity, string title, string message, Action? onClose = null)
    {
        var (tint, back, icon) = severity switch
        {
            Severity.Success => (p.Success, p.SuccessBg, Icons.Success),
            Severity.Warning => (p.Caution, p.CautionBg, Icons.Warning),
            Severity.Error => (p.Critical, p.CriticalBg, Icons.Error),
            _ => (p.Accent, p.AttentionBg, Icons.Info),
        };

        var children = new List<Element>
        {
            IconRenderer.Draw(icon, 20f, tint),
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
            .AccessibleName($"{title}. {message}")
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
            .OnHover(b => b.Background(selected ? p.CardHover : p.SubtleHover))
            .OnPressed(b => b.Background(p.SubtlePressed))
            .Transition(TransitionProperty.Background, 90f)
            .OnClick(_ => onClick())
            .AccessibleRole(AccessibilityRole.ListItem)
            .AccessibleName(label)
            .Children(pill, row), onClick);
    }
}
