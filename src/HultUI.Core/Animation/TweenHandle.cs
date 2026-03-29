namespace HultUI.Core.Animation;

public readonly struct TweenHandle
{
    private readonly ITween? _tween;

    internal TweenHandle(ITween tween) => _tween = tween;

    public bool IsComplete => _tween is null || _tween.IsComplete;

    public void Cancel() => _tween?.Cancel();
}
