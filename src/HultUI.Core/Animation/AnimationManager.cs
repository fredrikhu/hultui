namespace HultUI.Core.Animation;

public static class AnimationManager
{
    private static readonly List<ITween> _active = [];
    private static readonly List<ITween> _pendingAdd = [];
    private static bool _ticking;

    public static bool HasActiveAnimations => _active.Count > 0 || _pendingAdd.Count > 0;

    internal static void Add(ITween tween)
    {
        if (_ticking)
            _pendingAdd.Add(tween);
        else
            _active.Add(tween);
    }

    internal static void Remove(ITween tween)
    {
        tween.Cancel();
    }

    public static void Tick(float deltaSeconds)
    {
        if (_active.Count == 0 && _pendingAdd.Count == 0)
            return;

        if (_pendingAdd.Count > 0)
        {
            _active.AddRange(_pendingAdd);
            _pendingAdd.Clear();
        }

        _ticking = true;
        for (int i = 0; i < _active.Count; i++)
            _active[i].Tick(deltaSeconds);
        _ticking = false;

        for (int i = _active.Count - 1; i >= 0; i--)
        {
            if (_active[i].IsComplete)
                _active.RemoveAt(i);
        }

        if (_pendingAdd.Count > 0)
        {
            _active.AddRange(_pendingAdd);
            _pendingAdd.Clear();
        }
    }
}
