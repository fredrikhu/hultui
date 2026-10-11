using HultUI.Core.Accessibility;
using HultUI.Core.Animation;
using HultUI.Core.Layout;
using HultUI.Core.Primitives;
using HultUI.Core.Rendering;
using HultUI.Core.Styling;

namespace HultUI.Core.Elements;

/// <summary>
/// A horizontal progress bar: a track with a fill that grows from the left to the
/// fraction <see cref="Value"/> is of <see cref="Minimum"/>..<see cref="Maximum"/>.
/// Set <see cref="IsIndeterminate"/> for activity with no known fraction and a segment
/// sweeps the track instead, animated only while the element is on screen.
/// </summary>
/// <remarks>
/// The bar stretches to the width it is offered, so it sits in a column like a
/// divider does; give it a <c>Width</c> to fix it. Labels, percentages and captions
/// are the app's to compose around it — every hand-rolled version put them in a
/// different place, and none of them belonged inside the bar.
/// </remarks>
public class ProgressBarElement : Element, IRangeValueElement
{
    private double _value;
    private double _min;
    private double _max = 1;
    private bool _indeterminate;
    private bool _sweeping;
    private float _sweep;
    private TweenHandle _sweepTween;

    public double Value
    {
        get => _value;
        set
        {
            var v = Math.Clamp(value, _min, _max);
            if (_value == v) return;
            _value = v;
            MarkPaintDirty();
            RangeChanged();
        }
    }

    public double Minimum { get => _min; set { if (_min == value) return; _min = value; Value = _value; RangeChanged(); } }
    public double Maximum { get => _max; set { if (_max == value) return; _max = value; Value = _value; RangeChanged(); } }

    // A reader is told the value on a schedule, not every frame a download or a playback moves
    // it (AccessibilityProperties.RangeValue).
    private void RangeChanged() => NotifyAccessibilityPropertyChanged(AccessibilityProperties.RangeValue);

    /// <summary>Show activity without a known fraction. The fill is replaced by a
    /// segment that sweeps the track for as long as this stays set.</summary>
    public bool IsIndeterminate
    {
        get => _indeterminate;
        set
        {
            if (_indeterminate == value) return;
            _indeterminate = value;
            MarkPaintDirty();
            // Starting is left to the next paint: only an element that actually paints
            // is on screen. Stopping is immediate.
            if (!value) StopSweep();
        }
    }

    /// <summary>The fraction filled, 0..1, whatever the range is.</summary>
    public double Fraction => _max > _min ? (_value - _min) / (_max - _min) : 0;

    // Each visual knob has a theme value and an optional explicit one; the explicit one
    // wins, so a bar given its own fill (two bars in one panel, coloured apart) keeps it
    // through the style pass instead of being repainted the theme's colour.
    private float _themeHeight = 4f;
    private float? _height;
    private float? _themeCornerRadius;
    private float? _cornerRadius;
    private Color _themeTrack = new(226, 232, 240);
    private Color? _track;
    private Color _themeFill = new(79, 70, 229);
    private Color? _fill;

    public float Height { get => _height ?? _themeHeight; set { _height = value; MarkLayoutDirty(); } }
    /// <summary>Corner radius of track and fill; null rounds fully.</summary>
    public float? CornerRadius { get => _cornerRadius ?? _themeCornerRadius; set { _cornerRadius = value; MarkPaintDirty(); } }
    public Color TrackColor { get => _track ?? _themeTrack; set { _track = value; MarkPaintDirty(); } }
    public Color FillColor { get => _fill ?? _themeFill; set { _fill = value; MarkPaintDirty(); } }
    public float IndeterminateSeconds { get; set; } = 1.4f;
    public float IndeterminateFraction { get; set; } = 0.35f;

    /// <summary>Where the indeterminate segment is, 0 (about to enter from the left)
    /// to 1 (has left on the right). For tests; the bar reads it when it paints.</summary>
    public float SweepPosition => _sweep;

    protected internal override void ApplyTheme(Theme theme)
    {
        var t = theme.ProgressBar;
        // The height is all it measures from the theme; a theme that keeps it repaints the bar.
        bool resized = t.Height != _themeHeight;
        _themeHeight = t.Height;
        _themeCornerRadius = t.CornerRadius;
        _themeTrack = t.Track ?? theme.Border;
        _themeFill = t.Fill ?? theme.Primary;
        if (resized) MarkLayoutDirty();
        IndeterminateSeconds = t.IndeterminateSeconds;
        IndeterminateFraction = t.IndeterminateFraction;
    }

    public static ProgressBarElement New(double value = 0, double max = 1, [OriginFile] string? file = null, [OriginLine] int line = 0)
    {
        var bar = new ProgressBarElement { _max = max };
        bar._value = Math.Clamp(value, bar._min, bar._max);
        return bar.WithOrigin(file, line);
    }

    // The sweep is a looping tween, and a looping tween outlives anything that forgets
    // it. So it starts only from Render (a twin built during a rebuild never paints), and
    // every tick checks the bar is still hanging off a hosted root, stopping itself
    // when a rebuild, a removed panel or a swapped root has left it behind.
    private void StartSweep()
    {
        if (_sweeping || IndeterminateSeconds <= 0f) return;
        _sweeping = true;
        _sweepTween = Animate.Value(0f, 1f, v =>
            {
                if (!IsHosted) { StopSweep(); return; }
                _sweep = v;
                MarkPaintDirty();
            })
            .Duration(IndeterminateSeconds)
            .Ease(Easing.Linear)
            .Loop()
            .Start();
    }

    private void StopSweep()
    {
        _sweepTween.Cancel();
        _sweeping = false;
        _sweep = 0f;
    }

    protected override void OnUnmounted()
    {
        base.OnUnmounted();
        StopSweep();
    }

    // Stretch to the width offered; a bar in an unbounded row (a scroll view's content,
    // a measure at infinity) takes a sensible default instead of infinity.
    internal override Size MeasureContent(float availableWidth, float availableHeight, IMeasureContext ctx) =>
        new(IsBounded(availableWidth) ? availableWidth : 160f, Height);

    internal override float MeasureMinContentWidth(IMeasureContext ctx) => 0f;

    private static bool IsBounded(float w) => !float.IsInfinity(w) && !float.IsNaN(w) && w < float.MaxValue / 2;

    /// <summary>Unbounded: a box shorter than the bar still draws the whole bar, centred.</summary>
    public override Rect? PaintBounds(IRenderContext ctx) => null;

    public override void Render(IRenderContext ctx)
    {
        var box = Layout.AbsoluteContentBox;
        float y = box.Y + (box.Height - Height) / 2;
        var track = new Rect(box.X, y, box.Width, Height);
        var radius = new CornerRadius(CornerRadius ?? Height / 2);
        ctx.DrawRoundedRect(track, radius, TrackColor, Color.Transparent, default);

        if (_indeterminate)
        {
            StartSweep();
            float segment = box.Width * IndeterminateFraction;
            // Enters from beyond the left edge and leaves beyond the right, so the
            // sweep never appears or vanishes mid-track.
            float x = box.X - segment + (box.Width + segment) * _sweep;
            ctx.PushRoundedClip(track, radius);
            ctx.DrawRoundedRect(new Rect(x, y, segment, Height), radius, FillColor, Color.Transparent, default);
            ctx.Pop();
            return;
        }

        float width = box.Width * (float)Fraction;
        if (width > 0f)
            ctx.DrawRoundedRect(new Rect(box.X, y, width, Height), radius, FillColor, Color.Transparent, default);
    }

    // ── IRangeValueElement: read-only, the bridge refuses SetValue ──
    double IRangeValueElement.Value => _value;
    double IRangeValueElement.Minimum => _min;
    double IRangeValueElement.Maximum => _max;
    double IRangeValueElement.SmallChange => 0;
    double IRangeValueElement.LargeChange => 0;
    bool IRangeValueElement.IsReadOnly => true;
    void IRangeValueElement.SetAccessibleValue(double value) { }

    internal override void PatchFrom(Element source)
    {
        base.PatchFrom(source);
        if (source is ProgressBarElement p)
        {
            _height = p._height;
            _cornerRadius = p._cornerRadius;
            _track = p._track;
            _fill = p._fill;
            IndeterminateSeconds = p.IndeterminateSeconds;
            IndeterminateFraction = p.IndeterminateFraction;
            if (_min != p._min || _max != p._max) RangeChanged();
            _min = p._min;
            _max = p._max;
            Value = p._value;
            IsIndeterminate = p._indeterminate;
        }
    }
}
