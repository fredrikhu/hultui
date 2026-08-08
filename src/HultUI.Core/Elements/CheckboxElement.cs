using HultUI.Core.Accessibility;
using HultUI.Core.Events;
using HultUI.Core.Layout;
using HultUI.Core.Primitives;
using HultUI.Core.Rendering;
using HultUI.Core.Styling;

namespace HultUI.Core.Elements;

public class CheckboxElement : Element
{
    private bool _isChecked;
    private bool _focused;
    private Color _focusRingColor;

    public bool IsChecked
    {
        get => _isChecked;
        set
        {
            if (_isChecked == value) return;
            _isChecked = value;
            MarkPaintDirty();
            NotifyAccessibilityPropertyChanged(AccessibilityProperties.IsToggled);
        }
    }

    public string? Label { get; set; }
    public float LabelGap { get; set; } = 8f;
    public float CheckboxSize { get; set; } = 18f;
    public float BorderWidth { get; set; } = 2f;
    public Color BoxColor { get; set; } = new(200, 200, 200, 255);
    public Color CheckedColor { get; set; } = new(66, 135, 245, 255);
    public Color CheckmarkColor { get; set; } = new(255, 255, 255, 255);
    public float CornerRadius { get; set; } = 3f;

    protected internal override void ApplyTheme(Theme theme)
    {
        var ct = theme.Checkbox;
        CheckboxSize = ct.Size;
        CornerRadius = ct.CornerRadius.TopLeft; // uniform
        BoxColor = ct.BorderColor ?? theme.Border;
        CheckedColor = ct.CheckedBackground ?? theme.Primary;
        CheckmarkColor = ct.CheckMarkColor ?? Color.White;
        _focusRingColor = theme.FocusRing;
    }

    public Action<bool>? OnChanged { get; set; }

    public CheckboxElement()
    {
        IsFocusable = true;
        Inherited.Cursor = CursorType.Pointer;
        OnOwn<ClickEvent>(OnClick);
        OnOwn<FocusEvent>(_ => { _focused = true; MarkPaintDirty(); });
        OnOwn<BlurEvent>(_ => { _focused = false; MarkPaintDirty(); });
        OnOwn<KeyDownEvent>(evt =>
        {
            if (!_focused) return;
            if (evt.Key == KeyCode.Space)
            {
                IsChecked = !IsChecked;
                OnChanged?.Invoke(IsChecked);
            }
        });
    }

    // One factory rather than two overloads: with a trailing optional string in
    // the signature, New(true, "Label") would bind "Label" to the captured file
    // path instead of the label, and overload resolution would pick that silently.
    public static CheckboxElement New(bool isChecked = false, string? label = null, [OriginFile] string? file = null, [OriginLine] int line = 0) =>
        new CheckboxElement { _isChecked = isChecked, Label = label }.WithOrigin(file, line);

    private void OnClick(ClickEvent evt)
    {
        IsChecked = !IsChecked;
        OnChanged?.Invoke(IsChecked);
    }

    internal override Size MeasureContent(float availableWidth, float availableHeight, IMeasureContext ctx)
    {
        if (Label is null)
            return new Size(CheckboxSize, CheckboxSize);

        float textMaxWidth = availableWidth - CheckboxSize - LabelGap;
        if (textMaxWidth < 0) textMaxWidth = 0;
        var textSize = ctx.MeasureText(Label, ResolvedInherited, textMaxWidth, null);
        return new Size(
            CheckboxSize + LabelGap + textSize.Width,
            Math.Max(CheckboxSize, textSize.Height));
    }

    public override void Render(IRenderContext ctx)
    {
        var box = Layout.AbsoluteContentBox;
        float x = Label is null ? box.X + (box.Width - CheckboxSize) / 2 : box.X;
        float y = box.Y + (box.Height - CheckboxSize) / 2;
        var checkRect = new Rect(x, y, CheckboxSize, CheckboxSize);
        var radius = new CornerRadius(CornerRadius);

        if (IsChecked)
        {
            // Filled box
            ctx.DrawRoundedRect(checkRect, radius, CheckedColor, Color.Transparent, default);

            // Draw checkmark as two lines
            float pad = CheckboxSize * 0.25f;
            float midX = x + CheckboxSize * 0.38f;
            float midY = y + CheckboxSize * 0.65f;
            ctx.DrawLine(new Point(x + pad, y + CheckboxSize * 0.5f), new Point(midX, midY), CheckmarkColor, 2f);
            ctx.DrawLine(new Point(midX, midY), new Point(x + CheckboxSize - pad, y + pad), CheckmarkColor, 2f);
        }
        else
        {
            // Empty box with border
            ctx.DrawRoundedRect(checkRect, radius, Color.Transparent, BoxColor, new Thickness(BorderWidth));
        }

        // Focus ring
        if (_focused)
        {
            float offset = 3f;
            var ringRect = new Rect(x - offset, y - offset, CheckboxSize + offset * 2, CheckboxSize + offset * 2);
            var ringRadius = new CornerRadius(CornerRadius + offset);
            ctx.DrawRoundedRect(ringRect, ringRadius, Color.Transparent, _focusRingColor, new Thickness(2f));
        }

        // Label
        if (Label is not null)
        {
            float textX = box.X + CheckboxSize + LabelGap;
            float textWidth = box.Width - CheckboxSize - LabelGap;
            var textBounds = new Rect(textX, box.Y, textWidth, box.Height);
            ctx.DrawText(Label, textBounds, ResolvedInherited, TextOverflow.Ellipsis, null);
        }
    }

    internal override void PatchFrom(Element source)
    {
        base.PatchFrom(source);
        if (source is CheckboxElement cb)
        {
            CheckboxSize = cb.CheckboxSize;
            BorderWidth = cb.BorderWidth;
            BoxColor = cb.BoxColor;
            CheckedColor = cb.CheckedColor;
            CheckmarkColor = cb.CheckmarkColor;
            CornerRadius = cb.CornerRadius;
            Label = cb.Label;
            LabelGap = cb.LabelGap;
            OnChanged = cb.OnChanged;
            IsChecked = cb.IsChecked;
        }
    }
}
