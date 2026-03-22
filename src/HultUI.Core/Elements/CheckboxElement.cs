using HultUI.Core.Events;
using HultUI.Core.Layout;
using HultUI.Core.Primitives;
using HultUI.Core.Rendering;
using HultUI.Core.Styling;

namespace HultUI.Core.Elements;

public class CheckboxElement : Element
{
    private bool _isChecked;

    public bool IsChecked
    {
        get => _isChecked;
        set
        {
            if (_isChecked == value) return;
            _isChecked = value;
            MarkPaintDirty();
        }
    }

    public float CheckboxSize { get; set; } = 18f;
    public float BorderWidth { get; set; } = 2f;
    public Color BoxColor { get; set; } = new(200, 200, 200, 255);
    public Color CheckedColor { get; set; } = new(66, 135, 245, 255);
    public Color CheckmarkColor { get; set; } = new(255, 255, 255, 255);
    public float CornerRadius { get; set; } = 3f;

    public Action<bool>? OnChanged { get; set; }

    public CheckboxElement()
    {
        Inherited.Cursor = CursorType.Pointer;
        On<ClickEvent>(OnClick);
    }

    public static CheckboxElement New(bool isChecked = false) => new() { _isChecked = isChecked };

    private void OnClick(ClickEvent evt)
    {
        IsChecked = !IsChecked;
        OnChanged?.Invoke(IsChecked);
    }

    internal override Size MeasureContent(float availableWidth, float availableHeight, IMeasureContext ctx)
    {
        return new Size(CheckboxSize, CheckboxSize);
    }

    public override void Render(IRenderContext ctx)
    {
        var box = Layout.AbsoluteContentBox;
        // Center the checkbox in the content box
        float x = box.X + (box.Width - CheckboxSize) / 2;
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
            OnChanged = cb.OnChanged;
            // Preserve IsChecked state
        }
    }
}
