using HultUI.Core.Accessibility;
using HultUI.Core.Events;
using HultUI.Core.Layout;
using HultUI.Core.Primitives;
using HultUI.Core.Rendering;
using HultUI.Core.Styling;

namespace HultUI.Core.Elements;

/// <summary>
/// A scrollbar over a logical range of up to <see cref="long.MaxValue"/> units, standing on its own
/// beside whatever it scrolls: a document too large to lay out, drawn a screenful at a time on a
/// <see cref="CanvasElement"/>, whose position is a byte offset or a row index rather than a pixel
/// offset. It looks and behaves like <see cref="ScrollViewElement"/>'s bars.
///
/// <para>The range is GTK's adjustment: a document <see cref="ContentLength"/>, a
/// <see cref="ViewportLength"/> of it on screen, and a <see cref="Value"/> in
/// [0, <see cref="MaxValue"/>]. Every fraction is worked out in <c>double</c>, so a thumb is placed
/// to well under a pixel for any range and a drag to the end reads back <see cref="MaxValue"/>
/// exactly. The value is controlled, as a slider's is: a build sets it, and a user gesture (drag,
/// track click, wheel, keys, assistive technology) sets it and calls <see cref="OnChanged"/>.</para>
///
/// <para>Not focusable by default, like a platform scrollbar: the document it scrolls owns the
/// keyboard. Made focusable, it takes the arrows of its axis, Page Up/Down and Home/End.</para>
/// </summary>
public class ScrollbarElement : Element, IRangeValueElement
{
    // The looks, the hover animation and the paint, shared with ScrollViewElement's bars.
    private readonly Scrollbar _bar = new();

    private ScrollbarAxis _axis;
    private long _content;
    private long _viewport;
    private long _value;
    private long _smallChange = 1;
    private long _largeChange;
    private bool _focused;
    private bool _dragging;

    /// <summary>How far into the thumb the press that started the drag was, or half the thumb
    /// for a track click.</summary>
    private float _grab;

    /// <summary>The pointer's latest place along the track, applied once per frame.</summary>
    private float _pointerAlong;

    private PointerFollow? _follow;

    /// <summary>What a wheel moved by that did not make a whole unit yet.</summary>
    private double _wheelRemainder;

    public ScrollbarAxis Axis
    {
        get => _axis;
        set
        {
            if (_axis == value) return;
            _axis = value;
            MarkLayoutDirty();
        }
    }

    /// <summary>The whole document, in the app's unit. Negative is 0.</summary>
    public long ContentLength
    {
        get => _content;
        set
        {
            value = Math.Max(0, value);
            if (_content == value) return;
            _content = value;
            Value = _value;
            MarkPaintDirty();
            RangeChanged();
        }
    }

    /// <summary>How much of the document shows at once. Negative is 0.</summary>
    public long ViewportLength
    {
        get => _viewport;
        set
        {
            value = Math.Max(0, value);
            if (_viewport == value) return;
            _viewport = value;
            Value = _value;
            MarkPaintDirty();
            RangeChanged();
        }
    }

    /// <summary>The largest <see cref="Value"/>: the start of the last screenful.</summary>
    public long MaxValue => Math.Max(0, _content - _viewport);

    /// <summary>The first unit shown, clamped to [0, <see cref="MaxValue"/>].</summary>
    public long Value
    {
        get => _value;
        set
        {
            long clamped = Math.Clamp(value, 0, MaxValue);
            if (clamped == _value) return;
            _value = clamped;
            MarkPaintDirty();
            RangeChanged();
        }
    }

    /// <summary>What an arrow key moves by; a wheel notch moves three. At least 1.</summary>
    public long SmallChange
    {
        get => _smallChange;
        set
        {
            value = Math.Max(1, value);
            if (_smallChange == value) return;
            _smallChange = value;
            RangeChanged();
        }
    }

    /// <summary>What Page Up and Page Down move by. 0, the default, is
    /// <see cref="ViewportLength"/>.</summary>
    public long LargeChange
    {
        get => _largeChange;
        set
        {
            value = Math.Max(0, value);
            if (_largeChange == value) return;
            _largeChange = value;
            RangeChanged();
        }
    }

    private long EffectiveLargeChange => _largeChange > 0 ? _largeChange : Math.Max(1, _viewport);

    /// <summary>Called with the new value when a user gesture moves it, never when a build
    /// sets it.</summary>
    public Action<long>? OnChanged { get; set; }

    /// <summary>Whether the thumb is being dragged. A patch then keeps the dragged value.</summary>
    public bool IsDragging => _dragging;

    // ──── Appearance ────
    //
    // The same defaults and meanings as ScrollViewElement's Scrollbar* facades. Not named Width:
    // the fluent .Width(...) extension would no longer bind.

    public Color TrackColor
    {
        get => _bar.TrackColor;
        set { _bar.TrackColor = value; MarkPaintDirty(); }
    }

    public Color ThumbColor
    {
        get => _bar.ThumbColor;
        set { _bar.ThumbColor = value; MarkPaintDirty(); }
    }

    /// <summary>Track colour while expanded. Null keeps <see cref="TrackColor"/>.</summary>
    public Color? TrackHoverColor
    {
        get => _bar.TrackHoverColor;
        set { _bar.TrackHoverColor = value; MarkPaintDirty(); }
    }

    /// <summary>The drawn width at rest.</summary>
    public float BarWidth
    {
        get => _bar.Width;
        set { _bar.Width = value; MarkLayoutDirty(); }
    }

    /// <summary>Corner radius of the track and thumb. Negative, the default, is half the drawn
    /// width: a pill.</summary>
    public float BarRadius
    {
        get => _bar.Radius;
        set { _bar.Radius = value; MarkPaintDirty(); }
    }

    /// <summary>Whether the bar widens under the pointer.</summary>
    public bool ExpandOnHover
    {
        get => _bar.ExpandOnHover;
        set { _bar.ExpandOnHover = value; MarkLayoutDirty(); }
    }

    /// <summary>The drawn width while the pointer is on it; the thumb grows to half of it. The
    /// element is this wide (or tall) at rest too, so there is something to reach for.</summary>
    public float ExpandedBarWidth
    {
        get => _bar.ExpandedWidth;
        set { _bar.ExpandedWidth = value; MarkLayoutDirty(); }
    }

    /// <summary>Seconds the widening takes.</summary>
    public float ExpandDuration
    {
        get => _bar.ExpandDuration;
        set => _bar.ExpandDuration = value;
    }

    public ScrollbarElement()
    {
        Inherited.Cursor = CursorType.Default;
        OnOwn<PointerEnterEvent>(_ => _bar.SetHover(true, this));
        OnOwn<PointerLeaveEvent>(_ => { if (!_dragging) _bar.SetHover(false, this); });
        OnOwn<PointerDownEvent>(OnPointerDown);
        OnOwn<PointerMoveEvent>(e =>
        {
            // A second finger reaches the capture holder too (docs/design/POINTER-IDENTITY.md §2):
            // the thumb follows the first.
            if (!_dragging || !e.IsPrimary) return;
            // Recorded, and applied once per frame that may be drawn: OnChanged rebuilds the
            // viewer it scrolls, and a 1,000 Hz mouse rebuilt it about sixteen times a frame.
            _pointerAlong = Along(e.LocalPosition);
            (_follow ??= new PointerFollow(FollowPointer)).Request(this);
        });
        // A press the system took ends as a release does: the thumb stays where it was dragged.
        OnOwn<PointerCancelEvent>(e =>
        {
            if (!e.IsPrimary) return;
            _follow?.Flush();
            _dragging = false;
            _bar.SetHover(false, this);
        });
        OnOwn<PointerUpEvent>(e =>
        {
            if (!e.IsPrimary) return;
            // The drag ends exactly where the pointer did, whether or not a frame came between.
            _follow?.Flush();
            _dragging = false;
            var box = Layout.AbsoluteContentBox;
            var local = e.LocalPosition;
            if (!Layout.AbsoluteBorderBox.Contains(new Point(box.X + local.X, box.Y + local.Y)))
                _bar.SetHover(false, this);
        });
        OnOwn<ScrollEvent>(OnWheel);
        OnOwn<FocusEvent>(_ => _focused = true);
        OnOwn<BlurEvent>(_ => _focused = false);
        OnOwn<KeyDownEvent>(OnKey);
    }

    public static ScrollbarElement New(ScrollbarAxis axis = ScrollbarAxis.Vertical,
        [OriginFile] string? file = null, [OriginLine] int line = 0) =>
        new ScrollbarElement { _axis = axis }.WithOrigin(file, line);

    // A reader is told the value on a schedule, not every frame of a drag
    // (AccessibilityProperties.RangeValue).
    private void RangeChanged() => NotifyAccessibilityPropertyChanged(AccessibilityProperties.RangeValue);

    // ──── Geometry ────

    private bool Vertical => _axis == ScrollbarAxis.Vertical;

    private float Track(in Rect box) => Vertical ? box.Height : box.Width;

    private float TrackOrigin(in Rect box) => Vertical ? box.Y : box.X;

    /// <summary>The pointer's place along the track, in window coordinates.</summary>
    private float Along(Point local)
    {
        var box = Layout.AbsoluteContentBox;
        return Vertical ? box.Y + local.Y : box.X + local.X;
    }

    private float ThumbLength(in Rect box)
    {
        float track = Track(box);
        double visible = _content <= 0 ? 1.0 : Math.Min(1.0, (double)_viewport / _content);
        return Math.Min(track, Math.Max(Scrollbar.MinThumbLength, (float)(track * visible)));
    }

    private float ThumbStart(in Rect box, float thumbLength)
    {
        long max = MaxValue;
        double ratio = max == 0 ? 0.0 : (double)_value / max;
        return TrackOrigin(box) + (float)(ratio * (Track(box) - thumbLength));
    }

    /// <summary>The value that puts the thumb, held <see cref="_grab"/> into it, at
    /// <paramref name="along"/>. <see cref="MaxValue"/> exactly at the track's end, and never past
    /// it however large the range.</summary>
    private long ValueAt(float along)
    {
        var box = Layout.AbsoluteContentBox;
        long max = MaxValue;
        float travel = Track(box) - ThumbLength(box);
        if (max == 0) return 0;
        // A track shorter than the shortest thumb is all thumb: no place on it means a value.
        if (travel <= 0) return _value;

        double r = Math.Clamp((along - _grab - TrackOrigin(box)) / (double)travel, 0, 1);
        double p = Math.Round(r * max);
        return p >= max ? max : (long)p;
    }

    // ──── Input ────

    private void OnPointerDown(PointerDownEvent e)
    {
        if (e.Button != PointerEvent.LeftButton || !e.IsPrimary || MaxValue == 0) return;
        // What the moves before the press were to show comes first.
        _follow?.Flush();

        var box = Layout.AbsoluteContentBox;
        float thumbLength = ThumbLength(box);
        float thumbStart = ThumbStart(box, thumbLength);
        float along = Along(e.LocalPosition);
        if (along >= thumbStart && along <= thumbStart + thumbLength)
        {
            _grab = along - thumbStart;
            e.CapturePointer();
            _dragging = true;
        }
        else
        {
            // A jump that centres the thumb under the pointer, and no drag: as a scroll view's
            // track click does.
            _grab = thumbLength / 2f;
            ApplyUserValue(ValueAt(along));
        }
        e.StopPropagation = true;
    }

    private void FollowPointer()
    {
        // Against the lengths as they are now: a document that grows during the drag keeps the
        // thumb under the pointer.
        if (_dragging) ApplyUserValue(ValueAt(_pointerAlong));
    }

    private void OnWheel(ScrollEvent evt)
    {
        if (!evt.IsFor(this)) return;

        bool sideways = !Vertical && evt.RemainingDeltaX != 0;
        float delta = sideways ? evt.RemainingDeltaX : evt.RemainingDeltaY;
        if (delta == 0) return;

        // A wheel turned up (positive) moves back towards the start. Three units a notch, and
        // what a smooth wheel's fractions add up to is kept until it makes a whole unit.
        _wheelRemainder += -delta * 3.0 * _smallChange;
        double whole = Math.Truncate(_wheelRemainder);
        _wheelRemainder -= whole;
        long step = whole >= long.MaxValue ? long.MaxValue : whole <= long.MinValue ? long.MinValue : (long)whole;

        long before = _value;
        if (step != 0) ApplyUserValue(SaturatingAdd(_value, step));
        bool moved = _value != before;
        evt.Consume(this, sideways && moved, !sideways && moved);
    }

    private void OnKey(KeyDownEvent e)
    {
        if (!_focused) return;
        _follow?.Flush();

        long? target = e.Key switch
        {
            KeyCode.Up when Vertical => SaturatingAdd(_value, -_smallChange),
            KeyCode.Down when Vertical => SaturatingAdd(_value, _smallChange),
            KeyCode.Left when !Vertical => SaturatingAdd(_value, -_smallChange),
            KeyCode.Right when !Vertical => SaturatingAdd(_value, _smallChange),
            KeyCode.PageUp => SaturatingAdd(_value, -EffectiveLargeChange),
            KeyCode.PageDown => SaturatingAdd(_value, EffectiveLargeChange),
            KeyCode.Home => 0,
            KeyCode.End => MaxValue,
            _ => null,
        };
        if (target is not { } v) return;

        ApplyUserValue(v);
        e.StopPropagation = true;
    }

    private static long SaturatingAdd(long a, long b)
    {
        long sum = unchecked(a + b);
        // Overflow happened when both operands share a sign the sum does not.
        if (((a ^ sum) & (b ^ sum)) < 0) return a < 0 ? long.MinValue : long.MaxValue;
        return sum;
    }

    // A user gesture both sets the value and reports it.
    private void ApplyUserValue(long v)
    {
        long before = _value;
        Value = v;
        if (_value != before) OnChanged?.Invoke(_value);
    }

    // ──── Layout and paint ────

    internal override Size MeasureContent(float availableWidth, float availableHeight, IMeasureContext ctx) =>
        Vertical ? new(_bar.HitWidth, 0f) : new(0f, _bar.HitWidth);

    public override Rect? PaintBounds(IRenderContext ctx) =>
        _bar.PaintsInside(Layout.AbsoluteContentBox, Vertical) ? Layout.AbsoluteBorderBox : null;

    public override void Render(IRenderContext ctx)
    {
        // Nothing to scroll, no bar: as a scroll view hides an idle one.
        if (MaxValue == 0) return;

        var box = Layout.AbsoluteContentBox;
        float thumbLength = ThumbLength(box);
        _bar.PaintAt(ctx, box, ThumbStart(box, thumbLength), thumbLength, Vertical);
    }

    // ──── IRangeValueElement ────

    double IRangeValueElement.Value => _value;
    double IRangeValueElement.Minimum => 0;
    double IRangeValueElement.Maximum => MaxValue;
    double IRangeValueElement.SmallChange => _smallChange;
    double IRangeValueElement.LargeChange => EffectiveLargeChange;
    bool IRangeValueElement.IsReadOnly => false;

    void IRangeValueElement.SetAccessibleValue(double value)
    {
        if (double.IsNaN(value)) return;
        _follow?.Flush();
        long max = MaxValue;
        double r = Math.Round(Math.Clamp(value, 0, max));
        ApplyUserValue(r >= max ? max : (long)r);
    }

    // ──── Patch ────

    // Its measure reads the axis and the bar's hit width and nothing else it copies; the patch
    // marks the layout when those change.
    private protected override bool PatchMarksOwnLayout => GetType() == typeof(ScrollbarElement);

    internal override void PatchFrom(Element source)
    {
        base.PatchFrom(source);
        if (source is ScrollbarElement s)
        {
            if (_axis != s._axis || _bar.HitWidth != s._bar.HitWidth) MarkLayoutDirty();
            // The looks only: hover and drag belong to the live element and the pointer on it.
            _bar.CopyFrom(s._bar);
            _axis = s._axis;
            // The lengths before the value, so it is not clamped against the old range.
            ContentLength = s._content;
            ViewportLength = s._viewport;
            SmallChange = s._smallChange;
            LargeChange = s._largeChange;
            OnChanged = s.OnChanged;
            // A rebuild with an older value does not throw a dragged thumb back.
            if (!_dragging) Value = s._value;
        }
    }
}
