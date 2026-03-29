namespace HultUI.Core.Animation;

internal interface ITween
{
    bool IsComplete { get; }
    void Tick(float deltaSeconds);
    void Cancel();
}

public sealed class Tween<T> : ITween
{
    private readonly T _from;
    private readonly T _to;
    private readonly float _duration;
    private readonly float _delay;
    private readonly Func<float, float> _easing;
    private readonly Func<T, T, float, T> _lerp;
    private readonly Action<T> _setter;
    private readonly Action? _onComplete;
    private readonly Action? _markDirty;

    private float _elapsed;
    private bool _complete;

    public bool IsComplete => _complete;

    internal Tween(
        T from, T to,
        float duration, float delay,
        Func<float, float> easing,
        Func<T, T, float, T> lerp,
        Action<T> setter,
        Action? onComplete,
        Action? markDirty)
    {
        _from = from;
        _to = to;
        _duration = duration;
        _delay = delay;
        _easing = easing;
        _lerp = lerp;
        _setter = setter;
        _onComplete = onComplete;
        _markDirty = markDirty;
    }

    public void Tick(float deltaSeconds)
    {
        if (_complete) return;

        _elapsed += deltaSeconds;

        if (_elapsed < _delay)
            return;

        float activeTime = _elapsed - _delay;
        float t = Math.Clamp(activeTime / _duration, 0f, 1f);
        float easedT = _easing(t);
        T value = _lerp(_from, _to, easedT);
        _setter(value);
        _markDirty?.Invoke();

        if (t >= 1f)
        {
            _complete = true;
            _onComplete?.Invoke();
        }
    }

    public void Cancel()
    {
        _complete = true;
    }
}
