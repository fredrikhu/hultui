using HultUI.Core.Accessibility;
using HultUI.Core.Events;
using HultUI.Core.Layout;
using HultUI.Core.Primitives;
using HultUI.Core.Rendering;
using HultUI.Core.Styling;

namespace HultUI.Core.Elements;

/// <summary>
/// A horizontal slider: drag the thumb or click the track to set a value in
/// [<see cref="Minimum"/>, <see cref="Maximum"/>], snapped to <see cref="Step"/>.
/// Arrow keys nudge by a step; Home/End jump to the ends. Exposes the UIA
/// RangeValue pattern to assistive tech.
/// </summary>
public class SliderElement : Element, IRangeValueElement
{
    private double _value;
    private double _min;
    private double _max = 100;
    private double _step = 1;
    private bool _focused;
    private bool _dragging;
    private Color _trackColor = new(90, 90, 100);
    private Color _fillColor = new(66, 135, 245);
    private Color _thumbColor = Color.White;
    private Color _thumbBorderColor = new(66, 135, 245);
    private float _thumbBorderWidth = 2f;
    private Color _focusRingColor = new(66, 135, 245);

    public double Minimum { get => _min; set { _min = value; Value = _value; } }
    public double Maximum { get => _max; set { _max = value; Value = _value; } }
    public double Step { get => _step; set => _step = value; }

    public double Value
    {
        get => _value;
        set
        {
            double clamped = Clamp(value);
            if (clamped == _value) return;
            _value = clamped;
            MarkPaintDirty();
            NotifyAccessibilityPropertyChanged(AccessibilityProperties.Value);
        }
    }

    public Action<double>? OnChanged { get; set; }

    public float TrackHeight { get; set; } = 4f;
    public float ThumbRadius { get; set; } = 8f;

    public SliderElement()
    {
        IsFocusable = true;
        Inherited.Cursor = CursorType.Pointer;
        // The press captures the pointer, so a drag that wanders above or below the
        // track keeps moving the thumb, as sliders do everywhere; only the release ends it.
        OnOwn<PointerDownEvent>(e =>
        {
            if (e.Button != PointerEvent.LeftButton) return;
            e.CapturePointer();
            _dragging = true;
            Focus();
            ApplyUserValue(ValueFromLocalX(e.LocalPosition.X));
        });
        OnOwn<PointerMoveEvent>(e => { if (_dragging) ApplyUserValue(ValueFromLocalX(e.LocalPosition.X)); });
        OnOwn<PointerUpEvent>(_ => _dragging = false);
        OnOwn<FocusEvent>(_ => { _focused = true; MarkPaintDirty(); });
        OnOwn<BlurEvent>(_ => { _focused = false; MarkPaintDirty(); });
        OnOwn<KeyDownEvent>(e =>
        {
            if (!_focused) return;
            switch (e.Key)
            {
                case KeyCode.Left or KeyCode.Down: ApplyUserValue(_value - _step); break;
                case KeyCode.Right or KeyCode.Up: ApplyUserValue(_value + _step); break;
                case KeyCode.Home: ApplyUserValue(_min); break;
                case KeyCode.End: ApplyUserValue(_max); break;
            }
        });
    }

    public static SliderElement New(double value = 0, double min = 0, double max = 100, [OriginFile] string? file = null, [OriginLine] int line = 0) =>
        new SliderElement { _min = min, _max = max, _value = Math.Clamp(value, min, max) }
            .WithOrigin(file, line);

    protected internal override void ApplyTheme(Theme theme)
    {
        var st = theme.Get<SliderTheme>();
        _fillColor = st.FillColor ?? theme.Primary;
        _focusRingColor = theme.FocusRing;
        _trackColor = st.TrackColor ?? theme.Border;
        _thumbColor = st.ThumbColor ?? Color.White;
        _thumbBorderColor = st.ThumbBorderColor ?? _fillColor;
        _thumbBorderWidth = st.ThumbBorderWidth;
    }

    private double Clamp(double v)
    {
        v = Math.Clamp(v, _min, _max);
        if (_step > 0)
            v = Math.Clamp(_min + Math.Round((v - _min) / _step) * _step, _min, _max);
        return v;
    }

    private double ValueFromLocalX(float localX)
    {
        float trackWidth = Layout.AbsoluteContentBox.Width - 2 * ThumbRadius;
        double t = trackWidth > 0 ? Math.Clamp((localX - ThumbRadius) / trackWidth, 0, 1) : 0;
        return _min + t * (_max - _min);
    }

    // A user gesture (drag/keyboard/AT) both sets the value and fires the change.
    private void ApplyUserValue(double v)
    {
        double before = _value;
        Value = v;
        if (_value != before) OnChanged?.Invoke(_value);
    }

    internal override Size MeasureContent(float availableWidth, float availableHeight, IMeasureContext ctx) =>
        new(160f, Math.Max(20f, ThumbRadius * 2));

    public override void Render(IRenderContext ctx)
    {
        var box = Layout.AbsoluteContentBox;
        float cy = box.Y + box.Height / 2;
        float trackLeft = box.X + ThumbRadius;
        float trackWidth = box.Width - 2 * ThumbRadius;
        double range = _max - _min;
        float t = range > 0 ? (float)((_value - _min) / range) : 0f;

        var trackRect = new Rect(trackLeft, cy - TrackHeight / 2, trackWidth, TrackHeight);
        ctx.DrawRoundedRect(trackRect, new CornerRadius(TrackHeight / 2), _trackColor, Color.Transparent, default);

        var fillRect = new Rect(trackLeft, cy - TrackHeight / 2, trackWidth * t, TrackHeight);
        ctx.DrawRoundedRect(fillRect, new CornerRadius(TrackHeight / 2), _fillColor, Color.Transparent, default);

        float thumbX = trackLeft + trackWidth * t;
        var thumbRect = new Rect(thumbX - ThumbRadius, cy - ThumbRadius, ThumbRadius * 2, ThumbRadius * 2);
        ctx.DrawRoundedRect(thumbRect, new CornerRadius(ThumbRadius), _thumbColor, _thumbBorderColor, new Thickness(_thumbBorderWidth));

        if (_focused)
        {
            float o = 3f;
            var ring = new Rect(thumbX - ThumbRadius - o, cy - ThumbRadius - o, (ThumbRadius + o) * 2, (ThumbRadius + o) * 2);
            ctx.DrawRoundedRect(ring, new CornerRadius(ThumbRadius + o), Color.Transparent, _focusRingColor, new Thickness(2f));
        }
    }

    // ── IRangeValueElement ──
    double IRangeValueElement.Value => _value;
    double IRangeValueElement.Minimum => _min;
    double IRangeValueElement.Maximum => _max;
    double IRangeValueElement.SmallChange => _step;
    double IRangeValueElement.LargeChange => _step * 10;
    bool IRangeValueElement.IsReadOnly => false;
    void IRangeValueElement.SetAccessibleValue(double value) => ApplyUserValue(value);

    internal override void PatchFrom(Element source)
    {
        base.PatchFrom(source);
        if (source is SliderElement s)
        {
            _min = s._min;
            _max = s._max;
            _step = s._step;
            TrackHeight = s.TrackHeight;
            ThumbRadius = s.ThumbRadius;
            OnChanged = s.OnChanged;
            Value = s._value;
        }
    }
}
