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

    private string? _label;
    private float _labelGap = 8f;
    private float _checkboxSize = 18f;

    // What MeasureContent reads lays the checkbox out again when it changes: the size comes from
    // the theme (ApplyTheme), so a theme with a bigger box left the old one's room around it.
    public string? Label
    {
        get => _label;
        set
        {
            if (_label == value) return;
            _label = value;
            MarkLayoutDirty();
            MarkPaintDirty();
            // It names the control for a screen reader: told as a name change, not left to the
            // next rebuild that happens to come.
            NotifyAccessibilityPropertyChanged(AccessibilityProperties.Name);
        }
    }

    /// <summary>
    /// Whether <see cref="Label"/> marks its mnemonic with <c>&amp;</c>: <c>"&amp;Wrap lines"</c> is
    /// shown as "Wrap lines", with the W underlined while Alt is held, and <see cref="Mnemonic"/> is
    /// 'w'. <c>&amp;&amp;</c> is an ampersand. False, the default: the label is shown as written.
    /// Choosing it with Alt and the letter is the app's, as for a button.
    /// </summary>
    public bool UseMnemonic
    {
        get => _useMnemonic;
        set
        {
            if (_useMnemonic == value) return;
            _useMnemonic = value;
            MarkLayoutDirty();
            MarkPaintDirty();
            NotifyAccessibilityPropertyChanged(AccessibilityProperties.Name);
        }
    }
    private bool _useMnemonic;

    /// <summary>The label as shown and read out: <see cref="Label"/> without its marker where
    /// <see cref="UseMnemonic"/> is set, <see cref="Label"/> otherwise; "" for none.</summary>
    public string DisplayLabel => _useMnemonic ? Parsed.Label : _label ?? "";

    /// <summary>The mnemonic, lower case, or '\0' for none.</summary>
    public char Mnemonic
    {
        get
        {
            if (!_useMnemonic) return '\0';
            var (label, index) = Parsed;
            return index < 0 || char.IsSurrogate(label[index]) ? '\0' : char.ToLowerInvariant(label[index]);
        }
    }

    // The label parsed, kept until it changes.
    private (string? Source, string Label, int Index) _parsed;

    private (string Label, int Index) Parsed
    {
        get
        {
            var source = _label ?? "";
            if (!ReferenceEquals(_parsed.Source, source))
            {
                var (label, index) = Mnemonics.Parse(source);
                _parsed = (source, label, index);
            }
            return (_parsed.Label, _parsed.Index);
        }
    }

    public float LabelGap
    {
        get => _labelGap;
        set { if (_labelGap == value) return; _labelGap = value; MarkLayoutDirty(); MarkPaintDirty(); }
    }

    public float CheckboxSize
    {
        get => _checkboxSize;
        set { if (_checkboxSize == value) return; _checkboxSize = value; MarkLayoutDirty(); MarkPaintDirty(); }
    }
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
        if (ct.BorderWidth is { } bw) BorderWidth = bw;
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
                evt.Handled = true;
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
        var textSize = ctx.MeasureText(DisplayLabel, ResolvedInherited, textMaxWidth, null);
        return new Size(
            CheckboxSize + LabelGap + textSize.Width,
            Math.Max(CheckboxSize, textSize.Height));
    }

    // The label, drawn from the top of the content box beside the square. No label, no text.
    internal override float? MeasureBaseline(Size borderBox, IMeasureContext ctx) =>
        string.IsNullOrEmpty(Label)
            ? null
            : ContentTop + ctx.MeasureFirstBaseline(DisplayLabel, ResolvedInherited, float.PositiveInfinity, null, TextOverflow.Ellipsis);

    /// <summary>Unbounded: the focus ring stands 3 past the square, and a label squeezed into a
    /// short box runs out of it.</summary>
    public override Rect? PaintBounds(IRenderContext ctx) => null;

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
            // The mnemonic underlined while Alt is held (FocusManager.MnemonicCues).
            var spans = _useMnemonic && Parsed.Index >= 0 && Mnemonics.ShowsCue(FocusManager)
                ? Mnemonics.Underline(Parsed.Index) : null;
            ctx.DrawText(DisplayLabel, textBounds, ResolvedInherited, TextOverflow.Ellipsis, null, spans);
        }
    }

    // Its box's size is the theme's (compared on a themed twin, so a rebuild hands back the size
    // the style pass wrote); of what it copies below, the label, its gap and that size are measured,
    // and their setters mark layout when one changes. The rest is paint.
    private protected override bool PatchMarksOwnLayout => GetType() == typeof(CheckboxElement);
    private protected override bool PatchThemesTwin => GetType() == typeof(CheckboxElement);

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
            UseMnemonic = cb.UseMnemonic;
            LabelGap = cb.LabelGap;
            OnChanged = cb.OnChanged;
            IsChecked = cb.IsChecked;
        }
    }
}
