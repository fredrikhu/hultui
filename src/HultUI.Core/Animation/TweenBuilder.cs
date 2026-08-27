namespace HultUI.Core.Animation;

public sealed class TweenBuilder<T>
{
    private readonly T _from;
    private readonly T _to;
    private readonly Func<T, T, float, T> _lerp;
    private readonly Action<T> _setter;

    private float _duration = 0.3f;
    private float _delay;
    private Func<float, float> _easing = Easing.EaseOutCubic;
    private Action? _onComplete;
    private Action? _markDirty;
    private int _cycles = 1;
    private bool _yoyo;

    internal TweenBuilder(T from, T to, Func<T, T, float, T> lerp, Action<T> setter)
    {
        _from = from;
        _to = to;
        _lerp = lerp;
        _setter = setter;
    }

    public TweenBuilder<T> Duration(float seconds) { _duration = seconds; return this; }
    public TweenBuilder<T> Delay(float seconds) { _delay = seconds; return this; }
    public TweenBuilder<T> Ease(Func<float, float> easing) { _easing = easing; return this; }
    public TweenBuilder<T> OnComplete(Action callback) { _onComplete = callback; return this; }
    internal TweenBuilder<T> MarkDirty(Action markDirty) { _markDirty = markDirty; return this; }

    /// <summary>
    /// Runs the tween <paramref name="times"/> times over. Zero or less means forever —
    /// the shape of every busy indicator, which has to run until the work it stands for
    /// finishes rather than for a duration known up front. Cancel the returned
    /// <see cref="TweenHandle"/> to stop one.
    /// </summary>
    /// <remarks>The delay applies once, before the first cycle, and each later cycle
    /// carries over whatever the frame overshot the end by — so a repeat holds its
    /// period instead of drifting a frame longer every time round.</remarks>
    public TweenBuilder<T> Repeat(int times) { _cycles = times; return this; }

    /// <summary>Repeats forever. Shorthand for <c>Repeat(0)</c>.</summary>
    public TweenBuilder<T> Loop() { _cycles = 0; return this; }

    /// <summary>Makes every second cycle run backwards, so a repeat eases to and fro
    /// instead of snapping back to the start. Only meaningful with a repeat.</summary>
    public TweenBuilder<T> Yoyo(bool yoyo = true) { _yoyo = yoyo; return this; }

    public TweenHandle Start()
    {
        var tween = new Tween<T>(_from, _to, _duration, _delay, _easing, _lerp, _setter, _onComplete, _markDirty,
            _cycles, _yoyo);
        AnimationManager.Add(tween);
        return new TweenHandle(tween);
    }
}
