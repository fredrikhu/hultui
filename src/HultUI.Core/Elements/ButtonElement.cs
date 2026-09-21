using HultUI.Core.Events;
using HultUI.Core.Primitives;
using HultUI.Core.Rendering;
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
/// A push button. Its look comes entirely from <see cref="ButtonTheme"/> and the palette —
/// fill, border and label per <see cref="Variant"/>, one padding, radius, font and border
/// width for all of them — and is re-imposed on every style pass, so style the theme, not
/// the instance.
/// </summary>
public class ButtonElement : BoxElement
{
    private ButtonVariant _variant = ButtonVariant.Primary;
    private bool _hovered;
    private bool _pressed;
    private bool _focused;
    private Color _baseColor;
    private Color _hoverColor;
    private Color _pressedColor;
    private Color _borderBase;
    private Color _borderHover;
    private Color _textColor;
    private Color _focusRingColor;

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
        Flex.FlexShrink = 0;
        Inherited.Cursor = CursorType.Pointer;

        OnOwn<PointerEnterEvent>(_ =>
        {
            _hovered = true;
            UpdateVisualState();
        });
        OnOwn<PointerLeaveEvent>(_ =>
        {
            _hovered = false;
            _pressed = false;
            UpdateVisualState();
        });
        OnOwn<PointerDownEvent>(_ =>
        {
            _pressed = true;
            UpdateVisualState();
        });
        OnOwn<PointerUpEvent>(_ =>
        {
            _pressed = false;
            UpdateVisualState();
        });
        OnOwn<FocusEvent>(_ =>
        {
            _focused = true;
            MarkPaintDirty();
        });
        OnOwn<BlurEvent>(_ =>
        {
            _focused = false;
            MarkPaintDirty();
        });
        OnOwn<KeyDownEvent>(evt =>
        {
            if (!_focused) return;
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

    protected internal override void ApplyTheme(Theme theme)
    {
        var bt = theme.Button;
        CornerRadius = bt.CornerRadius;
        Box.Padding = bt.Padding;
        Box.BorderWidth = bt.BorderWidth;
        Inherited.FontSize = bt.FontSize;
        Inherited.FontWeight = bt.FontWeight;
        _focusRingColor = theme.FocusRing;

        var v = bt.For(_variant);

        // The palette's answer for each variant, then whatever the variant theme overrides.
        var (fill, border, text) = _variant switch
        {
            ButtonVariant.Secondary => (Color.Transparent, theme.Border, theme.TextColor),
            ButtonVariant.Danger => (theme.Danger, Color.Transparent, theme.DangerText),
            ButtonVariant.Ghost => (Color.Transparent, Color.Transparent, theme.Primary),
            _ => (theme.Primary, Color.Transparent, theme.PrimaryText),
        };
        _baseColor = v.Background ?? fill;
        _borderBase = v.Border ?? border;
        _textColor = v.Text ?? text;
        _borderHover = v.HoverBorder ?? _borderBase;

        // A filled button darkens under the pointer; an unfilled one takes a wash of its
        // own text colour, which is what reads as "the same button, lit" on any ground.
        bool filled = _baseColor.A > 0;
        _hoverColor = v.HoverBackground ?? (filled ? Darken(_baseColor, bt.HoverDarken) : Tint(_textColor, bt.HoverTint));
        _pressedColor = v.PressedBackground ?? (filled ? Darken(_baseColor, bt.PressedDarken) : Tint(_textColor, bt.PressedTint));

        Inherited.TextColor = _textColor;
        UpdateVisualState();
    }

    public override void Render(IRenderContext ctx)
    {
        base.Render(ctx);

        if (_focused)
        {
            var box = Layout.AbsoluteBorderBox;
            float offset = 2f;
            var ringRect = new Rect(box.X - offset, box.Y - offset, box.Width + offset * 2, box.Height + offset * 2);
            var ringRadius = new CornerRadius(
                CornerRadius.TopLeft + offset, CornerRadius.TopRight + offset,
                CornerRadius.BottomRight + offset, CornerRadius.BottomLeft + offset);
            ctx.DrawRoundedRect(ringRect, ringRadius, Color.Transparent, _focusRingColor, new Thickness(2f));
        }
    }

    private void UpdateVisualState()
    {
        BackgroundColor = _pressed ? _pressedColor : _hovered ? _hoverColor : _baseColor;
        BorderColor = _hovered || _pressed ? _borderHover : _borderBase;
        MarkPaintDirty();
    }

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
        }
    }
}
