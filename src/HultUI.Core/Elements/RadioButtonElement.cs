using HultUI.Core.Accessibility;
using HultUI.Core.Events;
using HultUI.Core.Layout;
using HultUI.Core.Primitives;
using HultUI.Core.Rendering;
using HultUI.Core.Styling;

namespace HultUI.Core.Elements;

public class RadioButtonElement : Element
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
            NotifyAccessibilityPropertyChanged(AccessibilityProperties.IsSelected);
        }
    }

    public string? Label { get; set; }
    public float LabelGap { get; set; } = 8f;
    public string GroupName { get; set; } = "";
    public float RadioSize { get; set; } = 18f;
    public float BorderWidth { get; set; } = 2f;
    public Color CircleColor { get; set; } = new(200, 200, 200, 255);
    public Color CheckedColor { get; set; } = new(66, 135, 245, 255);
    public Color DotColor { get; set; } = new(255, 255, 255, 255);

    public Action<bool>? OnChanged { get; set; }

    protected internal override void ApplyTheme(Theme theme)
    {
        var rt = theme.RadioButton;
        RadioSize = rt.Size;
        CircleColor = rt.BorderColor ?? theme.Border;
        CheckedColor = rt.SelectedColor ?? theme.Primary;
        DotColor = rt.DotColor ?? Color.White;
        _focusRingColor = theme.FocusRing;
    }

    public RadioButtonElement()
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
                if (!IsChecked)
                {
                    if (!string.IsNullOrEmpty(GroupName))
                    {
                        var root = FindGroupRoot();
                        DeselectGroupMembers(root);
                    }
                    IsChecked = true;
                    OnChanged?.Invoke(true);
                }
            }
        });
    }

    // Collapsed for the same reason as CheckboxElement.New: a trailing optional
    // string would swallow the label into the captured file path.
    public static RadioButtonElement New(string groupName = "", bool isChecked = false, string? label = null,
        [OriginFile] string? file = null, [OriginLine] int line = 0) =>
        new RadioButtonElement { GroupName = groupName, _isChecked = isChecked, Label = label }
            .WithOrigin(file, line);

    private void OnClick(ClickEvent evt)
    {
        if (IsChecked) return; // Already selected, no-op

        // Deselect all radio buttons in the same group within the subtree
        if (!string.IsNullOrEmpty(GroupName))
        {
            var root = FindGroupRoot();
            DeselectGroupMembers(root);
        }

        IsChecked = true;
        OnChanged?.Invoke(true);
    }

    /// <summary>
    /// Walks up the tree to find the root ancestor for group searching.
    /// </summary>
    private Element FindGroupRoot()
    {
        Element current = this;
        while (current.Parent is not null)
            current = current.Parent;
        return current;
    }

    private void DeselectGroupMembers(Element subtree)
    {
        if (subtree is RadioButtonElement radio &&
            radio != this &&
            radio.GroupName == GroupName &&
            radio.IsChecked)
        {
            radio.IsChecked = false;
            radio.OnChanged?.Invoke(false);
        }

        foreach (var child in subtree.Children)
            DeselectGroupMembers(child);
    }

    internal override Size MeasureContent(float availableWidth, float availableHeight, IMeasureContext ctx)
    {
        if (Label is null)
            return new Size(RadioSize, RadioSize);

        float textMaxWidth = availableWidth - RadioSize - LabelGap;
        if (textMaxWidth < 0) textMaxWidth = 0;
        var textSize = ctx.MeasureText(Label, ResolvedInherited, textMaxWidth, null);
        return new Size(
            RadioSize + LabelGap + textSize.Width,
            Math.Max(RadioSize, textSize.Height));
    }

    public override void Render(IRenderContext ctx)
    {
        var box = Layout.AbsoluteContentBox;
        float radius = RadioSize / 2;

        // Use a canvas context for circle drawing
        var canvasCtx = ctx.CreateCanvasContext(box);

        float localCx = Label is null ? box.Width / 2 : radius;
        float localCy = box.Height / 2;

        if (IsChecked)
        {
            // Filled outer circle
            canvasCtx.DrawCircle(new Point(localCx, localCy), radius, CheckedColor);
            // Inner dot
            canvasCtx.DrawCircle(new Point(localCx, localCy), radius * 0.4f, DotColor);
        }
        else
        {
            // Empty circle with border
            canvasCtx.DrawCircleStroke(new Point(localCx, localCy), radius - BorderWidth / 2, CircleColor, BorderWidth);
        }

        // Focus ring
        if (_focused)
        {
            float offset = 3f;
            canvasCtx.DrawCircleStroke(new Point(localCx, localCy), radius + offset, _focusRingColor, 2f);
        }

        // Label
        if (Label is not null)
        {
            float textX = box.X + RadioSize + LabelGap;
            float textWidth = box.Width - RadioSize - LabelGap;
            var textBounds = new Rect(textX, box.Y, textWidth, box.Height);
            ctx.DrawText(Label, textBounds, ResolvedInherited, TextOverflow.Ellipsis, null);
        }
    }

    internal override void PatchFrom(Element source)
    {
        base.PatchFrom(source);
        if (source is RadioButtonElement radio)
        {
            Label = radio.Label;
            LabelGap = radio.LabelGap;
            GroupName = radio.GroupName;
            RadioSize = radio.RadioSize;
            BorderWidth = radio.BorderWidth;
            CircleColor = radio.CircleColor;
            CheckedColor = radio.CheckedColor;
            DotColor = radio.DotColor;
            OnChanged = radio.OnChanged;
            IsChecked = radio.IsChecked;
        }
    }
}
