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
    private Color _hoverFillColor = new(66, 135, 245, 120);

    public double Minimum { get => _min; set { if (_min == value) return; _min = value; Value = _value; RangeChanged(); } }
    public double Maximum { get => _max; set { if (_max == value) return; _max = value; Value = _value; RangeChanged(); } }
    public double Step { get => _step; set { if (_step == value) return; _step = value; RangeChanged(); } }

    public double Value
    {
        get => _value;
        set
        {
            double clamped = Clamp(value);
            if (clamped == _value) return;
            _value = clamped;
            MarkPaintDirty();
            RangeChanged();
        }
    }

    // A reader is told the value on a schedule, not every frame of a drag or a playback
    // (AccessibilityProperties.RangeValue).
    private void RangeChanged() => NotifyAccessibilityPropertyChanged(AccessibilityProperties.RangeValue);

    public Action<double>? OnChanged { get; set; }

    /// <summary>
    /// The value under the pointer as it moves across the track, and null when it leaves.
    /// Hovering is not changing: a video scrubber previews the frame you are pointing at
    /// before you commit to seeking there, and a volume slider could show the level the
    /// same way.
    /// </summary>
    public Action<double?>? OnHoverValue { get; set; }

    /// <summary>
    /// Values inside the range where the track is cut into pieces, with
    /// <see cref="SegmentGap"/> between them — a video scrubber divided by its chapter
    /// marks. Null or empty is one continuous track, which is what a slider has always
    /// been. Values outside the range, and duplicates, are ignored.
    /// </summary>
    public IReadOnlyList<double>? Segments
    {
        get => _segments;
        set { _segments = value; MarkPaintDirty(); }
    }
    private IReadOnlyList<double>? _segments;

    /// <summary>The gap drawn between segments.</summary>
    public float SegmentGap { get; set; } = 2f;

    /// <summary>
    /// Whether the track fills to the pointer as it crosses, in a paler colour than the
    /// played part — how a video scrubber says "release here and this is where you land".
    /// Off by default: on an ordinary slider it would read as a second value.
    /// </summary>
    public bool ShowHoverFill { get; set; }

    /// <summary>Where the pointer is, while it is over the track.</summary>
    private double? _hoverValue;

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
            // What the moves before the press were to show comes first.
            _follow?.Flush();
            e.CapturePointer();
            _dragging = true;
            Focus();
            ApplyUserValue(ValueFromLocalX(e.LocalPosition.X));
        });
        OnOwn<PointerMoveEvent>(e =>
        {
            // Recorded, and applied once per frame that may be drawn (FollowPointer): a drag's
            // OnChanged and the hover's OnHoverValue rebuild their owner, a video player for the
            // scrubber's preview, and a 1,000 Hz mouse rebuilt it about sixteen times a frame
            // drawn. A hover nothing shows or hears needs no frame at all.
            _pointerAt = ValueFromLocalX(e.LocalPosition.X);
            if (_dragging || OnHoverValue is not null || ShowHoverFill)
                (_follow ??= new PointerFollow(FollowPointer)).Request(this);
            else
                SetHover(_pointerAt);
        });
        OnOwn<PointerLeaveEvent>(_ =>
        {
            // A drag's value still lands; a hover the next frame was to show goes with the pointer.
            if (_dragging) _follow?.Flush();
            else _follow?.Cancel();
            SetHover(null);
            OnHoverValue?.Invoke(null);
        });
        OnOwn<PointerUpEvent>(_ =>
        {
            // The drag ends exactly where the pointer did, whether or not a frame came between.
            _follow?.Flush();
            _dragging = false;
        });
        OnOwn<FocusEvent>(_ => { _focused = true; MarkPaintDirty(); });
        OnOwn<BlurEvent>(_ => { _focused = false; MarkPaintDirty(); });
        OnOwn<KeyDownEvent>(e =>
        {
            if (!_focused) return;
            _follow?.Flush();
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
        // Paler than the played part by default, so "where you would land" never reads as
        // "where you are".
        _hoverFillColor = st.HoverFillColor ?? new Color(_fillColor.R, _fillColor.G, _fillColor.B, 120);
    }

    /// <summary>The pointer's latest place on the track, as a value, applied once per frame.</summary>
    private double _pointerAt;
    private PointerFollow? _follow;

    private void FollowPointer()
    {
        double at = _pointerAt;
        if (_dragging) ApplyUserValue(at);
        SetHover(at);
        // Reported while dragging too: the thumb is under the pointer either way, and
        // a preview that vanished the moment you pressed would be the wrong way round.
        OnHoverValue?.Invoke(at);
    }

    private void SetHover(double? at)
    {
        if (_hoverValue == at) return;
        _hoverValue = at;
        if (ShowHoverFill) MarkPaintDirty();
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

    /// <summary>Unbounded: the thumb and its focus ring reach past the ends of the track.</summary>
    public override Rect? PaintBounds(IRenderContext ctx) => null;

    public override void Render(IRenderContext ctx)
    {
        var box = Layout.AbsoluteContentBox;
        float cy = box.Y + box.Height / 2;
        float trackLeft = box.X + ThumbRadius;
        float trackWidth = box.Width - 2 * ThumbRadius;
        double range = _max - _min;
        float t = range > 0 ? (float)((_value - _min) / range) : 0f;

        float hoverT = ShowHoverFill && _hoverValue is { } hover && range > 0
            ? (float)Math.Clamp((hover - _min) / range, 0, 1)
            : 0f;

        // One piece per segment, so a chapter is a thing you can see and aim at. Each is
        // drawn as its own little track, filled by however much of it is behind the thumb
        // (and, while pointed at, by however much is behind the pointer).
        float top = cy - TrackHeight / 2;
        var radius = new CornerRadius(TrackHeight / 2);
        if (range <= 0 || trackWidth <= 0 || _segments is not { Count: > 0 } segments)
        {
            DrawPiece(0f, 1f);
        }
        else
        {
            // The track as fractions of its width: one piece per segment with the gap taken out
            // of each piece's right-hand end. Worked out here every frame, as it always was, but
            // in a span: a LINQ chain and an iterator per frame were most of what a steady
            // repaint allocated on a screen with sliders, and a video's scrubber repaints with
            // every frame it plays.
            Span<float> cuts = segments.Count <= 256 ? stackalloc float[segments.Count] : new float[segments.Count];
            int count = 0;
            for (int i = 0; i < segments.Count; i++)
            {
                double v = segments[i];
                if (v > _min && v < _max) cuts[count++] = (float)((v - _min) / range);
            }
            cuts = cuts[..count];
            cuts.Sort();

            float gap = Math.Min(SegmentGap / trackWidth, 0.02f);
            float start = 0f;
            for (int i = 0; i < cuts.Length; i++)
            {
                float cut = cuts[i];
                if (i > 0 && cut.Equals(cuts[i - 1])) continue;   // duplicates are one cut
                if (cut - start > gap) DrawPiece(start, cut - gap);
                start = cut;
            }
            if (start < 1f) DrawPiece(start, 1f);
        }

        void DrawPiece(float from, float to)
        {
            float x = trackLeft + trackWidth * from;
            float width = trackWidth * (to - from);
            if (width <= 0) return;

            ctx.DrawRoundedRect(new Rect(x, top, width, TrackHeight), radius, _trackColor, Color.Transparent, default);

            float hoverPart = Math.Clamp((hoverT - from) / (to - from), 0f, 1f);
            if (hoverPart > 0)
                ctx.DrawRoundedRect(new Rect(x, top, width * hoverPart, TrackHeight), radius,
                    _hoverFillColor, Color.Transparent, default);

            float playedPart = Math.Clamp((t - from) / (to - from), 0f, 1f);
            if (playedPart > 0)
                ctx.DrawRoundedRect(new Rect(x, top, width * playedPart, TrackHeight), radius,
                    _fillColor, Color.Transparent, default);
        }

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
    void IRangeValueElement.SetAccessibleValue(double value)
    {
        _follow?.Flush();
        ApplyUserValue(value);
    }

    // Its measure reads ThumbRadius and nothing else it copies; the patch marks the layout
    // when that changes.
    private protected override bool PatchMarksOwnLayout => GetType() == typeof(SliderElement);

    internal override void PatchFrom(Element source)
    {
        base.PatchFrom(source);
        if (source is SliderElement s)
        {
            bool range = _min != s._min || _max != s._max || _step != s._step;
            _min = s._min;
            _max = s._max;
            _step = s._step;
            if (range) RangeChanged();
            TrackHeight = s.TrackHeight;
            if (ThumbRadius != s.ThumbRadius)
            {
                ThumbRadius = s.ThumbRadius;
                MarkLayoutDirty();
            }
            OnChanged = s.OnChanged;
            OnHoverValue = s.OnHoverValue;
            Segments = s.Segments;
            SegmentGap = s.SegmentGap;
            ShowHoverFill = s.ShowHoverFill;
            Value = s._value;
        }
    }
}
