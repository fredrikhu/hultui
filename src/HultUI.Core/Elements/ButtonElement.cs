using HultUI.Core.Events;
using HultUI.Core.Layout;
using HultUI.Core.Primitives;
using HultUI.Core.Styling;

namespace HultUI.Core.Elements;

public enum ButtonVariant
{
    /// <summary>Filled with the primary colour: the one action a view is for.</summary>
    Primary,
    /// <summary>Outlined on no fill: every other button in a row of them.</summary>
    Secondary,
    /// <summary>Filled with the danger colour: deletes and discards.</summary>
    Danger,
    /// <summary>Neither fill nor border until hovered: toolbar and inline actions.</summary>
    Ghost,
}

/// <summary>
/// A push button. Its look comes from the theme and is re-imposed on every style pass, so
/// style the theme or give the element a <see cref="Style"/>, not its box: each knob
/// resolves from the element's own <see cref="Style"/>, then the theme's style for its
/// <see cref="Variant"/>, then the shared values on <see cref="ButtonTheme"/>, then the
/// palette. The variant is the button's meaning and supplies its colours; the style is
/// its shape, and a reusable one is declared once and applied per element.
///
/// <para>Any children: a label, a glyph, or both. <see cref="Label"/> is the shorthand for
/// the text-only case. Focus shows through the window's focus visual, which the theme
/// keeps to keyboard focus by default, so a click does not leave a ring behind.</para>
///
/// <para>Hover and pressed are interaction styles, the same mechanism as <c>.OnHover</c>,
/// so an app's <c>.Transition(TransitionProperty.Background, …)</c> fades them and its own
/// <c>.OnHover</c> layers over them rather than fighting them.</para>
///
/// <para>A disabled button is drawn at <see cref="ButtonTheme.DisabledOpacity"/>, leaves
/// the Tab ring, and swallows its click before any handler sees it — but stays under the
/// pointer, so a tooltip can still say why.</para>
/// </summary>
public class ButtonElement : BoxElement
{
    private ButtonVariant _variant = ButtonVariant.Primary;
    private ButtonStyle? _style;
    private bool _enabled = true;
    private bool _circular;
    private float _disabledOpacity = 0.45f;
    private Color _baseColor;
    private Color _hoverColor;
    private Color _pressedColor;
    private Color _borderBase;
    private Color _borderHover;
    private Color _textColor;

    public ButtonVariant Variant
    {
        get => _variant;
        set
        {
            if (_variant == value) return;
            _variant = value;
            MarkStyleDirty();
        }
    }

    /// <summary>
    /// This button's own look, layered over its variant's. Null: the variant as the theme
    /// styles it. Compared by reference, so hand the same instance each build — a static
    /// on the app's UI helpers — rather than a fresh one.
    /// </summary>
    public ButtonStyle? Style
    {
        get => _style;
        set
        {
            if (ReferenceEquals(_style, value)) return;
            _style = value;
            MarkStyleDirty();
        }
    }

    /// <summary>
    /// Off: dimmed, out of the Tab ring, and its click goes nowhere. It stays hit-testable so
    /// a tooltip can explain why. Turning a focused button off does not move focus; the
    /// window's next traversal skips it.
    /// </summary>
    public bool IsEnabled
    {
        get => _enabled;
        set
        {
            if (_enabled == value) return;
            _enabled = value;
            IsFocusable = value;
            MarkStyleDirty();
        }
    }

    public string Label
    {
        get => Children.Count > 0 && Children[0] is TextElement te ? te.Content : "";
        set
        {
            if (Children.Count > 0 && Children[0] is TextElement te)
                te.Content = value;
            else
                ReplaceChildren(TextElement.New(value));
        }
    }

    /// <summary>The resolved rest fill, after the theme and palette have had their say.</summary>
    public Color ResolvedBackground => _baseColor;
    /// <summary>The resolved hover fill.</summary>
    public Color ResolvedHoverBackground => _hoverColor;
    /// <summary>The resolved pressed fill.</summary>
    public Color ResolvedPressedBackground => _pressedColor;
    /// <summary>The resolved rest border colour; transparent for a variant with no visible border.</summary>
    public Color ResolvedBorder => _borderBase;
    /// <summary>The resolved label colour.</summary>
    public Color ResolvedText => _textColor;

    public ButtonElement()
    {
        IsFocusable = true;
        ShowsFocusVisual = true;
        Flex.FlexShrink = 0;
        // A row with its content centred: a glyph sits beside its label, and a label in a
        // button wider than itself sits in the middle, which is what a button is.
        Flex.Direction = FlexDirection.Row;
        Flex.AlignItems = AlignItems.Center;
        Flex.JustifyContent = JustifyContent.Center;
        Inherited.Cursor = CursorType.Pointer;

        // Static, and reading the element they are handed: reconciliation copies the
        // incoming build's interaction config onto the retained element, so a lambda
        // that closed over `this` would light the retained button in a discarded one's
        // colours.
        AddHoverStyle(static e => ((ButtonElement)e).ApplyLit(pressed: false));
        AddPressedStyle(static e => ((ButtonElement)e).ApplyLit(pressed: true));

        // Registered before any app handler, so a disabled button's click stops here: the
        // app's OnClick never runs, and nor does a row's behind it.
        OnOwn<ClickEvent>(evt =>
        {
            if (!_enabled) evt.StopPropagation = true;
        });
        OnOwn<KeyDownEvent>(evt =>
        {
            if (!IsFocused || !_enabled) return;
            if (evt.Key is KeyCode.Enter or KeyCode.Space)
            {
                // Synthesize a click event for keyboard activation
                EventDispatcher.DispatchDirect(new ClickEvent { Position = Point.Zero }, this);
            }
        });
    }

    public static ButtonElement New(string label, ButtonVariant variant = ButtonVariant.Primary, [OriginFile] string? file = null, [OriginLine] int line = 0)
    {
        var btn = new ButtonElement { _variant = variant };
        btn.AddChild(TextElement.New(label, file, line));
        return btn.WithOrigin(file, line);
    }

    /// <summary>An empty button of one variant, for content that is not a plain label —
    /// a glyph, or a glyph and a label — added as children.</summary>
    public static ButtonElement New(ButtonVariant variant, [OriginFile] string? file = null, [OriginLine] int line = 0) =>
        new ButtonElement { _variant = variant }.WithOrigin(file, line);

    protected internal override void ApplyTheme(Theme theme)
    {
        var bt = theme.Button;
        var v = bt.For(_variant);
        var s = _style;

        // Geometry: the element's style, the variant's, then the theme's shared values.
        Box.Padding = s?.Padding ?? v.Padding ?? bt.Padding;
        Box.BorderWidth = s?.BorderWidth ?? v.BorderWidth ?? bt.BorderWidth;
        Inherited.FontSize = s?.FontSize ?? v.FontSize ?? bt.FontSize;
        Inherited.FontWeight = s?.FontWeight ?? v.FontWeight ?? bt.FontWeight;
        Inherited.Cursor = _enabled ? CursorType.Pointer : CursorType.Default;
        _disabledOpacity = bt.DisabledOpacity;

        // A size the style does not name is left to the app: a primary button stretched to
        // the width of its composer keeps that width across style passes.
        float? size = s?.Size ?? v.Size;
        if (size is { } fixed_)
        {
            Box.Width = Dimension.Px(fixed_);
            Box.Height = Dimension.Px(fixed_);
        }
        if ((s?.MinWidth ?? v.MinWidth) is { } minW) Box.MinWidth = minW;
        if ((s?.MinHeight ?? v.MinHeight) is { } minH) Box.MinHeight = minH;

        // Round is a statement about the radius, so a nearer layer saying "round" beats a
        // farther one naming a radius, the same as it would beat a farther "round".
        _circular = s?.Circular ?? v.Circular ?? false;
        CornerRadius = RadiusOf(s) ?? RadiusOf(v) ?? bt.CornerRadius;

        CornerRadius? RadiusOf(ButtonStyle? st) => st is null ? null
            : st.CornerRadius ?? (st.Circular == true && size is { } d ? new CornerRadius(d / 2f) : null);

        // The palette's answer for each variant, then whatever the styles override.
        var (fill, border, text) = _variant switch
        {
            ButtonVariant.Secondary => (Color.Transparent, theme.Border, theme.TextColor),
            ButtonVariant.Danger => (theme.Danger, Color.Transparent, theme.DangerText),
            ButtonVariant.Ghost => (Color.Transparent, Color.Transparent, theme.Primary),
            _ => (theme.Primary, Color.Transparent, theme.PrimaryText),
        };
        _baseColor = s?.Background ?? v.Background ?? fill;
        _borderBase = s?.Border ?? v.Border ?? border;
        _textColor = s?.Text ?? v.Text ?? text;
        _borderHover = s?.HoverBorder ?? v.HoverBorder ?? _borderBase;

        // A filled button darkens under the pointer; an unfilled one takes a wash of its
        // own text colour, which is what reads as "the same button, lit" on any ground.
        bool filled = _baseColor.A > 0;
        _hoverColor = s?.HoverBackground ?? v.HoverBackground
            ?? (filled ? Darken(_baseColor, bt.HoverDarken) : Tint(_textColor, bt.HoverTint));
        _pressedColor = s?.PressedBackground ?? v.PressedBackground
            ?? (filled ? Darken(_baseColor, bt.PressedDarken) : Tint(_textColor, bt.PressedTint));

        Inherited.TextColor = _textColor;

        // The rest look is the interaction base: write it, re-snapshot, and re-apply
        // whatever state the pointer is in — a theme change mid-hover keeps the button lit,
        // in the new theme's colours.
        BackgroundColor = _baseColor;
        BorderColor = _borderBase;
        RefreshInteractionAfterPatch();
    }

    private void ApplyLit(bool pressed)
    {
        if (!_enabled) return;
        BackgroundColor = pressed ? _pressedColor : _hoverColor;
        BorderColor = _borderHover;
    }

    protected override CornerRadius RenderCornerRadius(in Rect box) =>
        _circular ? new CornerRadius(MathF.Min(box.Width, box.Height) / 2f) : CornerRadius;

    internal override float RenderOpacity => _enabled ? base.RenderOpacity : base.RenderOpacity * _disabledOpacity;

    internal override bool IsEnabledForAccessibility => _enabled;

    private static Color Darken(Color c, float factor) => new(
        (byte)(c.R * factor),
        (byte)(c.G * factor),
        (byte)(c.B * factor),
        c.A);

    private static Color Tint(Color c, float alpha) => new(c.R, c.G, c.B, (byte)Math.Clamp(alpha * 255f, 0f, 255f));

    internal override void PatchFrom(Element source)
    {
        base.PatchFrom(source);
        if (source is ButtonElement btn)
        {
            Variant = btn.Variant;
            Style = btn.Style;
            IsEnabled = btn.IsEnabled;
        }
    }
}
