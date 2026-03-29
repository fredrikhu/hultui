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

    public TweenHandle Start()
    {
        var tween = new Tween<T>(_from, _to, _duration, _delay, _easing, _lerp, _setter, _onComplete, _markDirty);
        AnimationManager.Add(tween);
        return new TweenHandle(tween);
    }
}
