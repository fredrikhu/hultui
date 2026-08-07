using HultUI.Core.Events;
using HultUI.Core.Primitives;
using HultUI.Core.Rendering;
using HultUI.Core.Styling;

namespace HultUI.Core.Elements;

public enum ButtonVariant
{
    Primary,
    Secondary,
    Danger,
}

public class ButtonElement : BoxElement
{
    private ButtonVariant _variant = ButtonVariant.Primary;
    private bool _hovered;
    private bool _pressed;
    private bool _focused;
    private Color _baseColor;
    private Color _hoverColor;
    private Color _pressedColor;
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

    public static ButtonElement New(string label, ButtonVariant variant = ButtonVariant.Primary)
    {
        var btn = new ButtonElement { _variant = variant };
        btn.AddChild(TextElement.New(label));
        return btn;
    }

    protected internal override void ApplyTheme(Theme theme)
    {
        var bt = theme.Button;
        CornerRadius = bt.CornerRadius;
        Box.Padding = bt.Padding;
        Inherited.FontSize = bt.FontSize;
        Inherited.FontWeight = bt.FontWeight;
        _focusRingColor = theme.FocusRing;

        // Resolve variant colors from theme semantic colors
        (_baseColor, _textColor) = _variant switch
        {
            ButtonVariant.Primary => (theme.Primary, theme.PrimaryText),
            ButtonVariant.Secondary => (theme.Secondary, theme.SecondaryText),
            ButtonVariant.Danger => (theme.Danger, theme.DangerText),
            _ => (theme.Primary, theme.PrimaryText),
        };

        _hoverColor = DarkenColor(_baseColor, bt.HoverDarken);
        _pressedColor = DarkenColor(_baseColor, bt.PressedDarken);

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
        MarkPaintDirty();
    }

    private static Color DarkenColor(Color c, float factor) => new(
        (byte)(c.R * factor),
        (byte)(c.G * factor),
        (byte)(c.B * factor),
        c.A);

    internal override void PatchFrom(Element source)
    {
        base.PatchFrom(source);
        if (source is ButtonElement btn)
        {
            Variant = btn.Variant;
        }
    }
}
