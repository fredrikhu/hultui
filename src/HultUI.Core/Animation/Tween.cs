namespace HultUI.Core.Animation;

internal interface ITween
{
    bool IsComplete { get; }
    void Tick(float deltaSeconds);
    void Cancel();

    /// <summary>Whether it has had a step: the frame loop steps a tween's first by the time since
    /// its last step, at most an interval (<see cref="AnimationManager.Tick(float, float)"/>).</summary>
    bool Started { get; }

    /// <summary>Seconds it waits before it moves: a timer's whole life. The frame loop starts
    /// the clock of one that waits at its first step.</summary>
    float Delay { get; }

    /// <summary>Seconds from its last step until a step can next change anything: 0 when the
    /// next one does (it is running, or has not had its first step), what is left of its delay
    /// while it waits that out. Not asked of a complete tween.</summary>
    float NextChangeIn { get; }
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
    private readonly int _cycles;      // <= 0: forever
    private readonly bool _yoyo;

    private float _elapsed;
    private int _cycle;                // how many cycles have finished
    private bool _complete;
    private bool _started;

    public bool IsComplete => _complete;

    bool ITween.Started => _started;

    float ITween.Delay => _delay;

    float ITween.NextChangeIn => _started && _elapsed < _delay ? _delay - _elapsed : 0f;

    internal Tween(
        T from, T to,
        float duration, float delay,
        Func<float, float> easing,
        Func<T, T, float, T> lerp,
        Action<T> setter,
        Action? onComplete,
        Action? markDirty,
        int cycles = 1,
        bool yoyo = false)
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
        _cycles = cycles;
        _yoyo = yoyo;
    }

    public void Tick(float deltaSeconds)
    {
        if (_complete) return;

        _started = true;
        _elapsed += deltaSeconds;

        if (_elapsed < _delay)
            return;

        float activeTime = _elapsed - _delay;

        // Which cycle we are in, and how far through it, derived from the elapsed time
        // rather than accumulated per cycle. That is what keeps a repeat on period: a
        // frame almost never lands on a cycle boundary, and a restart that dropped the
        // overshoot would make every cycle a frame longer than the last — a spinner
        // built that way visibly slows down.
        float t;
        bool last;
        if (_duration > 0f)
        {
            int done = (int)(activeTime / _duration);
            last = _cycles > 0 && done >= _cycles;
            _cycle = last ? _cycles - 1 : done;
            // A finished repeat holds its end value; a running one is a fraction into
            // its current cycle, so a loop never renders the terminal frame at all.
            t = last ? 1f : activeTime / _duration - done;
        }
        else
        {
            // A zero duration is "already at the end" — one cycle per tick.
            t = 1f;
            last = _cycles > 0 && _cycle + 1 >= _cycles;
            if (!last) _cycle++;
        }

        // A yoyo's odd cycles run backwards, so the pair reads as one there-and-back
        // motion rather than a snap to the start every period.
        float progress = _yoyo && (_cycle & 1) == 1 ? 1f - t : t;
        _setter(_lerp(_from, _to, _easing(progress)));
        _markDirty?.Invoke();

        if (!last) return;
        _complete = true;
        _onComplete?.Invoke();
    }

    public void Cancel()
    {
        _complete = true;
    }
}
