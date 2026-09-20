using HultUI.Core.Accessibility;
using HultUI.Core.Animation;
using HultUI.Core.Layout;
using HultUI.Core.Primitives;
using HultUI.Core.Rendering;
using HultUI.Core.Styling;

namespace HultUI.Core.Elements;

/// <summary>
/// A busy indicator: a ring of dots with the leading one at full colour and a fading
/// tail, turning for as long as the element is on screen. Drop it in a row beside the
/// label of whatever is waiting.
/// </summary>
/// <remarks>
/// The apps animated a rotated glyph with a phase field on the component and a
/// looping tween serviced after every build. Here the phase belongs to the element, the
/// tween starts from the first paint and stops itself once the element is no longer
/// hanging off a hosted root (see <see cref="Element.IsHosted"/>), so a rebuild has
/// nothing to service and a build that stops asking for the spinner stops the tween.
/// Dots rather than a rotated arc because the render context draws rounded rectangles
/// and nothing rotates them: eight circles need no transform and no arc primitive.
/// </remarks>
public class SpinnerElement : Element, IRangeValueElement
{
    private bool _spinning;
    private float _phase;
    private TweenHandle _tween;
    private float? _requestedSize;

    /// <summary>Diameter of the ring; null takes the theme's size.</summary>
    public float Size { get; set; } = 16f;
    public Color Color { get; set; } = new(79, 70, 229);
    public int DotCount { get; set; } = 8;
    /// <summary>Seconds for one full turn.</summary>
    public float PeriodSeconds { get; set; } = 0.9f;
    /// <summary>Alpha of the dot furthest behind the leading one, 0..1.</summary>
    public float TailAlpha { get; set; } = 0.15f;

    /// <summary>Where the leading dot is, 0..1 of a turn. For tests.</summary>
    public float Phase => _phase;

    public SpinnerElement()
    {
        Role = AccessibilityRole.ProgressBar;
    }

    protected internal override void ApplyTheme(Theme theme)
    {
        var t = theme.Spinner;
        Size = _requestedSize ?? t.Size;
        Color = t.Color ?? theme.Primary;
        DotCount = t.DotCount;
        PeriodSeconds = t.PeriodSeconds;
        TailAlpha = t.TailAlpha;
    }

    public static SpinnerElement New(float? size = null, [OriginFile] string? file = null, [OriginLine] int line = 0)
    {
        var s = new SpinnerElement { _requestedSize = size };
        if (size is { } px) s.Size = px;
        return s.WithOrigin(file, line);
    }

    private void Start()
    {
        if (_spinning || PeriodSeconds <= 0f) return;
        _spinning = true;
        _tween = Animate.Value(0f, 1f, v =>
            {
                if (!IsHosted) { Stop(); return; }
                _phase = v;
                MarkPaintDirty();
            })
            .Duration(PeriodSeconds)
            .Ease(Easing.Linear)
            .Loop()
            .Start();
    }

    private void Stop()
    {
        _tween.Cancel();
        _spinning = false;
        _phase = 0f;
    }

    protected override void OnUnmounted()
    {
        base.OnUnmounted();
        Stop();
    }

    internal override Size MeasureContent(float availableWidth, float availableHeight, IMeasureContext ctx) =>
        new(Size, Size);

    public override void Render(IRenderContext ctx)
    {
        Start();
        var box = Layout.AbsoluteContentBox;
        float cx = box.X + box.Width / 2;
        float cy = box.Y + box.Height / 2;
        int n = Math.Max(3, DotCount);
        // Dot diameter and ring radius from the size: dots touch neither each other nor
        // the edge, at any count that still looks like a ring.
        float dot = Size * 0.22f;
        float radius = (Size - dot) / 2;
        // Which dot leads: the phase turns clockwise, and the tail fades behind it in
        // equal steps down to TailAlpha.
        float lead = _phase * n;
        for (int i = 0; i < n; i++)
        {
            float behind = ((lead - i) % n + n) % n;      // 0 = leading, n-1 = furthest back
            float alpha = 1f - (1f - TailAlpha) * (behind / (n - 1));
            var colour = new Color(Color.R, Color.G, Color.B, (byte)Math.Round(Color.A * alpha));
            double angle = -Math.PI / 2 + i * (2 * Math.PI / n);
            float x = cx + radius * (float)Math.Cos(angle);
            float y = cy + radius * (float)Math.Sin(angle);
            ctx.DrawRoundedRect(new Rect(x - dot / 2, y - dot / 2, dot, dot), new CornerRadius(dot / 2), colour, Color.Transparent, default);
        }
    }

    // ── IRangeValueElement: an indeterminate progress bar reports no value ──
    double IRangeValueElement.Value => 0;
    double IRangeValueElement.Minimum => 0;
    double IRangeValueElement.Maximum => 0;
    double IRangeValueElement.SmallChange => 0;
    double IRangeValueElement.LargeChange => 0;
    bool IRangeValueElement.IsReadOnly => true;
    void IRangeValueElement.SetAccessibleValue(double value) { }

    internal override void PatchFrom(Element source)
    {
        base.PatchFrom(source);
        if (source is SpinnerElement s)
        {
            _requestedSize = s._requestedSize;
            Size = s.Size;
            Color = s.Color;
            DotCount = s.DotCount;
            PeriodSeconds = s.PeriodSeconds;
            TailAlpha = s.TailAlpha;
        }
    }
}
