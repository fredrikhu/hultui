using System.Globalization;
using HultUI.Core.Accessibility;
using HultUI.Core.Animation;
using HultUI.Core.Events;
using HultUI.Core.Layout;
using HultUI.Core.Primitives;
using HultUI.Core.Rendering;
using HultUI.Core.Styling;

namespace HultUI.Core.Elements;

/// <summary>
/// A number field with step buttons: the value on the left, an up/down strip on the
/// right. Up/Down and the wheel step by <see cref="Step"/>, PageUp/PageDown by
/// <see cref="LargeStep"/>, Home/End go to the bounds, a held button repeats, and typing
/// replaces the value on Enter (or blur) after parsing. The range is enforced here, so
/// callers stop clamping at every site.
/// </summary>
/// <remarks>
/// One self-drawing element rather than a row of a text box and two buttons, for the
/// reasons the pickers are: one focus, one hit target, one accessible node with a
/// range value, and no internal children for a rebuild to reconcile.
/// </remarks>
public class NumericUpDownElement : Element, IRangeValueElement
{
    private double _value;
    private double _min;
    private double _max = 100;
    private double _step = 1;
    private bool _focused;
    private string? _typed;          // the text being edited; null when showing the value
    private int _held;               // +1 up, -1 down, 0 none
    private TweenHandle _repeat;
    private int _repeatDone;

    private DropdownTheme _boxTheme = new();
    private NumericUpDownTheme _theme = new();
    private Color _chevronColor = new(100, 116, 139);
    private Color _pressedBackground = new(241, 245, 249);
    private Color _focusRingColor = new(59, 130, 246, 180);
    private float _textWidth = 48f;

    public NumericUpDownElement()
    {
        IsFocusable = true;
        Role = AccessibilityRole.SpinButton;
        Inherited.Cursor = CursorType.Pointer;
        OnOwn<PointerDownEvent>(OnPointerDown);
        OnOwn<PointerUpEvent>(_ => Release());
        OnOwn<PointerLeaveEvent>(_ => Release());
        OnOwn<FocusEvent>(_ => { _focused = true; MarkPaintDirty(); });
        OnOwn<BlurEvent>(_ => { _focused = false; Commit(); Release(); MarkPaintDirty(); });
        OnOwn<KeyDownEvent>(OnKeyDown);
        OnOwn<TextInputEvent>(OnTextInput);
        OnOwn<ScrollEvent>(OnScroll);
    }

    public static NumericUpDownElement New(double value = 0, double min = 0, double max = 100, double step = 1,
        [OriginFile] string? file = null, [OriginLine] int line = 0)
    {
        var n = new NumericUpDownElement { _min = min, _max = max, _step = step };
        n._value = n.Clamp(value);
        return n.WithOrigin(file, line);
    }

    // ── Model ────────────────────────────────────────────────────────────

    /// <summary>The value, clamped to the range and snapped to <see cref="Step"/> from
    /// <see cref="Minimum"/>. Setting it from code does not raise <see cref="OnChanged"/>.</summary>
    public double Value
    {
        get => _value;
        set
        {
            var v = Clamp(value);
            if (_value == v) return;
            _value = v;
            _typed = null;
            MarkPaintDirty();
            NotifyAccessibilityPropertyChanged(AccessibilityProperties.Value);
        }
    }

    public double Minimum { get => _min; set { _min = value; Value = _value; MarkLayoutDirty(); } }
    public double Maximum { get => _max; set { _max = value; Value = _value; MarkLayoutDirty(); } }
    /// <summary>How far one press moves, and the grid values snap to. Zero disables snapping.</summary>
    public double Step { get => _step; set { _step = value; Value = _value; } }
    /// <summary>How far PageUp/PageDown move; null is ten steps.</summary>
    public double? LargeStep { get; set; }

    /// <summary>How the value is written; null shows as many decimals as the step has.</summary>
    public Func<double, string>? Formatter { get; set; }

    /// <summary>Runs when the user changes the value — buttons, keys, wheel or typing.
    /// Never raised by assigning <see cref="Value"/>.</summary>
    public Action<double>? OnChanged { get; set; }

    /// <summary>Fluent registration of <see cref="OnChanged"/>.</summary>
    public NumericUpDownElement Changed(Action<double> handler) { OnChanged = handler; return this; }

    // Typed characters only arrive while the platform's text input is armed.
    internal override bool WantsTextInput => true;

    /// <summary>What the field shows: the text being typed, or the formatted value.</summary>
    public string DisplayText => _typed ?? FormatValue(_value);

    /// <summary>Whether a typed edit is pending (not yet committed by Enter or blur).</summary>
    public bool IsEditing => _typed is not null;

    private string FormatValue(double v)
    {
        if (Formatter is not null) return Formatter(v);
        int decimals = 0;
        for (double s = _step; s > 0 && decimals < 6 && Math.Abs(s - Math.Round(s)) > 1e-9; s *= 10) decimals++;
        return v.ToString("F" + decimals, CultureInfo.InvariantCulture);
    }

    private double Clamp(double v)
    {
        if (double.IsNaN(v)) v = _min;
        v = Math.Clamp(v, _min, _max);
        if (_step > 0 && !double.IsInfinity(_min))
            v = Math.Clamp(_min + Math.Round((v - _min) / _step) * _step, _min, _max);
        return v;
    }

    // A user gesture both sets the value and reports it.
    private void ApplyUserValue(double v)
    {
        double before = _value;
        Value = v;
        if (_value != before) OnChanged?.Invoke(_value);
    }

    private void StepBy(double delta) => ApplyUserValue((_typed is not null ? ParseTyped() ?? _value : _value) + delta);

    private double? ParseTyped() =>
        _typed is not null && double.TryParse(_typed, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : null;

    /// <summary>Applies the typed text; an unparseable edit is dropped and the value stands.</summary>
    private void Commit()
    {
        if (_typed is null) return;
        var parsed = ParseTyped();
        _typed = null;
        if (parsed is { } v) ApplyUserValue(v);
        MarkPaintDirty();
    }

    private void Revert()
    {
        _typed = null;
        MarkPaintDirty();
    }

    // ── Input ────────────────────────────────────────────────────────────

    private int ButtonAt(Point local)
    {
        var content = Layout.AbsoluteContentBox;
        if (local.X < content.Width - _theme.ButtonWidth) return 0;
        return local.Y < content.Height / 2 ? 1 : -1;
    }

    private void OnPointerDown(PointerDownEvent evt)
    {
        if (evt.Button != 0) return;
        Focus();
        int button = ButtonAt(evt.LocalPosition);
        if (button != 0)
        {
            Commit();
            _held = button;
            StepBy(button * _step);
            StartRepeat();
        }
        MarkPaintDirty();
        evt.StopPropagation = true;
    }

    private void Release()
    {
        if (_held == 0) return;
        _held = 0;
        _repeat.Cancel();
        MarkPaintDirty();
    }

    // A held button repeats on a tween that plays the clock: after the initial delay its
    // value is the seconds held, and the steps due are counted off it — so a frame that
    // arrives late (or a headless test that pumps a whole second at once) fires every
    // step it covers rather than one. Virtual-time driven, like everything animated.
    private const float LongestHoldSeconds = 3600f;

    private void StartRepeat()
    {
        _repeat.Cancel();
        if (_theme.RepeatIntervalSeconds <= 0f) return;
        _repeatDone = 0;
        _repeat = Animate.Value(0f, LongestHoldSeconds, seconds =>
            {
                if (_held == 0 || !IsHosted) { _repeat.Cancel(); return; }
                int due = (int)(seconds / _theme.RepeatIntervalSeconds);
                while (_repeatDone < due)
                {
                    _repeatDone++;
                    StepBy(_held * _step);
                }
            })
            .Delay(_theme.RepeatDelaySeconds)
            .Duration(LongestHoldSeconds)
            .Ease(Easing.Linear)
            .Start();
    }

    /// <summary>Steps on the vertical wheel. Not while a page is being scrolled past it (see
    /// <see cref="ScrollEvent.IsFor"/>), and not on a sideways swipe, which has no DeltaY and
    /// used to read as "down".</summary>
    private void OnScroll(ScrollEvent evt)
    {
        if (!evt.IsFor(this) || evt.RemainingDeltaY == 0) return;
        StepBy(evt.RemainingDeltaY > 0 ? _step : -_step);
        evt.Consume(this, movedX: false, movedY: true);
    }

    private void OnKeyDown(KeyDownEvent evt)
    {
        if (!_focused) return;
        double large = LargeStep ?? _step * 10;
        switch (evt.Key)
        {
            case KeyCode.Up: StepBy(_step); break;
            case KeyCode.Down: StepBy(-_step); break;
            case KeyCode.PageUp: StepBy(large); break;
            case KeyCode.PageDown: StepBy(-large); break;
            case KeyCode.Home when !double.IsInfinity(_min): ApplyUserValue(_min); break;
            case KeyCode.End when !double.IsInfinity(_max): ApplyUserValue(_max); break;
            case KeyCode.Enter: Commit(); break;
            case KeyCode.Escape when _typed is not null: Revert(); break;
            case KeyCode.Backspace:
                _typed = (_typed ?? FormatValue(_value)) is { Length: > 0 } t ? t[..^1] : "";
                MarkPaintDirty();
                break;
            default: return;
        }
        evt.StopPropagation = true;
    }

    private void OnTextInput(TextInputEvent evt)
    {
        if (!_focused) return;
        foreach (var ch in evt.Text)
        {
            if (!(char.IsDigit(ch) || ch is '-' or '.' or ',')) continue;
            // The first keystroke replaces the shown value rather than appending to it,
            // which is what a field that was not visibly in edit mode should do.
            _typed = (_typed ?? "") + (ch == ',' ? '.' : ch);
        }
        MarkPaintDirty();
        evt.StopPropagation = true;
    }

    // ── Theme / measure / render ─────────────────────────────────────────

    /// <summary>
    /// This field's own look for the box it borrows from the dropdown, laid over the theme's —
    /// see <see cref="DropdownStyle"/>. Compared by reference, so hand the same instance each build.
    /// </summary>
    public DropdownStyle? Style
    {
        get => _style;
        set
        {
            if (ReferenceEquals(_style, value)) return;
            _style = value;
            MarkStyleDirty();
        }
    }
    private DropdownStyle? _style;

    protected internal override void ApplyTheme(Theme theme)
    {
        _boxTheme = theme.Dropdown.Resolve(_style);
        _theme = theme.NumericUpDown;
        _chevronColor = _theme.ChevronColor ?? theme.Secondary;
        _pressedBackground = _theme.PressedBackground ?? theme.SurfaceVariant;
        _focusRingColor = theme.FocusRing;
        MarkLayoutDirty();
    }

    internal override Size MeasureContent(float availableWidth, float availableHeight, IMeasureContext ctx)
    {
        var style = ResolvedInherited with { FontSize = _boxTheme.FontSize };
        var digit = ctx.MeasureText("0", style, float.MaxValue, 1);
        // Room for the widest value the range allows, so the field does not resize as
        // the number grows; an unbounded range gets eight digits.
        float widest = Math.Max(
            WidthOf(_min, ctx, style), WidthOf(_max, ctx, style));
        if (double.IsInfinity(_min) || double.IsInfinity(_max)) widest = Math.Max(widest, digit.Width * 8);
        _textWidth = Math.Max(widest, ctx.MeasureText(DisplayText, style, float.MaxValue, 1).Width);
        return new Size(
            _boxTheme.Padding.Left + _textWidth + _boxTheme.ChevronGap + _theme.ButtonWidth,
            _boxTheme.Padding.Top + digit.Height + _boxTheme.Padding.Bottom);
    }

    private float WidthOf(double v, IMeasureContext ctx, ResolvedInheritedStyle style) =>
        double.IsInfinity(v) ? 0f : ctx.MeasureText(FormatValue(v), style, float.MaxValue, 1).Width;

    public override void Render(IRenderContext ctx)
    {
        var box = Layout.AbsoluteBorderBox;
        var radius = new CornerRadius(_boxTheme.CornerRadius);
        ctx.DrawRoundedRect(box, radius, _boxTheme.Background,
            _focused ? _focusRingColor : _boxTheme.BorderColor,
            new Thickness(_focused ? _boxTheme.FocusBorderWidth : _boxTheme.BorderWidth));

        var content = Layout.AbsoluteContentBox;
        float buttonsX = content.Right - _theme.ButtonWidth;

        // Value (or the edit in progress), left-aligned in what the buttons leave and
        // centred on the box's height — the measure reserved the theme's padding, but a
        // row that stretches the field gives it more, and the text should sit in the
        // middle of whatever it got rather than at the top.
        var style = ResolvedInherited with { FontSize = _boxTheme.FontSize, TextColor = _boxTheme.TextColor };
        float lineHeight = ctx.MeasureText(DisplayText, style).Height;
        var textRect = new Rect(content.X + _boxTheme.Padding.Left, content.Y + (content.Height - lineHeight) / 2,
            Math.Max(0f, buttonsX - _boxTheme.ChevronGap - content.X - _boxTheme.Padding.Left), lineHeight);
        ctx.DrawText(DisplayText, textRect, style, TextOverflow.Clip, 1);

        // The strip: a hairline off the text, an up half and a down half.
        ctx.DrawLine(new Point(buttonsX, box.Y + 1), new Point(buttonsX, box.Bottom - 1), _boxTheme.BorderColor, 1f);
        float half = content.Height / 2;
        var up = new Rect(buttonsX, content.Y, _theme.ButtonWidth, half);
        var down = new Rect(buttonsX, content.Y + half, _theme.ButtonWidth, half);
        if (_held > 0) ctx.DrawRect(up, _pressedBackground);
        if (_held < 0) ctx.DrawRect(down, _pressedBackground);
        DrawChevron(ctx, up, pointsUp: true);
        DrawChevron(ctx, down, pointsUp: false);
    }

    private void DrawChevron(IRenderContext ctx, Rect area, bool pointsUp)
    {
        float w = MathF.Round(_boxTheme.FontSize * 0.5f);
        float h = w / 2f;
        float cx = area.X + area.Width / 2f;
        float cy = area.Y + area.Height / 2f;
        float tip = pointsUp ? cy - h / 2f : cy + h / 2f;
        float baseY = pointsUp ? cy + h / 2f : cy - h / 2f;
        ctx.DrawLine(new Point(cx - w / 2f, baseY), new Point(cx, tip), _chevronColor, _theme.ChevronStrokeWidth);
        ctx.DrawLine(new Point(cx, tip), new Point(cx + w / 2f, baseY), _chevronColor, _theme.ChevronStrokeWidth);
    }

    // ── IRangeValueElement ──
    double IRangeValueElement.Value => _value;
    double IRangeValueElement.Minimum => _min;
    double IRangeValueElement.Maximum => _max;
    double IRangeValueElement.SmallChange => _step;
    double IRangeValueElement.LargeChange => LargeStep ?? _step * 10;
    bool IRangeValueElement.IsReadOnly => false;
    void IRangeValueElement.SetAccessibleValue(double value) => ApplyUserValue(value);

    protected override void OnUnmounted()
    {
        base.OnUnmounted();
        _held = 0;
        _repeat.Cancel();
    }

    internal override void PatchFrom(Element source)
    {
        base.PatchFrom(source);
        if (source is not NumericUpDownElement n) return;
        _min = n._min;
        _max = n._max;
        _step = n._step;
        LargeStep = n.LargeStep;
        Formatter = n.Formatter;
        Style = n.Style;
        OnChanged = n.OnChanged;
        // A rebuild while the user is typing keeps the edit; the value under it moves.
        var typed = _typed;
        Value = n._value;
        _typed = typed;
    }
}
